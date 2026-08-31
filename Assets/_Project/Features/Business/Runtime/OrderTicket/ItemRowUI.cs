using UnityEngine;
using TMPro;

public class ItemRowUI : MonoBehaviour
{
    [SerializeField] TMP_Text itemNameText;
    [SerializeField] TMP_Text qtyText;
    [SerializeField] TMP_Text priceText;

    public void Set(string itemName, int qty, int price)
    {
        if (itemNameText) itemNameText.text = itemName;
        if (qtyText) qtyText.text = qty.ToString();
        if (priceText) priceText.text = $"{price}$";
    }
}
