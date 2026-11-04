using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

public static class HeldContentsDiagnosis
{
    const string Key = "FluidExperiment.HeldContentsDiagnosis";
    [InitializeOnLoadMethod] static void Register()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
            SessionState.EraseBool(Key);
            new GameObject("HeldContentsDiagnosis").AddComponent<HeldContentsDiagnosisRunner>();
        };
    }
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
}

public sealed class HeldContentsDiagnosisRunner : MonoBehaviour
{
    readonly List<string> report = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<FluidExperimentBody> pieces = new List<FluidExperimentBody>();
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentInteractor hand;
    FluidExperimentBody glass, icePrefab;
    FluidExperimentGarnishSource source;
    ItemDef ingredient;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    GraphicsBuffer Particles => (GraphicsBuffer)gpu.GetType().GetField("particleBuffer", Private).GetValue(gpu);
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool pass, string message)
    {
        report.Add((pass ? "PASS " : "FAIL ") + message);
        if (!pass) throw new Exception(message);
    }
    void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) errors.Add(message);
    }
    IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        yield return null;
        var comparison = FindFirstObjectByType<FluidExperimentComparison>();
        while (!comparison.Ready) yield return null;
        var oldSimulation = Physics2D.simulationMode;
        var oldGravity = Physics2D.gravity;
        bool success = false;
        try
        {
            world = comparison.World; gpu = comparison.Gpu; hand = world.interactor;
            comparison.automaticScenario = false; comparison.showControls = false;
            world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = new Vector2(0, -9.81f);
            glass = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
            icePrefab = world.Items.First(b => b.kind == LabItemKind.IceBucket).icePrefab;
            source = world.GetComponentsInChildren<FluidExperimentGarnishSource>().First();
            ingredient = world.Items.First(b => b.ingredient != null).ingredient;
            Check(gpu.CohesivePhysicsActive, "Current F solver is active");
            LiquidRelease(true);
            LiquidRelease(true, true);
            LiquidRelease(false);
            SolidCarry(false);
            SolidCarry(true);
            StaleFlowReprojection();
            Check(errors.Count == 0, "No Unity application errors or assertions");
            success = true;
        }
        catch (Exception exception) { report.Add(exception.ToString()); }
        finally
        {
            Physics2D.simulationMode = oldSimulation; Physics2D.gravity = oldGravity;
            Application.logMessageReceived -= OnLog;
            report.AddRange(errors);
            File.WriteAllText(Path.Combine(Evidence, "held-contents-diagnosis.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(Evidence, "held-contents-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
    void ResetFixture()
    {
        hand.ReleaseWithVelocity(Vector2.zero);
        foreach (var piece in pieces) if (piece != null) { piece.gameObject.SetActive(false); Destroy(piece.gameObject); }
        pieces.Clear();
        foreach (var body in world.Items.ToArray())
        {
            body.SetHeld(false); body.Teleport(new Vector2(-60 - body.Id * 3, 20), 0);
            body.Body.simulated = false; body.pourMlPerSecond = 0;
        }
        gpu.ResetSimulation();
        glass.Body.simulated = true; glass.Body.bodyType = RigidbodyType2D.Kinematic;
        glass.Teleport(new Vector2(0, 1.5f), 0); glass.SetSealed(false);
        Physics2D.SyncTransforms();
    }
    GpuLiquidParticle[] Read()
    {
        gpu.ReadbackNow(); return (GpuLiquidParticle[])gpu.Snapshot.Clone();
    }
    int SeedOwned(Vector2 velocity, bool freeParticle)
    {
        Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        Check(gpu.TryEmit(center, velocity, ingredient, .5f, glass.Id), "Seed owned liquid");
        if (freeParticle) Check(gpu.TryEmit(new Vector2(5, 4), new Vector2(-.7f, .25f), ingredient, .5f, 0), "Seed unrelated free liquid");
        world.TickLiquid(.001f);
        var particles = Read();
        int slot = Array.FindIndex(particles, p => p.Active != 0 && p.VesselId == glass.Id);
        Check(slot >= 0, "Seed materialized on GPU");
        particles[slot].Position = center;
        particles[slot].Velocity = velocity;
        particles[slot].PreviousPosition = center - velocity * .02f;
        Particles.SetData(particles);
        return slot;
    }
    void LiquidRelease(bool click, bool moving = false)
    {
        ResetFixture(); Check(hand.Pick(glass, glass.Position), "Pick glass for release probe");
        int slot = SeedOwned(new Vector2(1.2f, -.4f), true);
        if (moving)
        {
            ((IList)typeof(FluidExperimentInteractor).GetField("samples", Private).GetValue(hand)).Clear();
            var sample = typeof(FluidExperimentInteractor).GetMethod("Sample", Private);
            sample.Invoke(hand, new object[] { 10f });
            glass.SetHeldPose(glass.Position + new Vector2(.04f, 0), 0);
            typeof(FluidExperimentBody).GetMethod("ApplyHeldPose", Private).Invoke(glass, null);
            sample.Invoke(hand, new object[] { 10.02f });
        }
        var before = Read(); Vector2 start = glass.Position;
        int freeSlot = Array.FindIndex(before, p => p.Active != 0 && p.VesselId == 0);
        if (click) Check(hand.Drop(glass.Position), "Actual click-drop succeeds without obstacle correction");
        else hand.ReleaseWithVelocity(new Vector2(2, .3f), 30);
        var after = Read();
        report.Add($"MEASURE release click={click} moving={moving} glassDelta={glass.Position-start} glassVelocity={glass.Body.linearVelocity} liquidBefore={before[slot].Velocity} liquidAfter={after[slot].Velocity} historyBefore={before[slot].Position-before[slot].PreviousPosition} historyAfter={after[slot].Position-after[slot].PreviousPosition}");
        Check(Vector2.Distance(glass.Position, start) < .00001f, "Release probe has zero position correction");
        Check(after[slot].VolumeMl == before[slot].VolumeMl && after[slot].VesselId == before[slot].VesselId, "Release preserves amount and owner");
        Check(after[freeSlot].Velocity == before[freeSlot].Velocity, "Release leaves unrelated free liquid velocity unchanged");
        if (click)
        {
            Check(after[slot].Velocity == Vector2.zero, "Reproduced defect: click-drop erases owned liquid velocity");
            Check(after[slot].Position == after[slot].PreviousPosition, "Reproduced defect: click-drop erases liquid motion history");
        }
        else Check(after[slot].Velocity == before[slot].Velocity, "Programmatic release bypasses the click-drop velocity reset");
    }
    FluidExperimentBody Spawn(bool garnish)
    {
        Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        var piece = Instantiate(garnish ? source.garnishPrefab : icePrefab, center, Quaternion.identity, world.transform);
        piece.Teleport(center, 0); pieces.Add(piece); world.RefreshIceContainment();
        return piece;
    }
    void SolidCarry(bool garnish)
    {
        ResetFixture(); var piece = Spawn(garnish);
        Check(piece.ContainingVesselId == glass.Id, "Solid belongs to probe glass");
        Check(hand.Pick(glass, glass.Position), "Pick glass for carry probe");
        Vector2 before = piece.Position, glassBefore = glass.Position;
        Vector2 delta = new Vector2(.06f, .04f);
        glass.SetHeldPose(glassBefore + delta, 0);
        typeof(FluidExperimentBody).GetMethod("ApplyHeldPose", Private).Invoke(glass, null);
        report.Add($"MEASURE carry garnish={garnish} glassDelta={glass.Position-glassBefore} solidDelta={piece.Position-before} glassNativeVelocity={glass.Body.linearVelocity} impliedPoseVelocity={delta/.02f} solidVelocity={piece.Body.linearVelocity}");
        Check(Vector2.Distance(piece.Position - before, garnish ? Vector2.zero : delta) < .00001f,
            garnish ? "F garnish has no direct held-vessel carry" : "Reproduced defect: ice follows glass pose even without any physics step");
        Check(glass.Body.linearVelocity == Vector2.zero, "Held glass native velocity remains zero while its pose is moved");
    }
    void StaleFlowReprojection()
    {
        ResetFixture(); var garnish = Spawn(true);
        SeedOwned(new Vector2(.5f, 0), false);
        gpu.ReadbackNow();
        var snapshot = gpu.Snapshot;
        Array.Clear(snapshot, 0, snapshot.Length);
        Vector2 center = garnish.Position;
        int slot = 0;
        for (int x=-2; x<=2; x++) for (int y=-1; y<=1; y++)
            snapshot[slot++] = new GpuLiquidParticle { Active=1, VesselId=glass.Id, VolumeMl=.5f,
                Position=center + new Vector2(x,y)*.04f, Velocity=new Vector2(.5f,0) };
        Check(gpu.TrySampleGarnishFlow(garnish, out _, out float beforeWet), "Synthetic completed snapshot initially supports garnish");
        // Move poses only. Keep the recorded world-space liquid snapshot unchanged.
        Vector2 delta = new Vector2(1, 0);
        glass.Body.position += delta; garnish.Body.position += delta;
        Physics2D.SyncTransforms();
        bool sampled = gpu.TrySampleGarnishFlow(garnish, out Vector2 flow, out float afterWet);
        report.Add($"MEASURE reproject unchangedSnapshot=true glassDelta={delta} sampled={sampled} submergedBefore={beforeWet:R} submergedAfter={afterWet:R} sampledFlow={flow}");
        Check(sampled && afterWet > 0, "Snapshot sampling carries old liquid support with the moved glass without a GPU tick");
        report.Add("INFO Reprojection probe is synthetic and demonstrates sampler policy, not measured gameplay quality.");
    }
}
