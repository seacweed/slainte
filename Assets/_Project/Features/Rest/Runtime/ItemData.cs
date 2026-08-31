using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName = "Shop/Item Data")]
public class ItemData : ScriptableObject
{
    public string itemName;        // 아이템 이름
    public Sprite icon;            // 아이콘 이미지
    public int price;              // 가격
    [TextArea] public string desc; // 설명

    // 이미지에 있는 6개 카테고리 정의
    public enum ItemType 
    { 
        Alcohol,    // 술
        Liqueur,    // 리큐르
        NonAlcohol, // 무알콜
        Powder,     // 파우더
        Tool,       // 도구
        Glass       // 잔
    }
    public ItemType category;
}