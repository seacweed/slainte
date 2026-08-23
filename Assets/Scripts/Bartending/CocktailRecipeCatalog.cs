using System;
using System.Collections.Generic;
using Slainte.Economy;
using UnityEngine;

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

    public enum CocktailRecipeEvaluationGrade
    {
        Good,
        Mid
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
        public string englishName;
        public int price;
        public int strangeCoinPrice;
        public bool isOrderable = true;
        public bool appearsInRecipeBook = true;
        public string baseRecipeId;
        public CocktailRecipeEvaluationGrade evaluationGrade = CocktailRecipeEvaluationGrade.Good;
        public float expectedAbvPercent;
        public float minTotalMl;
        public float maxTotalMl;
        public float toleranceMl;
        public bool allowExtraIngredients;
        public string glassId;
        public IceRequirement iceRequirement = IceRequirement.Any;
        public int requiredIceCount = -1;
        public IceRequirement shakeIceRequirement = IceRequirement.Any;
        public CocktailTechnique requiredTechnique = CocktailTechnique.None;
        public readonly HashSet<string> ingredientPropertyTags = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> tasteTags = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> moodTags = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<CocktailRecipeIngredient> ingredients = new();
        public Sprite icon;
        public string description;

        public int GetPrice(GameCurrency currency)
        {
            return currency == GameCurrency.StrangeCoin ? strangeCoinPrice : price;
        }
    }

    public sealed class CocktailRecipeCatalog
    {
        private readonly Dictionary<string, CocktailRecipe> recipesById = new(System.StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, CocktailRecipe> RecipesById => recipesById;
        public IEnumerable<CocktailRecipe> Recipes => recipesById.Values;

        public IEnumerable<CocktailRecipe> OrderableRecipes
        {
            get
            {
                foreach (CocktailRecipe recipe in recipesById.Values)
                {
                    if (recipe != null && recipe.isOrderable)
                        yield return recipe;
                }
            }
        }

        public int Count => recipesById.Count;

        public int OrderableCount
        {
            get
            {
                int count = 0;
                foreach (CocktailRecipe recipe in recipesById.Values)
                {
                    if (recipe != null && recipe.isOrderable)
                        count++;
                }

                return count;
            }
        }

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
