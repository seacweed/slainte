using UnityEngine;
using Slainte.Economy;

public enum IngredientUnlockHintType
{
    None,
    RecipeBook,
    Episode
}

[CreateAssetMenu(menuName = "Bartending/Liquor Bottle")]
public class LiquorBottleDef : ScriptableObject
{
    public string   id;
    public string   displayName;
    [Tooltip("ItemDef consumed by bartending. The CSV importer connects the matching item explicitly.")]
    public ItemDef  item;
    [Tooltip("Legacy context-neutral bottle image. Kept as the fallback for existing assets.")]
    public Sprite   sprite;

    [Header("Context Images")]
    [Tooltip("Use context images even when every context is intentionally empty. Any assigned context image also enables strict context mode.")]
    public bool     useContextImages;
    [Tooltip("Bottle image used in shops. Expected artwork suffix: _blank.")]
    public Sprite   shopBlankSprite;
    [Tooltip("Capped bottle image used on the liquor shelf. Expected artwork suffix: _lid.")]
    public Sprite   shelfLidSprite;
    [Tooltip("Uncapped bottle image used on the bar table. Expected artwork: base file name without a suffix.")]
    public Sprite   barSprite;
    [Tooltip("GameProgress flag key. Empty = always unlocked.")]
    public string   unlockFlagKey;
    public string   subCategory;
    [Tooltip("Number of bottle icons shown in the info card.")]
    public int      bottleCount = 6;
    [Tooltip("Number of full bottles owned when no saved inventory exists yet.")]
    public int      defaultBottleCount;
    [Tooltip("Volume per bottle, e.g. 700 (ml).")]
    public float    unitVolume = 700f;

    [Header("Shop")]
    [Tooltip("Shop category grouping. Null = not sold in the shop.")]
    public LiquorCategoryDef category;
    [Tooltip("Price for one bottle (unitVolume) in the shop.")]
    public int      price;
    [Tooltip("Price for one bottle (unitVolume) in the strange shop, paid with strange coins.")]
    public int      strangeCoinPrice;

    [Header("Shop Lock Hint (display only, unlock itself uses unlockFlagKey)")]
    public IngredientUnlockHintType unlockHintType = IngredientUnlockHintType.None;
    [Tooltip("Used when unlockHintType == RecipeBook.")]
    public Sprite   recipeBookIcon;
    public string   recipeBookName;

    public float MaxAmount => bottleCount * unitVolume;
    public float DefaultAmount => Mathf.Clamp(defaultBottleCount, 0, bottleCount) * unitVolume;
    public string InventoryId
    {
        get
        {
            if (item != null && !string.IsNullOrWhiteSpace(item.id))
                return item.id;

            string legacyId = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
            return legacyId.ToLowerInvariant() switch
            {
                "tropical_juice" => "item_1001",
                "siltrop" => "item_1002",
                "synthetic_lemon" => "item_1003",
                "nanangna" => "item_1005",
                "cotton" => "item_1006",
                "hectar" => "item_1007",
                "bless" => "item_1008",
                "breeze_vodka" => "item_1009",
                "johnny_dogs" => "item_1010",
                "burnham_bourbon" => "item_1011",
                "beatha" => "item_1012",
                "minute_fizz" => "item_1013",
                "hot_water" => "item_1014",
                "coffee_powder" => "item_1015",
                _ => legacyId
            };
        }
    }
    public bool HasContextVisuals => useContextImages
        || shopBlankSprite != null
        || shelfLidSprite != null
        || barSprite != null;

    public Sprite GetShopSprite()
    {
        return HasContextVisuals ? shopBlankSprite : sprite;
    }

    public Sprite GetShelfSprite()
    {
        return HasContextVisuals ? shelfLidSprite : sprite;
    }

    public Sprite GetBarSprite(Sprite legacyItemIcon = null)
    {
        if (HasContextVisuals)
            return barSprite;
        if (legacyItemIcon != null)
            return legacyItemIcon;
        return sprite;
    }

    public int GetPrice(GameCurrency currency)
    {
        return currency == GameCurrency.StrangeCoin ? strangeCoinPrice : price;
    }
}
