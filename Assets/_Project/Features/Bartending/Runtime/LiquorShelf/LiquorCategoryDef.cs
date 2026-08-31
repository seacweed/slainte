using UnityEngine;

[CreateAssetMenu(menuName = "Bartending/Liquor Category")]
public class LiquorCategoryDef : ScriptableObject
{
    public string id;
    public string displayName;
    public Sprite icon;
    [Tooltip("Unique color for this category. Tints same-shape accent images next to the category name (see LiquorCategoryButtonUI.colorImage), and highlights the category word inside free-form shop text (see CategoryColorText).")]
    public Color  color = Color.white;
}
