using Slainte.Bartending;

namespace Slainte.Business
{
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
