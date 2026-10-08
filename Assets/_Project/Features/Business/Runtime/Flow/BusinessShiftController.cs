using System;
using System.Collections.Generic;
using Slainte.TV;
using UnityEngine;

namespace Slainte.Business
{
    public enum BusinessShiftState
    {
        Idle,
        Running,
        OrderActive,
        EncounterActive,
        Completed
    }

    // 하루 영업(Shift) 한 번의 상태 머신. 손님 슬롯 1..N(설정의 customersPerDay)을 순서대로 하나씩
    // 처리한다. 각 슬롯은 일정(IDayScheduleSource)에 배정된 에피소드 중 등장 조건을 만족하는 하나로,
    // 없으면 가중치 랜덤 손님으로 채운다. 주문·인카운터가 끝나면 다음 프레임의 Update가 다음 슬롯으로
    // 넘어가고, 마지막 슬롯이 끝나면 ShiftCompleted로 정산을 요청한다.
    public sealed class BusinessShiftController : MonoBehaviour
    {
        // 같은 손님이 연달아 다시 나오지 않도록 최근 등장한 손님 N명을 기억해 다음 가중치 선택에서 제외한다.
        private const int RecentCustomerLimit = 2;

        private readonly Queue<string> recentCustomerKeys = new();
        private readonly HashSet<string> recentCustomerKeySet =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> invalidVisitKeys =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> attemptedEpisodeIds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CustomerVisitData> frozenCustomerPool = new();
        private readonly List<EpisodeData> slotCandidates = new();

        private BusinessOrderSessionController orderSession;
        private BusinessOrderSessionUI sessionUi;
        private GameModeManager modeManager;
        private BusinessOrderFlowSettings settings;
        private IBusinessSalePayoutPolicy salePayoutPolicy;
        private IDayScheduleSource scheduleSource;
        private EpisodeData forcedFirstSlotEpisode;
        private System.Random random;
        private bool initialized;
        private bool shiftActive;
        private bool orderActive;
        private bool encounterActive;
        private bool explicitlyPaused;
        private bool forceCompletionRequested;
        private int orderSequence;
        private int completedOrderCount;
        private int currentSlot;
        private int slotsPerDay;

        public BusinessShiftState State { get; private set; } = BusinessShiftState.Idle;
        public bool IsActive => shiftActive;
        public int CurrentSlot => currentSlot;
        public int SlotsPerDay => slotsPerDay;
        public int FrozenCustomerPoolCount => frozenCustomerPool.Count;
        public int CompletedOrderCount => completedOrderCount;
        public int TotalStartedCustomerCount { get; private set; }
        public int TotalStartedEncounterCount { get; private set; }
        public bool IsForceCompletionPending => forceCompletionRequested;
        public string LastSelectedVisitKey { get; private set; } = string.Empty;
        public string LastStartedEpisodeId { get; private set; } = string.Empty;

        public event Action<BusinessShiftState, BusinessShiftState> StateChanged;
        public event Action ShiftCompleted;
        public event Action<CustomerVisitData> CustomerVisitStarted;
        // 슬롯이 시작될 때 호출. episode가 null이면 그 슬롯은 랜덤 손님으로 채워진 것이다.
        public event Action<int, EpisodeData> SlotStarted;

        public void Initialize(
            BusinessOrderSessionController sessionController,
            BusinessOrderSessionUI businessSessionUi,
            GameModeManager gameModeManager,
            BusinessOrderFlowSettings flowSettings,
            IDayScheduleSource schedule)
        {
            if (initialized)
                return;

            orderSession = sessionController;
            sessionUi = businessSessionUi;
            modeManager = gameModeManager;
            settings = flowSettings;
            scheduleSource ??= schedule;
            salePayoutPolicy = new ImmediateSalePayoutPolicy();
            initialized = orderSession != null && settings != null;

            if (!initialized)
                Debug.LogError("[BusinessShift] 영업 컨트롤러 초기화에 필요한 참조가 없습니다.");
        }

        // 플레이테스트가 실제 에피소드 카탈로그 대신 메모리 일정표를 주입할 때 사용한다.
        public bool TrySetScheduleSource(IDayScheduleSource schedule)
        {
            if (shiftActive || schedule == null)
                return false;

            scheduleSource = schedule;
            return true;
        }

        public bool TrySetSalePayoutPolicy(IBusinessSalePayoutPolicy policy)
        {
            if (shiftActive || policy == null)
                return false;

            salePayoutPolicy = policy;
            return true;
        }

        public bool BeginShift()
        {
            if (!initialized || shiftActive)
                return false;

            ResetRuntimeState();
            GameProgress progress = GameProgress.Instance;
            CustomerVisitDatabase database = settings.customerVisitDatabase
                ?? CustomerVisitDatabase.LoadDefault();

            // 구조적으로 쓸 수 있는 손님만 영업 시작 시 한 번 추려 둔다. 실제 등장 가능 여부(조건·TV)는
            // 슬롯마다 다시 평가하므로 앞 슬롯 에피소드가 바꾼 진행도가 뒤 슬롯 손님에게도 반영된다.
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

            if (frozenCustomerPool.Count == 0)
            {
                Debug.LogError(
                    "[BusinessShift] 영업 시작 시 사용할 수 있는 랜덤 손님이 없습니다. "
                    + "에피소드가 배정되지 않은 슬롯은 건너뜁니다.");
            }

            int day = progress != null ? progress.CurrentDay : 0;
            random = new System.Random(unchecked(Environment.TickCount ^ day * 397 ^ GetInstanceID()));
            slotsPerDay = Mathf.Max(1, settings.customersPerDay);
            forcedFirstSlotEpisode = EpisodeManager.Instance?.ConsumeQueuedDebugEncounter();
            shiftActive = true;
            SetState(BusinessShiftState.Running);
            modeManager?.RequestModeChange(GameMode.OrderMode);
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

            if (orderActive || encounterActive)
            {
                Debug.LogWarning(
                    "[BusinessShift] 영업 강제 완료가 예약되었습니다. 현재 주문 또는 인카운터 종료 후 정산합니다.");
                return true;
            }

            CompleteShift();
            return true;
        }

        // 다음 슬롯 진입을 완료 콜백이 아니라 Update에서만 하는 이유: 주문 세션이 BeginOrder 호출
        // 안에서 동기적으로 끝나는 경우에도 재진입 없이 항상 같은 경로로 다음 슬롯을 시작하기 위해서다.
        private void Update()
        {
            if (!shiftActive || orderActive || encounterActive || IsPaused())
                return;

            AdvanceToNextSlot();
        }

        private void AdvanceToNextSlot()
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

            // 슬롯을 시작하지 못하면(손님 풀 고갈 등) 그 슬롯을 비우고 같은 프레임에 다음 슬롯을 시도한다.
            while (currentSlot < slotsPerDay)
            {
                currentSlot++;
                if (TryStartSlot(currentSlot, progress))
                    return;

                Debug.LogError(
                    $"[BusinessShift] {progress.CurrentDay}일차 {currentSlot}번 슬롯을 채울 대상이 없어 건너뜁니다.");
            }

            CompleteShift();
        }

        private bool TryStartSlot(int slot, GameProgress progress)
        {
            EpisodeData episode = ResolveSlotEpisode(slot, progress);
            if (episode != null && StartBusinessEncounter(episode))
            {
                SlotStarted?.Invoke(slot, episode);
                return true;
            }

            // 배정된 에피소드가 없거나 등장 조건을 만족하지 못하면 그 자리는 랜덤 손님이 채운다.
            if (!TryStartRandomCustomer(progress))
                return false;

            SlotStarted?.Invoke(slot, null);
            return true;
        }

        private EpisodeData ResolveSlotEpisode(int slot, GameProgress progress)
        {
            if (slot == 1 && forcedFirstSlotEpisode != null)
            {
                EpisodeData forced = forcedFirstSlotEpisode;
                forcedFirstSlotEpisode = null;
                return forced;
            }

            if (scheduleSource == null)
                return null;

            scheduleSource.CollectSlotCandidates(
                progress.CurrentChapterId,
                progress.CurrentDay,
                slot,
                slotCandidates);
            return DayScheduleResolver.PickEpisode(slotCandidates, progress, attemptedEpisodeIds);
        }

        private bool TryStartRandomCustomer(GameProgress progress)
        {
            // 시작에 실패한 방문은 invalidVisitKeys에 들어가므로 풀 크기만큼만 재시도하면 반드시 끝난다.
            for (int attempt = 0; attempt <= frozenCustomerPool.Count; attempt++)
            {
                BusinessVisitSelection selection = PickRandomVisit(progress);
                if (selection == null)
                    return false;

                if (StartCustomerOrder(selection.Visit, selection.OrderOption))
                    return true;
            }

            return false;
        }

        // 하루 손님 수가 고정이라 슬롯을 비울 수 없으므로, 최근 2명 제한 때문에 후보가 없으면
        // 제한을 풀고 한 번 더 뽑는다(연속 등장이 슬롯 공백보다 낫다는 기획 결정).
        private BusinessVisitSelection PickRandomVisit(GameProgress progress)
        {
            BusinessVisitSelection selection = BusinessSequencePlanner.PickWeightedVisit(
                frozenCustomerPool,
                progress,
                recentCustomerKeySet,
                invalidVisitKeys,
                random);
            if (selection != null || recentCustomerKeySet.Count == 0)
                return selection;

            Debug.LogWarning(
                "[BusinessShift] 최근 손님 2명 제한 때문에 후보가 없어 제한을 풀고 다시 뽑습니다. "
                + $"recent=[{string.Join(", ", recentCustomerKeys)}]");
            return BusinessSequencePlanner.PickWeightedVisit(
                frozenCustomerPool,
                progress,
                null,
                invalidVisitKeys,
                random);
        }

        private bool StartCustomerOrder(
            CustomerVisitData visit,
            CustomerVisitOrderOption orderOption)
        {
            CustomerOrderData order = orderOption?.order;
            if (visit == null || order == null)
                return false;

            if (!orderSession.ValidateCustomerOrderData(order, out string validationError))
            {
                invalidVisitKeys.Add(visit.visitKey);
                Debug.LogError(
                    $"[BusinessShift] 유효하지 않은 손님 주문을 시작하지 않습니다: "
                    + $"visit={visit.visitKey}, order={order.key}, "
                    + $"recipe={order.requestedRecipeId}, reason={validationError}");
                return false;
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
                // 세션이 자체적으로 보상을 지급하지 않게 막고, 완료 후 RecordSale()에서
                // salePayoutPolicy를 통해 지급한다 — 정책(즉시 지급/정산 시 일괄 지급 등)을
                // 교체 가능하게 하기 위해서다(TrySetSalePayoutPolicy).
                applyProgressRewards = false,
                clearCustomerOnComplete = true
            };

            orderActive = true;
            SetState(BusinessShiftState.OrderActive);
            bool started = orderSession.BeginOrder(
                request,
                result => HandleCustomerOrderCompleted(visit, result));
            if (!started)
            {
                orderActive = false;
                invalidVisitKeys.Add(visit.visitKey);
                Debug.LogError(
                    $"[BusinessShift] 손님 주문을 시작하지 못했습니다: {visit.visitKey}/{order.key}");
                SetState(BusinessShiftState.Running);
                return false;
            }

            TotalStartedCustomerCount++;
            LastSelectedVisitKey = visit.visitKey;
            RecordRecentCustomer(visit);
            CustomerVisitStarted?.Invoke(visit);
            RecordCustomerAppearance(visit);
            return true;
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
                salePayoutPolicy?.Apply(result, GameProgress.Instance);
            }
            else
            {
                // 기술적 실패(세션 초기화 오류 등)는 손님 잘못이 아니므로 오늘 풀에 남겨 재시도
                // 가능하게 하고, 그 외(거절·오답 제출 등 실제 결과가 난 실패)만 풀에서 제외한다.
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

            SetState(BusinessShiftState.Running);
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

        private bool StartBusinessEncounter(EpisodeData episode)
        {
            // 시작 성공 여부와 무관하게 기록해, 실패한 에피소드를 같은 날 다른 슬롯에서 다시 고르지 않게 한다.
            attemptedEpisodeIds.Add(episode.episodeId);

            encounterActive = true;
            SetState(BusinessShiftState.EncounterActive);

            bool started = EpisodeManager.Instance != null
                && EpisodeManager.Instance.TryStartBusinessEncounter(
                    episode,
                    HandleBusinessEncounterCompleted);
            if (started)
            {
                TotalStartedEncounterCount++;
                LastStartedEpisodeId = episode.episodeId;
                return true;
            }

            encounterActive = false;
            Debug.LogError(
                $"[BusinessShift] 인카운터를 시작하지 못해 랜덤 손님으로 대체합니다: {episode.episodeId}");
            SetState(BusinessShiftState.Running);
            return false;
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

            SetState(BusinessShiftState.Running);
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

            shiftActive = false;
            forceCompletionRequested = false;
            SetState(BusinessShiftState.Completed);
            sessionUi?.ShowDayComplete();
            ShiftCompleted?.Invoke();
        }

        private void ResetRuntimeState()
        {
            recentCustomerKeys.Clear();
            recentCustomerKeySet.Clear();
            invalidVisitKeys.Clear();
            attemptedEpisodeIds.Clear();
            frozenCustomerPool.Clear();
            slotCandidates.Clear();
            forcedFirstSlotEpisode = null;
            orderActive = false;
            encounterActive = false;
            explicitlyPaused = false;
            forceCompletionRequested = false;
            orderSequence = 0;
            completedOrderCount = 0;
            currentSlot = 0;
            TotalStartedCustomerCount = 0;
            TotalStartedEncounterCount = 0;
            LastSelectedVisitKey = string.Empty;
            LastStartedEpisodeId = string.Empty;
            SetState(BusinessShiftState.Idle);
        }

        private bool IsPaused()
        {
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
