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
        public float score;
        public float actualTotalMl;
        public string failureReason;
        public readonly List<CocktailIngredientEvaluation> ingredients = new();
        public readonly List<CocktailExtraIngredient> extraIngredients = new();

        public string ToDebugString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(isSuccess ? "GOOD" : "BAD");
            builder.Append(" | ");
            builder.Append(matchedRecipe != null ? matchedRecipe.displayName : "No Recipe");
            builder.Append(" | score ");
            builder.Append((score * 100f).ToString("0.#"));
            builder.Append("% | total ");
            builder.Append(actualTotalMl.ToString("0.##"));
            builder.AppendLine(" ml");

            if (!string.IsNullOrWhiteSpace(failureReason))
                builder.AppendLine(failureReason);

            for (int i = 0; i < ingredients.Count; i++)
            {
                CocktailIngredientEvaluation ingredient = ingredients[i];
                builder.Append("- ");
                builder.Append(ingredient.recipeIngredient != null
                    ? ingredient.recipeIngredient.ingredientId
                    : "unknown");
                builder.Append(": ");
                builder.Append(ingredient.actualMl.ToString("0.##"));
                builder.Append(" / ");
                builder.Append(ingredient.recipeIngredient != null
                    ? ingredient.recipeIngredient.targetMl.ToString("0.##")
                    : "0");
                builder.Append(" ml");
                if (!ingredient.isWithinTolerance)
                    builder.Append(" (off)");
                builder.AppendLine();
            }

            for (int i = 0; i < extraIngredients.Count; i++)
            {
                builder.Append("- extra ");
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
                return "Unknown";

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
            CocktailEvaluationResult best = null;
            if (recipeCatalog == null || recipeCatalog.Count == 0)
                return CreateNoRecipeResult(composition, "No recipes loaded.");

            foreach (CocktailRecipe recipe in recipeCatalog.Recipes)
            {
                CocktailEvaluationResult result = EvaluateRecipe(recipe, composition);
                if (best == null || result.score > best.score)
                    best = result;
            }

            return best ?? CreateNoRecipeResult(composition, "No matching recipe.");
        }

        public CocktailEvaluationResult EvaluateRecipe(string recipeId, CocktailComposition composition)
        {
            if (recipeCatalog == null || recipeCatalog.Count == 0)
                return CreateNoRecipeResult(composition, "No recipes loaded.");

            if (!recipeCatalog.TryGet(recipeId, out CocktailRecipe recipe))
                return CreateNoRecipeResult(composition, $"Recipe '{recipeId}' not loaded.");

            return EvaluateRecipe(recipe, composition);
        }

        private static CocktailEvaluationResult EvaluateRecipe(CocktailRecipe recipe, CocktailComposition composition)
        {
            CocktailEvaluationResult result = new CocktailEvaluationResult
            {
                matchedRecipe = recipe,
                actualTotalMl = composition != null ? composition.TotalVolumeMl : 0f
            };

            HashSet<ItemDef> recipeItems = new HashSet<ItemDef>();
            float ingredientPenalty = 0f;
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

                float divisor = Mathf.Max(recipeIngredient.targetMl, toleranceMl, 1f);
                ingredientPenalty += Mathf.Clamp01(Mathf.Abs(deltaMl) / divisor);

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
            bool extrasValid = recipe.allowExtraIngredients || extraVolume <= Mathf.Max(recipe.toleranceMl, 0f);
            bool totalValid = IsTotalWithinRange(recipe, result.actualTotalMl);

            float ingredientScore = recipe.ingredients.Count > 0
                ? 1f - Mathf.Clamp01(ingredientPenalty / recipe.ingredients.Count)
                : 0f;
            float totalScore = GetTotalScore(recipe, result.actualTotalMl);
            float extraScore = recipe.allowExtraIngredients
                ? 1f
                : 1f - Mathf.Clamp01(extraVolume / Mathf.Max(result.actualTotalMl, 1f));

            result.score = Mathf.Clamp01(ingredientScore * 0.75f + totalScore * 0.15f + extraScore * 0.1f);
            result.isSuccess = allIngredientsValid && extrasValid && totalValid && !hasUnresolvedIngredient;
            result.failureReason = BuildFailureReason(hasUnresolvedIngredient, allIngredientsValid, extrasValid, totalValid);
            return result;
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
            float extraTolerance = Mathf.Max(recipe.toleranceMl, 0f);
            foreach (KeyValuePair<ItemDef, float> pair in composition.Volumes)
            {
                if (pair.Key == null || recipeItems.Contains(pair.Key) || pair.Value <= extraTolerance)
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

        private static float GetTotalScore(CocktailRecipe recipe, float actualTotalMl)
        {
            if (IsTotalWithinRange(recipe, actualTotalMl))
                return 1f;

            float targetTotal = 0f;
            for (int i = 0; i < recipe.ingredients.Count; i++)
                targetTotal += recipe.ingredients[i].targetMl;

            if (targetTotal <= 0f)
                targetTotal = Mathf.Max(recipe.minTotalMl, recipe.maxTotalMl, 1f);

            float delta = 0f;
            if (recipe.minTotalMl > 0f && actualTotalMl < recipe.minTotalMl)
                delta = recipe.minTotalMl - actualTotalMl;
            else if (recipe.maxTotalMl > 0f && actualTotalMl > recipe.maxTotalMl)
                delta = actualTotalMl - recipe.maxTotalMl;

            return 1f - Mathf.Clamp01(delta / targetTotal);
        }

        private static string BuildFailureReason(
            bool hasUnresolvedIngredient,
            bool allIngredientsValid,
            bool extrasValid,
            bool totalValid)
        {
            if (!hasUnresolvedIngredient && allIngredientsValid && extrasValid && totalValid)
                return string.Empty;

            List<string> reasons = new List<string>();
            if (hasUnresolvedIngredient)
                reasons.Add("unresolved ingredient id");
            if (!allIngredientsValid)
                reasons.Add("ingredient volume mismatch");
            if (!extrasValid)
                reasons.Add("extra ingredients");
            if (!totalValid)
                reasons.Add("total volume out of range");

            return string.Join(", ", reasons);
        }

        private static CocktailEvaluationResult CreateNoRecipeResult(CocktailComposition composition, string reason)
        {
            return new CocktailEvaluationResult
            {
                isSuccess = false,
                score = 0f,
                actualTotalMl = composition != null ? composition.TotalVolumeMl : 0f,
                failureReason = reason
            };
        }
    }
}
