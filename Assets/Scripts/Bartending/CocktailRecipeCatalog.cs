using System.Collections.Generic;

namespace Slainte.Bartending
{
    public sealed class CocktailRecipeIngredient
    {
        public string ingredientId;
        public ItemDef item;
        public float targetMl;
        public float toleranceMl;
    }

    public sealed class CocktailRecipe
    {
        public string id;
        public string displayName;
        public float minTotalMl;
        public float maxTotalMl;
        public float toleranceMl;
        public bool allowExtraIngredients;
        public readonly List<CocktailRecipeIngredient> ingredients = new();
    }

    public sealed class CocktailRecipeCatalog
    {
        private readonly Dictionary<string, CocktailRecipe> recipesById = new(System.StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, CocktailRecipe> RecipesById => recipesById;
        public IEnumerable<CocktailRecipe> Recipes => recipesById.Values;

        public int Count => recipesById.Count;

        public void Add(CocktailRecipe recipe)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.id))
                return;

            recipesById[recipe.id.Trim()] = recipe;
        }

        public bool TryGet(string id, out CocktailRecipe recipe)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                recipe = null;
                return false;
            }

            return recipesById.TryGetValue(id.Trim(), out recipe);
        }
    }
}
