using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RecipeBookSlotUI : MonoBehaviour
{
    [Header("Left: Image + Price/Buy")]
    public Image  iconImage;
    public TextMeshProUGUI priceText;
    [Tooltip("Currency unit icon shown before priceText. Hidden when the price text shows \"구매완료\" instead of a price.")]
    public GameObject currencyIcon;
    public Button buyButton;

    [Header("Insufficient Funds")]
    [Tooltip("buyButton's Image component. Swapped between buyButtonOnSprite/buyButtonOffSprite by affordability.")]
    public Image buyButtonImage;
    public Sprite buyButtonOnSprite;
    public Sprite buyButtonOffSprite;
    public Color priceColorNormal = Color.white;
    public Color priceColorInsufficient = Color.red;

    [Header("Right: Name + Description + Unlock Info")]
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI descText;
    public TextMeshProUGUI unlockInfoText;

    public event Action OnPurchased;

    private RecipeBookDef _def;

    public void Bind(RecipeBookDef def)
    {
        _def = def;

        if (iconImage)
        {
            iconImage.sprite = def.icon;
            iconImage.preserveAspect = true;
        }
        if (nameText)       nameText.text       = def.displayName;
        if (descText)       descText.text       = CategoryColorText.Highlight(def.description);
        if (unlockInfoText) unlockInfoText.text = CategoryColorText.Highlight(def.unlockInfoText);

        buyButton.onClick.RemoveAllListeners();
        buyButton.onClick.AddListener(OnBuyClick);

        Refresh();
    }

    public void Refresh()
    {
        bool purchased = GameProgress.Instance.HasFlag(_def.PurchasedFlagKey);
        bool canAfford = purchased || GameProgress.Instance.CurrentMoney >= _def.price;
        bool insufficientFunds = !purchased && !canAfford;

        if (buyButton)    buyButton.interactable = !purchased && canAfford;
        if (priceText)
        {
            priceText.text = purchased ? "구매완료" : $"{_def.price:N0}";
            priceText.color = insufficientFunds ? priceColorInsufficient : priceColorNormal;
        }
        if (currencyIcon) currencyIcon.SetActive(!purchased);

        if (buyButtonImage != null && buyButtonOnSprite != null && buyButtonOffSprite != null)
            buyButtonImage.sprite = insufficientFunds ? buyButtonOffSprite : buyButtonOnSprite;
    }

    private void OnBuyClick()
    {
        if (GameProgress.Instance.HasFlag(_def.PurchasedFlagKey)) return;
        if (!GameProgress.Instance.TrySpendMoney(_def.price)) return;

        GameProgress.Instance.SetFlag(_def.PurchasedFlagKey);
        foreach (var flag in _def.unlockFlagKeys) GameProgress.Instance.SetFlag(flag);

        Refresh();
        OnPurchased?.Invoke();
    }
}
