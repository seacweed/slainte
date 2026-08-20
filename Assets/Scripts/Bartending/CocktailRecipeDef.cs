using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    [Serializable]
    public sealed class CocktailRecipeIngredientDef
    {
        public string itemId;
        [Min(0f)] public float targetMl;
        [Min(0f)] public float toleranceMl = 5f;
    }

    [CreateAssetMenu(menuName = "Slainte/Bartending/Cocktail Recipe", fileName = "CocktailRecipe_")]
    public sealed class CocktailRecipeDef : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        public string englishName;

        [Header("Availability")]
        public bool isOrderable = true;
        public bool appearsInRecipeBook = true;
        public string baseRecipeId;
        public CocktailRecipeEvaluationGrade evaluationGrade = CocktailRecipeEvaluationGrade.Good;

        [Header("Evaluation")]
        [Min(0f)] public float minTotalMl;
        [Min(0f)] public float maxTotalMl;
        [Min(0f)] public float toleranceMl = 5f;
        public bool allowExtraIngredients;
        public string glassId;
        public IceRequirement iceRequirement = IceRequirement.Any;
        public IceRequirement shakeIceRequirement = IceRequirement.Any;
        public CocktailTechnique requiredTechnique = CocktailTechnique.None;
        [Tooltip("0 이상이면 이 값을 사용하고, 음수이면 재료의 도수와 용량으로 계산합니다.")]
        public float abvOverridePercent = -1f;

        [Header("Search Tags")]
        public List<string> ingredientPropertyTags = new();
        public List<string> tasteTags = new();
        public List<string> moodTags = new();

        [Header("Ingredients")]
        public List<CocktailRecipeIngredientDef> ingredients = new();

        public CocktailRecipe ToRuntime(ItemDefCatalog itemCatalog)
        {
            CocktailRecipe recipe = new CocktailRecipe
            {
                id = id != null ? id.Trim() : string.Empty,
                displayName = displayName,
                englishName = englishName,
                isOrderable = isOrderable && ingredients != null && ingredients.Count > 0,
                appearsInRecipeBook = appearsInRecipeBook,
                baseRecipeId = baseRecipeId != null ? baseRecipeId.Trim() : string.Empty,
                evaluationGrade = evaluationGrade,
                minTotalMl = minTotalMl,
                maxTotalMl = maxTotalMl,
                toleranceMl = toleranceMl,
                allowExtraIngredients = allowExtraIngredients,
                glassId = glassId != null ? glassId.Trim() : string.Empty,
                iceRequirement = iceRequirement,
                shakeIceRequirement = shakeIceRequirement,
                requiredTechnique = requiredTechnique
            };

            CopyTags(ingredientPropertyTags, recipe.ingredientPropertyTags);
            CopyTags(tasteTags, recipe.tasteTags);
            CopyTags(moodTags, recipe.moodTags);

            float totalMl = 0f;
            float pureAlcoholMl = 0f;
            if (ingredients != null)
            {
                for (int i = 0; i < ingredients.Count; i++)
                {
                    CocktailRecipeIngredientDef source = ingredients[i];
                    if (source == null || string.IsNullOrWhiteSpace(source.itemId))
                        continue;

                    string itemId = source.itemId.Trim();
                    ItemDef item = null;
                    itemCatalog?.TryGet(itemId, out item);
                    recipe.ingredients.Add(new CocktailRecipeIngredient
                    {
                        ingredientId = itemId,
                        item = item,
                        targetMl = source.targetMl,
                        toleranceMl = source.toleranceMl > 0f ? source.toleranceMl : toleranceMl
                    });

                    totalMl += Mathf.Max(0f, source.targetMl);
                    if (item != null)
                        pureAlcoholMl += Mathf.Max(0f, source.targetMl) * Mathf.Max(0f, item.abvPercent) / 100f;
                }
            }

            recipe.expectedAbvPercent = abvOverridePercent >= 0f
                ? abvOverridePercent
                : totalMl > 0f ? pureAlcoholMl / totalMl * 100f : 0f;
            return recipe;
        }

        private static void CopyTags(IEnumerable<string> source, HashSet<string> destination)
        {
            if (source == null || destination == null)
                return;

            foreach (string value in source)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    destination.Add(value.Trim());
            }
        }
    }
}
