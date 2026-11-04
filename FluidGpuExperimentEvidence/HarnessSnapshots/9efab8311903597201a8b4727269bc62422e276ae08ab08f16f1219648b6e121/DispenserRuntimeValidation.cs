using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;
using Slainte.Bartending.FluidGpuExperiment.Editor;

public static class ExperimentDispenserRuntimeValidation
{
    const string Key = "FluidExperiment.DispenserRuntimeValidation";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("DispenserRuntimeValidation").AddComponent<DispenserRuntimeValidationRunner>();
    };
    public static void Begin()
    {
        // All scene construction is confined to the disposable harness. No authored scene is saved.
        FluidExperimentAssemblyBuilder.BuildAll();
        EditorSceneManager.OpenScene(FluidExperimentAssemblyBuilder.SampleScenePath);
        var sample = UnityEngine.Object.FindFirstObjectByType<FluidExperimentRuntimeRig>();
        sample.enablePointerInput = false;
        string path = AssetDatabase.GenerateUniqueAssetPath(FluidExperimentAssemblyBuilder.Root + "/Scenes/DispenserValidationSecond.unity");
        var scene = FluidExperimentAssemblyBuilder.CreateWorkingScene(path);
        var other = FluidExperimentAssemblyBuilder.CreateSandbox(scene,
            AssetDatabase.LoadAssetAtPath<GameObject>(FluidExperimentAssemblyBuilder.Root + "/Prefabs/Glass_highball.prefab"));
        other.name = "IndependentDispenserRuntime";
        other.enablePointerInput = false; other.seedLiquids = false;
        other.environment.gameObject.SetActive(false); other.inputCamera.enabled = false;
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

// Actual runtime prefab/sample composition, without Comparison or desktop input.
// Scripted native/GPU stepping isolates ownership, stock and reset across two rigs.
public sealed class DispenserRuntimeValidationRunner : MonoBehaviour
{
    const float Dt = .02f;
    readonly List<string> report = new List<string>(), errors = new List<string>();
    FluidExperimentRuntimeRig rig, other;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool okay, string text)
    { report.Add((okay ? "PASS " : "FAIL ") + text); if (!okay) throw new Exception(text); }
    bool Near(float a, float b) => Mathf.Abs(a - b) < .001f;
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }

    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null;
        var previousSimulation = Physics2D.simulationMode;
        var previousGravity = Physics2D.gravity;
        bool success = false;
        try
        {
            var rigs = FindObjectsByType<FluidExperimentRuntimeRig>(FindObjectsSortMode.None);
            rig = rigs.First(value => value.name != "IndependentDispenserRuntime");
            other = rigs.First(value => value.name == "IndependentDispenserRuntime");
            Check(Application.isBatchMode && rigs.Length == 2 && rigs.All(value => value.Ready),
                "Two reusable runtime compositions initialize actual GPU resources without desktop input");
            Check(FindFirstObjectByType<FluidExperimentComparison>() == null,
                "Sample and second sandbox use the reusable runtime without a comparison controller");
            Check(rig.seedLiquids && !rig.World.ExplosiveGlassBreakage && rig.World.shardLiquidCollision,
                "The remote sample retains seeded liquid, natural breakage and shard-liquid contact");
            Check(rigs.All(value => value.catalog.entries.Length == 28
                && value.catalog.entries.All(entry => entry.prefab.GetComponent<FluidExperimentDispenser>() == null)),
                "The original 28 catalog entries remain intact; the single world-owned dispenser is excluded from ordinary spawning");
            foreach (var value in rigs)
            {
                var tool = value.World.Dispenser;
                Check(value.World.dispenserPrefab != null && tool != null && tool.IsAvailable
                    && tool.Body.World == value.World && value.World.Items.Contains(tool.Body),
                    value.name + " instantiates and registers its own configured dispenser");
                Check(value.World.GetComponentsInChildren<FluidExperimentDispenser>(true).Length == 1
                    && tool.State == FluidExperimentDispenserState.Docked && tool.Mode == FluidExperimentDispenserMode.None
                    && Near(tool.RemainingMl, 5000), value.name + " starts with exactly one docked, off, full supply");
                Check(tool.Hose != null && (value.inputCamera.cullingMask & (1 << tool.Hose.gameObject.layer)) != 0
                    && value.Interactor.inputCamera == value.inputCamera && value.Liquid.outputCamera == value.inputCamera,
                    value.name + " uses its explicit runtime camera for visible hose, input and liquid rendering");
            }
            Check(rig.World.Dispenser != other.World.Dispenser && rig.World.Dispenser.Body.World != other.World.Dispenser.Body.World,
                "Dispenser instances never share ownership across independent runtime worlds");
            CheckDockFits(rig);
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = new Vector2(0, -9.81f);
            Prepare(rig); Prepare(other);
            EmitHot(rig); EmitHot(other);
            Check(rig.Liquid.Snapshot.Any(particle => particle.Active != 0 && particle.TemperatureC > 40)
                && other.Liquid.Snapshot.Any(particle => particle.Active != 0 && particle.TemperatureC > 40),
                "Both independent GPU buffers materialize the dispenser's hot-water particles");
            float otherStock = other.World.Dispenser.RemainingMl;
            float otherVolume = other.Liquid.SnapshotTotalMl;
            var toolA = rig.World.Dispenser;
            // The duplicate environment is inactive, and the second tool is stationary during
            // the first world's native motion test; GPU/stock state remains independently live.
            other.World.Dispenser.Body.Body.simulated = false;
            Vector2 target = toolA.DockWorld + Vector2.left * 3;
            toolA.MoveToPointer(target);
            Check(Vector2.Distance(toolA.Body.Position, target) < .002f
                && Vector2.Distance(toolA.transform.position, target) < .002f,
                "Reusable runtime pointer movement immediately updates native and rendered poses before physics");
            for (int i = 0; i < 100; i++) Step(rig);
            Check(Vector2.Distance(toolA.Body.Position, target) < .08f,
                "Reusable runtime world ticks move the held dispenser inside its narrower environment");
            Check(rig.World.floorColliders.Contains(rig.World.leftBoundary)
                && rig.World.floorColliders.Contains(rig.World.rightBoundary)
                && rig.World.floorColliders.Contains(rig.World.topBoundary),
                "Reusable environment fixture includes its walls and ceiling in the shared floor-collider registry");
            toolA.BeginRetract();
            Check(toolA.State == FluidExperimentDispenserState.Retracting && !toolA.CollisionSuppressed
                && toolA.Body.Body.bodyType == RigidbodyType2D.Dynamic
                && toolA.Body.solidColliders.Where(collider => collider != null).All(collider => !collider.isTrigger),
                "Free-space release near the fitted dock restores contacts without treating the registered ceiling as a floor");
            for (int i = 0; i < 250 && toolA.State != FluidExperimentDispenserState.Docked; i++) Step(rig);
            Check(toolA.State == FluidExperimentDispenserState.Docked && Vector2.Distance(toolA.Body.Position, toolA.DockWorld) < .04f,
                "Runtime dispenser returns to its environment-adjusted dock instead of an out-of-bounds comparison position");
            Check(toolA.TryClickMode(toolA.IceButtonWorld) && toolA.Mode == FluidExperimentDispenserMode.Ice
                && rig.Interactor.PickDispenser(toolA, toolA.Body.Position), "Reusable interactor selects and acquires the runtime Ice tool");
            toolA.SetDispensing(true);
            for (int i = 0; i < 15; i++) Step(rig);
            toolA.SetDispensing(false);
            var ice = rig.World.Items.Where(body => body.kind == LabItemKind.Ice).ToArray();
            Check(ice.Length == 1 && ice[0].World == rig.World && !ice[0].CanBePicked
                && Near(toolA.RemainingMl, 4986), "Runtime ice belongs to the emitting world, cannot be picked again and debits the shared stock");
            Capture("dispenser-runtime.png");
            rig.ResetRig(); Disable(rig);
            other.Liquid.ReadbackNow();
            Check(rig.World.Dispenser == toolA && rig.World.Items.Contains(toolA.Body)
                && rig.World.GetComponentsInChildren<FluidExperimentDispenser>().Length == 1
                && toolA.State == FluidExperimentDispenserState.Docked && toolA.Mode == FluidExperimentDispenserMode.None
                && Near(toolA.RemainingMl, 5000) && !toolA.IsDispensing && rig.Interactor.HeldDispenser == null,
                "ResetRig reuses one world-owned dispenser and restores its complete hand, stock and mode state");
            Check(rig.World.Items.All(body => body.kind != LabItemKind.Ice), "ResetRig removes dispenser-generated ice");
            Check(Near(other.World.Dispenser.RemainingMl, otherStock) && Near(other.Liquid.SnapshotTotalMl, otherVolume)
                && other.World.Dispenser.Mode == FluidExperimentDispenserMode.Hot,
                "Resetting the sample preserves the independent world's hot stock, selected mode and GPU liquid");
            other.ResetRig(); Disable(other);
            Check(Near(other.World.Dispenser.RemainingMl, 5000)
                && other.World.Dispenser.Mode == FluidExperimentDispenserMode.None, "The second runtime resets its own supply independently");
            Check(errors.Count == 0, "Runtime dispenser integration reports no Unity, shader or buffer errors");
            success = true;
        }
        catch (Exception exception) { report.Add(exception.ToString()); }
        finally
        {
            Physics2D.simulationMode = previousSimulation; Physics2D.gravity = previousGravity;
            Application.logMessageReceived -= Log; report.AddRange(errors);
            File.WriteAllText(Path.Combine(Evidence, "dispenser-runtime-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(Evidence, "dispenser-runtime-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }

    void Disable(FluidExperimentRuntimeRig value)
    { value.World.enabled = false; value.Interactor.enabled = false; value.Liquid.automaticReadback = false; }
    void Prepare(FluidExperimentRuntimeRig value)
    {
        value.seedLiquids = false; value.ResetRig(); Disable(value);
        foreach (var body in value.World.Items.ToArray())
        {
            body.Body.interpolation = RigidbodyInterpolation2D.None;
            if (body == value.World.Dispenser.Body) continue;
            body.Body.simulated = false; body.Teleport(new Vector2(-80 - body.Id * 3, 30), 0);
            body.transform.SetPositionAndRotation(body.Position, Quaternion.identity); body.pourMlPerSecond = 0;
        }
        value.Liquid.ResetSimulation(); Physics2D.SyncTransforms();
    }
    void EmitHot(FluidExperimentRuntimeRig value)
    {
        var tool = value.World.Dispenser;
        Check(tool.TryClickMode(tool.HotButtonWorld) && tool.Mode == FluidExperimentDispenserMode.Hot
            && tool.TryPick(tool.Body.Position), value.name + " accepts docked Hot selection and pickup");
        tool.SetDispensing(true); tool.Tick(.1f); tool.SetDispensing(false);
        value.Liquid.Step(.1f); value.Liquid.ReadbackNow();
        Check(Near(tool.RemainingMl, 4996) && Near(value.Liquid.EmittedMl, 4),
            value.name + " sends exactly 4 ml only to its own world and supply ledger");
    }
    void Step(FluidExperimentRuntimeRig value)
    {
        value.World.SendMessage("FixedUpdate"); Physics2D.SyncTransforms(); Physics2D.Simulate(Dt);
        value.World.TickLiquid(Dt);
    }
    void CheckDockFits(FluidExperimentRuntimeRig value)
    {
        var bounds = value.World.Dispenser.Body.SolidBounds;
        var left = value.World.leftBoundary; var right = value.World.rightBoundary; var top = value.World.topBoundary;
        Check(bounds.min.x >= left.bounds.max.x - .01f && bounds.max.x <= right.bounds.min.x + .01f
            && bounds.max.y <= top.bounds.min.y + .01f,
            "Runtime dock fits the actual environment walls and ceiling, without assuming the comparison dock coordinates");
    }
    void Capture(string file)
    {
        var camera = rig.inputCamera; var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var target = new RenderTexture(1280, 720, 24); var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Evidence, file), pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            target.Release(); Destroy(target); Destroy(pixels);
        }
    }
}
