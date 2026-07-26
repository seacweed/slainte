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
        public bool CanAbandonCurrentOrder => currentRequest != null && currentRequest.allowAbandon;

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
            currentOrder = orderGenerator?.GenerateRecipeOrder(currentRequest.requestedRecipeId);
            pendingResult = null;
            servingTarget = null;

            if (currentOrder == null)
            {
                ui?.ShowError("The requested recipe could not be loaded: " + currentRequest.requestedRecipeId);
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
            customerSpawner?.ShowCustomers(new[] { currentRequest.customerOrderKey });

            if (customerSpawner == null || customerSpawner.CurrentOrderData == null)
            {
                ui?.ShowError("Customer order data could not be loaded: " + currentRequest.customerOrderKey);
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

        public void AcceptOrder()
        {
            if (State != BusinessOrderSessionState.AwaitingDecision)
                return;

            BeginCrafting();
        }

        private void BeginCrafting()
        {
            if (currentRequest == null)
                return;

            if (!string.IsNullOrWhiteSpace(currentRequest.ticketKey))
                ticketManager?.Prepare(currentRequest.ticketKey);

            SetState(BusinessOrderSessionState.Crafting);
            ui?.ShowCrafting(CurrentRecipeName, currentRequest.allowAbandon);
            modeManager?.RequestModeChange(GameMode.CraftingMode);
            servingTarget = bartending != null ? bartending.CurrentTargetTracker : null;
        }

        public void RejectOrder()
        {
            if (State != BusinessOrderSessionState.AwaitingDecision
                || currentRequest == null
                || !currentRequest.allowReject)
                return;

            ticketManager?.ClearTicket();
            CompleteCurrentOrder(new BusinessOrderSessionResult
            {
                outcome = OrderSessionOutcome.Rejected,
                customerOrderKey = currentRequest.customerOrderKey,
                requestedRecipeId = currentRequest.requestedRecipeId,
                accepted = false,
                grade = OrderEvaluationGrade.Bad
            });
        }

        public void DiscardCocktail()
        {
            if (State != BusinessOrderSessionState.Crafting)
                return;

            servingTarget = null;
            bartending?.DiscardAndResetSession();
            ui?.ShowCrafting(CurrentRecipeName, CanAbandonCurrentOrder);
        }

        public void ConfirmAbandonOrder()
        {
            if (State != BusinessOrderSessionState.Crafting
                || currentRequest == null
                || !currentRequest.allowAbandon)
                return;

            modeManager?.RequestModeChange(GameMode.OrderMode);
            ticketManager?.ClearTicket();
            CompleteCurrentOrder(new BusinessOrderSessionResult
            {
                outcome = OrderSessionOutcome.Abandoned,
                customerOrderKey = currentRequest.customerOrderKey,
                requestedRecipeId = currentRequest.requestedRecipeId,
                accepted = true,
                abandoned = true,
                grade = OrderEvaluationGrade.Bad,
                reputationDelta = settings != null ? settings.abandonReputationReward : 0
            });
        }

        public void SubmitOrder()
        {
            if (State != BusinessOrderSessionState.Crafting)
                return;

            servingTarget = servingTarget != null
                ? servingTarget
                : bartending != null ? bartending.CurrentTargetTracker : null;
            if (servingTarget == null)
            {
                ui?.ShowError("No serving glass is available.");
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

            Debug.Log("[BusinessOrderSession] " + (evaluation != null
                ? evaluation.ToDebugString()
                : "Evaluation service is unavailable."));

            modeManager?.RequestModeChange(GameMode.OrderMode);
            ticketManager?.ClearTicket();
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
            CocktailRecipeCatalog recipeCatalog = CocktailRecipeCsvLoader.LoadFromStreamingAssets(
                itemCatalog,
                "Data",
                "recipes.csv",
                "recipe_ingredients.csv");
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
                SetState(BusinessOrderSessionState.AwaitingDecision);
                ui?.ShowDecision(currentRequest == null || currentRequest.allowReject);
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
