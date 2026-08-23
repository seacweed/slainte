using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Slainte.Bartending;

// 레시피 상세 화면: 아이콘/이름/도수/재료 목록/맛·분위기 태그/설명을 채운다.
public class RecipeDetailUI : MonoBehaviour
{
    private const int MaxTasteChips = 3;
    private const int MaxMoodChips = 2;

    private delegate bool TryGetColor(string tag, out Color backgroundColor, out Color textColor);

    [Header("Header")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text abvText;

    [Header("Ingredients")]
    [SerializeField] private Transform ingredientListContent;
    [SerializeField] private RecipeIngredientRowUI ingredientRowPrefab;

    [Header("Tags")]
    [SerializeField] private Transform tasteChipContent;
    [SerializeField] private Transform moodChipContent;
    [SerializeField] private RecipeSearchOptionButton tagButtonPrefab;

    [Header("Description")]
    [SerializeField] private TMP_Text descriptionText;

    private readonly List<GameObject> _spawned = new();

    public void Bind(CocktailRecipe recipe, TasteMoodTagPaletteDef palette)
    {
        ClearSpawned();
        if (recipe == null) return;

        if (iconImage)
        {
            iconImage.sprite = recipe.icon;
            iconImage.enabled = recipe.icon != null;
        }
        if (nameText) nameText.text = recipe.displayName;
        if (abvText) abvText.text = $"{recipe.expectedAbvPercent:0.#}%";
        if (descriptionText) descriptionText.text = recipe.description;

        PopulateIngredients(recipe);
        PopulateChips(tasteChipContent, recipe.tasteTags, MaxTasteChips,
            palette != null ? (TryGetColor)palette.TryGetTasteColor : null);
        PopulateChips(moodChipContent, recipe.moodTags, MaxMoodChips,
            palette != null ? (TryGetColor)palette.TryGetMoodColor : null);
    }

    private void PopulateIngredients(CocktailRecipe recipe)
    {
        if (!ingredientListContent || !ingredientRowPrefab) return;

        foreach (CocktailRecipeIngredient ingredient in recipe.ingredients)
        {
            if (ingredient == null) continue;
            RecipeIngredientRowUI row = Instantiate(ingredientRowPrefab, ingredientListContent);
            string ingredientName = ingredient.item != null ? ingredient.item.displayName : ingredient.ingredientId;
            row.Setup(ingredientName, ingredient.targetMl);
            _spawned.Add(row.gameObject);
        }
    }

    private void PopulateChips(
        Transform content,
        IEnumerable<string> tags,
        int maxCount,
        TryGetColor tryGetColor)
    {
        if (!content || !tagButtonPrefab || tags == null) return;

        int count = 0;
        foreach (string tag in tags)
        {
            if (count >= maxCount) break;
            Color backgroundColor = Color.white;
            Color textColor = Color.black;
            tryGetColor?.Invoke(tag, out backgroundColor, out textColor);

            RecipeSearchOptionButton chip = Instantiate(tagButtonPrefab, content);
            chip.Setup(tag, backgroundColor, textColor, interactable: false);
            _spawned.Add(chip.gameObject);
            count++;
        }
    }

    private void ClearSpawned()
    {
        foreach (GameObject go in _spawned)
            if (go) Destroy(go);
        _spawned.Clear();
    }
}
