using UnityEngine;

namespace Slainte.Bartending
{
    public static class CocktailRecipeDataLoader
    {
        public static CocktailRecipeCatalog LoadDefault(ItemDefCatalog itemCatalog)
        {
            CocktailRecipeCatalog catalog = CocktailRecipeCsvLoader.LoadFromStreamingAssets(
                itemCatalog,
                "Data",
                "recipes.csv",
                "recipe_ingredients.csv");
            CocktailRecipeCatalog assetCatalog = LoadFromResources(itemCatalog);
            foreach (CocktailRecipe recipe in assetCatalog.Recipes)
                catalog.Add(recipe);

            return catalog;
        }

        public static CocktailRecipeCatalog LoadFromResources(
            ItemDefCatalog itemCatalog,
            string resourcesPath = "Recipes")
        {
            CocktailRecipeCatalog catalog = new CocktailRecipeCatalog();
            CocktailRecipeDef[] definitions = Resources.LoadAll<CocktailRecipeDef>(resourcesPath);
            for (int i = 0; i < definitions.Length; i++)
            {
                CocktailRecipeDef definition = definitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                    continue;

                CocktailRecipe recipe = definition.ToRuntime(itemCatalog);
                catalog.Add(recipe);
            }

            return catalog;
        }
    }
}
