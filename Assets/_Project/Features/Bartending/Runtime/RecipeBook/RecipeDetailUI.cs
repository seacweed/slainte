using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Slainte.Bartending;

// 레시피 상세 화면: 이름/도수, 칵테일 이미지, 분위기 칩(최대 2개, 아래 칸부터), 재료 라벨+양, 설명을 채운다.
public class RecipeDetailUI : MonoBehaviour
{
    private const int MaxMoodChips = 2;

    [Header("Header")]
    [SerializeField] private Image    iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text abvText;

    [Header("Ingredients")]
    [SerializeField] private Transform             ingredientListContent;
    [SerializeField] private RecipeIngredientRowUI ingredientRowPrefab;
    [Tooltip("Label sprite for every ingredient until per-category colors exist (element-BG-pink).")]
    [SerializeField] private Sprite                defaultIngredientLabel;

    [Header("Mood Chips")]
    [Tooltip("Bottom-aligned vertical layout. Chips fill from the bottom slot upward.")]
    [SerializeField] private Transform       moodChipContent;
    [SerializeField] private RecipeTagChipUI moodChipPrefab;

    [Header("Description")]
    [SerializeField] private TMP_Text descriptionText;

    private readonly List<GameObject> _spawned = new();

    public void Bind(CocktailRecipe recipe)
    {
        ClearSpawned();
        if (recipe == null) return;

        if (iconImage)
        {
            iconImage.sprite = recipe.icon;
            iconImage.enabled = recipe.icon != null;
        }
        if (nameText) nameText.text = recipe.displayName;
        if (abvText) abvText.text = $"ALC {recipe.expectedAbvPercent:0.0}%";
        if (descriptionText) descriptionText.text = recipe.description;

        PopulateIngredients(recipe);
        PopulateMoodChips(recipe.moodTags);
    }

    private void PopulateIngredients(CocktailRecipe recipe)
    {
        if (!ingredientListContent || !ingredientRowPrefab) return;

        foreach (CocktailRecipeIngredient ingredient in recipe.ingredients)
        {
            if (ingredient == null) continue;
            RecipeIngredientRowUI row = Instantiate(ingredientRowPrefab, ingredientListContent);
            string ingredientName = ingredient.item != null ? ingredient.item.displayName : ingredient.ingredientId;
            row.Setup(ingredientName, ingredient.targetMl, ResolveIngredientLabel(ingredient));
            _spawned.Add(row.gameObject);
        }
    }

    // 재료 카테고리별 색 스프라이트가 준비되면 여기서 카테고리 → 스프라이트를 고른다. 지금은 모두 기본(분홍).
    private Sprite ResolveIngredientLabel(CocktailRecipeIngredient ingredient)
    {
        return defaultIngredientLabel;
    }

    // 맛은 표시하지 않고 분위기만 표시한다. 칸은 아래부터 채우므로, 레이아웃이 위→아래로 쌓는 점을 이용해
    // 나중에 만든 칩을 맨 앞으로 보낸다(첫 태그가 항상 맨 아래 칸).
    private void PopulateMoodChips(IEnumerable<string> tags)
    {
        if (!moodChipContent || !moodChipPrefab || tags == null) return;

        int count = 0;
        foreach (string tag in tags)
        {
            if (count >= MaxMoodChips) break;
            if (string.IsNullOrWhiteSpace(tag)) continue;

            RecipeTagChipUI chip = Instantiate(moodChipPrefab, moodChipContent);
            chip.Setup(tag);
            chip.transform.SetAsFirstSibling();
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
