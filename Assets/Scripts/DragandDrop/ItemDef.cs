using UnityEngine;

public enum ItemType
{
    Bottle = 0,
    Glass = 1,
    Tool = 2
}

public enum TasteTag
{
    Sweet,
    Bitter,
    Sour,
    SweetSour,
    RichSweet,
    Neutral
}

public enum BottleCategory
{
    Spirit,
    Liqueur,
    Syrup,
    Juice,
    NonAlcohol,
    Other
}

public enum BottleLiquidType
{
    Vodka,
    Whisky,
    Rum,
    Gin,
    Brandy,
    Juice,
    Syrup,
    Other
}

[CreateAssetMenu(menuName = "Bartending/Item")]
public class ItemDef : ScriptableObject
{
    [Header("Common")]
    public string id;
    public string displayName;
    public ItemType type;
    public Sprite icon;
    public int price;

    [Header("Bottle Only")]
    public TasteTag tasteTag;
    public float abvPercent;
    public BottleCategory bottleCategory;
    public BottleLiquidType liquidType;
    public float capacityMl = 700f;
    public Color liquidColor = Color.white;
    public float density = 1f;
    [Tooltip("새로 생성되는 액체 입자에 적용할 온도입니다.")]
    public float servingTemperatureC = 20f;

    [Header("Rules")]
    [Tooltip("true면 원본이 이동하는 아이템으로 취급합니다. Bottle은 true 권장. false면 원본 유지 + 복사본 생성 방식으로 사용합니다.")]
    public bool dragMovesObject = false;

    [Tooltip("Tool 중 테이블 위에 놓을 수 있는 도구만 true로 설정합니다. Tool이 아닌 타입에서는 무시됩니다.")]
    public bool toolPlaceableOnTable = false;
}
