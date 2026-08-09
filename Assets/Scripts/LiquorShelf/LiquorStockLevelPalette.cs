using UnityEngine;

[CreateAssetMenu(menuName = "Bartending/Liquor Stock Level Palette")]
public class LiquorStockLevelPalette : ScriptableObject
{
    private const int IntermediateStageCount = 10;

    [SerializeField] private Sprite   emptySprite;
    [SerializeField] private Sprite[] intermediateSprites = new Sprite[IntermediateStageCount];
    [SerializeField] private Sprite   fullSprite;

    public Sprite GetSprite(float ratio01)
    {
        if (ratio01 <= 0f) return emptySprite;
        if (ratio01 >= 1f) return fullSprite;

        int stage = Mathf.Clamp(Mathf.CeilToInt(ratio01 * IntermediateStageCount) - 1, 0, IntermediateStageCount - 1);
        return intermediateSprites[stage];
    }
}
