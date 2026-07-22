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
        private BusinessSequenceEntrySnapshot currentEntry;
        private VesselLiquidTracker servingTarget;
        private BusinessOrderSessionResult pendingResult;
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
            if (!initialized || entry == null || string.IsNullOrWhiteSpace(entry.entryId))
                return false;

            if (State != BusinessOrderSessionState.Idle && State != BusinessOrderSessionState.Completed)
                return false;

            currentEntry = entry.Clone();
            currentOrder = orderGenerator?.GenerateRecipeOrder(currentEntry.contentId);
            pendingResult = null;
            servingTarget = null;

            if (currentOrder == null)
            {
                ui?.ShowError("The requested recipe could not be loaded: " + currentEntry.contentId);
                CompleteCurrentOrder(new BusinessOrderSessionResult
                {
                    customerOrderKey = currentEntry.entryId,
                    requestedRecipeId = currentEntry.contentId,
                    accepted = false,
                    grade = OrderEvaluationGrade.Bad
                });
                return false;
            }

            SetState(BusinessOrderSessionState.PresentingOrder);
            ui?.ShowPresentingOrder(currentEntry.entryId);
            customerSpawner?.ShowCustomers(new[] { currentEntry.entryId });

            if (customerSpawner == null || customerSpawner.CurrentOrderData == null)
            {
                ui?.ShowError("Customer order data could not be loaded: " + currentEntry.entryId);
                CompleteCurrentOrder(new BusinessOrderSessionResult
                {
                    customerOrderKey = currentEntry.entryId,
                    requestedRecipeId = currentEntry.contentId,
                    accepted = false,
                    grade = OrderEvaluationGrade.Bad
                });
                return false;
            }

            return true;
        }

        public void AcceptOrder()
        {
            if (State != BusinessOrderSessionState.AwaitingDecision)
                return;

            SetState(BusinessOrderSessionState.Crafting);
            ui?.ShowCrafting(CurrentRecipeName);
            modeManager?.RequestModeChange(GameMode.CraftingMode);
            servingTarget = bartending != null ? bartending.CurrentTargetTracker : null;
        }

        public void RejectOrder()
        {
            if (State != BusinessOrderSessionState.AwaitingDecision)
                return;

            ticketManager?.ClearTicket();
            CompleteCurrentOrder(new BusinessOrderSessionResult
            {
                customerOrderKey = currentEntry.entryId,
                requestedRecipeId = currentEntry.contentId,
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
            ui?.ShowCrafting(CurrentRecipeName);
        }

        public void ConfirmAbandonOrder()
        {
            if (State != BusinessOrderSessionState.Crafting)
                return;

            modeManager?.RequestModeChange(GameMode.OrderMode);
            ticketManager?.ClearTicket();
            CompleteCurrentOrder(new BusinessOrderSessionResult
            {
                customerOrderKey = currentEntry.entryId,
                requestedRecipeId = currentEntry.contentId,
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
                customerOrderKey = currentEntry.entryId,
                requestedRecipeId = currentEntry.contentId,
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
                ui?.ShowDecision();
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

            GameProgress progress = GameProgress.Instance;
            if (progress != null)
            {
                progress.AddMoney(result.moneyDelta);
                progress.AddReputation(result.reputationDelta);
            }

            customerSpawner?.Clear();
            ticketManager?.ClearTicket();
            SetState(BusinessOrderSessionState.Completed);
            OrderCompleted?.Invoke(result);
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
