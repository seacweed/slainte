using System.Text;

namespace Slainte.Bartending
{
    public enum CocktailOrderEvaluationOutcome
    {
        Bad,
        MidWrongMenu,
        MidIce,
        MidGlass,
        MidIceGlass,
        Good
    }

    public sealed class CocktailOrderEvaluationResult
    {
        public GeneratedCocktailOrder order;
        public CocktailEvaluationResult detectedRecipeResult;
        public CocktailEvaluationResult requestedRecipeResult;
        public CocktailOrderEvaluationOutcome outcome = CocktailOrderEvaluationOutcome.Bad;
        public bool isSuccess;
        public string failureReason;

        public string ToDebugString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(isSuccess ? "주문 성공" : "주문 실패");
            builder.Append(" | ");
            builder.Append(outcome);
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
                builder.AppendLine();
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

    // CocktailEvaluator의 단순 성공/실패 판정을 "이번 주문" 맥락에서 Good/MidGlass/MidIce/
    // MidIceGlass/MidWrongMenu/Bad 여섯 등급으로 재분류한다(HANDOFF.md 문서에 정리된 등급 정의와
    // 동일). 등급별 파생 레시피 에셋은 없으며, 이 클래스가 요청 레시피 대비 판정 결과만으로
    // 등급을 계산한다(ClassifyRequestedOutcome).
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
                case CocktailOrderType.CustomRecipeOrder:
                case CocktailOrderType.EpisodeOrder:
                    return EvaluateRecipeOrder(order, composition, detectedRecipeResult);
                case CocktailOrderType.TasteOrder:
                    return EvaluateTagOrder(order, composition, detectedRecipeResult, true);
                case CocktailOrderType.MoodOrder:
                    return EvaluateTagOrder(order, composition, detectedRecipeResult, false);
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

        private CocktailOrderEvaluationResult EvaluateTagOrder(
            GeneratedCocktailOrder order,
            CocktailComposition composition,
            CocktailEvaluationResult detectedRecipeResult,
            bool tasteOrder)
        {
            System.Collections.Generic.HashSet<string> required = tasteOrder
                ? order.requiredTasteTags
                : order.requiredMoodTags;
            bool hasRequirement = required != null && required.Count > 0;
            CocktailEvaluationResult requestedResult = null;
            if (hasRequirement && cocktailEvaluator != null && composition != null)
            {
                requestedResult = cocktailEvaluator.EvaluateFirstOrderable(
                    composition,
                    recipe => HasRequiredTags(recipe, required, tasteOrder),
                    true);
            }
            else if (hasRequirement
                && detectedRecipeResult != null
                && HasRequiredTags(detectedRecipeResult.matchedRecipe, required, tasteOrder))
            {
                requestedResult = detectedRecipeResult;
            }

            CocktailOrderEvaluationOutcome outcome = hasRequirement
                ? ClassifyRequestedOutcome(requestedResult)
                : CocktailOrderEvaluationOutcome.Bad;
            if (outcome == CocktailOrderEvaluationOutcome.Bad
                && detectedRecipeResult != null
                && detectedRecipeResult.isSuccess)
            {
                outcome = CocktailOrderEvaluationOutcome.MidWrongMenu;
            }

            bool success = IsRequestedOrderSuccess(outcome);
            string label = tasteOrder ? "맛" : "분위기";
            return new CocktailOrderEvaluationResult
            {
                order = order,
                detectedRecipeResult = detectedRecipeResult,
                requestedRecipeResult = requestedResult ?? detectedRecipeResult,
                outcome = outcome,
                isSuccess = success,
                failureReason = success
                    ? string.Empty
                    : !hasRequirement
                        ? $"이 주문에 {label} 태그가 설정되지 않았습니다."
                        : outcome == CocktailOrderEvaluationOutcome.MidWrongMenu
                            ? $"제출한 칵테일이 요청한 {label} 조건과 일치하지 않습니다."
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
                ? cocktailEvaluator.EvaluateRecipe(order.requestedRecipeId, composition)
                : null;

            // 요청 레시피 기준으로는 Bad(요청 레시피 자체가 안 맞음)여도, 조성이 요청과 다른
            // "다른 기본 레시피"를 정확히 만족한다면 완전 실패가 아니라 MidWrongMenu로 승격한다.
            CocktailOrderEvaluationOutcome outcome = ClassifyRequestedOutcome(requestedRecipeResult);
            if (outcome == CocktailOrderEvaluationOutcome.Bad
                && IsDifferentDetectedMenu(order, detectedRecipeResult))
            {
                outcome = CocktailOrderEvaluationOutcome.MidWrongMenu;
            }

            bool success = IsRequestedOrderSuccess(outcome);
            return new CocktailOrderEvaluationResult
            {
                order = order,
                detectedRecipeResult = detectedRecipeResult,
                requestedRecipeResult = requestedRecipeResult,
                outcome = outcome,
                isSuccess = success,
                failureReason = success
                    ? string.Empty
                    : BuildRecipeOrderFailure(order, detectedRecipeResult, requestedRecipeResult)
            };
        }

        // coreValid(배합·기법·추가재료 조건)가 이미 깨졌다면 잔/얼음을 볼 것도 없이 Bad다.
        // coreValid인데 잔/얼음만 어긋난 경우에만 Mid* 등급을 주되, 제출한 잔 자체가 알려진
        // 잔 종류가 아니면(오타·미등록 glassId) "부분 성공"으로 인정하지 않고 Bad로 취급한다.
        private CocktailOrderEvaluationOutcome ClassifyRequestedOutcome(
            CocktailEvaluationResult result)
        {
            if (result == null || result.matchedRecipe == null)
                return CocktailOrderEvaluationOutcome.Bad;
            if (result.isSuccess)
                return CocktailOrderEvaluationOutcome.Good;
            if (!result.coreValid)
                return CocktailOrderEvaluationOutcome.Bad;

            bool glassInvalid = !result.glassValid;
            bool iceInvalid = !result.iceValid;
            if (glassInvalid
                && (cocktailEvaluator == null
                    || !cocktailEvaluator.IsKnownGlass(result.actualGlassId)))
            {
                return CocktailOrderEvaluationOutcome.Bad;
            }

            if (glassInvalid && iceInvalid)
                return CocktailOrderEvaluationOutcome.MidIceGlass;
            if (iceInvalid)
                return CocktailOrderEvaluationOutcome.MidIce;
            if (glassInvalid)
                return CocktailOrderEvaluationOutcome.MidGlass;
            return CocktailOrderEvaluationOutcome.Bad;
        }

        private static bool HasRequiredTags(
            CocktailRecipe recipe,
            System.Collections.Generic.HashSet<string> required,
            bool tasteOrder)
        {
            if (recipe == null || required == null || required.Count == 0)
                return false;

            System.Collections.Generic.HashSet<string> actual = tasteOrder
                ? recipe.tasteTags
                : recipe.moodTags;
            if (actual == null)
                return false;

            foreach (string tag in required)
            {
                if (!actual.Contains(tag))
                    return false;
            }

            return true;
        }

        private static bool IsDifferentDetectedMenu(
            GeneratedCocktailOrder order,
            CocktailEvaluationResult detectedRecipeResult)
        {
            if (order == null
                || detectedRecipeResult == null
                || !detectedRecipeResult.isSuccess
                || detectedRecipeResult.matchedRecipe == null)
            {
                return false;
            }

            CocktailRecipe detected = detectedRecipeResult.matchedRecipe;
            return !string.Equals(
                    detected.id,
                    order.requestedRecipeId,
                    System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRequestedOrderSuccess(CocktailOrderEvaluationOutcome outcome)
        {
            return outcome == CocktailOrderEvaluationOutcome.Good
                || outcome == CocktailOrderEvaluationOutcome.MidIce
                || outcome == CocktailOrderEvaluationOutcome.MidGlass
                || outcome == CocktailOrderEvaluationOutcome.MidIceGlass;
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
                CocktailOrderType.CustomRecipeOrder => "수제 레시피 주문",
                CocktailOrderType.EpisodeOrder => "에피소드 주문",
                _ => "알 수 없는 주문"
            };
        }
    }
}
