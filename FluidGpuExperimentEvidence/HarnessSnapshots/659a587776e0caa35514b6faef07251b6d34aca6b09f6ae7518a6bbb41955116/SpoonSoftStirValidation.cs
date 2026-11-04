using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending;
using Slainte.Bartending.FluidGpuExperiment;

public static class SpoonSoftStirValidation
{
    const string Key = "Fluid.SoftStirRestore";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("SoftStirRestoreValidation").AddComponent<SpoonSoftStirRestoreRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

public sealed class SpoonSoftStirRestoreRunner : MonoBehaviour
{
    const string Root = "Assets/_Project/Features/Bartending/FluidGpuExperiment";
    readonly List<string> report = new List<string>(), errors = new List<string>();
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentBody spoon, glass;
    void Check(bool value, string label)
    { report.Add((value ? "PASS " : "FAIL ") + label); if (!value) throw new Exception(label); }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    FluidExperimentBody Spawn(string name, Vector2 position)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + name + ".prefab");
        var body = Instantiate(prefab, world.transform).GetComponent<FluidExperimentBody>();
        body.Teleport(position, 0); body.Body.bodyType = RigidbodyType2D.Kinematic; body.pourMlPerSecond = 0;
        Physics2D.SyncTransforms(); return body;
    }
    void Pair(Vector2 center, uint owner, ItemDef a, ItemDef b)
    {
        if (!gpu.TryEmit(center + Vector2.left * .18f, Vector2.zero, a, 1, owner)
            || !gpu.TryEmit(center + Vector2.right * .18f, Vector2.zero, b, 3, owner))
            throw new Exception("Two-ingredient fixture emission rejected");
    }
    double Variance(uint owner)
    {
        double variance = 0, volume = gpu.VolumeIn(owner);
        for (int ingredient = 0; ingredient < gpu.settings.gpuLiquidMaximumIngredients; ingredient++)
        {
            double mean = 0;
            for (int i = 0; i < gpu.Snapshot.Length; i++)
                if (gpu.Snapshot[i].Active != 0 && gpu.Snapshot[i].VesselId == owner)
                    mean += gpu.Snapshot[i].VolumeMl * gpu.CompositionSnapshot[i * gpu.settings.gpuLiquidMaximumIngredients + ingredient] / volume;
            for (int i = 0; i < gpu.Snapshot.Length; i++)
                if (gpu.Snapshot[i].Active != 0 && gpu.Snapshot[i].VesselId == owner)
                {
                    double difference = gpu.CompositionSnapshot[i * gpu.settings.gpuLiquidMaximumIngredients + ingredient] - mean;
                    variance += gpu.Snapshot[i].VolumeMl * difference * difference / volume;
                }
        }
        return variance;
    }
    void Step(Vector2 position, bool right = true)
    {
        spoon.SetSpoonContactEnabled(right); spoon.SetHeldPose(position, 0);
        world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms(); Physics2D.Simulate(.02f);
        world.TickLiquid(.02f); gpu.ReadbackNow();
    }
    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null; yield return null;
        var previousMode = Physics2D.simulationMode; var previousGravity = Physics2D.gravity;
        bool success = false;
        try
        {
            var comparison = FindFirstObjectByType<FluidExperimentComparison>();
            comparison.automaticScenario = false; comparison.ResetComparison();
            world = comparison.World; gpu = world.Liquid;
            world.enabled = false; world.interactor.enabled = false; gpu.automaticReadback = false;
            world.Cocktails.showControls = false;
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = Vector2.zero;
            foreach (var body in world.Items.ToArray()) body.gameObject.SetActive(false);
            gpu.ResetSimulation();
            glass = Spawn("Glass_highball", new Vector2(0, 2)); glass.SetSealed(true);
            var other = Spawn("Glass_highball", new Vector2(6, 2)); other.SetSealed(true);
            spoon = Spawn("Spoon", Vector2.zero);
            var ingredients = world.Cocktails.ItemCatalog.ItemsById.Values.Take(2).ToArray();
            Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
            Pair(center, glass.Id, ingredients[0], ingredients[1]);
            Pair(other.LocalToWorld(other.collisionProfile.InteriorBounds.center), other.Id, ingredients[0], ingredients[1]);
            Pair(new Vector2(-6, 3), 0, ingredients[0], ingredients[1]);
            gpu.Step(.0001f); gpu.ReadbackNow();
            Check(Math.Abs(gpu.VolumeIn(glass.Id) - 4) < .0001 && Math.Abs(gpu.VolumeIn(other.Id) - 4) < .0001
                && Math.Abs(gpu.VolumeIn(0) - 4) < .0001, "Fixture has separate 1ml/3ml ingredient pairs in target, foreign vessel and free liquid");
            Vector2 tip = spoon.collisionProfile.solids.SelectMany(h => h.points)
                .OrderBy(p => p.y * spoon.transform.lossyScale.y).First();
            Vector2 home = center - spoon.PointAt(tip, Vector2.zero, 0) - Vector2.up * .2f;
            spoon.Teleport(home, 0); spoon.SetHeld(true); Step(home);
            Check(spoon.SpoonLiquidVessel == glass && !spoon.SpoonWaitingForClearance, "Spoon enters target cavity without a wall overlap");
            for (int i = 0; i < 5; i++) Step(home);
            Check(gpu.SpoonMixSources == 0 && gpu.SpoonStirSources == 0, "An initially stationary wet spoon starts neither mixing nor flow");
            double before = Variance(glass.Id), foreignBefore = Variance(other.Id), freeBefore = Variance(0);
            var vesselPose = (glass.Position, glass.Angle, glass.Body.linearVelocity, glass.Body.angularVelocity);
            float peakSpeed = 0, peakTarget = 0; int activeTicks = 0; Vector2 last = home;
            for (int i = 1; i <= 90; i++)
            {
                last = home + Vector2.right * (.035f * Mathf.Sin(i * .6f)); Step(last);
                if (gpu.SpoonMixSources > 0) activeTicks++;
                peakTarget = Mathf.Max(peakTarget, gpu.MaximumSpoonFlowTargetSpeed);
                foreach (var particle in gpu.Snapshot)
                    if (particle.Active != 0 && particle.VesselId == glass.Id) peakSpeed = Mathf.Max(peakSpeed, particle.Velocity.magnitude);
            }
            double after = Variance(glass.Id);
            report.Add("MEASURE varianceBefore=" + before + " varianceAfter=" + after + " activeTicks=" + activeTicks
                + " peakTarget=" + peakTarget + " sparseFixturePeakSpeed=" + peakSpeed);
            Check(activeTicks > 50 && after < before * .25, "Moving the wet spoon homogenizes the two-ingredient target mixture");
            Check(peakTarget > .001f && peakTarget <= .1601f && peakSpeed > .0001f && peakSpeed < .25f,
                "Sparse zero-gravity fixture receives a small nonzero flow without a solid spoon impulse");
            foreach (uint owner in new[] { glass.Id, other.Id, 0u })
                Check(Math.Abs(gpu.IngredientVolumeIn(owner, ingredients[0]) - 1) < .001
                    && Math.Abs(gpu.IngredientVolumeIn(owner, ingredients[1]) - 3) < .001,
                    "Each ingredient's ml is conserved for owner " + owner);
            Check(Math.Abs(Variance(other.Id) - foreignBefore) < .0001 && Math.Abs(Variance(0) - freeBefore) < .0001,
                "Spoon mixing leaves isolated foreign-vessel and unowned-liquid composition unchanged");
            Check(vesselPose.Equals((glass.Position, glass.Angle, glass.Body.linearVelocity, glass.Body.angularVelocity)),
                "Stirring leaves target vessel pose and velocities unchanged");
            Step(last); Check(gpu.SpoonMixSources == 0, "Stopping spoon motion immediately stops added mixture homogenization");
            for (int i = 0; i < 20; i++) Step(last);
            Check(gpu.SpoonStirSources == 0, "The short settling flow expires while spoon remains wet");
            Step(last + Vector2.right * .01f); Step(spoon.Position, false);
            Check(gpu.SpoonMixSources == 0 && gpu.SpoonStirSources == 0 && spoon.SpoonLiquidVessel == null,
                "RMB release clears the vessel and both stirring sources");
            gpu.ResetSimulation();
            Check(gpu.SpoonMixSources == 0 && gpu.SpoonStirSources == 0, "Simulation reset clears stirring state");
            Check(errors.Count == 0, "No Unity, shader, buffer or application errors in soft stirring validation");
            success = true;
        }
        catch (Exception error) { report.Add(error.ToString()); }
        finally
        {
            Physics2D.simulationMode = previousMode; Physics2D.gravity = previousGravity;
            Application.logMessageReceived -= Log; report.AddRange(errors);
            string evidence = Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
            File.WriteAllLines(Path.Combine(evidence, "soft-stir-validation.txt"), report);
            File.WriteAllText(Path.Combine(evidence, "soft-stir-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
