using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Slainte.Bartending
{
    public static class CocktailRecipeCsvLoader
    {
        public static CocktailRecipeCatalog LoadFromStreamingAssets(
            ItemDefCatalog itemCatalog,
            string dataFolder = "Data",
            string recipesFileName = "recipes.csv",
            string recipeIngredientsFileName = "recipe_ingredients.csv")
        {
            string dataPath = Path.Combine(Application.streamingAssetsPath, dataFolder);
            string recipesPath = Path.Combine(dataPath, recipesFileName);
            string recipeIngredientsPath = Path.Combine(dataPath, recipeIngredientsFileName);

            return LoadFromFiles(itemCatalog, recipesPath, recipeIngredientsPath);
        }

        public static CocktailRecipeCatalog LoadFromFiles(
            ItemDefCatalog itemCatalog,
            string recipesPath,
            string recipeIngredientsPath)
        {
            CocktailRecipeCatalog catalog = new CocktailRecipeCatalog();

            if (!File.Exists(recipesPath))
            {
                Debug.LogError($"Recipe CSV not found: {recipesPath}");
                return catalog;
            }

            if (!File.Exists(recipeIngredientsPath))
            {
                Debug.LogError($"Recipe ingredients CSV not found: {recipeIngredientsPath}");
                return catalog;
            }

            List<CsvRow> recipeRows = CsvTable.Parse(File.ReadAllText(recipesPath));
            for (int i = 0; i < recipeRows.Count; i++)
            {
                CsvRow row = recipeRows[i];
                CocktailRecipe recipe = new CocktailRecipe
                {
                    id = row.Get("id"),
                    displayName = row.Get("displayName"),
                    minTotalMl = ParseFloat(row.Get("minTotalMl")),
                    maxTotalMl = ParseFloat(row.Get("maxTotalMl")),
                    toleranceMl = ParseFloat(row.Get("toleranceMl"), 5f),
                    allowExtraIngredients = ParseBool(row.Get("allowExtraIngredients"))
                };

                if (string.IsNullOrWhiteSpace(recipe.displayName))
                    recipe.displayName = recipe.id;

                catalog.Add(recipe);
            }

            List<CsvRow> ingredientRows = CsvTable.Parse(File.ReadAllText(recipeIngredientsPath));
            for (int i = 0; i < ingredientRows.Count; i++)
            {
                CsvRow row = ingredientRows[i];
                string recipeId = row.Get("recipeId");
                if (!catalog.TryGet(recipeId, out CocktailRecipe recipe))
                {
                    Debug.LogWarning($"Recipe ingredient row references unknown recipe id '{recipeId}'.");
                    continue;
                }

                string ingredientId = row.Get("ingredientId");
                ItemDef item = null;
                if (itemCatalog == null || !itemCatalog.TryGet(ingredientId, out item))
                    Debug.LogWarning($"Recipe '{recipe.id}' references unknown ingredient id '{ingredientId}'.");

                float toleranceMl = ParseFloat(row.Get("toleranceMl"), recipe.toleranceMl);
                recipe.ingredients.Add(new CocktailRecipeIngredient
                {
                    ingredientId = ingredientId,
                    item = item,
                    targetMl = ParseFloat(row.Get("targetMl")),
                    toleranceMl = toleranceMl
                });
            }

            return catalog;
        }

        private static float ParseFloat(string value, float fallback = 0f)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
                ? result
                : fallback;
        }

        private static bool ParseBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            return value == "1"
                || value.Equals("true", System.StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", System.StringComparison.OrdinalIgnoreCase)
                || value.Equals("y", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
