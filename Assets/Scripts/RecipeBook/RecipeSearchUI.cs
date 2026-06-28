using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public enum RecipeSearchCategory
{
    Mood,
    Taste,
    Ingredient
}

public class RecipeSearchUI : MonoBehaviour
{
    [Serializable]
    private class OptionEntry
    {
        public string label;
        public Color  color     = Color.white;
        public Color  textColor = Color.black;
    }

    [Serializable]
    private class CategoryConfig
    {
        public RecipeSearchCategory category;
        public string                title;
        public Sprite                headerIcon;
        public List<OptionEntry>     options = new List<OptionEntry>();
    }

    [Header("Views")]
    [SerializeField] private GameObject mainView;
    [SerializeField] private GameObject categoryView;
    [SerializeField] private GameObject resultsView;

    [Header("Category Header (Category View)")]
    [SerializeField] private TMP_Text categoryTitleText;
    [SerializeField] private Image    categoryHeaderIcon;
    [SerializeField] private Button   backButton;

    [Header("Results Header (Results View)")]
    [SerializeField] private TMP_Text resultsTitleText;
    [SerializeField] private Image    resultsTitleBackground;
    [SerializeField] private Button   resultsBackButton;

    [Header("Category Buttons (Main View)")]
    [SerializeField] private Button moodButton;
    [SerializeField] private Button tasteButton;
    [SerializeField] private Button ingredientButton;

    [Header("Category Options (Category View)")]
    [SerializeField] private Transform                optionsContent;
    [SerializeField] private RecipeSearchOptionButton  optionButtonPrefab;

    [Header("Category Configs")]
    [SerializeField]
    private List<CategoryConfig> categoryConfigs = new List<CategoryConfig>
    {
        new CategoryConfig { category = RecipeSearchCategory.Mood },
        new CategoryConfig { category = RecipeSearchCategory.Taste },
        new CategoryConfig { category = RecipeSearchCategory.Ingredient },
    };

    private readonly List<RecipeSearchOptionButton> _spawnedOptions = new List<RecipeSearchOptionButton>();
    private RecipeSearchCategory _currentCategory;

    void Awake()
    {
        if (moodButton)       moodButton.onClick.AddListener(() => ShowCategory(RecipeSearchCategory.Mood));
        if (tasteButton)      tasteButton.onClick.AddListener(() => ShowCategory(RecipeSearchCategory.Taste));
        if (ingredientButton) ingredientButton.onClick.AddListener(() => ShowCategory(RecipeSearchCategory.Ingredient));
        if (backButton)       backButton.onClick.AddListener(ShowMain);
        if (resultsBackButton) resultsBackButton.onClick.AddListener(BackFromResults);

        ShowMain();
    }

    public void ShowMain()
    {
        ClearOptions();

        if (mainView)     mainView.SetActive(true);
        if (categoryView) categoryView.SetActive(false);
        if (resultsView)  resultsView.SetActive(false);
    }

    public void ShowCategory(RecipeSearchCategory category)
    {
        CategoryConfig config = FindConfig(category);
        if (config == null) return;

        _currentCategory = category;

        if (categoryTitleText) categoryTitleText.text = config.title;
        if (categoryHeaderIcon)
        {
            categoryHeaderIcon.sprite = config.headerIcon;
            categoryHeaderIcon.gameObject.SetActive(config.headerIcon != null);
        }
        if (mainView)     mainView.SetActive(false);
        if (categoryView) categoryView.SetActive(true);
        if (resultsView)  resultsView.SetActive(false);

        PopulateOptions(config);
    }

    // Called by RecipeBookUI only when the book transitions from disabled back to enabled
    // (e.g. EpisodeMode -> OrderMode/CraftingMode). Plain Tab-toggle open/close must not reset.
    public void ResetToMain() => ShowMain();

    // An option button was picked -> drill into the cocktail list for that option.
    // Carries the option's colors over so the results header reads as the same "tag".
    public void NotifyOptionClicked(string label, Color color, Color textColor)
    {
        if (resultsTitleText)
        {
            resultsTitleText.text  = label;
            resultsTitleText.color = textColor;
        }
        if (resultsTitleBackground) resultsTitleBackground.color = color;
        if (categoryView) categoryView.SetActive(false);
        if (resultsView)  resultsView.SetActive(true);
    }

    private void BackFromResults()
    {
        if (resultsView) resultsView.SetActive(false);
        ShowCategory(_currentCategory);
    }

    private CategoryConfig FindConfig(RecipeSearchCategory category)
    {
        foreach (CategoryConfig c in categoryConfigs)
            if (c.category == category) return c;
        return null;
    }

    private void PopulateOptions(CategoryConfig config)
    {
        ClearOptions();
        if (!optionButtonPrefab || !optionsContent) return;

        foreach (OptionEntry entry in config.options)
        {
            RecipeSearchOptionButton option = Instantiate(optionButtonPrefab, optionsContent);
            option.Setup(entry.label, entry.color, entry.textColor, this);
            _spawnedOptions.Add(option);
        }
    }

    private void ClearOptions()
    {
        foreach (RecipeSearchOptionButton option in _spawnedOptions)
            if (option) Destroy(option.gameObject);

        _spawnedOptions.Clear();
    }
}
