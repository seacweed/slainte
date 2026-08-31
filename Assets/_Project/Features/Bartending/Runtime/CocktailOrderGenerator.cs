using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    public sealed class CocktailOrderGenerator
    {
        private readonly CocktailRecipeCatalog recipeCatalog;
        private readonly CocktailOrderTemplateCatalog templateCatalog;

        public CocktailOrderGenerator(
            CocktailRecipeCatalog recipeCatalog,
            CocktailOrderTemplateCatalog templateCatalog)
        {
            this.recipeCatalog = recipeCatalog;
            this.templateCatalog = templateCatalog;
        }

        public GeneratedCocktailOrder GenerateRecipeOrder(string requestedRecipeId = "")
        {
            return GenerateOrder(CocktailOrderType.RecipeOrder, requestedRecipeId);
        }

        public bool CanGenerateOrder(string requestedRecipeId)
        {
            return ResolveRecipe(requestedRecipeId) != null;
        }

        public bool CanGenerateOrder(
            CocktailOrderType orderType,
            string requestedRecipeId,
            IEnumerable<string> requestedTags)
        {
            if (IsTagOrder(orderType))
                return CocktailOrderTagRules.TryGetSingleTag(requestedTags, out _);

            return CanGenerateOrder(requestedRecipeId);
        }

        public GeneratedCocktailOrder GenerateOrder(
            CocktailOrderType orderType,
            string requestedRecipeId = "",
            IEnumerable<string> requestedTags = null,
            string requestedConditionLabel = null)
        {
            bool tagOrder = IsTagOrder(orderType);
            CocktailRecipe recipe = tagOrder ? null : ResolveRecipe(requestedRecipeId);
            string requestedTag = string.Empty;
            if (!tagOrder && recipe == null)
                return null;
            if (tagOrder
                && !CocktailOrderTagRules.TryGetSingleTag(requestedTags, out requestedTag))
                return null;

            CocktailOrderTemplate template = PickTemplate(orderType);
            string lineTemplate = template != null
                ? template.lineTemplate
                : tagOrder
                    ? "{condition} 조건으로 한 잔 부탁하네."
                    : "{recipeName} 한 잔 부탁하네.";
            string conditionLabel = tagOrder
                ? string.IsNullOrWhiteSpace(requestedConditionLabel)
                    ? requestedTag
                    : CocktailOrderTagRules.Normalize(requestedConditionLabel)
                : string.Empty;

            GeneratedCocktailOrder order = new GeneratedCocktailOrder
            {
                id = CreateGeneratedId(recipe, template, conditionLabel),
                orderType = orderType,
                line = FormatLine(lineTemplate, recipe, conditionLabel),
                requestedRecipeId = recipe != null ? recipe.id : string.Empty,
                requestedConditionLabel = conditionLabel,
                requestedRecipe = recipe,
                sourceTemplate = template
            };

            if (orderType == CocktailOrderType.TasteOrder)
                order.requiredTasteTags.Add(requestedTag);
            else if (orderType == CocktailOrderType.MoodOrder)
                order.requiredMoodTags.Add(requestedTag);

            return order;
        }

        private static bool IsTagOrder(CocktailOrderType orderType)
        {
            return orderType == CocktailOrderType.TasteOrder
                || orderType == CocktailOrderType.MoodOrder;
        }

        private CocktailRecipe ResolveRecipe(string requestedRecipeId)
        {
            if (recipeCatalog == null || recipeCatalog.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(requestedRecipeId))
                return recipeCatalog.TryGet(requestedRecipeId, out CocktailRecipe requestedRecipe)
                    && requestedRecipe.isOrderable
                    ? requestedRecipe
                    : null;

            return PickRecipe();
        }

        private CocktailRecipe PickRecipe()
        {
            if (recipeCatalog.OrderableCount <= 0)
                return null;

            int targetIndex = Random.Range(0, recipeCatalog.OrderableCount);
            int index = 0;
            foreach (CocktailRecipe recipe in recipeCatalog.OrderableRecipes)
            {
                if (index == targetIndex)
                    return recipe;

                index++;
            }

            return null;
        }

        private CocktailOrderTemplate PickTemplate(CocktailOrderType orderType)
        {
            IReadOnlyList<CocktailOrderTemplate> templates = templateCatalog != null
                ? templateCatalog.GetTemplates(orderType)
                : null;

            if (templates == null || templates.Count == 0)
                return null;

            float totalWeight = 0f;
            for (int i = 0; i < templates.Count; i++)
                totalWeight += Mathf.Max(0f, templates[i].weight);

            if (totalWeight <= 0f)
                return templates[Random.Range(0, templates.Count)];

            float roll = Random.Range(0f, totalWeight);
            float cursor = 0f;
            for (int i = 0; i < templates.Count; i++)
            {
                cursor += Mathf.Max(0f, templates[i].weight);
                if (roll <= cursor)
                    return templates[i];
            }

            return templates[templates.Count - 1];
        }

        private static string FormatLine(
            string lineTemplate,
            CocktailRecipe recipe,
            string conditionLabel)
        {
            string recipeName = GetRecipeName(recipe);
            return (string.IsNullOrWhiteSpace(lineTemplate) ? "{recipeName} 한 잔 부탁하네." : lineTemplate)
                .Replace("{recipeName}", recipeName)
                .Replace("{recipeId}", recipe != null ? recipe.id : string.Empty)
                .Replace("{condition}", conditionLabel ?? string.Empty)
                .Replace("{tasteTag}", conditionLabel ?? string.Empty)
                .Replace("{moodTag}", conditionLabel ?? string.Empty);
        }

        private static string CreateGeneratedId(
            CocktailRecipe recipe,
            CocktailOrderTemplate template,
            string conditionLabel)
        {
            string templateId = template != null ? template.id : "fallback_recipe_order";
            string targetId = recipe != null
                ? recipe.id
                : string.IsNullOrWhiteSpace(conditionLabel)
                    ? "unknown_condition"
                    : conditionLabel.Trim();
            return $"{templateId}:{targetId}";
        }

        private static string GetRecipeName(CocktailRecipe recipe)
        {
            if (recipe == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(recipe.displayName))
                return recipe.displayName;

            return recipe.id;
        }
    }
}
