using UnityEngine;
namespace Slainte.Bartending.FluidGpuExperiment
{
    [CreateAssetMenu(menuName = "Slainte/Fluid GPU Experiment/Liquid Settings")]
    public sealed class FluidExperimentLiquidSettings : ScriptableObject
    {
        public float gpuLiquidAgitationMixRate = 8f;
        public float gpuLiquidArtificialPressureRadiusRatio = 0.3f;
        public float gpuLiquidArtificialPressureStrength = 0.00035f;
        public float gpuLiquidBottomImpactSpread;
        public float gpuLiquidBoundaryDensityScale = 4f;
        public float gpuLiquidClosedVesselDampingRate = 2f;
        public float gpuLiquidClosedVesselMaximumRelativeSpeed = 6f;
        public ComputeShader gpuLiquidComputeShader;
        public float gpuLiquidDensityCompliance = 0.000001f;
        public float gpuLiquidFullMixRelativeSpeed = 0.8f;
        public float gpuLiquidLambdaEpsilon = 0.01f;
        public int gpuLiquidMaximumIngredients = 16;
        public float gpuLiquidMaximumMixPerSubstep = 0.3f;
        public float gpuLiquidMaximumSpeed = 20f;
        public int gpuLiquidParticleCapacity = 4096;
        public float gpuLiquidParticleRadius = 0.065f;
        public float gpuLiquidParticleVolumeMl = 0.5f;
        public float gpuLiquidPassiveMixRate = 0.02f;
        public float gpuLiquidRestDensity = 150f;
        public float gpuLiquidSmoothingRadius = 0.16f;
        public int gpuLiquidSolverIterations = 5;
        public float gpuLiquidSplashMinimumImpactSpeed = 1.1f;
        public float gpuLiquidSplashTransfer = 0.06f;
        public int gpuLiquidSubsteps = 2;
        public float gpuLiquidVelocityDamping = 0.003f;
        public float gpuLiquidViscosity = 0.02f;
        public float gpuLiquidWallFriction = 0.01f;
        public float gpuLiquidWallRestitution;
        public Vector2 gpuLiquidWorldMax = new Vector2(20f, 15f);
        public Vector2 gpuLiquidWorldMin = new Vector2(-20f, -15f);
        public float liquidMinimumVisibleAlpha = 0.05f;

        [Header("Reference SPH experiment (world units and seconds)")]
        [Tooltip("Neighborhood radius. Only the reference solver uses this value; baseline PBF keeps its original kernel.")]
        [Min(.02f)] public float referenceSmoothingRadius = .22f;
        [Tooltip("Dimensionless density, with one 0.5 ml particle contributing one unit at zero distance.")]
        [Min(.1f)] public float referenceRestDensity = 2.8f;
        [Min(0)] public float referencePressureStiffness = 40f;
        [Min(0)] public float referenceNearPressureStiffness = 80f;
        [Tooltip("Approaching pair radial velocity damping rate per second; separating pairs are unaffected.")]
        [Min(0)] public float referenceViscosityRate = 8f;
        [Tooltip("Limits negative pressure relative to stiffness times rest density, preventing tensile collapse at free surfaces.")]
        [Range(0, 1)] public float referenceMaximumTensionRatio = .25f;
        [Tooltip("Acceleration safety bound for explicit pressure; also bounded by referenceMaximumDisplacementRatio each substep.")]
        [Min(1)] public float referenceMaximumAcceleration = 250f;
        [Range(.05f, .75f)] public float referenceMaximumDisplacementRatio = .5f;

        [Header("Calibrated liquid (E only)")]
        [Min(.001f)] public float improvedAreaPerMl = .00678f;
        [Min(.01f)] public float improvedParticleRadius = .024f;
        [Range(1.8f, 3.5f)] public float improvedKernelRatio = 2.4f;
        [Min(.05f)] public float improvedMaximumKernelRadius = .3f;
        [Range(2, 16)] public int improvedSolverIterations = 8;
        [Min(0)] public float improvedDensityCompliance = .0000001f;
        [Range(.1f, 1)] public float improvedMaximumCorrectionRatio = .75f;
        [Range(0, 1)] public float improvedWallDensity = 1f;
        [Min(0)] public float improvedShearViscosity = 1f;
        [Min(0)] public float improvedSurfaceTension = 1f;
        [Min(0)] public float improvedWetting = 1f;

        [Header("Coherent liquid (F only; keeps the D particle and volume scale)")]
        [Tooltip("Compression-only projection iterations. Zero disables pressure for controlled comparisons.")]
        [Range(0, 12)] public int cohesiveDensityIterations = 4;
        [Min(0)] public float cohesiveDensityCompliance = .000001f;
        [Range(.05f, .75f)] public float cohesiveMaximumCorrectionRatio = .35f;
        [Tooltip("Independent finite-range attraction acceleration. Zero disables cohesion; pressure never attracts.")]
        [Min(0)] public float cohesiveSurfaceTension = 18f;
        [Tooltip("Multiplier for material full-vector shear damping. Zero disables shear viscosity.")]
        [Min(0)] public float cohesiveShearViscosity = 1f;
        [Tooltip("Cohesion starts beyond this fraction of the D smoothing radius and vanishes at the kernel edge.")]
        [Range(.35f, .9f)] public float cohesiveRestDistanceRatio = .6f;
        [Tooltip("Optional nearest-flat-wall density surrogate; 0 disables it. Does not calibrate vessel capacity or model corner overlap.")]
        [Range(0, 1)] public float cohesiveWallDensity;

        [Header("Surface rendering (does not affect physics or ml)")]
        public Shader surfaceAccumulationShader;
        public Shader surfaceCompositeShader;
        public Shader surfaceDisplayShader;
        public Shader streamAccumulationShader;
        [Min(.01f)] public float streamMaximumTimeGap = .075f;
        [Min(.05f)] public float streamMaximumLength = .65f;
        [Range(.25f, 1f)] public float surfaceResolutionScale = 1;
        [Range(1f, 2f)] public float containedRenderRadius = 1.45f;
        [Range(1f, 2f)] public float airborneRenderRadius = 1.6f;
        [Range(1f, 3f)] public float airborneStretch = 2.2f;
        [Min(.1f)] public float airborneFullStretchSpeed = 4;
        [Range(.05f, .8f)] public float surfaceThreshold = .3f;
        [Range(0, 1)] public float surfaceMergeStrength = .8f;
        [Range(.005f, .15f)] public float surfaceEdgeSoftness = .035f;
        [Range(0, 1)] public float surfaceHighlightStrength = .3f;
        [Range(.5f, 4f)] public float surfaceHighlightWidth = 1.5f;
    }
}
