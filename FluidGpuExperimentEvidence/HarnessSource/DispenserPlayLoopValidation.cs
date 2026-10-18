using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

public static class ExperimentDispenserPlayLoopValidation
{
    const string Key = "FluidExperiment.DispenserPlayLoopValidation";
    const string ControlKey = "FluidExperiment.DispenserPlayLoopWithoutDispenser";
    const string ExplosiveKey = "FluidExperiment.DispenserPlayLoopExplosive";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        bool control = SessionState.GetBool(ControlKey, false);
        SessionState.EraseBool(ControlKey);
        var runner = new GameObject("DispenserPlayLoopValidation").AddComponent<DispenserPlayLoopValidationRunner>();
        runner.ControlWithoutDispenser = control;
        runner.ExplosiveVariant = SessionState.GetBool(ExplosiveKey, false);
        SessionState.EraseBool(ExplosiveKey);
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(ControlKey, false);
        SessionState.SetBool(ExplosiveKey, false);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    public static void BeginExplosive()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        // In-memory G startup only. The authored scene continues to default to F.
        UnityEngine.Object.FindFirstObjectByType<FluidExperimentComparison>().initialMode = FluidExperimentMode.GExplosiveBreakage;
        SessionState.SetBool(ControlKey, false);
        SessionState.SetBool(ExplosiveKey, true);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    public static void BeginWithoutDispenser()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        // Disposable in-memory control only. Never save the modified scene or asset.
        var world = UnityEngine.Object.FindFirstObjectByType<FluidExperimentWorld>();
        if (world == null) throw new InvalidOperationException("Comparison world is missing from the baseline scene.");
        world.dispenserPrefab = null;
        SessionState.SetBool(ControlKey, true);
        SessionState.SetBool(ExplosiveKey, false);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
}

// Integration smoke: retain the authored items, seed liquid, native automatic
// Physics2D, world.FixedUpdate and world.SimulateAfterPhysics coroutine. Only the
// physical mouse interactor is disabled; controller commands stand in for intent.
// There are no calls to Tick, StepMotion, Physics2D.Simulate or GPU.Step here.
public sealed class DispenserPlayLoopValidationRunner : MonoBehaviour
{
    public bool ControlWithoutDispenser;
    public bool ExplosiveVariant;
    readonly List<string> report = new List<string>(), errors = new List<string>();
    readonly List<string> timeline = new List<string> {
        "stage,fixedTime,toolX,toolY,targetX,targetY,error,angle,stockMl,totalEmittedMl,receiverMl,state,dispensing,blocked"
    };
    FluidExperimentComparison comparison;
    FluidExperimentWorld world;
    FluidExperimentDispenser dispenser;
    FluidExperimentBody receiver;
    Vector2 target;
    sealed class OriginalBodyState
    {
        public FluidExperimentBody body;
        public uint id;
        public string name;
        public bool simulated, active;
        public RigidbodyType2D bodyType;
        public Vector2 position;
        public float angle;
        public int iceStock;
    }
    OriginalBodyState[] originals;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    string Prefix => ControlWithoutDispenser ? "dispenser-play-loop-baseline" : ExplosiveVariant ? "dispenser-play-loop-g" : "dispenser-play-loop";
    void Check(bool okay, string text)
    {
        report.Add((okay ? "PASS " : "FAIL ") + text);
        if (!okay) throw new Exception(text);
    }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }

    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        IEnumerator body = Run();
        bool failed = false;
        while (true)
        {
            bool more = false;
            object current = null;
            try { more = body.MoveNext(); if (more) current = body.Current; }
            catch (Exception exception) { report.Add(exception.ToString()); failed = true; }
            if (failed || !more) break;
            yield return current;
        }
        Application.logMessageReceived -= Log;
        report.AddRange(errors);
        bool success = !failed && errors.Count == 0;
        File.WriteAllText(Path.Combine(Evidence, Prefix + "-validation.txt"), string.Join("\n", report));
        File.WriteAllLines(Path.Combine(Evidence, Prefix + "-timeline.csv"), timeline);
        File.WriteAllText(Path.Combine(Evidence, Prefix + "-result.txt"), success ? "PASS" : "FAIL");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }

    IEnumerator Run()
    {
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        float deadline = Time.realtimeSinceStartup + 30;
        while (comparison != null && !comparison.Ready && Time.realtimeSinceStartup < deadline) yield return null;
        Check(Application.isBatchMode && comparison != null && comparison.Ready,
            "Hidden normal-play-loop scene initializes without desktop input");
        world = comparison.World;
        world.interactor.enabled = false;
        // Manual has no scripted scenario motion; leave the authored automaticScenario
        // flag intact to exercise the regular Comparison.FixedUpdate path as well.
        Check(comparison.ActiveMode == (ExplosiveVariant ? FluidExperimentMode.GExplosiveBreakage : FluidExperimentMode.FCoherentLiquid)
            && comparison.ActiveScenario == FluidExperimentScenario.Manual,
            ExplosiveVariant ? "The remote G variant starts in Manual without a test reset" : "The authored scene starts in F Manual without a test reset or mode override");
        Check(world.ExplosiveGlassBreakage == ExplosiveVariant && world.Liquid.CohesivePhysicsActive,
            "Manual startup retains the selected natural/explosive breakage and cohesive liquid settings");
        Check(world.enabled && Physics2D.simulationMode == SimulationMode2D.FixedUpdate,
            "World component and native automatic fixed-step physics stay enabled");
        Check(world.seedLiquids && world.Liquid.automaticReadback, "Authored seed liquid and automatic GPU readback stay enabled");
        dispenser = world.Dispenser;
        if (ControlWithoutDispenser)
            Check(dispenser == null && world.dispenserPrefab == null,
                "Baseline control omits only the in-memory dispenser reference and creates no dispenser");
        else
        {
            Check(dispenser != null && dispenser.IsAvailable && dispenser.State == FluidExperimentDispenserState.Docked,
                "Normal world startup creates the available docked dispenser");
            Check(dispenser.Hose != null
                && (world.interactor.inputCamera.cullingMask & (1 << dispenser.Hose.gameObject.layer)) != 0,
                "The generated hose layer is included in the real gameplay camera culling mask");
        }
        receiver = world.Items.First(item => item.kind == LabItemKind.Glass && item.name.Contains("highball"));
        int initialItemCount = world.Items.Count;
        originals = world.Items.Select(item => new OriginalBodyState {
            body = item, id = item.Id, name = item.name, simulated = item.Body.simulated,
            active = item.gameObject.activeInHierarchy, bodyType = item.Body.bodyType,
            position = item.Position, angle = item.Angle, iceStock = item.iceStock
        }).ToArray();
        var hurricaneProfile = originals.Select(state => state.body.GetComponent<FluidExperimentBreakableGlass>())
            .First(value => value != null).profile;
        Check(Mathf.Abs(hurricaneProfile.minimumImpactSpeed - 6) < .0001f
            && Mathf.Abs(hurricaneProfile.minimumImpulsePerMass - 4) < .0001f,
            "Automatic startup preserves the remote 6/4 hurricane thresholds");
        RecordBodies("initial");
        var wait = new WaitForFixedUpdate();
        for (int i = 0; i < 60; i++) yield return wait;
        world.Liquid.ReadbackNow();
        float initialReceiverMl = world.Liquid.VolumeIn(receiver.Id);
        RecordBodies("after-natural-settling");
        ValidateNaturalStartup();
        Check(initialReceiverMl > 0, "The real receiving highball retains authored seed liquid before dispensing");
        report.Add(FormattableString.Invariant($"INFO original receiverId={receiver.Id} position=({receiver.Position.x:R},{receiver.Position.y:R}) seedMl={initialReceiverMl:R} initialTotalMl={world.Liquid.SnapshotTotalMl:R} itemCount={initialItemCount}"));
        if (ControlWithoutDispenser)
        {
            Capture("dispenser-baseline-after-startup.png");
            Check(errors.Count == 0, "No Unity errors, assertions or exceptions in dispenser-free automatic startup");
            yield break;
        }
        Check(dispenser.TryClickMode(dispenser.HotButtonWorld) && dispenser.Mode == FluidExperimentDispenserMode.Hot,
            "Docked real tool accepts Hot selection");
        Check(dispenser.TryPick(dispenser.Body.Position), "Normal scene tool enters Held from a controller pickup command");
        Vector2 mouthOffset = dispenser.MouthWorld - dispenser.Body.Position;
        target = receiver.LocalToWorld(receiver.mouthLocal) + Vector2.up * .8f - mouthOffset;
        float initialDistance = Vector2.Distance(dispenser.Body.Position, target);
        dispenser.MoveToPointer(target);
        for (int i = 0; i < 100; i++)
        {
            yield return wait;
            if (i % 10 == 0) Record("moving-" + i);
        }
        float settledError = Vector2.Distance(dispenser.Body.Position, target);
        Check(settledError < .12f && settledError < initialDistance * .03f,
            "Actual world.FixedUpdate drives held motion to the cursor target over the authored highball");
        Check(Mathf.Abs(dispenser.Body.Angle) < .02f && !world.interactor.PointerCaptured,
            "Automatic play-loop translation keeps the tool upright without cursor capture");
        float stockBefore = dispenser.RemainingMl;
        float totalBefore = world.Liquid.EmittedMl;
        float emissionStart = Time.fixedTime;
        dispenser.SetDispensing(true);
        for (int i = 0; i < 50; i++)
        {
            yield return wait;
            if (i % 10 == 0) Record("emitting-" + i);
        }
        // Capture during the live output; original camera framing shows the full scene.
        Capture("dispenser-normal-play-emitting.png");
        dispenser.SetDispensing(false);
        float emissionDuration = Time.fixedTime - emissionStart;
        float consumed = stockBefore - dispenser.RemainingMl;
        float accepted = world.Liquid.EmittedMl - totalBefore;
        float rateAllowance = dispenser.hotFlowMlPerSecond * Time.fixedDeltaTime + world.Liquid.ParticleVolumeMl + .01f;
        Check(consumed > 20 && Mathf.Abs(consumed - accepted) < .001f,
            "Actual SimulateAfterPhysics ticks submit hot particles and debit exactly the accepted volume");
        Check(Mathf.Abs(consumed - emissionDuration * dispenser.hotFlowMlPerSecond) <= rateAllowance,
            "Normal play-loop output follows the configured 40 ml per second within one fixed tick and particle quantization");
        report.Add(FormattableString.Invariant($"INFO emissionDuration={emissionDuration:R} consumed={consumed:R} accepted={accepted:R} configuredFlow={dispenser.hotFlowMlPerSecond:R} fixedDt={Time.fixedDeltaTime:R} tolerance={rateAllowance:R}"));
        float stoppedStock = dispenser.RemainingMl;
        for (int i = 0; i < 50; i++) yield return wait;
        world.Liquid.ReadbackNow();
        Check(!dispenser.IsDispensing && Mathf.Abs(dispenser.RemainingMl - stoppedStock) < .001f,
            "Emission off stays off across subsequent automatic physics and liquid ticks");
        Check(world.Liquid.Snapshot.Where(particle => particle.Active != 0).All(particle =>
            float.IsFinite(particle.Position.x) && float.IsFinite(particle.Position.y)
            && float.IsFinite(particle.Velocity.x) && float.IsFinite(particle.Velocity.y)),
            "Normal authored-scene liquid remains finite after dispensing");
        float afterReceiverMl = world.Liquid.VolumeIn(receiver.Id);
        report.Add(FormattableString.Invariant($"INFO receiver seedBefore={initialReceiverMl:R} afterSettling={afterReceiverMl:R} change={afterReceiverMl - initialReceiverMl:R} emittedDose={accepted:R} totalSnapshotMl={world.Liquid.SnapshotTotalMl:R}"));
        Record("stopped");
        dispenser.BeginRetract();
        Check(dispenser.State == FluidExperimentDispenserState.Retracting, "Release command starts normal-play retraction");
        target = dispenser.DockWorld;
        for (int i = 0; i < 400 && dispenser.State != FluidExperimentDispenserState.Docked; i++)
        {
            yield return wait;
            if (i % 10 == 0) Record("retracting-" + i);
        }
        Check(dispenser.State == FluidExperimentDispenserState.Docked
            && Vector2.Distance(dispenser.Body.Position, dispenser.DockWorld) < .035f,
            "Automatic fixed-step motion returns the tool through the real scene to its dock");
        for (int i = 0; i < 10; i++) yield return wait;
        yield return null; // Let the normal LateUpdate settle the hose before the final capture.
        Check(!dispenser.IsDispensing && Mathf.Abs(dispenser.RemainingMl - stoppedStock) < .001f,
            "Retraction and docking preserve the stopped shared inventory");
        Check(world.enabled && world.seedLiquids && world.Liquid.automaticReadback
            && Physics2D.simulationMode == SimulationMode2D.FixedUpdate,
            "Integration keeps production world and GPU scheduling active throughout");
        Record("docked");
        Capture("dispenser-normal-play-docked.png");
        RecordBodies("after-docking");
        Check(originals.All(state => state.body != null && world.Items.Contains(state.body)) && world.GlassShards.Count == 0,
            "Normal dispensing and retraction preserve every original body without incidental hurricane fracture");
        Check(errors.Count == 0, "No Unity errors, assertions or exceptions in the normal play loop");
    }

    void ValidateNaturalStartup()
    {
        var missing = originals.Where(state => state.body == null || !world.Items.Contains(state.body)).ToArray();
        Check(world.Items.All(item => originals.Any(state => state.body == item)),
            "Natural startup adds no unexpected registered bodies");
        Check(missing.Length == 0 && world.GlassShards.Count == 0,
            "The remote 6/4 thresholds prevent incidental hurricane fracture during authored startup");
        var surviving = originals.Where(state => state.body != null && world.Items.Contains(state.body)).ToArray();
        Check(surviving.All(state => state.body.Body.simulated == state.simulated
            && state.body.Body.bodyType == state.bodyType && state.body.gameObject.activeInHierarchy == state.active),
            "Every unbroken original retains its initialized simulation, rigidbody type and active state");
        Check(surviving.Length + missing.Length == originals.Length
            && missing.Sum(state => state.body.GetComponent<FluidExperimentBreakableGlass>().profile.shards.Length) == world.GlassShards.Count,
            "Original body and native shard accounting explains the complete startup registry difference");
        report.Add($"INFO startupControl={ControlWithoutDispenser} naturalFractures={missing.Length} retainedOriginals={surviving.Length} shards={world.GlassShards.Count}; no fixture teleport, native simulation disable, threshold change or removal was applied");
    }

    void RecordBodies(string stage)
    {
        var initialIds = new HashSet<uint>(originals.Select(state => state.id));
        var currentIds = new HashSet<uint>(world.Items.Where(item => item != null).Select(item => item.Id));
        string missing = string.Join(";", initialIds.Where(id => !currentIds.Contains(id)));
        string added = string.Join(";", currentIds.Where(id => !initialIds.Contains(id)));
        string nonsimulated = string.Join(";", world.Items.Where(item => item != null && !item.Body.simulated).Select(item => item.Id + ":" + item.name));
        report.Add($"INFO bodies {stage} originalCount={originals.Length} registeredCount={world.Items.Count} missingIds=[{missing}] addedIds=[{added}] nonSimulated=[{nonsimulated}] shardCount={world.GlassShards.Count}");
        var rows = new List<string> { "stage,id,name,kind,registered,active,simulated,bodyType,x,y,angle,initialActive,initialSimulated,initialBodyType,initialX,initialY,initialAngle,initialIceStock,iceStock,broken,impactSpeed,impactImpulse" };
        foreach (var state in originals)
        {
            var item = state.body;
            if (item == null)
            {
                report.Add($"INFO original body destroyed id={state.id} name={state.name}");
                continue;
            }
            var breakable = item.GetComponent<FluidExperimentBreakableGlass>();
            bool broken = breakable != null && breakable.IsBroken;
            float impactSpeed = breakable != null ? breakable.LastImpactSpeed : 0;
            float impactImpulse = breakable != null ? breakable.LastImpactImpulse : 0;
            rows.Add(FormattableString.Invariant($"{stage},{item.Id},{item.name},{item.kind},{world.Items.Contains(item)},{item.gameObject.activeInHierarchy},{item.Body.simulated},{item.Body.bodyType},{item.Position.x:R},{item.Position.y:R},{item.Angle:R},{state.active},{state.simulated},{state.bodyType},{state.position.x:R},{state.position.y:R},{state.angle:R},{state.iceStock},{item.iceStock},{broken},{impactSpeed:R},{impactImpulse:R}"));
            if (!world.Items.Contains(item) || item.Body.simulated != state.simulated || item.Body.bodyType != state.bodyType
                || item.gameObject.activeInHierarchy != state.active)
                report.Add($"INFO changed original id={item.Id} name={item.name} initialSimulated={state.simulated} currentSimulated={item.Body.simulated} initialType={state.bodyType} currentType={item.Body.bodyType} initialActive={state.active} currentActive={item.gameObject.activeInHierarchy} broken={broken} impactSpeed={impactSpeed:R} impactImpulse={impactImpulse:R}");
        }
        foreach (var item in world.Items.Where(item => item != null && !initialIds.Contains(item.Id)))
            report.Add($"INFO added runtime body id={item.Id} name={item.name} kind={item.kind} simulated={item.Body.simulated} active={item.gameObject.activeInHierarchy} position={item.Position:R} canPick={item.CanBePicked}");
        File.WriteAllLines(Path.Combine(Evidence, Prefix + "-bodies-" + stage + ".csv"), rows);
    }

    void Record(string stage)
    {
        var point = dispenser.Body.Position;
        float error = Vector2.Distance(point, target);
        timeline.Add(FormattableString.Invariant($"{stage},{Time.fixedTime:R},{point.x:R},{point.y:R},{target.x:R},{target.y:R},{error:R},{dispenser.Body.Angle:R},{dispenser.RemainingMl:R},{world.Liquid.EmittedMl:R},{world.Liquid.VolumeIn(receiver.Id):R},{dispenser.State},{dispenser.IsDispensing},{dispenser.BlockedReason}"));
    }

    void Capture(string filename)
    {
        var camera = world.interactor.inputCamera;
        var oldTarget = camera.targetTexture;
        var oldActive = RenderTexture.active;
        var targetTexture = new RenderTexture(1280, 720, 24);
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = targetTexture;
            camera.Render(); RenderTexture.active = targetTexture;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Evidence, filename), pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            targetTexture.Release(); Destroy(targetTexture); Destroy(pixels);
        }
    }
}
