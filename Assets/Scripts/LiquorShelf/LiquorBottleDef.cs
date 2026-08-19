using UnityEngine;

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
    [Tooltip("Volume per bottle, e.g. 700 (ml).")]
    public float    unitVolume = 700f;

    [Header("Shop")]
    [Tooltip("Shop category grouping. Null = not sold in the shop.")]
    public LiquorCategoryDef category;
    [Tooltip("Price for one bottle (unitVolume) in the shop.")]
    public int      price;

    [Header("Shop Lock Hint (display only, unlock itself uses unlockFlagKey)")]
    public IngredientUnlockHintType unlockHintType = IngredientUnlockHintType.None;
    [Tooltip("Used when unlockHintType == RecipeBook.")]
    public Sprite   recipeBookIcon;
    public string   recipeBookName;

    public float MaxAmount => bottleCount * unitVolume;
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
}
