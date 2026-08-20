using UnityEngine;

namespace Slainte.Bartending
{
    [CreateAssetMenu(menuName = "Bartending/Glass Definition")]
    public sealed class GlassDef : ScriptableObject
    {
        public string id;
        public string displayName;
        public Sprite cabinetSprite;
        public Sprite worldSprite;
        public GameObject worldPrefab;
        public string glassId;
        [Min(1f)] public float capacityMl = 200f;
        [Min(0.05f)] public float worldScale = 1f;

        public string StableId => id?.Trim() ?? string.Empty;
    }
}
