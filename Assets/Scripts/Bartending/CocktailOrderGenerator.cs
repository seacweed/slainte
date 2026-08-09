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

        public GeneratedCocktailOrder GenerateOrder(
            CocktailOrderType orderType,
            string requestedRecipeId = "")
        {
            CocktailRecipe recipe = ResolveRecipe(requestedRecipeId);
            if (recipe == null)
                return null;

            CocktailOrderTemplate template = PickTemplate(orderType);
            string lineTemplate = template != null
                ? template.lineTemplate
                : "{recipeName} 한 잔 부탁하네.";

            GeneratedCocktailOrder order = new GeneratedCocktailOrder
            {
                id = CreateGeneratedId(recipe, template),
                orderType = orderType,
                line = FormatLine(lineTemplate, recipe),
                requestedRecipeId = recipe.id,
                requestedRecipe = recipe,
                sourceTemplate = template
            };

            if (orderType == CocktailOrderType.TasteOrder)
                CopyTags(recipe.tasteTags, order.requiredTasteTags);
            else if (orderType == CocktailOrderType.MoodOrder)
                CopyTags(recipe.moodTags, order.requiredMoodTags);

            return order;
        }

        private static void CopyTags(IEnumerable<string> source, HashSet<string> destination)
        {
            if (source == null || destination == null)
                return;

            foreach (string tag in source)
                destination.Add(tag);
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

        private static string FormatLine(string lineTemplate, CocktailRecipe recipe)
        {
            string recipeName = GetRecipeName(recipe);
            return (string.IsNullOrWhiteSpace(lineTemplate) ? "{recipeName} 한 잔 부탁하네." : lineTemplate)
                .Replace("{recipeName}", recipeName)
                .Replace("{recipeId}", recipe != null ? recipe.id : string.Empty);
        }

        private static string CreateGeneratedId(CocktailRecipe recipe, CocktailOrderTemplate template)
        {
            string templateId = template != null ? template.id : "fallback_recipe_order";
            string recipeId = recipe != null ? recipe.id : "unknown_recipe";
            return $"{templateId}:{recipeId}";
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
