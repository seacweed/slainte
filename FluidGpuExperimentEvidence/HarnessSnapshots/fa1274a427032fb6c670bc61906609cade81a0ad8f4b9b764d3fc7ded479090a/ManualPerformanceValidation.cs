using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

// Evidence-only: preserve the complete authored Manual world and drive the existing interaction API.
public static class ExperimentManualPerformanceValidation
{
    private const string Key = "Slainte.FluidGpuExperiment.ManualPerformanceValidation";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR")
        ?? "FluidGpuExperimentEvidence/manual-performance-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }
    public static void Begin()
    {
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Path.Combine(Evidence, "manual-performance-result.txt"), "STARTED\n");
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    private static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("ManualPerformanceValidationRunner").AddComponent<ManualPerformanceValidationRunner>().Begin();
    }
    public static void Finish(bool success)
    {
        File.WriteAllText(Path.Combine(Evidence, "manual-performance-result.txt"), success ? "PASS\n" : "FAIL\n");
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ManualPerformanceValidationRunner : MonoBehaviour
{
    [Serializable] private sealed class Distribution
    {
        public int count;
        public double mean, p50, p95, p99;
    }
    [Serializable] private sealed class Case
    {
        public string name;
        public int itemCount, bottleCount, boundarySegments, capacity, substeps, surfaceWidth, surfaceHeight;
        public int normalFrames, normalFixedSteps, syncSamples, renderedFrames;
        public bool automaticReadback, controlsEnabled, effectsEnabled;
        public float measurementSeconds, generatedMl, sourceDebitMl, activeMl, ownedMl, retiredMl, maximumSpeed;
        public Distribution frameIntervalMs, gpuSubmissionCpuStepMs, manualCameraCpuMs, mainThreadAllocatedBytes;
        public Distribution completedLiquidPhysicsAndRenderMs;
        public bool functionalChecksPassed;
        public bool inventoryPreserved;
    }
    [Serializable] private sealed class InventoryItem
    {
        public uint id;
        public string name, kind;
    }
    [Serializable] private sealed class KindCount
    {
        public string kind;
        public int count;
    }
    [Serializable] private sealed class Report
    {
        public string status, error, unityVersion, gpu, platform;
        public string protocol = "Full authored Manual scene, E mode, original particle capacity/substeps, all bodies and GUI/effects retained. Public Pick/MoveHeld/BeginRotation/RotateBy drive held motion; hardware mouse polling disabled. Normal PlayerLoop:1s warmup+3s measurement with exactly one explicit1280x720 Camera.Render per rendered frame. No synchronous readback in normal timing. Separate20-sample diagnostic explicitly simulates one .02s CPU/liquid tick, renders, and reads one RT pixel to await completion. This diagnostic is CPU+GPU completed-work wall time, not GPU timestamps or gameplay FPS. Normal statistics exclude diagnostics. GC statistics include the fixed harness overhead; no CSV/JSON allocation occurs in normal samples.";
        public List<Case> cases = new List<Case>();
        public List<InventoryItem> authoredInventory = new List<InventoryItem>();
        public List<KindCount> authoredKindCounts = new List<KindCount>();
    }
    private struct Sample
    {
        public int frame;
        public double interval, render, allocated;
    }
    private FluidExperimentComparison comparison;
    private FluidExperimentWorld world;
    private FluidExperimentGpuLiquid gpu;
    private FluidExperimentInteractor hand;
    private FluidExperimentBody controlled, glass, bottle;
    private FluidExperimentEffects effects;
    private RenderTexture target, previousTarget;
    private Camera captureCamera;
    private Texture2D completionPixel;
    private bool previousCameraEnabled, finished, renderWorkload, measure, previousAutomaticReadback;
    private SimulationMode2D previousSimulationMode;
    private PropertyInfo recordingProperty;
    private FieldInfo collectStepsField;
    private Dictionary<int, double> physicsFrames;
    private List<double> physicsSteps;
    private Action<float> sampleHand;
    private string currentCase;
    private float started, phaseStarted, commandAngle;
    private Vector2 movementOrigin;
    private double lastFrameTime;
    private long lastAllocated, renderedAtStart;
    private readonly List<Sample> samples = new List<Sample>(4096);
    private readonly List<double> sync = new List<double>(20);
    private readonly List<string> errors = new List<string>();
    private readonly Report report = new Report();
    private readonly StringBuilder csv = new StringBuilder("case,kind,frame,wall_frame_ms,camera_cpu_ms,allocated_bytes,completed_tick_render_ms\n");

    public void Begin()
    {
        started = Time.realtimeSinceStartup; Application.logMessageReceived += OnLog;
        StartCoroutine(Guard());
    }
    private void OnLog(string message, string stack, LogType type)
    {
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) errors.Add(message + "\n" + stack);
    }
    private IEnumerator Guard()
    {
        var stack = new Stack<IEnumerator>(); stack.Push(Run());
        while (stack.Count > 0)
        {
            bool more; object next = null;
            try
            {
                if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
                if (Time.realtimeSinceStartup - started > 205) throw new Exception("Manual performance probe exceeded205seconds.");
                more = stack.Peek().MoveNext(); if (more) next = stack.Peek().Current;
            }
            catch (Exception error)
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                Finish(false, error.ToString()); yield break;
            }
            if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (next is IEnumerator nested) { stack.Push(nested); continue; }
            yield return next;
        }
        Finish(true, null);
    }
    private IEnumerator Run()
    {
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        Check(comparison != null, "Authored comparison scene missing.");
        while (!comparison.Ready) yield return null;
        comparison.automaticScenario = false; comparison.showControls = true;
        comparison.SwitchMode(FluidExperimentMode.ECalibratedLiquid);
        world = comparison.World; gpu = comparison.Gpu; hand = world.interactor;
        comparison.StartScenario(FluidExperimentScenario.Manual);
        report.authoredInventory = CaptureInventory();
        report.authoredKindCounts = report.authoredInventory.GroupBy(item => item.kind)
            .Select(group => new KindCount { kind = group.Key, count = group.Count() }).ToList();
        effects = FindFirstObjectByType<FluidExperimentEffects>();
        previousAutomaticReadback = gpu.automaticReadback; gpu.automaticReadback = true;
        captureCamera = gpu.outputCamera; previousTarget = captureCamera.targetTexture; previousCameraEnabled = captureCamera.enabled;
        target = new RenderTexture(1280, 720, 24) { name = "Full Manual evidence render" }; target.Create();
        completionPixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        captureCamera.targetTexture = target; captureCamera.enabled = false; captureCamera.aspect = 1280f / 720;
        Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0; Application.runInBackground = true;
        previousSimulationMode = Physics2D.simulationMode;
        const BindingFlags fields = BindingFlags.Static | BindingFlags.NonPublic;
        recordingProperty = typeof(FluidExperimentPerformance).GetProperty("IsRecording", BindingFlags.Static | BindingFlags.Public);
        collectStepsField = typeof(FluidExperimentPerformance).GetField("collectPhysicsSteps", fields);
        physicsFrames = (Dictionary<int, double>)typeof(FluidExperimentPerformance).GetField("PhysicsMilliseconds", fields).GetValue(null);
        physicsSteps = (List<double>)typeof(FluidExperimentPerformance).GetField("PhysicsStepMilliseconds", fields).GetValue(null);
        sampleHand = (Action<float>)typeof(FluidExperimentInteractor).GetMethod("Sample", BindingFlags.Instance | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<float>), hand);
        report.unityVersion = Application.unityVersion; report.gpu = SystemInfo.graphicsDeviceName;
        report.platform = "Unity Editor full authored Manual PlayerLoop, " + SystemInfo.graphicsDeviceType;
        foreach (string name in new[] { "idle", "glass-drag", "glass-rotate", "bottle-pour" }) yield return RunCase(name);
    }
    private IEnumerator RunCase(string name)
    {
        currentCase = name; measure = renderWorkload = false;
        SetRecording(false); world.enabled = false;
        comparison.StartScenario(FluidExperimentScenario.Manual);
        hand.enabled = false;
        glass = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
        bottle = world.Items.First(b => b.kind == LabItemKind.Bottle && b.name.Contains("1002"));
        CheckInventory(name + " initial reset");
        controlled = null; commandAngle = 0;
        if (name == "bottle-pour")
        {
            Vector2 desired = glass.Position + Vector2.up * 3.5f;
            bottle.Teleport(desired - 2 * bottle.PointAt(bottle.rotationPivotLocal, Vector2.zero, 0), 0);
            controlled = bottle;
        }
        else if (name != "idle") controlled = glass;
        if (controlled != null)
        {
            Check(hand.Pick(controlled, controlled.Position), "Controlled body pickup failed.");
            movementOrigin = controlled.Position + Vector2.up * .6f;
            if (name != "glass-drag") hand.BeginRotation();
        }
        phaseStarted = Time.unscaledTime; Drive(0);
        world.enabled = true; renderWorkload = true;
        SetRecording(false);
        float warmupEnd = Time.unscaledTime + 1;
        while (Time.unscaledTime < warmupEnd) yield return null;
        Check(gpu.SurfaceRenderingReady && gpu.SurfaceTextureSize.x == 1280 && gpu.SurfaceTextureSize.y == 720,
            "Normal frame did not render the expected1280x720 liquid surface.");
        samples.Clear(); sync.Clear(); physicsFrames.Clear(); physicsSteps.Clear();
        float stockAtStart = bottle.remainingMl, emittedAtStart = gpu.EmittedMl;
        renderedAtStart = gpu.RenderedSurfaceFrames;
        lastFrameTime = Time.realtimeSinceStartupAsDouble; lastAllocated = GC.GetAllocatedBytesForCurrentThread();
        measure = true; SetRecording(true);
        float measureStart = Time.unscaledTime;
        while (Time.unscaledTime - measureStart < 3 || physicsSteps.Count < 50) yield return null;
        measure = renderWorkload = false; SetRecording(false); world.enabled = false;
        var result = new Case { name = name, itemCount = world.Items.Count,
            bottleCount = world.Items.Count(b => b.kind == LabItemKind.Bottle),
            boundarySegments = world.Items.Sum(b => b.collisionProfile != null ? b.collisionProfile.BoundaryCount : b.liquidWall.Length),
            capacity = gpu.settings.gpuLiquidParticleCapacity, substeps = gpu.LastSubsteps,
            surfaceWidth = gpu.SurfaceTextureSize.x, surfaceHeight = gpu.SurfaceTextureSize.y,
            automaticReadback = gpu.automaticReadback, controlsEnabled = comparison.showControls,
            effectsEnabled = effects != null && effects.enabled, normalFrames = samples.Count,
            normalFixedSteps = physicsSteps.Count, renderedFrames = (int)(gpu.RenderedSurfaceFrames - renderedAtStart),
            measurementSeconds = Time.unscaledTime - measureStart,
            frameIntervalMs = Summarize(samples.Select(s => s.interval)),
            manualCameraCpuMs = Summarize(samples.Select(s => s.render)),
            mainThreadAllocatedBytes = Summarize(samples.Select(s => s.allocated)),
            gpuSubmissionCpuStepMs = Summarize(physicsSteps), generatedMl = gpu.EmittedMl - emittedAtStart,
            sourceDebitMl = stockAtStart - bottle.remainingMl };
        report.cases.Add(result);
        foreach (Sample sample in samples) csv.Append(name).Append(",normal,").Append(sample.frame).Append(',')
            .Append(F(sample.interval)).Append(',').Append(F(sample.render)).Append(',').Append(F(sample.allocated)).Append(",\n");
        gpu.ReadbackNow(); // Outside normal timing, validates logical work and queue state.
        result.activeMl = gpu.SnapshotTotalMl; result.ownedMl = gpu.VolumeIn(glass.Id);
        result.retiredMl = (float)gpu.Ledger.Total.RetiredMl;
        result.maximumSpeed = gpu.Snapshot.Where(p => p.Active != 0).Select(p => p.Velocity.magnitude).DefaultIfEmpty().Max();
        Check(samples.Count >= 10 && result.renderedFrames >= samples.Count && result.normalFixedSteps >= 50,
            name + ": insufficient normal PlayerLoop/render samples.");
        Check(gpu.Ledger != null && Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .03
            && Math.Abs(gpu.Ledger.Total.QueueErrorMl) < .03, name + ": ledger no longer conserves logical ml.");
        Check(gpu.Snapshot.Where(p => p.Active != 0).All(p => float.IsFinite(p.Position.x) && float.IsFinite(p.Position.y)
            && float.IsFinite(p.Velocity.x) && float.IsFinite(p.Velocity.y)), name + ": particle state became nonfinite.");
        if (name == "bottle-pour") Check(result.generatedMl > 5 && Math.Abs(result.generatedMl - result.sourceDebitMl) < .03,
            "Pour case did not exercise real accepted bottle emission/stock debit.");
        else Check(Math.Abs(result.generatedMl) < .001, name + ": fixture unexpectedly emitted extra bottle liquid.");
        if (controlled != null) Check(hand.Held == controlled && controlled.IsHeld, name + ": held interaction state was lost.");
        CheckInventory(name + " after normal measurement");

        // Drain older queued work first; each diagnostic then measures exactly one completed fresh tick+render.
        CompleteRender();
        Physics2D.simulationMode = SimulationMode2D.Script;
        float controlTime = Time.unscaledTime - phaseStarted;
        for (int i = 0; i < 20; i++)
        {
            yield return null;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            Drive(controlTime += .02f);
            world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms(); Physics2D.Simulate(.02f);
            world.TickLiquid(.02f);
            CompleteRender();
            double ms = Elapsed(start); sync.Add(ms);
            csv.Append(name).Append(",completed-tick-render,").Append(Time.frameCount).Append(",,,,").Append(F(ms)).Append('\n');
        }
        Physics2D.simulationMode = previousSimulationMode;
        result.completedLiquidPhysicsAndRenderMs = Summarize(sync); result.syncSamples = sync.Count;
        CheckInventory(name + " after synchronous diagnostics");
        result.inventoryPreserved = true;
        result.functionalChecksPassed = true;
        if (hand.Held != null) hand.ReleaseWithVelocity(Vector2.zero);
        Export();
    }
    private void Update()
    {
        if (renderWorkload) Drive(Time.unscaledTime - phaseStarted);
    }
    private void Drive(float t)
    {
        if (controlled == null) return;
        if (currentCase == "glass-drag")
            hand.MoveHeld(movementOrigin + new Vector2(.8f * Mathf.Sin(t * 5), .2f * Mathf.Sin(t * 7)));
        else
        {
            float desired = currentCase == "bottle-pour" ? 165 + 12 * Mathf.Sin(t * 7) : 75 * Mathf.Sin(t * 7);
            hand.RotateBy(desired - commandAngle); commandAngle = desired;
        }
        sampleHand(Time.unscaledTime);
    }
    private void LateUpdate()
    {
        if (!renderWorkload || captureCamera == null) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        captureCamera.Render();
        double renderMs = Elapsed(start), now = Time.realtimeSinceStartupAsDouble;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        if (measure) samples.Add(new Sample { frame = Time.frameCount, interval = (now - lastFrameTime) * 1000,
            render = renderMs, allocated = allocated - lastAllocated });
        lastFrameTime = now; lastAllocated = allocated;
        physicsFrames?.Clear(); // Only the per-fixed-step list is used; never grow the static frame dictionary.
    }
    private void CompleteRender()
    {
        captureCamera.Render(); RenderTexture old = RenderTexture.active;
        try { RenderTexture.active = target; completionPixel.ReadPixels(new Rect(640, 360, 1, 1), 0, 0, false); }
        finally { RenderTexture.active = old; }
    }
    private void SetRecording(bool value)
    {
        recordingProperty?.SetValue(null, value); collectStepsField?.SetValue(null, value);
    }
    private List<InventoryItem> CaptureInventory() => world.Items.OrderBy(item => item.Id)
        .Select(item => new InventoryItem { id = item.Id, name = item.name, kind = item.kind.ToString() }).ToList();
    private void CheckInventory(string phase)
    {
        List<InventoryItem> actual = CaptureInventory();
        Check(actual.Count == report.authoredInventory.Count, phase + ": authored object count changed.");
        for (int i = 0; i < actual.Count; i++)
        {
            InventoryItem expected = report.authoredInventory[i], found = actual[i];
            Check(found.id == expected.id && found.name == expected.name && found.kind == expected.kind,
                phase + ": authored object identity/name/kind changed at slot " + i + ".");
        }
        Check(world.Items.All(item => item.enabled && item.gameObject.activeInHierarchy && item.Body.simulated),
            phase + ": authored body was disabled or removed from physics.");
        foreach (KindCount expected in report.authoredKindCounts)
            Check(actual.Count(item => item.kind == expected.kind) == expected.count,
                phase + ": authored kind count changed for " + expected.kind + ".");
    }
    private static Distribution Summarize(IEnumerable<double> values)
    {
        double[] sorted = values.Where(v => double.IsFinite(v) && v >= 0).OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return new Distribution();
        double At(double q) => sorted[Mathf.Clamp((int)Math.Ceiling(q * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new Distribution { count = sorted.Length, mean = sorted.Average(), p50 = At(.5), p95 = At(.95), p99 = At(.99) };
    }
    private static double Elapsed(long timestamp) => (System.Diagnostics.Stopwatch.GetTimestamp() - timestamp)
        * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
    private void Export()
    {
        File.WriteAllText(Path.Combine(ExperimentManualPerformanceValidation.Evidence, "manual-performance.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(ExperimentManualPerformanceValidation.Evidence, "manual-performance.csv"), csv.ToString());
    }
    private void Finish(bool success, string error)
    {
        finished = true; measure = renderWorkload = false; SetRecording(false);
        Application.logMessageReceived -= OnLog;
        Physics2D.simulationMode = previousSimulationMode;
        if (gpu != null) gpu.automaticReadback = previousAutomaticReadback;
        if (captureCamera != null) { captureCamera.targetTexture = previousTarget; captureCamera.enabled = previousCameraEnabled; captureCamera.ResetAspect(); }
        if (target != null) { target.Release(); Destroy(target); }
        if (completionPixel != null) Destroy(completionPixel);
        report.status = success ? "PASS" : "FAIL"; report.error = error; Export();
        if (success) Debug.Log("Full Manual performance validation PASS."); else Debug.LogError(error);
        ExperimentManualPerformanceValidation.Finish(success);
    }
}
