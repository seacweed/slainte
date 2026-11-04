using UnityEngine;
namespace Slainte.Bartending.FluidGpuExperiment
{
    [CreateAssetMenu(menuName = "Slainte/Fluid GPU Experiment/Liquid Settings")]
    public sealed class FluidExperimentLiquidSettings : ScriptableObject
    {
        public float gpuLiquidAgitationMixRate = 8f;
        public float gpuLiquidClosedVesselDampingRate = 2f;
        public float gpuLiquidClosedVesselMaximumRelativeSpeed = 6f;
        public ComputeShader gpuLiquidComputeShader;
        public float gpuLiquidFullMixRelativeSpeed = 0.8f;
        public int gpuLiquidMaximumIngredients = 16;
        public float gpuLiquidMaximumMixPerSubstep = 0.3f;
        public float gpuLiquidMaximumSpeed = 20f;
        public int gpuLiquidParticleCapacity = 4096;
        public float gpuLiquidParticleRadius = 0.065f;
        public float gpuLiquidParticleVolumeMl = 0.5f;
        public float gpuLiquidPassiveMixRate = 0.02f;
        public int gpuLiquidSubsteps = 2;
        public float gpuLiquidVelocityDamping = 0.003f;
        public float gpuLiquidWallFriction = 0.01f;
        public float gpuLiquidWallRestitution;
        public Vector2 gpuLiquidWorldMax = new Vector2(20f, 15f);
        public Vector2 gpuLiquidWorldMin = new Vector2(-20f, -15f);
        public float liquidMinimumVisibleAlpha = 0.05f;

        [Header("Liquid neighborhood (world units and seconds)")]
        [Tooltip("Cohesive liquid neighborhood radius.")]
        [Min(.02f)] public float referenceSmoothingRadius = .22f;
        [Tooltip("Dimensionless density, with one 0.5 ml particle contributing one unit at zero distance.")]
        [Min(.1f)] public float referenceRestDensity = 2.8f;

        [Header("Shared volume geometry")]
        [Min(.001f)] public float improvedAreaPerMl = .00678f;

        [Header("Coherent liquid")]
        [Tooltip("Compression-only projection iterations. Zero disables pressure for controlled comparisons.")]
        [Range(0, 12)] public int cohesiveDensityIterations = 4;
        [Min(0)] public float cohesiveDensityCompliance = .000001f;
        [Range(.05f, .75f)] public float cohesiveMaximumCorrectionRatio = .35f;
        [Tooltip("Independent finite-range attraction acceleration. Zero disables cohesion; pressure never attracts.")]
        [Min(0)] public float cohesiveSurfaceTension = 18f;
        [Tooltip("Multiplier for material full-vector shear damping. Zero disables shear viscosity.")]
        [Min(0)] public float cohesiveShearViscosity = 1f;
        [Tooltip("Cohesion starts beyond this fraction of the smoothing radius and vanishes at the kernel edge.")]
        [Range(.35f, .9f)] public float cohesiveRestDistanceRatio = .6f;
        [Tooltip("Optional nearest-flat-wall density surrogate; 0 disables it. Does not calibrate vessel capacity or model corner overlap.")]
        [Range(0, 1)] public float cohesiveWallDensity;

        [Header("Floating solids (F/G only; world-space forces)")]
        [Tooltip("Solid density divided by liquid density. Values below one float.")]
        [Range(.1f, 2)] public float iceRelativeDensity = .917f;
        [Range(.1f, 2)] public float garnishRelativeDensity = .87f;
        [Tooltip("Relative-flow damping per second at full immersion; zero disables fluid drag.")]
        [Min(0)] public float iceFlowDrag = 8f;
        [Tooltip("F/G colliding ice only: dissipates unresolved relative pressure noise while preserving common liquid translation.")]
        [Min(0)] public float iceContactRelaxationRate = 16f;
        [Min(0)] public float garnishFlowDrag = 6f;
        [Tooltip("Ice rotational damping per second at full immersion. Angular slip dissipates faster than translation; zero leaves native contact spin free.")]
        [Min(0)] public float iceAngularFlowDrag = 24f;
        [Min(0)] public float solidAngularFlowDrag = 4f;
        [Tooltip("Reject older completed snapshots instead of carrying them with the vessel.")]
        [Range(.05f, .35f)] public float solidMaximumSnapshotAge = .25f;
        [Tooltip("Limit prediction in world space when using an asynchronous flow sample.")]
        [Range(0, .2f)] public float solidFlowPredictionSeconds = .1f;

        [Header("Surface rendering (does not affect physics or ml)")]
        public Shader surfaceDisplayShader;
        [Range(.25f, 1f)] public float surfaceResolutionScale = 1;
        [Range(0, 1)] public float surfaceHighlightStrength = .3f;
        [Range(.5f, 4f)] public float surfaceHighlightWidth = 1.5f;

        [Header("Liquid appearance (display only)")]
        public bool polishLiquidAppearance = true;
        [Range(0, 1)] public float surfaceRimStrength = .22f;
        [Range(0, 1)] public float surfaceLowerEdgeShade = .08f;
    }
}
