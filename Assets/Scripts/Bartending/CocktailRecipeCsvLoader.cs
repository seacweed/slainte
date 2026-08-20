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
                Debug.LogError($"레시피 CSV를 찾을 수 없습니다: {recipesPath}");
                return catalog;
            }

            if (!File.Exists(recipeIngredientsPath))
            {
                Debug.LogError($"레시피 재료 CSV를 찾을 수 없습니다: {recipeIngredientsPath}");
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
                    price = ParseInt(row.Get("price")),
                    strangeCoinPrice = ParseInt(row.Get("strangeCoinPrice")),
                    minTotalMl = ParseFloat(row.Get("minTotalMl")),
                    maxTotalMl = ParseFloat(row.Get("maxTotalMl")),
                    toleranceMl = ParseFloat(row.Get("toleranceMl"), 5f),
                    allowExtraIngredients = ParseBool(row.Get("allowExtraIngredients")),
                    glassId = row.Get("glassId").Trim(),
                    iceRequirement = ParseEnum(row.Get("iceRequirement"), IceRequirement.Any),
                    requiredIceCount = ParseInt(row.Get("requiredIceCount"), -1),
                    shakeIceRequirement = ParseEnum(
                        row.Get("shakeIceRequirement"),
                        IceRequirement.Any),
                    requiredTechnique = ParseEnum(row.Get("technique"), CocktailTechnique.None)
                };

                AddTags(recipe.tasteTags, row.Get("tasteTags"));
                AddTags(recipe.moodTags, row.Get("moodTags"));

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
                    Debug.LogWarning($"레시피 재료 행이 알 수 없는 레시피 ID '{recipeId}'를 참조합니다.");
                    continue;
                }

                string ingredientId = row.Get("ingredientId");
                ItemDef item = null;
                if (itemCatalog == null || !itemCatalog.TryGet(ingredientId, out item))
                    Debug.LogWarning($"레시피 '{recipe.id}'가 알 수 없는 재료 ID '{ingredientId}'를 참조합니다.");

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

        private static int ParseInt(string value, int fallback = 0)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
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

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            return !string.IsNullOrWhiteSpace(value)
                && System.Enum.TryParse(value.Trim(), true, out T parsed)
                    ? parsed
                    : fallback;
        }

        private static void AddTags(HashSet<string> target, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(value))
                return;

            string[] tags = value.Split('|');
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = tags[i].Trim();
                if (!string.IsNullOrWhiteSpace(tag))
                    target.Add(tag);
            }
        }
    }
}
