using UnityEngine;
using UnityEngine.Serialization;

namespace Slainte.Bartending
{
    [CreateAssetMenu(menuName = "Bartending/Business Scene Settings")]
    public sealed class BusinessBartendingSettings : ScriptableObject
    {
        [Header("Prefabs")]
        public GameObject beakerPrefab;
        [Tooltip("스트레이너가 뚜껑에 결합된 코블러 셰이커 프리팹입니다.")]
        public GameObject cobblerShakerPrefab;
        public GameObject glassPrefab;
        [FormerlySerializedAs("orangeJuiceBottlePrefab")]
        public GameObject bottlePrefab;
        public GameObject slotPrefab;
        public GameObject liquidParticlePrefab;
        [Tooltip("Optional visual prefab. A runtime fallback is created when it is empty.")]
        public GameObject iceBinPrefab;
        [Tooltip("Optional physical ice prefab. A runtime fallback is created when it is empty.")]
        public GameObject iceCubePrefab;

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

        [Header("Serving Target")]
        [HideInInspector, Range(0f, 1f)] public float serveLineScreenRatio = 0.3f;
        public Color serveTargetFillColor = new Color(1f, 1f, 1f, 0f);
        public Color serveTargetOutlineColor = new Color(1f, 0.82f, 0.05f, 0.8f);
        [Min(0f)] public float serveTargetOutlineWidth = 4f;
        [Min(0f)] public float serveTargetPaddingPixels = 0f;
        [Tooltip("Sandbox fallback target as normalized x, y, width, height.")]
        public Vector4 sandboxServeTargetNormalized = new Vector4(0.35f, 0.48f, 0.3f, 0.42f);

        [Header("Vessel Contents UI")]
        public Color contentsLabelBackgroundColor = new Color(0.04f, 0.05f, 0.06f, 0.82f);
        public Color contentsLabelTextColor = Color.white;
        public Vector2 contentsLabelSizePixels = new Vector2(220f, 150f);
        public Vector2 contentsLabelOffsetPixels = new Vector2(28f, 45f);
        [Min(8)] public int contentsLabelFontSize = 18;
        [Min(0.02f)] public float contentsRefreshInterval = 0.1f;
        [Min(0f)] public float contentsMinimumVisibleMl = 0.05f;

        [Header("Rotation Horizontal Movement")]
        [Min(0f)] public float rotationHorizontalSensitivity = 1f;
        [Min(0f)] public float rotationHorizontalScreenPadding = 12f;

        [Header("Initial Item Positions")]
        public Vector3 bottlePosition = new Vector3(-8f, -0.8f, 0f);
        public Vector3 beakerPosition = new Vector3(-1.6f, -1f, 0f);
        public Vector3 cobblerShakerPosition = new Vector3(1.6f, -1f, 0f);
        public Vector3 glassPosition = new Vector3(4.8f, -1f, 0f);
        public Vector3 iceBinPosition = new Vector3(7.8f, 1.3f, 0f);
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

        [Header("Ice")]
        [Min(0.1f)] public float iceBinWidth = 2.2f;
        [Min(0.1f)] public float iceBinHeight = 1.2f;
        [Min(0.05f)] public float iceCubeSize = 0.42f;
        [Min(0f)] public float iceGravityScale = 1f;
        [Min(0f)] public float iceDragThreshold = 0.08f;
        public float iceCleanupY = -8f;
    }
}
