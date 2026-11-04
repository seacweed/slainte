using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

public static class ExperimentScenarioValidation
{
    private const string Key = "Slainte.FluidGpuExperiment.ScenarioValidation";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR")
        ?? "FluidGpuExperimentEvidence/scenario-manual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= Entered; EditorApplication.playModeStateChanged += Entered;
    }
    public static void Begin()
    {
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Path.Combine(Evidence, "scenario-validation.txt"), "STARTED; no result yet.\n");
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    private static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("ExperimentScenarioValidationRunner").AddComponent<ExperimentScenarioValidationRunner>().Begin();
    }
    public static void Finish(bool success, string report)
    {
        File.WriteAllText(Path.Combine(Evidence, "scenario-validation.txt"), report);
        File.WriteAllText(Path.Combine(Evidence, "scenario-result.txt"), success ? "PASS\n" : "FAIL\n");
        if (success) Debug.Log("[ExperimentScenarioValidation] PASS\n" + report);
        else Debug.LogError("[ExperimentScenarioValidation] FAIL\n" + report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ExperimentScenarioValidationRunner : MonoBehaviour
{
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private readonly List<string> rows = new List<string> {
        "case,time_s,generated_ml,active_ml,owned_ml,free_ml,retired_ml,source_stock_ml,mean_speed,rms_speed,maximum_speed,center_x,center_y,cosmetics,mean_density,mean_compression_error,max_compression_error,correction_cap_ml_fraction,diagnostic_rms_speed,spoon_tip_y,actual_liquid_p95_y,tip_immersion_depth,spoon_fluid_contact,conservation_error_ml,queue_error_ml" };
    private FluidExperimentComparison comparison;
    private FluidExperimentEffects effects;
    private float started;
    private bool finished;
    private readonly Dictionary<string, Result> results = new Dictionary<string, Result>();
    private sealed class Result
    {
        public float initialMl, finalMl, finalOwned, retired, emitted, peakFree, peakSpeed, tailSpeed, poseTravel;
        public float tailRmsSum, tailCompressionSum, tailCorrectionCapSum;
        public float maximumTipImmersion;
        public int peakCosmetics, tailSamples, immersedSamples;
        public readonly List<Vector2> centers = new List<Vector2>();
        public readonly List<float> rmsSpeeds = new List<float>();
    }
    public void Begin()
    {
        started = Time.realtimeSinceStartup; Application.logMessageReceived += OnLog; StartCoroutine(Guard());
    }
    private void OnLog(string message, string stack, LogType type)
    {
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) errors.Add(message + "\n" + stack);
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
                if (Time.realtimeSinceStartup - started > 210) throw new Exception("Scenario validation exceeded 210 seconds.");
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
        Finish(errors.Count == 0, "E replay, conservation, material and tool-contact validation completed.");
    }
    private IEnumerator Run()
    {
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        Require(comparison != null, "Authored comparison scene exists");
        while (!comparison.Ready) yield return null;
        comparison.automaticScenario = false; comparison.showControls = false; comparison.enabled = false;
        comparison.SwitchMode(FluidExperimentMode.ECalibratedLiquid);
        comparison.World.enabled = false; comparison.Gpu.automaticReadback = false;
        effects = FindFirstObjectByType<FluidExperimentEffects>();
        Require(effects != null, "Actual E secondary effects component exists");
        effects.enabled = false; effects.enableAudio = false;
        report.Add("Protocol: E Water Rest/Pour/Tilt/Stir/SealedShake, matched held ghost-spoon Stir, Syrup Pour and Milk Stir; .02s fixed ticks, .2s coherent measurements; independent exact-quantity and bounded-response assertions.");
        report.Add("Diagnostics are the final E density/projection pass of each sampled tick, before its final velocity reconstruction; snapshot RMS speed is measured separately. Compression/correction-cap rates are recorded, not described as a general naturalness score.");
        foreach (var scenario in new[] { FluidExperimentScenario.Rest, FluidExperimentScenario.Pour,
            FluidExperimentScenario.Tilt, FluidExperimentScenario.Stir, FluidExperimentScenario.SealedShake })
            yield return Replay(scenario, FluidExperimentMaterial.Water, false);
        yield return Replay(FluidExperimentScenario.Stir, FluidExperimentMaterial.Water, true);
        yield return Replay(FluidExperimentScenario.Pour, FluidExperimentMaterial.Syrup, false);
        yield return Replay(FluidExperimentScenario.Stir, FluidExperimentMaterial.Milk, false);

        Result stir = results["Stir-Water"], ghost = results["Stir-Water-ghost"];
        float change = 0, rmsChange = 0;
        int separatedSamples = 0;
        for (int i = 0; i < Mathf.Min(stir.centers.Count, ghost.centers.Count); i++)
        {
            float centerDifference = Vector2.Distance(stir.centers[i], ghost.centers[i]);
            float speedDifference = Mathf.Abs(stir.rmsSpeeds[i] - ghost.rmsSpeeds[i]);
            change = Mathf.Max(change, centerDifference); rmsChange = Mathf.Max(rmsChange, speedDifference);
            if (centerDifference > .02f || speedDifference > .05f) separatedSamples++;
        }
        Require((change > .02f || rmsChange > .05f) && separatedSamples >= 3,
            "Immersed unheld spoon measurably changes liquid versus identical held ghost-spoon motion across at least3samples (max center "
            + F(change) + "u; RMS " + F(rmsChange) + "u/s; separated samples " + separatedSamples + ")");
        Require(results["Pour-Syrup"].emitted < results["Pour-Water"].emitted * .6f,
            "Global syrup preset reduces actual delivered ml relative to water under the same scripted motion");
        report.Add("OBSERVATION milk/water stir cosmetic peaks: " + results["Stir-Milk"].peakCosmetics + "/" + stir.peakCosmetics
            + "; material lifetime and clip/disposal guarantees are tested by the dedicated pour/effects suite.");
        comparison.StartScenario(FluidExperimentScenario.Manual);
        foreach (var body in comparison.World.Items) body.Body.simulated = true;
        Require(comparison.World.interactor.enabled && comparison.World.Items.All(b => !b.IsHeld),
            "Finishing replay restores manual input and authored unheld state");
    }

    private IEnumerator Replay(FluidExperimentScenario scenario, FluidExperimentMaterial material, bool ghost)
    {
        const float dt = .02f;
        var world = comparison.World; var gpu = comparison.Gpu;
        string key = scenario + "-" + material + (ghost ? "-ghost" : "");
        gpu.improvedMaterial = material;
        comparison.StartScenario(scenario);
        foreach (var body in world.Items) body.Body.simulated = false;
        var glass = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
        var shaker = world.Items.First(b => b.kind == LabItemKind.Shaker);
        var spoon = world.Items.First(b => b.kind == LabItemKind.Spoon);
        var bottle = world.Items.First(b => b.kind == LabItemKind.Bottle && b.name.Contains("1002"));
        var owner = scenario == FluidExperimentScenario.SealedShake ? shaker : glass;
        var moving = scenario == FluidExperimentScenario.Stir ? spoon : owner;
        if (ghost) spoon.SetHeld(true);
        if (scenario == FluidExperimentScenario.Pour)
            Require(!glass.IsHeld && glass.Body.bodyType == RigidbodyType2D.Kinematic,
                key + ": receiving glass is unheld so incoming bottle liquid can collide");
        if (scenario == FluidExperimentScenario.Stir)
            Require(!glass.IsHeld && spoon.IsHeld == ghost, key + ": tool/owner held policy matches the fixture");
        Result result = new Result { initialMl = gpu.EmittedMl };
        Require(Mathf.Abs(result.initialMl - 60) < .005f, key + ": replay starts with exactly 60 queued ml");
        float initialStock = bottle.remainingMl;
        Vector2 lastPose = moving.Position;
        int steps = scenario == FluidExperimentScenario.Pour ? 300 : scenario == FluidExperimentScenario.Rest ? 150 : 200;
        bool allFinite = true, ledgerValid = true;
        float queuedBeforeTail = 0;
        for (int step = 1; step <= steps; step++)
        {
            comparison.AdvanceScenario(dt);
            if (ghost) spoon.SetHeldPose(spoon.Body.position, spoon.Body.rotation);
            world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms();
            result.poseTravel += Vector2.Distance(lastPose, moving.Position); lastPose = moving.Position;
            world.TickLiquid(dt);
            if (step == steps - 50) queuedBeforeTail = gpu.EmittedMl;
            if (step % 10 == 0)
            {
                gpu.ReadbackNow(); effects.RefreshSnapshot();
                var ledger = gpu.Ledger.Total;
                double ingredientMl = 0;
                foreach (var ingredient in gpu.Ledger.Ingredients) ingredientMl += ingredient.GeneratedMl;
                ledgerValid &= Math.Abs(ledger.ConservationErrorMl) < .02 && Math.Abs(ledger.QueueErrorMl) < .02
                    && Math.Abs(ingredientMl - ledger.GeneratedMl) < .02;
                float owned = 0, outside = 0, speed = 0, speedSquared = 0, maxSpeed = 0, active = 0;
                float density = 0, compression = 0, maxCompression = 0, capMl = 0, diagnosticSpeedSquared = 0;
                Vector4[] diagnostics = gpu.ReadImprovedDiagnostics();
                Vector2 center = Vector2.zero;
                for (int particleIndex = 0; particleIndex < gpu.Snapshot.Length; particleIndex++)
                {
                    var p = gpu.Snapshot[particleIndex];
                    if (p.Active == 0) continue;
                    allFinite &= float.IsFinite(p.Position.x) && float.IsFinite(p.Position.y)
                        && float.IsFinite(p.Velocity.x) && float.IsFinite(p.Velocity.y)
                        && float.IsFinite(p.VolumeMl) && p.VolumeMl > 0;
                    active += p.VolumeMl;
                    if (p.VesselId != owner.Id) { outside += p.VolumeMl; continue; }
                    owned += p.VolumeMl; center += p.Position * p.VolumeMl;
                    speed += p.Velocity.magnitude * p.VolumeMl; maxSpeed = Mathf.Max(maxSpeed, p.Velocity.magnitude);
                    speedSquared += p.Velocity.sqrMagnitude * p.VolumeMl;
                    Vector4 diagnostic = diagnostics[particleIndex];
                    allFinite &= float.IsFinite(diagnostic.x) && float.IsFinite(diagnostic.y)
                        && float.IsFinite(diagnostic.z) && float.IsFinite(diagnostic.w)
                        && diagnostic.y >= 0 && diagnostic.z >= 0 && diagnostic.z <= 1 && diagnostic.w >= 0;
                    density += diagnostic.x * p.VolumeMl; compression += diagnostic.y * p.VolumeMl;
                    maxCompression = Mathf.Max(maxCompression, diagnostic.y);
                    capMl += diagnostic.z * p.VolumeMl; diagnosticSpeedSquared += diagnostic.w * p.VolumeMl;
                }
                float rms = 0, diagnosticRms = 0;
                if (owned > 0)
                {
                    center /= owned; speed /= owned; density /= owned; compression /= owned; capMl /= owned;
                    rms = Mathf.Sqrt(speedSquared / owned); diagnosticRms = Mathf.Sqrt(diagnosticSpeedSquared / owned);
                }
                if (step > steps - 50)
                {
                    result.tailSamples++; result.tailRmsSum += rms;
                    result.tailCompressionSum += compression; result.tailCorrectionCapSum += capMl;
                }
                result.centers.Add(center);
                result.rmsSpeeds.Add(rms);
                float tipY = 0, liquidLevel = 0, immersion = 0;
                bool contact = false;
                if (scenario == FluidExperimentScenario.Stir)
                {
                    Vector2 tip = LowestSolidPoint(spoon);
                    tipY = tip.y; liquidLevel = ParticleSurface95(gpu, owner.Id);
                    immersion = liquidLevel - tipY;
                    float nearest = float.PositiveInfinity;
                    foreach (var p in gpu.Snapshot)
                        if (p.Active != 0 && p.VesselId == owner.Id)
                            nearest = Mathf.Min(nearest, Vector2.Distance(p.Position, tip));
                    contact = owner.ContainsLiquid(tip) && immersion > .04f
                        && nearest <= gpu.ImprovedKernelRadius(owner.Id);
                    if (contact) result.immersedSamples++;
                    if (owner.ContainsLiquid(tip)) result.maximumTipImmersion = Mathf.Max(result.maximumTipImmersion, immersion);
                }
                result.peakFree = Mathf.Max(result.peakFree, outside);
                result.peakSpeed = Mathf.Max(result.peakSpeed, maxSpeed);
                result.tailSpeed = speed; result.finalMl = active; result.finalOwned = owned;
                result.retired = (float)ledger.RetiredMl;
                rows.Add(string.Join(",", key, F(step * dt), F(ledger.GeneratedMl), F(active), F(owned), F(outside),
                    F(ledger.RetiredMl), F(bottle.remainingMl), F(speed), F(rms), F(maxSpeed), F(center.x), F(center.y),
                    effects.ActiveCosmeticCount, F(density), F(compression), F(maxCompression), F(capMl), F(diagnosticRms),
                    F(tipY), F(liquidLevel), F(immersion), contact ? 1 : 0,
                    F(ledger.ConservationErrorMl), F(ledger.QueueErrorMl)));
            }
            effects.StepCosmetics(dt);
            result.peakCosmetics = Mathf.Max(result.peakCosmetics, effects.ActiveCosmeticCount);
            if (step % 20 == 0) yield return null;
        }
        result.emitted = gpu.EmittedMl - result.initialMl;
        Require(allFinite && result.peakSpeed <= gpu.settings.gpuLiquidMaximumSpeed + .05f,
            key + ": all active particles and velocities remain finite and bounded");
        Require(ledgerValid, key + ": every sampled completed tick conserves total and ingredient ml");
        Require(result.peakCosmetics <= FluidExperimentEffects.MaximumCosmetics, key + ": secondary effects remain bounded");
        if (scenario == FluidExperimentScenario.Pour)
        {
            Require(result.emitted > 5 && Math.Abs(initialStock - bottle.remainingMl - result.emitted) < .02,
                key + ": the real bottle emits and debits only accepted stock");
            Require(result.finalOwned > 60.5f, key + ": the receiving glass captures actual new bottle liquid");
            Require(bottle.ReservoirCurrentFlowMlPerSecond == 0, key + ": restored upright bottle ends the finite drip tail");
            Require(Mathf.Abs(gpu.EmittedMl - queuedBeforeTail) < .001f,
                key + ": final one-second tail contains no deferred emitter births");
        }
        else Require(Math.Abs(result.emitted) < .005f, key + ": moving replay cannot generate liquid");
        if (scenario == FluidExperimentScenario.Rest)
        {
            Require(result.finalOwned >= 59.5f, key + ": resting glass retains its seeded contents");
            Require(result.tailRmsSum / Mathf.Max(1, result.tailSamples) < .5f,
                key + ": stationary liquid's final one-second RMS speed stays below .5 units/s");
        }
        if (scenario == FluidExperimentScenario.Tilt)
            Require(result.peakFree + result.retired > .5f && Mathf.Abs(glass.HeldAngle) > 130,
                key + ": actual vessel tilt spills liquid through its open mouth");
        if (scenario == FluidExperimentScenario.Stir || scenario == FluidExperimentScenario.SealedShake)
            Require(result.poseTravel > .2f && result.peakSpeed > .1f, key + ": authored moving geometry drives the replay");
        if (scenario == FluidExperimentScenario.Stir)
            Require(result.maximumTipImmersion > .04f && result.immersedSamples >= result.centers.Count / 4,
                key + ": authored spoon tip actually enters the bowl below the measured particle surface with neighboring liquid ("
                + result.immersedSamples + "/" + result.centers.Count + "samples; max depth " + F(result.maximumTipImmersion) + "u)");
        if (scenario == FluidExperimentScenario.SealedShake)
            Require(shaker.sealedVessel && result.finalOwned >= 59.5f && result.peakFree <= .5f && result.retired <= .005f,
                key + ": sealed moving shaker retains its contents without teleporting or loss");
        results[key] = result;
        report.Add("OBSERVATION " + key + " emitted=" + F(result.emitted) + " owned=" + F(result.finalOwned)
            + " outsidePeak=" + F(result.peakFree) + " retired=" + F(result.retired) + " tailSpeed=" + F(result.tailSpeed)
            + " tailRms=" + F(result.tailRmsSum / Mathf.Max(1, result.tailSamples))
            + " tailCompression=" + F(result.tailCompressionSum / Mathf.Max(1, result.tailSamples))
            + " tailCorrectionCap=" + F(result.tailCorrectionCapSum / Mathf.Max(1, result.tailSamples))
            + " spoonImmersedSamples=" + result.immersedSamples + " tipMaxDepth=" + F(result.maximumTipImmersion)
            + " cosmeticsPeak=" + result.peakCosmetics);
        Capture(key);
    }

    private static Vector2 LowestSolidPoint(FluidExperimentBody body)
    {
        Vector2 lowest = new Vector2(0, float.PositiveInfinity);
        foreach (var hull in body.collisionProfile.solids)
        foreach (Vector2 point in hull.points)
        {
            Vector2 world = body.LocalToWorld(point);
            if (world.y < lowest.y) lowest = world;
        }
        return lowest;
    }
    private static float ParticleSurface95(FluidExperimentGpuLiquid gpu, uint owner)
    {
        var particles = gpu.Snapshot.Where(p => p.Active != 0 && p.VesselId == owner && p.VolumeMl > 0)
            .OrderBy(p => p.Position.y).ToArray();
        float target = particles.Sum(p => p.VolumeMl) * .95f, cumulative = 0;
        foreach (var particle in particles)
        {
            cumulative += particle.VolumeMl;
            if (cumulative >= target) return particle.Position.y + gpu.Radius;
        }
        return float.NegativeInfinity;
    }

    private void Capture(string key)
    {
        Camera camera = comparison.Gpu.outputCamera;
        if (camera == null) camera = comparison.World.interactor.inputCamera;
        var target = new RenderTexture(960, 640, 24);
        var image = new Texture2D(960, 640, TextureFormat.RGBA32, false);
        RenderTexture previous = camera.targetTexture, active = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(ExperimentScenarioValidation.Evidence, key + ".png"), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previous; RenderTexture.active = active;
            target.Release(); Destroy(target); Destroy(image);
        }
    }
    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        report.Add("PASS " + message);
    }
    private void Finish(bool success, string message)
    {
        finished = true; Application.logMessageReceived -= OnLog; report.Add(message);
        File.WriteAllLines(Path.Combine(ExperimentScenarioValidation.Evidence, "scenario-trajectories.csv"), rows);
        ExperimentScenarioValidation.Finish(success, string.Join("\n", report));
    }
}
