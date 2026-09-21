// Copied only into the isolated scene harness. Never runs in the gameplay project.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.PhysicsLab;
using Slainte.Bartending.PhysicsLab.Editor;

public static class BoundaryBenchmark
{
    const string Key = "PhysicsLab.BoundaryBenchmark";
    public static void BeginReference() { SessionState.SetBool(Key + ".Linear", true); Begin(); }
    public static void BeginDifferential() { SessionState.SetBool(Key + ".Differential", true); Begin(); }
    public static void Begin()
    {
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene(PhysicsLabBuilder.ScenePath);
        EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod] static void Resume()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                new GameObject("Boundary benchmark").AddComponent<BoundaryBenchmarkRunner>();
        };
    }
    public static void Finish(Exception error)
    {
        SessionState.EraseBool(Key);
        File.WriteAllText(Path.Combine(PhysicsLabValidator.EvidenceDirectory, "result.txt"), error == null ? "PASS\n" : error.ToString());
        if (error != null) UnityEngine.Debug.LogException(error);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(error == null ? 0 : 1);
    }
}
public sealed partial class BoundaryBenchmarkRunner : MonoBehaviour
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    PhysicsLabGpuLiquid gpu;
    PhysicsLabWorld world;
    ComputeShader shader;
    readonly uint[] fence = new uint[4];
    GpuLiquidParticle[] particles;
    GpuLiquidStreamParticle[] streams;
    float[] composition;
    readonly List<string> rows = new List<string> { "scenario,count,operation,sample,wall_ms_per_operation" };
    object Field(string name) => typeof(PhysicsLabGpuLiquid).GetField(name, Private).GetValue(gpu);
    void Call(string name, params object[] args) => typeof(PhysicsLabGpuLiquid).GetMethod(name, Private).Invoke(gpu, args);
    void Drain() => ((GraphicsBuffer)Field("statisticsBuffer")).GetData(fence);
    IEnumerator Start()
    {
        yield return null;
        yield return new WaitForFixedUpdate();
        try { Run(); BoundaryBenchmark.Finish(null); }
        catch (Exception ex) { BoundaryBenchmark.Finish(ex); }
    }
    void Run()
    {
        world = FindFirstObjectByType<PhysicsLabWorld>(); gpu = world.Liquid;
        if (!gpu.IsOperational) throw new Exception(gpu.Error);
        world.enabled = false; world.interactor.enabled = false;
        gpu.automaticReadback = false; gpu.renderParticles = false;
        foreach (var body in world.Items) { body.Body.simulated = false; body.SynchronizeHistory(); }
        shader = (ComputeShader)Field("simulationShader");
        bool linear = SessionState.GetBool("PhysicsLab.BoundaryBenchmark.Linear", false);
        SessionState.EraseBool("PhysicsLab.BoundaryBenchmark.Linear");
        if (linear) shader.EnableKeyword("PHYSICSLAB_LINEAR_BOUNDARIES");
        bool differential = SessionState.GetBool("PhysicsLab.BoundaryBenchmark.Differential", false);
        SessionState.EraseBool("PhysicsLab.BoundaryBenchmark.Differential");
        if (differential) { RunDifferential(); return; }
        File.WriteAllText(Path.Combine(PhysicsLabValidator.EvidenceDirectory, "environment.txt"),
            $"Unity {Application.unityVersion}\n{SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType}\n" +
            $"capacity={gpu.settings.gpuLiquidParticleCapacity}, iterations={gpu.settings.gpuLiquidSolverIterations}, substeps={gpu.settings.gpuLiquidSubsteps}\n" +
            $"linearReference={linear}\n" +
            "Measurement: synchronized CPU submission + GPU completion wall time; NOT GPU timestamps or gameplay FPS.\n" +
            "Static authored nine-body scene; controlled contained liquid + falling streams. Snapshot restored outside timing.\n");
        foreach (int count in new[] { 100, 300, 600, 1000 })
        {
            Seed(count);
            Measure(count, "Step", () => gpu.Step(.02f), 3);
            if (count == 1000)
            {
                foreach (string name in new[] { "SweepBoundaries", "CalculateDensityLambda", "CalculatePositionDelta", "ApplyDeltaAndBoundaries", "UpdateVelocities", "MixComposition" })
                {
                    int kernel = shader.FindKernel(name);
                    Measure(count, name, () => shader.Dispatch(kernel, gpu.settings.gpuLiquidParticleCapacity / 64, 1, 1), 12);
                }
                Measure(count, "BuildStreamSurface", () => Call("PrepareStreamSurface", new object[] { null }), 12);
                Measure(count, "UploadGeometry", () => Call("UploadGeometry", 0f, .5f, .01f), 12);
                var target = new RenderTexture(1600, 900, 24);
                Camera camera = world.interactor.inputCamera;
                camera.targetTexture = target; gpu.renderParticles = true;
                Measure(count, "Camera1600x900", () => camera.Render(), 3);
                camera.targetTexture = null; target.Release(); Destroy(target); gpu.renderParticles = false;
            }
        }
        File.WriteAllLines(Path.Combine(PhysicsLabValidator.EvidenceDirectory, "timings.csv"), rows);
    }
    void Seed(int count)
    {
        gpu.ResetSimulation();
        ItemDef ingredient = world.Items.First(x => x.ingredient != null).ingredient;
        int emitted = 0;
        var vessels = world.Items.Where(x => x.IsVessel && x.collisionProfile != null).ToArray();
        // At most half of the requested population in actual, authored vessel interiors.
        foreach (var vessel in vessels)
        foreach (Rect region in vessel.contentRegions)
        for (float y = region.yMin; y <= region.yMax; y += .12f)
        for (float x = region.xMin; x <= region.xMax; x += .12f)
        {
            if (emitted >= count / 2 || !vessel.ContainsLiquidDisk(new Vector2(x, y), gpu.Radius)) continue;
            if (gpu.TryEmit(vessel.LocalToWorld(new Vector2(x, y)), Vector2.zero, ingredient, .5f, vessel.Id)) emitted++;
        }
        uint previous = 0, stream = gpu.NewPourStream();
        for (int i = emitted; i < count; i++)
        {
            int j = i - emitted;
            if (j % 40 == 0) { stream = gpu.NewPourStream(); previous = 0; }
            Vector2 position = new Vector2(-8 + (j / 40) * .55f, 4 + (j % 40) * .09f);
            if (!gpu.TryEmitStream(position, Vector2.down * 2, ingredient, .5f, 0, stream, previous, 0, .04f, out uint token))
                throw new Exception("Particle seed failed");
            previous = token;
        }
        gpu.Step(.02f); gpu.ReadbackNow();
        if (gpu.ActiveCount != count) throw new Exception($"Expected {count}, actual {gpu.ActiveCount}");
        particles = (GpuLiquidParticle[])gpu.Snapshot.Clone();
        streams = gpu.ReadStreamParticles();
        composition = (float[])gpu.CompositionSnapshot.Clone();
    }
    void Restore()
    {
        ((GraphicsBuffer)Field("particleBuffer")).SetData(particles);
        ((GraphicsBuffer)Field("streamParticleBuffer")).SetData(streams);
        ((GraphicsBuffer)Field("compositionA")).SetData(composition);
        ((GraphicsBuffer)Field("compositionB")).SetData(composition);
        typeof(PhysicsLabGpuLiquid).GetField("simulationTime", Private).SetValue(gpu, .02f);
        Call("RebuildGrid"); Drain();
    }
    void Measure(int count, string operation, Action action, int repeats)
    {
        for (int sample = -2; sample < 9; sample++)
        {
            Restore();
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++) action();
            Drain(); timer.Stop();
            if (sample >= 0) rows.Add($"contained-and-stream,{count},{operation},{sample},{(timer.Elapsed.TotalMilliseconds / repeats).ToString("F6", CultureInfo.InvariantCulture)}");
        }
        File.WriteAllLines(Path.Combine(PhysicsLabValidator.EvidenceDirectory, "timings.csv"), rows);
    }
}
