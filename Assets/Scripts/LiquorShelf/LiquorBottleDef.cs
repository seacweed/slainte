using UnityEngine;

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

    public float MaxAmount => bottleCount * unitVolume;
}
