using System;
using System.Collections;
using Slainte.Bartending;
using Slainte.Content;
using Slainte.Economy;
using Slainte.TV;
using UnityEngine;

namespace Slainte.Business
{
    // 영업 손님 주문과 에피소드 제조 노드가 공유하는 "주문 제시 → 제조 → 판정 → 결과 피드백"
    // 세션(State: Idle → PresentingOrder → Crafting → Evaluating → PresentingFeedback → Completed).
    // 호출자는 OrderSessionRequest의 owner/presentOrder/presentFeedback/applyProgressRewards 등
    // 플래그로 어떤 단계를 건너뛰고 어떤 부수효과(보상 지급·판매 기록 등)를 적용할지 선택한다 —
    // 예: 에피소드 제조는 손님 주문 제시·보상 지급을 생략하지만 판정 로직 자체는 영업과 동일하다.
    public sealed class BusinessOrderSessionController : MonoBehaviour
    {
        private GameModeManager modeManager;
        private CustomerSpawner customerSpawner;
        private DialogueController dialogue;
        private OrderTicketManager ticketManager;
        private BusinessBartendingBootstrap bartending;
        private BusinessOrderSessionUI ui;
        private BusinessOrderFlowSettings settings;
        private CocktailOrderGenerator orderGenerator;
        private CocktailOrderEvaluator orderEvaluator;
        private TVBroadcastDatabase tvBroadcastDatabase;
        private GeneratedCocktailOrder currentOrder;
        private OrderSessionRequest currentRequest;
        private VesselLiquidTracker servingTarget;
        private BusinessOrderSessionResult pendingResult;
        private Action<BusinessOrderSessionResult> completionCallback;
        private Coroutine craftingPreparationRoutine;
        private bool initialized;
        private bool completionDispatched;

        [SerializeField, Min(0.1f)]
        private float craftingPrepareTimeoutSeconds = 5f;

        public BusinessOrderSessionState State { get; private set; } = BusinessOrderSessionState.Idle;
        public string CurrentRecipeName => currentOrder?.RequestedRecipeName ?? string.Empty;
        public bool HasActiveOrder => currentRequest != null
            && !completionDispatched
            && State != BusinessOrderSessionState.Idle
            && State != BusinessOrderSessionState.Completed;
        public string CurrentSessionId => currentRequest?.sessionId ?? string.Empty;
        public OrderSessionOwner CurrentOwner => currentRequest?.owner ?? OrderSessionOwner.Business;

        public event Action<BusinessOrderSessionState, BusinessOrderSessionState> StateChanged;
        public event Action<BusinessOrderSessionResult> OrderCompleted;

        public bool ValidateCustomerOrderData(
            CustomerOrderData order,
            out string failureReason)
        {
            if (order == null)
            {
                failureReason = "주문 에셋이 없습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(order.key))
            {
                failureReason = "주문 키가 비어 있습니다.";
                return false;
            }

            bool tagOrder = IsTagOrder(order.orderType);
            if (!BusinessSequencePlanner.HasStructurallyValidOrderTarget(order))
            {
                failureReason = tagOrder
                    ? $"주문 {order.key}에는 정확히 하나의 유효한 조건 태그가 필요합니다."
                    : $"주문 {order.key}의 레시피 ID가 비어 있습니다.";
                return false;
            }

            if (orderGenerator == null
                || !orderGenerator.CanGenerateOrder(
                    order.orderType,
                    order.requestedRecipeId,
                    order.tags))
            {
                failureReason = tagOrder
                    ? $"주문 {order.key}에는 정확히 하나의 유효한 조건 태그가 필요합니다."
                    : $"주문 가능한 레시피를 불러올 수 없습니다: {order.requestedRecipeId}";
                return false;
            }

            if (customerSpawner == null)
            {
                failureReason = "CustomerSpawner 참조가 없습니다.";
                return false;
            }

            if (!customerSpawner.CanResolveOrder(order.key, out CustomerOrderData registered))
            {
                failureReason = $"손님 주문 DB에 주문 키가 없습니다: {order.key}";
                return false;
            }

            if (registered != order)
            {
                failureReason = $"방문 데이터와 손님 주문 DB가 서로 다른 주문 에셋을 가리킵니다: {order.key}";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        public void Initialize(
            GameModeManager gameModeManager,
            CustomerSpawner sceneCustomerSpawner,
            DialogueController dialogueController,
            OrderTicketManager orderTicketManager,
            BusinessBartendingBootstrap bartendingBootstrap,
            BusinessOrderSessionUI sessionUi,
            BusinessOrderFlowSettings flowSettings)
        {
            if (initialized)
                return;

            modeManager = gameModeManager;
            customerSpawner = sceneCustomerSpawner;
            dialogue = dialogueController;
            ticketManager = orderTicketManager;
            bartending = bartendingBootstrap;
            ui = sessionUi;
            settings = flowSettings;
            tvBroadcastDatabase = TVBroadcastDatabase.LoadDefault();
            BuildEvaluationServices();

            if (dialogue != null)
                dialogue.DialogueClosed += HandleDialogueClosed;
            if (bartending != null)
            {
                bartending.SessionReady += HandleBartendingSessionReady;
                bartending.SessionDestroyed += HandleBartendingSessionDestroyed;
                bartending.ServeRequested += HandleServeRequested;
            }

            initialized = true;
        }

        private void OnDestroy()
        {
            if (dialogue != null)
                dialogue.DialogueClosed -= HandleDialogueClosed;
            if (bartending != null)
            {
                bartending.SessionReady -= HandleBartendingSessionReady;
                bartending.SessionDestroyed -= HandleBartendingSessionDestroyed;
                bartending.ServeRequested -= HandleServeRequested;
            }
        }

        public bool BeginOrder(
            OrderSessionRequest request,
            Action<BusinessOrderSessionResult> onCompleted)
        {
            if (!initialized
                || request == null
                || string.IsNullOrWhiteSpace(request.sessionId)
                || !HasValidRequestTarget(request))
                return false;

            if (State != BusinessOrderSessionState.Idle && State != BusinessOrderSessionState.Completed)
                return false;

            currentRequest = request;
            completionCallback = onCompleted;
            completionDispatched = false;
            currentOrder = orderGenerator?.GenerateOrder(
                currentRequest.orderType,
                currentRequest.requestedRecipeId,
                currentRequest.requestedTags,
                currentRequest.requestedConditionLabel);
            pendingResult = null;
            servingTarget = null;

            if (currentOrder == null)
            {
                string reason = IsTagOrder(currentRequest.orderType)
                    ? "요청한 주문 조건을 불러올 수 없습니다: "
                        + currentRequest.requestedConditionLabel
                    : "요청한 레시피를 불러올 수 없습니다: "
                        + currentRequest.requestedRecipeId;
                ui?.ShowError(reason);
                CompleteCurrentOrder(new BusinessOrderSessionResult
                {
                    outcome = OrderSessionOutcome.Failed,
                    customerOrderKey = currentRequest.customerOrderKey,
                    requestedRecipeId = currentRequest.requestedRecipeId,
                    accepted = false,
                    grade = OrderEvaluationGrade.Bad,
                    technicalFailure = true,
                    failureReason = reason
                });
                return true;
            }

            if (!currentRequest.presentOrder)
            {
                BeginCrafting();
                return true;
            }

            SetState(BusinessOrderSessionState.PresentingOrder);
            ui?.ShowPresentingOrder(currentRequest.customerOrderKey);
            bool visitShown = customerSpawner != null
                && customerSpawner.ShowVisit(
                    currentRequest.customerVisitKey,
                    currentRequest.customerOrderKey,
                    currentOrder.line,
                    dialogueStarted =>
                    {
                        if (!dialogueStarted
                            && State == BusinessOrderSessionState.PresentingOrder)
                        {
                            BeginCrafting();
                        }
                    });

            if (!visitShown)
            {
                string reason = customerSpawner == null
                    ? "CustomerSpawner 참조가 없습니다."
                    : "손님 주문 DB에서 주문 키를 찾을 수 없습니다.";
                reason += $" visit={currentRequest.customerVisitKey}, "
                    + $"order={currentRequest.customerOrderKey}, "
                    + $"recipe={currentRequest.requestedRecipeId}";
                ui?.ShowError(reason);
                CompleteCurrentOrder(new BusinessOrderSessionResult
                {
                    outcome = OrderSessionOutcome.Failed,
                    customerOrderKey = currentRequest.customerOrderKey,
                    requestedRecipeId = currentRequest.requestedRecipeId,
                    accepted = false,
                    grade = OrderEvaluationGrade.Bad,
                    technicalFailure = true,
                    failureReason = reason
                });
                return true;
            }

            return true;
        }

        public bool TryAbortEpisodeOrderForRecovery()
        {
            if (currentRequest == null
                || currentRequest.owner != OrderSessionOwner.Episode
                || completionDispatched
                || State == BusinessOrderSessionState.Idle
                || State == BusinessOrderSessionState.Completed)
            {
                return false;
            }

            AbortForTechnicalFailure("현재 에피소드 강제 완료로 제조 세션을 종료했습니다.");
            return true;
        }

#if UNITY_EDITOR
        public bool TryCompleteCurrentOrderForPlaytest()
        {
            return TryForceCurrentOrderResult(
                CraftingJobResult.Good,
                presentConfiguredFeedback: false);
        }

        public bool TryForceCurrentOrderResult(CraftingJobResult forcedResult)
        {
            return TryForceCurrentOrderResult(
                forcedResult,
                presentConfiguredFeedback: true);
        }

        private bool TryForceCurrentOrderResult(
            CraftingJobResult forcedResult,
            bool presentConfiguredFeedback)
        {
            if (!HasActiveOrder)
                return false;

            dialogue?.HideImmediate();
            StopCraftingPreparationTimeout();
            SetState(BusinessOrderSessionState.Evaluating);

            CocktailOrderEvaluationOutcome outcome = forcedResult switch
            {
                CraftingJobResult.Good => CocktailOrderEvaluationOutcome.Good,
                CraftingJobResult.MidIce => CocktailOrderEvaluationOutcome.MidIce,
                CraftingJobResult.MidGlass => CocktailOrderEvaluationOutcome.MidGlass,
                CraftingJobResult.MidIceGlass => CocktailOrderEvaluationOutcome.MidIceGlass,
                CraftingJobResult.MidWrongMenu => CocktailOrderEvaluationOutcome.MidWrongMenu,
                _ => CocktailOrderEvaluationOutcome.Bad
            };
            CocktailOrderEvaluationResult evaluation = new()
            {
                order = currentOrder,
                outcome = outcome,
                isSuccess = forcedResult == CraftingJobResult.Good
                    || forcedResult == CraftingJobResult.MidIce
                    || forcedResult == CraftingJobResult.MidGlass
                    || forcedResult == CraftingJobResult.MidIceGlass,
                failureReason = $"에디터 디버그 도구에서 {forcedResult} 결과로 넘겼습니다."
            };
            OrderEvaluationGrade grade = OrderEvaluationGrader.Resolve(evaluation, settings);
            GameCurrency paymentCurrency = currentRequest.paymentCurrency;
            int listedPrice = BusinessOrderPriceRules.ApplyPaymentMultiplier(
                GetListedPrice(paymentCurrency),
                currentRequest.paymentMultiplier);
            BusinessOrderReward reward = BusinessOrderRewardCalculator.Calculate(
                grade,
                listedPrice,
                settings,
                TVBroadcastRuntime.GetTipMultiplier(
                    GameProgress.Instance,
                    tvBroadcastDatabase),
                GameProgress.Instance != null ? GameProgress.Instance.CurrentMoney : 0,
                currentRequest.rewardProfile);
            pendingResult = new BusinessOrderSessionResult
            {
                outcome = OrderSessionOutcome.Served,
                customerOrderKey = currentRequest.customerOrderKey,
                customerVisitKey = currentRequest.customerVisitKey,
                requestedRecipeId = currentRequest.requestedRecipeId,
                paymentCurrency = paymentCurrency,
                listedPrice = listedPrice,
                accepted = true,
                grade = grade,
                customerMood = reward.Mood,
                baseRevenue = reward.BaseRevenue,
                tipAmount = reward.TipAmount,
                penaltyAmount = reward.PenaltyAmount,
                moneyDelta = paymentCurrency == GameCurrency.Money ? reward.TotalRevenue : 0,
                strangeCoinDelta = paymentCurrency == GameCurrency.StrangeCoin ? reward.TotalRevenue : 0,
                totalPayment = reward.TotalRevenue,
                reputationDelta = reward.ReputationDelta,
                evaluation = evaluation
            };

            Debug.LogWarning(
                $"[BusinessOrderSession] 현재 주문을 디버그 결과로 넘깁니다: "
                + $"session={currentRequest.sessionId}, result={forcedResult}");
            PresentPendingResult(presentConfiguredFeedback);
            return true;
        }
#endif

        private void BeginCrafting()
        {
            if (currentRequest == null)
                return;

            if (currentRequest.ticketData != null
                || !string.IsNullOrWhiteSpace(currentRequest.ticketKey))
                PrepareCurrentOrderTicket();

            SetState(BusinessOrderSessionState.Crafting);
            ui?.ShowCrafting(CurrentRecipeName);
            if (bartending == null)
            {
                AbortForTechnicalFailure("영업 제조 런타임을 찾을 수 없습니다.");
                return;
            }

            modeManager?.RequestModeChange(GameMode.CraftingMode);
            servingTarget = null;
            StopCraftingPreparationTimeout();
            if (!bartending.IsSessionReady)
                craftingPreparationRoutine = StartCoroutine(WaitForCraftingPreparation());
        }

        private void PrepareCurrentOrderTicket()
        {
            if (ticketManager == null
                || currentRequest == null
                || (currentRequest.ticketData == null
                    && string.IsNullOrWhiteSpace(currentRequest.ticketKey)))
            {
                return;
            }

            CustomerOrderData customerOrder = customerSpawner?.CurrentOrderData;
            bool matchingPresentedOrder = currentRequest.presentOrder
                && customerOrder != null
                && string.Equals(
                    customerOrder.key,
                    currentRequest.customerOrderKey,
                    StringComparison.Ordinal);
            if (matchingPresentedOrder)
            {
                string memo = OrderTicketMemoFormatter.Build(
                    customerOrder,
                    currentOrder?.line);
                if (currentRequest.ticketData != null)
                    ticketManager.Prepare(currentRequest.ticketData, memo);
                else
                    ticketManager.Prepare(currentRequest.ticketKey, memo);
                return;
            }

            if (currentRequest.ticketData != null)
                ticketManager.Prepare(currentRequest.ticketData);
            else
                ticketManager.Prepare(currentRequest.ticketKey);
        }

        private void SubmitOrder()
        {
            if (State != BusinessOrderSessionState.Crafting)
                return;

            if (servingTarget == null)
            {
                ui?.ShowError("제출할 잔을 찾을 수 없습니다.");
                return;
            }

            StopCraftingPreparationTimeout();
            SetState(BusinessOrderSessionState.Evaluating);
            ui?.ShowEvaluating();

            CocktailComposition composition = servingTarget.BuildComposition();
            CocktailOrderEvaluationResult evaluation = orderEvaluator?.Evaluate(currentOrder, composition);
            if (evaluation == null)
            {
                AbortForTechnicalFailure("제조 결과 판정기를 사용할 수 없습니다.");
                return;
            }

            OrderEvaluationGrade grade = OrderEvaluationGrader.Resolve(evaluation, settings);
            GameCurrency paymentCurrency = currentRequest.paymentCurrency;
            int listedPrice = BusinessOrderPriceRules.ApplyPaymentMultiplier(
                GetListedPrice(paymentCurrency, evaluation),
                currentRequest.paymentMultiplier);
            BusinessOrderReward reward = BusinessOrderRewardCalculator.Calculate(
                grade,
                listedPrice,
                settings,
                TVBroadcastRuntime.GetTipMultiplier(
                    GameProgress.Instance,
                    tvBroadcastDatabase),
                GameProgress.Instance != null ? GameProgress.Instance.CurrentMoney : 0,
                currentRequest.rewardProfile);
            pendingResult = new BusinessOrderSessionResult
            {
                outcome = OrderSessionOutcome.Served,
                customerOrderKey = currentRequest.customerOrderKey,
                customerVisitKey = currentRequest.customerVisitKey,
                requestedRecipeId = currentRequest.requestedRecipeId,
                paymentCurrency = paymentCurrency,
                listedPrice = listedPrice,
                accepted = true,
                grade = grade,
                customerMood = reward.Mood,
                baseRevenue = reward.BaseRevenue,
                tipAmount = reward.TipAmount,
                penaltyAmount = reward.PenaltyAmount,
                moneyDelta = paymentCurrency == GameCurrency.Money ? reward.TotalRevenue : 0,
                strangeCoinDelta = paymentCurrency == GameCurrency.StrangeCoin ? reward.TotalRevenue : 0,
                totalPayment = reward.TotalRevenue,
                reputationDelta = reward.ReputationDelta,
                evaluation = evaluation
            };

            Debug.Log("[주문 처리] " + (evaluation != null
                ? evaluation.ToDebugString()
                : "판정 기능을 사용할 수 없습니다."));

            PresentPendingResult(presentConfiguredFeedback: true);
        }

        private void PresentPendingResult(bool presentConfiguredFeedback)
        {
            modeManager?.RequestModeChange(GameMode.OrderMode);
            ticketManager?.ClearTicket();

            if (!presentConfiguredFeedback
                || currentRequest == null
                || !currentRequest.presentFeedback)
            {
                CompletePendingResult();
                return;
            }

            SetState(BusinessOrderSessionState.PresentingFeedback);
            ui?.ShowFeedback(pendingResult);

            CraftingJobResult detailedResult = CraftingResultMapper.Map(pendingResult);
            CustomerDialoguePresentation feedbackPresentation = customerSpawner != null
                ? customerSpawner.ShowFeedback(detailedResult)
                : CustomerDialoguePresentation.Missing;
            if (feedbackPresentation == CustomerDialoguePresentation.IntentionallySkipped)
            {
                CompletePendingResult();
                return;
            }

            bool feedbackStarted = feedbackPresentation == CustomerDialoguePresentation.Played;
            if (!feedbackStarted && dialogue != null && settings != null)
            {
                string fallback = settings.GetMissingFeedbackDummy(detailedResult);
                if (string.IsNullOrWhiteSpace(fallback))
                    fallback = settings.GetFallbackFeedback(pendingResult.grade);
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    Debug.LogWarning(
                        "[CustomerFeedback] 결과 대사가 없어 임시 대사를 출력합니다. "
                        + $"order={currentRequest?.customerOrderKey ?? pendingResult?.customerOrderKey}, "
                        + $"result={detailedResult}, fallback={fallback}");
                    dialogue.ShowSingleLine(settings.feedbackSpeakerName, fallback, Color.white);
                    feedbackStarted = true;
                }
            }

            if (!feedbackStarted)
                CompletePendingResult();
        }

        private void BuildEvaluationServices()
        {
            ItemDefCatalog itemCatalog = ItemDefCatalog.LoadFromResources(
                ProjectResourcePaths.BartendingItems,
                null);
            CocktailRecipeCatalog recipeCatalog = CocktailRecipeDataLoader.LoadDefault(itemCatalog);
            CocktailOrderTemplateCatalog templateCatalog = CocktailOrderCsvLoader.LoadTemplatesFromStreamingAssets(
                "Data",
                "order_templates.csv");
            CocktailEvaluator evaluator = new CocktailEvaluator(recipeCatalog);
            orderGenerator = new CocktailOrderGenerator(recipeCatalog, templateCatalog);
            orderEvaluator = new CocktailOrderEvaluator(evaluator);
        }

        private void HandleDialogueClosed()
        {
            if (State == BusinessOrderSessionState.PresentingOrder)
            {
                BeginCrafting();
                return;
            }

            if (State == BusinessOrderSessionState.PresentingFeedback)
                CompletePendingResult();
        }

        private void HandleBartendingSessionReady()
        {
            if (State != BusinessOrderSessionState.Crafting)
                return;

            StopCraftingPreparationTimeout();
        }

        private void HandleBartendingSessionDestroyed()
        {
            servingTarget = null;
            if (State == BusinessOrderSessionState.Crafting)
                AbortForTechnicalFailure("제조가 완료되기 전에 제조 세션이 종료됐습니다.");
        }

        private void HandleServeRequested(VesselLiquidTracker tracker)
        {
            if (State != BusinessOrderSessionState.Crafting || tracker == null)
                return;

            servingTarget = tracker;
            SubmitOrder();
        }

        private void CompletePendingResult()
        {
            if (pendingResult == null)
                return;

            BusinessOrderSessionResult result = pendingResult;
            pendingResult = null;
            CompleteCurrentOrder(result);
        }

        // 기술적 실패(AbortForTechnicalFailure), 정상 판정 완료, 에디터 강제 결과 등 여러 경로가
        // 이 메서드로 모이므로, completionDispatched 플래그로 같은 세션이 두 번 완료 처리되어
        // 콜백이 중복 호출되지 않게 막는다.
        private void CompleteCurrentOrder(BusinessOrderSessionResult result)
        {
            if (result == null || completionDispatched)
                return;

            completionDispatched = true;
            StopCraftingPreparationTimeout();
            OrderSessionRequest completedRequest = currentRequest;
            result.sessionId = completedRequest?.sessionId ?? result.sessionId;
            result.owner = completedRequest?.owner ?? result.owner;
            result.customerVisitKey = completedRequest?.customerVisitKey ?? result.customerVisitKey;

            // 지급·판매기록·평판 반영은 각각 독립 플래그다 — 영업 주문은 셋 다 켜지만, 에피소드
            // 제조는 applyProgressRewards=false로 지갑/평판에 영향을 주지 않으면서도 판정 자체는
            // 동일 경로를 통과한다(호출자가 결과를 보고 자체적으로 보상을 줄 수도 있음).
            GameProgress progress = GameProgress.Instance;
            if (progress != null && completedRequest != null)
            {
                bool paymentApplied = completedRequest.applyProgressRewards
                    || completedRequest.applyPayment;
                if (paymentApplied)
                    GameCurrencyWallet.Add(progress, result.paymentCurrency, result.PaymentAmount);
                if (completedRequest.recordSale
                    && result.outcome == OrderSessionOutcome.Served
                    && result.accepted)
                {
                    BusinessSaleRecord record = result.ToSaleRecord();
                    record.paymentApplied = paymentApplied;
                    if (!completedRequest.applyProgressRewards
                        && !completedRequest.applyReputation)
                    {
                        record.reputationDelta = 0;
                    }
                    progress.RecordDrinkSale(record);
                }
                if (completedRequest.applyProgressRewards || completedRequest.applyReputation)
                    progress.AddReputation(result.reputationDelta);
            }

            if (completedRequest == null || completedRequest.clearCustomerOnComplete)
                customerSpawner?.Clear();
            ticketManager?.ClearTicket();
            SetState(BusinessOrderSessionState.Completed);
            ui?.ShowIdle();

            Action<BusinessOrderSessionResult> callback = completionCallback;
            completionCallback = null;
            currentRequest = null;
            currentOrder = null;
            pendingResult = null;
            servingTarget = null;
            OrderCompleted?.Invoke(result);
            callback?.Invoke(result);
        }

        private int GetListedPrice(
            GameCurrency currency,
            CocktailOrderEvaluationResult evaluation = null)
        {
            CocktailRecipe recipe = ResolveListedRecipe(currentOrder, evaluation);
            if (recipe != null)
                return recipe.GetPrice(currency);

            // 제출 결과를 판정한 뒤에도 레시피를 식별하지 못했다면
            // 고정 보상으로 폴백하지 않고 결과 칵테일의 가격을 0으로 취급한다.
            return evaluation != null ? 0 : -1;
        }

        // 가격은 "무엇을 만들었는지"를 우선한다: 실제로 감지된 레시피가 성공 판정이면 그 가격을
        // 매기고(엉뚱하지만 유효한 다른 레시피를 제출한 경우도 그 레시피 가격), 감지 실패 시에만
        // 요청 레시피 기준(부분 일치 이상)으로 폴백한다.
        private static CocktailRecipe ResolveListedRecipe(
            GeneratedCocktailOrder order,
            CocktailOrderEvaluationResult evaluation)
        {
            if (evaluation == null)
                return order?.requestedRecipe;

            CocktailEvaluationResult detected = evaluation.detectedRecipeResult;
            if (detected != null
                && detected.isSuccess
                && detected.matchedRecipe != null)
            {
                return detected.matchedRecipe;
            }

            CocktailEvaluationResult requested = evaluation.requestedRecipeResult;
            if (requested != null
                && requested.matchedRecipe != null
                && (requested.isSuccess || requested.coreValid))
            {
                return requested.matchedRecipe;
            }

            return null;
        }

        private static bool HasValidRequestTarget(OrderSessionRequest request)
        {
            if (request == null)
                return false;

            if (!IsTagOrder(request.orderType))
                return !string.IsNullOrWhiteSpace(request.requestedRecipeId);

            return CocktailOrderTagRules.TryGetSingleTag(request.requestedTags, out _);
        }

        private static bool IsTagOrder(CocktailOrderType? orderType)
        {
            return orderType == CocktailOrderType.TasteOrder
                || orderType == CocktailOrderType.MoodOrder;
        }

        // 바텐딩 씬/세션이 아직 준비되지 않은 상태로 제조 단계에 진입하면(씬 전환 지연 등)
        // craftingPrepareTimeoutSeconds 동안만 기다리고, 그래도 준비되지 않으면 기술적 실패로
        // 처리해 세션이 영구히 멈추지 않게 한다.
        private IEnumerator WaitForCraftingPreparation()
        {
            float elapsed = 0f;
            while (State == BusinessOrderSessionState.Crafting
                && (bartending == null || !bartending.IsSessionReady))
            {
                if (Time.timeScale > 0f)
                    elapsed += Mathf.Max(0f, Time.unscaledDeltaTime);

                if (elapsed >= craftingPrepareTimeoutSeconds)
                {
                    craftingPreparationRoutine = null;
                    AbortForTechnicalFailure("제조 화면 준비 시간이 초과됐습니다.");
                    yield break;
                }

                yield return null;
            }

            craftingPreparationRoutine = null;
        }

        private void AbortForTechnicalFailure(string reason)
        {
            if (currentRequest == null
                || State == BusinessOrderSessionState.Idle
                || State == BusinessOrderSessionState.Completed)
                return;

            StopCraftingPreparationTimeout();
            SetState(BusinessOrderSessionState.Evaluating);
            Debug.LogError($"[BusinessOrderSession] {reason}");
            ui?.ShowError(reason);
            modeManager?.RequestModeChange(GameMode.OrderMode);
            CompleteCurrentOrder(new BusinessOrderSessionResult
            {
                outcome = OrderSessionOutcome.Failed,
                customerOrderKey = currentRequest.customerOrderKey,
                requestedRecipeId = currentRequest.requestedRecipeId,
                accepted = false,
                grade = OrderEvaluationGrade.Bad,
                technicalFailure = true,
                failureReason = reason
            });
        }

        private void StopCraftingPreparationTimeout()
        {
            if (craftingPreparationRoutine == null)
                return;

            StopCoroutine(craftingPreparationRoutine);
            craftingPreparationRoutine = null;
        }

        private void SetState(BusinessOrderSessionState nextState)
        {
            if (State == nextState)
                return;

            BusinessOrderSessionState previous = State;
            State = nextState;
            StateChanged?.Invoke(previous, nextState);
        }
    }
}
