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
    [Tooltip("다른 색상 액체와 섞일 때 자신의 색은 혼합에 반영하지 않고, 함께 섞인 액체의 색을 따릅니다.")]
    public bool inheritMixedLiquidColor;
    public float density = 1f;
    [Tooltip("새로 생성되는 액체 입자에 적용할 온도입니다.")]
    public float servingTemperatureC = 20f;

    [Header("Bottle Geometry Override")]
    [Tooltip("활성화하면 스프라이트 교체 후 병 입구와 클릭 콜라이더를 아래 정규화 좌표로 맞춥니다.")]
    public bool overrideBottleGeometry;
    [Tooltip("클릭 콜라이더와 별개로 병 입구의 액체 생성 위치만 스프라이트별 좌표로 맞춥니다.")]
    public bool overrideBottleLiquidSpawn;
    [Tooltip("병 입구 위치는 유지하고 클릭 콜라이더만 스프라이트별 정규화 좌표로 맞춥니다.")]
    public bool overrideBottleClickCollider;
    [Tooltip("스프라이트 경계의 왼쪽 아래가 (0, 0), 오른쪽 위가 (1, 1)인 병 입구 좌표입니다.")]
    public Vector2 liquidSpawnNormalized = new Vector2(0.5f, 0.98f);
    [Tooltip("병 입구에서 스프라이트의 위쪽 방향으로 더 이동할 거리입니다. 픽셀 단위이며 입자가 병 안에서 생성되는 것을 방지합니다.")]
    [Min(0f)] public float liquidSpawnOutwardPixels = 3f;
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
