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
            builder.Append(isSuccess ? "주문 성공" : "주문 실패");
            builder.Append(" | ");
            builder.Append(order != null ? order.line : "진행 중인 주문 없음");
            builder.AppendLine();

            if (order != null)
            {
                builder.Append("요청 레시피: ");
                builder.Append(order.RequestedRecipeName);
                builder.Append(" (");
                builder.Append(CocktailOrderEvaluator.GetOrderTypeLabel(order.orderType));
                builder.AppendLine(")");
            }

            if (!string.IsNullOrWhiteSpace(failureReason))
                builder.AppendLine(failureReason);

            if (detectedRecipeResult != null)
            {
                builder.Append("감지된 레시피: ");
                builder.Append(GetRecipeLabel(detectedRecipeResult));
                builder.Append(" | ");
                builder.Append(detectedRecipeResult.isSuccess ? "레시피 일치" : "레시피 불일치");
                builder.Append(" | 점수 ");
                builder.Append((detectedRecipeResult.score * 100f).ToString("0.#"));
                builder.AppendLine("%");
            }

            if (requestedRecipeResult != null)
            {
                builder.AppendLine("요청 레시피 판정:");
                builder.Append(requestedRecipeResult.ToDebugString());
            }

            return builder.ToString();
        }

        private static string GetRecipeLabel(CocktailEvaluationResult result)
        {
            if (result == null || result.matchedRecipe == null)
                return "레시피 없음";

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
                    failureReason = "진행 중인 주문이 없습니다."
                };
            }

            switch (order.orderType)
            {
                case CocktailOrderType.RecipeOrder:
                case CocktailOrderType.RecipeModifierOrder:
                case CocktailOrderType.VariantRecipeOrder:
                case CocktailOrderType.CustomRecipeOrder:
                case CocktailOrderType.EpisodeOrder:
                    return EvaluateRecipeOrder(order, composition, detectedRecipeResult);
                case CocktailOrderType.TasteOrder:
                    return EvaluateTagOrder(order, detectedRecipeResult, true);
                case CocktailOrderType.MoodOrder:
                    return EvaluateTagOrder(order, detectedRecipeResult, false);
                default:
                    return new CocktailOrderEvaluationResult
                    {
                        order = order,
                        detectedRecipeResult = detectedRecipeResult,
                        isSuccess = false,
                        failureReason = $"'{GetOrderTypeLabel(order.orderType)}' 주문 유형은 아직 지원하지 않습니다."
                    };
            }
        }

        private static CocktailOrderEvaluationResult EvaluateTagOrder(
            GeneratedCocktailOrder order,
            CocktailEvaluationResult detectedRecipeResult,
            bool tasteOrder)
        {
            System.Collections.Generic.HashSet<string> required = tasteOrder
                ? order.requiredTasteTags
                : order.requiredMoodTags;
            System.Collections.Generic.HashSet<string> actual = tasteOrder
                ? detectedRecipeResult?.matchedRecipe?.tasteTags
                : detectedRecipeResult?.matchedRecipe?.moodTags;

            bool hasRequirement = required != null && required.Count > 0;
            bool tagsValid = hasRequirement && actual != null;
            if (tagsValid)
            {
                foreach (string tag in required)
                {
                    if (!actual.Contains(tag))
                    {
                        tagsValid = false;
                        break;
                    }
                }
            }

            bool success = detectedRecipeResult != null
                && detectedRecipeResult.isSuccess
                && tagsValid;
            string label = tasteOrder ? "맛" : "분위기";
            return new CocktailOrderEvaluationResult
            {
                order = order,
                detectedRecipeResult = detectedRecipeResult,
                requestedRecipeResult = detectedRecipeResult,
                isSuccess = success,
                failureReason = success
                    ? string.Empty
                    : !hasRequirement
                        ? $"이 주문에 {label} 태그가 설정되지 않았습니다."
                        : detectedRecipeResult == null || !detectedRecipeResult.isSuccess
                            ? "제출한 칵테일이 알려진 레시피와 일치하지 않습니다."
                            : $"제출한 칵테일이 요청한 {label} 조건과 일치하지 않습니다."
            };
        }

        private CocktailOrderEvaluationResult EvaluateRecipeOrder(
            GeneratedCocktailOrder order,
            CocktailComposition composition,
            CocktailEvaluationResult detectedRecipeResult)
        {
            CocktailEvaluationResult requestedRecipeResult = cocktailEvaluator != null
                ? cocktailEvaluator.EvaluateRecipeFamily(order.requestedRecipeId, composition)
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
            string requestedName = order != null ? order.RequestedRecipeName : "요청 레시피";

            if (detectedRecipeResult != null
                && detectedRecipeResult.isSuccess
                && detectedRecipeResult.matchedRecipe != null
                && order != null
                && !string.Equals(detectedRecipeResult.matchedRecipe.id, order.requestedRecipeId, System.StringComparison.OrdinalIgnoreCase))
            {
                return $"{GetRecipeLabel(detectedRecipeResult)}을 제출했지만 주문은 {requestedName}입니다.";
            }

            if (requestedRecipeResult != null && !string.IsNullOrWhiteSpace(requestedRecipeResult.failureReason))
                return $"{requestedName} 조건을 만족하지 않습니다: {requestedRecipeResult.failureReason}.";

            return $"{requestedName} 조건을 만족하지 않습니다.";
        }

        private static string GetRecipeLabel(CocktailEvaluationResult result)
        {
            if (result == null || result.matchedRecipe == null)
                return "레시피 없음";

            if (!string.IsNullOrWhiteSpace(result.matchedRecipe.displayName))
                return result.matchedRecipe.displayName;

            return result.matchedRecipe.id;
        }

        internal static string GetOrderTypeLabel(CocktailOrderType orderType)
        {
            return orderType switch
            {
                CocktailOrderType.RecipeOrder => "레시피 주문",
                CocktailOrderType.RecipeModifierOrder => "조건 변경 주문",
                CocktailOrderType.TasteOrder => "맛 주문",
                CocktailOrderType.MoodOrder => "분위기 주문",
                CocktailOrderType.VariantRecipeOrder => "변형 레시피 주문",
                CocktailOrderType.CustomRecipeOrder => "수제 레시피 주문",
                CocktailOrderType.EpisodeOrder => "에피소드 주문",
                _ => "알 수 없는 주문"
            };
        }
    }
}
