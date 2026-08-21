using System;
using System.Collections;
using Slainte.Bartending;
using Slainte.Economy;
using Slainte.TV;
using UnityEngine;

namespace Slainte.Business
{
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
            if (!tagOrder && string.IsNullOrWhiteSpace(order.requestedRecipeId))
            {
                failureReason = $"주문 {order.key}의 레시피 ID가 비어 있습니다.";
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

#if UNITY_EDITOR
        public bool TryCompleteCurrentOrderForPlaytest()
        {
            if (currentRequest == null
                || completionDispatched
                || State == BusinessOrderSessionState.Idle
                || State == BusinessOrderSessionState.Completed)
            {
                return false;
            }

            dialogue?.HideImmediate();
            modeManager?.RequestModeChange(GameMode.OrderMode);
            GameCurrency paymentCurrency = currentRequest.paymentCurrency;
            int listedPrice = GetListedPrice(paymentCurrency);
            BusinessOrderReward reward = BusinessOrderRewardCalculator.Calculate(
                OrderEvaluationGrade.Good,
                listedPrice,
                settings,
                TVBroadcastRuntime.GetTipMultiplier(
                    GameProgress.Instance,
                    tvBroadcastDatabase));
            CompleteCurrentOrder(new BusinessOrderSessionResult
            {
                outcome = OrderSessionOutcome.Served,
                customerOrderKey = currentRequest.customerOrderKey,
                customerVisitKey = currentRequest.customerVisitKey,
                requestedRecipeId = currentRequest.requestedRecipeId,
                paymentCurrency = paymentCurrency,
                listedPrice = listedPrice,
                accepted = true,
                grade = OrderEvaluationGrade.Good,
                customerMood = reward.Mood,
                baseRevenue = reward.BaseRevenue,
                tipAmount = reward.TipAmount,
                moneyDelta = paymentCurrency == GameCurrency.Money ? reward.TotalRevenue : 0,
                strangeCoinDelta = paymentCurrency == GameCurrency.StrangeCoin ? reward.TotalRevenue : 0,
                totalPayment = reward.TotalRevenue,
                reputationDelta = reward.ReputationDelta
            });
            return true;
        }
#endif

        private void BeginCrafting()
        {
            if (currentRequest == null)
                return;

            if (!string.IsNullOrWhiteSpace(currentRequest.ticketKey))
                ticketManager?.Prepare(currentRequest.ticketKey);

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
            int listedPrice = GetListedPrice(paymentCurrency, evaluation);
            BusinessOrderReward reward = BusinessOrderRewardCalculator.Calculate(
                grade,
                listedPrice,
                settings,
                TVBroadcastRuntime.GetTipMultiplier(
                    GameProgress.Instance,
                    tvBroadcastDatabase));
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
                moneyDelta = paymentCurrency == GameCurrency.Money ? reward.TotalRevenue : 0,
                strangeCoinDelta = paymentCurrency == GameCurrency.StrangeCoin ? reward.TotalRevenue : 0,
                totalPayment = reward.TotalRevenue,
                reputationDelta = reward.ReputationDelta,
                evaluation = evaluation
            };

            Debug.Log("[주문 처리] " + (evaluation != null
                ? evaluation.ToDebugString()
                : "판정 기능을 사용할 수 없습니다."));

            modeManager?.RequestModeChange(GameMode.OrderMode);
            ticketManager?.ClearTicket();

            if (currentRequest != null && !currentRequest.presentFeedback)
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
                string fallback = settings.GetFallbackFeedback(grade);
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    dialogue.ShowSingleLine(settings.feedbackSpeakerName, fallback, Color.white);
                    feedbackStarted = true;
                }
            }

            if (!feedbackStarted)
                CompletePendingResult();
        }

        private void BuildEvaluationServices()
        {
            ItemDefCatalog itemCatalog = ItemDefCatalog.LoadFromResources("Items", null);
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

            GameProgress progress = GameProgress.Instance;
            if (progress != null && completedRequest != null)
            {
                if (completedRequest.applyProgressRewards || completedRequest.applyPayment)
                    GameCurrencyWallet.Add(progress, result.paymentCurrency, result.PaymentAmount);
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
            CocktailRecipe recipe = IsTagOrder(currentOrder?.orderType)
                ? evaluation?.detectedRecipeResult?.matchedRecipe
                : currentOrder?.requestedRecipe;
            return recipe != null ? recipe.GetPrice(currency) : -1;
        }

        private static bool HasValidRequestTarget(OrderSessionRequest request)
        {
            if (request == null)
                return false;

            if (!IsTagOrder(request.orderType))
                return !string.IsNullOrWhiteSpace(request.requestedRecipeId);

            if (request.requestedTags == null)
                return false;

            int validTags = 0;
            for (int i = 0; i < request.requestedTags.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(request.requestedTags[i]))
                    validTags++;
            }
            return validTags == 1;
        }

        private static bool IsTagOrder(CocktailOrderType? orderType)
        {
            return orderType == CocktailOrderType.TasteOrder
                || orderType == CocktailOrderType.MoodOrder;
        }

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
