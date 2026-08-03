using System;
using System.Collections.Generic;

namespace Slainte.Bartending
{
    [Flags]
    public enum CocktailTechnique
    {
        None = 0,
        Build = 1 << 0,
        Stir = 1 << 1,
        Shake = 1 << 2
    }

    public enum IceRequirement
    {
        Any,
        None,
        Required
    }

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
        public string glassId;
        public IceRequirement iceRequirement = IceRequirement.Any;
        public CocktailTechnique requiredTechnique = CocktailTechnique.None;
        public readonly HashSet<string> tasteTags = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> moodTags = new(StringComparer.OrdinalIgnoreCase);
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
