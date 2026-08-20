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

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    // currency를 생략하면 일반 상점(원화)으로 동작 — 기존 호출부와 호환됨.
    public void Setup(LiquorBottleDef def, IShopCurrency currency = null)
    {
        _def = def;
        _currency = currency ?? new MoneyShopCurrency();

        buyButton.onClick.RemoveAllListeners();
        buyButton.onClick.AddListener(OnBuyClick);

        Refresh();
    }

    public void Refresh()
    {
        if (_def == null) return;

        bool unlocked = IsUnlocked();

        if (iconImage != null)
        {
            iconImage.sprite = _def.shopSprite;
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

        int  price = _currency.GetPrice(_def);
        bool isFull = GameProgress.Instance.GetBottleAmount(_def.id, 0f) >= _def.MaxAmount;
        bool canAfford = _currency.CurrentAmount >= price;
        bool insufficientFunds = unlocked && !isFull && !canAfford;

        if (priceText)
        {
            priceText.text = unlocked ? $"{price:N0}" : "";
            priceText.color = insufficientFunds ? priceColorInsufficient : priceColorNormal;
        }
        if (currencyIcon) currencyIcon.SetActive(unlocked);
        if (buyButton)    buyButton.interactable = unlocked && !isFull && canAfford;

        if (buyButtonImage != null && buyButtonOnSprite != null && buyButtonOffSprite != null)
            buyButtonImage.sprite = insufficientFunds ? buyButtonOffSprite : buyButtonOnSprite;
    }

    private bool IsUnlocked()
    {
        return _def != null
            && (string.IsNullOrEmpty(_def.unlockFlagKey)
                || GameProgress.Instance.HasFlag(_def.unlockFlagKey));
    }

    private void OnBuyClick()
    {
        if (!IsUnlocked()) return;
        if (!_currency.TrySpend(_currency.GetPrice(_def))) return;

        GameProgress.Instance.AddBottleAmount(_def.id, _def.unitVolume, _def.MaxAmount);
        Refresh();
        OnPurchased?.Invoke();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_def == null) return;

        if (IsUnlocked())
            LiquorBottleInfoCard.Instance?.Show(_def, GameProgress.Instance.GetBottleAmount(_def.id, 0f), _rectTransform);
        else
            IngredientUnlockTooltip.Instance?.Show(_def, _rectTransform);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        LiquorBottleInfoCard.Instance?.Hide();
        IngredientUnlockTooltip.Instance?.Hide();
    }
}
