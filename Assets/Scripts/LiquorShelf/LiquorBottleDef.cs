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
    public Sprite   sprite;
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
}
