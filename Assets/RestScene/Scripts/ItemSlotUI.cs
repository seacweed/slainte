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

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    public void Setup(LiquorBottleDef def)
    {
        _def = def;

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
            iconImage.sprite = _def.sprite;
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

        bool isFull = GameProgress.Instance.GetBottleAmount(_def.id, 0f) >= _def.MaxAmount;

        if (priceText) priceText.text = unlocked ? $"{_def.price:N0} G" : "";
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
        if (!IsUnlocked()) return;
        if (!GameProgress.Instance.TrySpendMoney(_def.price)) return;

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
