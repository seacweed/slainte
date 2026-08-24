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
        private const int RecentCustomerLimit = 2;

        private readonly Queue<string> recentCustomerKeys = new();
        private readonly HashSet<string> recentCustomerKeySet =
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
        private bool randomCustomerSpawningStopped;
        private bool forceCompletionRequested;
        private int orderSequence;
        private int completedOrderCount;
        private int startedSequenceCount;
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
        public int StartedSequenceCount => startedSequenceCount;
        public int TotalStartedCustomerCount { get; private set; }
        public int TotalStartedEncounterCount { get; private set; }
        public bool IsRandomCustomerSpawningStopped => randomCustomerSpawningStopped;
        public bool IsForceCompletionPending => forceCompletionRequested;
        public string LastSelectedVisitKey { get; private set; } = string.Empty;

        public event Action<BusinessShiftState, BusinessShiftState> StateChanged;
        public event Action ShiftCompleted;
        public event Action<CustomerVisitData> CustomerVisitStarted;

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
            salePayoutPolicy = new ImmediateSalePayoutPolicy();
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
            ExcludeVisitsWithInvalidOrderData();
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

        public bool TryForceCompleteShift()
        {
            if (!shiftActive)
                return false;

            forceCompletionRequested = true;
            explicitlyPaused = false;
            randomCustomerSpawningStopped = true;
            remainingSeconds = 0f;
            sessionUi?.SetShiftTime(0f, false);

            if (orderActive || encounterActive)
            {
                Debug.LogWarning(
                    "[BusinessShift] 영업 강제 완료가 예약되었습니다. 현재 주문 또는 인카운터 종료 후 정산합니다.");
                return true;
            }

            CompleteShift();
            return true;
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

            if (!orderActive
                && !encounterActive
                && !paused
                && (!randomCustomerSpawningStopped || remainingSeconds <= 0f))
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
            if (forceCompletionRequested)
            {
                CompleteShift();
                return;
            }

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

            BusinessRequiredActionRule sequenceRequired = PickRequiredAction(
                progress,
                BusinessRequiredActionTiming.SequenceSlot,
                includeAllTimings: false,
                sequenceSlot: startedSequenceCount + 1);
            if (sequenceRequired != null)
            {
                ExecuteRequiredAction(sequenceRequired);
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
                recentCustomerKeySet,
                invalidVisitKeys,
                invalidEncounterIds,
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
                    selection.OrderOption);
                return;
            }

            StopRandomCustomerSpawning();
        }

        private BusinessRequiredActionRule PickRequiredAction(
            GameProgress progress,
            BusinessRequiredActionTiming timing,
            bool includeAllTimings,
            int sequenceSlot = 0)
        {
            return BusinessSequencePlanner.PickNextRequiredAction(
                settings.requiredActions,
                progress,
                timing,
                includeAllTimings,
                executedRuleIds,
                executedTargetKeys,
                sequenceSlot);
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
            CustomerVisitOrderOption orderOption)
        {
            CustomerOrderData order = orderOption?.order;
            if (visit == null || order == null)
                return;

            if (!orderSession.ValidateCustomerOrderData(order, out string validationError))
            {
                invalidVisitKeys.Add(visit.visitKey);
                Debug.LogError(
                    $"[BusinessShift] 유효하지 않은 손님 주문을 시작하지 않습니다: "
                    + $"visit={visit.visitKey}, order={order.key}, "
                    + $"recipe={order.requestedRecipeId}, reason={validationError}");
                return;
            }

            OrderSessionRequest request = new()
            {
                sessionId = $"business_{GameProgress.Instance?.CurrentDay ?? 0}_{++orderSequence}",
                owner = OrderSessionOwner.Business,
                customerOrderKey = order.key,
                customerVisitKey = visit.visitKey,
                requestedRecipeId = order.requestedRecipeId,
                requestedConditionLabel = order.tags != null && order.tags.Count > 0
                    ? order.tags[0]
                    : string.Empty,
                requestedTags = order.tags != null
                    ? new List<string>(order.tags)
                    : new List<string>(),
                ticketKey = order.key,
                orderType = order.orderType,
                paymentCurrency = BusinessCustomerRules.ResolvePaymentCurrency(
                    visit,
                    order.paymentCurrency),
                rewardProfile = BusinessCustomerRules.ResolveRewardProfile(visit),
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

            startedSequenceCount++;

            if (orderActive)
            {
                TotalStartedCustomerCount++;
                LastSelectedVisitKey = visit.visitKey;
                RecordRecentCustomer(visit);
                CustomerVisitStarted?.Invoke(visit);
                RecordCustomerAppearance(visit);
            }
        }

        private void HandleCustomerOrderCompleted(
            CustomerVisitData visit,
            BusinessOrderSessionResult result)
        {
            orderActive = false;

            bool completedSuccessfully = result != null
                && result.outcome == OrderSessionOutcome.Served
                && result.accepted;

            if (completedSuccessfully)
            {
                completedOrderCount++;
                RecordSale(result);
            }
            else
            {
                bool excludeVisit = result != null && !result.technicalFailure;
                if (excludeVisit
                    && visit != null
                    && !string.IsNullOrWhiteSpace(visit.visitKey))
                {
                    invalidVisitKeys.Add(visit.visitKey);
                }

                string message =
                    (excludeVisit
                        ? "[BusinessShift] 주문 미완료로 손님을 오늘의 풀에서 제외합니다: "
                        : "[BusinessShift] 주문 처리 실패 후 손님을 오늘의 풀에 유지합니다: ")
                    + $"visit={visit?.visitKey}, order={result?.customerOrderKey}, "
                    + $"recipe={result?.requestedRecipeId}, "
                    + $"reason={result?.failureReason ?? "결과가 없거나 주문이 거절됐습니다."}";
                if (result?.technicalFailure == true)
                    Debug.LogError(message);
                else
                    Debug.LogWarning(message);
            }

            if (forceCompletionRequested)
            {
                CompleteShift();
                return;
            }

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

        private void ExcludeVisitsWithInvalidOrderData()
        {
            if (orderSession == null || frozenCustomerPool.Count == 0)
                return;

            List<string> failures = new();
            for (int visitIndex = 0; visitIndex < frozenCustomerPool.Count; visitIndex++)
            {
                CustomerVisitData visit = frozenCustomerPool[visitIndex];
                if (visit?.orders == null)
                    continue;

                for (int orderIndex = 0; orderIndex < visit.orders.Count; orderIndex++)
                {
                    CustomerOrderData order = visit.orders[orderIndex]?.order;
                    if (orderSession.ValidateCustomerOrderData(order, out string reason))
                        continue;

                    if (!string.IsNullOrWhiteSpace(visit.visitKey))
                        invalidVisitKeys.Add(visit.visitKey);
                    failures.Add(
                        $"- visit={visit.visitKey}, order={order?.key}, "
                        + $"recipe={order?.requestedRecipeId}, reason={reason}");
                    break;
                }
            }

            if (failures.Count > 0)
            {
                Debug.LogError(
                    $"[BusinessShift] 주문 데이터가 유효하지 않은 방문 {failures.Count}개를 "
                    + "오늘의 풀에서 제외합니다.\n"
                    + string.Join("\n", failures));
            }
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
                startedSequenceCount++;
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
            if (forceCompletionRequested)
            {
                CompleteShift();
                return;
            }

            SetState(remainingSeconds <= 0f
                ? BusinessShiftState.CompletingRequiredActions
                : BusinessShiftState.Running);
            if (!IsBusinessClockPaused())
                AdvanceAtSafePoint();
        }

        private void RecordRecentCustomer(CustomerVisitData visit)
        {
            string key = visit?.GetReappearanceKey();
            if (string.IsNullOrWhiteSpace(key))
                return;

            recentCustomerKeys.Enqueue(key);
            recentCustomerKeySet.Add(key);
            while (recentCustomerKeys.Count > RecentCustomerLimit)
            {
                string removed = recentCustomerKeys.Dequeue();
                if (!recentCustomerKeys.Contains(removed))
                    recentCustomerKeySet.Remove(removed);
            }
        }

        private void StopRandomCustomerSpawning()
        {
            if (randomCustomerSpawningStopped)
                return;

            randomCustomerSpawningStopped = true;
            SetState(BusinessShiftState.WaitingForCustomer);
            sessionUi?.ShowWaitingForCustomer();
            Debug.LogWarning(
                "[BusinessShift] 최근 손님 2명 제한과 현재 등장 조건을 만족하는 다음 대상이 없습니다. "
                + "이번 영업의 랜덤 손님 생성을 중단하고 남은 시간은 계속 진행합니다. "
                + $"recent=[{string.Join(", ", recentCustomerKeys)}]");
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
            forceCompletionRequested = false;
            SetState(BusinessShiftState.Completed);
            sessionUi?.SetShiftTime(0f, false);
            sessionUi?.ShowDayComplete();
            ShiftCompleted?.Invoke();
        }

        private void ResetRuntimeState()
        {
            recentCustomerKeys.Clear();
            recentCustomerKeySet.Clear();
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
            randomCustomerSpawningStopped = false;
            forceCompletionRequested = false;
            orderSequence = 0;
            completedOrderCount = 0;
            startedSequenceCount = 0;
            TotalStartedCustomerCount = 0;
            TotalStartedEncounterCount = 0;
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

                if (rule.timing == BusinessRequiredActionTiming.SequenceSlot
                    && rule.sequenceSlot <= 0)
                {
                    Debug.LogError(
                        $"[BusinessShift] 고정 슬롯 필수 액션 '{rule.ruleId}'의 슬롯이 올바르지 않습니다: "
                        + rule.sequenceSlot);
                }
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
                    || !ProgressConditionEvaluator.IsMet(
                        rule.encounterEpisode.triggerCondition,
                        progress)
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
