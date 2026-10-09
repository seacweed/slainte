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

// Executes only in the isolated batch GPU project; never reads or changes desktop input.
public static class ExperimentIceContactValidation
{
    private const string Key = "FluidExperiment.IceContactValidation";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    [InitializeOnLoadMethod]
    private static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        var runner = new GameObject("IceContactValidationRunner").AddComponent<ExperimentIceContactValidationRunner>();
        runner.validate = SessionState.GetBool(Key + ".validate", false);
        runner.movingOnly = SessionState.GetBool(Key + ".movingOnly", false);
    };
    public static void Begin() => Start(false);
    public static void BeginValidate() => Start(true);
    public static void BeginMovingOnly() => Start(true, true);
    private static void Start(bool validate, bool movingOnly = false)
    {
        Directory.CreateDirectory(Evidence);
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".validate", validate);
        SessionState.SetBool(Key + ".movingOnly", movingOnly);
        EditorApplication.isPlaying = true;
    }
    public static void Finish(bool success, string report)
    {
        File.WriteAllText(Path.Combine(Evidence, "ice-contact-validation.txt"), report);
        File.WriteAllText(Path.Combine(Evidence, "ice-contact-result.txt"), success ? "PASS\n" : "FAIL\n");
        if (success) Debug.Log("[IceContactValidation] PASS\n" + report);
        else Debug.LogError("[IceContactValidation] FAIL\n" + report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ExperimentIceContactValidationRunner : MonoBehaviour
{
    public bool validate;
    public bool movingOnly;
    // Fixed before the baseline: about 0.5% of the authored 0.366-unit ice width.
    private const float Dt = .02f, SettleSeconds = 6, SampleSeconds = 2;
    private const float SleepMinimum = .8f, JitterMaximum = .002f, SpanMaximum = .01f, SpeedMaximum = .02f;
    private const float LiquidNearP95Maximum = .5f, LiquidNearMeanMaximum = .1f;
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private readonly List<string> csv = new List<string> {
        "case,tick,time,ice_index,x,y,angle,vx,vy,angular_velocity,measured_speed,sleeping,contacts,near_liquid_count,near_liquid_mean_speed,near_liquid_p95_speed,deep_liquid_inside_ice,active_ml,retired_ml" };
    private readonly List<CaseResult> results = new List<CaseResult>();
    private readonly List<string> movingTrace = new List<string> {
        "step,phase,ice_id,ice_owner,ice_x,ice_y,ice_angle,previous_x,previous_y,previous_angle,rb_vx,rb_vy,recovery_x,recovery_y,particle_slot,particle_owner,particle_x,particle_y,particle_vx,particle_vy,particle_ml,local_x,local_y,inside_solid,edge_distance,deep_inside,active_count,gpu_substeps,glass_id,glass_x,glass_y,glass_scale_x,glass_scale_y" };
    private readonly List<string> movingBoundaryTrace = new List<string> {
        "step,segment_index,vessel_id,flags,start_x,start_y,end_x,end_y,start_angle,angle_delta,a_x,a_y,b_x,b_y,va_x,va_y,vb_x,vb_y,final_substep_start_fraction,final_substep_end_fraction" };
    private FluidExperimentComparison comparison;
    private FluidExperimentWorld world;
    private FluidExperimentGpuLiquid gpu;
    private FluidExperimentBody glass, prefab;
    private readonly List<FluidExperimentBody> ice = new List<FluidExperimentBody>();
    private readonly ContactPoint2D[] contacts = new ContactPoint2D[64];
    private SimulationMode2D oldSimulation;
    private Vector2 oldGravity;
    private bool finished;

    private IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        float deadline = Time.realtimeSinceStartup + 30;
        while (comparison != null && !comparison.Ready && Time.realtimeSinceStartup < deadline) yield return null;
        oldGravity = Physics2D.gravity; oldSimulation = Physics2D.simulationMode;
        try
        {
            Require(Application.isBatchMode, "Hidden batch GPU harness; no desktop input");
            Require(comparison != null && comparison.Ready, "Authored comparison scene initializes");
            comparison.SwitchMode((FluidExperimentMode)Enum.Parse(typeof(FluidExperimentMode), "FCoherentLiquid"));
            comparison.automaticScenario = false; comparison.showControls = false;
            world = comparison.World; gpu = comparison.Gpu;
            world.enabled = false; world.showControls = false; world.interactor.enabled = false;
            gpu.automaticReadback = false;
            Require(gpu.IsOperational && gpu.useCohesivePhysics && !gpu.useImprovedPhysics, "Actual F GPU solver is operational");
            glass = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
            prefab = world.Items.First(b => b.kind == LabItemKind.IceBucket).icePrefab;
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = new Vector2(0, -9.81f);
            if (movingOnly)
            {
                ValidateMovingIce();
                Require(errors.Count == 0, "No Unity exception, error or assertion");
                Finish(true); yield break;
            }
            report.Add($"INFO dt={Dt}, settle={SettleSeconds}s, sample={SampleSeconds}s; limits sleep>={SleepMinimum}, rms<={JitterMaximum}, span<={SpanMaximum}, speed-p95<={SpeedMaximum}; fixed-ice near-liquid mean<={LiquidNearMeanMaximum}, p95<={LiquidNearP95Maximum}");
            foreach (bool held in new[] { false, true })
            {
                Measure(held, false, false);
                Measure(held, true, false);
                Measure(held, true, true);
            }
            WriteEvidence();
            if (validate) { ValidateResults(); ValidateMovingIce(); }
            Require(errors.Count == 0, "No Unity exception, error or assertion");
            Finish(true);
        }
        catch (Exception ex) { report.Add(ex.ToString()); Finish(false); }
    }

    private void Prepare(bool held, bool liquid, bool fixedIce)
    {
        foreach (var cube in ice) { cube.gameObject.SetActive(false); Destroy(cube.gameObject); }
        ice.Clear();
        foreach (var body in world.Items.ToArray())
        {
            body.SetHeld(false); body.Teleport(new Vector2(-50 - body.Id * 4, 20), 0);
            body.Body.simulated = false; body.pourMlPerSecond = 0;
        }
        gpu.ResetSimulation();
        glass.Teleport(new Vector2(0, 1.5f), 0); glass.SetSealed(false);
        glass.Body.simulated = true; glass.Body.bodyType = RigidbodyType2D.Kinematic;
        Rect inside = glass.collisionProfile.InteriorBounds;
        for (int i = 0; i < (fixedIce ? 1 : 4); i++)
        {
            var cube = Instantiate(prefab, glass.Position, Quaternion.identity, world.transform);
            cube.Body.simulated = true;
            Vector2 local = new Vector2(inside.center.x + (fixedIce ? 0 : (i % 2 == 0 ? -.20f : .20f)),
                inside.yMin + .30f + (fixedIce ? 0 : i / 2 * .40f));
            cube.Teleport(glass.LocalToWorld(local), fixedIce ? 0 : (i % 2 == 0 ? 5 : -5));
            cube.Body.bodyType = fixedIce ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            cube.Body.gravityScale = 1; ice.Add(cube);
        }
        Physics2D.SyncTransforms(); world.RefreshIceContainment();
        Require(ice.All(b => b.ContainingVesselId == glass.Id), "Fixture cubes acquire the actual glass before pickup");
        if (held) glass.SetHeld(true);
        world.RefreshCollisionPairs();
        foreach (var body in world.Items) body.SynchronizeHistory();
        if (!liquid) return;
        ItemDef ingredient = world.Items.First(b => b.ingredient != null).ingredient;
        // The public Fill method deliberately does not sample dynamic ice. Seed
        // its same D lattice here with ice clearance so this contact regression
        // never starts particles trapped inside an already-existing solid.
        float amount = 0, requested = 40;
        Vector3 scale = glass.transform.lossyScale;
        float dx = gpu.Radius * 1.75f / Mathf.Abs(scale.x), dy = gpu.Radius * 1.75f / Mathf.Abs(scale.y);
        foreach (Rect region in glass.contentRegions)
        for (float y = region.yMin + dy; y < region.yMax - dy && amount < requested; y += dy)
        for (float x = region.xMin + dx; x < region.xMax - dx && amount < requested; x += dx)
        {
            Vector2 local = new Vector2(x, y), point = glass.LocalToWorld(local);
            if (!glass.ContainsLiquidDisk(local, gpu.Radius) || InsideIce(point, 0)
                || DistanceToIce(point) < gpu.Radius + .005f) continue;
            float volume = Mathf.Min(gpu.ParticleVolumeMl, requested - amount);
            Require(gpu.TryEmit(point, Vector2.zero, ingredient, volume, glass.Id), "Exterior-only lattice particle is accepted", false);
            amount += volume;
        }
        Require(Mathf.Abs(amount - requested) < .0001f, "Liquid fixture admits exactly 40 ml outside every ice solid and its particle-radius clearance");
        // Explicit particles touching the right ice face ensure an actual liquid/ice
        // boundary interaction even if the bulk fill settles below a suspended cube.
        int nearSeeds = 0;
        foreach (var cube in ice)
        {
            Rect bounds = FluidExperimentCollisionProfile.BoundsOf(cube.collisionProfile.solids[0].points);
            Vector2 local = new Vector2(bounds.xMax + gpu.Radius * 1.01f, bounds.center.y);
            Vector2 point = cube.LocalToWorld(local);
            if (!glass.ContainsLiquid(point) || InsideIce(point, .001f)) continue;
            if (gpu.TryEmit(point, Vector2.left * .05f, ingredient, .5f, glass.Id)) nearSeeds++;
        }
        Require(nearSeeds > 0, "Explicit labeled boundary particles exercise liquid contact at an ice face");
    }

    private void Measure(bool held, bool liquid, bool fixedIce)
    {
        Prepare(held, liquid, fixedIce);
        string name = (held ? "Held" : "Kinematic") + (fixedIce ? "-FixedSingle" : "-Cluster4") + (liquid ? "-F" : "-Empty");
        var result = new CaseResult { name = name, held = held, liquid = liquid, fixedIce = fixedIce, iceCount = ice.Count };
        var positions = ice.Select(_ => new List<Vector2>()).ToArray();
        var speeds = new List<float>(); var nearSpeeds = new List<float>();
        Vector2[] previous = ice.Select(b => b.Position).ToArray();
        int asleep = 0, samples = 0, contactSamples = 0, nearSamples = 0, penetrations = 0;
        float maxStep = 0, angularMaximum = 0, maxLiquidSpeed = 0, maxLedgerError = 0;
        int totalSteps = Mathf.RoundToInt((SettleSeconds + SampleSeconds) / Dt), firstSample = Mathf.RoundToInt(SettleSeconds / Dt);
        for (int tick = 0; tick < totalSteps; tick++)
        {
            world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms();
            Require(Physics2D.Simulate(Dt), name + " actual Physics2D step " + tick, false);
            world.TickLiquid(Dt);
            bool sample = tick >= firstSample;
            if (liquid && (sample || tick < 10 || tick % 50 == 49))
            {
                gpu.ReadbackNow();
                Require(gpu.Snapshot.Where(p => p.Active != 0).All(p => Finite(p.Position) && Finite(p.Velocity) && p.VolumeMl > 0), name + " finite liquid", false);
                float error = (float)Math.Abs(gpu.Ledger.Total.ConservationErrorMl);
                maxLedgerError = Mathf.Max(maxLedgerError, error);
                Require(error < .01f && gpu.Ledger.Ingredients.All(e => Math.Abs(e.ConservationErrorMl) < .01), name + " conserved total and ingredient ml", false);
            }
            var near = new List<float>(); int insideCount = 0;
            if (liquid && sample)
            {
                foreach (var particle in gpu.Snapshot)
                {
                    if (particle.Active == 0) continue;
                    float speed = particle.Velocity.magnitude;
                    maxLiquidSpeed = Mathf.Max(maxLiquidSpeed, speed);
                    if (InsideIce(particle.Position, .002f)) insideCount++;
                    if (DistanceToIce(particle.Position) <= gpu.Radius * 2.5f) near.Add(speed);
                }
                nearSpeeds.AddRange(near); nearSamples += near.Count; penetrations += insideCount;
            }
            for (int i = 0; i < ice.Count; i++)
            {
                var cube = ice[i]; float movement = Vector2.Distance(cube.Position, previous[i]);
                Require(Finite(cube.Position) && Finite(cube.Body.linearVelocity) && float.IsFinite(cube.Body.angularVelocity), name + " finite ice", false);
                if (sample)
                {
                    int count = cube.Body.GetContacts(contacts);
                    positions[i].Add(cube.Position); speeds.Add(cube.Body.linearVelocity.magnitude);
                    maxStep = Mathf.Max(maxStep, movement); angularMaximum = Mathf.Max(angularMaximum, Mathf.Abs(cube.Body.angularVelocity));
                    samples++; if (cube.Body.IsSleeping()) asleep++; if (count > 0) contactSamples++;
                    csv.Add(string.Join(",", name, tick, F((tick + 1) * Dt), i, F(cube.Position.x), F(cube.Position.y), F(cube.Angle),
                        F(cube.Body.linearVelocity.x), F(cube.Body.linearVelocity.y), F(cube.Body.angularVelocity), F(movement / Dt),
                        cube.Body.IsSleeping() ? "1" : "0", count, near.Count, F(near.Count > 0 ? near.Average() : 0), F(Percentile(near, .95f)),
                        insideCount, F(liquid ? (float)gpu.Ledger.Total.ActiveMl : 0), F(liquid ? (float)gpu.Ledger.Total.RetiredMl : 0)));
                }
                previous[i] = cube.Position;
            }
        }
        result.sleepFraction = (float)asleep / samples; result.contactFraction = (float)contactSamples / samples;
        result.speedP95 = Percentile(speeds, .95f); result.maximumStep = maxStep; result.maximumAngularSpeed = angularMaximum;
        result.positionRms = positions.Max(ps => { Vector2 mean = ps.Aggregate(Vector2.zero, (a, b) => a + b) / ps.Count; return Mathf.Sqrt(ps.Average(p => (p - mean).sqrMagnitude)); });
        result.positionSpan = positions.Max(ps => { Vector2 min = ps[0], max = ps[0]; foreach (var p in ps) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); } return (max - min).magnitude; });
        result.nearLiquidSamples = nearSamples; result.nearLiquidMeanSpeed = nearSpeeds.Count > 0 ? nearSpeeds.Average() : 0;
        result.nearLiquidSpeedP95 = Percentile(nearSpeeds, .95f); result.deepPenetrationSamples = penetrations; result.maximumLiquidSpeed = maxLiquidSpeed;
        result.maximumLedgerError = maxLedgerError; result.retainedMl = liquid ? (float)gpu.Ledger.Total.ActiveMl : 0;
        result.generatedMl = liquid ? (float)gpu.Ledger.Total.GeneratedMl : 0; result.retiredMl = liquid ? (float)gpu.Ledger.Total.RetiredMl : 0;
        result.ownedIceCount = ice.Count(b => b.ContainingVesselId == glass.Id);
        results.Add(result); WriteEvidence(); Capture(name);
        report.Add("MEASURE " + JsonUtility.ToJson(result));
        Require(result.ownedIceCount == result.iceCount, name + " stationary glass retains all ice");
        if (liquid) Require(result.nearLiquidSamples > 0, name + " actual GPU particles remain adjacent to ice during the measured interval");
    }

    private void ValidateResults()
    {
        foreach (var r in results)
        {
            if (!r.fixedIce)
            {
                Require(r.contactFraction > .25f, r.name + " real solid contacts persist rather than hovering above the floor");
                Require(r.sleepFraction >= SleepMinimum, r.name + " dynamic ice sleeps for at least 80% of settled samples");
                Require(r.positionRms <= JitterMaximum && r.positionSpan <= SpanMaximum && r.speedP95 <= SpeedMaximum,
                    r.name + " settled ice stays inside predeclared physical jitter and speed limits");
            }
            if (!r.liquid) continue;
            Require(r.maximumLiquidSpeed <= gpu.settings.gpuLiquidMaximumSpeed + .02f && r.deepPenetrationSamples == 0,
                r.name + " liquid remains bounded and outside ice solids");
            Require(r.retainedMl >= r.generatedMl * .99f, r.name + " stationary fixture retains at least 99% of generated liquid");
            if (r.fixedIce) Require(r.nearLiquidMeanSpeed <= LiquidNearMeanMaximum && r.nearLiquidSpeedP95 <= LiquidNearP95Maximum,
                r.name + " stationary ice contact stays inside predeclared liquid-speed limits");
        }
        string path = Environment.GetEnvironmentVariable("FLUID_ICE_CONTACT_BASELINE");
        if (string.IsNullOrWhiteSpace(path)) { report.Add("INFO No before/after comparison requested; absolute gates still applied."); return; }
        var before = JsonUtility.FromJson<Snapshot>(File.ReadAllText(path));
        Require(before.fixtureVersion == 2 && before.dt == Dt && before.settleSeconds == SettleSeconds && before.sampleSeconds == SampleSeconds && before.cases.Length == results.Count,
            "Baseline uses the identical timestep, window and fixture count");
        foreach (var after in results)
        {
            var prior = before.cases.Single(b => b.name == after.name);
            Require(prior.iceCount == after.iceCount && prior.generatedMl == after.generatedMl, after.name + " compares equal physical inputs");
            if (!after.fixedIce && prior.positionRms > JitterMaximum)
                Require(after.positionRms <= prior.positionRms * .5f, after.name + " reduces previously visible ice jitter by at least 50%");
            if (!after.fixedIce && prior.sleepFraction < SleepMinimum)
                Require(after.sleepFraction >= prior.sleepFraction + .2f, after.name + " improves previously deficient sleep by at least 20 percentage points");
            if (after.fixedIce && prior.nearLiquidMeanSpeed > LiquidNearMeanMaximum)
                Require(after.nearLiquidMeanSpeed <= prior.nearLiquidMeanSpeed * .5f, after.name + " halves excessive stationary liquid-contact motion");
        }
    }

    private void ValidateMovingIce()
    {
        // A single particle removes pressure/cohesion neighbor forces from the
        // oracle. Actual kinematic Physics2D travel must still transfer motion.
        Prepare(false, false, true);
        var cube = ice[0];
        cube.Teleport(glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center) - Vector2.right * .14f, 0);
        Physics2D.SyncTransforms(); world.RefreshIceContainment();
        Require(cube.ContainingVesselId == glass.Id, "Repositioned moving cube reacquires the unheld glass before pickup");
        glass.SetHeld(true);
        Require(cube.ContainingVesselId == glass.Id && !cube.IsHeld, "Held glass retains its independently moving ice owner");
        foreach (var body in world.Items) body.SynchronizeHistory();
        Vector2 gravity = Physics2D.gravity;
        Physics2D.gravity = Vector2.zero;
        try
        {
            ItemDef ingredient = world.Items.First(b => b.ingredient != null).ingredient;
            Rect bounds = FluidExperimentCollisionProfile.BoundsOf(cube.collisionProfile.solids[0].points);
            Vector2 initial = cube.LocalToWorld(new Vector2(bounds.xMax + gpu.Radius + .03f, .06f));
            Vector2 endpoint = cube.Position + Vector2.right * .14f;
            Vector2 leadingParticleAtEndpoint = cube.PointAt(new Vector2(bounds.xMax + gpu.Radius, .06f), endpoint, 0);
            Require(glass.ContainsLiquidDisk(glass.WorldToLocal(initial), gpu.Radius)
                && glass.ContainsLiquidDisk(glass.WorldToLocal(leadingParticleAtEndpoint), gpu.Radius + .005f),
                "Moving probe has full particle-disk wall clearance initially and at its travel endpoint");
            report.Add($"INFO Moving probe seed=({F(initial.x)},{F(initial.y)}), radius={F(gpu.Radius)}, dt={F(Dt)}");
            Require(gpu.TryEmit(initial, Vector2.zero, ingredient, .5f, glass.Id), "Moving ice probe queues one stationary particle at its leading face");
            world.TickLiquid(Dt); gpu.ReadbackNow();
            TraceMoving(-1, "initial-after-liquid", cube);
            TraceMovingBoundaries(-1, cube);
            Require(cube.ContainingVesselId == glass.Id, "Moving cube and liquid have the same compatible held-vessel owner initially");
            var first = gpu.Snapshot.Single(p => p.Active != 0);
            Vector2 startIce = cube.Position; float peakSpeed = 0;
            cube.Body.linearVelocity = Vector2.right * .7f;
            for (int step = 0; step < 10; step++)
            {
                world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms();
                Require(Physics2D.Simulate(Dt), "Moving ice uses actual Physics2D translation", false);
                TraceMoving(step, "after-physics-previous-particle-snapshot", cube);
                world.TickLiquid(Dt); gpu.ReadbackNow();
                TraceMoving(step, "after-liquid", cube);
                TraceMovingBoundaries(step, cube);
                Require(cube.ContainingVesselId == glass.Id, "Moving cube retains the liquid's held-vessel owner at step " + step, false);
                var particle = gpu.Snapshot.Single(p => p.Active != 0);
                Require(Finite(particle.Position) && Finite(particle.Velocity) && !InsideIce(particle.Position, .002f),
                    "Moving ice keeps its particle finite and outside the solid (step " + step + ")", false);
                peakSpeed = Mathf.Max(peakSpeed, particle.Velocity.x);
                Require(Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .001,
                    "Moving ice preserves the explicit partial-particle ledger", false);
            }
            var last = gpu.Snapshot.Single(p => p.Active != 0);
            var measurement = new MovingResult { iceTravel = cube.Position.x - startIce.x,
                particleTravel = last.Position.x - first.Position.x, maximumParticleXSpeed = peakSpeed,
                generatedMl = (float)gpu.Ledger.Total.GeneratedMl, activeMl = (float)gpu.Ledger.Total.ActiveMl };
            File.WriteAllText(Path.Combine(ExperimentIceContactValidation.Evidence, "ice-moving-contact.json"), JsonUtility.ToJson(measurement, true));
            report.Add("MEASURE moving ice " + JsonUtility.ToJson(measurement));
            Require(measurement.iceTravel > .12f && measurement.particleTravel > .05f && peakSpeed > .1f,
                "Real moving ice pushes liquid; resting-contact stabilization does not zero genuine boundary motion");
            Capture("MovingIce-F");
        }
        finally { Physics2D.gravity = gravity; }
    }

    private void TraceMoving(int step, string phase, FluidExperimentBody cube)
    {
        // Diagnostic-only reads: do not call another containment/physics update.
        // TickLiquid clears recovery/history after use, so the pre-liquid row
        // preserves the available prior pose; GPU boundary rows retain velocity.
        var property = typeof(FluidExperimentBody).GetProperty("IceRecoveryTranslation");
        Vector2 recovery = property == null ? Vector2.zero : (Vector2)property.GetValue(cube);
        int active = gpu.Snapshot.Count(p => p.Active != 0);
        for (int slot = 0; slot < gpu.Snapshot.Length; slot++)
        {
            var particle = gpu.Snapshot[slot]; if (particle.Active == 0) continue;
            Vector2 local = cube.WorldToLocal(particle.Position);
            bool inside = cube.collisionProfile.solids.Any(h => FluidExperimentCollisionProfile.Contains(h.points, local));
            float edgeDistance = cube.collisionProfile.solids.Min(h => FluidExperimentCollisionProfile.EdgeDistance(h.points, local));
            movingTrace.Add(string.Join(",", step, phase, cube.Id, cube.ContainingVesselId, F(cube.Position.x), F(cube.Position.y), F(cube.Angle),
                F(cube.PreviousPosition.x), F(cube.PreviousPosition.y), F(cube.PreviousAngle), F(cube.Body.linearVelocity.x), F(cube.Body.linearVelocity.y),
                F(recovery.x), F(recovery.y), slot, particle.VesselId, F(particle.Position.x), F(particle.Position.y), F(particle.Velocity.x), F(particle.Velocity.y),
                F(particle.VolumeMl), F(local.x), F(local.y), inside ? "1" : "0", F(edgeDistance), InsideIce(particle.Position, .002f) ? "1" : "0",
                active, gpu.settings.gpuLiquidSubsteps, glass.Id, F(glass.Position.x), F(glass.Position.y), F(glass.transform.lossyScale.x), F(glass.transform.lossyScale.y)));
        }
        File.WriteAllLines(Path.Combine(ExperimentIceContactValidation.Evidence, "ice-moving-trace.csv"), movingTrace);
    }

    private void TraceMovingBoundaries(int step, FluidExperimentBody cube)
    {
        var field = typeof(FluidExperimentGpuLiquid).GetField("boundaryUpload", BindingFlags.Instance | BindingFlags.NonPublic);
        var boundaries = field?.GetValue(gpu) as Array;
        if (boundaries == null) return;
        for (int index = 0; index < boundaries.Length; index++)
        {
            object edge = boundaries.GetValue(index); Type type = edge.GetType();
            uint flags = (uint)type.GetField("Flags").GetValue(edge); if ((flags & 4u) == 0) continue;
            Vector2 Point(string name) => (Vector2)type.GetField(name).GetValue(edge);
            float Scalar(string name) => (float)type.GetField(name).GetValue(edge);
            Vector2 start = Point("StartPosition"), end = Point("EndPosition"), a = Point("A"), b = Point("B"), va = Point("VelocityA"), vb = Point("VelocityB");
            if ((end - cube.Position).sqrMagnitude > .00000001f) continue; // Ignore stale unused upload slots from a prior fixture.
            movingBoundaryTrace.Add(string.Join(",", step, index, type.GetField("VesselId").GetValue(edge), flags,
                F(start.x), F(start.y), F(end.x), F(end.y), F(Scalar("StartAngle")), F(Scalar("AngleDelta")),
                F(a.x), F(a.y), F(b.x), F(b.y), F(va.x), F(va.y), F(vb.x), F(vb.y),
                F(1 - 1f / Mathf.Max(1, gpu.settings.gpuLiquidSubsteps)), "1"));
        }
        File.WriteAllLines(Path.Combine(ExperimentIceContactValidation.Evidence, "ice-moving-boundaries.csv"), movingBoundaryTrace);
    }

    private float DistanceToIce(Vector2 point)
    {
        float best = float.PositiveInfinity;
        foreach (var cube in ice)
        foreach (var hull in cube.collisionProfile.solids)
        {
            Vector2 local = cube.WorldToLocal(point);
            best = Mathf.Min(best, FluidExperimentCollisionProfile.EdgeDistance(hull.points, local));
        }
        return best;
    }
    private bool InsideIce(Vector2 point, float margin)
    {
        foreach (var cube in ice)
        foreach (var hull in cube.collisionProfile.solids)
        {
            Vector2 local = cube.WorldToLocal(point);
            if (FluidExperimentCollisionProfile.Contains(hull.points, local)
                && FluidExperimentCollisionProfile.EdgeDistance(hull.points, local) > margin) return true;
        }
        return false;
    }
    private static bool Finite(Vector2 v) => float.IsFinite(v.x) && float.IsFinite(v.y);
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static float Percentile(List<float> values, float p)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray(); return sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Length * p) - 1, 0, sorted.Length - 1)];
    }
    private void WriteEvidence()
    {
        File.WriteAllLines(Path.Combine(ExperimentIceContactValidation.Evidence, "ice-contact-samples.csv"), csv);
        File.WriteAllText(Path.Combine(ExperimentIceContactValidation.Evidence, "ice-contact-snapshot.json"), JsonUtility.ToJson(new Snapshot
        { fixtureVersion = 2, dt = Dt, settleSeconds = SettleSeconds, sampleSeconds = SampleSeconds, cases = results.ToArray() }, true));
    }
    private void Capture(string name)
    {
        var camera = world.interactor.inputCamera;
        Vector3 oldPosition = camera.transform.position; float oldSize = camera.orthographicSize;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var target = new RenderTexture(640, 640, 24); target.Create();
        var texture = new Texture2D(640, 640, TextureFormat.RGBA32, false);
        try
        {
            foreach (var body in world.Items) body.transform.SetPositionAndRotation(body.Position, Quaternion.Euler(0, 0, body.Angle));
            Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
            camera.transform.position = new Vector3(center.x, center.y, oldPosition.z); camera.orthographicSize = 1.7f;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(ExperimentIceContactValidation.Evidence, name + ".png"), texture.EncodeToPNG());
        }
        finally
        {
            camera.transform.position = oldPosition; camera.orthographicSize = oldSize;
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            target.Release(); Destroy(target); Destroy(texture);
        }
    }
    private void Require(bool success, string label, bool logPass = true)
    {
        if (logPass || !success) report.Add((success ? "PASS " : "FAIL ") + label);
        if (!success) throw new Exception(label);
    }
    private void OnLog(string message, string stack, LogType type)
    {
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) errors.Add(message + "\n" + stack);
    }
    private void Finish(bool success)
    {
        WriteEvidence(); finished = true; Application.logMessageReceived -= OnLog;
        Physics2D.gravity = oldGravity; Physics2D.simulationMode = oldSimulation;
        ExperimentIceContactValidation.Finish(success, string.Join("\n", report));
    }
    [Serializable] public sealed class Snapshot { public int fixtureVersion; public float dt, settleSeconds, sampleSeconds; public CaseResult[] cases; }
    [Serializable] public sealed class MovingResult { public float iceTravel, particleTravel, maximumParticleXSpeed, generatedMl, activeMl; }
    [Serializable] public sealed class CaseResult
    {
        public string name;
        public bool held, liquid, fixedIce;
        public int iceCount, ownedIceCount, nearLiquidSamples, deepPenetrationSamples;
        public float sleepFraction, contactFraction, positionRms, positionSpan, speedP95, maximumStep, maximumAngularSpeed;
        public float nearLiquidMeanSpeed, nearLiquidSpeedP95, maximumLiquidSpeed, maximumLedgerError, generatedMl, retainedMl, retiredMl;
    }
}
