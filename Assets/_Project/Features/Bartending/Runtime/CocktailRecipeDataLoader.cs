using Slainte.Content;
using UnityEngine;

namespace Slainte.Bartending
{
    public static class CocktailRecipeDataLoader
    {
        public static CocktailRecipeCatalog LoadDefault(ItemDefCatalog itemCatalog)
        {
            // The generated Resources assets are the single runtime recipe source.
            return LoadFromResources(itemCatalog);
        }

        public static CocktailRecipeCatalog LoadFromResources(
            ItemDefCatalog itemCatalog,
            string resourcesPath = ProjectResourcePaths.BartendingRecipes)
        {
            if (string.IsNullOrWhiteSpace(resourcesPath) || resourcesPath == "Recipes")
                resourcesPath = ProjectResourcePaths.BartendingRecipes;

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
