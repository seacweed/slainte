using UnityEngine;
using UnityEngine.UI;

public class CategoryButton : MonoBehaviour
{
    [Header("Settings")]
    public ItemData.ItemType targetCategory; // 인스펙터에서 설정 (술, 도구 등)
    private Button btn;

    // 매니저가 시작할 때 이 함수를 호출해줌
    public void Init(ShopUIManager manager)
    {
        btn = GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => manager.ShowListByCategory(targetCategory));
    }
}