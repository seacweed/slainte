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
    public Button buyButton;
    [Tooltip("Shown instead of name/subCategory when the ingredient is locked.")]
    public GameObject lockedLabel;

    public event Action OnPurchased;

    private LiquorBottleDef _def;
    private RectTransform   _rectTransform;
    private float           _priceMultiplier = 1f;
    private Func<int, bool> _trySpendMoney;
    private float           _defaultInventoryAmount;

    public LiquorBottleDef Definition => _def;
    public float PriceMultiplier => _priceMultiplier;
    public int CurrentPrice => CalculatePrice(_def != null ? _def.price : 0, _priceMultiplier);

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    public void Setup(LiquorBottleDef def)
    {
        Setup(def, 1f, null, 0f);
    }

    public void Setup(
        LiquorBottleDef def,
        float priceMultiplier,
        Func<int, bool> trySpendMoney = null,
        float defaultInventoryAmount = 0f)
    {
        _def = def;
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
        bool isFull = progress == null
            || progress.GetBottleAmount(_def.id, _defaultInventoryAmount) >= _def.MaxAmount;

        if (priceText) priceText.text = unlocked ? $"{CurrentPrice:N0} G" : "";
        if (buyButton) buyButton.interactable = unlocked && !isFull;
    }

    private bool IsUnlocked()
    {
        return _def != null
            && (string.IsNullOrEmpty(_def.unlockFlagKey)
                || GameProgress.Instance.HasFlag(_def.unlockFlagKey));
    }

    private void OnBuyClick()
    {
        TryPurchase();
    }

    public bool TryPurchase()
    {
        GameProgress progress = GameProgress.Instance;
        if (!IsUnlocked() || progress == null) return false;
        if (progress.GetBottleAmount(_def.id, _defaultInventoryAmount) >= _def.MaxAmount) return false;

        Func<int, bool> spend = _trySpendMoney ?? progress.TrySpendMoney;
        if (!spend(CurrentPrice)) return false;

        progress.AddBottleAmount(_def.id, _def.unitVolume, _def.MaxAmount);
        Refresh();
        OnPurchased?.Invoke();
        return true;
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
                GameProgress.Instance.GetBottleAmount(_def.id, _defaultInventoryAmount),
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
