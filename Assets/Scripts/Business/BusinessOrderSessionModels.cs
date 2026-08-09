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
        Failed
    }

    public enum BusinessOrderSessionState
    {
        Idle,
        PresentingOrder,
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
        public string customerVisitKey;
        public string requestedRecipeId;
        public string ticketKey;
        public CocktailOrderType orderType = CocktailOrderType.RecipeOrder;
        public bool presentOrder = true;
        public bool presentFeedback = true;
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
                customerVisitKey = entry.visitKey,
                requestedRecipeId = entry.contentId,
                ticketKey = entry.entryId,
                orderType = System.Enum.IsDefined(typeof(CocktailOrderType), entry.orderType)
                    ? (CocktailOrderType)entry.orderType
                    : CocktailOrderType.RecipeOrder,
                presentOrder = true,
                presentFeedback = true,
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

            if (result != null
                && result.isSuccess
                && result.requestedRecipeResult?.matchedRecipe != null
                && result.requestedRecipeResult.matchedRecipe.evaluationGrade
                    == CocktailRecipeEvaluationGrade.Mid)
                return OrderEvaluationGrade.Mid;

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
        public OrderEvaluationGrade grade;
        public int moneyDelta;
        public int reputationDelta;
        public CocktailOrderEvaluationResult evaluation;
    }
}
