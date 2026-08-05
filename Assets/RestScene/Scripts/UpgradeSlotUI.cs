using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UpgradeSlotUI : MonoBehaviour
{
    [Header("UI Components")]
    public Image iconImage;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI descText;
    [Tooltip("Fixed slots, one per level. Extra slots beyond UpgradeDef.MaxLevel are hidden.")]
    public Image[] levelPips;
    public TextMeshProUGUI priceText;
    public Button buyButton;

    [Header("Pip Colors")]
    public Color filledColor = Color.white;
    public Color emptyColor  = new Color(1f, 1f, 1f, 0.3f);

    public event Action OnPurchased;

    private UpgradeDef _def;

    public void Bind(UpgradeDef def)
    {
        _def = def;

        if (iconImage) iconImage.sprite = def.icon;
        if (nameText)  nameText.text    = def.displayName;
        if (descText)  descText.text    = def.description;

        buyButton.onClick.RemoveAllListeners();
        buyButton.onClick.AddListener(OnBuyClick);

        Refresh();
    }

    public void Refresh()
    {
        int level    = GameProgress.Instance.GetUpgradeLevel(_def.id);
        int maxLevel = _def.MaxLevel;

        for (int i = 0; i < levelPips.Length; i++)
        {
            Image pip = levelPips[i];
            if (pip == null) continue;

            bool inRange = i < maxLevel;
            pip.gameObject.SetActive(inRange);
            if (inRange) pip.color = i < level ? filledColor : emptyColor;
        }

        bool isMax = level >= maxLevel;
        if (buyButton)  buyButton.interactable = !isMax;
        if (priceText)  priceText.text = isMax ? "MAX" : $"{_def.pricesPerLevel[level]:N0} G";
    }

    private void OnBuyClick()
    {
        int level = GameProgress.Instance.GetUpgradeLevel(_def.id);
        if (level >= _def.MaxLevel) return;

        int price = _def.pricesPerLevel[level];
        if (!GameProgress.Instance.TrySpendMoney(price)) return;

        GameProgress.Instance.SetUpgradeLevel(_def.id, level + 1);
        Refresh();
        OnPurchased?.Invoke();
    }
}
