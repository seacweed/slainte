using System;
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
        [Tooltip("Back color, back line, front color, and front line, in that order.")]
        public Sprite[] cabinetLayers = Array.Empty<Sprite>();
        [Tooltip("Back color, back line, front color, and front line, in that order.")]
        public Sprite[] worldLayers = Array.Empty<Sprite>();
        [Tooltip("Full-canvas sprite used to convert collision profile pixels to local space.")]
        public Sprite collisionReferenceSprite;
        public GameObject worldPrefab;
        public string glassId;
        [Min(1f)] public float capacityMl = 200f;
        [Min(0.05f)] public float worldScale = 1f;

        public string StableId => id?.Trim() ?? string.Empty;

        public Sprite[] GetCabinetLayers()
        {
            if (cabinetLayers != null && cabinetLayers.Length > 0)
                return cabinetLayers;
            return cabinetSprite != null
                ? new[] { cabinetSprite }
                : Array.Empty<Sprite>();
        }

        public Sprite[] GetWorldLayers()
        {
            if (worldLayers != null && worldLayers.Length > 0)
                return worldLayers;
            return worldSprite != null
                ? new[] { worldSprite }
                : Array.Empty<Sprite>();
        }

        public Sprite GetCollisionReferenceSprite()
        {
            if (collisionReferenceSprite != null)
                return collisionReferenceSprite;

            Sprite[] layers = GetWorldLayers();
            for (int i = layers.Length - 1; i >= 0; i--)
            {
                if (layers[i] != null)
                    return layers[i];
            }
            return null;
        }
    }
}
