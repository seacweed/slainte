using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Shop/Recipe Book")]
public class RecipeBookDef : ScriptableObject
{
    public string id;
    public Sprite icon;
    public int    price;
    public string displayName;
    [TextArea] public string description;
    [Tooltip("Shown as the unlock summary line, e.g. \"Spirit x1 unlocked\". Category words inside are auto-colored via CategoryColorText.")]
    [TextArea] public string unlockInfoText;

    [Tooltip("GameProgress flags set on purchase — should match the unlockFlagKey of the LiquorBottleDef entries this book unlocks.")]
    public List<string> unlockFlagKeys = new();

    public string PurchasedFlagKey => $"RecipeBook_{id}_Purchased";
}
