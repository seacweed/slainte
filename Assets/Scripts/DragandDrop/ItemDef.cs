using UnityEngine;

public enum ItemType { Bottle, Glass, Tool }

[CreateAssetMenu(menuName="Bartending/Item")]
public class ItemDef : ScriptableObject
{
    public string id;
    public ItemType type;
    public Sprite icon;

    [Header("Rules")]
    [Tooltip("true면 원본이 이동하는 아이템(예: 술병). false면 원본 유지+복사(예: 잔/도구)")]
    public bool dragMovesObject = false; // Bottle = true 권장

    [Tooltip("Tool 중 테이블 위에 '놓을 수 있는' 도구만 true (예: 셰이커). Tool 아닌 타입은 무시")]
    public bool toolPlaceableOnTable = false;
}