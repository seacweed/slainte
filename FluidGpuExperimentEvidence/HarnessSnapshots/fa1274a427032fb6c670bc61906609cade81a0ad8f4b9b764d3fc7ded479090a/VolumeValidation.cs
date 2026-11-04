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

// Evidence-only baseline: measures today's quantity/height/area behavior without
// treating desired volume calibration or complete settling as an existing feature.
public static class ExperimentVolumeValidation
{
    private const string Key = "Slainte.FluidGpuExperiment.VolumeValidation";
    private const string ScenePath = "Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR")
        ?? "FluidGpuExperimentEvidence/volume-manual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    public static void Begin() => Start(false);
    public static void BeginCalibrated() => Start(true);
    public static void BeginCapacityOnly() => Start(true, true);
    private static void Start(bool improved, bool capacityOnly = false)
    {
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Path.Combine(Evidence, "volume-validation.txt"), "STARTED; no result yet.\n");
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Key, true);
        SessionState.SetBool(Key + ".Improved", improved);
        SessionState.SetBool(Key + ".CapacityOnly", capacityOnly);
        EditorApplication.isPlaying = true;
    }

    private static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        var runner = new GameObject("ExperimentVolumeValidationRunner").AddComponent<ExperimentVolumeValidationRunner>();
        runner.ImprovedBenchmark = SessionState.GetBool(Key + ".Improved", false);
        runner.CapacityOnly = SessionState.GetBool(Key + ".CapacityOnly", false);
        SessionState.EraseBool(Key + ".Improved");
        SessionState.EraseBool(Key + ".CapacityOnly");
        runner.Begin();
    }

    public static void Finish(bool success, string report)
    {
        File.WriteAllText(Path.Combine(Evidence, "volume-validation.txt"), report);
        File.WriteAllText(Path.Combine(Evidence, "volume-result.txt"), success ? "PASS\n" : "FAIL\n");
        if (success) Debug.Log("[ExperimentVolumeValidation] PASS\n" + report);
        else Debug.LogError("[ExperimentVolumeValidation] FAIL\n" + report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ExperimentVolumeValidationRunner : MonoBehaviour
{
    public bool ImprovedBenchmark;
    public bool CapacityOnly;
    private const float Dt = .02f;
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private readonly List<string> trajectory = new List<string> {
        "case,mode,vessel,requested_ml,accepted_ml,time_s,active_count,owner_count,active_ml,owner_ml,free_ml,other_owner_ml,retired_ml,owned_inside_ml,p95_center_height,max_center_height,max_extent_height,owner_speed_rms" };
    private readonly List<string> summaries = new List<string> {
        "case,mode,vessel,requested_ml,accepted_ml,unseeded_ml,active_ml,owner_ml,free_ml,other_owner_ml,retired_ml,owned_inside_ml,p95_center_height,max_extent_height,tail_p95_height_range,tail_mean_speed_rms,unsettled_observation,surface_width,surface_height,source_opacity,visible_pixels_coverage005,visible_pixels_coverage025,raw_alpha_pixel_sum,normalized_coverage_pixel_sum,interior_pixels_coverage005,visible_area_world2,interior_area_world2,visible_edge_pixels,visual_interior_max_height,elapsed_wall_s" };
    private readonly List<string> capacitySamples = new List<string> {
        "case,stage,capacity_ml,queued_ml,generated_ml,cpu_pending_ml,gpu_pending_ml,active_ml,owned_ml,free_ml,other_owner_ml,retired_ml,transferred_ml,active_count,partial_count,conservation_error_ml" };
    private FluidExperimentComparison comparison;
    private FluidExperimentWorld world;
    private FluidExperimentGpuLiquid gpu;
    private Vector3 manualCameraPosition;
    private float manualCameraSize;
    private float started;
    private bool finished;
    private bool optionalScreenshot;

    private struct Sample
    {
        public int activeCount, ownerCount;
        public float activeMl, ownerMl, freeMl, otherMl, retiredMl, insideMl;
        public float p95, maxCenter, maxExtent, speedRms;
    }
    private struct Surface
    {
        public int width, height, pixels005, pixels025, interiorPixels, edgePixels;
        public double alphaSum, coverageSum;
        public float sourceOpacity, visibleArea, interiorArea, interiorMaxHeight;
    }

    public void Begin()
    {
        started = Time.realtimeSinceStartup;
        Application.logMessageReceived += OnLog;
        StartCoroutine(Guard());
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if (optionalScreenshot && (message.IndexOf("screenshot", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("ScreenCapture", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            report.Add("SCREENSHOT NOTE " + message);
            return;
        }
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            errors.Add(message + "\n" + stack);
    }

    private IEnumerator Guard()
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(Run());
        while (stack.Count > 0 && !finished)
        {
            object next = null;
            bool more;
            try
            {
                if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
                if (Time.realtimeSinceStartup - started > 210) throw new Exception("Volume baseline exceeded 210 seconds.");
                more = stack.Peek().MoveNext();
                if (more) next = stack.Peek().Current;
            }
            catch (Exception exception)
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                Finish(false, exception.ToString());
                yield break;
            }
            if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (next is IEnumerator nested) { stack.Push(nested); continue; }
            yield return next;
        }
        if (!finished) Finish(errors.Count == 0,
            "Measurement/accounting validation completed. Height, area, settling and rejected fill quantities are baseline observations, not calibration guarantees.");
    }

    private IEnumerator Run()
    {
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        Require(comparison != null, "Authored comparison controller exists");
        while (!comparison.Ready)
        {
            if (Time.realtimeSinceStartup - started > 20) throw new Exception("Comparison initialization timed out: " + comparison.Error);
            yield return null;
        }
        comparison.automaticScenario = false;
        comparison.showControls = false;
        comparison.StartScenario(FluidExperimentScenario.Manual);
        manualCameraPosition = comparison.World.interactor.inputCamera.transform.position;
        manualCameraSize = comparison.World.interactor.inputCamera.orthographicSize;
        if (CapacityOnly)
        {
            report.Add("Protocol: E Highball/Hurricane direct authored-capacity fill and 200ml settled-content topping with separate final .25ml; exact admission/conservation and finite state, plus at least97%owned retention after2s settling. No calibration or UI cases in this entrypoint.");
            yield return ValidateCalibratedCapacity();
            yield break;
        }
        report.Add(ImprovedBenchmark ? "Protocol: D highball and E highball/hurricane at 30/60/120 requested ml; 300 fixed .02s ticks, readback every .2s. E target height tolerance:20%; visible area tolerance:20%; generated quantity tolerance:.005ml."
            : "Protocol: 12 cases; A/B/C highball and C hurricane at 30/60/120 requested ml; 300 fixed .02s ticks, readback every .2s.");
        report.Add("Heights are world units above the authored interior's lowest point. P95 is volume-weighted owned-particle center height; max extent adds collision radius.");
        report.Add("Unsettled is an observation when the final 1s p95 range exceeds half a particle radius or mean owned-particle RMS speed exceeds .15 world units/s; these are provisional diagnostic thresholds.");
        report.Add("Surface area is actual premultiplied-alpha output before scene compositing, normalized by measured GPU particle opacity; coverage thresholds .05 and .25, raw alpha sum and normalized coverage sum are reported separately.");
        ValidateMetricsOracle();
        foreach (FluidExperimentMode mode in ImprovedBenchmark
            ? new[] { FluidExperimentMode.DImprovedSurface, FluidExperimentMode.ECalibratedLiquid }
            : new[] { FluidExperimentMode.ACurrent, FluidExperimentMode.BReferencePhysics, FluidExperimentMode.CReferenceSurface })
        {
            comparison.SwitchMode(mode);
            world = comparison.World;
            gpu = comparison.Gpu;
            Require(gpu.IsOperational, mode + ": GPU operational on " + SystemInfo.graphicsDeviceName);
            foreach (string name in mode == FluidExperimentMode.CReferenceSurface || mode == FluidExperimentMode.ECalibratedLiquid
                ? new[] { "highball", "hurricane" } : new[] { "highball" })
            {
                FluidExperimentBody vessel = world.Items.First(body => body.kind == LabItemKind.Glass && body.name.Contains(name));
                foreach (float requested in new[] { 30f, 60f, 120f })
                    yield return MeasureCase(mode, vessel, requested);
            }
        }
        if (ImprovedBenchmark) yield return ValidateCalibratedCapacity();
        yield return ValidateControlsFrame();
        ValidateFractionalFillAndReset();
        ValidateScenarioLifecycle();
    }

    private IEnumerator MeasureCase(FluidExperimentMode mode, FluidExperimentBody vessel, float requested)
    {
        string key = mode + "-" + vessel.name.Replace(' ', '_') + "-" + F(requested) + "ml";
        float caseStart = Time.realtimeSinceStartup;
        comparison.StartVolumeCheck(vessel, requested);
        FreezeCpu();
        float accepted = comparison.QueuedVolumeMl;
        Require(comparison.ActiveScenario == FluidExperimentScenario.VolumeCheck && comparison.VolumeVessel == vessel
            && Mathf.Abs(comparison.RequestedVolumeMl - requested) < .0001f,
            key + ": public measurement scenario preserves selection and requested amount");
        Require(accepted > 0 && accepted <= requested + .001f && Mathf.Abs(gpu.EmittedMl - accepted) < .001f,
            key + ": accepted Fill quantity is positive, bounded and agrees with GPU reservations");
        Require(!vessel.IsHeld && vessel.Body.bodyType == RigidbodyType2D.Kinematic,
            key + ": measured glass is stationary and unheld");
        gpu.ReadbackNow();
        Require(gpu.ActiveCount == 0 && gpu.Snapshot.All(p => p.VolumeMl == 0),
            key + ": reset clears prior slots while the new fill is still queued");
        float bottom = InteriorBottom(vessel);
        var tail = new List<Sample>();
        Sample last = default;
        for (int step = 1; step <= 300; step++)
        {
            comparison.AdvanceScenario(Dt);
            world.SendMessage("FixedUpdate");
            world.TickLiquid(Dt);
            if (step % 10 != 0) continue;
            last = ReadSample(vessel, accepted, bottom);
            trajectory.Add(string.Join(",", key, mode, vessel.name, F(requested), F(accepted), F(step * Dt),
                last.activeCount, last.ownerCount, F(last.activeMl), F(last.ownerMl), F(last.freeMl), F(last.otherMl),
                F(last.retiredMl), F(last.insideMl), F(last.p95), F(last.maxCenter), F(last.maxExtent), F(last.speedRms)));
            if (step >= 250) tail.Add(last);
            yield return null;
        }
        Require(tail.Count == 6, key + ": six independent tail observations cover the final second");
        float range = tail.Max(x => x.p95) - tail.Min(x => x.p95);
        float tailSpeed = tail.Average(x => x.speedRms);
        bool unsettled = tail.Any(x => !float.IsFinite(x.p95)) || range > gpu.Radius * .5f || tailSpeed > .15f;
        var measured = new FluidExperimentVolumeMetrics();
        measured.Measure(gpu, vessel, requested);
        Require(Mathf.Abs(measured.ActiveMl - last.activeMl) < .005f
            && Mathf.Abs(measured.ContainedMl - last.ownerMl) < .005f
            && Mathf.Abs(measured.OutsideMl - last.freeMl - last.otherMl) < .005f,
            key + ": runtime metrics agree with independent ownership accounting");
        if (last.ownerCount > 0)
            Require(Mathf.Abs(measured.ParticleHeight95 - last.p95 - gpu.Radius) < .0001f
                && Mathf.Abs(measured.ParticleHeightMax - last.maxExtent) < .0001f,
                key + ": runtime particle heights agree with independently sorted volume weights");
        Surface surface = Capture(vessel, key, bottom);
        if (mode == FluidExperimentMode.ECalibratedLiquid)
        {
            float expectedArea = requested * gpu.AreaPerMl(vessel.Id);
            float heightError = Mathf.Abs(measured.ParticleHeight95 - measured.CapacityReferenceHeight) / Mathf.Max(.001f, measured.CapacityReferenceHeight);
            float areaError = Mathf.Abs(surface.interiorArea - expectedArea) / Mathf.Max(.001f, expectedArea);
            report.Add("CALIBRATION " + key + ": expected height " + F(measured.CapacityReferenceHeight)
                + ", measured95 extent " + F(measured.ParticleHeight95) + ", height error " + F(heightError)
                + ", expected area " + F(expectedArea) + ", interior visible area " + F(surface.interiorArea) + ", area error " + F(areaError));
            // Always preserve measurements even if a calibration acceptance criterion fails.
            SaveTables();
            Require(Mathf.Abs(accepted - requested) < .005f && Mathf.Abs(last.ownerMl - requested) < .005f,
                key + ": calibrated fill generates and retains every requested ml");
            Require(heightError <= .2f, key + ": particle surface height is within20%of authored capacity reference");
            Require(areaError <= .2f, key + ": visible interior area is within20%of requested calibrated area");
        }
        summaries.Add(string.Join(",", key, mode, vessel.name, F(requested), F(accepted), F(requested - accepted),
            F(last.activeMl), F(last.ownerMl), F(last.freeMl), F(last.otherMl), F(last.retiredMl), F(last.insideMl),
            F(last.p95), F(last.maxExtent), F(range), F(tailSpeed), unsettled,
            surface.width, surface.height, F(surface.sourceOpacity), surface.pixels005, surface.pixels025,
            F(surface.alphaSum), F(surface.coverageSum),
            surface.interiorPixels, F(surface.visibleArea), F(surface.interiorArea), surface.edgePixels,
            F(surface.interiorMaxHeight), F(Time.realtimeSinceStartup - caseStart)));
        report.Add("OBSERVATION " + key + ": accepted " + F(accepted) + "/" + F(requested)
            + " ml, owned " + F(last.ownerMl) + " ml, free " + F(last.freeMl) + " ml, retired " + F(last.retiredMl)
            + " ml, p95 height " + F(last.p95) + ", visible area " + F(surface.visibleArea)
            + ", unsettled=" + unsettled + ", edge pixels=" + surface.edgePixels + ".");
        SaveTables();
    }

    private void FreezeCpu()
    {
        world.enabled = false;
        world.showControls = false;
        world.interactor.enabled = false;
        gpu.automaticReadback = false;
        foreach (FluidExperimentBody body in world.Items) body.Body.simulated = false;
    }

    private IEnumerator ValidateCalibratedCapacity()
    {
        comparison.SwitchMode(FluidExperimentMode.ECalibratedLiquid);
        world = comparison.World; gpu = comparison.Gpu;
        foreach (string name in new[] { "highball", "hurricane" })
        foreach (bool topping in new[] { false, true })
        {
            FluidExperimentBody vessel = world.Items.First(body => body.kind == LabItemKind.Glass && body.name.Contains(name));
            float capacity = vessel.capacityMl;
            Require(float.IsFinite(capacity) && capacity > 1, name + ": authored full capacity is finite and positive");
            float initial = topping ? Mathf.Min(200, capacity * .5f) : capacity;
            string key = "E-capacity-" + name + (topping ? "-topping" : "-direct");
            comparison.StartVolumeCheck(vessel, initial); FreezeCpu();
            Require(Mathf.Abs(comparison.QueuedVolumeMl - initial) <= .005f,
                key + ": exact initial " + F(initial) + "ml queued against authored " + F(capacity) + "ml capacity");
            CheckCapacityState(vessel, key, "initial-queued", 0, initial);
            ItemDef ingredient = gpu.Ledger.Ingredients.First(entry => entry.QueuedMl > 0).Ingredient;
            if (!topping) Require(gpu.Fill(vessel, ingredient, .5f) == 0,
                key + ": additional fill is rejected while authored capacity is fully reserved before physics");
            CheckPendingFillGeometry(vessel, key + "/initial", initial);
            world.TickLiquid(Dt);
            CheckCapacityState(vessel, key, "initial-born", initial, 0);
            if (topping)
            {
                for (int step = 1; step <= 100; step++)
                {
                    world.TickLiquid(Dt);
                    if (step % 20 != 0) continue;
                    CheckCapacityState(vessel, key, "half-settle-" + F(step * Dt), initial, 0);
                    yield return null;
                }
                Require(Mathf.Abs(gpu.VolumeIn(vessel.Id) - initial) <= .005f,
                    key + ": initial contents remain available when finding new fill sites");
                float top = capacity - initial - .25f;
                float topQueued = gpu.Fill(vessel, ingredient, top);
                Require(Mathf.Abs(topQueued - top) <= .005f,
                    key + ": occupied settled particles permit exact " + F(top) + "ml topping request");
                CheckCapacityState(vessel, key, "top-queued", initial, top);
                float lastQuarter = gpu.Fill(vessel, ingredient, .25f);
                Require(Mathf.Abs(lastQuarter - .25f) <= .0001f,
                    key + ": separate final .25ml request accounts for already queued topping particles");
                CheckCapacityState(vessel, key, "quarter-queued", initial, capacity - initial);
                Require(gpu.Fill(vessel, ingredient, .5f) == 0,
                    key + ": additional fill is rejected while the vessel capacity is fully reserved");
                CheckPendingFillGeometry(vessel, key + "/topping", capacity - initial);
                world.TickLiquid(Dt);
                CheckCapacityState(vessel, key, "top-born", capacity, 0);
                Require(gpu.Snapshot.Count(p => p.Active != 0 && Mathf.Abs(p.VolumeMl - .25f) < .0001f) == 2,
                    key + ": both fractional final particles retain .25ml without rounding or duplication");
            }
            // Once physics has advanced, a brim-level surface can already spill. Pending
            // admission/geometry is tested before that tick; actual ownership is recorded.

            for (int step = 1; step <= 100; step++)
            {
                world.TickLiquid(Dt);
                if (step % 20 != 0) continue;
                CheckCapacityState(vessel, key, "full-settle-" + F(step * Dt), capacity, 0);
                yield return null;
            }
            Capture(vessel, key, InteriorBottom(vessel));
            report.Add("CAPACITY OBSERVATION " + key + ": generated " + F(gpu.Ledger.Total.GeneratedMl)
                + "ml, owned " + F(gpu.VolumeIn(vessel.Id)) + "ml, active " + F(gpu.Ledger.Total.ActiveMl)
                + "ml, retired " + F(gpu.Ledger.Total.RetiredMl) + "ml. Open-rim spill remains physical and separately accounted.");
            SaveTables();
            Require(gpu.VolumeIn(vessel.Id) >= capacity * .97f - .005f,
                key + ": full-capacity initialization retains at least97%of authored capacity after2s settling (owned "
                + F(gpu.VolumeIn(vessel.Id)) + "/" + F(capacity) + "ml)");
        }
    }

    private void CheckPendingFillGeometry(FluidExperimentBody vessel, string key, float expectedPending)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = typeof(FluidExperimentGpuLiquid);
        int count = (int)type.GetField("pendingSpawnCount", hidden).GetValue(gpu);
        var commands = (Array)type.GetField("spawnCommands", hidden).GetValue(gpu);
        Type commandType = commands.GetType().GetElementType();
        FieldInfo positionField = commandType.GetField("Position"), ownerField = commandType.GetField("VesselId"),
            volumeField = commandType.GetField("VolumeMl");
        double pending = 0;
        bool valid = count > 0;
        for (int i = 0; i < count; i++)
        {
            object command = commands.GetValue(i);
            Vector2 position = (Vector2)positionField.GetValue(command);
            uint owner = (uint)ownerField.GetValue(command);
            float volume = (float)volumeField.GetValue(command);
            pending += volume;
            valid &= owner == vessel.Id && Finite(position) && float.IsFinite(volume)
                && volume > 0 && vessel.ContainsLiquidDisk(vessel.WorldToLocal(position), gpu.Radius);
        }
        Require(valid && Math.Abs(pending - expectedPending) <= .005,
            key + ": all " + count + " actual queued spawn commands start owned by the intended vessel with finite ml and disk-safe interior positions ("
            + F(pending) + "ml)");
    }

    private void CheckCapacityState(FluidExperimentBody vessel, string key, string stage, float generated, float pending)
    {
        gpu.ReadbackNow();
        FluidExperimentLedgerEntry total = gpu.Ledger.Total;
        double owned = 0, free = 0, other = 0;
        int count = 0, partial = 0;
        bool finite = true;
        foreach (GpuLiquidParticle particle in gpu.Snapshot)
        {
            if (particle.Active == 0) continue;
            finite &= Finite(particle.Position) && Finite(particle.Velocity) && float.IsFinite(particle.VolumeMl) && particle.VolumeMl > 0;
            count++; if (particle.VolumeMl < gpu.ParticleVolumeMl - .0001f) partial++;
            if (particle.VesselId == vessel.Id) owned += particle.VolumeMl;
            else if (particle.VesselId == 0) free += particle.VolumeMl;
            else other += particle.VolumeMl;
        }
        Require(finite, key + "/" + stage + ": all " + count + " active particles have finite positions, velocities and positive ml");
        capacitySamples.Add(string.Join(",", key, stage, F(vessel.capacityMl), F(total.QueuedMl), F(total.GeneratedMl),
            F(total.CpuPendingMl), F(total.GpuPendingMl), F(total.ActiveMl), F(owned), F(free), F(other),
            F(total.RetiredMl), F(total.TransferredMl), count, partial, F(total.ConservationErrorMl)));
        SaveTables();
        Require(Math.Abs(total.QueuedMl - generated - pending) <= .005 && Math.Abs(total.GeneratedMl - generated) <= .005
            && Math.Abs(total.CpuPendingMl - pending) <= .005,
            key + "/" + stage + ": exact queued, generated and pending ml");
        Require(Math.Abs(owned + free + other - total.ActiveMl) <= .005 && Math.Abs(total.ActiveMl + total.RetiredMl - generated) <= .005,
            key + "/" + stage + ": owned/free/other/retired partition preserves every generated ml");
        Require(gpu.Ledger.Ingredients.Concat(new[] { total }).All(entry => Math.Abs(entry.ConservationErrorMl) <= .005
            && Math.Abs(entry.QueueErrorMl) <= .005 && Math.Abs(entry.RequestedMl - entry.QueuedMl - entry.CpuRejectedMl) <= .005),
            key + "/" + stage + ": total and ingredient conservation hold through capacity filling");
    }

    private Sample ReadSample(FluidExperimentBody vessel, float accepted, float bottom)
    {
        gpu.ReadbackNow();
        var sample = new Sample();
        var owned = new List<GpuLiquidParticle>();
        float speedEnergy = 0;
        foreach (GpuLiquidParticle particle in gpu.Snapshot)
        {
            if (particle.VolumeMl == 0) continue;
            if (!Finite(particle.Position) || !Finite(particle.Velocity) || !float.IsFinite(particle.VolumeMl) || particle.VolumeMl < 0)
                throw new Exception("Non-finite or negative-volume particle in volume measurement.");
            if (particle.Active == 0)
            {
                Vector2 min = gpu.settings.gpuLiquidWorldMin, max = gpu.settings.gpuLiquidWorldMax;
                if (!(particle.Position.x < min.x || particle.Position.y < min.y || particle.Position.x > max.x || particle.Position.y > max.y))
                    throw new Exception("Non-active nonempty slot has no out-of-bounds retirement evidence.");
                sample.retiredMl += particle.VolumeMl;
                continue;
            }
            sample.activeCount++;
            sample.activeMl += particle.VolumeMl;
            if (particle.VesselId == vessel.Id)
            {
                sample.ownerCount++;
                sample.ownerMl += particle.VolumeMl;
                if (vessel.ContainsLiquid(particle.Position)) sample.insideMl += particle.VolumeMl;
                speedEnergy += particle.VolumeMl * particle.Velocity.sqrMagnitude;
                owned.Add(particle);
            }
            else if (particle.VesselId == 0) sample.freeMl += particle.VolumeMl;
            else sample.otherMl += particle.VolumeMl;
        }
        // No emission follows the initial Fill, so retired slot volume cannot be
        // overwritten by later births; this is not a general cumulative ledger.
        if (Mathf.Abs(gpu.EmittedMl - accepted) > .005f || Mathf.Abs(sample.activeMl + sample.retiredMl - accepted) > .005f)
            throw new Exception("Accepted ml do not equal active plus unreused retired ml: accepted=" + F(accepted)
                + ", queued=" + F(gpu.EmittedMl) + ", active=" + F(sample.activeMl) + ", retired=" + F(sample.retiredMl)
                + ", active count=" + sample.activeCount + ", GPU generated=" + (gpu.Ledger?.Total.GeneratedMl.ToString() ?? "unavailable"));
        if (Mathf.Abs(sample.ownerMl + sample.freeMl + sample.otherMl - sample.activeMl) > .005f)
            throw new Exception("Owner/free/other partition does not equal active ml.");
        if (owned.Count == 0)
        {
            sample.p95 = sample.maxCenter = sample.maxExtent = float.NaN;
            return sample;
        }
        owned.Sort((a, b) => a.Position.y.CompareTo(b.Position.y));
        float cumulative = 0;
        sample.p95 = owned[owned.Count - 1].Position.y - bottom;
        foreach (GpuLiquidParticle particle in owned)
        {
            cumulative += particle.VolumeMl;
            if (cumulative < sample.ownerMl * .95f) continue;
            sample.p95 = particle.Position.y - bottom;
            break;
        }
        sample.maxCenter = owned[owned.Count - 1].Position.y - bottom;
        sample.maxExtent = sample.maxCenter + gpu.Radius;
        sample.speedRms = Mathf.Sqrt(speedEnergy / sample.ownerMl);
        return sample;
    }

    private Surface Capture(FluidExperimentBody vessel, string key, float bottom)
    {
        foreach (FluidExperimentBody body in world.Items)
            body.transform.SetPositionAndRotation(new Vector3(body.Position.x, body.Position.y, body.transform.position.z), Quaternion.Euler(0, 0, body.Angle));
        Physics2D.SyncTransforms();
        Camera camera = gpu.outputCamera;
        // The scenario frames the authored interior, independent of disabled CPU colliders.
        camera.orthographic = true;
        camera.aspect = 16f / 9f;
        var scene = new RenderTexture(1280, 720, 24);
        RenderTexture oldTarget = camera.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        GpuLiquidParticle[] before = gpu.Snapshot.ToArray();
        try
        {
            camera.targetTexture = scene;
            camera.Render();
            ReadImage(scene, key + "-scene.png");
            var target = (RenderTexture)typeof(FluidExperimentGpuLiquid)
                .GetField("surfaceComposite", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gpu);
            Require(gpu.SurfaceRenderingReady && gpu.SurfaceRenderingError == null && target != null,
                key + ": actual liquid-only surface target is available");
            Color32[] pixels = ReadImage(target, key + "-liquid.png");
            var result = new Surface { width = target.width, height = target.height, interiorMaxHeight = float.NaN };
            var colorBuffer = (GraphicsBuffer)typeof(FluidExperimentGpuLiquid)
                .GetField("particleColorBuffer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gpu);
            var colors = new Vector4[gpu.Snapshot.Length];
            colorBuffer.GetData(colors);
            float opacityWeight = 0, opacityMl = 0;
            for (int i = 0; i < gpu.Snapshot.Length; i++)
                if (gpu.Snapshot[i].Active != 0)
                {
                    opacityWeight += colors[i].w * gpu.Snapshot[i].VolumeMl;
                    opacityMl += gpu.Snapshot[i].VolumeMl;
                }
            result.sourceOpacity = opacityMl > 0 ? opacityWeight / opacityMl : 1;
            Require(result.sourceOpacity > 0 && float.IsFinite(result.sourceOpacity), key + ": GPU ingredient opacity is measurable");
            float pixelArea = 4 * camera.orthographicSize * camera.orthographicSize * camera.aspect / pixels.Length;
            for (int i = 0; i < pixels.Length; i++)
            {
                float alpha = pixels[i].a / 255f;
                result.alphaSum += alpha;
                float coverage = Mathf.Clamp01(alpha / result.sourceOpacity);
                result.coverageSum += coverage;
                if (coverage >= .25f) result.pixels025++;
                if (coverage < .05f) continue;
                result.pixels005++;
                int x = i % target.width, y = i / target.width;
                if (x == 0 || y == 0 || x == target.width - 1 || y == target.height - 1) result.edgePixels++;
                Vector2 point = camera.ViewportToWorldPoint(new Vector3((x + .5f) / target.width, (y + .5f) / target.height, 20));
                if (!vessel.ContainsLiquid(point)) continue;
                result.interiorPixels++;
                result.interiorMaxHeight = float.IsNaN(result.interiorMaxHeight)
                    ? point.y - bottom : Mathf.Max(result.interiorMaxHeight, point.y - bottom);
            }
            result.visibleArea = result.pixels005 * pixelArea;
            result.interiorArea = result.interiorPixels * pixelArea;
            Require(result.width > 0 && result.height > 0 && pixels.Length == result.width * result.height,
                key + ": area measurement uses a valid rendered image");
            gpu.ReadbackNow();
            Require(before.Zip(gpu.Snapshot, (a, b) => a.Active == b.Active && a.VesselId == b.VesselId
                && a.Position.Equals(b.Position) && a.Velocity.Equals(b.Velocity) && a.VolumeMl == b.VolumeMl).All(x => x),
                key + ": capturing scene and surface leaves physical state unchanged");
            return result;
        }
        finally
        {
            camera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            scene.Release();
            Destroy(scene);
        }
    }

    private Color32[] ReadImage(RenderTexture source, string filename)
    {
        RenderTexture.active = source;
        var image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
        try
        {
            image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(ExperimentVolumeValidation.Evidence, filename), image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally { Destroy(image); }
    }

    private void ValidateFractionalFillAndReset()
    {
        FluidExperimentBody vessel = world.Items.First(body => body.kind == LabItemKind.Glass && body.name.Contains("highball"));
        comparison.StartVolumeCheck(vessel, .25f);
        FreezeCpu();
        Require(Mathf.Abs(comparison.QueuedVolumeMl - .25f) < .0001f, "Fractional Fill accepts exactly .25ml");
        world.TickLiquid(Dt);
        Sample sample = ReadSample(vessel, .25f, InteriorBottom(vessel));
        Require(sample.activeCount == 1 && Mathf.Abs(sample.activeMl - .25f) < .0001f,
            "A single partial particle preserves .25ml without rounding to the normal particle amount");
        bool rejected = false;
        try { comparison.StartVolumeCheck(vessel, -.25f); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Require(rejected && Mathf.Abs(comparison.QueuedVolumeMl - .25f) < .0001f,
            "Negative volume requests are rejected without changing the accepted fractional fill");
        gpu.ResetSimulation();
        gpu.ResetSimulation();
        gpu.ReadbackNow();
        Require(gpu.EmittedMl == 0 && gpu.ActiveCount == 0 && gpu.Snapshot.All(p => p.VolumeMl == 0),
            "Repeated GPU reset clears active and retired state after fractional fill");
    }

    private IEnumerator ValidateControlsFrame()
    {
        Camera camera = gpu.outputCamera;
        int previousWidth = Screen.width, previousHeight = Screen.height;
        FullScreenMode previousFullScreen = Screen.fullScreenMode;
        float previousAspect = camera.aspect;
        RenderTexture previousTarget = camera.targetTexture;
        bool previousControls = comparison.showControls;
        try
        {
            Screen.SetResolution(960, 640, FullScreenMode.Windowed);
            camera.targetTexture = null;
            camera.ResetAspect();
            for (int frame = 0; frame < 12; frame++)
            {
                yield return null;
                if (Screen.width == 960 && Screen.height == 640) break;
            }
            if (Screen.width != 960 || Screen.height != 640)
                report.Add("LIMITATION batch viewport did not accept 960x640; actual controls frame is "
                    + Screen.width + "x" + Screen.height + ". Layout is checked at that actual size.");
            comparison.showControls = true;
            FluidExperimentBody vessel = world.Items.First(body => body.kind == LabItemKind.Glass && body.name.Contains("highball"));
            comparison.StartVolumeCheck(vessel, 60);
            FreezeCpu();
            for (int step = 0; step < 100; step++)
            {
                comparison.AdvanceScenario(Dt);
                world.SendMessage("FixedUpdate");
                world.TickLiquid(Dt);
            }
            gpu.ReadbackNow();
            foreach (FluidExperimentBody body in world.Items)
                body.transform.SetPositionAndRotation(new Vector3(body.Position.x, body.Position.y, body.transform.position.z),
                    Quaternion.Euler(0, 0, body.Angle));
            Physics2D.SyncTransforms();
            yield return null;
            yield return null;

            var points = new List<Vector2>();
            if (vessel.collisionProfile != null)
            {
                points.AddRange(vessel.collisionProfile.interior);
                foreach (FluidExperimentHull hull in vessel.collisionProfile.solids) points.AddRange(hull.points);
                if (vessel.sealedVessel) points.AddRange(vessel.collisionProfile.lid);
            }
            else points.AddRange(vessel.liquidWall);
            Require(points.Count >= 3, "Controls layout uses the authored vessel profile");
            Vector2 low = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 high = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (Vector2 point in points)
            {
                Vector3 screen = camera.WorldToScreenPoint(vessel.LocalToWorld(point));
                Require(screen.z > 0, "Controls layout profile remains in front of the camera");
                Vector2 gui = new Vector2(screen.x, Screen.height - screen.y);
                low = Vector2.Min(low, gui);
                high = Vector2.Max(high, gui);
            }
            Rect controls = world.interactor.pointerBlockRect;
            const float margin = 12;
            Require(low.x >= controls.xMax + margin - .5f && high.x <= Screen.width - margin + .5f
                && low.y >= margin - .5f && high.y <= Screen.height - margin + .5f,
                "Controls-visible profile envelope clears the panel and screen edges by 12px: "
                + low.ToString("F1") + ".." + high.ToString("F1") + ", panel right " + controls.xMax);
            string path = Path.Combine(ExperimentVolumeValidation.Evidence, "volume-controls-frame-960x640.png");
            optionalScreenshot = true;
            bool requested = false;
            try { ScreenCapture.CaptureScreenshot(path); requested = true; }
            catch (Exception exception) { report.Add("LIMITATION ScreenCapture unavailable: " + exception.Message); }
            float deadline = Time.realtimeSinceStartup + 2;
            for (int frame = 0; requested && frame < 240 && Time.realtimeSinceStartup < deadline; frame++)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 1024) break;
                yield return null;
            }
            if (File.Exists(path) && new FileInfo(path).Length > 1024)
            {
                try
                {
                    byte[] png = File.ReadAllBytes(path);
                    int width = png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19];
                    int height = png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23];
                    report.Add((width == 960 && height == 640 ? "PASS " : "LIMITATION ")
                        + "Actual showControls=true ScreenCapture saved at " + width + "x" + height + ": " + path);
                }
                catch (Exception exception) { report.Add("LIMITATION could not inspect captured frame: " + exception.Message); }
            }
            else report.Add("LIMITATION batch ScreenCapture did not write a usable frame within the bounded wait; profile layout assertions passed independently.");
        }
        finally
        {
            optionalScreenshot = false;
            comparison.showControls = previousControls;
            camera.targetTexture = previousTarget;
            camera.aspect = previousAspect;
            Screen.SetResolution(previousWidth, previousHeight, previousFullScreen);
        }
        yield return null;
        yield return null;
        SaveTables();
    }

    private void ValidateMetricsOracle()
    {
        var metrics = new FluidExperimentVolumeMetrics();
        var particles = new[] {
            new GpuLiquidParticle { Active = 1, VesselId = 7, VolumeMl = 95, Position = new Vector2(1, .2f), Velocity = Vector2.right },
            new GpuLiquidParticle { Active = 1, VesselId = 7, VolumeMl = 5, Position = new Vector2(1, 1.8f), Velocity = Vector2.right * 3 },
            new GpuLiquidParticle { Active = 1, VesselId = 0, VolumeMl = 2 },
            new GpuLiquidParticle { Active = 1, VesselId = 8, VolumeMl = 3 },
            new GpuLiquidParticle { Active = 0, VesselId = 7, VolumeMl = 100 }
        };
        var square = new[] { Vector2.zero, new Vector2(2, 0), new Vector2(2, 2), new Vector2(0, 2) };
        metrics.MeasureSnapshot(particles, 7, .1f, square, 200, 50);
        Require(metrics.ActiveMl == 105 && metrics.ContainedMl == 100 && metrics.OutsideMl == 5,
            "Synthetic oracle partitions owned/free/other liquid and excludes inactive slots");
        Require(Mathf.Abs(metrics.ParticleHeight95 - .3f) < .0001f
            && Mathf.Abs(metrics.ParticleHeightMax - 1.9f) < .0001f && Mathf.Abs(metrics.MeanSpeed - 1.1f) < .0001f,
            "Synthetic oracle measures volume-weighted P95, radius-inclusive max and weighted mean speed");
        Require(Mathf.Abs(metrics.InteriorArea - 4) < .0001f && Mathf.Abs(metrics.CapacityReferenceHeight - .5f) < .0001f,
            "Synthetic square has known area and nominal quarter-capacity height");
        metrics.MeasureSnapshot(particles, 0, .1f, square, 200, 50);
        Require(metrics.ContainedMl == 0 && metrics.OutsideMl == 105, "Unowned ID zero is not treated as a selected vessel");
        metrics.MeasureSnapshot(null, 7, 0, new[] { Vector2.zero, new Vector2(2, 0), new Vector2(0, 2) }, 200, 100);
        Require(Mathf.Abs(metrics.InteriorArea - 2) < .0001f
            && Mathf.Abs(metrics.CapacityReferenceHeight - (2 - Mathf.Sqrt(2))) < .0001f,
            "Synthetic triangle nominal half-capacity height follows clipped area rather than linear height");
    }

    private void ValidateScenarioLifecycle()
    {
        FluidExperimentBody vessel = world.Items.First(body => body.kind == LabItemKind.Glass && body.name.Contains("hurricane"));
        comparison.StartVolumeCheck(vessel, 60);
        FreezeCpu();
        Require(!comparison.VolumeSampleAvailable,
            "Starting a measurement waits for a fresh completed snapshot");
        world.TickLiquid(Dt);
        gpu.ReadbackNow();
        Require(comparison.VolumeSampleAvailable && gpu.ActiveCount > 0,
            "A completed readback makes the new measurement available");
        foreach (FluidExperimentMode mode in new[] { FluidExperimentMode.ACurrent, FluidExperimentMode.BReferencePhysics, FluidExperimentMode.CReferenceSurface })
        {
            comparison.SwitchMode(mode);
            FreezeCpu();
            Require(comparison.ActiveMode == mode && comparison.ActiveScenario == FluidExperimentScenario.VolumeCheck
                && comparison.VolumeVessel == vessel && comparison.RequestedVolumeMl == 60
                && comparison.QueuedVolumeMl > 0,
                mode + ": mode switching restarts the same glass and requested volume");
            Require(!comparison.VolumeSampleAvailable,
                mode + ": mode switching invalidates the previous measurement snapshot");
            world.TickLiquid(Dt);
            gpu.ReadbackNow();
            Require(comparison.VolumeSampleAvailable && gpu.ActiveCount > 0,
                mode + ": restarted measurement becomes available only after fresh readback");
        }
        comparison.StartScenario(FluidExperimentScenario.Manual);
        foreach (FluidExperimentBody body in world.Items) body.Body.simulated = true;
        Require(comparison.ActiveScenario == FluidExperimentScenario.Manual && comparison.VolumeVessel == null
            && !comparison.VolumeSampleAvailable && world.interactor.enabled,
            "Manual leaves measurement mode and restores pointer input");
        Require(world.Items.All(body => !body.IsHeld && body.Body.bodyType == RigidbodyType2D.Dynamic && body.Body.simulated),
            "Manual restores dynamic unheld bodies after the harness restores its own simulation override");
        Camera camera = world.interactor.inputCamera;
        Require(Vector3.Distance(camera.transform.position, manualCameraPosition) < .0001f
            && Mathf.Abs(camera.orthographicSize - manualCameraSize) < .0001f,
            "Manual restores the authored camera position and zoom");
    }

    private static float InteriorBottom(FluidExperimentBody vessel)
    {
        if (vessel.collisionProfile != null && vessel.collisionProfile.interior.Length > 0)
            return vessel.collisionProfile.interior.Min(point => vessel.LocalToWorld(point).y);
        return vessel.contentRegions.Min(rect => vessel.LocalToWorld(new Vector2(rect.center.x, rect.yMin)).y);
    }
    private static bool Finite(Vector2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private void Require(bool condition, string message)
    {
        report.Add((condition ? "PASS " : "FAIL ") + message);
        if (!condition) throw new Exception(message);
    }
    private void SaveTables()
    {
        File.WriteAllLines(Path.Combine(ExperimentVolumeValidation.Evidence, "volume-trajectories.csv"), trajectory);
        File.WriteAllLines(Path.Combine(ExperimentVolumeValidation.Evidence, "volume-summary.csv"), summaries);
        if (ImprovedBenchmark) File.WriteAllLines(Path.Combine(ExperimentVolumeValidation.Evidence, "volume-capacity.csv"), capacitySamples);
        File.WriteAllLines(Path.Combine(ExperimentVolumeValidation.Evidence, "volume-validation.txt"), report);
    }
    private void Finish(bool success, string detail)
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        report.Add(detail);
        SaveTables();
        ExperimentVolumeValidation.Finish(success, string.Join("\n", report) + "\n");
    }
}
