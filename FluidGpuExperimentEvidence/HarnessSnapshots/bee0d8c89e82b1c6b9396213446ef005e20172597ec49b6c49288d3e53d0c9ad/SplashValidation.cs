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

public static class ExperimentSplashValidation
{
    const string Key = "FluidExperiment.SplashValidation";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key); new GameObject("SplashValidation").AddComponent<SplashValidationRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

// Direct buffers deliberately isolate the break transaction from gravity, wall contacts
// and neighbor forces. This validates the real GPU kernel against the CPU queued path.
public sealed class SplashValidationRunner : MonoBehaviour
{
    const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    const uint Owner = 701, ForeignOwner = 702;
    readonly List<string> report = new List<string>(), errors = new List<string>();
    FluidExperimentGpuLiquid gpu;
    FluidExperimentSplashProfile profile;
    ItemDef ingredient;
    readonly Vector2 center = new Vector2(0, .5f), point = Vector2.zero;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    object Field(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
    void SetField(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
    object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Members).Invoke(target, args);
    T Value<T>(object target, string name) => (T)Field(target, name);
    bool Near(Vector2 a, Vector2 b, float epsilon = .0002f) => Vector2.Distance(a, b) <= epsilon;
    void Check(bool condition, string description)
    { report.Add((condition ? "PASS " : "FAIL ") + description); if (!condition) throw new Exception(description); }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }

    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null;
        var comparison = FindFirstObjectByType<FluidExperimentComparison>();
        while (!comparison.Ready) yield return null;
        var oldSimulation = Physics2D.simulationMode;
        bool success = false;
        try
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            comparison.automaticScenario = false; comparison.showControls = false;
            comparison.World.enabled = false; comparison.World.interactor.enabled = false;
            gpu = comparison.Gpu; gpu.automaticReadback = false;
            ingredient = comparison.World.Items.First(body => body.ingredient != null).ingredient;
            profile = ScriptableObject.CreateInstance<FluidExperimentSplashProfile>();
            Check(Application.isBatchMode && gpu.CohesivePhysicsActive, "Splash harness uses actual F GPU in hidden batch mode");
            DirectionAndLimits();
            Transaction(false, Vector2.up);
            Transaction(true, Vector2.up);
            Transaction(false, Vector2.left);
            NullAndZeroProfile();
            Check(errors.Count == 0, "No shader, buffer or Unity errors during splash transactions");
            success = true;
        }
        catch (Exception exception) { report.Add(exception.ToString()); }
        finally
        {
            if (profile != null) Destroy(profile);
            Physics2D.simulationMode = oldSimulation;
            Application.logMessageReceived -= Log; report.AddRange(errors);
            File.WriteAllText(Path.Combine(Evidence, "splash-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(Evidence, "splash-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }

    void DirectionAndLimits()
    {
        Vector2 left = profile.EvaluateVelocityDelta(new Vector2(-.4f, .4f), center, point, Vector2.up, 8, 8);
        Vector2 right = profile.EvaluateVelocityDelta(new Vector2(.4f, .4f), center, point, Vector2.up, 8, 8);
        Check(left.x < 0 && right.x > 0 && left.y > 0 && right.y > 0,
            "Floor impact produces a two-sided upward fan");
        Check(Mathf.Abs(left.x + right.x) < .0001f && Mathf.Abs(left.y - right.y) < .0001f,
            "Symmetric particles receive balanced tangential motion");
        Vector2 upper = profile.EvaluateVelocityDelta(new Vector2(-.2f, .9f), center, point, Vector2.left, 8, 8);
        Vector2 lower = profile.EvaluateVelocityDelta(new Vector2(-.2f, .1f), center, point, Vector2.left, 8, 8);
        Check(upper.x < 0 && lower.x < 0 && upper.y > 0 && lower.y < 0,
            "Wall impact turns the fan with the collision normal");
        Vector2 extreme = profile.EvaluateVelocityDelta(new Vector2(.1f, .1f), center, point, Vector2.up, 1000, 1000, true);
        Check(extreme.magnitude <= profile.maximumDeltaSpeed + .0001f && extreme.y >= 0,
            "Extreme explosive impact respects the delta-speed cap and never points into the floor");
        Check(profile.EvaluateVelocityDelta(new Vector2(10, 10), center, point, Vector2.up, 8, 8) == Vector2.zero,
            "Distant owned liquid receives no splash outside the impact radius");
        Check(profile.EvaluateVelocityDelta(Vector2.one, center, point, Vector2.up, 8, 0) == Vector2.zero,
            "Zero measured contact impulse cannot inject splash energy");
        Check(profile.EvaluateVelocityDelta(Vector2.one, center, point, Vector2.up, float.NaN, 8) == Vector2.zero,
            "Non-finite impact input cannot produce invalid velocity");
        Vector2 burst = profile.EvaluateVelocityDelta(new Vector2(.4f, .4f), center, point, Vector2.up, 8, 8, true);
        Check(burst.magnitude > right.magnitude && burst.magnitude <= profile.maximumDeltaSpeed + .0001f,
            "G increases liquid splash within the same hard speed limit");
    }

    void UploadBirths()
    {
        int count = Value<int>(gpu, "pendingSpawnCount");
        ((GraphicsBuffer)Field(gpu, "spawnCommandBuffer")).SetData((Array)Field(gpu, "spawnCommands"), 0, 0, count);
        ((GraphicsBuffer)Field(gpu, "spawnStreamBuffer")).SetData((Array)Field(gpu, "spawnStreams"), 0, 0, count);
        ((ComputeShader)Field(gpu, "simulationShader")).SetInt("_SpawnCount", count);
        Invoke(gpu, "DispatchForCount", Field(gpu, "spawnKernel"), count);
        SetField(gpu, "pendingSpawnCount", 0);
    }
    GpuLiquidParticle[] Read()
    { gpu.ReadbackNow(); return (GpuLiquidParticle[])gpu.Snapshot.Clone(); }

    void Transaction(bool explosive, Vector2 normal)
    {
        gpu.ResetSimulation();
        string label = explosive ? "G" : "F";
        Vector2 activePosition = new Vector2(-.4f, .4f), pendingPosition = new Vector2(.4f, .4f);
        Check(gpu.TryEmit(activePosition, new Vector2(.3f, -3f), ingredient, .5f, Owner)
            && gpu.TryEmit(pendingPosition, new Vector2(-.2f, -2f), ingredient, .75f, Owner)
            && gpu.TryEmit(new Vector2(-1, 1), Vector2.right, ingredient, .6f, ForeignOwner)
            && gpu.TryEmit(new Vector2(1, 1), Vector2.left, ingredient, .7f, 0)
            && gpu.TryEmit(new Vector2(5, 5), Vector2.down, ingredient, .8f, Owner), label + " GPU fixtures reserve");
        UploadBirths();
        var particles = Read(); var streams = gpu.ReadStreamParticles();
        int active = Array.FindIndex(particles, p => p.VesselId == Owner && Near(p.Position, activePosition));
        int pending = Array.FindIndex(particles, p => p.VesselId == Owner && Near(p.Position, pendingPosition));
        Check(active >= 0 && pending >= 0, label + " fixtures contain separate active and reserved GPU slots");
        particles[active].Active = 1; particles[active].PreviousPosition = activePosition + new Vector2(.01f, .02f);
        particles[active].TechniqueFlags = 3; particles[active].StateFlags = 2;
        streams[active].Pending = 0; streams[active].StepDt = .01f;
        streams[pending].Delay = .375f; streams[pending].BirthTime = 1.75f;
        streams[pending].Token = 29; streams[pending].PreviousToken = 28; streams[pending].StreamId = 8;
        ((GraphicsBuffer)Field(gpu, "particleBuffer")).SetData(particles);
        ((GraphicsBuffer)Field(gpu, "streamParticleBuffer")).SetData(streams);
        Check(gpu.TryEmit(pendingPosition, new Vector2(-.2f, -2f), ingredient, .9f, Owner)
            && gpu.TryEmit(new Vector2(2, 1), Vector2.up, ingredient, .4f, ForeignOwner), label + " CPU queued fixtures reserve");
        var queuedStreams = (GpuLiquidStreamParticle[])Field(gpu, "spawnStreams");
        queuedStreams[0].Delay = .625f; queuedStreams[0].BirthTime = 2.75f;
        queuedStreams[0].Token = 33; queuedStreams[0].PreviousToken = 32; queuedStreams[0].StreamId = 9;
        var before = Read(); var composition = (float[])gpu.CompositionSnapshot.Clone();
        var beforeStreams = gpu.ReadStreamParticles(); var beforeCpuStreams = (GpuLiquidStreamParticle[])queuedStreams.Clone();
        var commands = (Array)Field(gpu, "spawnCommands"); var beforeCommands = (Array)commands.Clone();
        int count = Value<int>(gpu, "pendingSpawnCount");
        gpu.ReleaseWithSplash(Owner, center, point, normal, 8, 7, profile, explosive);
        var after = Read(); var afterStreams = gpu.ReadStreamParticles();
        bool gpuExact = true, streamExact = true;
        for (int i = 0; i < before.Length; i++)
        {
            var expected = before[i];
            if (expected.VesselId == Owner)
            {
                if (expected.Active != 0 || beforeStreams[i].Pending != 0)
                    expected.Velocity += profile.EvaluateVelocityDelta(expected.Position, center, point, normal, 8, 7, explosive);
                expected.VesselId = 0;
            }
            gpuExact &= SameParticle(after[i], expected);
            streamExact &= afterStreams[i].Equals(beforeStreams[i]);
        }
        Check(gpuExact, label + " GPU active/pending velocities match CPU formula; position, history, flags, volume and unrelated particles are exact");
        Check(streamExact && gpu.CompositionSnapshot.SequenceEqual(composition),
            label + " GPU birth delay, stream links, timing and all ingredient composition are unchanged");
        bool cpuExact = Value<int>(gpu, "pendingSpawnCount") == count;
        for (int i = 0; i < count; i++)
        {
            object old = beforeCommands.GetValue(i), current = commands.GetValue(i);
            bool owned = Value<uint>(old, "VesselId") == Owner;
            Vector2 expected = Value<Vector2>(old, "Velocity") + (owned
                ? profile.EvaluateVelocityDelta(Value<Vector2>(old, "Position"), center, point, normal, 8, 7, explosive) : Vector2.zero);
            cpuExact &= Near(Value<Vector2>(current, "Velocity"), expected)
                && Value<uint>(current, "VesselId") == (owned ? 0 : Value<uint>(old, "VesselId"))
                && Value<Vector2>(current, "Position") == Value<Vector2>(old, "Position")
                && Value<float>(current, "VolumeMl") == Value<float>(old, "VolumeMl")
                && Value<float>(current, "TemperatureC") == Value<float>(old, "TemperatureC")
                && Value<uint>(current, "SourceIngredient") == Value<uint>(old, "SourceIngredient")
                && Value<uint>(current, "StateFlags") == Value<uint>(old, "StateFlags")
                && queuedStreams[i].Equals(beforeCpuStreams[i]);
        }
        Check(cpuExact, label + " CPU queued births receive identical impulse without changing content, chronology or unrelated commands");
        Check(Near(after[pending].Velocity, Value<Vector2>(commands.GetValue(0), "Velocity")),
            label + " identical GPU-pending and CPU-queued particle positions/velocities produce the same result");
        var afterCommands = (Array)commands.Clone();
        gpu.ReleaseWithSplash(Owner, center, point, normal, 8, 7, profile, explosive);
        gpu.ReleaseOwner(Owner); // Body.OnDisable's second release is allowed.
        var twice = Read();
        Check(twice.Zip(after, (a, b) => SameParticle(a, b)).All(equal => equal)
            && Enumerable.Range(0, count).All(i => commands.GetValue(i).Equals(afterCommands.GetValue(i))),
            label + " repeated break/release cannot add a second impulse");
        UploadBirths();
        var final = Read(); var finalStreams = gpu.ReadStreamParticles();
        int cpuBirth = Array.FindIndex(finalStreams, s => s.Token == 33);
        Check(cpuBirth >= 0 && final[cpuBirth].VesselId == 0 && Near(final[cpuBirth].Velocity, after[pending].Velocity)
            && finalStreams[cpuBirth].Pending == 1 && finalStreams[cpuBirth].Delay == .625f,
            label + " released CPU birth uploads later with its original delay and exactly one impulse");
    }

    bool SameParticle(GpuLiquidParticle actual, GpuLiquidParticle expected) => Near(actual.Velocity, expected.Velocity)
        && actual.Position == expected.Position && actual.PreviousPosition == expected.PreviousPosition
        && actual.VesselId == expected.VesselId && actual.VolumeMl == expected.VolumeMl
        && actual.TemperatureC == expected.TemperatureC && actual.TechniqueFlags == expected.TechniqueFlags
        && actual.StateFlags == expected.StateFlags && actual.Active == expected.Active && actual.Padding == expected.Padding;

    void NullAndZeroProfile()
    {
        foreach (bool missing in new[] { true, false })
        {
            gpu.ResetSimulation();
            Check(gpu.TryEmit(new Vector2(.2f, .2f), Vector2.down, ingredient, .5f, Owner), "Release-only fixture reserves");
            UploadBirths(); var before = Read();
            float oldStrength = profile.strength; profile.strength = 0;
            gpu.ReleaseWithSplash(Owner, center, point, Vector2.up, 8, 8, missing ? null : profile, true);
            profile.strength = oldStrength;
            var after = Read();
            for (int i = 0; i < before.Length; i++) if (before[i].VesselId == Owner) before[i].VesselId = 0;
            Check(after.Zip(before, (a, b) => SameParticle(a, b)).All(equal => equal),
                missing ? "Null splash profile preserves legacy release-only motion" : "Zero splash strength disables extra velocity while releasing contents");
        }
    }
}
