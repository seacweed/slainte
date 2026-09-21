using UnityEngine;
// Harness-only ingredient data. Production Runtime/ sources are copied unchanged.
// No inventory, legacy pooling, rendering or game-scene integration is tested by this stand-in.
public sealed class ItemDef : ScriptableObject
{
    public Sprite icon;
    public float liquidSpawnOutwardPixels;
    public Color liquidColor = Color.cyan;
    public bool inheritMixedLiquidColor;
    public float servingTemperatureC = 20;
}
