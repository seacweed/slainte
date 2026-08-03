using Slainte.Bartending;

namespace Slainte.Business
{
    public enum OrderSessionOwner
    {
        Business,
        Episode
    }

    public enum OrderSessionOutcome
    {
        Served,
        Rejected,
        Abandoned,
        Failed
    }

    public enum BusinessOrderSessionState
    {
        Idle,
        PresentingOrder,
        AwaitingDecision,
        Crafting,
        Evaluating,
        PresentingFeedback,
        Completed
    }

    public sealed class OrderSessionRequest
    {
        public string sessionId;
        public OrderSessionOwner owner;
        public string customerOrderKey;
        public string requestedRecipeId;
        public string ticketKey;
        public CocktailOrderType orderType = CocktailOrderType.RecipeOrder;
        public bool presentOrder = true;
        public bool presentFeedback = true;
        public bool allowReject = true;
        public bool allowAbandon = true;
        public bool applyProgressRewards = true;
        public bool clearCustomerOnComplete = true;

        public static OrderSessionRequest ForBusiness(BusinessSequenceEntrySnapshot entry)
        {
            if (entry == null)
                return null;

            return new OrderSessionRequest
            {
                sessionId = entry.entryId,
                owner = OrderSessionOwner.Business,
                customerOrderKey = entry.entryId,
                requestedRecipeId = entry.contentId,
                ticketKey = entry.entryId,
                orderType = CocktailOrderType.RecipeOrder,
                presentOrder = true,
                presentFeedback = true,
                allowReject = true,
                allowAbandon = true,
                applyProgressRewards = true,
                clearCustomerOnComplete = true
            };
        }
    }

    public enum OrderEvaluationGrade
    {
        Bad,
        Mid,
        Good
    }

    public static class OrderEvaluationGrader
    {
        public static OrderEvaluationGrade Resolve(
            CocktailOrderEvaluationResult result,
            BusinessOrderFlowSettings settings)
        {
            float score = result?.requestedRecipeResult != null
                ? result.requestedRecipeResult.score
                : result?.detectedRecipeResult != null ? result.detectedRecipeResult.score : 0f;
            float goodThreshold = settings != null ? settings.goodScoreThreshold : 0.8f;
            float midThreshold = settings != null ? settings.midScoreThreshold : 0.45f;

            if (result != null && result.isSuccess && score >= goodThreshold)
                return OrderEvaluationGrade.Good;

            return score >= midThreshold ? OrderEvaluationGrade.Mid : OrderEvaluationGrade.Bad;
        }
    }

    public sealed class BusinessOrderSessionResult
    {
        public string sessionId;
        public OrderSessionOwner owner;
        public OrderSessionOutcome outcome;
        public string customerOrderKey;
        public string requestedRecipeId;
        public bool accepted;
        public bool abandoned;
        public OrderEvaluationGrade grade;
        public int moneyDelta;
        public int reputationDelta;
        public CocktailOrderEvaluationResult evaluation;
    }
}
