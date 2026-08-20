using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DeliveryShopPanelUI : MonoBehaviour
{
    [Header("Copied Shop Hierarchy")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI moneyText;
    [SerializeField] private GameObject homePanel;
    [SerializeField] private GameObject ingredientCategoryPanel;
    [SerializeField] private GameObject itemScrollPanel;
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private Transform categoryButtonContent;
    [SerializeField] private LiquorCategoryButtonUI categoryButtonPrefab;
    [SerializeField] private TextMeshProUGUI categoryNameText;
    [SerializeField] private ScrollRect itemScrollView;
    [SerializeField] private Transform contentRoot;
    [SerializeField] private ItemSlotUI itemSlotPrefab;

    private readonly List<LiquorCategoryButtonUI> categoryButtons = new();
    private readonly List<ItemSlotUI> itemSlots = new();
    private LiquorShopCatalog catalog;
    private float priceMultiplier = 2f;
    private bool initialized;

    public event Action Purchased;
    public event Action CloseRequested;

    public bool IsVisible => gameObject.activeSelf;
    public int VisibleItemCount => itemSlots.Count;
    public float PriceMultiplier => priceMultiplier;

    public void ConfigureFromShopTemplate(ShopUIManager source)
    {
        if (source == null) return;

        canvasGroup = source.GetComponent<CanvasGroup>();
        moneyText = source.moneyText;
        homePanel = source.homePanel;
        ingredientCategoryPanel = source.ingredientCategoryPanel;
        itemScrollPanel = source.itemScrollPanel;
        upgradePanel = source.upgradePanel;
        categoryButtonContent = source.categoryButtonContent;
        categoryButtonPrefab = source.categoryButtonPrefab;
        categoryNameText = source.categoryNameText;
        itemScrollView = source.itemScrollView;
        contentRoot = source.contentRoot;
        itemSlotPrefab = source.slotPrefab != null
            ? source.slotPrefab.GetComponent<ItemSlotUI>()
            : null;
    }

    public void Initialize(LiquorShopCatalog sharedCatalog, float multiplier)
    {
        catalog = sharedCatalog != null ? sharedCatalog : LiquorShopCatalog.LoadDefault();
        priceMultiplier = Mathf.Max(0f, multiplier);

        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (!initialized)
        {
            RebindCopiedButtons();
            BuildCategoryButtons();
            initialized = true;
        }

        ShowCategories();
        HideImmediate();
    }

    public void Show()
    {
        gameObject.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        ShowCategories();
        Refresh();
    }

    public void HideImmediate()
    {
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        gameObject.SetActive(false);
    }

    public void SetInteractable(bool interactable)
    {
        if (canvasGroup != null)
        {
            canvasGroup.interactable = interactable;
            canvasGroup.blocksRaycasts = interactable;
        }
    }

    public void ShowCategories()
    {
        SetScreen(ingredientCategoryPanel);
        RefreshMoneyText();
    }

    public void ShowListByCategory(LiquorCategoryDef category)
    {
        SetScreen(itemScrollPanel);
        if (categoryNameText != null)
            categoryNameText.text = category != null ? category.displayName : string.Empty;

        RebuildItems(category);
        if (itemScrollView != null) itemScrollView.verticalNormalizedPosition = 1f;
    }

    public bool ShowFirstCategory()
    {
        if (catalog?.categories == null) return false;
        for (int i = 0; i < catalog.categories.Count; i++)
        {
            LiquorCategoryDef category = catalog.categories[i];
            if (category == null) continue;
            ShowListByCategory(category);
            return true;
        }
        return false;
    }

    public void RequestClose()
    {
        CloseRequested?.Invoke();
    }

    public void Refresh()
    {
        for (int i = 0; i < itemSlots.Count; i++)
            if (itemSlots[i] != null) itemSlots[i].Refresh();
        RefreshMoneyText();
    }

    private void BuildCategoryButtons()
    {
        if (categoryButtonContent == null || categoryButtonPrefab == null || catalog?.categories == null)
            return;

        for (int i = categoryButtonContent.childCount - 1; i >= 0; i--)
            Destroy(categoryButtonContent.GetChild(i).gameObject);
        categoryButtons.Clear();

        foreach (LiquorCategoryDef category in catalog.categories)
        {
            if (category == null) continue;
            LiquorCategoryButtonUI button = Instantiate(categoryButtonPrefab, categoryButtonContent);
            button.Bind(category, ShowListByCategory);
            categoryButtons.Add(button);
        }
    }

    private void RebuildItems(LiquorCategoryDef category)
    {
        ClearItems();
        if (contentRoot == null || itemSlotPrefab == null || catalog?.bottles == null)
            return;

        IEnumerable<LiquorBottleDef> products = catalog.bottles.Where(bottle =>
            bottle != null && (category == null || bottle.category == category));
        foreach (LiquorBottleDef bottle in products)
        {
            ItemSlotUI slot = Instantiate(itemSlotPrefab, contentRoot);
            slot.Setup(bottle, priceMultiplier, null, bottle.DefaultAmount);
            slot.OnPurchased += HandlePurchased;
            itemSlots.Add(slot);
        }
    }

    private void ClearItems()
    {
        for (int i = 0; i < itemSlots.Count; i++)
        {
            if (itemSlots[i] == null) continue;
            itemSlots[i].OnPurchased -= HandlePurchased;
            Destroy(itemSlots[i].gameObject);
        }
        itemSlots.Clear();
    }

    private void RebindCopiedButtons()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            bool bound = false;
            int persistentCount = button.onClick.GetPersistentEventCount();
            for (int listener = 0; listener < persistentCount; listener++)
            {
                string method = button.onClick.GetPersistentMethodName(listener);
                if (method == nameof(ShopUIManager.ShowHome)
                    || method == nameof(ShopUIManager.OpenIngredients))
                {
                    button.onClick.AddListener(ShowCategories);
                    bound = true;
                }
                else if (method == nameof(BaseUIManager.CloseUI))
                {
                    button.onClick.AddListener(RequestClose);
                    bound = true;
                }
                else if (method == nameof(ShowCategories)
                    || method == nameof(RequestClose))
                {
                    bound = true;
                }
            }

            if (!bound && string.Equals(button.name, "ExitButton", StringComparison.OrdinalIgnoreCase))
                button.onClick.AddListener(RequestClose);
        }
    }

    private void SetScreen(GameObject target)
    {
        if (homePanel != null) homePanel.SetActive(false);
        if (ingredientCategoryPanel != null)
            ingredientCategoryPanel.SetActive(target == ingredientCategoryPanel);
        if (itemScrollPanel != null) itemScrollPanel.SetActive(target == itemScrollPanel);
        if (upgradePanel != null) upgradePanel.SetActive(false);
    }

    private void RefreshMoneyText()
    {
        GameProgress progress = GameProgress.Instance;
        if (moneyText != null)
            moneyText.text = progress != null ? $"{progress.CurrentMoney:N0} G" : "-";
    }

    private void HandlePurchased()
    {
        Refresh();
        Purchased?.Invoke();
    }
}
