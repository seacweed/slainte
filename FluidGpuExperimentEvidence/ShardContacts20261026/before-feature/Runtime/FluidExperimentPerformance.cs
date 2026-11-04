using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace Slainte.Bartending.FluidGpuExperiment
{
    /// <summary>
    /// Bounded, opt-in standalone-player benchmark. Normal fixed ticks drive physics; one explicit
    /// offscreen Camera.Render per player frame drives a controlled rendering workload. Sampling
    /// never performs synchronous GPU readback. It is inert without -fluid-benchmark.
    /// </summary>
    public sealed class FluidExperimentPerformance : MonoBehaviour
    {
        internal static readonly CustomSampler PhysicsSampler = CustomSampler.Create("FluidExperiment.Physics.Step");
        public static bool IsRecording { get; private set; }
        private static readonly Dictionary<int, double> PhysicsMilliseconds = new Dictionary<int, double>();
        private static readonly List<double> PhysicsStepMilliseconds = new List<double>(4096);
        private static bool collectPhysicsSteps;
        internal static void RecordPhysics(long stopwatchTicks)
        {
            int frame = Time.frameCount;
            PhysicsMilliseconds.TryGetValue(frame, out double previous);
            double milliseconds = stopwatchTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            PhysicsMilliseconds[frame] = previous + milliseconds;
            if (collectPhysicsSteps && PhysicsStepMilliseconds.Count < 50000) PhysicsStepMilliseconds.Add(milliseconds);
        }

        [Serializable] public sealed class Percentiles
        {
            public string status;
            public int samples;
            // -1 means missing. Zero is never substituted for unsupported telemetry.
            public double meanMs = -1, p50Ms = -1, p95Ms = -1, p99Ms = -1;
        }
        [Serializable] public sealed class CaseResult
        {
            public string mode, status;
            public int requestedParticles, initialParticles, finalParticles, ingredientCount, capacity, vessels = 2;
            public int width, height, surfaceWidth, surfaceHeight, warmupFrames, measuredFrames;
            public int measuredPhysicsSteps, verifiedSurfaceFrames;
            public double measurementSeconds;
            public string screenshot, workload;
            public Vector2 cameraCenter, vesselBoundsMin, vesselBoundsMax;
            public float cameraOrthographicSize, framingViewportMargin;
            public double requestedVolumeMl = 500, volumeMl, vesselCapacityMl, requestedFillFraction, fillFraction;
            public int gridCellMaxOccupancy;
            public float gridCellSize, gridOccupancySnapshotTime;
            public string gridOccupancyStatus;
            public Percentiles frameInterval, cpuFrame, gpuFrame, cpuPhysicsSubmission, cpuPhysicsStep, cpuManualCameraRender, gpuSurface, computeGpuTiming;
        }
        [Serializable] public sealed class Report
        {
            public string status, unityVersion, operatingSystem, cpu, gpu, graphicsApi, evidence;
            public bool editor, developmentBuild, frameTimingEnabled;
            public int width, height, vSyncCount, targetFrameRate;
            public float fixedDeltaTime;
            public List<CaseResult> cases = new List<CaseResult>();
        }
        private struct Sample
        {
            public int pollFrame, physicsSourceFrame, gpuMarkerSourceFrame, surfaceSourceFrame;
            public long surfaceRenderCount;
            public ulong timingTimestamp;
            public double interval, cpuFrame, gpuFrame, physics, manualCameraRender, gpuSurface;
        }

        private FluidExperimentComparison comparison;
        private FluidExperimentGpuLiquid gpu;
        private FluidExperimentBody[] vessels;
        private ItemDef[] materials;
        private readonly FrameTiming[] timing = new FrameTiming[1];
        private readonly List<Sample> samples = new List<Sample>(1200);
        private readonly Dictionary<int, double> cameraRenderMilliseconds = new Dictionary<int, double>();
        private readonly StringBuilder csv = new StringBuilder("case,mode,requested_particles,ingredients,poll_frame,physics_source_frame,gpu_marker_source_frame,surface_source_frame,surface_render_count,frame_timing_timestamp,wall_interval_ms,cpu_frame_ms,gpu_frame_ms,cpu_physics_submission_ms,cpu_manual_camera_render_ms,gpu_surface_ms\n");
        private Report report;
        private Recorder surfaceRecorder;
        private RenderTexture cameraTarget;
        private bool renderWorkload;
        private int lastManualRenderFrame = -1;
        private string manualRenderError;
        private string output;
        private ulong lastTimingTimestamp;
        private int warmup = 60, measured = 240;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Activate()
        {
            if (Application.isEditor || !Environment.GetCommandLineArgs().Contains("-fluid-benchmark")) return;
            new GameObject("FluidExperimentPerformance").AddComponent<FluidExperimentPerformance>();
        }
        private IEnumerator Start()
        {
            IEnumerator routine = Run(); bool complete = false;
            while (!complete)
            {
                object next = null;
                try { complete = !routine.MoveNext(); if (!complete) next = routine.Current; }
                catch (Exception exception)
                {
                    IsRecording = false; renderWorkload = false;
                    if (report == null) report = new Report();
                    report.status = "FAILED: " + exception;
                    Export(); Debug.LogException(exception); Application.Quit(1); yield break;
                }
                if (!complete) yield return next;
            }
            IsRecording = false; renderWorkload = false;
            bool valid = report.cases.All(x => x.status == "COMPLETE");
            report.status = valid ? "COMPLETE" : "INVALID_CASES"; Export(); Application.Quit(valid ? 0 : 1);
        }
        private IEnumerator Run()
        {
            output = Argument("-fluid-output", Path.Combine(Application.persistentDataPath, "FluidBenchmark"));
            Directory.CreateDirectory(output);
            int width = IntArgument("-fluid-width", 1280, 320, 3840), height = IntArgument("-fluid-height", 720, 240, 2160);
            warmup = IntArgument("-fluid-warmup", 60, 16, 600); measured = IntArgument("-fluid-samples", 240, 30, 1200);
            float runDeadline = Time.realtimeSinceStartup + IntArgument("-fluid-max-seconds", 1200, 30, 3600);
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
            Application.runInBackground = true;
            yield return null; yield return null;
            comparison = FindFirstObjectByType<FluidExperimentComparison>();
            float deadline = Time.realtimeSinceStartup + 30;
            while (comparison != null && !comparison.Ready && Time.realtimeSinceStartup < deadline) yield return null;
            if (comparison == null || !comparison.Ready) throw new InvalidOperationException("Comparison scene did not initialize.");
            comparison.automaticScenario = false; comparison.enabled = false; comparison.showControls = false;
            comparison.World.enabled = false; comparison.World.interactor.enabled = false;
            comparison.World.seedLiquids = false;
            gpu = comparison.Gpu; gpu.automaticReadback = false;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
                throw new InvalidOperationException("Benchmark requires the prepared scene's Scriptable Render Pipeline asset.");
            cameraTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            { name = "Fluid Benchmark Camera Output", hideFlags = HideFlags.DontSave };
            if (!cameraTarget.Create()) throw new InvalidOperationException("Offscreen camera target allocation failed.");
            gpu.outputCamera.targetTexture = cameraTarget;
            // A hidden Windows player can skip automatic camera rendering even with a target.
            // Keep automatic camera rendering off and exercise it once in LateUpdate instead.
            gpu.outputCamera.enabled = false;
            gpu.outputCamera.aspect = (float)width / height;
            var source = comparison.World.Items.First(x => x.kind == LabItemKind.Glass && x.name.Contains("highball"));
            vessels = new[] { source, (FluidExperimentBody)null };
            ItemDef sourceIngredient = comparison.World.Items.First(x => x.ingredient != null).ingredient;
            materials = new ItemDef[16];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = Instantiate(sourceIngredient); materials[i].name = "BenchmarkIngredient" + i;
                materials[i].liquidColor = Color.HSVToRGB(i / 16f, .5f, .85f);
            }
            foreach (var body in comparison.World.Items.ToArray())
                if (!vessels.Contains(body)) body.gameObject.SetActive(false);
            report = new Report { status = "RUNNING", unityVersion = Application.unityVersion, editor = Application.isEditor,
                developmentBuild = Debug.isDebugBuild, operatingSystem = SystemInfo.operatingSystem,
                cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                width = cameraTarget.width, height = cameraTarget.height, vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate, fixedDeltaTime = Time.fixedDeltaTime,
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(),
                evidence = "Controlled standalone-player offscreen workload: one explicit Camera.Render in LateUpdate per regular player frame, with a dedicated RenderTexture and normal fixed ticks. This is not automatic camera rendering, gameplay FPS, or proof of a 60 FPS budget. Target frame rate 60 is only sampling cadence. Every case measures at least two seconds and 50 physics steps, and verifies surface submission on every measured frame. CPU Camera.Render wall time is recorded separately from CPU physics submission and includes any render-call waits; neither is GPU solver time. No GPU readback inside measurement. FrameTiming samples have their own timestamps; GPU surface marker is delayed three frames. Unsupported metrics use status and -1 percentiles, CSV blanks. Case PNG readback occurs after timing." };

            string[] modes = Argument("-fluid-modes", "CReferenceSurface,DImprovedSurface,ECalibratedLiquid").Split(',');
            if (modes.Length > 5) throw new ArgumentException("At most five benchmark modes are allowed.");
            int caseNumber = 0;
            foreach (string modeName in modes)
            foreach (int particleCount in new[] { 100, 300, 600, 1000 })
            foreach (int ingredientCount in new[] { 1, 4, 8, 16 })
            {
                if (!Enum.TryParse(modeName, out FluidExperimentMode mode)) throw new ArgumentException("Unknown mode: " + modeName);
                SetupCase(mode, particleCount, ingredientCount);
                IsRecording = true;
                for (int frame = 0; frame < warmup; frame++)
                {
                    if (Time.realtimeSinceStartup > runDeadline) throw new TimeoutException("Benchmark run time limit exceeded.");
                    FrameTimingManager.CaptureFrameTimings();
                    if (frame == 4) EnableSurfaceRecorder();
                    DrainPhysics(Time.frameCount - 3);
                    yield return null;
                }
                if (!gpu.SurfaceRenderingReady || gpu.SurfaceTextureSize.x <= 0 || gpu.SurfaceTextureSize.y <= 0
                    || gpu.RenderedSurfaceFrames == 0)
                    throw new InvalidOperationException("Benchmark surface did not render during warmup. " + RenderingDiagnostic());
                Bounds framedBounds = ValidateCameraFraming();
                samples.Clear(); lastTimingTimestamp = 0; PhysicsStepMilliseconds.Clear(); collectPhysicsSteps = true;
                float measurementStart = Time.realtimeSinceStartup;
                int verifiedSurfaceFrames = 0;
                for (int frame = 0; frame < measured || Time.realtimeSinceStartup - measurementStart < 2
                    || PhysicsStepMilliseconds.Count < 50; frame++)
                {
                    if (Time.realtimeSinceStartup > runDeadline) throw new TimeoutException("Benchmark run time limit exceeded.");
                    if (frame >= 1200) throw new InvalidOperationException("Benchmark could not collect the required duration and 50 physics steps within 1200 frames.");
                    long renderedBefore = gpu.RenderedSurfaceFrames;
                    int expectedRenderFrame = Time.frameCount;
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                    if (gpu.RenderedSurfaceFrames <= renderedBefore || gpu.LastRenderedSurfaceFrame != expectedRenderFrame
                        || gpu.SurfaceTextureSize.x <= 0 || gpu.SurfaceTextureSize.y <= 0 || !cameraTarget.IsCreated())
                        throw new InvalidOperationException("Benchmark surface failed to render measured frame " + expectedRenderFrame
                            + "; last submitted frame=" + gpu.LastRenderedSurfaceFrame + ". " + RenderingDiagnostic());
                    verifiedSurfaceFrames++;
                    CaptureSample();
                }
                double measurementSeconds = Time.realtimeSinceStartup - measurementStart;
                IsRecording = false; collectPhysicsSteps = false; renderWorkload = false; comparison.World.enabled = false;
                gpu.ReadbackNow(); // after the measured interval only
                double vesselCapacity = vessels.Sum(v => (double)v.capacityMl);
                int maximumOccupancy = gpu.MeasureSnapshotGridOccupancy(out float cellSize);
                var result = new CaseResult { mode = mode.ToString(), requestedParticles = particleCount,
                    initialParticles = particleCount, finalParticles = gpu.ActiveCount, ingredientCount = ingredientCount,
                    capacity = gpu.settings.gpuLiquidParticleCapacity, width = cameraTarget.width, height = cameraTarget.height,
                    surfaceWidth = gpu.SurfaceTextureSize.x, surfaceHeight = gpu.SurfaceTextureSize.y,
                    warmupFrames = warmup, measuredFrames = samples.Count, volumeMl = gpu.Ledger.Total.ActiveMl,
                    measuredPhysicsSteps = PhysicsStepMilliseconds.Count, verifiedSurfaceFrames = verifiedSurfaceFrames,
                    measurementSeconds = measurementSeconds, workload = "Controlled offscreen Camera.Render once per player frame plus normal fixed ticks; cadence capped at 60 Hz, not gameplay FPS",
                    cameraCenter = gpu.outputCamera.transform.position, cameraOrthographicSize = gpu.outputCamera.orthographicSize,
                    vesselBoundsMin = framedBounds.min, vesselBoundsMax = framedBounds.max, framingViewportMargin = .05f,
                    vesselCapacityMl = vesselCapacity, requestedFillFraction = 500 / vesselCapacity,
                    fillFraction = gpu.Ledger.Total.ActiveMl / vesselCapacity,
                    gridCellMaxOccupancy = maximumOccupancy, gridCellSize = cellSize,
                    gridOccupancySnapshotTime = gpu.Ledger.SimulationTime,
                    gridOccupancyStatus = "CPU reconstruction after timed region: active nonsuspended snapshot particles, solver floor cell coordinates and bounds; no GPU grid readback",
                    status = gpu.ActiveCount == particleCount ? "COMPLETE" : "INVALID_PARTICLE_COUNT_CHANGED",
                    frameInterval = Summarize(samples.Select(x => x.interval), "wall frame interval"),
                    cpuFrame = Summarize(samples.Select(x => x.cpuFrame), "FrameTiming CPU"),
                    gpuFrame = Summarize(samples.Select(x => x.gpuFrame), "FrameTiming GPU"),
                    cpuPhysicsSubmission = Summarize(samples.Select(x => x.physics), "CustomSampler scope stopwatch CPU submission"),
                    cpuPhysicsStep = Summarize(PhysicsStepMilliseconds, "per fixed-step CPU submission stopwatch"),
                    cpuManualCameraRender = Summarize(samples.Select(x => x.manualCameraRender), "explicit Camera.Render CPU wall time, including any render-call waits"),
                    gpuSurface = Summarize(samples.Select(x => x.gpuSurface), "CommandBuffer GPU surface marker"),
                    computeGpuTiming = new Percentiles { status = "UNSUPPORTED: separate compute-dispatch GPU timestamps are not recorded; total GPU frame and CPU submission timings are not compute GPU timings" } };
                result.screenshot = SaveCameraOutput(++caseNumber, result);
                report.cases.Add(result); WriteSamples(caseNumber, result); Export();
                if (surfaceRecorder != null) surfaceRecorder.enabled = false;
                PhysicsMilliseconds.Clear(); cameraRenderMilliseconds.Clear(); yield return null;
            }
        }
        private void SetupCase(FluidExperimentMode mode, int particleCount, int ingredientCount)
        {
            comparison.World.enabled = false;
            // Session resets destroy non-authored bodies. Recreate this owned benchmark clone only
            // after every reset, and explicitly unregister the preceding case's clone first.
            if (vessels[1] != null)
            {
                vessels[1].gameObject.SetActive(false); Destroy(vessels[1].gameObject); vessels[1] = null;
            }
            gpu.settings.gpuLiquidParticleCapacity = 2048;
            gpu.settings.gpuLiquidMaximumIngredients = 16;
            gpu.settings.gpuLiquidParticleVolumeMl = 500f / particleCount;
            comparison.SwitchMode(mode); comparison.ResetComparison();
            vessels[1] = Instantiate(vessels[0].gameObject, vessels[0].transform.parent).GetComponent<FluidExperimentBody>();
            vessels[1].name = "BenchmarkHighballB"; vessels[1].Attach(comparison.World);
            comparison.World.interactor.enabled = false; gpu.automaticReadback = false;
            gpu.ResetSimulation();
            for (int v = 0; v < vessels.Length; v++)
            {
                var vessel = vessels[v]; vessel.SetHeld(true); vessel.Teleport(new Vector2(v == 0 ? -1.2f : 1.2f, -.4f), 0);
                vessel.SetSealed(true); vessel.pourMlPerSecond = 0;
                var positions = SeedPositions(vessel, particleCount / 2);
                for (int i = 0; i < particleCount / 2; i++)
                    if (!gpu.TryEmit(positions[i], Vector2.zero, materials[(i + v * (particleCount / 2)) % ingredientCount],
                        500f / particleCount, vessel.Id)) throw new InvalidOperationException("Benchmark seed rejected.");
            }
            Physics2D.SyncTransforms();
            gpu.Step(Time.fixedDeltaTime); gpu.ReadbackNow();
            if (gpu.ActiveCount != particleCount) throw new InvalidOperationException("Benchmark could not seed exact particle count.");
            Camera camera = gpu.outputCamera;
            camera.targetTexture = cameraTarget; camera.enabled = false; camera.aspect = (float)cameraTarget.width / cameraTarget.height;
            Bounds bounds = BenchmarkVesselBounds();
            camera.orthographic = true;
            camera.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.center.y, -20), Quaternion.identity);
            camera.orthographicSize = Mathf.Max(.1f, bounds.extents.y, bounds.extents.x / camera.aspect) * 1.2f;
            ValidateCameraFraming();
            comparison.World.enabled = true; PhysicsMilliseconds.Clear(); cameraRenderMilliseconds.Clear();
            manualRenderError = null; renderWorkload = true;
        }
        private Bounds BenchmarkVesselBounds()
        {
            Bounds bounds = new Bounds(vessels[0].Position, Vector3.zero);
            foreach (FluidExperimentBody vessel in vessels)
            {
                // Explicit profile transforms remain accurate even before Box2D refreshes a collider's bounds.
                bounds.Encapsulate(vessel.SolidBounds);
                if (vessel.collisionProfile != null)
                {
                    foreach (Vector2 point in vessel.collisionProfile.interior) bounds.Encapsulate(vessel.LocalToWorld(point));
                    foreach (FluidExperimentHull hull in vessel.collisionProfile.solids)
                        foreach (Vector2 point in hull.points) bounds.Encapsulate(vessel.LocalToWorld(point));
                    foreach (Vector2 point in vessel.collisionProfile.lid) bounds.Encapsulate(vessel.LocalToWorld(point));
                }
                else foreach (Rect region in vessel.contentRegions)
                {
                    bounds.Encapsulate(vessel.LocalToWorld(region.min));
                    bounds.Encapsulate(vessel.LocalToWorld(region.max));
                    bounds.Encapsulate(vessel.LocalToWorld(new Vector2(region.xMin, region.yMax)));
                    bounds.Encapsulate(vessel.LocalToWorld(new Vector2(region.xMax, region.yMin)));
                }
            }
            if (!float.IsFinite(bounds.size.x) || !float.IsFinite(bounds.size.y) || bounds.size.x <= 0 || bounds.size.y <= 0)
                throw new InvalidOperationException("Benchmark vessels have invalid world bounds.");
            return bounds;
        }
        private Bounds ValidateCameraFraming()
        {
            Bounds bounds = BenchmarkVesselBounds();
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            {
                Vector3 point = gpu.outputCamera.WorldToViewportPoint(new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                    y == 0 ? bounds.min.y : bounds.max.y, 0));
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || point.z <= 0
                    || point.x < .05f || point.x > .95f || point.y < .05f || point.y > .95f)
                    throw new InvalidOperationException("Benchmark camera crops vessel geometry or lacks 5% viewport margin: " + point);
            }
            return bounds;
        }
        private void LateUpdate()
        {
            if (!renderWorkload || gpu == null || cameraTarget == null || lastManualRenderFrame == Time.frameCount) return;
            lastManualRenderFrame = Time.frameCount;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { gpu.outputCamera.Render(); }
            catch (Exception exception) { manualRenderError = exception.ToString(); renderWorkload = false; }
            finally
            {
                cameraRenderMilliseconds[Time.frameCount] = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                foreach (int frame in cameraRenderMilliseconds.Keys.Where(x => x < Time.frameCount - 4).ToArray())
                    cameraRenderMilliseconds.Remove(frame);
            }
        }
        private List<Vector2> SeedPositions(FluidExperimentBody vessel, int count)
        {
            Rect bounds = vessel.collisionProfile.InteriorBounds;
            var positions = new List<Vector2>(count);
            for (int resolution = 12; resolution <= 256; resolution += 4)
            {
                positions.Clear();
                float dx = bounds.width / resolution, dy = bounds.height * .8f / resolution;
                for (int y = 0; y < resolution && positions.Count < count; y++)
                for (int x = 0; x < resolution && positions.Count < count; x++)
                {
                    Vector2 local = new Vector2(bounds.xMin + (x + .5f) * dx, bounds.yMin + (y + .5f) * dy);
                    if (vessel.ContainsLiquidDisk(local, gpu.Radius)) positions.Add(vessel.LocalToWorld(local));
                }
                if (positions.Count >= count) return positions;
            }
            throw new InvalidOperationException("Benchmark vessel interior cannot supply seed positions.");
        }
        private void EnableSurfaceRecorder()
        {
            string marker = gpu.useImprovedSurface ? "FluidExperiment Improved Surface" :
                gpu.useReferenceSurface ? "FluidExperiment Particle Density Surface" : "FluidExperiment Stream Surface";
            surfaceRecorder = Recorder.Get(marker);
            if (surfaceRecorder != null && surfaceRecorder.isValid) surfaceRecorder.enabled = true;
        }
        private string RenderingDiagnostic()
        {
            string Program(Shader shader) => shader == null ? "missing" : shader.name + " supported=" + shader.isSupported;
            Shader accumulation = gpu.useImprovedSurface ? gpu.improvedAccumulationShader :
                gpu.useReferenceSurface ? gpu.referenceAccumulationShader : gpu.settings.surfaceAccumulationShader;
            Shader composite = gpu.useImprovedSurface ? gpu.improvedCompositeShader :
                gpu.useReferenceSurface ? gpu.referenceCompositeShader : gpu.settings.surfaceCompositeShader;
            return "surfaceError=" + (gpu.SurfaceRenderingError ?? "none") + "; operational=" + gpu.IsOperational
                + "; renderParticles=" + gpu.renderParticles + "; surfaceEnabled=" + gpu.useSurfaceRendering
                + "; cameraEnabled=" + gpu.outputCamera.isActiveAndEnabled + "; cameraTargetCreated=" + cameraTarget.IsCreated()
                + "; explicitRenderError=" + (manualRenderError ?? "none") + "; lastExplicitRenderFrame=" + lastManualRenderFrame
                + "; pipeline=" + UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                + "; draw=" + Program(gpu.drawShader) + "; accumulation=" + Program(accumulation)
                + "; composite=" + Program(composite) + "; display=" + Program(gpu.settings.surfaceDisplayShader);
        }
        private void CaptureSample()
        {
            int previousFrame = Time.frameCount - 1;
            var sample = new Sample { pollFrame = Time.frameCount, physicsSourceFrame = previousFrame,
                gpuMarkerSourceFrame = Time.frameCount - 3, interval = Time.unscaledDeltaTime * 1000.0,
                surfaceSourceFrame = gpu.LastRenderedSurfaceFrame, surfaceRenderCount = gpu.RenderedSurfaceFrames,
                cpuFrame = -1, gpuFrame = -1, physics = -1, manualCameraRender = -1, gpuSurface = -1 };
            // A frame with no fixed step has zero physics work. This is measured scheduling, not an
            // unsupported metric. It is kept separately from telemetry channels that may be missing.
            sample.physics = PhysicsMilliseconds.TryGetValue(previousFrame, out double physics) ? physics : 0;
            if (cameraRenderMilliseconds.TryGetValue(previousFrame, out double render)) sample.manualCameraRender = render;
            uint captured = FrameTimingManager.GetLatestTimings(1, timing);
            if (captured > 0 && timing[0].frameStartTimestamp != lastTimingTimestamp)
            {
                var t = timing[0]; lastTimingTimestamp = t.frameStartTimestamp; sample.timingTimestamp = lastTimingTimestamp;
                if (t.cpuFrameTime > 0 && double.IsFinite(t.cpuFrameTime)) sample.cpuFrame = t.cpuFrameTime;
                if (t.gpuFrameTime > 0 && double.IsFinite(t.gpuFrameTime)) sample.gpuFrame = t.gpuFrameTime;
            }
            if (surfaceRecorder != null && surfaceRecorder.isValid && surfaceRecorder.gpuSampleBlockCount > 0
                && surfaceRecorder.gpuElapsedNanoseconds > 0) sample.gpuSurface = surfaceRecorder.gpuElapsedNanoseconds / 1000000.0;
            samples.Add(sample); DrainPhysics(previousFrame - 2);
        }
        private static void DrainPhysics(int beforeFrame)
        {
            // Only a handful of frames are retained; no recording state grows with run duration.
            foreach (int frame in PhysicsMilliseconds.Keys.Where(x => x < beforeFrame).ToArray()) PhysicsMilliseconds.Remove(frame);
        }
        private static Percentiles Summarize(IEnumerable<double> values, string source)
        {
            double[] sorted = values.Where(x => x >= 0 && double.IsFinite(x)).OrderBy(x => x).ToArray();
            if (sorted.Length == 0) return new Percentiles { status = "UNSUPPORTED_OR_NO_VALID_SAMPLES: " + source };
            double At(double quantile) => sorted[Mathf.Clamp((int)Math.Ceiling(quantile * sorted.Length) - 1, 0, sorted.Length - 1)];
            return new Percentiles { status = "MEASURED: " + source, samples = sorted.Length,
                meanMs = sorted.Average(), p50Ms = At(.5), p95Ms = At(.95), p99Ms = At(.99) };
        }
        private void WriteSamples(int caseNumber, CaseResult result)
        {
            foreach (Sample s in samples)
                csv.Append(caseNumber).Append(',').Append(result.mode).Append(',').Append(result.requestedParticles).Append(',')
                    .Append(result.ingredientCount).Append(',').Append(s.pollFrame).Append(',').Append(s.physicsSourceFrame).Append(',')
                    .Append(s.gpuMarkerSourceFrame).Append(',').Append(s.surfaceSourceFrame).Append(',').Append(s.surfaceRenderCount).Append(',')
                    .Append(s.timingTimestamp).Append(',').Append(Cell(s.interval)).Append(',')
                    .Append(Cell(s.cpuFrame)).Append(',').Append(Cell(s.gpuFrame)).Append(',').Append(Cell(s.physics)).Append(',')
                    .Append(Cell(s.manualCameraRender)).Append(',').Append(Cell(s.gpuSurface)).Append('\n');
        }
        private string SaveCameraOutput(int caseNumber, CaseResult result)
        {
            string filename = caseNumber.ToString("D2") + "-" + result.mode + "-" + result.requestedParticles
                + "p-" + result.ingredientCount + "i.png";
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(cameraTarget.width, cameraTarget.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = cameraTarget;
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(output, filename), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Destroy(image); }
            return filename;
        }
        private static string Cell(double value) => value >= 0 && double.IsFinite(value) ? value.ToString("F6", CultureInfo.InvariantCulture) : "";
        private void Export()
        {
            if (string.IsNullOrEmpty(output)) output = Path.Combine(Application.persistentDataPath, "FluidBenchmark");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "player-performance.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(output, "player-performance.csv"), csv.ToString());
        }
        private static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
        private static int IntArgument(string name, int fallback, int minimum, int maximum)
            => int.TryParse(Argument(name, fallback.ToString()), out int value) ? Mathf.Clamp(value, minimum, maximum) : fallback;
        private void OnDestroy()
        {
            IsRecording = false; collectPhysicsSteps = false; renderWorkload = false;
            PhysicsMilliseconds.Clear(); PhysicsStepMilliseconds.Clear(); cameraRenderMilliseconds.Clear();
            if (surfaceRecorder != null) surfaceRecorder.enabled = false;
            if (cameraTarget != null)
            {
                if (gpu != null && gpu.outputCamera != null && gpu.outputCamera.targetTexture == cameraTarget)
                    gpu.outputCamera.targetTexture = null;
                cameraTarget.Release(); Destroy(cameraTarget);
            }
            if (materials != null) foreach (ItemDef material in materials) if (material != null) Destroy(material);
        }
    }

    public sealed partial class FluidExperimentGpuLiquid
    {
        // This diagnostic runs only after the measured interval. Read the actual allocated grid
        // dimensions and solver cell size rather than duplicating per-mode settings in the recorder.
        internal int MeasureSnapshotGridOccupancy(out float cellSize)
        {
            cellSize = SolverSmoothingRadius;
            var occupancy = new Dictionary<int, int>();
            int maximum = 0;
            foreach (GpuLiquidParticle particle in snapshotParticles)
            {
                if (particle.Active == 0 || (particle.StateFlags & 4u) != 0u) continue;
                int x = Mathf.FloorToInt((particle.Position.x - settings.gpuLiquidWorldMin.x) / cellSize);
                int y = Mathf.FloorToInt((particle.Position.y - settings.gpuLiquidWorldMin.y) / cellSize);
                if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight) continue;
                int cell = x + y * gridWidth;
                occupancy.TryGetValue(cell, out int previous);
                occupancy[cell] = previous + 1; maximum = Mathf.Max(maximum, previous + 1);
            }
            return maximum;
        }
    }
}
