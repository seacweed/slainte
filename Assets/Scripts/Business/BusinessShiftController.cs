using System;
using System.Collections.Generic;
using Slainte.TV;
using UnityEngine;

namespace Slainte.Business
{
    public static class BusinessShiftClock
    {
        public static void Advance(
            ref float remainingSeconds,
            ref float activeBusinessSeconds,
            float deltaSeconds,
            bool paused)
        {
            if (paused || remainingSeconds <= 0f)
                return;

            float delta = Mathf.Max(0f, deltaSeconds);
            if (delta <= 0f)
                return;

            float consumed = Mathf.Min(remainingSeconds, delta);
            remainingSeconds = Mathf.Max(0f, remainingSeconds - consumed);
            activeBusinessSeconds += consumed;
        }
    }

    public enum BusinessShiftState
    {
        Idle,
        Running,
        WaitingForCustomer,
        OrderActive,
        EncounterActive,
        CompletingRequiredActions,
        Completed
    }

    public sealed class BusinessShiftController : MonoBehaviour
    {
        private readonly Dictionary<string, float> cooldownUntilByVisit =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> invalidVisitKeys =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> invalidEncounterIds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> startedEncounterIds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> executedTargetKeys =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> executedRuleIds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CustomerVisitData> frozenCustomerPool = new();
        private readonly List<BusinessRandomEncounterEntry> frozenEncounterPool = new();

        private BusinessOrderSessionController orderSession;
        private BusinessOrderSessionUI sessionUi;
        private GameModeManager modeManager;
        private BusinessOrderFlowSettings settings;
        private IBusinessSalePayoutPolicy salePayoutPolicy;
        private System.Random random;
        private bool initialized;
        private bool shiftActive;
        private bool orderActive;
        private bool encounterActive;
        private bool explicitlyPaused;
        private bool initialRequiredPhaseComplete;
        private bool beginOrderCallInProgress;
        private int orderSequence;
        private int completedOrderCount;
        private float remainingSeconds;
        private float activeBusinessSeconds;

        public BusinessShiftState State { get; private set; } = BusinessShiftState.Idle;
        public bool IsActive => shiftActive;
        public bool IsTimerExpired => shiftActive && remainingSeconds <= 0f;
        public float RemainingSeconds => remainingSeconds;
        public float ActiveBusinessSeconds => activeBusinessSeconds;
        public int FrozenCustomerPoolCount => frozenCustomerPool.Count;
        public int FrozenEncounterPoolCount => frozenEncounterPool.Count;
        public int CompletedOrderCount => completedOrderCount;
        public int TotalStartedCustomerCount { get; private set; }
        public int TotalStartedEncounterCount { get; private set; }
        public int CooldownFallbackSelectionCount { get; private set; }
        public string LastSelectedVisitKey { get; private set; } = string.Empty;
        public int CoolingDownCustomerCount
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<string, float> pair in cooldownUntilByVisit)
                {
                    if (pair.Value > activeBusinessSeconds)
                        count++;
                }

                return count;
            }
        }

        public event Action<BusinessShiftState, BusinessShiftState> StateChanged;
        public event Action ShiftCompleted;
        public event Action<CustomerVisitData, bool> CustomerVisitStarted;

        public void Initialize(
            BusinessOrderSessionController sessionController,
            BusinessOrderSessionUI businessSessionUi,
            GameModeManager gameModeManager,
            BusinessOrderFlowSettings flowSettings)
        {
            if (initialized)
                return;

            orderSession = sessionController;
            sessionUi = businessSessionUi;
            modeManager = gameModeManager;
            settings = flowSettings;
            salePayoutPolicy = new DeferredSettlementSalePayoutPolicy();
            initialized = orderSession != null && settings != null;

            if (!initialized)
                Debug.LogError("[BusinessShift] 영업 컨트롤러 초기화에 필요한 참조가 없습니다.");
        }

        public bool BeginShift()
        {
            if (!initialized || shiftActive)
                return false;

            ResetRuntimeState();
            GameProgress progress = GameProgress.Instance;
            CustomerVisitDatabase database = settings.customerVisitDatabase
                ?? CustomerVisitDatabase.LoadDefault();

            frozenCustomerPool.AddRange(
                BusinessSequencePlanner.BuildEligibleVisitPool(database, progress));
            if (!BusinessSequencePlanner.HasEligibleTargetForActiveTVEffect(
                    frozenCustomerPool,
                    progress))
            {
                TVBroadcastEntry active = TVBroadcastRuntime.GetActiveBroadcast(
                    progress,
                    TVBroadcastDatabase.LoadDefault());
                Debug.LogWarning(
                    $"[TV] 오늘 조건을 만족하는 방송 대상이 없어 효과만 생략합니다: {active?.id}");
            }
            frozenEncounterPool.AddRange(
                BusinessSequencePlanner.BuildEligibleRandomEncounterPool(
                    settings.randomEncounters,
                    progress,
                    BuildReservedEncounterTargetKeys(progress)));

            int day = progress != null ? progress.CurrentDay : 0;
            random = new System.Random(unchecked(Environment.TickCount ^ day * 397 ^ GetInstanceID()));
            remainingSeconds = Mathf.Max(1f, settings.shiftDurationSeconds);
            shiftActive = true;
            SetState(BusinessShiftState.Running);
            modeManager?.RequestModeChange(GameMode.OrderMode);
            sessionUi?.SetShiftTime(remainingSeconds, false);

            ValidateRequiredRules();

            if (frozenCustomerPool.Count == 0 && frozenEncounterPool.Count == 0)
            {
                Debug.LogError(
                    "[BusinessShift] 영업 시작 시 조건을 만족하는 일반 손님과 랜덤 인카운터가 없습니다. "
                    + "필수 액션만 처리한 뒤 정산으로 이동합니다.");
                remainingSeconds = 0f;
            }

            AdvanceAtSafePoint();
            return true;
        }

        public void SetPaused(bool paused)
        {
            explicitlyPaused = paused;
        }

        public bool TrySetSalePayoutPolicy(IBusinessSalePayoutPolicy policy)
        {
            if (shiftActive || policy == null)
                return false;

            salePayoutPolicy = policy;
            return true;
        }

        private void Update()
        {
            if (!shiftActive)
                return;

            TickBusinessClock();
            bool paused = IsBusinessClockPaused();
            sessionUi?.SetShiftTime(remainingSeconds, paused);

            if (!orderActive && !encounterActive && !paused)
                AdvanceAtSafePoint();
        }

        private void TickBusinessClock()
        {
            BusinessShiftClock.Advance(
                ref remainingSeconds,
                ref activeBusinessSeconds,
                Time.deltaTime,
                IsBusinessClockPaused());

            // Expiring the clock stops new random visits, but it must not replace
            // the state of an order or encounter that is still in progress.
            if (remainingSeconds <= 0f && !orderActive && !encounterActive)
                SetState(BusinessShiftState.CompletingRequiredActions);
        }

        private void AdvanceAtSafePoint()
        {
            GameProgress progress = GameProgress.Instance;
            if (progress == null)
            {
                Debug.LogError("[BusinessShift] GameProgress가 없어 영업을 종료합니다.");
                CompleteShift();
                return;
            }

            bool timerExpired = remainingSeconds <= 0f;
            if (timerExpired)
            {
                SetState(BusinessShiftState.CompletingRequiredActions);
                BusinessRequiredActionRule remainingRequired = PickRequiredAction(
                    progress,
                    BusinessRequiredActionTiming.AfterTimer,
                    includeAllTimings: true);
                if (remainingRequired != null)
                {
                    ExecuteRequiredAction(remainingRequired);
                    return;
                }

                CompleteShift();
                return;
            }

            if (!initialRequiredPhaseComplete)
            {
                BusinessRequiredActionRule initialRequired = PickRequiredAction(
                    progress,
                    BusinessRequiredActionTiming.BeforeFirstCustomer,
                    includeAllTimings: false);
                if (initialRequired != null)
                {
                    ExecuteRequiredAction(initialRequired);
                    return;
                }

                initialRequiredPhaseComplete = true;
            }

            if (completedOrderCount > 0)
            {
                BusinessRequiredActionRule betweenOrdersRequired = PickRequiredAction(
                    progress,
                    BusinessRequiredActionTiming.BetweenOrders,
                    includeAllTimings: false);
                if (betweenOrdersRequired != null)
                {
                    ExecuteRequiredAction(betweenOrdersRequired);
                    return;
                }
            }

            BusinessSequenceSelection selection = BusinessSequencePlanner.PickWeightedSequence(
                frozenCustomerPool,
                frozenEncounterPool,
                startedEncounterIds,
                progress,
                cooldownUntilByVisit,
                invalidVisitKeys,
                invalidEncounterIds,
                activeBusinessSeconds,
                random);
            if (selection != null)
            {
                if (selection.IsEncounter)
                {
                    StartBusinessEncounter(selection.Encounter, isRandomSelection: true);
                    return;
                }

                StartCustomerOrder(
                    selection.Visit,
                    selection.OrderOption,
                    selection.UsedCooldownFallback);
                return;
            }

            SetState(BusinessShiftState.WaitingForCustomer);
            sessionUi?.ShowWaitingForCustomer();
        }

        private BusinessRequiredActionRule PickRequiredAction(
            GameProgress progress,
            BusinessRequiredActionTiming timing,
            bool includeAllTimings)
        {
            return BusinessSequencePlanner.PickNextRequiredAction(
                settings.requiredActions,
                progress,
                timing,
                includeAllTimings,
                executedRuleIds,
                executedTargetKeys);
        }

        private void ExecuteRequiredAction(BusinessRequiredActionRule rule)
        {
            if (!string.IsNullOrWhiteSpace(rule.ruleId))
                executedRuleIds.Add(rule.ruleId);
            if (!string.IsNullOrWhiteSpace(rule.TargetKey))
                executedTargetKeys.Add(rule.TargetKey);

            if (rule.actionType == BusinessRequiredActionType.CustomerVisit)
            {
                CustomerVisitOrderOption order = BusinessSequencePlanner.PickWeightedOrder(
                    rule.customerVisit,
                    GameProgress.Instance,
                    random);
                if (order == null)
                {
                    Debug.LogError(
                        $"[BusinessShift] 필수 손님 규칙 '{rule.ruleId}'에 실행 가능한 주문이 없습니다.");
                    return;
                }

                StartCustomerOrder(rule.customerVisit, order);
                return;
            }

            StartBusinessEncounter(rule.encounterEpisode, rule.ruleId, isRandomSelection: false);
        }

        private void StartCustomerOrder(
            CustomerVisitData visit,
            CustomerVisitOrderOption orderOption,
            bool usedCooldownFallback = false)
        {
            CustomerOrderData order = orderOption?.order;
            if (visit == null || order == null)
                return;

            OrderSessionRequest request = new()
            {
                sessionId = $"business_{GameProgress.Instance?.CurrentDay ?? 0}_{++orderSequence}",
                owner = OrderSessionOwner.Business,
                customerOrderKey = order.key,
                customerVisitKey = visit.visitKey,
                requestedRecipeId = order.requestedRecipeId,
                ticketKey = order.key,
                orderType = order.orderType,
                presentOrder = true,
                presentFeedback = true,
                applyProgressRewards = false,
                clearCustomerOnComplete = true
            };

            orderActive = true;
            SetState(BusinessShiftState.OrderActive);
            beginOrderCallInProgress = true;
            bool started = orderSession.BeginOrder(
                request,
                result => HandleCustomerOrderCompleted(visit, result));
            beginOrderCallInProgress = false;
            if (!started)
            {
                orderActive = false;
                invalidVisitKeys.Add(visit.visitKey);
                Debug.LogError(
                    $"[BusinessShift] 손님 주문을 시작하지 못했습니다: {visit.visitKey}/{order.key}");
                SetState(BusinessShiftState.Running);
                return;
            }

            if (orderActive)
            {
                TotalStartedCustomerCount++;
                LastSelectedVisitKey = visit.visitKey;
                if (usedCooldownFallback)
                    CooldownFallbackSelectionCount++;
                CustomerVisitStarted?.Invoke(visit, usedCooldownFallback);
                RecordCustomerAppearance(visit);
            }
        }

        private void HandleCustomerOrderCompleted(
            CustomerVisitData visit,
            BusinessOrderSessionResult result)
        {
            orderActive = false;
            completedOrderCount++;

            bool completedSuccessfully = result != null
                && result.outcome == OrderSessionOutcome.Served
                && result.accepted;

            if (!completedSuccessfully)
            {
                if (visit != null && !string.IsNullOrWhiteSpace(visit.visitKey))
                    invalidVisitKeys.Add(visit.visitKey);
                Debug.LogError(
                    $"[BusinessShift] 주문 처리에 실패하여 손님을 오늘의 풀에서 제외합니다: {visit?.visitKey}");
            }
            else if (visit != null && !string.IsNullOrWhiteSpace(visit.visitKey))
            {
                cooldownUntilByVisit[visit.GetCooldownKey()] =
                    activeBusinessSeconds + Mathf.Max(0f, visit.cooldownSeconds);
            }

            if (completedSuccessfully)
                RecordSale(result);

            SetState(remainingSeconds <= 0f
                ? BusinessShiftState.CompletingRequiredActions
                : BusinessShiftState.Running);
            if (!beginOrderCallInProgress && !IsBusinessClockPaused())
                AdvanceAtSafePoint();
        }

        private void RecordSale(BusinessOrderSessionResult result)
        {
            GameProgress progress = GameProgress.Instance;
            if (progress == null)
                return;

            salePayoutPolicy?.Apply(result, progress);
        }

        private void StartBusinessEncounter(
            BusinessRandomEncounterEntry entry,
            bool isRandomSelection)
        {
            StartBusinessEncounter(
                entry?.episode,
                entry?.TargetKey,
                isRandomSelection);
        }

        private void StartBusinessEncounter(
            EpisodeData episode,
            string sourceKey,
            bool isRandomSelection)
        {
            string episodeId = episode?.episodeId;
            GameProgress progress = GameProgress.Instance;
            if (episode == null
                || string.IsNullOrWhiteSpace(episodeId)
                || episode.episodeType != EpisodeType.Encounter
                || (progress != null && progress.IsEpisodeCompleted(episodeId))
                || startedEncounterIds.Contains(episodeId))
            {
                if (isRandomSelection && !string.IsNullOrWhiteSpace(episodeId))
                    invalidEncounterIds.Add(episodeId);
                Debug.LogError(
                    $"[BusinessShift] 인카운터를 시작할 수 없는 상태입니다: {sourceKey}/{episodeId}");
                return;
            }

            encounterActive = true;
            SetState(BusinessShiftState.EncounterActive);

            bool started = EpisodeManager.Instance != null
                && EpisodeManager.Instance.TryStartBusinessEncounter(
                    episodeId,
                    HandleBusinessEncounterCompleted);
            if (started)
            {
                startedEncounterIds.Add(episodeId);
                executedTargetKeys.Add("episode:" + episodeId);
                TotalStartedEncounterCount++;
                return;
            }

            encounterActive = false;
            if (isRandomSelection)
                invalidEncounterIds.Add(episodeId);
            Debug.LogError(
                $"[BusinessShift] 인카운터를 시작하지 못했습니다: {sourceKey}/{episodeId}");
            SetState(remainingSeconds <= 0f
                ? BusinessShiftState.CompletingRequiredActions
                : BusinessShiftState.Running);
        }

        private void HandleBusinessEncounterCompleted()
        {
            encounterActive = false;
            modeManager?.RequestModeChange(GameMode.OrderMode);
            SetState(remainingSeconds <= 0f
                ? BusinessShiftState.CompletingRequiredActions
                : BusinessShiftState.Running);
            if (!IsBusinessClockPaused())
                AdvanceAtSafePoint();
        }

        private void RecordCustomerAppearance(CustomerVisitData visit)
        {
            GameProgress progress = GameProgress.Instance;
            if (progress == null || visit?.members == null)
                return;

            for (int i = 0; i < visit.members.Count; i++)
            {
                CustomerVisitMember member = visit.members[i];
                if (member != null && !string.IsNullOrWhiteSpace(member.characterKey))
                    progress.IncrementCustomerAppearance(member.characterKey);
            }
        }

        private void CompleteShift()
        {
            if (!shiftActive)
                return;

            remainingSeconds = 0f;
            shiftActive = false;
            SetState(BusinessShiftState.Completed);
            sessionUi?.SetShiftTime(0f, false);
            sessionUi?.ShowDayComplete();
            ShiftCompleted?.Invoke();
        }

        private void ResetRuntimeState()
        {
            cooldownUntilByVisit.Clear();
            invalidVisitKeys.Clear();
            invalidEncounterIds.Clear();
            startedEncounterIds.Clear();
            executedTargetKeys.Clear();
            executedRuleIds.Clear();
            frozenCustomerPool.Clear();
            frozenEncounterPool.Clear();
            orderActive = false;
            encounterActive = false;
            explicitlyPaused = false;
            initialRequiredPhaseComplete = false;
            beginOrderCallInProgress = false;
            orderSequence = 0;
            completedOrderCount = 0;
            TotalStartedCustomerCount = 0;
            TotalStartedEncounterCount = 0;
            CooldownFallbackSelectionCount = 0;
            LastSelectedVisitKey = string.Empty;
            remainingSeconds = 0f;
            activeBusinessSeconds = 0f;
            SetState(BusinessShiftState.Idle);
        }

        private void ValidateRequiredRules()
        {
            if (settings.requiredActions == null)
                return;

            HashSet<string> ruleIds = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < settings.requiredActions.Count; i++)
            {
                BusinessRequiredActionRule rule = settings.requiredActions[i];
                if (rule == null)
                {
                    Debug.LogError($"[BusinessShift] 필수 액션 {i}번이 비어 있습니다.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.ruleId))
                    Debug.LogError($"[BusinessShift] 필수 액션 {i}번의 ruleId가 비어 있습니다.");
                else if (!ruleIds.Add(rule.ruleId))
                    Debug.LogError($"[BusinessShift] 필수 액션 ruleId가 중복됩니다: {rule.ruleId}");

                if (string.IsNullOrWhiteSpace(rule.TargetKey))
                    Debug.LogError($"[BusinessShift] 필수 액션 '{rule.ruleId}'의 대상이 비어 있습니다.");
            }
        }

        private HashSet<string> BuildReservedEncounterTargetKeys(GameProgress progress)
        {
            HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
            if (settings.requiredActions == null || progress == null)
                return result;

            for (int i = 0; i < settings.requiredActions.Count; i++)
            {
                BusinessRequiredActionRule rule = settings.requiredActions[i];
                if (rule == null
                    || rule.actionType != BusinessRequiredActionType.EncounterEpisode
                    || (rule.exactDay > 0 && rule.exactDay != progress.CurrentDay)
                    || !ProgressConditionEvaluator.IsMet(rule.condition, progress)
                    || rule.encounterEpisode == null
                    || progress.IsEpisodeCompleted(rule.encounterEpisode.episodeId)
                    || string.IsNullOrWhiteSpace(rule.TargetKey))
                    continue;

                result.Add(rule.TargetKey);
            }

            return result;
        }

        private bool IsBusinessClockPaused()
        {
            // Orders, crafting and business encounters all consume shift time.
            // Only an explicit/global pause is allowed to stop the business clock.
            return explicitlyPaused || Time.timeScale <= 0f;
        }

        private void SetState(BusinessShiftState next)
        {
            if (State == next)
                return;

            BusinessShiftState previous = State;
            State = next;
            StateChanged?.Invoke(previous, next);
        }
    }
}
