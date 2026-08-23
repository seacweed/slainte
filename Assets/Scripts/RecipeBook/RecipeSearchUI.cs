using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Slainte.Bartending;

public enum RecipeSearchCategory
{
    Mood,
    Taste
}

public class RecipeSearchUI : MonoBehaviour
{
    private enum DetailOrigin
    {
        CategoryResults,
        SearchInline
    }

    [Serializable]
    private class CategoryHeaderConfig
    {
        public RecipeSearchCategory category;
        public string               title;
        public Sprite               headerIcon;
    }

    [Header("Views")]
    [SerializeField] private GameObject mainView;
    [SerializeField] private GameObject categoryView;
    [SerializeField] private GameObject resultsView;
    [SerializeField] private GameObject detailView;

    [Header("Category Header (Category View)")]
    [SerializeField] private TMP_Text categoryTitleText;
    [SerializeField] private Image    categoryHeaderIcon;
    [SerializeField] private Button   backButton;

    [Header("Results Header (Results View)")]
    [SerializeField] private RecipeSearchOptionButton resultsHeaderButton;
    [SerializeField] private Button                   resultsBackButton;

    [Header("Category Buttons (Main View)")]
    [SerializeField] private GameObject categoryButtonsRoot;
    [SerializeField] private Button     moodButton;
    [SerializeField] private Button     tasteButton;

    [Header("Category Options (Category View)")]
    [SerializeField] private Transform               optionsContent;
    [SerializeField] private RecipeSearchOptionButton optionButtonPrefab;

    [Header("Category Headers")]
    [SerializeField]
    private List<CategoryHeaderConfig> categoryHeaders = new List<CategoryHeaderConfig>
    {
        new CategoryHeaderConfig { category = RecipeSearchCategory.Mood },
        new CategoryHeaderConfig { category = RecipeSearchCategory.Taste },
    };

    [Header("Tag Palette")]
    [SerializeField] private TasteMoodTagPaletteDef tagPalette;

    [Header("Search (Main View)")]
    [SerializeField] private TMP_InputField searchInputField;
    [SerializeField] private Button         searchButton;
    [SerializeField] private GameObject     searchResultsRoot;
    [SerializeField] private Transform      searchResultsContent;

    [Header("Recipe List")]
    [SerializeField] private Transform          resultsContent;
    [SerializeField] private RecipeListItemUI   recipeListItemPrefab;

    [Header("Detail View")]
    [SerializeField] private RecipeDetailUI recipeDetailUI;
    [SerializeField] private Button         detailBackButton;

    private RecipeSearchCategory _currentCategory;
    private DetailOrigin         _detailOrigin;
    private List<CocktailRecipe> _allBookRecipes = new();

    void Awake()
    {
        if (moodButton)  moodButton.onClick.AddListener(() => ShowCategory(RecipeSearchCategory.Mood));
        if (tasteButton) tasteButton.onClick.AddListener(() => ShowCategory(RecipeSearchCategory.Taste));
        if (backButton)  backButton.onClick.AddListener(ShowMain);
        if (resultsBackButton) resultsBackButton.onClick.AddListener(BackFromResults);
        if (detailBackButton)  detailBackButton.onClick.AddListener(BackFromDetail);
        if (searchButton) searchButton.onClick.AddListener(OnSearchSubmit);
        if (searchInputField) searchInputField.onSubmit.AddListener(_ => OnSearchSubmit());

        LoadRecipes();
        ShowMain();
    }

    private void LoadRecipes()
    {
        ItemDefCatalog itemCatalog = ItemDefCatalog.LoadFromResources("Items");
        CocktailRecipeCatalog catalog = CocktailRecipeDataLoader.LoadDefault(itemCatalog);
        _allBookRecipes = catalog.Recipes
            .Where(recipe => recipe != null && recipe.appearsInRecipeBook)
            .OrderBy(recipe => recipe.id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Called by RecipeBookUI only when the book transitions from disabled back to enabled
    // (e.g. EpisodeMode -> OrderMode/CraftingMode). Plain Tab-toggle open/close must not reset.
    public void ResetToMain() => ShowMain();

    public void ShowMain()
    {
        if (searchInputField) searchInputField.text = string.Empty;
        ClearContent(searchResultsContent);
        SetSearchActive(false);
        SetViews(main: true, category: false, results: false, detail: false);
    }

    public void ShowCategory(RecipeSearchCategory category)
    {
        _currentCategory = category;

        CategoryHeaderConfig header = categoryHeaders.FirstOrDefault(c => c.category == category);
        if (categoryTitleText) categoryTitleText.text = header?.title;
        if (categoryHeaderIcon)
        {
            categoryHeaderIcon.sprite = header?.headerIcon;
            categoryHeaderIcon.gameObject.SetActive(header?.headerIcon != null);
        }

        SetViews(main: false, category: true, results: false, detail: false);
        PopulateOptions(category);
    }

    private void OnSearchSubmit()
    {
        string query = searchInputField ? searchInputField.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            ShowMain();
            return;
        }

        SetSearchActive(true);
        List<CocktailRecipe> matches = _allBookRecipes
            .Where(recipe => !string.IsNullOrEmpty(recipe.displayName)
                && recipe.displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
        PopulateRecipeList(searchResultsContent, matches, recipe => ShowDetail(recipe, DetailOrigin.SearchInline));
        SetViews(main: true, category: false, results: false, detail: false);
    }

    private void SetSearchActive(bool active)
    {
        if (categoryButtonsRoot) categoryButtonsRoot.SetActive(!active);
        if (searchResultsRoot)   searchResultsRoot.SetActive(active);
    }

    private void PopulateOptions(RecipeSearchCategory category)
    {
        ClearContent(optionsContent);
        if (!optionButtonPrefab || !optionsContent || !tagPalette) return;

        List<TasteMoodTagPaletteDef.TagColorEntry> entries =
            category == RecipeSearchCategory.Mood ? tagPalette.moodEntries : tagPalette.tasteEntries;

        foreach (TasteMoodTagPaletteDef.TagColorEntry entry in entries)
        {
            if (entry == null) continue;
            RecipeSearchOptionButton option = Instantiate(optionButtonPrefab, optionsContent);
            option.Setup(entry.tag, entry.backgroundColor, entry.textColor, interactable: true,
                onSelected: _ => OnTagSelected(category, entry));
        }
    }

    private void OnTagSelected(RecipeSearchCategory category, TasteMoodTagPaletteDef.TagColorEntry entry)
    {
        List<CocktailRecipe> matches = _allBookRecipes
            .Where(recipe => category == RecipeSearchCategory.Mood
                ? recipe.moodTags.Contains(entry.tag)
                : recipe.tasteTags.Contains(entry.tag))
            .ToList();
        PopulateRecipeList(resultsContent, matches, recipe => ShowDetail(recipe, DetailOrigin.CategoryResults));

        if (resultsHeaderButton)
            resultsHeaderButton.Setup(entry.tag, entry.backgroundColor, entry.textColor, interactable: false);

        SetViews(main: false, category: false, results: true, detail: false);
    }

    private void BackFromResults()
    {
        ShowCategory(_currentCategory);
    }

    private void ShowDetail(CocktailRecipe recipe, DetailOrigin origin)
    {
        _detailOrigin = origin;
        recipeDetailUI?.Bind(recipe, tagPalette);
        SetViews(main: false, category: false, results: false, detail: true);
    }

    private void BackFromDetail()
    {
        if (_detailOrigin == DetailOrigin.SearchInline)
        {
            SetSearchActive(true);
            SetViews(main: true, category: false, results: false, detail: false);
        }
        else
        {
            SetViews(main: false, category: false, results: true, detail: false);
        }
    }

    private void PopulateRecipeList(Transform content, List<CocktailRecipe> recipes, Action<CocktailRecipe> onClick)
    {
        ClearContent(content);
        if (!content || !recipeListItemPrefab) return;

        foreach (CocktailRecipe recipe in recipes)
        {
            RecipeListItemUI item = Instantiate(recipeListItemPrefab, content);
            item.Setup(recipe, onClick);
        }
    }

    private void SetViews(bool main, bool category, bool results, bool detail)
    {
        if (mainView)     mainView.SetActive(main);
        if (categoryView) categoryView.SetActive(category);
        if (resultsView)  resultsView.SetActive(results);
        if (detailView)   detailView.SetActive(detail);
    }

    private static void ClearContent(Transform content)
    {
        if (!content) return;
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
    }
}
