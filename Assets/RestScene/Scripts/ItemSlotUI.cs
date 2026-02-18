using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ItemSlotUI : MonoBehaviour
{
    [Header("UI Components")]
    public Image iconImage;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI priceText;
    public Button buyButton;

    private ItemData _data;

    public void Setup(ItemData data)
    {
        _data = data;
        
        // UI 갱신
        iconImage.sprite = data.icon;
        nameText.text = data.itemName;
        priceText.text = $"{data.price:N0} G"; // 1,000 단위 쉼표 표시

        // 버튼 초기화 (중복 리스너 방지)
        buyButton.onClick.RemoveAllListeners();
        buyButton.onClick.AddListener(OnBuyClick);
    }

    void OnBuyClick()
    {
        Debug.Log($"[구매] {_data.itemName} ({_data.price}원)");
        // 여기에 인벤토리 추가 또는 골드 차감 로직 연결
    }
}