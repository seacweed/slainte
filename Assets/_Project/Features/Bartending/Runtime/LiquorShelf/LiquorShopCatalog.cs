using System.Collections.Generic;
using Slainte.Content;
using UnityEngine;

[CreateAssetMenu(menuName = "Shop/Liquor Shop Catalog", fileName = "LiquorShopCatalog")]
public sealed class LiquorShopCatalog : ScriptableObject
{
    public const string DefaultResourcePath = ProjectResourcePaths.BartendingShopCatalog;

    [Header("Products")]
    public List<LiquorCategoryDef> categories = new();
    public List<LiquorBottleDef> bottles = new();

    [Header("Delivery UI")]
    public DeliveryShopPanelUI deliveryPanelPrefab;
    public DeliveryItemSlotUI deliveryItemSlotPrefab;
    public LiquorCategoryButtonUI deliveryCategoryButtonPrefab;

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
