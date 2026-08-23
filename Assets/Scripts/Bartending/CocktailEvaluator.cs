using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Slainte.Bartending
{
    public sealed class CocktailIngredientEvaluation
    {
        public CocktailRecipeIngredient recipeIngredient;
        public float actualMl;
        public float deltaMl;
        public bool isWithinTolerance;
        public bool isResolved;
    }

    public sealed class CocktailExtraIngredient
    {
        public ItemDef item;
        public float volumeMl;
    }

    public sealed class CocktailEvaluationResult
    {
        public CocktailRecipe matchedRecipe;
        public bool isSuccess;
        public float actualTotalMl;
        public string actualGlassId = string.Empty;
        public int actualIceCount;
        public Color finalColor = Color.clear;
        public string failureReason;
        public bool ingredientsValid;
        public bool extrasValid;
        public bool totalValid;
        public bool hasUnresolvedIngredient;
        public bool glassValid = true;
        public bool iceValid = true;
        public bool shakeIceValid = true;
        public bool techniqueValid = true;
        public readonly List<CocktailIngredientEvaluation> ingredients = new();
        public readonly List<CocktailExtraIngredient> extraIngredients = new();

        public bool coreValid => ingredientsValid
            && extrasValid
            && totalValid
            && shakeIceValid
            && techniqueValid
            && !hasUnresolvedIngredient;

        public string ToDebugString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(isSuccess ? "레시피 일치" : "레시피 불일치");
            builder.Append(" | ");
            builder.Append(matchedRecipe != null ? matchedRecipe.displayName : "레시피 없음");
            builder.Append(" | 총량 ");
            builder.Append(actualTotalMl.ToString("0.##"));
            builder.AppendLine(" ml");
            builder.Append("최종 색상: #");
            builder.Append(ColorUtility.ToHtmlStringRGBA(finalColor));
            builder.Append(" | RGBA(");
            builder.Append(finalColor.r.ToString("0.###"));
            builder.Append(", ");
            builder.Append(finalColor.g.ToString("0.###"));
            builder.Append(", ");
            builder.Append(finalColor.b.ToString("0.###"));
            builder.Append(", ");
            builder.Append(finalColor.a.ToString("0.###"));
            builder.AppendLine(")");

            if (!string.IsNullOrWhiteSpace(failureReason))
                builder.AppendLine(failureReason);

            for (int i = 0; i < ingredients.Count; i++)
            {
                CocktailIngredientEvaluation ingredient = ingredients[i];
                builder.Append("- ");
                builder.Append(ingredient.recipeIngredient != null
                    ? ingredient.recipeIngredient.item != null
                        ? GetItemLabel(ingredient.recipeIngredient.item)
                        : "알 수 없는 재료(" + ingredient.recipeIngredient.ingredientId + ")"
                    : "알 수 없는 재료");
                builder.Append(": ");
                builder.Append(ingredient.actualMl.ToString("0.##"));
                builder.Append(" / ");
                builder.Append(ingredient.recipeIngredient != null
                    ? ingredient.recipeIngredient.targetMl.ToString("0.##")
                    : "0");
                builder.Append(" ml");
                if (!ingredient.isWithinTolerance)
                    builder.Append(" (허용 오차 초과)");
                builder.AppendLine();
            }

            for (int i = 0; i < extraIngredients.Count; i++)
            {
                builder.Append("- 추가 재료 ");
                builder.Append(GetItemLabel(extraIngredients[i].item));
                builder.Append(": ");
                builder.Append(extraIngredients[i].volumeMl.ToString("0.##"));
                builder.AppendLine(" ml");
            }

            return builder.ToString();
        }

        private static string GetItemLabel(ItemDef item)
        {
            if (item == null)
                return "알 수 없음";

            if (!string.IsNullOrWhiteSpace(item.displayName))
                return item.displayName;

            if (!string.IsNullOrWhiteSpace(item.id))
                return item.id;

            return item.name;
        }
    }

    public sealed class CocktailEvaluator
    {
        private readonly CocktailRecipeCatalog recipeCatalog;

        public CocktailEvaluator(CocktailRecipeCatalog recipeCatalog)
        {
            this.recipeCatalog = recipeCatalog;
        }

        public CocktailEvaluationResult Evaluate(CocktailComposition composition)
        {
            if (recipeCatalog == null || recipeCatalog.Count == 0)
                return CreateNoRecipeResult(composition, "불러온 레시피가 없습니다.");

            foreach (CocktailRecipe recipe in recipeCatalog.Recipes)
            {
                if (!IsDetectableBaseRecipe(recipe))
                    continue;

                CocktailEvaluationResult result = EvaluateRecipe(recipe, composition);
                if (result.isSuccess)
                    return result;
            }

            return CreateNoRecipeResult(composition, "일치하는 레시피가 없습니다.");
        }

        public CocktailEvaluationResult EvaluateRecipe(string recipeId, CocktailComposition composition)
        {
            if (recipeCatalog == null || recipeCatalog.Count == 0)
                return CreateNoRecipeResult(composition, "불러온 레시피가 없습니다.");

            if (!recipeCatalog.TryGet(recipeId, out CocktailRecipe recipe))
                return CreateNoRecipeResult(composition, $"'{recipeId}' 레시피를 불러오지 못했습니다.");

            return EvaluateRecipe(recipe, composition);
        }

        public CocktailEvaluationResult EvaluateRecipeFamily(string recipeId, CocktailComposition composition)
        {
            CocktailEvaluationResult requested = EvaluateRecipe(recipeId, composition);
            if (requested.isSuccess || recipeCatalog == null)
                return requested;

            foreach (CocktailRecipe candidate in recipeCatalog.Recipes)
            {
                if (candidate == null
                    || candidate.evaluationGrade != CocktailRecipeEvaluationGrade.Mid
                    || !string.Equals(candidate.baseRecipeId, recipeId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                CocktailEvaluationResult result = EvaluateRecipe(candidate, composition);
                if (result.isSuccess)
                    return result;
            }

            return requested;
        }

        public CocktailEvaluationResult EvaluateFirstOrderable(
            CocktailComposition composition,
            System.Predicate<CocktailRecipe> predicate,
            bool allowServingStyleMismatch)
        {
            if (recipeCatalog == null || recipeCatalog.Count == 0)
                return CreateNoRecipeResult(composition, "불러온 레시피가 없습니다.");

            CocktailEvaluationResult servingStyleMismatch = null;
            foreach (CocktailRecipe recipe in recipeCatalog.Recipes)
            {
                if (!IsDetectableBaseRecipe(recipe)
                    || (predicate != null && !predicate(recipe)))
                    continue;

                CocktailEvaluationResult result = EvaluateRecipe(recipe, composition);
                if (result.isSuccess)
                    return result;
                if (allowServingStyleMismatch
                    && servingStyleMismatch == null
                    && result.coreValid
                    && (!result.glassValid || !result.iceValid))
                {
                    servingStyleMismatch = result;
                }
            }

            return servingStyleMismatch
                ?? CreateNoRecipeResult(composition, "일치하는 레시피가 없습니다.");
        }

        public bool IsKnownGlass(string glassId)
        {
            if (string.IsNullOrWhiteSpace(glassId) || recipeCatalog == null)
                return false;

            string normalized = glassId.Trim();
            if (string.Equals(normalized, "rock", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "highball", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "hurricane", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "martini", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (CocktailRecipe recipe in recipeCatalog.Recipes)
            {
                if (recipe != null
                    && string.Equals(
                        recipe.glassId,
                        normalized,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDetectableBaseRecipe(CocktailRecipe recipe)
        {
            return recipe != null
                && recipe.isOrderable
                && recipe.evaluationGrade == CocktailRecipeEvaluationGrade.Good
                && string.IsNullOrWhiteSpace(recipe.baseRecipeId);
        }

        private static CocktailEvaluationResult EvaluateRecipe(CocktailRecipe recipe, CocktailComposition composition)
        {
            CocktailEvaluationResult result = new CocktailEvaluationResult
            {
                matchedRecipe = recipe,
                actualTotalMl = composition != null ? composition.TotalVolumeMl : 0f,
                actualGlassId = composition != null ? composition.GlassId : string.Empty,
                actualIceCount = composition != null ? composition.IceCount : 0,
                finalColor = composition != null ? composition.EvaluateFinalColor() : Color.clear
            };

            HashSet<ItemDef> recipeItems = new HashSet<ItemDef>();
            bool allIngredientsValid = recipe.ingredients.Count > 0;
            bool hasUnresolvedIngredient = false;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                CocktailRecipeIngredient recipeIngredient = recipe.ingredients[i];
                float actualMl = recipeIngredient.item != null && composition != null
                    ? composition.GetVolume(recipeIngredient.item)
                    : 0f;
                float deltaMl = actualMl - recipeIngredient.targetMl;
                float toleranceMl = Mathf.Max(0f, recipeIngredient.toleranceMl);
                bool resolved = recipeIngredient.item != null;
                bool withinTolerance = resolved && Mathf.Abs(deltaMl) <= toleranceMl;

                if (!resolved)
                    hasUnresolvedIngredient = true;

                if (!withinTolerance)
                    allIngredientsValid = false;

                if (recipeIngredient.item != null)
                    recipeItems.Add(recipeIngredient.item);

                result.ingredients.Add(new CocktailIngredientEvaluation
                {
                    recipeIngredient = recipeIngredient,
                    actualMl = actualMl,
                    deltaMl = deltaMl,
                    isWithinTolerance = withinTolerance,
                    isResolved = resolved
                });
            }

            float extraVolume = CollectExtras(recipe, composition, recipeItems, result);
            bool extrasValid = recipe.allowExtraIngredients || extraVolume <= 0.0001f;
            bool totalValid = IsTotalWithinRange(recipe, result.actualTotalMl);
            bool glassValid = IsGlassValid(recipe, composition);
            bool iceValid = IsIceValid(recipe, composition);
            bool shakeIceValid = IsShakeIceValid(recipe, composition);
            bool techniqueValid = IsTechniqueValid(recipe, composition);

            result.ingredientsValid = allIngredientsValid;
            result.extrasValid = extrasValid;
            result.totalValid = totalValid;
            result.hasUnresolvedIngredient = hasUnresolvedIngredient;
            result.glassValid = glassValid;
            result.iceValid = iceValid;
            result.shakeIceValid = shakeIceValid;
            result.techniqueValid = techniqueValid;
            result.isSuccess = allIngredientsValid
                && extrasValid
                && totalValid
                && glassValid
                && iceValid
                && shakeIceValid
                && techniqueValid
                && !hasUnresolvedIngredient;
            result.failureReason = BuildFailureReason(
                hasUnresolvedIngredient,
                allIngredientsValid,
                extrasValid,
                totalValid,
                glassValid,
                iceValid,
                shakeIceValid,
                techniqueValid);
            return result;
        }

        private static bool IsGlassValid(CocktailRecipe recipe, CocktailComposition composition)
        {
            return string.IsNullOrWhiteSpace(recipe.glassId)
                || (composition != null
                    && string.Equals(
                        recipe.glassId.Trim(),
                        composition.GlassId,
                        System.StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsIceValid(CocktailRecipe recipe, CocktailComposition composition)
        {
            if (recipe.requiredIceCount >= 0)
                return composition != null && composition.IceCount == recipe.requiredIceCount;

            if (recipe.iceRequirement == IceRequirement.Any)
                return true;

            bool hasIce = composition != null && composition.HasIce;
            return recipe.iceRequirement == IceRequirement.Required ? hasIce : !hasIce;
        }

        private static bool IsTechniqueValid(CocktailRecipe recipe, CocktailComposition composition)
        {
            if (recipe.requiredTechnique == CocktailTechnique.None)
                return true;

            return composition != null
                ? composition.MatchesRequiredTechnique(recipe.requiredTechnique)
                : recipe.requiredTechnique == CocktailTechnique.Build;
        }

        private static bool IsShakeIceValid(CocktailRecipe recipe, CocktailComposition composition)
        {
            return composition != null
                ? composition.MatchesShakeIceRequirement(recipe.shakeIceRequirement)
                : recipe.shakeIceRequirement != IceRequirement.Required;
        }

        private static float CollectExtras(
            CocktailRecipe recipe,
            CocktailComposition composition,
            HashSet<ItemDef> recipeItems,
            CocktailEvaluationResult result)
        {
            if (composition == null)
                return 0f;

            float extraVolume = 0f;
            foreach (KeyValuePair<ItemDef, float> pair in composition.Volumes)
            {
                if (pair.Key == null || recipeItems.Contains(pair.Key) || pair.Value <= 0.0001f)
                    continue;

                extraVolume += pair.Value;
                result.extraIngredients.Add(new CocktailExtraIngredient
                {
                    item = pair.Key,
                    volumeMl = pair.Value
                });
            }

            return extraVolume;
        }

        private static bool IsTotalWithinRange(CocktailRecipe recipe, float actualTotalMl)
        {
            if (recipe.minTotalMl > 0f && actualTotalMl < recipe.minTotalMl)
                return false;

            if (recipe.maxTotalMl > 0f && actualTotalMl > recipe.maxTotalMl)
                return false;

            return true;
        }

        private static string BuildFailureReason(
            bool hasUnresolvedIngredient,
            bool allIngredientsValid,
            bool extrasValid,
            bool totalValid,
            bool glassValid,
            bool iceValid,
            bool shakeIceValid,
            bool techniqueValid)
        {
            if (!hasUnresolvedIngredient
                && allIngredientsValid
                && extrasValid
                && totalValid
                && glassValid
                && iceValid
                && shakeIceValid
                && techniqueValid)
                return string.Empty;

            List<string> reasons = new List<string>();
            if (hasUnresolvedIngredient)
                reasons.Add("확인할 수 없는 재료 ID");
            if (!allIngredientsValid)
                reasons.Add("재료 용량 불일치");
            if (!extrasValid)
                reasons.Add("허용되지 않은 추가 재료");
            if (!totalValid)
                reasons.Add("총용량 범위 초과");
            if (!glassValid)
                reasons.Add("잔 종류 불일치");
            if (!iceValid)
                reasons.Add("얼음 조건 불일치");
            if (!shakeIceValid)
                reasons.Add("셰이킹 얼음 조건 불일치");
            if (!techniqueValid)
                reasons.Add("제조법 불일치");

            return string.Join(", ", reasons);
        }

        private static CocktailEvaluationResult CreateNoRecipeResult(CocktailComposition composition, string reason)
        {
            return new CocktailEvaluationResult
            {
                isSuccess = false,
                actualTotalMl = composition != null ? composition.TotalVolumeMl : 0f,
                actualGlassId = composition != null ? composition.GlassId : string.Empty,
                actualIceCount = composition != null ? composition.IceCount : 0,
                finalColor = composition != null ? composition.EvaluateFinalColor() : Color.clear,
                failureReason = reason
            };
        }
    }
}
