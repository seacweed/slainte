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
            CocktailRecipe recipe = ResolveRecipe(requestedRecipeId);
            if (recipe == null)
                return null;

            CocktailOrderTemplate template = PickTemplate(CocktailOrderType.RecipeOrder);
            string lineTemplate = template != null
                ? template.lineTemplate
                : "{recipeName} 한 잔 부탁하네.";

            return new GeneratedCocktailOrder
            {
                id = CreateGeneratedId(recipe, template),
                orderType = CocktailOrderType.RecipeOrder,
                line = FormatLine(lineTemplate, recipe),
                requestedRecipeId = recipe.id,
                requestedRecipe = recipe,
                sourceTemplate = template
            };
        }

        private CocktailRecipe ResolveRecipe(string requestedRecipeId)
        {
            if (recipeCatalog == null || recipeCatalog.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(requestedRecipeId))
                return recipeCatalog.TryGet(requestedRecipeId, out CocktailRecipe requestedRecipe)
                    ? requestedRecipe
                    : null;

            return PickRecipe();
        }

        private CocktailRecipe PickRecipe()
        {
            int targetIndex = Random.Range(0, recipeCatalog.Count);
            int index = 0;
            foreach (CocktailRecipe recipe in recipeCatalog.Recipes)
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
