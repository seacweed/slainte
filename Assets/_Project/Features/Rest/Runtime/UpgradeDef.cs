using UnityEngine;

[CreateAssetMenu(menuName = "Shop/Upgrade")]
public class UpgradeDef : ScriptableObject
{
    public string id;
    public Sprite icon;
    public string displayName;
    [TextArea] public string description;
    [Tooltip("Price for each level. Array length defines the max level.")]
    public int[] pricesPerLevel = new int[4];

    public int MaxLevel => pricesPerLevel.Length;
}
