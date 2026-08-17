using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Shop/Liquor Shop Catalog", fileName = "LiquorShopCatalog")]
public sealed class LiquorShopCatalog : ScriptableObject
{
    public const string DefaultResourcePath = "Shop/LiquorShopCatalog";

    [Header("Products")]
    public List<LiquorCategoryDef> categories = new();
    public List<LiquorBottleDef> bottles = new();

    [Header("Shared Shop UI")]
    public LiquorCategoryButtonUI categoryButtonPrefab;
    public ItemSlotUI itemSlotPrefab;

    [Header("Delivery UI")]
    public DeliveryShopPanelUI deliveryPanelPrefab;

    [Header("Delivery Character")]
    public Sprite deliveryPortrait;
    public Sprite deliveryBusyPortrait;
    public Sprite deliveryMidPortrait;
    public Sprite deliveryPointPortrait;

    public static LiquorShopCatalog LoadDefault()
    {
        return Resources.Load<LiquorShopCatalog>(DefaultResourcePath);
    }
}
