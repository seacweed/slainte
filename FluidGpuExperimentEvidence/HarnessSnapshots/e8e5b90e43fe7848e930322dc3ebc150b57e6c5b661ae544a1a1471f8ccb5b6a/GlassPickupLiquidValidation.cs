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

public static class ExperimentGlassPickupLiquidValidation
{
    const string Key = "FluidExperiment.GlassPickupLiquidValidation";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += s =>
    {
        if (s != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key); new GameObject("GlassPickupLiquidValidation").AddComponent<GlassPickupLiquidValidationRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

// Synchronous reads and fixture writes belong only to this isolated hidden GPU harness.
public sealed class GlassPickupLiquidValidationRunner : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<string> report = new List<string>(), errors = new List<string>();
    FluidExperimentComparison comparison;
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentInteractor hand;
    FluidExperimentBody glass;
    ItemDef ingredient;
    object Field(object o, string name) => o.GetType().GetField(name, Private).GetValue(o);
    void Invoke(object o, string name, params object[] args) => o.GetType().GetMethod(name, Private).Invoke(o, args);
    void Check(bool ok, string text) { report.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) throw new Exception(text); }
    bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < .00005f;
    void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(text); }
    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        while (!comparison.Ready) yield return null;
        var oldMode = Physics2D.simulationMode; var oldGravity = Physics2D.gravity;
        bool success = false;
        try
        {
            world = comparison.World; gpu = comparison.Gpu; hand = world.interactor;
            comparison.automaticScenario = false; comparison.showControls = false;
            world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = new Vector2(0, -9.81f);
            ingredient = world.Items.First(b => b.ingredient != null).ingredient;
            glass = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
            Check(Application.isBatchMode && gpu.CohesivePhysicsActive, "Hidden batch uses actual F GPU without desktop input");
            foreach (float angle in new[] { 90f, -90f, 135f, -135f, 180f, 855f }) ExactPickup(angle, false);
            ExactPickup(90, true);
            PendingBirth();
            MotionScope();
            ContainedSolids();
            foreach (var receiver in world.Items.Where(b => b.kind == LabItemKind.Glass).ToArray())
            {
                glass = receiver;
                foreach (float angle in new[] { 90f, -135f }) SettledPickup(angle);
            }
            LegacyMode();
            Check(errors.Count == 0, "No Unity errors or assertions"); success = true;
        }
        catch (Exception ex) { report.Add(ex.ToString()); }
        finally
        {
            Physics2D.simulationMode = oldMode; Physics2D.gravity = oldGravity;
            Application.logMessageReceived -= Log; report.AddRange(errors);
            string evidence = Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
            File.WriteAllText(Path.Combine(evidence, "glass-pickup-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(evidence, "glass-pickup-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
    void Reset(float angle = 0)
    {
        hand.ReleaseWithVelocity(Vector2.zero);
        foreach (var b in world.Items.ToArray())
        { b.SetHeld(false); b.Teleport(new Vector2(-60 - b.Id * 3, 20), 0); b.Body.simulated = false; b.pourMlPerSecond = 0; }
        gpu.ResetSimulation();
        glass.Body.simulated = true; glass.Body.bodyType = RigidbodyType2D.Kinematic;
        glass.Teleport(new Vector2(0, 1.5f), angle); glass.SetSealed(false);
        Physics2D.SyncTransforms();
    }
    void Step(float dt = .02f)
    {
        world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms(); Physics2D.Simulate(dt); world.TickLiquid(dt);
    }
    GpuLiquidParticle[] Read() { gpu.ReadbackNow(); return (GpuLiquidParticle[])gpu.Snapshot.Clone(); }
    GraphicsBuffer Particles => (GraphicsBuffer)Field(gpu, "particleBuffer");
    void Write(GpuLiquidParticle[] data) => Particles.SetData(data);
    Vector2 Point(Vector2 p, Vector2 from, Vector2 to, float turn) => to + FluidExperimentBody.Rotate(p - from, turn);
    void ExactPickup(float angle, bool clamp)
    {
        Reset(angle);
        if (clamp) glass.Teleport(new Vector2(0, gpu.settings.gpuLiquidWorldMax.y - .6f), angle);
        Vector2 from = glass.Position;
        gpu.TryEmit(glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center), new Vector2(.3f, -.2f), ingredient, .5f, glass.Id);
        gpu.TryEmit(new Vector2(5, 4), new Vector2(-.2f, .1f), ingredient, .5f, 0);
        gpu.TryEmit(new Vector2(-5, 4), new Vector2(.1f, .2f), ingredient, .5f, 98765);
        Step(.001f);
        var before = Read();
        Check(before.Any(p => p.Active != 0 && p.VesselId == glass.Id), $"Owned liquid fixture exists at {angle}, clamp={clamp}");
        var composition = (float[])gpu.CompositionSnapshot.Clone();
        var surfaceBuffer = (GraphicsBuffer)Field(gpu, "improvedSurfaceParticleBuffer");
        var surfaceBefore = new GpuImprovedSurfaceParticle[before.Length]; surfaceBuffer.GetData(surfaceBefore);
        int revision = gpu.SnapshotRevision;
        // A CPU-queued birth must also follow the pickup before the next GPU tick.
        gpu.TryEmit(glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center), Vector2.right, ingredient, .5f, glass.Id);
        var queued = (Array)Field(gpu, "spawnCommands"); object commandBefore = queued.GetValue(0);
        Check(hand.Pick(glass, from), "Real interactor picks tilted glass");
        Check(!gpu.TryTransferContents(glass.Id, revision, out _), "Pre-pickup snapshot cannot transfer changed liquid state");
        Vector2 to = glass.Position; float turn = Mathf.DeltaAngle(angle, 0);
        var after = Read();
        bool transformed = true, untouched = true, metadata = true;
        for (int i = 0; i < before.Length; i++)
        {
            var a = before[i]; var b = after[i];
            if (a.Active == 0) continue;
            if (a.VesselId == glass.Id)
                transformed &= Near(b.Position, Point(a.Position, from, to, turn))
                    && Near(b.PreviousPosition, Point(a.PreviousPosition, from, to, turn))
                    && Near(b.Velocity, FluidExperimentBody.Rotate(a.Velocity, turn));
            else untouched &= a.Equals(b);
            metadata &= a.VolumeMl == b.VolumeMl && a.VesselId == b.VesselId && a.Active == b.Active
                && a.TemperatureC == b.TemperatureC && a.TechniqueFlags == b.TechniqueFlags && a.StateFlags == b.StateFlags;
        }
        Check(transformed, "Both liquid position histories and velocity rotate with no added speed");
        Check(untouched, "Spilled and foreign-owner liquid are byte-identical");
        Check(metadata && composition.SequenceEqual(gpu.CompositionSnapshot), "Volume, composition, temperature and state flags are preserved");
        Vector2 FieldVector(object o, string name) => (Vector2)o.GetType().GetField(name).GetValue(o);
        object commandAfter = queued.GetValue(0);
        Check(Near(FieldVector(commandAfter, "Position"), Point(FieldVector(commandBefore, "Position"), from, to, turn))
            && Near(FieldVector(commandAfter, "Velocity"), FluidExperimentBody.Rotate(Vector2.right, turn)), "CPU queued birth follows exactly once");
        var surfaceAfter = new GpuImprovedSurfaceParticle[before.Length]; surfaceBuffer.GetData(surfaceAfter);
        Check(Enumerable.Range(0, before.Length).Where(i => before[i].Active != 0).All(i => before[i].VesselId != glass.Id
            ? surfaceBefore[i].Equals(surfaceAfter[i]) : Near(surfaceAfter[i].StartPosition, after[i].Position)
                && Near(surfaceAfter[i].EndPosition, after[i].Position)
                && Near(surfaceAfter[i].Direction, FluidExperimentBody.Rotate(surfaceBefore[i].Direction, turn))),
            "Surface endpoints rebase and ellipse directions rotate only for this glass");
        Check(Near(glass.PreviousPosition, glass.Position) && Mathf.Abs(glass.PreviousAngle) < .0001f,
            "Pickup adds no glass sweep or wall velocity on next tick, including whole turns");
        if (clamp) Check(Vector2.Distance(from, to) > .01f, "Fixture exercises ceiling-induced pickup translation");
    }
    void PendingBirth()
    {
        Reset(90);
        gpu.TryEmit(glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center), Vector2.right, ingredient, .5f, glass.Id);
        // Upload an ordinary emission without integrating its first birth yet.
        ((GraphicsBuffer)Field(gpu, "spawnCommandBuffer")).SetData((Array)Field(gpu, "spawnCommands"), 0, 0, 1);
        ((GraphicsBuffer)Field(gpu, "spawnStreamBuffer")).SetData((Array)Field(gpu, "spawnStreams"), 0, 0, 1);
        ((ComputeShader)Field(gpu, "simulationShader")).SetInt("_SpawnCount", 1);
        Invoke(gpu, "DispatchForCount", Field(gpu, "spawnKernel"), 1);
        gpu.GetType().GetField("pendingSpawnCount", Private).SetValue(gpu, 0);
        var before = Read(); var streams = gpu.ReadStreamParticles();
        int slot = Array.FindIndex(streams, s => s.Pending != 0);
        Check(slot >= 0 && before[slot].Active == 0, "GPU reserved-but-unborn particle fixture");
        Vector2 origin = glass.Position;
        hand.Pick(glass, origin); var after = Read();
        Check(Near(after[slot].Position, Point(before[slot].Position, origin, glass.Position, -90))
            && Near(after[slot].Velocity, FluidExperimentBody.Rotate(before[slot].Velocity, -90))
            && streams.SequenceEqual(gpu.ReadStreamParticles()), "GPU pending birth moves without changing its stream identity or timing");
        Step(); Read();
        Check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .0001 && gpu.VolumeIn(glass.Id) == .5f, "Pending birth activates inside upright glass without loss or duplication");
    }
    void MotionScope()
    {
        Reset(90); gpu.Fill(glass, ingredient, 10); Step(.001f);
        // Native motion since the previous liquid tick is preserved independently of pickup correction.
        Vector2 oldPrevious = glass.PreviousPosition;
        glass.Body.position += new Vector2(.1f, .05f);
        Vector2 from = glass.Position;
        hand.Pick(glass, from);
        Check(Near(glass.Position - glass.PreviousPosition, FluidExperimentBody.Rotate(from - oldPrevious, -90)), "Pending genuine translation survives pickup history rebase");
        glass.SynchronizeHistory();
        hand.MoveHeld(from + new Vector2(.2f, 0)); Invoke(glass, "ApplyHeldPose");
        Check(Vector2.Distance(glass.Position, glass.PreviousPosition) > .19f, "Mouse movement after pickup remains real boundary motion");
        var before = Read(); hand.BeginRotation(); hand.RotateBy(70); Invoke(glass, "ApplyHeldPose"); var after = Read();
        Check(before.SequenceEqual(after), "Manual tilt never carries GPU liquid");
        hand.EndRotation(from); hand.AdvanceUprightReturn(.08f, from); after = Read();
        Check(before.SequenceEqual(after), "Right-button upright return never carries GPU liquid");
    }
    void ContainedSolids()
    {
        Reset(90);
        var icePrefab = world.Items.First(b => b.kind == LabItemKind.IceBucket).icePrefab;
        var garnishPrefab = world.GetComponentsInChildren<FluidExperimentGarnishSource>()[0].garnishPrefab;
        Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        var ice = Instantiate(icePrefab, center, Quaternion.Euler(0, 0, 90), world.transform);
        var garnish = Instantiate(garnishPrefab, center, Quaternion.identity, world.transform);
        ice.Teleport(center, 90); garnish.Teleport(center, 0);
        world.RefreshIceContainment();
        Check(ice.ContainingVesselId == glass.Id && garnish.ContainingVesselId == glass.Id, "Both loose-solid fixtures belong to tilted glass");
        ice.Body.linearVelocity = new Vector2(.2f, -.1f); ice.Body.angularVelocity = 12;
        Vector2 from = glass.Position, iceStart = ice.Position, garnishStart = garnish.Position;
        hand.Pick(glass, from);
        Check(Near(ice.Position, Point(iceStart, from, glass.Position, -90)) && Near(ice.Position, ice.PreviousPosition)
            && Mathf.Abs(Mathf.DeltaAngle(ice.Angle, ice.PreviousAngle)) < .0001f,
            "Carried ice has no artificial pickup sweep to kick liquid");
        Check(Near(ice.Body.linearVelocity, FluidExperimentBody.Rotate(new Vector2(.2f, -.1f), -90))
            && ice.Body.angularVelocity == 12, "Ice free speed and spin are retained");
        Check(Near(garnish.Position, garnishStart), "Released native garnish is not attached to pickup carry");
        ice.gameObject.SetActive(false); garnish.gameObject.SetActive(false); Destroy(ice.gameObject); Destroy(garnish.gameObject);
    }
    void SettledPickup(float angle)
    {
        Reset(); float fill = gpu.Fill(glass, ingredient, 40);
        for (int i = 0; i < 150; i++) Step();
        var settled = Read(); Vector2 origin = glass.Position;
        // A rotated settled state isolates the artificial pickup impulse from gravity-induced sloshing.
        glass.Teleport(origin, angle);
        for (int i = 0; i < settled.Length; i++) if (settled[i].Active != 0 && settled[i].VesselId == glass.Id)
        {
            settled[i].Position = Point(settled[i].Position, origin, origin, angle);
            settled[i].PreviousPosition = Point(settled[i].PreviousPosition, origin, origin, angle);
            settled[i].Velocity = FluidExperimentBody.Rotate(settled[i].Velocity, angle);
        }
        Write(settled); hand.Pick(glass, origin);
        float maximum = 0;
        for (int i = 0; i < 12; i++)
        {
            Step(); var data = Read();
            maximum = Mathf.Max(maximum, data.Where(p => p.Active != 0).Select(p => p.Velocity.magnitude).DefaultIfEmpty().Max());
        }
        report.Add($"INFO {glass.name} angle={angle} fill={fill} retained={gpu.VolumeIn(glass.Id)} maxSpeed={maximum:F5}");
        Check(gpu.VolumeIn(glass.Id) >= fill - .001f && maximum < .6f,
            "Settled tilted-glass pickup retains every drop without a speed spike");
        Check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .001, "Post-pickup GPU ledger conserves volume");
    }
    void LegacyMode()
    {
        Reset(); comparison.SwitchMode(FluidExperimentMode.DImprovedSurface);
        gpu = comparison.Gpu; world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
        Reset(90); gpu.Fill(glass, ingredient, 5); Step(.001f); var before = Read();
        hand.Pick(glass, glass.Position); var after = Read();
        Check(before.SequenceEqual(after) && Mathf.Abs(glass.PreviousAngle) > 45, "D retains original liquid and swept pickup behavior");
    }
}
