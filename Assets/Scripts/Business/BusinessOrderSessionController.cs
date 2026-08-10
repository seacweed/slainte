using System;
using Slainte.Bartending;
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
        private GeneratedCocktailOrder currentOrder;
        private OrderSessionRequest currentRequest;
        private VesselLiquidTracker servingTarget;
        private BusinessOrderSessionResult pendingResult;
        private Action<BusinessOrderSessionResult> completionCallback;
        private bool initialized;

        public BusinessOrderSessionState State { get; private set; } = BusinessOrderSessionState.Idle;
        public string CurrentRecipeName => currentOrder?.RequestedRecipeName ?? string.Empty;

        public event Action<BusinessOrderSessionState, BusinessOrderSessionState> StateChanged;
        public event Action<BusinessOrderSessionResult> OrderCompleted;

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

        public bool BeginOrder(BusinessSequenceEntrySnapshot entry)
        {
            return BeginOrder(OrderSessionRequest.ForBusiness(entry), null);
        }

        public bool BeginOrder(
            OrderSessionRequest request,
            Action<BusinessOrderSessionResult> onCompleted)
        {
            if (!initialized
                || request == null
                || string.IsNullOrWhiteSpace(request.sessionId)
                || string.IsNullOrWhiteSpace(request.requestedRecipeId))
                return false;

            if (State != BusinessOrderSessionState.Idle && State != BusinessOrderSessionState.Completed)
                return false;

            currentRequest = request;
            completionCallback = onCompleted;
            currentOrder = orderGenerator?.GenerateOrder(
                currentRequest.orderType,
                currentRequest.requestedRecipeId);
            pendingResult = null;
            servingTarget = null;

            if (currentOrder == null)
            {
                ui?.ShowError("요청한 레시피를 불러올 수 없습니다: " + currentRequest.requestedRecipeId);
                CompleteCurrentOrder(new BusinessOrderSessionResult
                {
                    outcome = OrderSessionOutcome.Failed,
                    customerOrderKey = currentRequest.customerOrderKey,
                    requestedRecipeId = currentRequest.requestedRecipeId,
                    accepted = false,
                    grade = OrderEvaluationGrade.Bad
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
            customerSpawner?.ShowVisit(
                currentRequest.customerVisitKey,
                currentRequest.customerOrderKey);

            if (customerSpawner == null || customerSpawner.CurrentOrderData == null)
            {
                ui?.ShowError("손님 주문 데이터를 불러올 수 없습니다: " + currentRequest.customerOrderKey);
                CompleteCurrentOrder(new BusinessOrderSessionResult
                {
                    outcome = OrderSessionOutcome.Failed,
                    customerOrderKey = currentRequest.customerOrderKey,
                    requestedRecipeId = currentRequest.requestedRecipeId,
                    accepted = false,
                    grade = OrderEvaluationGrade.Bad
                });
                return true;
            }

            return true;
        }

        private void BeginCrafting()
        {
            if (currentRequest == null)
                return;

            if (!string.IsNullOrWhiteSpace(currentRequest.ticketKey))
                ticketManager?.Prepare(currentRequest.ticketKey);

            SetState(BusinessOrderSessionState.Crafting);
            ui?.ShowCrafting(CurrentRecipeName);
            modeManager?.RequestModeChange(GameMode.CraftingMode);
            servingTarget = bartending != null ? bartending.CurrentTargetTracker : null;
        }

        private void SubmitOrder()
        {
            if (State != BusinessOrderSessionState.Crafting)
                return;

            servingTarget = servingTarget != null
                ? servingTarget
                : bartending != null ? bartending.CurrentTargetTracker : null;
            if (servingTarget == null)
            {
                ui?.ShowError("제출할 잔을 찾을 수 없습니다.");
                return;
            }

            SetState(BusinessOrderSessionState.Evaluating);
            ui?.ShowEvaluating();

            CocktailComposition composition = servingTarget.BuildComposition();
            CocktailOrderEvaluationResult evaluation = orderEvaluator?.Evaluate(currentOrder, composition);
            OrderEvaluationGrade grade = OrderEvaluationGrader.Resolve(evaluation, settings);
            pendingResult = new BusinessOrderSessionResult
            {
                outcome = OrderSessionOutcome.Served,
                customerOrderKey = currentRequest.customerOrderKey,
                requestedRecipeId = currentRequest.requestedRecipeId,
                accepted = true,
                grade = grade,
                moneyDelta = settings != null ? settings.GetMoneyReward(grade) : 0,
                reputationDelta = settings != null ? settings.GetReputationReward(grade) : 0,
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
            ui?.ShowFeedback(grade, pendingResult.moneyDelta, pendingResult.reputationDelta);

            bool feedbackStarted = customerSpawner != null && customerSpawner.ShowFeedback(grade);
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

        private void HandleBartendingSessionReady(VesselLiquidTracker tracker)
        {
            if (State == BusinessOrderSessionState.Crafting)
                servingTarget = tracker;
        }

        private void HandleBartendingSessionDestroyed()
        {
            servingTarget = null;
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
            if (result == null)
                return;

            OrderSessionRequest completedRequest = currentRequest;
            result.sessionId = completedRequest?.sessionId ?? result.sessionId;
            result.owner = completedRequest?.owner ?? result.owner;

            GameProgress progress = GameProgress.Instance;
            if (progress != null && completedRequest != null && completedRequest.applyProgressRewards)
            {
                progress.AddMoney(result.moneyDelta);
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
            OrderCompleted?.Invoke(result);
            callback?.Invoke(result);
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
