using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 배송 상점 전용 아이템 슬롯. ItemSlotUI와 로직은 동일하되, 원가(배율 적용 전 가격)를
// 구매 버튼 아래에 회색 취소선으로 같이 보여주는 부분만 추가된 별도 프리팹용 컴포넌트.
public class DeliveryItemSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
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

    [Header("Original Price (before delivery markup)")]
    [Tooltip("Shown below the buy button in gray — the un-multiplied base (shop) price.")]
    public TextMeshProUGUI originalPriceText;
    [Tooltip("Diagonal strike-through line overlaid on originalPriceText. Toggled together with it.")]
    public GameObject originalPriceStrike;
    [Tooltip("Currency unit icon shown before originalPriceText. Toggled together with it.")]
    public GameObject originalPriceCurrencyIcon;

    public event Action OnPurchased;

    private LiquorBottleDef _def;
    private IShopCurrency   _currency;
    private RectTransform   _rectTransform;
    private float           _priceMultiplier = 1f;
    private Func<int, bool> _trySpendMoney;
    private float           _defaultInventoryAmount;

    public LiquorBottleDef Definition => _def;
    public float PriceMultiplier => _priceMultiplier;
    public int CurrentPrice => CalculatePrice(GetBasePrice(), _priceMultiplier);

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    public void Setup(LiquorBottleDef def, IShopCurrency currency = null)
    {
        Configure(
            def,
            currency ?? new MoneyShopCurrency(),
            1f,
            null,
            def != null ? def.DefaultAmount : 0f);
    }

    public void Setup(
        LiquorBottleDef def,
        IShopCurrency currency,
        float priceMultiplier,
        Func<int, bool> trySpendMoney,
        float defaultInventoryAmount)
    {
        Configure(
            def,
            currency ?? new MoneyShopCurrency(),
            priceMultiplier,
            trySpendMoney,
            defaultInventoryAmount);
    }

    // 배송 상점
    public void Setup(
        LiquorBottleDef def,
        float priceMultiplier,
        Func<int, bool> trySpendMoney = null,
        float defaultInventoryAmount = 0f)
    {
        Configure(
            def,
            new MoneyShopCurrency(),
            priceMultiplier,
            trySpendMoney,
            defaultInventoryAmount);
    }

    private void Configure(
        LiquorBottleDef def,
        IShopCurrency currency,
        float priceMultiplier,
        Func<int, bool> trySpendMoney,
        float defaultInventoryAmount)
    {
        _def = def;
        _currency = currency ?? new MoneyShopCurrency();
        _priceMultiplier = Mathf.Max(0f, priceMultiplier);
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
        int  basePrice = GetBasePrice();
        int  price = CalculatePrice(basePrice, _priceMultiplier);
        bool hasValidPrice = basePrice >= 0;
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

        if (originalPriceText != null)
        {
            originalPriceText.gameObject.SetActive(unlocked && hasValidPrice);
            if (hasValidPrice) originalPriceText.text = $"{basePrice:N0}";
        }
        if (originalPriceStrike != null)
            originalPriceStrike.SetActive(unlocked && hasValidPrice);
        if (originalPriceCurrencyIcon != null)
            originalPriceCurrencyIcon.SetActive(unlocked && hasValidPrice);
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

        int basePrice = GetBasePrice();
        if (basePrice < 0) return false;

        int price = CalculatePrice(basePrice, _priceMultiplier);
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

    public static int CalculatePrice(int basePrice, float multiplier)
    {
        return Mathf.Max(
            0,
            Mathf.CeilToInt(Mathf.Max(0, basePrice) * Mathf.Max(0f, multiplier)));
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
