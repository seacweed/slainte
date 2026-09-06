using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Slainte.Bartending
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class GpuLiquidSystem : MonoBehaviour, ILiquidSimulationBackend
    {
        internal const uint StateStirAttempted = 1u << 0;
        internal const uint StateShakenWithIce = 1u << 1;
        internal const uint StateSuspended = 1u << 2;

        private const int ThreadGroupSize = 64;
        private const int MaximumBoundarySegments = 512;
        private const int MaximumVesselTriggers = 64;
        private const int MaximumAgitators = 8;
        private const string AccumulationShaderName = "Hidden/Slainte/GpuLiquidAccumulation";

        public static GpuLiquidSystem Instance { get; private set; }

        private readonly Dictionary<ItemDef, int> ingredientIndices = new();
        private readonly List<ItemDef> ingredients = new();
        private readonly Dictionary<VesselLiquidTracker, GpuLiquidVesselProxy> vesselByTracker = new();
        private readonly Dictionary<uint, GpuLiquidVesselProxy> vesselById = new();
        private readonly List<GpuLiquidVesselProxy> vessels = new();
        private readonly List<GpuLiquidBoundarySegment> boundaryList = new(MaximumBoundarySegments);
        private readonly List<GpuLiquidVesselTrigger> triggerList = new(MaximumVesselTriggers);

        private BusinessBartendingSettings settings;
        private ComputeShader simulationShader;
        private Material accumulationMaterial;
        private MaterialPropertyBlock renderProperties;

        private GraphicsBuffer particleBuffer;
        private GraphicsBuffer compositionA;
        private GraphicsBuffer compositionB;
        private GraphicsBuffer particleColorBuffer;
        private GraphicsBuffer positionDeltaBuffer;
        private GraphicsBuffer lambdaBuffer;
        private GraphicsBuffer gridHeadBuffer;
        private GraphicsBuffer gridNextBuffer;
        private GraphicsBuffer freeIndexBuffer;
        private GraphicsBuffer freeCountBuffer;
        private GraphicsBuffer spawnCommandBuffer;
        private GraphicsBuffer ingredientVisualBuffer;
        private GraphicsBuffer boundaryBuffer;
        private GraphicsBuffer triggerBuffer;
        private GraphicsBuffer agitatorBuffer;
        private GraphicsBuffer statisticsBuffer;

        private GpuLiquidSpawnCommand[] spawnCommands;
        private GpuLiquidIngredientVisual[] ingredientVisuals;
        private GpuLiquidBoundarySegment[] boundaryUpload;
        private GpuLiquidVesselTrigger[] triggerUpload;
        private GpuLiquidAgitator[] agitatorUpload;
        private GpuLiquidParticle[] snapshotParticles;
        private float[] snapshotComposition;

        private int resetKernel;
        private int resetCompositionKernel;
        private int spawnKernel;
        private int integrateKernel;
        private int clearGridKernel;
        private int buildGridKernel;
        private int lambdaKernel;
        private int deltaKernel;
        private int applyKernel;
        private int velocityKernel;
        private int mixKernel;
        private int colorKernel;
        private int techniqueKernel;
        private int translateVesselKernel;
        private int suspendVesselKernel;

        private int particleCapacity;
        private int maximumIngredients;
        private int gridWidth;
        private int gridHeight;
        private int gridCellCount;
        private int pendingSpawnCount;
        private int activeParticleCount;
        private int renderLayer;
        private int closedVesselTriggerCount;
        private uint nextVesselId = 1;
        private bool compositionAIsCurrent = true;
        private bool configured;
        private bool initialized;
        private bool resetRequested;
        private bool readbackInFlight;
        private bool particleReadbackReady;
        private bool compositionReadbackReady;
        private bool disposed;
        private float nextSnapshotTime;
        private float lastFixedTime;
        private string initializationError = string.Empty;

        private Action<AsyncGPUReadbackRequest> particleReadbackCallback;
        private Action<AsyncGPUReadbackRequest> compositionReadbackCallback;

        public bool IsOperational => initialized && !disposed;
        public bool IsGpuBackend => true;
        public float DefaultParticleVolumeMl { get; private set; } = 1f;
        public int ActiveParticleCount => activeParticleCount + pendingSpawnCount;
        public int ParticleCapacity => particleCapacity;
        public int IngredientCount => ingredients.Count;
        public int BoundarySegmentCount => boundaryList.Count;
        public int VesselCount => vessels.Count;
        public int ClosedVesselTriggerCount => closedVesselTriggerCount;
        public float StirCompositionTolerance => settings != null
            ? Mathf.Clamp01(settings.gpuLiquidStirCompositionTolerance)
            : 0.08f;
        public string InitializationError => initializationError;

        internal GraphicsBuffer ParticleBuffer => particleBuffer;
        internal GraphicsBuffer ParticleColorBuffer => particleColorBuffer;
        internal Material AccumulationMaterial => accumulationMaterial;
        internal MaterialPropertyBlock RenderProperties => renderProperties;
        internal float ParticleRenderRadius => settings != null
            ? Mathf.Max(
                0.01f,
                settings.gpuLiquidParticleRadius
                    * Mathf.Clamp(
                        settings.gpuLiquidContainedRenderRadiusMultiplier,
                        1f,
                        1.5f))
            : 0.075f;
        internal float AirborneParticleRenderRadius => settings != null
            ? Mathf.Max(
                0.01f,
                settings.gpuLiquidParticleRadius
                    * Mathf.Clamp(settings.gpuLiquidRenderRadiusMultiplier, 1f, 2f))
            : 0.09f;

        public static bool CanRun(BusinessBartendingSettings candidate, out string reason)
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                reason = "The active graphics device does not support compute shaders.";
                return false;
            }

            if (candidate == null || candidate.gpuLiquidComputeShader == null)
            {
                reason = "BusinessBartendingSettings has no GPU liquid compute shader.";
                return false;
            }

            Shader renderShader = Shader.Find(AccumulationShaderName);
            if (renderShader == null || !renderShader.isSupported)
            {
                reason = $"Shader '{AccumulationShaderName}' is unavailable or unsupported.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public void Configure(
            BusinessBartendingSettings configuration,
            int targetRenderLayer)
        {
            settings = configuration;
            renderLayer = targetRenderLayer;
            configured = configuration != null;
            if (configuration != null)
                DefaultParticleVolumeMl = Mathf.Max(0.01f, configuration.gpuLiquidParticleVolumeMl);
        }

        private void OnEnable()
        {
            disposed = false;
            if (!configured)
                return;

            Initialize();
            if (initialized)
                Instance = this;
        }

        private void Initialize()
        {
            if (initialized)
                return;

            if (!CanRun(settings, out initializationError))
            {
                Debug.LogWarning("[GpuLiquidSystem] " + initializationError, this);
                return;
            }

            try
            {
                particleCapacity = Mathf.Max(64, settings.gpuLiquidParticleCapacity);
                maximumIngredients = Mathf.Clamp(settings.gpuLiquidMaximumIngredients, 1, 32);
                float smoothingRadius = Mathf.Max(
                    settings.gpuLiquidParticleRadius * 2f,
                    settings.gpuLiquidSmoothingRadius);
                Vector2 worldSize = settings.gpuLiquidWorldMax - settings.gpuLiquidWorldMin;
                gridWidth = Mathf.Max(1, Mathf.CeilToInt(worldSize.x / smoothingRadius));
                gridHeight = Mathf.Max(1, Mathf.CeilToInt(worldSize.y / smoothingRadius));
                gridCellCount = gridWidth * gridHeight;

                simulationShader = Instantiate(settings.gpuLiquidComputeShader);
                simulationShader.name = settings.gpuLiquidComputeShader.name + " (Runtime)";
                CacheKernels();
                AllocateBuffers();

                Shader renderShader = Shader.Find(AccumulationShaderName);
                accumulationMaterial = new Material(renderShader)
                {
                    name = "GPU Liquid Accumulation (Runtime)",
                    hideFlags = HideFlags.DontSave
                };
                renderProperties = new MaterialPropertyBlock();
                particleReadbackCallback = OnParticleReadback;
                compositionReadbackCallback = OnCompositionReadback;

                BindStaticParameters();
                DispatchReset();
                initialized = true;
                RegisterExistingVessels();
            }
            catch (Exception exception)
            {
                initializationError = exception.Message;
                Debug.LogException(exception, this);
                ReleaseResources();
            }
        }

        private void CacheKernels()
        {
            resetKernel = simulationShader.FindKernel("ResetParticles");
            resetCompositionKernel = simulationShader.FindKernel("ResetCompositionBuffers");
            spawnKernel = simulationShader.FindKernel("SpawnParticles");
            integrateKernel = simulationShader.FindKernel("IntegrateParticles");
            clearGridKernel = simulationShader.FindKernel("ClearGrid");
            buildGridKernel = simulationShader.FindKernel("BuildGrid");
            lambdaKernel = simulationShader.FindKernel("CalculateDensityLambda");
            deltaKernel = simulationShader.FindKernel("CalculatePositionDelta");
            applyKernel = simulationShader.FindKernel("ApplyDeltaAndBoundaries");
            velocityKernel = simulationShader.FindKernel("UpdateVelocities");
            mixKernel = simulationShader.FindKernel("MixComposition");
            colorKernel = simulationShader.FindKernel("UpdateParticleColors");
            techniqueKernel = simulationShader.FindKernel("ApplyTechnique");
            translateVesselKernel = simulationShader.FindKernel("TranslateVesselContents");
            suspendVesselKernel = simulationShader.FindKernel("SetVesselSuspended");
        }

        private void AllocateBuffers()
        {
            particleBuffer = CreateStructured<GpuLiquidParticle>(particleCapacity);
            compositionA = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCapacity * maximumIngredients,
                sizeof(float));
            compositionB = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCapacity * maximumIngredients,
                sizeof(float));
            particleColorBuffer = CreateStructured<Vector4>(particleCapacity);
            positionDeltaBuffer = CreateStructured<Vector2>(particleCapacity);
            lambdaBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCapacity,
                sizeof(float));
            gridHeadBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                gridCellCount,
                sizeof(int));
            gridNextBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCapacity,
                sizeof(int));
            freeIndexBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCapacity,
                sizeof(uint));
            freeCountBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(int));
            spawnCommandBuffer = CreateStructured<GpuLiquidSpawnCommand>(particleCapacity);
            ingredientVisualBuffer = CreateStructured<GpuLiquidIngredientVisual>(maximumIngredients);
            boundaryBuffer = CreateStructured<GpuLiquidBoundarySegment>(MaximumBoundarySegments);
            triggerBuffer = CreateStructured<GpuLiquidVesselTrigger>(MaximumVesselTriggers);
            agitatorBuffer = CreateStructured<GpuLiquidAgitator>(MaximumAgitators);
            statisticsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                4,
                sizeof(uint));

            spawnCommands = new GpuLiquidSpawnCommand[particleCapacity];
            ingredientVisuals = new GpuLiquidIngredientVisual[maximumIngredients];
            boundaryUpload = new GpuLiquidBoundarySegment[MaximumBoundarySegments];
            triggerUpload = new GpuLiquidVesselTrigger[MaximumVesselTriggers];
            agitatorUpload = new GpuLiquidAgitator[MaximumAgitators];
            snapshotParticles = new GpuLiquidParticle[particleCapacity];
            snapshotComposition = new float[particleCapacity * maximumIngredients];
        }

        private static GraphicsBuffer CreateStructured<T>(int count) where T : struct
        {
            return new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                count,
                Marshal.SizeOf<T>());
        }

        private void BindStaticParameters()
        {
            simulationShader.SetInt("_ParticleCapacity", particleCapacity);
            simulationShader.SetInt("_MaximumIngredients", maximumIngredients);
            simulationShader.SetInt("_GridWidth", gridWidth);
            simulationShader.SetInt("_GridHeight", gridHeight);
            simulationShader.SetInt("_GridCellCount", gridCellCount);
            simulationShader.SetVector("_GridOrigin", settings.gpuLiquidWorldMin);
            simulationShader.SetVector("_WorldMin", settings.gpuLiquidWorldMin);
            simulationShader.SetVector("_WorldMax", settings.gpuLiquidWorldMax);
            simulationShader.SetFloat(
                "_ParticleRadius",
                Mathf.Max(0.01f, settings.gpuLiquidParticleRadius));
            simulationShader.SetFloat(
                "_SmoothingRadius",
                Mathf.Max(
                    settings.gpuLiquidParticleRadius * 2f,
                    settings.gpuLiquidSmoothingRadius));
            simulationShader.SetFloat(
                "_RestDensity",
                Mathf.Max(0.01f, settings.gpuLiquidRestDensity));
            simulationShader.SetFloat(
                "_DensityCompliance",
                Mathf.Max(0f, settings.gpuLiquidDensityCompliance));
            simulationShader.SetFloat(
                "_LambdaEpsilon",
                Mathf.Max(0.000001f, settings.gpuLiquidLambdaEpsilon));
            simulationShader.SetFloat(
                "_ArtificialPressureStrength",
                Mathf.Clamp(settings.gpuLiquidArtificialPressureStrength, 0f, 0.02f));
            simulationShader.SetFloat(
                "_ArtificialPressureRadiusRatio",
                Mathf.Clamp(settings.gpuLiquidArtificialPressureRadiusRatio, 0.1f, 0.9f));
            simulationShader.SetFloat(
                "_BoundaryDensityScale",
                Mathf.Clamp(settings.gpuLiquidBoundaryDensityScale, 0f, 12f));
            simulationShader.SetFloat(
                "_WallFriction",
                Mathf.Clamp01(settings.gpuLiquidWallFriction));
            simulationShader.SetFloat(
                "_WallRestitution",
                Mathf.Clamp01(settings.gpuLiquidWallRestitution));
            simulationShader.SetFloat(
                "_SplashTransfer",
                Mathf.Clamp01(settings.gpuLiquidSplashTransfer));
            simulationShader.SetFloat(
                "_SplashMinimumImpactSpeed",
                Mathf.Max(0f, settings.gpuLiquidSplashMinimumImpactSpeed));
            simulationShader.SetFloat(
                "_BottomImpactSpread",
                Mathf.Clamp01(settings.gpuLiquidBottomImpactSpread));
            simulationShader.SetFloat(
                "_VelocityDamping",
                Mathf.Clamp01(settings.gpuLiquidVelocityDamping));
            simulationShader.SetFloat(
                "_MaximumSpeed",
                Mathf.Max(0.1f, settings.gpuLiquidMaximumSpeed));
            simulationShader.SetFloat(
                "_ClosedVesselDampingRate",
                Mathf.Max(0f, settings.gpuLiquidClosedVesselDampingRate));
            simulationShader.SetFloat(
                "_ClosedVesselMaximumRelativeSpeed",
                Mathf.Max(0.1f, settings.gpuLiquidClosedVesselMaximumRelativeSpeed));
            simulationShader.SetFloat(
                "_Viscosity",
                Mathf.Clamp01(settings.gpuLiquidViscosity));
            simulationShader.SetFloat(
                "_PassiveMixRate",
                Mathf.Max(0f, settings.gpuLiquidPassiveMixRate));
            simulationShader.SetFloat(
                "_AgitationMixRate",
                Mathf.Max(0f, settings.gpuLiquidAgitationMixRate));
            simulationShader.SetFloat(
                "_FullMixRelativeSpeed",
                Mathf.Max(0.01f, settings.gpuLiquidFullMixRelativeSpeed));
            simulationShader.SetFloat(
                "_MaximumMixPerSubstep",
                Mathf.Clamp(settings.gpuLiquidMaximumMixPerSubstep, 0.01f, 0.5f));
            simulationShader.SetFloat(
                "_MinimumVisibleAlpha",
                Mathf.Clamp01(settings.liquidMinimumVisibleAlpha));

            BindCommonBuffers(resetKernel);
            BindCommonBuffers(resetCompositionKernel);
            BindCommonBuffers(spawnKernel);
            BindCommonBuffers(integrateKernel);
            BindCommonBuffers(buildGridKernel);
            BindCommonBuffers(lambdaKernel);
            BindCommonBuffers(deltaKernel);
            BindCommonBuffers(applyKernel);
            BindCommonBuffers(velocityKernel);
            BindCommonBuffers(mixKernel);
            BindCommonBuffers(colorKernel);
            BindCommonBuffers(techniqueKernel);
            BindCommonBuffers(translateVesselKernel);
            BindCommonBuffers(suspendVesselKernel);
            simulationShader.SetBuffer(clearGridKernel, "_GridHeads", gridHeadBuffer);
        }

        private void BindCommonBuffers(int kernel)
        {
            simulationShader.SetBuffer(kernel, "_Particles", particleBuffer);
            simulationShader.SetBuffer(kernel, "_CompositionA", compositionA);
            simulationShader.SetBuffer(kernel, "_CompositionB", compositionB);
            simulationShader.SetBuffer(kernel, "_ParticleColors", particleColorBuffer);
            simulationShader.SetBuffer(kernel, "_PositionDeltas", positionDeltaBuffer);
            simulationShader.SetBuffer(kernel, "_Lambdas", lambdaBuffer);
            simulationShader.SetBuffer(kernel, "_GridHeads", gridHeadBuffer);
            simulationShader.SetBuffer(kernel, "_GridNext", gridNextBuffer);
            simulationShader.SetBuffer(kernel, "_FreeIndices", freeIndexBuffer);
            simulationShader.SetBuffer(kernel, "_FreeCount", freeCountBuffer);
            simulationShader.SetBuffer(kernel, "_SpawnCommands", spawnCommandBuffer);
            simulationShader.SetBuffer(kernel, "_IngredientVisuals", ingredientVisualBuffer);
            simulationShader.SetBuffer(kernel, "_Boundaries", boundaryBuffer);
            simulationShader.SetBuffer(kernel, "_VesselTriggers", triggerBuffer);
            simulationShader.SetBuffer(kernel, "_Agitators", agitatorBuffer);
            simulationShader.SetBuffer(kernel, "_Statistics", statisticsBuffer);
        }

        private void DispatchReset()
        {
            compositionAIsCurrent = true;
            pendingSpawnCount = 0;
            activeParticleCount = 0;
            DispatchForCount(resetKernel, particleCapacity);
            DispatchForCount(resetCompositionKernel, particleCapacity);
        }

        public bool TryEmit(
            Vector2 worldPosition,
            Vector2 initialVelocity,
            ItemDef sourceItem,
            float volumeMl)
        {
            return TryEmitInternal(
                worldPosition,
                initialVelocity,
                sourceItem,
                volumeMl,
                null);
        }

        public bool TryEmitIntoVessel(
            Vector2 worldPosition,
            Vector2 initialVelocity,
            ItemDef sourceItem,
            float volumeMl,
            VesselLiquidTracker targetVessel)
        {
            return TryEmitInternal(
                worldPosition,
                initialVelocity,
                sourceItem,
                volumeMl,
                targetVessel);
        }

        private bool TryEmitInternal(
            Vector2 worldPosition,
            Vector2 initialVelocity,
            ItemDef sourceItem,
            float volumeMl,
            VesselLiquidTracker targetVessel)
        {
            if (!IsOperational || sourceItem == null || volumeMl <= 0f)
                return false;
            if (pendingSpawnCount >= spawnCommands.Length)
                return false;

            int ingredientIndex = GetOrRegisterIngredient(sourceItem);
            if (ingredientIndex < 0)
                return false;

            uint vesselId = 0;
            VesselLiquidTracker containingVessel = targetVessel != null
                ? targetVessel
                : FindVesselAtWorldPoint(worldPosition);
            if (containingVessel != null)
            {
                if (!vesselByTracker.TryGetValue(
                        containingVessel,
                        out GpuLiquidVesselProxy vesselProxy))
                {
                    return false;
                }
                vesselId = vesselProxy.VesselId;
            }

            spawnCommands[pendingSpawnCount++] = new GpuLiquidSpawnCommand
            {
                Position = worldPosition,
                Velocity = initialVelocity,
                VolumeMl = volumeMl,
                TemperatureC = sourceItem.servingTemperatureC,
                SourceIngredient = (uint)ingredientIndex,
                VesselId = vesselId,
                StateFlags = 0
            };
            return true;
        }

        public void ResetSimulation()
        {
            if (IsOperational)
                resetRequested = true;
        }

        private int GetOrRegisterIngredient(ItemDef item)
        {
            if (ingredientIndices.TryGetValue(item, out int existing))
                return existing;
            if (ingredients.Count >= maximumIngredients)
            {
                Debug.LogError(
                    $"[GpuLiquidSystem] Active liquid ingredient capacity ({maximumIngredients}) exceeded by '{item.name}'.",
                    this);
                return -1;
            }

            int index = ingredients.Count;
            ingredients.Add(item);
            ingredientIndices.Add(item, index);
            Color color = item.liquidColor;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                float alpha = color.a;
                color = color.linear;
                color.a = alpha;
            }
            ingredientVisuals[index] = new GpuLiquidIngredientVisual
            {
                Color = color,
                InheritMixedColor = item.inheritMixedLiquidColor ? 1u : 0u
            };
            ingredientVisualBuffer.SetData(ingredientVisuals, index, index, 1);
            simulationShader.SetInt("_IngredientCount", ingredients.Count);
            return index;
        }

        private void FixedUpdate()
        {
            if (!IsOperational)
                return;

            float fixedDeltaTime = Mathf.Max(0.0001f, Time.fixedDeltaTime);
            if (resetRequested)
            {
                resetRequested = false;
                DispatchReset();
            }

            UploadGeometry(fixedDeltaTime);
            UploadAgitators();
            DispatchSpawns();

            int substeps = Mathf.Clamp(settings.gpuLiquidSubsteps, 1, 4);
            int solverIterations = Mathf.Clamp(settings.gpuLiquidSolverIterations, 1, 10);
            float substepDelta = fixedDeltaTime / substeps;
            simulationShader.SetFloat("_DeltaTime", substepDelta);
            simulationShader.SetVector("_Gravity", Physics2D.gravity);

            for (int substep = 0; substep < substeps; substep++)
            {
                DispatchForCount(integrateKernel, particleCapacity);
                for (int iteration = 0; iteration < solverIterations; iteration++)
                {
                    RebuildGrid();
                    DispatchForCount(lambdaKernel, particleCapacity);
                    DispatchForCount(deltaKernel, particleCapacity);
                    DispatchForCount(applyKernel, particleCapacity);
                }

                RebuildGrid();
                DispatchForCount(velocityKernel, particleCapacity);
                DispatchMix();
            }

            BindCurrentComposition(colorKernel);
            DispatchForCount(colorKernel, particleCapacity);
            lastFixedTime = Time.unscaledTime;
        }

        private void DispatchSpawns()
        {
            if (pendingSpawnCount <= 0)
                return;

            spawnCommandBuffer.SetData(spawnCommands, 0, 0, pendingSpawnCount);
            simulationShader.SetInt("_SpawnCount", pendingSpawnCount);
            DispatchForCount(spawnKernel, pendingSpawnCount);
            pendingSpawnCount = 0;
        }

        private void RebuildGrid()
        {
            DispatchForCount(clearGridKernel, gridCellCount);
            DispatchForCount(buildGridKernel, particleCapacity);
        }

        private void DispatchMix()
        {
            GraphicsBuffer read = compositionAIsCurrent ? compositionA : compositionB;
            GraphicsBuffer write = compositionAIsCurrent ? compositionB : compositionA;
            simulationShader.SetBuffer(mixKernel, "_CompositionRead", read);
            simulationShader.SetBuffer(mixKernel, "_CompositionWrite", write);
            DispatchForCount(mixKernel, particleCapacity);
            compositionAIsCurrent = !compositionAIsCurrent;
        }

        private void BindCurrentComposition(int kernel)
        {
            simulationShader.SetBuffer(
                kernel,
                "_CompositionRead",
                compositionAIsCurrent ? compositionA : compositionB);
        }

        private void UploadGeometry(float deltaTime)
        {
            boundaryList.Clear();
            triggerList.Clear();
            for (int i = 0; i < vessels.Count; i++)
            {
                vessels[i]?.CollectGeometry(boundaryList, triggerList, deltaTime);
            }

            int boundaryCount = Mathf.Min(boundaryList.Count, MaximumBoundarySegments);
            for (int i = 0; i < boundaryCount; i++)
                boundaryUpload[i] = boundaryList[i];
            if (boundaryCount > 0)
                boundaryBuffer.SetData(boundaryUpload, 0, 0, boundaryCount);

            int triggerCount = Mathf.Min(triggerList.Count, MaximumVesselTriggers);
            closedVesselTriggerCount = 0;
            for (int i = 0; i < triggerCount; i++)
            {
                triggerUpload[i] = triggerList[i];
                if ((triggerUpload[i].Flags & 1u) != 0)
                    closedVesselTriggerCount++;
            }
            if (triggerCount > 0)
                triggerBuffer.SetData(triggerUpload, 0, 0, triggerCount);

            simulationShader.SetInt("_BoundaryCount", boundaryCount);
            simulationShader.SetInt("_VesselTriggerCount", triggerCount);

            if (boundaryList.Count > MaximumBoundarySegments)
            {
                Debug.LogError(
                    $"[GpuLiquidSystem] Boundary segment capacity exceeded: {boundaryList.Count}/{MaximumBoundarySegments}.",
                    this);
            }
            if (triggerList.Count > MaximumVesselTriggers)
            {
                Debug.LogError(
                    $"[GpuLiquidSystem] Vessel trigger capacity exceeded: {triggerList.Count}/{MaximumVesselTriggers}.",
                    this);
            }
        }

        private void UploadAgitators()
        {
            int activeCount = 0;
            for (int i = 0; i < agitatorUpload.Length; i++)
            {
                if (agitatorUpload[i].Active != 0)
                {
                    agitatorUpload[activeCount++] = agitatorUpload[i];
                }
            }

            if (activeCount > 0)
                agitatorBuffer.SetData(agitatorUpload, 0, 0, activeCount);
            simulationShader.SetInt("_AgitatorCount", activeCount);

            for (int i = 0; i < agitatorUpload.Length; i++)
                agitatorUpload[i].Active = 0;
        }

        public void SetAgitator(
            Vector2 a,
            Vector2 b,
            Vector2 velocity,
            float radius,
            float strength,
            VesselLiquidTracker vessel)
        {
            if (!IsOperational)
                return;

            uint vesselId = 0;
            if (vessel != null && vesselByTracker.TryGetValue(vessel, out GpuLiquidVesselProxy proxy))
                vesselId = proxy.VesselId;

            agitatorUpload[0] = new GpuLiquidAgitator
            {
                A = a,
                B = b,
                Velocity = velocity,
                Radius = Mathf.Max(0.01f, radius),
                Strength = Mathf.Max(0f, strength),
                VesselId = vesselId,
                Active = 1
            };
        }

        public VesselLiquidTracker FindVesselAtWorldPoint(Vector2 worldPoint)
        {
            GpuLiquidVesselProxy preferred = null;
            for (int i = 0; i < vessels.Count; i++)
            {
                GpuLiquidVesselProxy candidate = vessels[i];
                if (candidate == null || !candidate.ContainsPoint(worldPoint))
                    continue;

                if (preferred == null
                    || candidate.Tracker.InteractionPriority
                        >= preferred.Tracker.InteractionPriority)
                {
                    preferred = candidate;
                }
            }

            return preferred?.Tracker;
        }

        internal void RegisterVessel(VesselLiquidTracker tracker)
        {
            if (!IsOperational || tracker == null || vesselByTracker.ContainsKey(tracker))
                return;

            GpuLiquidVesselProxy proxy = new GpuLiquidVesselProxy(
                tracker,
                nextVesselId++,
                maximumIngredients);
            vesselByTracker.Add(tracker, proxy);
            vesselById.Add(proxy.VesselId, proxy);
            vessels.Add(proxy);
        }

        internal void UnregisterVessel(VesselLiquidTracker tracker)
        {
            if (tracker == null || !vesselByTracker.TryGetValue(tracker, out GpuLiquidVesselProxy proxy))
                return;

            vesselByTracker.Remove(tracker);
            vesselById.Remove(proxy.VesselId);
            vessels.Remove(proxy);
        }

        private void RegisterExistingVessels()
        {
            VesselLiquidTracker[] trackers = FindObjectsByType<VesselLiquidTracker>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < trackers.Length; i++)
                RegisterVessel(trackers[i]);
        }

        public bool TryGetSnapshot(
            VesselLiquidTracker tracker,
            out GpuLiquidVesselSnapshot snapshot)
        {
            if (tracker != null && vesselByTracker.TryGetValue(tracker, out GpuLiquidVesselProxy proxy))
            {
                snapshot = proxy.Snapshot;
                return true;
            }

            snapshot = null;
            return false;
        }

        internal bool TryPopulateComposition(
            VesselLiquidTracker tracker,
            CocktailComposition composition)
        {
            if (composition == null
                || !TryGetSnapshot(tracker, out GpuLiquidVesselSnapshot snapshot))
            {
                return false;
            }

            for (int i = 0; i < ingredients.Count; i++)
            {
                float volumeMl = snapshot.ComponentVolumesMl[i];
                if (volumeMl > 0.0001f)
                    composition.Add(ingredients[i], volumeMl);
            }

            composition.AddThermalSample(
                snapshot.TotalVolumeMl,
                snapshot.AverageTemperatureC);
            if (snapshot.StirAttemptedVolumeMl > 0f)
            {
                composition.RecordParticleTechniqueState(
                    snapshot.StirAttemptedVolumeMl,
                    CocktailTechnique.None,
                    true,
                    false);
            }
            if (snapshot.StirredVolumeMl > 0f)
            {
                composition.RecordParticleTechniqueState(
                    snapshot.StirredVolumeMl,
                    CocktailTechnique.Stir,
                    false,
                    false);
            }
            if (snapshot.ShakenVolumeMl > 0f)
            {
                composition.RecordParticleTechniqueState(
                    snapshot.ShakenVolumeMl,
                    CocktailTechnique.Shake,
                    false,
                    false);
            }
            if (snapshot.ShakenWithIceVolumeMl > 0f)
            {
                composition.RecordParticleTechniqueState(
                    snapshot.ShakenWithIceVolumeMl,
                    CocktailTechnique.None,
                    false,
                    true);
            }
            return true;
        }

        internal void MarkTechnique(
            VesselLiquidTracker tracker,
            CocktailTechnique technique,
            bool stirAttempted,
            bool shakenWithIce)
        {
            if (!IsOperational
                || tracker == null
                || !vesselByTracker.TryGetValue(tracker, out GpuLiquidVesselProxy proxy))
            {
                return;
            }

            simulationShader.SetInt("_TechniqueTargetVessel", (int)proxy.VesselId);
            simulationShader.SetInt("_TechniqueMask", (int)technique);
            uint stateMask = 0;
            if (stirAttempted)
                stateMask |= StateStirAttempted;
            if (shakenWithIce)
                stateMask |= StateShakenWithIce;
            simulationShader.SetInt("_TechniqueStateMask", (int)stateMask);
            DispatchForCount(techniqueKernel, particleCapacity);
            nextSnapshotTime = 0f;
        }

        public bool TranslateVesselContents(
            VesselLiquidTracker tracker,
            Vector2 delta)
        {
            if (!IsOperational
                || tracker == null
                || delta.sqrMagnitude <= 0.000001f
                || !vesselByTracker.TryGetValue(
                    tracker,
                    out GpuLiquidVesselProxy proxy))
            {
                return false;
            }

            simulationShader.SetInt("_TransformTargetVessel", (int)proxy.VesselId);
            simulationShader.SetVector(
                "_TransformDelta",
                new Vector4(delta.x, delta.y, 0f, 0f));
            DispatchForCount(translateVesselKernel, particleCapacity);
            nextSnapshotTime = 0f;
            return true;
        }

        public bool SetVesselSuspended(
            VesselLiquidTracker tracker,
            bool suspended)
        {
            if (!IsOperational
                || tracker == null
                || !vesselByTracker.TryGetValue(
                    tracker,
                    out GpuLiquidVesselProxy proxy))
            {
                return false;
            }

            simulationShader.SetInt("_SuspensionTargetVessel", (int)proxy.VesselId);
            simulationShader.SetInt("_SuspensionEnabled", suspended ? 1 : 0);
            DispatchForCount(suspendVesselKernel, particleCapacity);
            nextSnapshotTime = 0f;
            return true;
        }

        public bool SettleVesselAfterImmediateMotion(
            VesselLiquidTracker tracker)
        {
            if (!IsOperational
                || tracker == null
                || !vesselByTracker.TryGetValue(
                    tracker,
                    out GpuLiquidVesselProxy proxy))
            {
                return false;
            }

            proxy.SynchronizeTransformHistory();
            simulationShader.SetInt("_SuspensionTargetVessel", (int)proxy.VesselId);
            simulationShader.SetInt("_SuspensionEnabled", 1);
            DispatchForCount(suspendVesselKernel, particleCapacity);
            simulationShader.SetInt("_SuspensionEnabled", 0);
            DispatchForCount(suspendVesselKernel, particleCapacity);
            nextSnapshotTime = 0f;
            return true;
        }

        private void Update()
        {
            if (!IsOperational || readbackInFlight || Time.unscaledTime < nextSnapshotTime)
                return;

            nextSnapshotTime = Time.unscaledTime
                + Mathf.Max(0.05f, settings.gpuLiquidSnapshotInterval);
            RequestSnapshotReadback();
        }

        private void RequestSnapshotReadback()
        {
            readbackInFlight = true;
            particleReadbackReady = false;
            compositionReadbackReady = false;
            GraphicsBuffer currentComposition = compositionAIsCurrent
                ? compositionA
                : compositionB;

            if (SystemInfo.supportsAsyncGPUReadback)
            {
                AsyncGPUReadback.Request(particleBuffer, particleReadbackCallback);
                AsyncGPUReadback.Request(currentComposition, compositionReadbackCallback);
                return;
            }

            particleBuffer.GetData(snapshotParticles);
            currentComposition.GetData(snapshotComposition);
            particleReadbackReady = true;
            compositionReadbackReady = true;
            ProcessSnapshotData();
        }

        private void OnParticleReadback(AsyncGPUReadbackRequest request)
        {
            if (disposed)
                return;
            if (request.hasError)
            {
                FinishFailedReadback();
                return;
            }

            request.GetData<GpuLiquidParticle>().CopyTo(snapshotParticles);
            particleReadbackReady = true;
            TryProcessReadbackPair();
        }

        private void OnCompositionReadback(AsyncGPUReadbackRequest request)
        {
            if (disposed)
                return;
            if (request.hasError)
            {
                FinishFailedReadback();
                return;
            }

            request.GetData<float>().CopyTo(snapshotComposition);
            compositionReadbackReady = true;
            TryProcessReadbackPair();
        }

        private void TryProcessReadbackPair()
        {
            if (particleReadbackReady && compositionReadbackReady)
                ProcessSnapshotData();
        }

        private void FinishFailedReadback()
        {
            readbackInFlight = false;
            particleReadbackReady = false;
            compositionReadbackReady = false;
        }

        private void ProcessSnapshotData()
        {
            for (int i = 0; i < vessels.Count; i++)
                vessels[i].Snapshot.BeginAccumulation();

            activeParticleCount = 0;
            int ingredientCount = ingredients.Count;
            for (int particleIndex = 0; particleIndex < particleCapacity; particleIndex++)
            {
                GpuLiquidParticle particle = snapshotParticles[particleIndex];
                if (particle.Active == 0)
                    continue;

                activeParticleCount++;
                if (particle.VesselId == 0
                    || !vesselById.TryGetValue(
                        particle.VesselId,
                        out GpuLiquidVesselProxy proxy))
                {
                    continue;
                }

                GpuLiquidVesselSnapshot snapshot = proxy.Snapshot;
                float volumeMl = Mathf.Max(0f, particle.VolumeMl);
                snapshot.ParticleCount++;
                snapshot.TotalVolumeMl += volumeMl;
                snapshot.WeightedTemperature += particle.TemperatureC * volumeMl;
                snapshot.HighestParticleY = Mathf.Max(snapshot.HighestParticleY, particle.Position.y);
                if ((particle.StateFlags & StateStirAttempted) != 0)
                    snapshot.StirAttemptedVolumeMl += volumeMl;
                if ((particle.TechniqueFlags & (uint)CocktailTechnique.Stir) != 0)
                    snapshot.StirredVolumeMl += volumeMl;
                if ((particle.TechniqueFlags & (uint)CocktailTechnique.Shake) != 0)
                    snapshot.ShakenVolumeMl += volumeMl;
                if ((particle.StateFlags & StateShakenWithIce) != 0)
                    snapshot.ShakenWithIceVolumeMl += volumeMl;

                int compositionOffset = particleIndex * maximumIngredients;
                for (int ingredient = 0; ingredient < ingredientCount; ingredient++)
                {
                    snapshot.ComponentVolumesMl[ingredient] +=
                        Mathf.Max(0f, snapshotComposition[compositionOffset + ingredient])
                        * volumeMl;
                }
            }

            for (int particleIndex = 0; particleIndex < particleCapacity; particleIndex++)
            {
                GpuLiquidParticle particle = snapshotParticles[particleIndex];
                if (particle.Active == 0
                    || particle.VesselId == 0
                    || !vesselById.TryGetValue(
                        particle.VesselId,
                        out GpuLiquidVesselProxy proxy))
                {
                    continue;
                }

                GpuLiquidVesselSnapshot snapshot = proxy.Snapshot;
                float volumeMl = Mathf.Max(0f, particle.VolumeMl);
                if (volumeMl <= 0.0001f || snapshot.TotalVolumeMl <= 0.0001f)
                    continue;

                float deviation = 0f;
                int compositionOffset = particleIndex * maximumIngredients;
                for (int ingredient = 0; ingredient < ingredientCount; ingredient++)
                {
                    float particleRatio = Mathf.Max(
                        0f,
                        snapshotComposition[compositionOffset + ingredient]);
                    float vesselRatio = snapshot.ComponentVolumesMl[ingredient]
                        / snapshot.TotalVolumeMl;
                    deviation += Mathf.Abs(particleRatio - vesselRatio);
                }
                deviation = Mathf.Clamp01(deviation * 0.5f);
                snapshot.WeightedDeviation += deviation * volumeMl;
                snapshot.MaximumCompositionDeviation = Mathf.Max(
                    snapshot.MaximumCompositionDeviation,
                    deviation);
                if (deviation > StirCompositionTolerance)
                    snapshot.OutOfToleranceParticleCount++;

                if (particle.Position.y >= snapshot.HighestParticleY - 0.18f)
                {
                    Color particleColor = EvaluateParticleColor(
                        compositionOffset,
                        ingredientCount);
                    snapshot.SurfaceVolumeMl += volumeMl;
                    snapshot.SurfaceWeightedTemperature += particle.TemperatureC * volumeMl;
                    snapshot.SurfaceWeightedPosition += particle.Position * volumeMl;
                    snapshot.SurfaceWeightedColor += particleColor * volumeMl;
                }
            }

            for (int i = 0; i < vessels.Count; i++)
            {
                GpuLiquidVesselSnapshot snapshot = vessels[i].Snapshot;
                snapshot.EndAccumulation(ingredientCount);
            }

            readbackInFlight = false;
            particleReadbackReady = false;
            compositionReadbackReady = false;
        }

        private Color EvaluateParticleColor(int compositionOffset, int ingredientCount)
        {
            float independentTotal = 0f;
            float allTotal = 0f;
            for (int i = 0; i < ingredientCount; i++)
            {
                float weight = Mathf.Max(0f, snapshotComposition[compositionOffset + i]);
                allTotal += weight;
                if (ingredientVisuals[i].InheritMixedColor == 0)
                    independentTotal += weight;
            }

            float denominator = independentTotal > 0.0001f ? independentTotal : allTotal;
            if (denominator <= 0.0001f)
                return Color.clear;

            Color result = Color.clear;
            for (int i = 0; i < ingredientCount; i++)
            {
                if (independentTotal > 0.0001f
                    && ingredientVisuals[i].InheritMixedColor != 0)
                {
                    continue;
                }

                float weight = Mathf.Max(0f, snapshotComposition[compositionOffset + i])
                    / denominator;
                result += (Color)ingredientVisuals[i].Color * weight;
            }
            return result;
        }

        internal bool TryGetRenderResources(
            out Material material,
            out MaterialPropertyBlock properties,
            out int instanceCount)
        {
            if (!IsOperational
                || accumulationMaterial == null
                || particleBuffer == null
                || particleColorBuffer == null)
            {
                material = null;
                properties = null;
                instanceCount = 0;
                return false;
            }

            renderProperties.Clear();
            renderProperties.SetBuffer("_GpuLiquidParticles", particleBuffer);
            renderProperties.SetBuffer("_GpuLiquidColors", particleColorBuffer);
            renderProperties.SetFloat(
                "_GpuContainedParticleRadius",
                ParticleRenderRadius);
            renderProperties.SetFloat(
                "_GpuAirborneParticleRadius",
                AirborneParticleRenderRadius);
            renderProperties.SetFloat("_GpuParticleZ", 0f);
            renderProperties.SetFloat(
                "_GpuAirborneStretchMultiplier",
                settings != null
                    ? Mathf.Clamp(settings.gpuLiquidAirborneStretchMultiplier, 1f, 3f)
                    : 1.8f);
            renderProperties.SetFloat(
                "_GpuAirborneFullStretchSpeed",
                settings != null
                    ? Mathf.Max(0.1f, settings.gpuLiquidAirborneFullStretchSpeed)
                    : 3f);
            material = accumulationMaterial;
            properties = renderProperties;
            instanceCount = particleCapacity;
            return true;
        }

        private void DispatchForCount(int kernel, int count)
        {
            if (count <= 0)
                return;
            simulationShader.Dispatch(
                kernel,
                Mathf.CeilToInt(count / (float)ThreadGroupSize),
                1,
                1);
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
            disposed = true;
            ReleaseResources();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            disposed = true;
            ReleaseResources();
        }

        private void ReleaseResources()
        {
            initialized = false;
            ReleaseBuffer(ref particleBuffer);
            ReleaseBuffer(ref compositionA);
            ReleaseBuffer(ref compositionB);
            ReleaseBuffer(ref particleColorBuffer);
            ReleaseBuffer(ref positionDeltaBuffer);
            ReleaseBuffer(ref lambdaBuffer);
            ReleaseBuffer(ref gridHeadBuffer);
            ReleaseBuffer(ref gridNextBuffer);
            ReleaseBuffer(ref freeIndexBuffer);
            ReleaseBuffer(ref freeCountBuffer);
            ReleaseBuffer(ref spawnCommandBuffer);
            ReleaseBuffer(ref ingredientVisualBuffer);
            ReleaseBuffer(ref boundaryBuffer);
            ReleaseBuffer(ref triggerBuffer);
            ReleaseBuffer(ref agitatorBuffer);
            ReleaseBuffer(ref statisticsBuffer);

            if (accumulationMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(accumulationMaterial);
                else
                    DestroyImmediate(accumulationMaterial);
                accumulationMaterial = null;
            }

            if (simulationShader != null)
            {
                if (Application.isPlaying)
                    Destroy(simulationShader);
                else
                    DestroyImmediate(simulationShader);
                simulationShader = null;
            }
        }

        private static void ReleaseBuffer(ref GraphicsBuffer buffer)
        {
            if (buffer == null)
                return;
            buffer.Release();
            buffer = null;
        }
    }
}
