using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

// Imported only into the disposable harness. This entry deliberately exercises
// the real PlayerLoop; it never manually advances physics, the replay, or the GPU.
public static class ExperimentFrameValidation
{
    private const string Key = "Slainte.FluidGpuExperiment.FrameValidation";
    private const string ScenePath = "Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR")
        ?? "FluidGpuExperimentEvidence/frame-manual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    public static void Begin()
    {
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Path.Combine(Evidence, "frame-validation.txt"), "STARTED: normal PlayerLoop validation; no result yet.\n");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            throw new InvalidOperationException("Comparison scene missing: " + ScenePath);
        if (EditorApplication.isPaused) throw new InvalidOperationException("Editor is paused; normal frame validation cannot run.");
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    private static void Entered(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("FluidExperimentFrameValidationRunner").AddComponent<ExperimentFrameValidationRunner>().Begin();
    }

    public static void Finish(bool success, string report)
    {
        File.WriteAllText(Path.Combine(Evidence, "frame-validation-final.txt"), report);
        File.AppendAllText(Path.Combine(Evidence, "frame-validation.txt"), "\nFINISHED: " + (success ? "PASS\n" : "FAIL\n"));
        File.WriteAllText(Path.Combine(Evidence, "frame-result.txt"), success ? "PASS\n" : "FAIL\n");
        if (success) Debug.Log("[FluidExperimentFrameValidation] PASS\n" + report);
        else Debug.LogError("[FluidExperimentFrameValidation] FAIL\n" + report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ExperimentFrameValidationRunner : MonoBehaviour
{
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private readonly List<string> poses = new List<string> {
        "mode,scenario,scenario_time,body,rigidbody_x,rigidbody_y,transform_x,transform_y,position_error,is_held,body_type" };
    private readonly Dictionary<FluidExperimentBody, InitialBody> initial = new Dictionary<FluidExperimentBody, InitialBody>();
    private FluidExperimentComparison comparison;
    private FluidExperimentWorld world;
    private FluidExperimentGpuLiquid gpu;
    private Vector3 initialCameraPosition;
    private float initialCameraSize;
    private float started;
    private bool finished;
    private struct InitialBody { public float rate, stock; public int ice; }

    public void Begin()
    {
        started = Time.realtimeSinceStartup;
        Application.logMessageReceived += OnLog;
        StartCoroutine(Guard());
    }

    private void OnLog(string condition, string stack, LogType type)
    {
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            errors.Add(condition + "\n" + stack);
    }

    private void Update()
    {
        if (!finished && Time.realtimeSinceStartup - started > 150)
            Finish(false, "Normal PlayerLoop validation exceeded 150 seconds; failed attempt.");
    }

    private IEnumerator Guard()
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(Run());
        while (stack.Count > 0 && !finished)
        {
            bool more;
            object next = null;
            try
            {
                if (errors.Count > 0) throw new Exception("Unity logged an error:\n" + string.Join("\n", errors));
                more = stack.Peek().MoveNext();
                if (more) next = stack.Peek().Current;
            }
            catch (Exception exception)
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                Finish(false, exception.ToString());
                yield break;
            }
            if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (next is IEnumerator nested) { stack.Push(nested); continue; }
            yield return next;
        }
        if (!finished) Finish(errors.Count == 0, errors.Count == 0
            ? "Normal PlayerLoop A/B/C/D/E/F pouring, stirring, camera output and Manual restoration passed."
            : string.Join("\n", errors));
    }

    private IEnumerator Run()
    {
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        Require(comparison != null, "Authored comparison controller exists");
        while (!comparison.Ready)
        {
            if (Time.realtimeSinceStartup - started > 20)
                throw new Exception("Comparison initialization timed out: " + comparison.Error);
            yield return null;
        }
        world = comparison.World;
        gpu = comparison.Gpu;
        Require(comparison.initialMode == FluidExperimentMode.FCoherentLiquid
            && comparison.ActiveMode == FluidExperimentMode.FCoherentLiquid
            && gpu.CohesivePhysicsActive && !gpu.useImprovedPhysics,
            "Authored scene starts directly in Model F with its cohesive physics active");
        comparison.StartScenario(FluidExperimentScenario.Manual);
        initialCameraPosition = world.interactor.inputCamera.transform.position;
        initialCameraSize = world.interactor.inputCamera.orthographicSize;
        foreach (FluidExperimentBody body in world.Items)
            initial[body] = new InitialBody { rate = body.pourMlPerSecond, stock = body.remainingMl, ice = body.iceStock };
        Require(SystemInfo.supportsComputeShaders && gpu.IsOperational,
            "GPU operational on " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType);
        Require(Physics2D.simulationMode == SimulationMode2D.FixedUpdate && Time.timeScale > 0,
            "Normal fixed-update physics and advancing game time are enabled");
        Require(comparison.automaticScenario && world.enabled && world.gameObject.activeInHierarchy,
            "Authored automatic replay and World coroutine remain enabled");
        Require(world.Items.All(body => body.Body.simulated), "Every authored Rigidbody2D remains simulated");

        foreach (FluidExperimentMode mode in Enum.GetValues(typeof(FluidExperimentMode)))
        {
            comparison.SwitchMode(mode);
            Require(comparison.Ready && gpu.IsOperational, mode + ": GPU mode initializes");
            comparison.StartScenario(FluidExperimentScenario.Pour);
            float initialAccepted = gpu.EmittedMl;
            float initialStock = world.Items.Where(body => body.kind == LabItemKind.Bottle).Sum(body => body.remainingMl);
            int firstFrame = Time.frameCount;
            yield return WaitForReplayTime(2.4f);
            Require(Time.frameCount > firstFrame + 1 && comparison.ScenarioTime >= 2.3f,
                mode + ": real frames advance Pour to " + F(comparison.ScenarioTime) + "s");
            Require(world.enabled && comparison.automaticScenario && world.Items.All(body => body.Body.simulated),
                mode + ": physics and GPU coroutine stayed active throughout Pour");
            CheckFinite(mode + " normal Pour");
            float emitted = gpu.EmittedMl - initialAccepted;
            float remainingStock = world.Items.Where(body => body.kind == LabItemKind.Bottle).Sum(body => body.remainingMl);
            Require(emitted > 5 && Mathf.Abs(initialStock - remainingStock - emitted) < .005f,
                mode + ": actual bottle emits and deducts the same volume (" + F(emitted) + " ml)");
            var sources = new HashSet<uint>(world.Items.Where(body => body.kind == LabItemKind.Bottle).Select(body => body.Id));
            var vessels = new HashSet<uint>(world.Items.Where(body => body.kind == LabItemKind.Glass).Select(body => body.Id));
            GpuLiquidStreamParticle[] streams = gpu.ReadStreamParticles();
            float received = 0;
            for (int i = 0; i < gpu.Snapshot.Length; i++)
                if (gpu.Snapshot[i].Active != 0 && vessels.Contains(gpu.Snapshot[i].VesselId) && sources.Contains(streams[i].SourceId))
                    received += gpu.Snapshot[i].VolumeMl;
            Require(received > .5f, mode + ": source-tagged bottle liquid reaches the glass (" + F(received) + " ml)");
            CheckTransforms(mode + " normal Pour");
            Capture(mode + "-frame-pour");

            comparison.StartScenario(FluidExperimentScenario.Stir);
            FluidExperimentBody spoon = world.Items.First(body => body.kind == LabItemKind.Spoon);
            FluidExperimentBody glass = world.Items.First(body => body.kind == LabItemKind.Glass && body.name.Contains("highball"));
            Require(!spoon.IsHeld && !glass.IsHeld && spoon.Body.bodyType == RigidbodyType2D.Kinematic
                && glass.Body.bodyType == RigidbodyType2D.Kinematic,
                mode + ": Stir cup and spoon are unheld kinematic collision participants");
            yield return WaitForReplayTime(.4f);
            Vector2 spoonPhysics = spoon.Body.position, spoonVisual = spoon.transform.position;
            Vector2 glassPhysics = glass.Body.position, glassVisual = glass.transform.position;
            CheckTransforms(mode + " Stir early");
            Capture(mode + "-frame-stir-early");
            yield return WaitForReplayTime(1.35f);
            float physicalTravel = Vector2.Distance(spoonPhysics, spoon.Body.position);
            float visualTravel = Vector2.Distance(spoonVisual, spoon.transform.position);
            Require(physicalTravel > .1f && visualTravel > .1f,
                mode + ": real replay moves spoon physics and sprite (" + F(physicalTravel) + "/" + F(visualTravel) + ")");
            Require(Vector2.Distance(glassPhysics, glass.Body.position) < .001f
                && Vector2.Distance(glassVisual, glass.transform.position) < .03f,
                mode + ": unheld kinematic cup remains stationary while spoon moves");
            Require(!spoon.IsHeld && !glass.IsHeld && spoon.Body.bodyType == RigidbodyType2D.Kinematic
                && glass.Body.bodyType == RigidbodyType2D.Kinematic,
                mode + ": Stir keeps cup and spoon unheld and kinematic during playback");
            CheckTransforms(mode + " Stir late");
            CheckFinite(mode + " normal Stir");
            Capture(mode + "-frame-stir-late");
            comparison.StartScenario(FluidExperimentScenario.Manual);
            CheckManual(mode);
            yield return null;
            Require(world.enabled && world.Items.All(body => body.Body.simulated), mode + ": normal simulation survives returning to Manual");
        }
    }

    private IEnumerator WaitForReplayTime(float target)
    {
        float phaseStart = Time.realtimeSinceStartup;
        while (comparison.ScenarioTime < target)
        {
            if (Time.realtimeSinceStartup - phaseStart > 30)
                throw new Exception("Replay did not reach " + F(target) + "s using normal frames; stopped at " + F(comparison.ScenarioTime));
            yield return null;
        }
        // Let another normal frame expose the interpolated Transform to renderers.
        // WaitForEndOfFrame is intentionally avoided: it can stall in batch mode.
        yield return null;
    }

    private void CheckFinite(string label)
    {
        gpu.ReadbackNow();
        Require(gpu.ActiveCount > 0 && gpu.Snapshot.Where(particle => particle.Active != 0).All(particle =>
            Finite(particle.Position) && Finite(particle.PreviousPosition) && Finite(particle.Velocity)
            && float.IsFinite(particle.VolumeMl) && particle.VolumeMl > 0 && float.IsFinite(particle.TemperatureC)),
            label + ": all active GPU particle states are finite");
        FluidExperimentLedgerSnapshot ledger = gpu.Ledger;
        Require(ledger != null, label + ": a coherent completed GPU ledger is available");
        FluidExperimentLedgerEntry total = ledger.Total;
        Require(Math.Abs(gpu.SnapshotTotalMl - total.ActiveMl) < .005,
            label + ": particle snapshot and ledger agree on active volume (" + F(gpu.SnapshotTotalMl) + " ml)");
        Require(Math.Abs(total.ConservationErrorMl) < .005,
            label + ": generated liquid equals active + retired + transferred + GPU pending (generated="
            + total.GeneratedMl.ToString("R", CultureInfo.InvariantCulture) + ", active="
            + total.ActiveMl.ToString("R", CultureInfo.InvariantCulture) + ", retired="
            + total.RetiredMl.ToString("R", CultureInfo.InvariantCulture) + " ml)");
        Require(Math.Abs(total.QueueErrorMl) < .005
            && Math.Abs(total.RequestedMl - total.QueuedMl - total.CpuRejectedMl) < .005,
            label + ": requested and queued volume are fully accounted, including pending and rejected births");
    }

    private void CheckTransforms(string label)
    {
        float largest = 0;
        foreach (FluidExperimentBody body in world.Items)
        {
            Vector2 physical = body.Body.position, visual = body.transform.position;
            float error = Vector2.Distance(physical, visual);
            largest = Mathf.Max(largest, error);
            poses.Add(comparison.ActiveMode + "," + comparison.ActiveScenario + "," + F(comparison.ScenarioTime)
                + "," + body.name.Replace(',', '_') + "," + F(physical.x) + "," + F(physical.y)
                + "," + F(visual.x) + "," + F(visual.y) + "," + F(error) + "," + body.IsHeld + "," + body.Body.bodyType);
        }
        File.WriteAllLines(Path.Combine(ExperimentFrameValidation.Evidence, "frame-body-poses.csv"), poses);
        Require(largest < .03f, label + ": visual Transform agrees with Rigidbody2D within .03 units (maximum " + F(largest) + ")");
    }

    private void CheckManual(FluidExperimentMode mode)
    {
        Require(comparison.ActiveScenario == FluidExperimentScenario.Manual && comparison.ScenarioTime == 0,
            mode + ": Manual resets replay state");
        Require(initial.All(pair => Mathf.Abs(pair.Key.pourMlPerSecond - pair.Value.rate) < .0001f
            && Mathf.Abs(pair.Key.remainingMl - pair.Value.stock) < .0001f && pair.Key.iceStock == pair.Value.ice),
            mode + ": Manual restores every body's original emission rate and supply");
        Require(initial.Keys.All(body => !body.IsHeld && body.Body.bodyType == RigidbodyType2D.Dynamic),
            mode + ": Manual releases replay bodies and restores dynamic physics");
        Camera camera = world.interactor.inputCamera;
        Require(Vector3.Distance(camera.transform.position, initialCameraPosition) < .0001f
            && Mathf.Abs(camera.orthographicSize - initialCameraSize) < .0001f,
            mode + ": Manual restores authored camera position and size");
        Rect controlsRect = (Rect)typeof(FluidExperimentComparison).GetProperty("ActiveControlsRect",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(comparison);
        Require(world.interactor.enabled && world.interactor.pointerBlockRect == controlsRect,
            mode + ": Manual enables picking and retains the controls' pointer exclusion rectangle");
    }

    private void Capture(string label)
    {
        Camera camera = gpu.outputCamera;
        const int width = 1280, height = 720;
        var target = new RenderTexture(width, height, 24);
        RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            string path = Path.Combine(ExperimentFrameValidation.Evidence, label + ".png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            Color32[] pixels = image.GetPixels32();
            Color32 first = pixels[0];
            int changed = pixels.Count(pixel => Math.Abs(pixel.r - first.r) + Math.Abs(pixel.g - first.g) + Math.Abs(pixel.b - first.b) > 10);
            Require(File.Exists(path) && new FileInfo(path).Length > 1024 && changed > 100
                && gpu.SurfaceRenderingReady && gpu.SurfaceRenderingError == null,
                label + ": saved nonuniform camera PNG with an operational GPU surface (" + changed + " foreground pixels)");
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Destroy(image); target.Release(); Destroy(target);
        }
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);
    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private void Require(bool condition, string message)
    {
        report.Add((condition ? "PASS: " : "FAIL: ") + message);
        File.AppendAllText(Path.Combine(ExperimentFrameValidation.Evidence, "frame-validation.txt"), report[report.Count - 1] + "\n");
        if (!condition) throw new Exception(message);
    }

    private void Finish(bool success, string detail)
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        report.Add(detail);
        File.WriteAllLines(Path.Combine(ExperimentFrameValidation.Evidence, "frame-body-poses.csv"), poses);
        ExperimentFrameValidation.Finish(success, string.Join("\n", report));
    }
}
