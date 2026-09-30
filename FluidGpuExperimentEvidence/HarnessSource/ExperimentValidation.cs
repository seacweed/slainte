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
using Stopwatch = System.Diagnostics.Stopwatch;

// Copied into the disposable validation project, never imported into the game.
public static class ExperimentValidation
{
    private const string Key = "Slainte.FluidGpuExperiment.Validation";
    public const string StirDiagnosticKey = "Slainte.FluidGpuExperiment.StirDiagnostic";
    public const string ScenePath = "Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR")
        ?? "FluidGpuExperimentEvidence/manual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    public static void Begin()
    {
        Directory.CreateDirectory(Evidence);
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            throw new InvalidOperationException("Comparison scene missing: " + ScenePath);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    public static void BeginStirDiagnostic()
    {
        SessionState.SetBool(StirDiagnosticKey, true);
        Begin();
    }

    private static void Entered(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("FluidExperimentValidationRunner").AddComponent<ExperimentValidationRunner>().Begin();
    }

    public static void Finish(bool success, string report)
    {
        // Keep the live progress file append-only: Windows readers may map it while Unity runs.
        File.WriteAllText(Path.Combine(Evidence, "validation-final.txt"), report);
        if (success) Debug.Log("[FluidExperimentValidation] PASS\n" + report);
        else Debug.LogError("[FluidExperimentValidation] FAIL\n" + report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ExperimentValidationRunner : MonoBehaviour
{
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private readonly List<string> timings = new List<string> {
        "mode,particles,sample,steps,synchronized_step_ms,notes" };
    private FluidExperimentComparison comparison;
    private FluidExperimentWorld world;
    private FluidExperimentGpuLiquid gpu;
    private bool finished;
    private float started;
    private const float Dt = .02f;

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
        if (!finished && Time.realtimeSinceStartup - started > 210)
            Finish(false, "Validation exceeded 210 seconds; this is a failed attempt.");
    }

    private IEnumerator Guard()
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(Run());
        while (stack.Count > 0)
        {
            bool more;
            object next = null;
            try { more = stack.Peek().MoveNext(); if (more) next = stack.Peek().Current; }
            catch (Exception ex)
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                Finish(false, ex.ToString());
                yield break;
            }
            if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (next is IEnumerator nested) { stack.Push(nested); continue; }
            yield return next;
        }
        Finish(errors.Count == 0, errors.Count == 0 ? "All experiment runtime checks passed." : string.Join("\n", errors));
    }

    private IEnumerator Run()
    {
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        Require(comparison != null, "Comparison controller exists in the authored scene");
        while (!comparison.Ready)
        {
            if (Time.realtimeSinceStartup - started > 15) throw new Exception("Comparison controller never became ready");
            yield return null;
        }
        Require(SystemInfo.supportsComputeShaders, "Compute-capable device: " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType);
        Require(!FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Any(x => x != null
            && (x.GetType().Namespace ?? "").StartsWith("Slainte.Bartending.PhysicsLab", StringComparison.Ordinal)),
            "No original PhysicsLab behaviour is active in the comparison scene");
        if (SessionState.GetBool(ExperimentValidation.StirDiagnosticKey, false))
        {
            SessionState.EraseBool(ExperimentValidation.StirDiagnosticKey);
            SetMode(FluidExperimentMode.ACurrent);
            comparison.StartScenario(FluidExperimentScenario.Stir);
            FreezeCpu();
            DiagnoseReplay(FluidExperimentMode.ACurrent, FluidExperimentScenario.Stir, 120);
            CheckState("Stir diagnostic final state");
            yield break;
        }
        var modes = new[] { FluidExperimentMode.ACurrent, FluidExperimentMode.BReferencePhysics, FluidExperimentMode.CReferenceSurface };
        float initialVolume = -1;
        int initialCount = -1;
        foreach (FluidExperimentMode mode in modes)
        {
            SetMode(mode);
            comparison.StartScenario(FluidExperimentScenario.Rest);
            FreezeCpu();
            Tick();
            gpu.ReadbackNow();
            if (initialVolume < 0) { initialVolume = gpu.SnapshotTotalMl; initialCount = gpu.ActiveCount; }
            Require(initialVolume > 0 && Mathf.Abs(gpu.SnapshotTotalMl - initialVolume) < .001f && gpu.ActiveCount == initialCount,
                mode + ": same initial liquid quantity and particle count (" + initialVolume + " ml / " + initialCount + ")");
            CheckState(mode + " initial state");
            float expected = gpu.EmittedMl;
            for (int i = 0; i < 100; i++) Tick();
            CheckState(mode + " resting state");
            Require(Mathf.Abs(gpu.SnapshotTotalMl - expected) < .001f, mode + ": resting liquid volume conserved");
            ValidateVisibleLiquid(mode + "-rest");

            ValidateReset(mode);
            ValidateRemoteIndependence(mode);
            ValidateClosedRotation(mode);
            ValidatePour(mode);
            foreach (FluidExperimentScenario scenario in new[] { FluidExperimentScenario.Tilt, FluidExperimentScenario.Stir, FluidExperimentScenario.SealedShake })
            {
                comparison.StartScenario(scenario);
                FreezeCpu();
                if (scenario == FluidExperimentScenario.Stir)
                    Require(world.Items.Any(x => x.kind == LabItemKind.Glass && !x.IsHeld && x.Body.bodyType == RigidbodyType2D.Kinematic)
                        && world.Items.Any(x => x.kind == LabItemKind.Spoon && !x.IsHeld && x.Body.bodyType == RigidbodyType2D.Kinematic),
                        mode + ": stir replay permits actual spoon-to-owned-liquid contacts");
                float accepted = gpu.EmittedMl;
                DiagnoseReplay(mode, scenario, 120);
                CheckState(mode + " " + scenario);
                Require(gpu.EmittedMl >= accepted, mode + " " + scenario + ": accepted volume is monotonic");
                Capture(mode + "-" + scenario);
            }
            yield return null;
        }
        ValidateSurfaceDoesNotChangePhysics();
        ValidateReferencePairBehavior();
        foreach (FluidExperimentMode mode in modes)
        {
            SetMode(mode);
            Benchmark(mode);
            yield return null;
        }
        Require(errors.Count == 0, "No Unity error, assertion, or exception during validation");
        report.Add("Performance samples include CPU submission and GPU completion synchronization, exclude rendering, and are not GPU timestamp measurements or gameplay FPS.");
    }

    private void SetMode(FluidExperimentMode mode)
    {
        comparison.automaticScenario = false;
        comparison.SwitchMode(mode);
        world = comparison.World;
        gpu = comparison.Gpu;
        Require(world != null && gpu != null && gpu.IsOperational, mode + ": actual GPU initializes (" + gpu?.Error + ")");
        Require(comparison.ActiveMode == mode, mode + ": controller reports the selected mode");
        Require(FindObjectsByType<FluidExperimentWorld>(FindObjectsSortMode.None).Count(x => x.gameObject.activeInHierarchy) == 1,
            mode + ": exactly one active experiment world");
        Require(!EditorUtility.IsPersistent(gpu.settings), mode + ": mutable solver settings are a runtime copy");
        Require(AssetDatabase.GetAssetPath(gpu.settings.gpuLiquidComputeShader).StartsWith(
            "Assets/_Project/Features/Bartending/FluidGpuExperiment/", StringComparison.Ordinal),
            mode + ": compute asset belongs to the isolated experiment");
        FreezeCpu();
    }

    private void FreezeCpu()
    {
        world.enabled = false;
        world.showControls = false;
        if (world.interactor != null) world.interactor.enabled = false;
        gpu.automaticReadback = false;
        foreach (FluidExperimentBody body in world.Items) body.Body.simulated = false;
    }

    private void Tick(bool scenario = true)
    {
        if (scenario) comparison.AdvanceScenario(Dt);
        world.SendMessage("FixedUpdate");
        world.TickLiquid(Dt);
    }

    private void DiagnoseReplay(FluidExperimentMode mode, FluidExperimentScenario scenario, int steps)
    {
        var rows = new List<string> { "step,index,active,x,y,vx,vy,volume_ml,owner" };
        gpu.ReadbackNow();
        GpuLiquidParticle[] previous = gpu.Snapshot.ToArray();
        Require(previous.All(x => x.VolumeMl == 0), mode + " " + scenario + ": GPU reset leaves no stale slot volume before initial births");
        float initialAccepted = gpu.EmittedMl;
        int retiredCount = 0;
        float retiredMl = 0;
        Vector2 minimum = gpu.settings.gpuLiquidWorldMin, maximum = gpu.settings.gpuLiquidWorldMax;
        for (int step = 0; step < steps; step++)
        {
            Tick();
            gpu.ReadbackNow();
            if (step == 0)
            {
                var streams = gpu.ReadStreamParticles();
                Require(gpu.Snapshot.Where((particle, index) => particle.VolumeMl > 0 && streams[index].Pending != 0).Count() == 0,
                    mode + " " + scenario + ": initial fill has no pending births after the first integration tick");
            }
            for (int i = 0; i < gpu.Snapshot.Length; i++)
            {
                GpuLiquidParticle current = gpu.Snapshot[i];
                if (previous[i].Active != 0 && current.Active == 0)
                {
                    retiredCount++; retiredMl += current.VolumeMl;
                    bool outside = current.Position.x < minimum.x || current.Position.y < minimum.y
                        || current.Position.x > maximum.x || current.Position.y > maximum.y;
                    report.Add(mode + " " + scenario + " retired step=" + (step + 1) + " slot=" + i
                        + " ml=" + current.VolumeMl + " previous=" + previous[i].Position.ToString("R")
                        + " velocity=" + previous[i].Velocity.ToString("R") + " owner=" + previous[i].VesselId
                        + " retired=" + current.Position.ToString("R") + " outside=" + outside);
                    if (!outside) throw new Exception("Particle retired inside simulation bounds");
                }
                if ((step % 10 == 0 && current.Active != 0) || (previous[i].Active != 0 && current.Active == 0))
                    rows.Add(string.Join(",", (step + 1).ToString(), i.ToString(), current.Active.ToString(),
                        current.Position.x.ToString("R", CultureInfo.InvariantCulture), current.Position.y.ToString("R", CultureInfo.InvariantCulture),
                        current.Velocity.x.ToString("R", CultureInfo.InvariantCulture), current.Velocity.y.ToString("R", CultureInfo.InvariantCulture),
                        current.VolumeMl.ToString("R", CultureInfo.InvariantCulture), current.VesselId.ToString()));
            }
            Array.Copy(gpu.Snapshot, previous, previous.Length);
        }
        report.Add(mode + " " + scenario + " accounting: emitted=" + gpu.EmittedMl + ", active=" + gpu.SnapshotTotalMl
            + ", retiredOutside=" + retiredMl + " ml / " + retiredCount + " particles; bounds=" + minimum + ".." + maximum);
        File.WriteAllLines(Path.Combine(ExperimentValidation.Evidence, mode + "-" + scenario + "-trajectory.csv"), rows);
        File.AppendAllText(Path.Combine(ExperimentValidation.Evidence, "validation.txt"), report[report.Count - 1] + "\n");
        Require(Mathf.Abs(initialAccepted - gpu.EmittedMl) < .001f,
            mode + " " + scenario + ": no further emission or retired-slot reuse during the replay");
        GpuLiquidParticle[] retired = gpu.Snapshot.Where(x => x.Active == 0 && x.VolumeMl > 0).ToArray();
        Require(retired.All(x => Finite(x.Position) && (x.Position.x < minimum.x || x.Position.y < minimum.y
            || x.Position.x > maximum.x || x.Position.y > maximum.y)) && retired.Length == retiredCount
            && Mathf.Abs(retired.Sum(x => x.VolumeMl) - retiredMl) < .001f,
            mode + " " + scenario + ": every inactive nonzero-volume slot matches an observed retirement outside simulation bounds");
        Require(Mathf.Abs(gpu.SnapshotTotalMl + retiredMl - gpu.EmittedMl) < .001f,
            mode + " " + scenario + ": active plus explicitly retired liquid equals accepted volume (active="
            + gpu.SnapshotTotalMl + ", retired=" + retiredMl + ", emitted=" + gpu.EmittedMl + " ml)");
    }

    private void CheckState(string label)
    {
        gpu.ReadbackNow();
        int stride = gpu.IngredientStride;
        for (int i = 0; i < gpu.Snapshot.Length; i++)
        {
            GpuLiquidParticle p = gpu.Snapshot[i];
            if (p.Active == 0) continue;
            if (!Finite(p.Position) || !Finite(p.PreviousPosition) || !Finite(p.Velocity)
                || !float.IsFinite(p.VolumeMl) || p.VolumeMl <= 0 || !float.IsFinite(p.TemperatureC))
                throw new Exception(label + ": non-finite or invalid particle at " + i);
            float total = 0;
            for (int component = 0; component < stride; component++)
            {
                float ratio = gpu.CompositionSnapshot[i * stride + component];
                if (!float.IsFinite(ratio) || ratio < -.0001f) throw new Exception(label + ": invalid composition at " + i);
                total += ratio;
            }
            if (Mathf.Abs(total - 1) > .002f) throw new Exception(label + ": unnormalized composition at " + i + " = " + total);
        }
        Require(gpu.ActiveCount > 0, label + ": active particle position, velocity, volume and composition are finite and valid");
        Require(gpu.LastSubsteps == Mathf.Clamp(gpu.settings.gpuLiquidSubsteps, 1, 16), label + ": fixed configured integration schedule");
    }

    private void ValidateReset(FluidExperimentMode mode)
    {
        comparison.ResetComparison();
        FreezeCpu();
        Tick(false);
        gpu.ReadbackNow();
        int first = gpu.ActiveCount;
        float volume = gpu.SnapshotTotalMl;
        comparison.ResetComparison();
        FreezeCpu();
        Tick(false);
        gpu.ReadbackNow();
        Require(gpu.ActiveCount == first && Mathf.Abs(gpu.SnapshotTotalMl - volume) < .001f,
            mode + ": consecutive resets cannot duplicate or retain stale particles");
        gpu.ResetSimulation();
        gpu.ReadbackNow();
        Require(gpu.ActiveCount == 0 && gpu.SnapshotTotalMl == 0 && gpu.CompositionSnapshot.All(x => x == 0),
            mode + ": explicit GPU reset clears particle and composition buffers");
    }

    private GpuLiquidParticle[] RemoteRun(bool move)
    {
        comparison.StartScenario(FluidExperimentScenario.Rest);
        FreezeCpu();
        FluidExperimentBody remote = world.Items.First(x => x.kind == LabItemKind.Bottle);
        remote.SetHeld(true);
        remote.Teleport(new Vector2(-18, 8), 0);
        remote.pourMlPerSecond = 0;
        FluidExperimentBody vessel = world.Items.First(x => x.kind == LabItemKind.Glass && x.name.Contains("highball"));
        ItemDef ingredient = world.Items.First(x => x.ingredient != null).ingredient;
        gpu.ResetSimulation();
        Vector2 local = vessel.contentRegions[0].center;
        Require(vessel.ContainsLiquidDisk(local, gpu.Radius), "Remote isolation seed fits inside the real highball");
        if (!gpu.TryEmitStream(vessel.LocalToWorld(local), new Vector2(.15f, .1f), ingredient,
            gpu.ParticleVolumeMl, 0, gpu.NewPourStream(), 0, 0, gpu.Radius, out uint token))
            throw new Exception("Remote fixture tagged emission rejected");
        // This fixture starts with existing glass contents. A held glass deliberately
        // cannot acquire a free droplet, so author ownership before the tagged birth.
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var commands = (Array)typeof(FluidExperimentGpuLiquid).GetField("spawnCommands", privateInstance).GetValue(gpu);
        int commandIndex = (int)typeof(FluidExperimentGpuLiquid).GetField("pendingSpawnCount", privateInstance).GetValue(gpu) - 1;
        object command = commands.GetValue(commandIndex);
        command.GetType().GetField("VesselId").SetValue(command, vessel.Id);
        commands.SetValue(command, commandIndex);
        gpu.Step(.00002f);
        gpu.ReadbackNow();
        int index = Array.FindIndex(gpu.ReadStreamParticles(), particle => particle.Token == token);
        Require(index >= 0 && gpu.ActiveCount == 1 && gpu.Snapshot[index].VesselId == vessel.Id,
            "Remote isolation begins with one tagged, moving particle owned by the stationary glass");
        bool scheduleFixed = true;
        for (int i = 0; i < 20; i++)
        {
            if (move) remote.SetHeldPose(new Vector2(-18 + (i % 2) * 2, 8), i * 180);
            Tick(false);
            scheduleFixed &= gpu.LastSubsteps == Mathf.Clamp(gpu.settings.gpuLiquidSubsteps, 1, 16);
        }
        gpu.ReadbackNow();
        Require(scheduleFixed, "Remote motion never changes configured substep count across all 20 ticks");
        Require(gpu.ReadStreamParticles()[index].Token == token && gpu.ActiveCount == 1
            && gpu.Snapshot[index].Active != 0 && gpu.Snapshot[index].VesselId == vessel.Id,
            "Remote isolation preserves the particle birth identity, mass and stationary-glass ownership");
        return new[] { gpu.Snapshot[index] };
    }

    private void ValidateRemoteIndependence(FluidExperimentMode mode)
    {
        GpuLiquidParticle[] baseline = RemoteRun(false), repeat = RemoteRun(false), moved = RemoteRun(true);
        var noise = Difference(baseline, repeat);
        var difference = Difference(baseline, moved);
        report.Add(mode + " remote control RMS: repeat=" + noise + "; moved=" + difference);
        Require(noise.position <= .00001f && noise.velocity <= .0001f,
            mode + ": single-particle control repeats within strict absolute position/velocity tolerance");
        Require(difference.position <= .00001f && difference.velocity <= .0001f,
            mode + ": moving and spinning an unrelated held bottle leaves the isolated liquid trajectory unchanged");
        report.Add("Remote-isolation test deliberately uses one moving owned particle to exclude chaotic neighbor summation; dense-fluid behavior is covered by separate rest, pour, coincident and high-count tests.");
    }

    private void ValidateClosedRotation(FluidExperimentMode mode)
    {
        comparison.StartScenario(FluidExperimentScenario.Rest);
        FreezeCpu();
        FluidExperimentBody shaker = world.Items.First(x => x.kind == LabItemKind.Shaker);
        ItemDef ingredient = world.Items.First(x => x.ingredient != null).ingredient;
        gpu.ResetSimulation();
        shaker.SetHeld(true);
        shaker.SetSealed(true);
        shaker.Teleport(new Vector2(0, 3), 0);
        float accepted = gpu.Fill(shaker, ingredient, 30);
        for (int i = 0; i < 30; i++) Tick(false);
        shaker.SetHeldPose(new Vector2(2, 4), 855);
        Tick(false);
        CheckState(mode + " fast closed rotation");
        Require(accepted > 0 && Mathf.Abs(gpu.VolumeIn(shaker.Id) - accepted) < .001f,
            mode + ": sealed shaker retains all liquid after 855-degree rotation and translation");
        Require(gpu.Snapshot.Where(x => x.Active != 0).All(x => shaker.ContainsLiquid(x.Position)),
            mode + ": retained liquid is geometrically inside the moved sealed shaker");
        Require(gpu.ReadSweepExhaustions() == 0, mode + ": fast closed rotation does not exhaust local collision sweep budget");
    }

    private void ValidatePour(FluidExperimentMode mode)
    {
        comparison.StartScenario(FluidExperimentScenario.Pour);
        FreezeCpu();
        float initialStock = world.Items.Where(x => x.kind == LabItemKind.Bottle).Sum(x => x.remainingMl);
        float initialAccepted = gpu.EmittedMl;
        for (int frame = 0; frame < 180; frame++)
        {
            Tick();
            if (frame == 44 || frame == 89 || frame == 179) Capture(mode + "-pour-" + (frame + 1));
        }
        CheckState(mode + " real bottle pour");
        float emitted = gpu.EmittedMl - initialAccepted;
        float remainingStock = world.Items.Where(x => x.kind == LabItemKind.Bottle).Sum(x => x.remainingMl);
        Require(emitted > 5 && Mathf.Abs(initialStock - remainingStock - emitted) < .005f,
            mode + ": authored bottle emitter conserves stock and emitted volume (" + emitted + " ml)");
        Require(Mathf.Abs(gpu.SnapshotTotalMl - gpu.EmittedMl) < .001f,
            mode + ": real pour conserves all prefilled and newly emitted GPU liquid");
        var bottleIds = new HashSet<uint>(world.Items.Where(x => x.kind == LabItemKind.Bottle).Select(x => x.Id));
        var glassIds = new HashSet<uint>(world.Items.Where(x => x.kind == LabItemKind.Glass).Select(x => x.Id));
        GpuLiquidStreamParticle[] stream = gpu.ReadStreamParticles();
        float received = 0;
        for (int i = 0; i < gpu.Snapshot.Length; i++)
            if (gpu.Snapshot[i].Active != 0 && glassIds.Contains(gpu.Snapshot[i].VesselId) && bottleIds.Contains(stream[i].SourceId))
                received += gpu.Snapshot[i].VolumeMl;
        Require(received > .5f, mode + ": particles born from the actual bottle reach a real glass (" + received + " ml)");
        ValidateVisibleLiquid(mode + "-pour-visible");
    }

    private GpuLiquidParticle[] CommonPhysicsRun(FluidExperimentMode mode)
    {
        SetMode(mode);
        comparison.StartScenario(FluidExperimentScenario.Rest);
        FreezeCpu();
        foreach (FluidExperimentBody body in world.Items)
        { body.SetHeld(true); body.Teleport(new Vector2(-18, 10), 0); body.pourMlPerSecond = 0; }
        gpu.ResetSimulation();
        var expected = new Dictionary<uint, Vector2>();
        ItemDef ingredient = world.Items.First(x => x.ingredient != null).ingredient;
        uint stream = gpu.NewPourStream();
        Vector2 gravity = Physics2D.gravity;
        try
        {
            Physics2D.gravity = Vector2.zero;
            for (int i = 0; i < 32; i++)
            {
                Vector2 position = new Vector2(-.315f + (i % 8) * .09f, 5 + (i / 8) * .09f);
                if (!gpu.TryEmitStream(position, Vector2.zero, ingredient, .5f, 0, stream, 0, 0, gpu.Radius, out uint token))
                    throw new Exception("Tagged comparison fixture emission rejected");
                expected.Add(token, position);
            }
            // Spawn allocation uses an atomic free-list; GPU slots are not particle IDs.
            // Tokens give each authored birth stable identity across fresh buffer allocations.
            // Activate reservations below every integration/projection stepDt guard;
            // the initial-state equality check must precede any fluid correction.
            gpu.Step(.0000001f);
            gpu.ReadbackNow();
            GpuLiquidStreamParticle[] metadata = gpu.ReadStreamParticles();
            var identities = new Dictionary<uint, int>();
            for (int i = 0; i < metadata.Length; i++)
                if (expected.ContainsKey(metadata[i].Token)) identities.Add(metadata[i].Token, i);
            Require(identities.Count == expected.Count && identities.All(pair =>
                (gpu.Snapshot[pair.Value].Position - expected[pair.Key]).magnitude < .00001f),
                mode + ": all 32 tagged births match the same authored initial positions");
            for (int i = 0; i < 12; i++) Tick(false);
            gpu.ReadbackNow();
            return expected.Keys.OrderBy(x => x).Select(token => gpu.Snapshot[identities[token]]).ToArray();
        }
        finally { Physics2D.gravity = gravity; }
    }

    private void ValidateSurfaceDoesNotChangePhysics()
    {
        GpuLiquidParticle[] a = CommonPhysicsRun(FluidExperimentMode.ACurrent);
        GpuLiquidParticle[] aRepeat = CommonPhysicsRun(FluidExperimentMode.ACurrent);
        GpuLiquidParticle[] b = CommonPhysicsRun(FluidExperimentMode.BReferencePhysics);
        GpuLiquidParticle[] repeat = CommonPhysicsRun(FluidExperimentMode.BReferencePhysics);
        GpuLiquidParticle[] c = CommonPhysicsRun(FluidExperimentMode.CReferenceSurface);
        var noise = Difference(b, repeat);
        var aNoise = Difference(a, aRepeat);
        var difference = Difference(b, c);
        report.Add("Tagged particle RMS: A/A=" + aNoise + "; B/B=" + noise + "; B/C=" + difference);
        Require(difference.position <= Mathf.Max(.002f, noise.position * 3)
            && difference.velocity <= Mathf.Max(.02f, noise.velocity * 3),
            "B and C share the same physical result within measured repeat noise; surface selection cannot drive physics");
        Require(Difference(a, b).position > Mathf.Max(.00001f, Mathf.Max(aNoise.position, noise.position) * 3),
            "Reference pressure produces a physical result distinguishable from baseline PBF beyond repeat noise");
    }

    private void ValidateReferencePairBehavior()
    {
        SetMode(FluidExperimentMode.BReferencePhysics);
        comparison.StartScenario(FluidExperimentScenario.Rest);
        FreezeCpu();
        foreach (FluidExperimentBody body in world.Items)
        { body.SetHeld(true); body.Teleport(new Vector2(-18, 10), 0); body.pourMlPerSecond = 0; }
        ItemDef ingredient = world.Items.First(x => x.ingredient != null).ingredient;
        Vector2 gravity = Physics2D.gravity;
        var settings = gpu.settings;
        float pressure = settings.referencePressureStiffness, near = settings.referenceNearPressureStiffness;
        float viscosity = settings.referenceViscosityRate, damping = settings.gpuLiquidVelocityDamping;
        try
        {
            Physics2D.gravity = Vector2.zero;
            gpu.ResetSimulation();
            for (int i = 0; i < 64; i++)
                if (!gpu.TryEmit(new Vector2(0, 5), Vector2.zero, ingredient, .5f, 0))
                    throw new Exception("Coincident-particle emission rejected");
            for (int i = 0; i < 10; i++) Tick(false);
            CheckState("Reference coincident-particle fixture");
            Vector2 mean = Vector2.zero;
            foreach (var particle in gpu.Snapshot.Where(x => x.Active != 0)) mean += particle.Position / gpu.ActiveCount;
            float variance = gpu.Snapshot.Where(x => x.Active != 0).Average(x => (x.Position - mean).sqrMagnitude);
            Require(gpu.ActiveCount == 64 && Mathf.Abs(gpu.SnapshotTotalMl - 32) < .001f && variance > .000001f,
                "Reference pressure separates 64 exactly coincident particles without loss or invalid numbers (variance=" + variance + ")");

            settings.referencePressureStiffness = settings.referenceNearPressureStiffness = 0;
            settings.gpuLiquidVelocityDamping = 0;
            float[] closing = new float[2], separating = new float[2];
            for (int run = 0; run < 2; run++)
            {
                settings.referenceViscosityRate = run == 0 ? 0 : 20;
                gpu.Dispose(); gpu.Initialize(world);
                Require(gpu.IsOperational, "Reference viscosity diagnostic initializes with rate " + settings.referenceViscosityRate);
                closing[run] = PairSpeed(true, ingredient);
                separating[run] = PairSpeed(false, ingredient);
            }
            report.Add("Reference viscosity pair speeds: closing=" + closing[0] + "/" + closing[1]
                + "; separating=" + separating[0] + "/" + separating[1]);
            Require(closing[1] < closing[0] - .001f,
                "Reference pair viscosity reduces approaching relative speed when pressure and damping are disabled");
            Require(Mathf.Abs(separating[1] - separating[0]) < .001f,
                "Reference pair viscosity does not damp separating particles");
        }
        finally
        {
            Physics2D.gravity = gravity;
            settings.referencePressureStiffness = pressure; settings.referenceNearPressureStiffness = near;
            settings.referenceViscosityRate = viscosity; settings.gpuLiquidVelocityDamping = damping;
            gpu.Dispose(); gpu.Initialize(world);
        }
    }

    private float PairSpeed(bool approaching, ItemDef ingredient)
    {
        gpu.ResetSimulation();
        float sign = approaching ? 1 : -1;
        if (!gpu.TryEmit(new Vector2(-.05f, 5), new Vector2(sign, 0), ingredient, .5f, 0)
            || !gpu.TryEmit(new Vector2(.05f, 5), new Vector2(-sign, 0), ingredient, .5f, 0))
            throw new Exception("Viscosity pair emission rejected");
        Tick(false); gpu.ReadbackNow();
        GpuLiquidParticle[] pair = gpu.Snapshot.Where(x => x.Active != 0).OrderBy(x => x.Position.x).ToArray();
        if (pair.Length != 2 || pair.Any(x => !Finite(x.Velocity))) throw new Exception("Viscosity pair lost valid state");
        Require((pair[0].Velocity + pair[1].Velocity).magnitude < .001f,
            "Isolated viscosity pair conserves total momentum (approaching=" + approaching + ")");
        return Mathf.Abs(pair[0].Velocity.x - pair[1].Velocity.x);
    }

    private void Benchmark(FluidExperimentMode mode)
    {
        comparison.StartScenario(FluidExperimentScenario.Rest);
        FreezeCpu();
        foreach (FluidExperimentBody body in world.Items)
        {
            body.SetHeld(true);
            body.Teleport(new Vector2(-18, 8), 0);
            body.pourMlPerSecond = 0;
        }
        ItemDef ingredient = world.Items.First(x => x.ingredient != null).ingredient;
        foreach (int count in new[] { 100, 300, 600, 1000 })
        {
            for (int sample = -1; sample < 5; sample++)
            {
                gpu.ResetSimulation();
                for (int i = 0; i < count; i++)
                    if (!gpu.TryEmit(new Vector2(-4 + (i % 40) * .14f, 4 + (i / 40) * .14f), Vector2.zero,
                        ingredient, gpu.ParticleVolumeMl, 0)) throw new Exception("Benchmark emission rejected at " + i);
                Tick(false);
                gpu.ReadbackNow();
                var timer = Stopwatch.StartNew();
                const int steps = 5;
                for (int step = 0; step < steps; step++) Tick(false);
                gpu.ReadbackNow();
                timer.Stop();
                if (gpu.ActiveCount != count) throw new Exception("Benchmark particle population changed");
                if (sample >= 0) timings.Add(mode + "," + count + "," + sample + "," + steps + ","
                    + (timer.Elapsed.TotalMilliseconds / steps).ToString("R", CultureInfo.InvariantCulture)
                    + ",CPU_submission_plus_GPU_completion_no_rendering");
            }
            Require(gpu.ActiveCount == count, mode + ": timed workload preserves " + count + " particles");
        }
        File.WriteAllLines(Path.Combine(ExperimentValidation.Evidence, "synchronized-step-timings.csv"), timings);
    }

    private void ValidateVisibleLiquid(string label)
    {
        gpu.ReadbackNow();
        GpuLiquidParticle[] before = gpu.Snapshot.ToArray();
        gpu.renderParticles = false;
        Color32[] empty = Capture(label + "-hidden");
        gpu.renderParticles = true;
        Color32[] visible = Capture(label);
        int changed = empty.Where((pixel, i) => ColorDifference(pixel, visible[i]) > 10).Count();
        Require(gpu.SurfaceRenderingReady && gpu.SurfaceRenderingError == null && changed > 100,
            label + ": GPU surface produces visible camera pixels (" + changed + " changed pixels)");
        gpu.ReadbackNow();
        var difference = Difference(before, gpu.Snapshot);
        Require(difference.position == 0 && difference.velocity == 0,
            label + ": rendering leaves particle positions and velocities unchanged");
    }

    private Color32[] Capture(string label)
    {
        // Manual validation disables Physics2D stepping, so it must explicitly publish
        // Rigidbody poses to render Transforms. Ordinary gameplay does this in physics.
        foreach (FluidExperimentBody body in world.Items)
            if (!body.Body.simulated)
                body.transform.SetPositionAndRotation(new Vector3(body.Position.x, body.Position.y, body.transform.position.z),
                    Quaternion.Euler(0, 0, body.Angle));
        Camera camera = gpu.outputCamera;
        const int width = 1280, height = 720;
        var target = new RenderTexture(width, height, 24);
        RenderTexture old = camera.targetTexture, active = RenderTexture.active;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(ExperimentValidation.Evidence, label + ".png"), image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            camera.targetTexture = old;
            RenderTexture.active = active;
            Destroy(image);
            target.Release();
            Destroy(target);
        }
    }

    private static int ColorDifference(Color32 a, Color32 b) => Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);
    private static bool Finite(Vector2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);
    private static (float position, float velocity) Difference(GpuLiquidParticle[] a, GpuLiquidParticle[] b)
    {
        if (a.Length != b.Length) return (float.PositiveInfinity, float.PositiveInfinity);
        float position = 0, velocity = 0;
        int count = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].Active != b[i].Active || a[i].VesselId != b[i].VesselId || a[i].VolumeMl != b[i].VolumeMl)
                return (float.PositiveInfinity, float.PositiveInfinity);
            if (a[i].Active == 0) continue;
            position += (a[i].Position - b[i].Position).sqrMagnitude;
            velocity += (a[i].Velocity - b[i].Velocity).sqrMagnitude;
            count++;
        }
        return (Mathf.Sqrt(position / Mathf.Max(1, count)), Mathf.Sqrt(velocity / Mathf.Max(1, count)));
    }

    private void Require(bool condition, string message)
    {
        report.Add((condition ? "PASS: " : "FAIL: ") + message);
        File.WriteAllText(Path.Combine(ExperimentValidation.Evidence, "validation.txt"), string.Join("\n", report));
        if (!condition) throw new Exception(message);
    }

    private void Finish(bool success, string detail)
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        report.Add(detail);
        File.WriteAllLines(Path.Combine(ExperimentValidation.Evidence, "synchronized-step-timings.csv"), timings);
        ExperimentValidation.Finish(success, string.Join("\n", report));
    }
}
