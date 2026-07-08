using System.Text;

namespace Slainte.Bartending
{
    public sealed class CocktailOrderEvaluationResult
    {
        public GeneratedCocktailOrder order;
        public CocktailEvaluationResult detectedRecipeResult;
        public CocktailEvaluationResult requestedRecipeResult;
        public bool isSuccess;
        public string failureReason;

        public string ToDebugString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(isSuccess ? "ORDER GOOD" : "ORDER BAD");
            builder.Append(" | ");
            builder.Append(order != null ? order.line : "No Order");
            builder.AppendLine();

            if (order != null)
            {
                builder.Append("Requested: ");
                builder.Append(order.RequestedRecipeName);
                builder.Append(" (");
                builder.Append(order.orderType);
                builder.AppendLine(")");
            }

            if (!string.IsNullOrWhiteSpace(failureReason))
                builder.AppendLine(failureReason);

            if (detectedRecipeResult != null)
            {
                builder.Append("Detected: ");
                builder.Append(GetRecipeLabel(detectedRecipeResult));
                builder.Append(" | ");
                builder.Append(detectedRecipeResult.isSuccess ? "recipe good" : "recipe bad");
                builder.Append(" | score ");
                builder.Append((detectedRecipeResult.score * 100f).ToString("0.#"));
                builder.AppendLine("%");
            }

            if (requestedRecipeResult != null)
            {
                builder.AppendLine("Requested recipe check:");
                builder.Append(requestedRecipeResult.ToDebugString());
            }

            return builder.ToString();
        }

        private static string GetRecipeLabel(CocktailEvaluationResult result)
        {
            if (result == null || result.matchedRecipe == null)
                return "No Recipe";

            if (!string.IsNullOrWhiteSpace(result.matchedRecipe.displayName))
                return result.matchedRecipe.displayName;

            return result.matchedRecipe.id;
        }
    }

    public sealed class CocktailOrderEvaluator
    {
        private readonly CocktailEvaluator cocktailEvaluator;

        public CocktailOrderEvaluator(CocktailEvaluator cocktailEvaluator)
        {
            this.cocktailEvaluator = cocktailEvaluator;
        }

        public CocktailOrderEvaluationResult Evaluate(GeneratedCocktailOrder order, CocktailComposition composition)
        {
            CocktailEvaluationResult detectedRecipeResult = cocktailEvaluator != null
                ? cocktailEvaluator.Evaluate(composition)
                : null;

            return Evaluate(order, composition, detectedRecipeResult);
        }

        public CocktailOrderEvaluationResult Evaluate(
            GeneratedCocktailOrder order,
            CocktailComposition composition,
            CocktailEvaluationResult detectedRecipeResult)
        {
            if (order == null)
            {
                return new CocktailOrderEvaluationResult
                {
                    order = null,
                    detectedRecipeResult = detectedRecipeResult,
                    isSuccess = false,
                    failureReason = "No active order."
                };
            }

            switch (order.orderType)
            {
                case CocktailOrderType.RecipeOrder:
                    return EvaluateRecipeOrder(order, composition, detectedRecipeResult);
                default:
                    return new CocktailOrderEvaluationResult
                    {
                        order = order,
                        detectedRecipeResult = detectedRecipeResult,
                        isSuccess = false,
                        failureReason = $"Order type '{order.orderType}' is not implemented yet."
                    };
            }
        }

        private CocktailOrderEvaluationResult EvaluateRecipeOrder(
            GeneratedCocktailOrder order,
            CocktailComposition composition,
            CocktailEvaluationResult detectedRecipeResult)
        {
            CocktailEvaluationResult requestedRecipeResult = cocktailEvaluator != null
                ? cocktailEvaluator.EvaluateRecipe(order.requestedRecipeId, composition)
                : null;

            bool success = requestedRecipeResult != null && requestedRecipeResult.isSuccess;
            return new CocktailOrderEvaluationResult
            {
                order = order,
                detectedRecipeResult = detectedRecipeResult,
                requestedRecipeResult = requestedRecipeResult,
                isSuccess = success,
                failureReason = success
                    ? string.Empty
                    : BuildRecipeOrderFailure(order, detectedRecipeResult, requestedRecipeResult)
            };
        }

        private static string BuildRecipeOrderFailure(
            GeneratedCocktailOrder order,
            CocktailEvaluationResult detectedRecipeResult,
            CocktailEvaluationResult requestedRecipeResult)
        {
            string requestedName = order != null ? order.RequestedRecipeName : "requested recipe";

            if (detectedRecipeResult != null
                && detectedRecipeResult.isSuccess
                && detectedRecipeResult.matchedRecipe != null
                && order != null
                && !string.Equals(detectedRecipeResult.matchedRecipe.id, order.requestedRecipeId, System.StringComparison.OrdinalIgnoreCase))
            {
                return $"Submitted {GetRecipeLabel(detectedRecipeResult)}, but order requested {requestedName}.";
            }

            if (requestedRecipeResult != null && !string.IsNullOrWhiteSpace(requestedRecipeResult.failureReason))
                return $"Does not satisfy {requestedName}: {requestedRecipeResult.failureReason}.";

            return $"Does not satisfy {requestedName}.";
        }

        private static string GetRecipeLabel(CocktailEvaluationResult result)
        {
            if (result == null || result.matchedRecipe == null)
                return "No Recipe";

            if (!string.IsNullOrWhiteSpace(result.matchedRecipe.displayName))
                return result.matchedRecipe.displayName;

            return result.matchedRecipe.id;
        }
    }
}
