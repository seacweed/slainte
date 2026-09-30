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

public static class ExperimentPouringValidation
{
    private const string Key = "Slainte.FluidGpuExperiment.PouringValidation";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR")
        ?? "FluidGpuExperimentEvidence/pouring-manual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }
    public static void Begin()
    {
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Path.Combine(Evidence, "pouring-validation.txt"), "STARTED; no result yet.\n");
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    private static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("ExperimentPouringValidationRunner").AddComponent<ExperimentPouringValidationRunner>().Begin();
    }
    public static void Finish(bool success, string report)
    {
        File.WriteAllText(Path.Combine(Evidence, "pouring-validation.txt"), report);
        File.WriteAllText(Path.Combine(Evidence, "pouring-result.txt"), success ? "PASS\n" : "FAIL\n");
        if (success) Debug.Log("[ExperimentPouringValidation] PASS\n" + report);
        else Debug.LogError("[ExperimentPouringValidation] FAIL\n" + report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ExperimentPouringValidationRunner : MonoBehaviour
{
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private bool finished;
    private float started;
    public void Begin()
    {
        started = Time.realtimeSinceStartup;
        Application.logMessageReceived += OnLog;
        StartCoroutine(Guard());
    }
    private void OnLog(string message, string stack, LogType type)
    {
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            errors.Add(message + "\n" + stack);
    }
    private IEnumerator Guard()
    {
        var pending = new Stack<IEnumerator>(); pending.Push(Run());
        while (pending.Count > 0)
        {
            bool more; object next = null;
            try
            {
                if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
                if (Time.realtimeSinceStartup - started > 190) throw new Exception("Pour/effects validation exceeded 190 seconds.");
                more = pending.Peek().MoveNext(); if (more) next = pending.Peek().Current;
            }
            catch (Exception error)
            {
                while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
                Finish(false, error.ToString()); yield break;
            }
            if (!more) { (pending.Pop() as IDisposable)?.Dispose(); continue; }
            if (next is IEnumerator nested) { pending.Push(nested); continue; }
            yield return next;
        }
        Finish(errors.Count == 0, "Finite-reservoir/material/effects validation completed.");
    }
    private IEnumerator Run()
    {
        yield return null;
        var comparison = FindFirstObjectByType<FluidExperimentComparison>();
        Require(comparison != null, "Authored comparison scene exists");
        while (!comparison.Ready) yield return null;
        comparison.automaticScenario = false; comparison.showControls = false;
        comparison.SwitchMode(FluidExperimentMode.ECalibratedLiquid);
        comparison.StartScenario(FluidExperimentScenario.Manual);
        comparison.enabled = false;
        var effects = FindFirstObjectByType<FluidExperimentEffects>();
        if (effects != null) effects.enabled = false;
        yield return ExperimentPouringChecks.Run(comparison.World, Require);
        yield return ExperimentEffectsChecks.Run(comparison.World, Require);
    }
    private void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        report.Add("PASS " + message);
    }
    private void Finish(bool success, string message)
    {
        finished = true; Application.logMessageReceived -= OnLog;
        report.Add(message); ExperimentPouringValidation.Finish(success, string.Join("\n", report));
    }
}

// Called by the unified disposable-project harness; never imported into the game.
public static class ExperimentPouringChecks
{
    private const float Dt = .02f;

    public static IEnumerator Run(FluidExperimentWorld world, Action<bool, string> require)
    {
        ValidateModel(require);
        FluidExperimentGpuLiquid gpu = world.Liquid;
        FluidExperimentBody bottle = world.Items.FirstOrDefault(x => x.kind == LabItemKind.Bottle && x.name.Contains("1002"))
            ?? world.Items.First(x => x.kind == LabItemKind.Bottle);
        float originalVolume = gpu.settings.gpuLiquidParticleVolumeMl;
        float originalCapacity = bottle.capacityMl;
        bool originalImproved = gpu.useImprovedPhysics;
        FluidExperimentMaterial originalGlobalMaterial = gpu.improvedMaterial;
        try
        {
            world.enabled = false;
            world.interactor.enabled = false;
            gpu.automaticReadback = false;
            foreach (FluidExperimentBody body in world.Items)
            {
                body.SetHeld(true);
                body.Teleport(new Vector2(-18, 10), 0);
                body.pourMlPerSecond = 0;
                body.Body.simulated = false;
            }
            gpu.useImprovedPhysics = true;
            gpu.improvedMaterial = FluidExperimentMaterial.Auto;
            gpu.Dispose(); gpu.Initialize(world);
            require(gpu.IsOperational, "E reservoir validation initializes the actual GPU");

            bottle.pourMlPerSecond = 20;
            require(bottle.ImprovedPourPreset.ViscosityRate == FluidExperimentMaterials.Resolve(bottle.ingredient).ViscosityRate,
                "E Auto emitter material follows ingredient metadata through the shared resolver");
            gpu.improvedMaterial = FluidExperimentMaterial.Water;
            float waterFlow = bottle.ImprovedPourPreset.FlowMultiplier;
            gpu.improvedMaterial = FluidExperimentMaterial.Syrup;
            require(bottle.ImprovedPourPreset.FlowMultiplier < waterFlow,
                "Global E material drives the bottle emitter consistently with GPU physics");
            gpu.improvedMaterial = FluidExperimentMaterial.Water;
            float firstFull = 181, firstLow = 181;
            for (int angle = 0; angle <= 180; angle += 5)
            {
                bottle.ResetSupply(originalCapacity * .9f, 0);
                if (bottle.ReservoirTargetFlow(angle) > .01f && firstFull > 180) firstFull = angle;
                bottle.ResetSupply(originalCapacity * .1f, 0);
                if (bottle.ReservoirTargetFlow(angle) > .01f && firstLow > 180) firstLow = angle;
            }
            require(firstFull < firstLow && firstLow <= 180,
                "E actual bottle starts pouring at a lower tilt when fuller (" + firstFull + " vs " + firstLow + " degrees)");
            bottle.ResetSupply(originalCapacity, 0);
            require(bottle.ReservoirTargetFlow(0) == 0, "E upright full bottle does not pour");

            float[] totals = new float[2];
            float[] targets = new float[2];
            for (int resolution = 0; resolution < 2; resolution++)
            {
                gpu.settings.gpuLiquidParticleVolumeMl = resolution == 0 ? .5f : .25f;
                gpu.Dispose(); gpu.Initialize(world);
                Setup(bottle, gpu, originalCapacity * .8f, 20);
                targets[resolution] = bottle.ReservoirTargetFlow(180);
                Tick(world);
                require(gpu.EmittedMl == 0 && bottle.ReservoirCurrentFlowMlPerSecond > 0
                    && bottle.ReservoirCurrentFlowMlPerSecond < targets[resolution],
                    "E neck response delays the first discrete birth at " + gpu.ParticleVolumeMl + "ml resolution");
                for (int step = 1; step < 100; step++)
                {
                    Tick(world);
                    if (step % 20 == 0) yield return null;
                }
                float beforeStop = gpu.EmittedMl;
                require(beforeStop > 5 && Mathf.Abs(originalCapacity * .8f - bottle.remainingMl - beforeStop) < .002f,
                    "E inverted flow debits exactly accepted source ml");
                bottle.SetHeldPose(bottle.Position, 0);
                Tick(world);
                float afterTurn = gpu.EmittedMl;
                float beforeQuiet = 0;
                for (int step = 0; step < 100; step++)
                {
                    if (step == 90) beforeQuiet = gpu.EmittedMl;
                    Tick(world);
                    if (step % 20 == 0) yield return null;
                }
                float emitted = gpu.EmittedMl;
                require(emitted > beforeStop && emitted - afterTurn <= bottle.reservoirDripMl + gpu.ParticleVolumeMl + .02f,
                    "E stopping yields a bounded physical last-droplet volume");
                require(Mathf.Abs(emitted - beforeQuiet) < .00001f && bottle.ReservoirCurrentFlowMlPerSecond == 0,
                    "E upright bottle becomes completely quiet after the finite tail");
                require(Mathf.Abs(originalCapacity * .8f - bottle.remainingMl - emitted) < .002f,
                    "E final droplets preserve accepted stock accounting");
                gpu.ReadbackNow();
                var stream = gpu.ReadStreamParticles();
                require(stream.Any(s => s.Token != 0 && s.SourceId == bottle.Id)
                    && stream.Where(s => s.Token != 0 && s.SourceId == bottle.Id).All(s =>
                        float.IsFinite(s.BirthTime) && s.BirthTime >= 0 && s.BirthTime <= 4.1f),
                    "E emitted particles retain genuine source and bounded birth-time metadata");
                totals[resolution] = emitted;
            }
            require(Mathf.Abs(targets[0] - targets[1]) < .00001f && Mathf.Abs(totals[0] - totals[1]) <= .75f,
                "E flow target is resolution-independent; completed .5/.25ml runs agree within quantization ("
                + totals[0] + " vs " + totals[1] + "ml)");

            bottle.capacityMl = .37f;
            Setup(bottle, gpu, .37f, 500);
            for (int step = 0; step < 100; step++) { Tick(world); if (step % 25 == 0) yield return null; }
            require(Mathf.Abs(gpu.EmittedMl - .37f) < .00001f && bottle.remainingMl == 0,
                "E finite reservoir emits the fractional last stock exactly once and cannot overdraw");
            bottle.capacityMl = originalCapacity;

            Setup(bottle, gpu, originalCapacity, 500);
            int reserved = 0;
            while (gpu.TryEmit(new Vector2(-10, 8), Vector2.zero, bottle.ingredient, .5f, 0)) reserved++;
            require(reserved >= 64, "Reservation rejection fixture actually fills the particle reservation budget");
            float stock = bottle.remainingMl, alreadyQueued = gpu.EmittedMl;
            typeof(FluidExperimentBody).GetMethod("Emit", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(bottle, new object[] { .2f });
            require(bottle.remainingMl == stock && gpu.EmittedMl == alreadyQueued
                && bottle.ReservoirPendingMl == 0 && bottle.ReservoirCurrentFlowMlPerSecond == 0,
                "E rejected spawn debits no stock and clears deferred emission pressure/credit");
            gpu.ResetSimulation();
            bottle.pourMlPerSecond = 20;
            Tick(world);
            require(gpu.EmittedMl == 0, "E capacity recovery cannot release a deferred burst");
        }
        finally
        {
            bottle.capacityMl = originalCapacity;
            gpu.improvedMaterial = originalGlobalMaterial;
            gpu.settings.gpuLiquidParticleVolumeMl = originalVolume;
            gpu.useImprovedPhysics = originalImproved;
            gpu.Dispose(); gpu.Initialize(world);
        }
    }

    private static void Setup(FluidExperimentBody bottle, FluidExperimentGpuLiquid gpu, float volume, float flow)
    {
        gpu.ResetSimulation();
        bottle.Teleport(new Vector2(0, 5), 180);
        bottle.ResetSupply(volume, 0);
        bottle.pourMlPerSecond = flow;
    }
    private static void Tick(FluidExperimentWorld world)
    {
        world.SendMessage("FixedUpdate");
        world.TickLiquid(Dt);
    }
    public static void ValidateModel(Action<bool, string> require)
    {
        Vector2[] rectangle = { new Vector2(-1, -2), new Vector2(1, -2), new Vector2(1, 2), new Vector2(-1, 2) };
        require(Mathf.Abs(FluidExperimentReservoirModel.SurfaceHeight(rectangle, .25f, out float height) + 1) < .00001f
            && height == 4, "Reservoir model solves a known quarter-full rectangle free surface");
        float whole = FluidExperimentReservoirModel.Integral(0, 20, .2f, .12f);
        float split = FluidExperimentReservoirModel.Integral(0, 20, .1f, .12f)
            + FluidExperimentReservoirModel.Integral(FluidExperimentReservoirModel.Rate(0, 20, .1f, .12f), 20, .1f, .12f);
        require(Mathf.Abs(whole - split) < .00001f, "Analytic response volume is invariant to emitter time subdivision");
        float birth = FluidExperimentReservoirModel.BirthTime(0, 20, .2f, .12f, .5f);
        require(birth > 0 && birth < .2f
            && Mathf.Abs(FluidExperimentReservoirModel.Integral(0, 20, birth, .12f) - .5f) < .00001f,
            "Analytic inverse places each birth at its integrated-volume crossing");
        var item = ScriptableObject.CreateInstance<ItemDef>();
        try
        {
            item.bottleCategory = BottleCategory.Syrup;
            var syrup = FluidExperimentMaterials.Resolve(item);
            var water = FluidExperimentMaterials.Resolve(item, FluidExperimentMaterial.Water);
            var milk = FluidExperimentMaterials.Resolve(item, FluidExperimentMaterial.Milk);
            require(syrup.FlowMultiplier < water.FlowMultiplier && syrup.ViscosityRate > water.ViscosityRate
                && milk.FoamLifetime > water.FoamLifetime,
                "Material metadata/explicit overrides select distinct syrup and milk responses");
        }
        finally { UnityEngine.Object.Destroy(item); }
    }
}
