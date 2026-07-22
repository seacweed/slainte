using UnityEngine;

namespace Slainte.Bartending
{
    [CreateAssetMenu(menuName = "Bartending/Business Scene Settings")]
    public sealed class BusinessBartendingSettings : ScriptableObject
    {
        [Header("Prefabs")]
        public GameObject beakerPrefab;
        public GameObject glassPrefab;
        public GameObject orangeJuiceBottlePrefab;
        public GameObject slotPrefab;
        public GameObject liquidParticlePrefab;

        [Header("Initial Bottles")]
        public ItemDef[] initialBottleItems;
        public Vector3[] bottlePositions =
        {
            new Vector3(-8f, -0.8f, 0f),
            new Vector3(-4.8f, -0.8f, 0f)
        };

        [Header("Viewport")]
        public Vector2Int renderTextureSize = new Vector2Int(2560, 1440);
        public float cameraOrthographicSize = 5.2f;
        [Range(8, 31)] public int renderLayer = 30;

        [Header("Initial Item Positions")]
        public Vector3 bottlePosition = new Vector3(-8f, -0.8f, 0f);
        public Vector3 beakerPosition = new Vector3(-1.6f, -1f, 0f);
        public Vector3 glassPosition = new Vector3(4.8f, -1f, 0f);
        public Vector3[] slotPositions =
        {
            new Vector3(-8f, -2.5f, 0f),
            new Vector3(-4.8f, -2.5f, 0f),
            new Vector3(-1.6f, -2.5f, 0f),
            new Vector3(1.6f, -2.5f, 0f),
            new Vector3(4.8f, -2.5f, 0f),
            new Vector3(8f, -2.5f, 0f)
        };

        [Header("Liquid")]
        [Min(1)] public int liquidPoolSize = 300;
    }
}
