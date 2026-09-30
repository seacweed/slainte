using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Slainte.Bartending.FluidGpuExperiment
{
    /// <summary>Instance-scoped GPU liquid. Does not register with the legacy GPU or particle pool.</summary>
    public sealed partial class FluidExperimentGpuLiquid : MonoBehaviour
    {
        public FluidExperimentLiquidSettings settings;
        public Shader drawShader;
        public Camera outputCamera;
        public bool automaticReadback = true;
        public bool renderParticles = true;
        public bool useSurfaceRendering = true;
        private FluidExperimentWorld world;
        private ComputeShader simulationShader;
        private Material drawMaterial;
        private MaterialPropertyBlock properties;
        private const int ThreadGroupSize = 64, MaximumBoundarySegments = 2048, MaximumVesselTriggers = 256, MaximumAgitators = 8;
        private GraphicsBuffer particleBuffer, compositionA, compositionB, particleColorBuffer, positionDeltaBuffer,
            lambdaBuffer, gridHeadBuffer, gridNextBuffer, freeIndexBuffer, freeCountBuffer, spawnCommandBuffer,
            ingredientVisualBuffer, boundaryBuffer, triggerBuffer, agitatorBuffer, statisticsBuffer, velocitySnapshotBuffer;
        private GpuLiquidSpawnCommand[] spawnCommands;
        private GpuLiquidIngredientVisual[] ingredientVisuals;
        private GpuLiquidBoundarySegment[] boundaryUpload;
        private GpuLiquidVesselTrigger[] triggerUpload;
        private GpuLiquidAgitator[] agitatorUpload;
        private GpuLiquidParticle[] snapshotParticles;
        private float[] snapshotComposition;
        private int resetKernel, resetCompositionKernel, spawnKernel, integrateKernel, clearGridKernel, buildGridKernel,
            lambdaKernel, deltaKernel, applyKernel, velocityKernel, mixKernel, colorKernel, techniqueKernel,
            translateVesselKernel, suspendVesselKernel, swapKernel, releaseOwnerKernel, sweepKernel, snapshotVelocityKernel;
        private int particleCapacity, maximumIngredients, gridWidth, gridHeight, gridCellCount, pendingSpawnCount, activeParticleCount;
        private bool compositionAIsCurrent = true;
        private readonly Dictionary<ItemDef, int> ingredientIndices = new Dictionary<ItemDef, int>();
        private readonly List<ItemDef> ingredients = new List<ItemDef>();
        private int availableSlots;
        private long reservations;
        private float nextReadback;
        private bool readbackInFlight;
        private int generation;
        public bool IsOperational { get; private set; }
        public string Error { get; private set; } = "not initialized";
        public float ParticleVolumeMl => settings != null ? settings.gpuLiquidParticleVolumeMl : .5f;
        public float Radius => settings != null ? settings.gpuLiquidParticleRadius : .065f;
        public int ActiveCount => activeParticleCount;
        public int LastSubsteps { get; private set; }
        public float EmittedMl { get; private set; }
        public float SnapshotTotalMl { get; private set; }
        // Changes only after a complete particle/composition snapshot has been processed.
        public int SnapshotRevision { get; private set; }
        public GpuLiquidParticle[] Snapshot => snapshotParticles;
        public float[] CompositionSnapshot => snapshotComposition;
        public int IngredientStride => maximumIngredients;

        public void Initialize(FluidExperimentWorld owner)
        {
            if (IsOperational) return;
            world = owner;
            if (!SystemInfo.supportsComputeShaders || settings == null || settings.gpuLiquidComputeShader == null || drawShader == null)
            { Error = "A compute-capable GPU and the FluidExperiment shader assets are required."; Debug.LogError(Error, this); return; }
            try
            {
                particleCapacity = Mathf.Max(64, settings.gpuLiquidParticleCapacity);
                maximumIngredients = Mathf.Clamp(settings.gpuLiquidMaximumIngredients, 1, 32);
                activeSolver = solver;
                float h = SolverSmoothingRadius;
                Vector2 size = settings.gpuLiquidWorldMax - settings.gpuLiquidWorldMin;
                gridWidth = Mathf.CeilToInt(size.x / h); gridHeight = Mathf.CeilToInt(size.y / h);
                gridCellCount = gridWidth * gridHeight;
                simulationShader = Instantiate(settings.gpuLiquidComputeShader);
                CacheKernels();
                swapKernel = RequireKernel("SwapContents");
                releaseOwnerKernel = RequireKernel("ReleaseOwner");
                AllocateBuffers(); BindStaticParameters();
                BindCommonBuffers(swapKernel); BindCommonBuffers(releaseOwnerKernel);
                DispatchReset();
                availableSlots = particleCapacity;
                reservations = 0;
                drawMaterial = new Material(drawShader) { hideFlags = HideFlags.DontSave };
                properties = new MaterialPropertyBlock();
                properties.SetBuffer("_GpuLiquidParticles", particleBuffer);
                properties.SetBuffer("_GpuLiquidColors", particleColorBuffer);
                properties.SetFloat("_Radius", Radius * 1.3f);
                IsOperational = true; Error = string.Empty;
                RenderPipelineManager.beginCameraRendering -= QueueDraw;
                RenderPipelineManager.beginCameraRendering += QueueDraw;
            }
            catch (Exception ex) { Error = ex.ToString(); Debug.LogException(ex, this); Dispose(); }
        }

        public bool TryEmit(Vector2 position, Vector2 velocity, ItemDef ingredient, float volumeMl, uint vesselId)
        {
            if (ingredient == null || volumeMl <= 0 || !float.IsFinite(volumeMl)
                || !float.IsFinite(position.x) || !float.IsFinite(position.y)
                || !float.IsFinite(velocity.x) || !float.IsFinite(velocity.y)) return false;
            RecordEmissionRequest(ingredient, volumeMl);
            if (!IsOperational || availableSlots <= 0 || pendingSpawnCount >= particleCapacity) return RejectEmission(ingredient, volumeMl);
            if (!ingredientIndices.TryGetValue(ingredient, out int index))
            {
                if (ingredients.Count >= maximumIngredients) return RejectEmission(ingredient, volumeMl);
                index = ingredients.Count; ingredients.Add(ingredient); ingredientIndices.Add(ingredient, index);
                Color color = ingredient.liquidColor;
                if (QualitySettings.activeColorSpace == ColorSpace.Linear) color = color.linear;
                ingredientVisuals[index] = new GpuLiquidIngredientVisual { Color = color, InheritMixedColor = ingredient.inheritMixedLiquidColor ? 1u : 0u };
                ingredientVisualBuffer.SetData(ingredientVisuals, index, index, 1);
                simulationShader.SetInt("_IngredientCount", ingredients.Count);
            }
            spawnStreams[pendingSpawnCount] = new GpuLiquidStreamParticle { Pending = 1, BirthTime = simulationTime };
            spawnCommands[pendingSpawnCount++] = new GpuLiquidSpawnCommand
            {
                Position = position, Velocity = velocity, VolumeMl = volumeMl,
                TemperatureC = ingredient.servingTemperatureC, SourceIngredient = (uint)index, VesselId = vesselId
            };
            availableSlots--; reservations++; EmittedMl += volumeMl;
            RecordEmissionQueued(ingredient, volumeMl);
            return true;
        }
        public float Fill(FluidExperimentBody vessel, ItemDef ingredient, float volume)
        {
            if (!vessel.IsVessel || !IsOperational) return 0;
            if (useImprovedPhysics) return FillCalibrated(vessel, ingredient, volume);
            float emitted = 0;
            foreach (Rect region in vessel.contentRegions)
            {
                Vector3 scale = vessel.transform.lossyScale;
                float dx = Radius * 1.75f / Mathf.Abs(scale.x), dy = Radius * 1.75f / Mathf.Abs(scale.y);
                for (float y = region.yMin + dy; y < region.yMax - dy && emitted < volume; y += dy)
                for (float x = region.xMin + dx; x < region.xMax - dx && emitted < volume; x += dx)
                {
                    if (vessel.collisionProfile != null && !vessel.ContainsLiquidDisk(new Vector2(x, y), Radius)) continue;
                    float amount = Mathf.Min(ParticleVolumeMl, volume - emitted);
                    if (!TryEmit(vessel.LocalToWorld(new Vector2(x, y)), Vector2.zero, ingredient, amount, vessel.Id)) return emitted;
                    emitted += amount;
                }
            }
            return emitted;
        }
        public void Step(float dt)
        {
            if (!IsOperational || dt <= 0) return;
            bool record = FluidExperimentPerformance.IsRecording;
            long started = record ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            FluidExperimentPerformance.PhysicsSampler?.Begin();
            try { StepMeasured(dt); }
            finally
            {
                FluidExperimentPerformance.PhysicsSampler?.End();
                if (record) FluidExperimentPerformance.RecordPhysics(System.Diagnostics.Stopwatch.GetTimestamp() - started);
            }
        }
        private void StepMeasured(float dt)
        {
            liquidStateVersion++;
            if (pendingSpawnCount > 0)
            {
                for (int i = 0; i < pendingSpawnCount; i++)
                {
                    spawnStreams[i].Delay = Mathf.Clamp(spawnStreams[i].Delay, 0, dt);
                    spawnStreams[i].BirthTime = simulationTime + spawnStreams[i].Delay;
                }
                spawnCommandBuffer.SetData(spawnCommands, 0, 0, pendingSpawnCount);
                spawnStreamBuffer.SetData(spawnStreams, 0, 0, pendingSpawnCount);
                simulationShader.SetInt("_SpawnCount", pendingSpawnCount);
                DispatchForCount(spawnKernel, pendingSpawnCount);
                pendingSpawnCount = 0;
            }
            BeginImprovedSurfaceTick(dt);
            // Fluid time integration is independent of every object's movement and selection state.
            // Fast boundaries are handled by particle-local continuous collision detection instead.
            int steps = Mathf.Clamp(settings.gpuLiquidSubsteps, 1, 16);
            LastSubsteps = steps;
            float subDt = dt / steps;
            simulationShader.SetFloat("_DeltaTime", subDt);
            simulationShader.SetVector("_Gravity", Physics2D.gravity);
            simulationShader.SetInt("_AgitatorCount", 0); // Spoon and ice use moving solid geometry.
            for (int step = 0; step < steps; step++)
            {
                UploadGeometry(step / (float)steps, (step + 1f) / steps, subDt);
                DispatchForCount(integrateKernel, particleCapacity);
                DispatchForCount(sweepKernel, particleCapacity);
                if (activeSolver == FluidExperimentSolver.ReferenceSph)
                {
                    StepReferenceFluid();
                }
                else for (int iteration = 0; iteration < settings.gpuLiquidSolverIterations; iteration++)
                {
                    RebuildGrid();
                    DispatchForCount(lambdaKernel, particleCapacity);
                    DispatchForCount(deltaKernel, particleCapacity);
                    DispatchForCount(applyKernel, particleCapacity);
                }
                RebuildGrid();
                DispatchForCount(snapshotVelocityKernel, particleCapacity);
                BindCurrentComposition(velocityKernel);
                DispatchForCount(velocityKernel, particleCapacity);
                DispatchForCount(mergeStreamContactsKernel, particleCapacity);
                DispatchMix();
            }
            BindCurrentComposition(colorKernel); DispatchForCount(colorKernel, particleCapacity);
            EndImprovedSurfaceTick(dt);
            simulationTime += dt;
            if (automaticReadback && Time.unscaledTime >= nextReadback) RequestReadback();
        }
        private void UploadGeometry(float from, float to, float dt)
        {
            int boundaryCount = 0, triggerCount = 0, groupCount = 0;
            foreach (FluidExperimentBody item in world.Items)
            {
                if (item == null) continue;
                Vector2 previousPosition = Vector2.Lerp(item.PreviousPosition, item.Position, from);
                Vector2 position = Vector2.Lerp(item.PreviousPosition, item.Position, to);
                float previousAngle = item.PreviousAngle + item.StepAngle * from;
                float angle = item.PreviousAngle + item.StepAngle * to;
                Vector3 bodyScale = item.transform.lossyScale;
                float radians = angle * Mathf.Deg2Rad, sine = Mathf.Sin(radians), cosine = Mathf.Cos(radians);
                float angleDelta = (angle - previousAngle) * Mathf.Deg2Rad;
                Vector2 linearVelocity = (position - previousPosition) / dt;
                float angularVelocity = angleDelta / dt;
                uint flags = (item.IsHeld ? 2u : 0u) | (item.kind == LabItemKind.Ice ? 4u : 0u);
                int first = boundaryCount, contourFirst, contourEnd;
                void UploadPath(Vector2[] path, bool closed, bool vesselContour = false, bool ownershipOnly = false)
                {
                    int segments = closed ? path.Length : path.Length - 1;
                    for (int i = 0; i < segments; i++)
                    {
                        if (boundaryCount >= MaximumBoundarySegments) throw new InvalidOperationException("FluidExperiment boundary budget exceeded.");
                        Vector2 la = path[i], lb = path[(i + 1) % path.Length];
                        // Body pose/scale is fixed for this substep. Reuse its transform
                        // instead of repeated native lossyScale reads and trig per edge.
                        Vector2 localA = new Vector2(la.x * bodyScale.x, la.y * bodyScale.y);
                        Vector2 localB = new Vector2(lb.x * bodyScale.x, lb.y * bodyScale.y);
                        Vector2 a = position + new Vector2(localA.x * cosine - localA.y * sine, localA.x * sine + localA.y * cosine);
                        Vector2 b = position + new Vector2(localB.x * cosine - localB.y * sine, localB.x * sine + localB.y * cosine);
                        Vector2 offsetA = a - position, offsetB = b - position;
                        uint edgeFlags = flags | (vesselContour ? 8u : 0u);
                        if (ownershipOnly) edgeFlags |= 16u;
                        if (vesselContour && !item.sealedVessel && i == path.Length - 1) edgeFlags |= 16u;
                        boundaryUpload[boundaryCount++] = new GpuLiquidBoundarySegment
                        {
                            A = a, B = b,
                            VelocityA = linearVelocity + new Vector2(-offsetA.y, offsetA.x) * angularVelocity,
                            VelocityB = linearVelocity + new Vector2(-offsetB.y, offsetB.x) * angularVelocity,
                            VesselId = item.Id, Flags = edgeFlags, LocalA = localA, LocalB = localB,
                            StartPosition = previousPosition, EndPosition = position,
                            StartAngle = previousAngle * Mathf.Deg2Rad, AngleDelta = angleDelta
                        };
                    }
                }
                // A virtual rim completes the ownership polygon but never collides with open-vessel fluid.
                if (item.collisionProfile != null)
                {
                    foreach (FluidExperimentHull hull in item.collisionProfile.solids) UploadPath(hull.points, true);
                    if (item.sealedVessel) UploadPath(item.collisionProfile.lid, true);
                    contourFirst = boundaryCount;
                    UploadPath(item.collisionProfile.interior, true, true, true);
                    contourEnd = boundaryCount;
                }
                else
                {
                    contourFirst = boundaryCount;
                    UploadPath(item.liquidWall, item.wallClosed || item.IsVessel, item.IsVessel);
                    contourEnd = item.IsVessel ? boundaryCount : contourFirst;
                    foreach (FluidExperimentHull hull in item.extraSolidHulls) UploadPath(hull.points, true);
                }
                if (boundaryCount > first)
                    UploadBoundaryGroup(groupCount++, item.Id, first, boundaryCount, contourFirst, contourEnd);
                foreach (Rect rect in item.contentRegions)
                {
                    if (triggerCount >= MaximumVesselTriggers) throw new InvalidOperationException("FluidExperiment trigger budget exceeded.");
                    Vector2 center = new Vector2(rect.center.x * bodyScale.x, rect.center.y * bodyScale.y);
                    triggerUpload[triggerCount++] = new GpuLiquidVesselTrigger
                    {
                        Center = position + new Vector2(center.x * cosine - center.y * sine, center.x * sine + center.y * cosine),
                        AxisX = new Vector2(cosine, sine), AxisY = new Vector2(-sine, cosine),
                        HalfExtents = new Vector2(rect.width * bodyScale.x, rect.height * bodyScale.y) * .5f,
                        VesselId = item.Id, Priority = (int)item.Id, Active = 1,
                        Flags = flags | (item.sealedVessel ? 1u : 0u) | (item.collisionProfile != null ? 32u : 0u)
                    };
                }
            }
            if (boundaryCount > 0) boundaryBuffer.SetData(boundaryUpload, 0, 0, boundaryCount);
            if (groupCount > 0) boundaryGroupBuffer.SetData(boundaryGroups, 0, 0, groupCount);
            if (triggerCount > 0) triggerBuffer.SetData(triggerUpload, 0, 0, triggerCount);
            simulationShader.SetInt("_BoundaryCount", boundaryCount);
            simulationShader.SetInt("_BoundaryGroupCount", groupCount);
            simulationShader.SetInt("_VesselTriggerCount", triggerCount);
        }
        public void SwapContents(uint a, Vector2 deltaA, uint b, Vector2 deltaB)
        {
            if (!IsOperational) return;
            liquidStateVersion++;
            simulationShader.SetInt("_SwapA", (int)a); simulationShader.SetInt("_SwapB", (int)b);
            simulationShader.SetVector("_SwapDeltaA", deltaA); simulationShader.SetVector("_SwapDeltaB", deltaB);
            DispatchForCount(swapKernel, particleCapacity);
            InvalidateImprovedSurfaceHistory(a);
            if (b != 0) InvalidateImprovedSurfaceHistory(b);
            for (int i = 0; i < pendingSpawnCount; i++)
            {
                if (spawnCommands[i].VesselId == a) spawnCommands[i].Position += deltaA;
                else if (b != 0 && spawnCommands[i].VesselId == b) spawnCommands[i].Position += deltaB;
            }
        }
        public void ReleaseOwner(uint id)
        {
            if (!IsOperational || id == 0) return;
            liquidStateVersion++;
            simulationShader.SetInt("_TransformTargetVessel", (int)id);
            DispatchForCount(releaseOwnerKernel, particleCapacity);
            ResetImprovedSurfaceHistory();
        }
        public void ReadbackNow()
        {
            if (!IsOperational) return;
            // A synchronous read supersedes any older async request, including its staging callbacks.
            snapshotRequestToken++; readbackInFlight = false;
            LedgerCapture capture = CaptureLedger();
            CollectLedger();
            particleBuffer.GetData(snapshotParticles);
            (compositionAIsCurrent ? compositionA : compositionB).GetData(snapshotComposition);
            ledgerSnapshotBuffer.GetData(ledgerReadback);
            ProcessSnapshot(capture);
        }
        public void ResetSimulation()
        {
            if (!IsOperational) return;
            generation++; readbackInFlight = false;
            DispatchReset();
            availableSlots = particleCapacity; reservations = 0;
            EmittedMl = SnapshotTotalMl = 0;
            Array.Clear(snapshotParticles, 0, snapshotParticles.Length);
            Array.Clear(snapshotComposition, 0, snapshotComposition.Length);
            ResetImprovedSurfaceHistory();
        }
        private void RequestReadback()
        {
            if (readbackInFlight || !SystemInfo.supportsAsyncGPUReadback) return;
            readbackInFlight = true; nextReadback = Time.unscaledTime + .2f;
            int expectedGeneration = generation;
            int expectedRequest = ++snapshotRequestToken;
            LedgerCapture capture = CaptureLedger();
            CollectLedger();
            int completed = 0;
            bool failed = false;
            Action finish = () =>
            {
                if (generation != expectedGeneration || snapshotRequestToken != expectedRequest || !IsOperational) return;
                if (++completed != 3) return;
                readbackInFlight = false;
                if (!failed)
                {
                    // Publish all three datasets together. A failed/partial callback never exposes
                    // particle positions from one tick with ingredient ratios from another.
                    Array.Copy(stagingParticles, snapshotParticles, snapshotParticles.Length);
                    Array.Copy(stagingComposition, snapshotComposition, snapshotComposition.Length);
                    Array.Copy(stagingLedger, ledgerReadback, ledgerReadback.Length);
                    ProcessSnapshot(capture);
                }
            };
            AsyncGPUReadback.Request(particleBuffer, request =>
            {
                if (generation != expectedGeneration || snapshotRequestToken != expectedRequest || !IsOperational) return;
                if (request.hasError) failed = true;
                else request.GetData<GpuLiquidParticle>().CopyTo(stagingParticles);
                finish();
            });
            AsyncGPUReadback.Request(compositionAIsCurrent ? compositionA : compositionB, request =>
            {
                if (generation != expectedGeneration || snapshotRequestToken != expectedRequest || !IsOperational) return;
                if (request.hasError) failed = true;
                else request.GetData<float>().CopyTo(stagingComposition);
                finish();
            });
            AsyncGPUReadback.Request(ledgerSnapshotBuffer, request =>
            {
                if (generation != expectedGeneration || snapshotRequestToken != expectedRequest || !IsOperational) return;
                if (request.hasError) failed = true;
                else request.GetData<Vector4>().CopyTo(stagingLedger);
                finish();
            });
        }
        private void ProcessSnapshot(LedgerCapture capture)
        {
            activeParticleCount = 0; SnapshotTotalMl = 0;
            foreach (GpuLiquidParticle particle in snapshotParticles)
            {
                if (particle.Active == 0) continue;
                activeParticleCount++; SnapshotTotalMl += particle.VolumeMl;
            }
            // Reserved but not-yet-born GPU particles occupy slots even when Active == 0.
            Vector4 counts = ledgerReadback[(maximumIngredients + 1) * 2];
            int occupied = Mathf.RoundToInt(counts.x);
            int actualFree = Mathf.Clamp(Mathf.RoundToInt(counts.w), 0, particleCapacity);
            int freeAtCapture = Mathf.Min(particleCapacity - occupied, actualFree);
            availableSlots = Mathf.Max(0, freeAtCapture - (int)(reservations - capture.reservations));
            SnapshotRevision++;
            PublishLedger(capture);
        }
        public float VolumeIn(uint id)
        {
            float volume = 0;
            if (snapshotParticles == null) return 0;
            foreach (GpuLiquidParticle particle in snapshotParticles) if (particle.Active != 0 && particle.VesselId == id) volume += particle.VolumeMl;
            return volume;
        }
        public uint ReadSweepExhaustions()
        {
            if (!IsOperational) return 0;
            var counters = new uint[4]; statisticsBuffer.GetData(counters);
            return counters[2];
        }
        private void QueueDraw(ScriptableRenderContext context, Camera camera)
        {
            if (surfaceRenderer != null) surfaceRenderer.forceRenderingOff = true;
            if (!IsOperational || !renderParticles || drawMaterial == null || camera != outputCamera) return;
            if (useSurfaceRendering && DrawLiquidSurface(context, camera)) return;
            Graphics.DrawProcedural(drawMaterial, new Bounds(Vector3.zero, Vector3.one * 100), MeshTopology.Triangles,
                6, particleCapacity, outputCamera, properties, ShadowCastingMode.Off, false, world.itemLayer);
        }
        private void OnDestroy() => Dispose();
        private void OnDisable() => Dispose();
        private void OnEnable() { if (world != null) Initialize(world); }
        public void Dispose()
        {
            generation++; IsOperational = false; readbackInFlight = false;
            RenderPipelineManager.beginCameraRendering -= QueueDraw;
            DisposeSurfaceRendering();
            DisposeStreamBuffers();
            DisposeReferenceBuffers();
            DisposeLedgerBuffers();
            boundaryGroupBuffer?.Dispose(); boundaryGroupBuffer = null;
            GraphicsBuffer[] buffers = { particleBuffer, compositionA, compositionB, particleColorBuffer, positionDeltaBuffer,
                lambdaBuffer, gridHeadBuffer, gridNextBuffer, freeIndexBuffer, freeCountBuffer, spawnCommandBuffer,
                ingredientVisualBuffer, boundaryBuffer, triggerBuffer, agitatorBuffer, statisticsBuffer, velocitySnapshotBuffer };
            foreach (GraphicsBuffer buffer in buffers) buffer?.Dispose();
            particleBuffer = compositionA = compositionB = particleColorBuffer = positionDeltaBuffer = null;
            lambdaBuffer = gridHeadBuffer = gridNextBuffer = freeIndexBuffer = freeCountBuffer = spawnCommandBuffer = null;
            ingredientVisualBuffer = boundaryBuffer = triggerBuffer = agitatorBuffer = statisticsBuffer = null;
            velocitySnapshotBuffer = null;
            if (simulationShader != null) Destroy(simulationShader);
            if (drawMaterial != null) Destroy(drawMaterial);
            ingredients.Clear(); ingredientIndices.Clear();
        }
    }
}
