using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ItemSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI Components")]
    public Image iconImage;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI subCategoryText;
    public TextMeshProUGUI priceText;
    [Tooltip("Currency unit icon shown before priceText. Hidden together with priceText when locked.")]
    public GameObject currencyIcon;
    public Button buyButton;
    [Tooltip("Shown instead of name/subCategory when the ingredient is locked.")]
    public GameObject lockedLabel;

    [Header("Insufficient Funds")]
    [Tooltip("buyButton's Image component. Swapped between buyButtonOnSprite/buyButtonOffSprite by affordability.")]
    public Image buyButtonImage;
    public Sprite buyButtonOnSprite;
    public Sprite buyButtonOffSprite;
    public Color priceColorNormal = Color.white;
    public Color priceColorInsufficient = Color.red;

    public event Action OnPurchased;

    private LiquorBottleDef _def;
    private IShopCurrency   _currency;
    private RectTransform   _rectTransform;
    private Func<int, bool> _trySpendMoney;
    private float           _defaultInventoryAmount;

    public LiquorBottleDef Definition => _def;
    public int CurrentPrice => GetBasePrice();

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    public void Setup(LiquorBottleDef def, IShopCurrency currency = null)
    {
        Configure(
            def,
            currency ?? new MoneyShopCurrency(),
            null,
            def != null ? def.DefaultAmount : 0f);
    }

    public void Setup(
        LiquorBottleDef def,
        IShopCurrency currency,
        Func<int, bool> trySpendMoney,
        float defaultInventoryAmount)
    {
        Configure(
            def,
            currency ?? new MoneyShopCurrency(),
            trySpendMoney,
            defaultInventoryAmount);
    }

    private void Configure(
        LiquorBottleDef def,
        IShopCurrency currency,
        Func<int, bool> trySpendMoney,
        float defaultInventoryAmount)
    {
        _def = def;
        _currency = currency ?? new MoneyShopCurrency();
        _trySpendMoney = trySpendMoney;
        _defaultInventoryAmount = Mathf.Max(0f, defaultInventoryAmount);

        if (buyButton != null)
        {
            buyButton.onClick.RemoveAllListeners();
            buyButton.onClick.AddListener(OnBuyClick);
        }

        Refresh();
    }

    public void Refresh()
    {
        if (_def == null) return;

        bool unlocked = IsUnlocked();

        if (iconImage != null)
        {
            Sprite shopSprite = _def.GetShopSprite();
            iconImage.preserveAspect = true;
            iconImage.sprite = shopSprite;
            iconImage.enabled = shopSprite != null;
            iconImage.color  = unlocked ? Color.white : Color.black;
            iconImage.preserveAspect = true;
        }

        if (nameText)        nameText.gameObject.SetActive(unlocked);
        if (subCategoryText) subCategoryText.gameObject.SetActive(unlocked);
        if (lockedLabel)      lockedLabel.SetActive(!unlocked);

        if (unlocked)
        {
            if (nameText)        nameText.text        = _def.displayName;
            if (subCategoryText) subCategoryText.text = _def.subCategory;
        }

        GameProgress progress = GameProgress.Instance;
        int  price = GetBasePrice();
        bool hasValidPrice = price >= 0;
        bool isFull = progress != null
            && progress.EnsureBottleAmount(_def.InventoryId, _defaultInventoryAmount) >= _def.MaxAmount;
        bool canAfford = hasValidPrice
            && progress != null
            && (price == 0 || _currency.CurrentAmount >= price);
        bool insufficientFunds = unlocked && !isFull && !canAfford;
        bool notBuyable = unlocked && (isFull || !canAfford);

        if (priceText)
        {
            priceText.text = unlocked ? $"{price:N0}" : "";
            priceText.color = insufficientFunds ? priceColorInsufficient : priceColorNormal;
        }
        if (currencyIcon) currencyIcon.SetActive(unlocked);
        if (buyButton)    buyButton.interactable = unlocked && !isFull && canAfford;

        if (buyButtonImage != null && buyButtonOnSprite != null && buyButtonOffSprite != null)
            buyButtonImage.sprite = notBuyable ? buyButtonOffSprite : buyButtonOnSprite;
    }

    private bool IsUnlocked()
    {
        GameProgress progress = GameProgress.Instance;
        return _def != null
            && (string.IsNullOrEmpty(_def.unlockFlagKey)
                || (progress != null && progress.HasFlag(_def.unlockFlagKey)));
    }

    private void OnBuyClick()
    {
        TryPurchase();
    }

    public bool TryPurchase()
    {
        GameProgress progress = GameProgress.Instance;
        if (!IsUnlocked() || progress == null) return false;
        string inventoryId = _def.InventoryId;
        if (progress.EnsureBottleAmount(inventoryId, _defaultInventoryAmount) >= _def.MaxAmount)
            return false;

        int price = GetBasePrice();
        if (price < 0) return false;

        if (price > 0)
        {
            bool spent = _trySpendMoney != null
                ? _trySpendMoney(price)
                : _currency != null
                    ? _currency.TrySpend(price)
                    : progress.TrySpendMoney(price);
            if (!spent) return false;
        }

        progress.AddBottleAmount(inventoryId, _def.unitVolume, _def.MaxAmount);
        Refresh();
        OnPurchased?.Invoke();
        return true;
    }

    private int GetBasePrice()
    {
        if (_currency != null)
            return _currency.GetPrice(_def);
        return _def != null ? _def.price : 0;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_def == null) return;

        if (IsUnlocked())
            LiquorBottleInfoCard.Instance?.Show(
                _def,
                GameProgress.Instance.EnsureBottleAmount(_def.InventoryId, _defaultInventoryAmount),
                _rectTransform);
        else
            IngredientUnlockTooltip.Instance?.Show(_def, _rectTransform);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        LiquorBottleInfoCard.Instance?.Hide();
        IngredientUnlockTooltip.Instance?.Hide();
    }
}
