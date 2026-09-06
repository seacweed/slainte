using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace Slainte.Bartending
{
    [CreateAssetMenu(menuName = "Bartending/Business Scene Settings")]
    public sealed class BusinessBartendingSettings : ScriptableObject
    {
        [Header("Tool Cabinet")]
        public bool useToolCabinet;

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
        [Tooltip("Optional serving-area art. It is centered inside the existing serving target.")]
        public Sprite serveTargetSprite;
        [HideInInspector, Range(0f, 1f)] public float serveLineScreenRatio = 0.3f;
        public Color serveTargetFillColor = new Color(1f, 1f, 1f, 0f);
        public Color serveTargetOutlineColor = new Color(1f, 0.82f, 0.05f, 0.8f);
        [Min(0f)] public float serveTargetOutlineWidth = 4f;
        [Min(0f)] public float serveTargetPaddingPixels = 0f;
        [Min(0f)] public float serveTargetFadeDuration = 0.2f;
        [FormerlySerializedAs("sandboxServeTargetNormalized")]
        [Tooltip("Fixed serving target as normalized x, y, width, height at every resolution.")]
        public Vector4 serveTargetNormalized = new Vector4(0.35f, 0.48f, 0.3f, 0.42f);

        [Header("World Capacity Text")]
        [Tooltip("Background color for capacity labels shown on beakers, jiggers, and cobbler shakers.")]
        public Color contentsLabelBackgroundColor = new Color(0.04f, 0.05f, 0.06f, 0.82f);
        [Tooltip("TMP font used by world capacity labels. The TMP default font is used when empty.")]
        public TMP_FontAsset contentsLabelFont;
        [Tooltip("Text color for world capacity labels.")]
        public Color contentsLabelTextColor = new Color(1f, 0.82f, 0.05f, 1f);
        public Vector2 contentsLabelSizePixels = new Vector2(220f, 150f);
        public Vector2 contentsLabelOffsetPixels = new Vector2(28f, 45f);
        [Tooltip("Font size shared by beaker, jigger, and cobbler-shaker capacity labels.")]
        [Range(8, 96)] public int contentsLabelFontSize = 18;
        [Min(0.02f)] public float contentsRefreshInterval = 0.1f;
        [Min(0f)] public float contentsMinimumVisibleMl = 0.05f;

        [Header("Runtime Debug")]
        [Tooltip("Show cached recipe, technique, ice, and volume labels for tracked vessels in Editor or Development Builds.")]
        public bool showVesselDebugLabels;
        [Min(0.05f)] public float vesselDebugRefreshInterval = 0.2f;

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
            new Vector3(-5.714f, -2.5f, 0f),
            new Vector3(-3.429f, -2.5f, 0f),
            new Vector3(-1.143f, -2.5f, 0f),
            new Vector3(1.143f, -2.5f, 0f),
            new Vector3(3.429f, -2.5f, 0f),
            new Vector3(5.714f, -2.5f, 0f),
            new Vector3(8f, -2.5f, 0f)
        };

        [Header("Liquid")]
        public LiquidSimulationBackendMode liquidSimulationBackend =
            LiquidSimulationBackendMode.Automatic;
        [Min(1)] public int liquidPoolSize = 300;
        public Material liquidMetaballAccumulationMaterial;
        public Material liquidMetaballCompositeMaterial;
        public Vector2Int liquidMetaballTextureSize = new Vector2Int(240, 135);
        [Range(0f, 1f)] public float liquidMetaballThreshold = 0.3f;
        [Range(0f, 1f)] public float liquidMetaballMergeStrength = 0.45f;
        [Range(0f, 0.25f)] public float liquidMetaballEdgeSoftness = 0.03f;
        [Range(0f, 1f)] public float liquidMinimumVisibleAlpha = 0.05f;
        public int liquidSortingOrder = 12;

        [Header("GPU Liquid PBF/XPBD")]
        public ComputeShader gpuLiquidComputeShader;
        [Min(64)] public int gpuLiquidParticleCapacity = 4096;
        [Min(0.01f)] public float gpuLiquidParticleVolumeMl = 0.5f;
        [Range(1, 32)] public int gpuLiquidMaximumIngredients = 16;
        [Range(1, 4)] public int gpuLiquidSubsteps = 2;
        [Range(1, 10)] public int gpuLiquidSolverIterations = 5;
        [Min(0.01f)] public float gpuLiquidParticleRadius = 0.065f;
        [Range(1f, 1.5f)] public float gpuLiquidContainedRenderRadiusMultiplier = 1.18f;
        [Range(1f, 2f)] public float gpuLiquidRenderRadiusMultiplier = 1.35f;
        [Range(1f, 3f)] public float gpuLiquidAirborneStretchMultiplier = 1.65f;
        [Min(0.1f)] public float gpuLiquidAirborneFullStretchSpeed = 2.25f;
        [Min(0.02f)] public float gpuLiquidSmoothingRadius = 0.16f;
        [Min(0.01f)] public float gpuLiquidRestDensity = 150f;
        [Min(0f)] public float gpuLiquidDensityCompliance = 0.000001f;
        [Min(0.000001f)] public float gpuLiquidLambdaEpsilon = 0.01f;
        [Range(0f, 0.02f)] public float gpuLiquidArtificialPressureStrength = 0.00035f;
        [Range(0.1f, 0.9f)] public float gpuLiquidArtificialPressureRadiusRatio = 0.3f;
        [Range(0f, 12f)] public float gpuLiquidBoundaryDensityScale = 4f;
        [Range(0f, 1f)] public float gpuLiquidWallFriction = 0.01f;
        [Range(0f, 1f)] public float gpuLiquidWallRestitution;
        [Range(0f, 1f)] public float gpuLiquidSplashTransfer = 0.06f;
        [Min(0f)] public float gpuLiquidSplashMinimumImpactSpeed = 1.1f;
        [Range(0f, 1f)] public float gpuLiquidBottomImpactSpread;
        [Range(0f, 1f)] public float gpuLiquidVelocityDamping = 0.003f;
        [Min(0.1f)] public float gpuLiquidMaximumSpeed = 20f;
        [Tooltip("Velocity damping per second, relative to a fully closed vessel. This prevents sustained shaking from accumulating unbounded particle energy without affecting poured liquid.")]
        [Min(0f)] public float gpuLiquidClosedVesselDampingRate = 2f;
        [Tooltip("Maximum particle speed relative to a fully closed vessel.")]
        [Min(0.1f)] public float gpuLiquidClosedVesselMaximumRelativeSpeed = 6f;
        [Range(0f, 1f)] public float gpuLiquidViscosity = 0.02f;
        [Min(0f)] public float gpuLiquidPassiveMixRate = 0.02f;
        [Min(0f)] public float gpuLiquidAgitationMixRate = 8f;
        [Min(0.01f)] public float gpuLiquidFullMixRelativeSpeed = 0.8f;
        [Range(0.01f, 0.5f)] public float gpuLiquidMaximumMixPerSubstep = 0.3f;
        [Range(0f, 1f)] public float gpuLiquidStirCompositionTolerance = 0.08f;
        [Min(0.05f)] public float gpuLiquidSnapshotInterval = 0.1f;
        public Vector2 gpuLiquidWorldMin = new Vector2(-20f, -15f);
        public Vector2 gpuLiquidWorldMax = new Vector2(20f, 15f);

        [Header("Liquid/Ice Interaction")]
        [Tooltip("Whether liquid particles physically collide with ice cubes during bartending.")]
        public bool liquidIceCollisionEnabled;

        [Header("Ice")]
        [Min(0.1f)] public float iceBinWidth = 2.2f;
        [Min(0.1f)] public float iceBinHeight = 1.2f;
        [Min(0.05f)] public float iceCubeSize = 0.42f;
        [Range(0f, 1f)] public float iceOpacity = 1f;
        [Min(0f)] public float iceGravityScale = 1f;
        [Min(0f)] public float iceDragThreshold = 0.08f;
        public float iceCleanupY = -15f;
    }
}
