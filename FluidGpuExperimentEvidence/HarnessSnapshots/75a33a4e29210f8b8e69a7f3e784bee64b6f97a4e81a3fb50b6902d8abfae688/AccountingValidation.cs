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

// Evidence-only GPU integration checks. Imported only by prepare_experiment.py into its isolated
// harness project. Execute ExperimentAccountingValidation.Begin; never attached to the game.
public static class ExperimentAccountingValidation
{
    private const string Key = "Slainte.FluidGpuExperiment.AccountingValidation";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR")
        ?? "FluidGpuExperimentEvidence/accounting-manual";
    [InitializeOnLoadMethod]
    private static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("AccountingValidationRunner").AddComponent<ExperimentAccountingValidationRunner>();
    };
    public static void Begin()
    {
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Path.Combine(Evidence, "accounting-result.txt"), "STARTED; no result yet\n");
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    public static void Finish(bool success, string report)
    {
        File.WriteAllText(Path.Combine(Evidence, "accounting-validation.txt"), report);
        File.WriteAllText(Path.Combine(Evidence, "accounting-result.txt"), success ? "PASS\n" : "FAIL\n");
        if (success) Debug.Log(report); else Debug.LogError(report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ExperimentAccountingValidationRunner : MonoBehaviour
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<string> report = new List<string>();
    private FluidExperimentComparison comparison;
    private FluidExperimentGpuLiquid gpu;
    private FluidExperimentBody vessel;
    private ItemDef[] ingredients;
    private int checks;

    private IEnumerator Start()
    {
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        float start = Time.realtimeSinceStartup;
        while (comparison != null && !comparison.Ready && Time.realtimeSinceStartup - start < 20) yield return null;
        int expectedRevision = 0, expectedGeneration = 0;
        bool ok = true;
        try
        {
            Check(comparison != null && comparison.Ready, "comparison ready");
            comparison.automaticScenario = false; comparison.enabled = false; comparison.World.enabled = false;
            comparison.SwitchMode(FluidExperimentMode.ECalibratedLiquid);
            comparison.ResetComparison(); comparison.World.interactor.enabled = false;
            gpu = comparison.Gpu; gpu.automaticReadback = false;
            ingredients = comparison.World.Items.Where(x => x.kind == LabItemKind.Bottle && x.ingredient != null)
                .Select(x => x.ingredient).Distinct().Take(3).ToArray();
            Check(ingredients.Length >= 2, "at least two authored ingredients");
            vessel = comparison.World.Items.First(x => x.kind == LabItemKind.Glass && x.name.Contains("highball"));
            foreach (var body in comparison.World.Items) { body.SetHeld(true); body.Teleport(new Vector2(-18, 10), 0); }
            vessel.Teleport(Vector2.zero, 0); Physics2D.SyncTransforms();
            RunQueueChecks(); RunBirthReservationCheck(); RunIsolatedMixChecks(); RunMixAndTransferChecks(); RunSlotReuseChecks();

            // Queue an async generation, replace it synchronously, then allow old callbacks to run.
            gpu.ResetSimulation(); Emit(.5f, 0); gpu.Step(.02f);
            if (SystemInfo.supportsAsyncGPUReadback)
                typeof(FluidExperimentGpuLiquid).GetMethod("RequestReadback", Hidden).Invoke(gpu, null);
            gpu.ResetSimulation(); Emit(.125f, 1); gpu.Step(.02f); gpu.ReadbackNow();
            expectedRevision = gpu.SnapshotRevision; expectedGeneration = gpu.Ledger.Generation;
        }
        catch (Exception ex) { ok = false; report.Add(ex.ToString()); }
        if (ok)
        {
            yield return null; yield return null;
            try
            {
                Check(gpu.SnapshotRevision == expectedRevision && gpu.Ledger.Generation == expectedGeneration,
                    "old asynchronous generation cannot overwrite the new snapshot");
                Close(gpu.Ledger.Total.GeneratedMl, .125, "new generation retained after async callback");
                CheckAccounting("new generation");
            }
            catch (Exception ex) { ok = false; report.Add(ex.ToString()); }
        }
        report.Insert(0, (ok ? "PASS " : "FAIL ") + checks + " checks; GPU=" + SystemInfo.graphicsDeviceName);
        ExperimentAccountingValidation.Finish(ok, string.Join("\n", report));
    }

    private Vector2 Center => vessel.LocalToWorld(vessel.collisionProfile.InteriorBounds.center);
    private void Emit(float amount, int ingredient)
        => Check(gpu.TryEmit(Center, Vector2.zero, ingredients[ingredient % ingredients.Length], amount, vessel.Id), "queued " + amount + " ml");
    private void Check(bool condition, string label)
    {
        checks++; if (!condition) throw new Exception(label); report.Add("PASS " + label);
    }
    private void Close(double actual, double expected, string label, double tolerance = .003)
        => Check(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) <= tolerance,
            label + ": " + actual.ToString("G10") + " expected " + expected.ToString("G10"));
    private void CheckAccounting(string label)
    {
        var ledger = gpu.Ledger;
        Check(ledger != null && ledger.Revision == gpu.SnapshotRevision, label + " completed snapshot revision");
        foreach (var entry in ledger.Ingredients.Concat(new[] { ledger.Total }))
        {
            string item = entry.Ingredient != null ? entry.Ingredient.name : "TOTAL";
            Close(entry.ConservationErrorMl, 0, label + " " + item + " generation=active+pending+retired+transferred");
            Close(entry.QueueErrorMl, 0, label + " " + item + " queued=pending+generated+GPU rejected");
            Close(entry.RequestedMl, entry.QueuedMl + entry.CpuRejectedMl, label + " " + item + " requested=queued+CPU rejected");
        }
    }
    private void RunQueueChecks()
    {
        gpu.ResetSimulation(); Emit(.375f, 0); gpu.ReadbackNow();
        Close(gpu.Ledger.Total.GeneratedMl, 0, "CPU queue is not generated");
        Close(gpu.Ledger.Total.CpuPendingMl, .375, "CPU pending request is visible");
        CheckAccounting("CPU pending");
        gpu.Step(.02f); gpu.ReadbackNow();
        Close(gpu.Ledger.Total.GeneratedMl, .375, "actual GPU spawn commits generated amount");
        Close(gpu.Ledger.Total.CpuPendingMl, 0, "queue drained");
        CheckAccounting("generated");

        gpu.ResetSimulation(); Emit(.375f, 0);
        // Force the actual GPU queue rejection branch, independently of the CPU admission estimate.
        ((GraphicsBuffer)typeof(FluidExperimentGpuLiquid).GetField("freeCountBuffer", Hidden).GetValue(gpu)).SetData(new[] { 0 });
        gpu.Step(.02f); gpu.ReadbackNow();
        Close(gpu.Ledger.Total.GpuRejectedMl, .375, "actual GPU queue rejection accounted");
        Close(gpu.Ledger.Total.GeneratedMl, 0, "rejected command never generated");
        CheckAccounting("GPU rejection");
        gpu.ResetSimulation();
        typeof(FluidExperimentGpuLiquid).GetField("availableSlots", Hidden).SetValue(gpu, 0);
        Check(!gpu.TryEmit(Center, Vector2.zero, ingredients[0], .125f, vessel.Id), "CPU capacity rejection");
        gpu.ReadbackNow(); Close(gpu.Ledger.Total.CpuRejectedMl, .125, "CPU rejected amount");
        CheckAccounting("CPU rejection");
    }
    private void RunMixAndTransferChecks()
    {
        gpu.ResetSimulation();
        for (int i = 0; i < 12; i++) Emit(i % 2 == 0 ? .125f : .375f, i);
        gpu.Step(.02f); gpu.ReadbackNow();
        var expected = gpu.Ledger.Ingredients.ToDictionary(x => x.Ingredient, x => x.GeneratedMl);
        for (int step = 0; step < 600; step++) gpu.Step(.02f);
        gpu.ReadbackNow(); CheckAccounting("600-step unequal-volume mixture");
        foreach (var ingredient in expected)
        {
            var entry = gpu.Ledger.Ingredients.First(x => x.Ingredient == ingredient.Key);
            Close(entry.ActiveMl + entry.RetiredMl, ingredient.Value, ingredient.Key.name + " ingredient conserved after mixing");
            Close(gpu.IngredientVolumeIn(vessel.Id, ingredient.Key), entry.ActiveMl, ingredient.Key.name + " vessel adapter consistent tick");
        }
        int staleRevision = gpu.SnapshotRevision, generation = gpu.Ledger.Generation;
        gpu.Step(.02f);
        Check(!gpu.TryTransferContents(vessel.Id, generation, staleRevision, out _), "reject stale snapshot after physics step");
        gpu.ReadbackNow(); int freshRevision = gpu.SnapshotRevision;
        Check(!gpu.TryTransferContents(vessel.Id, generation - 1, freshRevision, out _), "reject wrong generation");
        double before = gpu.VolumeIn(vessel.Id), retired = gpu.Ledger.Total.RetiredMl;
        Check(before > 0, "test retains contents before delivery");
        Check(gpu.TryTransferContents(vessel.Id, generation, freshRevision, out var receipt), "delivery succeeds from current snapshot");
        Check(receipt != null && !string.IsNullOrEmpty(receipt.Id), "delivery has unique receipt ID");
        Close(receipt.TotalMl, before, "receipt total equals consumed contents");
        Close(receipt.Ingredients.Sum(x => x.Value), before, "receipt ingredients equal consumed contents");
        Close(gpu.Ledger.Total.TransferredMl, before, "delivery has separate transferred ledger");
        Close(gpu.Ledger.Total.RetiredMl, retired, "delivery is not a spill");
        Check(!gpu.TryTransferContents(vessel.Id, generation, freshRevision, out _), "repeated receipt source cannot debit twice");
        Check(!gpu.TryTransferContents(vessel.Id, gpu.SnapshotRevision, out _), "empty vessel cannot produce second receipt");
        CheckAccounting("delivery");
        gpu.ResetSimulation(); Emit(.125f, 0); gpu.Step(.02f); gpu.ReadbackNow();
        Check(!gpu.TryTransferContents(vessel.Id, generation, gpu.SnapshotRevision, out _), "pre-reset generation cannot consume new contents");
        int revision = gpu.SnapshotRevision; Emit(.125f, 0);
        Check(!gpu.TryTransferContents(vessel.Id, revision, out _), "target's queued births block incomplete transfer");
    }
    private void RunBirthReservationCheck()
    {
        gpu.ResetSimulation(); Emit(.125f, 0); gpu.Step(.02f); gpu.ReadbackNow();
        // Observe the valid reserved/unborn GPU state directly, before the next integration kernel.
        // This is the state that an asynchronous admission estimate must count even with Active=0.
        var particles = gpu.Snapshot.ToArray(); var streams = gpu.ReadStreamParticles();
        int index = Array.FindIndex(particles, p => p.Active != 0);
        particles[index].Active = 0; streams[index].Pending = 1; streams[index].Delay = .1f;
        Buffer("particleBuffer").SetData(particles); Buffer("streamParticleBuffer").SetData(streams);
        gpu.ReadbackNow(); Close(gpu.Ledger.Total.GpuPendingMl, .125, "GPU unborn quantity visible");
        Vector2 outside = gpu.settings.gpuLiquidWorldMin - Vector2.one * 2;
        for (int i = 0; i < particles.Length - 1; i++)
            Check(gpu.TryEmit(outside, Vector2.zero, ingredients[1], .125f, 0), "remaining actual free slot admitted");
        Check(!gpu.TryEmit(outside, Vector2.zero, ingredients[1], .125f, 0), "unborn slot is unavailable to CPU admission");
        gpu.Step(.02f); gpu.ReadbackNow();
        Close(gpu.Ledger.Total.GpuRejectedMl, 0, "all admitted commands accepted despite delayed birth");
        CheckAccounting("delayed birth plus full queued capacity");
    }
    private GraphicsBuffer Buffer(string name) =>
        (GraphicsBuffer)typeof(FluidExperimentGpuLiquid).GetField(name, Hidden).GetValue(gpu);
    private void MixOnly(int iterations)
    {
        typeof(FluidExperimentGpuLiquid).GetMethod("RebuildGrid", Hidden).Invoke(gpu, null);
        MethodInfo mix = typeof(FluidExperimentGpuLiquid).GetMethod("DispatchMix", Hidden);
        for (int i = 0; i < iterations; i++) mix.Invoke(gpu, null);
        gpu.ReadbackNow();
    }
    private void RunIsolatedMixChecks()
    {
        gpu.ResetSimulation(); Emit(.125f, 0); Emit(.375f, 1); Emit(.5f, 0); gpu.Step(.02f); gpu.ReadbackNow();
        var particles = gpu.Snapshot.ToArray(); var streams = gpu.ReadStreamParticles();
        int[] active = Enumerable.Range(0, particles.Length).Where(i => particles[i].Active != 0).ToArray();
        Check(active.Length == 3, "isolated mix particles spawned");
        for (int i = 0; i < active.Length; i++)
        {
            int slot = active[i]; particles[slot].Position = Center; particles[slot].Velocity = Vector2.zero;
            particles[slot].VesselId = i == 0 ? vessel.Id : i == 1 ? vessel.Id + 1000 : 0;
            streams[slot].StepDt = .01f;
        }
        Buffer("particleBuffer").SetData(particles); Buffer("streamParticleBuffer").SetData(streams);
        float[] before = gpu.CompositionSnapshot.ToArray();
        MixOnly(100);
        Check(before.SequenceEqual(gpu.CompositionSnapshot), "two vessel owners and owned/free neighbors never exchange across ownership");
        CheckAccounting("ownership-isolated mix");

        // Same owner with unequal birth-clipped lifetimes: the frozen newborn endpoint cannot
        // exchange in either direction. A positive shorter lifetime then conserves ingredient ml.
        foreach (int slot in active) particles[slot].VesselId = vessel.Id;
        streams[active[0]].StepDt = 0;
        streams[active[1]].StepDt = .0004f; streams[active[2]].StepDt = .01f;
        Buffer("particleBuffer").SetData(particles); Buffer("streamParticleBuffer").SetData(streams);
        MixOnly(100);
        for (int k = 0; k < gpu.IngredientStride; k++)
            Close(gpu.CompositionSnapshot[active[0] * gpu.IngredientStride + k], before[active[0] * gpu.IngredientStride + k],
                "zero-dt birth endpoint unchanged", .000001);
        CheckAccounting("shared pair time across unequal volumes");
    }
    private void RunSlotReuseChecks()
    {
        gpu.ResetSimulation();
        Vector2 outside = gpu.settings.gpuLiquidWorldMin - Vector2.one * 2;
        double expected = 0;
        for (int cycle = 0; cycle < 128; cycle++)
        {
            for (int i = 0; i < 32; i++)
            {
                float amount = (i % 4 + 1) * .125f; expected += amount;
                Check(gpu.TryEmit(outside, Vector2.down, ingredients[i % ingredients.Length], amount, 0), "reuse spawn");
            }
            gpu.Step(.02f); gpu.ReadbackNow();
            Close(gpu.Ledger.Total.ActiveMl, 0, "out-of-bounds particles retired");
            Close(gpu.Ledger.Total.RetiredMl, expected, "cumulative retirement survives slot reuse");
            if (cycle % 16 == 0) CheckAccounting("recycle " + cycle);
        }
        CheckAccounting("4096 emitted particles across reused slots");
    }
}
