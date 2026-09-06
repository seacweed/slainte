using System;
using System.Collections.Generic;
using Slainte.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.Bartending.EditorTools
{
    public static class GpuLiquidPerformanceValidator
    {
        private const string SettingsPath =
            "Assets/Resources/Bartending/BusinessBartendingSettings.asset";
        private const string ComputePath =
            "Assets/_Project/Features/Bartending/Infrastructure/GpuFluid/Compute/GpuLiquidPbfXpbd.compute";
        private const string RenderShaderPath =
            "Assets/_Project/Features/Bartending/Infrastructure/GpuFluid/Graphics/GpuLiquidAccumulation.shader";
        private const string BottlePrefabPath =
            "Assets/_Project/Features/Bartending/Prefabs/Equipment/Bottle.prefab";
        private const string SandboxScenePath = ProjectScenePaths.BartendingSandbox;
        private const string RunningKey = "Slainte.GpuLiquidPerformanceValidator.Running";
        private const int StressParticleCount = 1500;
        private const float WarmupAndSampleSeconds = 8f;
        private const float MaximumP95Milliseconds = 33.34f;
        private const float MaximumP99Milliseconds = 50f;
        private const float PostMotionReadbackSeconds = 0.75f;
        private static readonly Vector2 VesselTranslation = new(0.37f, -0.21f);

        private static double validationStartedAt;
        private static double samplingStartedAt;
        private static bool stressStarted;
        private static float initialMaximumDeviation;
        private static int postMotionPhase;
        private static double postMotionStartedAt;
        private static Vector2 originalSurfacePosition;
        private static string performanceReport = string.Empty;
        private static CocktailComposition initialComposition;

        [MenuItem("Slainte/Bartending/Validate GPU Liquid PBF-XPBD")]
        public static void RunFromMenu()
        {
            Begin(false);
        }

        public static void RunFromCommandLine()
        {
            Begin(true);
        }

        private static void Begin(bool commandLine)
        {
            try
            {
                ValidateEditModeContracts();
            }
            catch (Exception exception)
            {
                Debug.LogError("[GpuLiquidPerformanceValidator] EDIT FAIL: " + exception);
                if (commandLine)
                    EditorApplication.Exit(1);
                else
                    throw;
                return;
            }

            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(RunningKey + ".CommandLine", commandLine);
            EditorSceneManager.OpenScene(SandboxScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        private static void ValidateEditModeContracts()
        {
            BusinessBartendingSettings settings = AssetDatabase.LoadAssetAtPath<
                BusinessBartendingSettings>(SettingsPath);
            Require(settings != null, "Bartending settings asset is missing.");
            Require(settings.liquidSimulationBackend != LiquidSimulationBackendMode.LegacyRigidbody2D,
                "Bartending settings explicitly select the legacy liquid backend.");
            Require(settings.gpuLiquidParticleCapacity >= StressParticleCount,
                $"GPU particle capacity must be at least {StressParticleCount}.");
            Require(settings.gpuLiquidParticleVolumeMl > 0f
                && settings.gpuLiquidParticleVolumeMl <= 0.5f,
                "GPU particle volume must be independently configured at 0.5 ml or less.");
            Require(settings.gpuLiquidMaximumIngredients >= 2,
                "At least two GPU ingredients are required to validate spatial mixing.");
            Require(settings.gpuLiquidSubsteps >= 1 && settings.gpuLiquidSolverIterations >= 1,
                "GPU solver iterations are disabled.");
            Require(settings.gpuLiquidRenderRadiusMultiplier >= 1f
                && settings.gpuLiquidRenderRadiusMultiplier <= 1.5f,
                "GPU particle render radius is too large for a readable liquid stream.");
            Require(settings.gpuLiquidContainedRenderRadiusMultiplier >= 1f
                && settings.gpuLiquidContainedRenderRadiusMultiplier
                    <= settings.gpuLiquidRenderRadiusMultiplier,
                "Contained particles must not render larger than airborne stream particles.");
            Require(settings.gpuLiquidBoundaryDensityScale > 0f,
                "GPU vessel walls must contribute virtual density near the boundary.");
            Require(settings.gpuLiquidArtificialPressureStrength > 0f,
                "GPU particles need artificial pressure to avoid granular stacking.");
            Require(settings.gpuLiquidArtificialPressureRadiusRatio > 0f
                && settings.gpuLiquidArtificialPressureRadiusRatio < 1f,
                "GPU artificial-pressure reference radius must stay inside the smoothing radius.");
            Require(settings.gpuLiquidWallFriction <= 0.15f,
                "GPU vessel wall friction is too high for liquid to slide freely.");
            Require(settings.gpuLiquidWallRestitution <= 0.1f,
                "GPU vessel wall restitution is too high for stable liquid containment.");
            Require(settings.gpuLiquidBottomImpactSpread >= 0f
                && settings.gpuLiquidBottomImpactSpread <= 0.2f,
                "GPU bottom-impact spreading must remain disabled or subtle.");
            Require(settings.gpuLiquidSplashTransfer > 0f
                && settings.gpuLiquidSplashTransfer <= 0.25f,
                "GPU particle impacts need controlled tangential splash transfer.");
            Require(settings.gpuLiquidSplashMinimumImpactSpeed > 0f,
                "GPU liquid splash must ignore low-speed resting contacts.");
            Require(settings.gpuLiquidClosedVesselDampingRate > 0f,
                "Closed vessels need relative-velocity damping during sustained shaking.");
            Require(settings.gpuLiquidClosedVesselMaximumRelativeSpeed > 0f
                && settings.gpuLiquidClosedVesselMaximumRelativeSpeed
                    < settings.gpuLiquidMaximumSpeed,
                "Closed-vessel relative speed must be capped below the global particle speed limit.");
            Require(settings.gpuLiquidAgitationMixRate >= 6f,
                "GPU agitation mixing is too slow for interactive stirring.");
            Require(settings.gpuLiquidFullMixRelativeSpeed <= 1f,
                "GPU mixing requires an unrealistically high relative particle speed.");
            Require(settings.gpuLiquidMaximumMixPerSubstep >= 0.25f,
                "GPU mixing is capped too aggressively to converge while stirring.");
            Require(settings.gpuLiquidAirborneStretchMultiplier > 1f,
                "Airborne particles must stretch along their velocity to read as a pour stream.");
            LiquidParticleData particleData = settings.liquidParticlePrefab != null
                ? settings.liquidParticlePrefab.GetComponent<LiquidParticleData>()
                : null;
            Require(particleData != null,
                "The liquid particle prefab is missing LiquidParticleData.");
            Require(Mathf.Approximately(particleData.DefaultVolumeMl, 2f),
                "Legacy Rigidbody2D particles must remain at 2 ml; configure GPU particle volume separately.");

            GameObject bottlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BottlePrefabPath);
            BottleController bottle = bottlePrefab != null
                ? bottlePrefab.GetComponent<BottleController>()
                : null;
            Require(bottle != null,
                "The canonical bottle prefab is missing BottleController.");
            SerializedObject serializedBottle = new(bottle);
            float pourExitSpeed = serializedBottle.FindProperty("pourExitSpeed").floatValue;
            float mouthVelocityInheritance = serializedBottle
                .FindProperty("mouthVelocityInheritance").floatValue;
            float pourSpawnHalfWidth = serializedBottle
                .FindProperty("pourSpawnHalfWidth").floatValue;
            Require(pourExitSpeed > 0f,
                "Bottle liquid must leave the mouth with a directed initial velocity.");
            Require(mouthVelocityInheritance == 0f,
                "The canonical bottle stream must not inherit mouth-motion jitter.");
            Require(pourSpawnHalfWidth == 0f,
                "The canonical bottle stream must start at one deterministic mouth point.");
            float particlesPerSecond = bottle.pourMlPerSecond
                / settings.gpuLiquidParticleVolumeMl;
            Require(particlesPerSecond >= 30f,
                "The canonical bottle emits too few visual particles per second for a continuous stream.");
            Require(pourExitSpeed / particlesPerSecond
                >= settings.gpuLiquidParticleRadius * 0.85f,
                "Bottle particles spawn too closely and will split under density pressure.");

            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            Require(compute != null, "GPU liquid compute shader is missing.");
            Require(settings.gpuLiquidComputeShader == compute,
                "Bartending settings do not reference the canonical GPU liquid compute shader.");
            string[] kernels =
            {
                "ResetParticles",
                "ResetCompositionBuffers",
                "SpawnParticles",
                "IntegrateParticles",
                "ClearGrid",
                "BuildGrid",
                "CalculateDensityLambda",
                "CalculatePositionDelta",
                "ApplyDeltaAndBoundaries",
                "UpdateVelocities",
                "MixComposition",
                "UpdateParticleColors",
                "ApplyTechnique",
                "TranslateVesselContents",
                "SetVesselSuspended"
            };
            for (int i = 0; i < kernels.Length; i++)
                Require(compute.HasKernel(kernels[i]), $"Compute kernel is missing: {kernels[i]}");

            Shader renderShader = AssetDatabase.LoadAssetAtPath<Shader>(RenderShaderPath);
            Require(renderShader != null, "GPU liquid accumulation shader is missing.");
            Require(renderShader.passCount >= 4,
                "GPU liquid accumulation shader must expose MRT and three fallback passes.");
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxScenePath) != null,
                "Bartending sandbox scene is missing.");
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            if (!SessionState.GetBool(RunningKey, false))
                return;

            validationStartedAt = EditorApplication.timeSinceStartup;
            samplingStartedAt = 0d;
            stressStarted = false;
            initialMaximumDeviation = 0f;
            postMotionPhase = 0;
            postMotionStartedAt = 0d;
            originalSurfacePosition = Vector2.zero;
            performanceReport = string.Empty;
            initialComposition = null;
            EditorApplication.update -= ValidateOnUpdate;
            EditorApplication.update += ValidateOnUpdate;
        }

        private static void ValidateOnUpdate()
        {
            if (!EditorApplication.isPlaying)
                return;
            if (EditorApplication.timeSinceStartup - validationStartedAt > 45d)
            {
                Finish(false, "Timed out while running the GPU liquid stress test.");
                return;
            }

            try
            {
                BartendingSandboxBootstrap sandbox =
                    UnityEngine.Object.FindFirstObjectByType<BartendingSandboxBootstrap>();
                LiquidStressHarness harness =
                    UnityEngine.Object.FindFirstObjectByType<LiquidStressHarness>();
                BartendingSessionInstance session = sandbox?.CurrentSession;
                GpuLiquidSystem gpu = session?.GpuLiquidSystem;
                if (sandbox == null || harness == null || gpu == null || !gpu.IsOperational)
                    return;

                Require(ReferenceEquals(session.LiquidBackend, gpu)
                    && session.LiquidPool == null,
                    "The sandbox did not select the GPU liquid backend.");
                Require(gpu.ParticleCapacity >= StressParticleCount,
                    "The live GPU particle capacity is below the stress target.");

                if (!stressStarted)
                {
                    harness.LoadParticles(StressParticleCount, false);
                    stressStarted = true;
                    return;
                }

                if (samplingStartedAt <= 0d)
                {
                    VesselLiquidTracker tracker = harness.TargetTracker;
                    if (harness.EmittedParticleCount != StressParticleCount
                        || gpu.ActiveParticleCount < 1000
                        || tracker == null
                        || !gpu.TryGetSnapshot(
                            tracker,
                            out GpuLiquidVesselSnapshot initialSnapshot)
                        || initialSnapshot.ParticleCount < 1000)
                    {
                        return;
                    }

                    initialMaximumDeviation = initialSnapshot.MaximumCompositionDeviation;
                    Require(initialMaximumDeviation > gpu.StirCompositionTolerance,
                        $"Separated source particles became uniform before agitation: {initialMaximumDeviation:0.000}.");
                    Require(initialSnapshot.OutOfToleranceParticleCount > 0,
                        "The all-particle uniformity check found no initial outliers.");
                    initialComposition = tracker.BuildComposition();
                    Require((initialComposition.Techniques & CocktailTechnique.Stir) == 0,
                        "Uniformity sampling incorrectly awarded Stir before rod activity.");
                    Require(harness.FirstTestIngredient != null
                        && harness.SecondTestIngredient != null
                        && harness.FirstTestIngredient != harness.SecondTestIngredient,
                        "The stress test requires two distinct liquid ingredients.");
                    float expectedIngredientVolumeMl = StressParticleCount
                        * gpu.DefaultParticleVolumeMl * 0.5f;
                    Require(Mathf.Abs(initialComposition.GetVolume(
                            harness.FirstTestIngredient) - expectedIngredientVolumeMl) <= 0.5f,
                        "Mixing failed to preserve the first ingredient during initial settling.");
                    Require(Mathf.Abs(initialComposition.GetVolume(
                            harness.SecondTestIngredient) - expectedIngredientVolumeMl) <= 0.5f,
                        "Mixing failed to preserve the second ingredient during initial settling.");
                    Debug.Log(
                        $"[GpuLiquidPerformanceValidator] Initial: active={gpu.ActiveParticleCount}, "
                        + $"owned={initialSnapshot.ParticleCount}, closedTriggers={gpu.ClosedVesselTriggerCount}, "
                        + $"maxDeviation={initialMaximumDeviation:0.000}, "
                        + $"outliers={initialSnapshot.OutOfToleranceParticleCount}.");
                    harness.SetAutoShake(true);
                    samplingStartedAt = EditorApplication.timeSinceStartup;
                    return;
                }

                if (EditorApplication.timeSinceStartup - samplingStartedAt
                    < WarmupAndSampleSeconds)
                {
                    return;
                }

                VesselLiquidTracker finalTracker = harness.TargetTracker;
                GpuLiquidVesselSnapshot finalSnapshot = null;
                bool hasFinalSnapshot = finalTracker != null
                    && gpu.TryGetSnapshot(finalTracker, out finalSnapshot);
                Require(hasFinalSnapshot,
                    "The final shaker composition snapshot is unavailable.");
                if (postMotionPhase == 0)
                {
                    Require(harness.EmittedParticleCount == StressParticleCount,
                        $"Only {harness.EmittedParticleCount}/{StressParticleCount} particles were emitted.");
                    Require(harness.IsAutoShaking,
                        "The shaker did not remain in automatic high-speed motion.");
                    Require(gpu.BoundarySegmentCount > 0 && gpu.VesselCount > 0,
                        "No live vessel collision geometry was uploaded to the GPU solver.");
                    Require(gpu.ClosedVesselTriggerCount > 0,
                        "The cobbler shaker did not upload a closed-vessel containment trigger.");
                    Require(gpu.ActiveParticleCount >= 1000,
                        $"Fewer than 1000 particles survived the stress interval: "
                        + $"active={gpu.ActiveParticleCount}, owned={finalSnapshot.ParticleCount}, "
                        + $"closedTriggers={gpu.ClosedVesselTriggerCount}.");
                    Require(finalSnapshot.MaximumCompositionDeviation
                        <= gpu.StirCompositionTolerance,
                        $"Local mixing did not converge: initial={initialMaximumDeviation:0.000}, final={finalSnapshot.MaximumCompositionDeviation:0.000}.");
                    Require(finalSnapshot.OutOfToleranceParticleCount == 0,
                        $"All-particle uniformity still has {finalSnapshot.OutOfToleranceParticleCount} outliers.");
                    CocktailComposition finalComposition = finalTracker.BuildComposition();
                    Require((finalComposition.Techniques & CocktailTechnique.Stir) == 0,
                        "Composition convergence alone incorrectly awarded Stir without rod activity.");
                    Require(initialComposition != null
                        && Mathf.Abs(finalComposition.TotalVolumeMl
                            - initialComposition.TotalVolumeMl) <= 0.01f,
                        "Local mixing changed the total liquid volume.");
                    foreach (KeyValuePair<ItemDef, float> pair in initialComposition.Volumes)
                    {
                        float finalVolumeMl = finalComposition.GetVolume(pair.Key);
                        Require(Mathf.Abs(finalVolumeMl - pair.Value) <= 0.5f,
                            $"Local mixing changed ingredient volume for '{pair.Key?.name}': "
                            + $"initial={pair.Value:0.000} ml, final={finalVolumeMl:0.000} ml.");
                    }
                    Require(harness.P95FrameTimeMs > 0f
                        && harness.P95FrameTimeMs <= MaximumP95Milliseconds,
                        $"p95 frame time {harness.P95FrameTimeMs:0.00} ms exceeds {MaximumP95Milliseconds:0.00} ms.");
                    Require(harness.P99FrameTimeMs > 0f
                        && harness.P99FrameTimeMs <= MaximumP99Milliseconds,
                        $"p99 frame time {harness.P99FrameTimeMs:0.00} ms exceeds {MaximumP99Milliseconds:0.00} ms.");
                    Require(finalSnapshot.HasSurfaceSample,
                        "No GPU surface sample is available for vessel translation validation.");

                    performanceReport = harness.BuildReport();
                    Require(gpu.SetVesselSuspended(finalTracker, true),
                        "The GPU vessel could not be suspended for cabinet storage.");
                    harness.SetAutoShake(false);
                    postMotionPhase = 1;
                    postMotionStartedAt = EditorApplication.timeSinceStartup;
                    return;
                }

                if (EditorApplication.timeSinceStartup - postMotionStartedAt
                    < PostMotionReadbackSeconds)
                {
                    return;
                }

                if (postMotionPhase == 1)
                {
                    Require(finalSnapshot.HasSurfaceSample,
                        "The suspended vessel lost its GPU surface sample.");
                    originalSurfacePosition = finalSnapshot.SurfaceWorldPosition;
                    finalTracker.TranslateTrackedParticles(VesselTranslation);
                    postMotionPhase = 2;
                    postMotionStartedAt = EditorApplication.timeSinceStartup;
                    return;
                }

                Vector2 expectedSurfacePosition = originalSurfacePosition
                    + VesselTranslation;
                Require(Vector2.Distance(
                        finalSnapshot.SurfaceWorldPosition,
                        expectedSurfacePosition) <= 0.03f,
                    $"Suspended GPU contents did not follow a vessel translation: "
                    + $"expected={expectedSurfacePosition}, actual={finalSnapshot.SurfaceWorldPosition}.");
                Require(gpu.ActiveParticleCount == StressParticleCount
                    && finalSnapshot.ParticleCount == StressParticleCount,
                    "Suspending and translating a stored vessel changed its liquid quantity.");
                finalTracker.TranslateTrackedParticles(-VesselTranslation);
                Require(gpu.SetVesselSuspended(finalTracker, false),
                    "The GPU vessel could not resume after cabinet storage.");
                Finish(
                    true,
                    performanceReport + "\nCabinet suspension and vessel translation passed.");
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= ValidateOnUpdate;
            SessionState.EraseBool(RunningKey);
            bool commandLine = SessionState.GetBool(RunningKey + ".CommandLine", false);
            SessionState.EraseBool(RunningKey + ".CommandLine");

            if (success)
                Debug.Log("[GpuLiquidPerformanceValidator] PASS: " + message);
            else
                Debug.LogError("[GpuLiquidPerformanceValidator] FAIL: " + message);

            if (commandLine)
                EditorApplication.Exit(success ? 0 : 1);
            else
                EditorApplication.isPlaying = false;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
