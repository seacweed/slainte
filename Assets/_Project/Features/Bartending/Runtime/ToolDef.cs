using System;
using UnityEngine;

namespace Slainte.Bartending
{
    public enum ToolKind
    {
        Jigger,
        CobblerShaker,
        BarSpoon,
        IceBucket
    }

    [CreateAssetMenu(menuName = "Bartending/Tool Definition")]
    public sealed class ToolDef : ScriptableObject
    {
        public string id;
        public string displayName;
        public ToolKind kind;
        public Sprite[] cabinetLayers = Array.Empty<Sprite>();
        public Sprite[] worldLayers = Array.Empty<Sprite>();
        public Sprite[] stateSprites = Array.Empty<Sprite>();
        public Sprite[] iceSprites = Array.Empty<Sprite>();
        public GameObject worldPrefab;
        [Min(0f)] public float primaryCapacityMl;
        [Min(0f)] public float secondaryCapacityMl;
        [Min(0)] public int maxCount;
        [Min(0.05f)] public float iceRefillPerSecond = 2f;
        [Min(0f)] public float chargingShakeAmplitude = 0.055f;
        [Min(0f)] public float chargingShakeFrequency = 18f;
        [Range(1f, 120f)] public float icePourStartAngle = 90f;
        [Min(0.05f)] public float icePourInterval = 0.18f;
        [Min(0f)] public float icePourExitSpeed = 0.8f;
        [Min(0.05f)] public float worldScale = 1f;
        public Vector2 worldVisualOffset;
        public bool useDedicatedAnchor;

        public string StableId => id?.Trim() ?? string.Empty;
    }
}
