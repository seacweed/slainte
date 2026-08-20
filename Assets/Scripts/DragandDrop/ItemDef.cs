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
    public string englishName;
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

    [Header("Bottle Geometry Override")]
    [Tooltip("활성화하면 스프라이트 교체 후 병 입구와 클릭 콜라이더를 아래 정규화 좌표로 맞춥니다.")]
    public bool overrideBottleGeometry;
    [Tooltip("스프라이트 경계의 왼쪽 아래가 (0, 0), 오른쪽 위가 (1, 1)인 병 입구 좌표입니다.")]
    public Vector2 liquidSpawnNormalized = new Vector2(0.5f, 0.98f);
    [Tooltip("스프라이트 경계 기준 BoxCollider2D 중심 좌표입니다.")]
    public Vector2 colliderCenterNormalized = new Vector2(0.5f, 0.5f);
    [Tooltip("스프라이트 크기에 곱할 BoxCollider2D 크기 비율입니다.")]
    public Vector2 colliderSizeNormalized = Vector2.one;

    [Header("Rules")]
    [Tooltip("true면 원본이 이동하는 아이템으로 취급합니다. Bottle은 true 권장. false면 원본 유지 + 복사본 생성 방식으로 사용합니다.")]
    public bool dragMovesObject = false;

    [Tooltip("Tool 중 테이블 위에 놓을 수 있는 도구만 true로 설정합니다. Tool이 아닌 타입에서는 무시됩니다.")]
    public bool toolPlaceableOnTable = false;
}
