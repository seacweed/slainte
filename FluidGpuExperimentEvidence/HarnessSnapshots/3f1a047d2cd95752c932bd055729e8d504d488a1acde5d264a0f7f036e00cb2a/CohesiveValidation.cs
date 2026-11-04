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

// No desktop input. Both entry points run only in an isolated GPU batch harness.
public static class ExperimentCohesiveValidation
{
    private const string Key = "FluidExperiment.CohesiveValidation";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    [InitializeOnLoadMethod]
    private static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        var runner = new GameObject("CohesiveValidationRunner").AddComponent<ExperimentCohesiveValidationRunner>();
        runner.baselineOnly = SessionState.GetBool(Key + ".baseline", false);
    };
    public static void BeginBaseline() => BeginRun(true);
    public static void Begin() => BeginRun(false);
    private static void BeginRun(bool baseline)
    {
        Directory.CreateDirectory(Evidence);
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".baseline", baseline);
        EditorApplication.isPlaying = true;
    }
    public static void Finish(bool success, string report)
    {
        File.WriteAllText(Path.Combine(Evidence, "cohesive-validation.txt"), report);
        File.WriteAllText(Path.Combine(Evidence, "cohesive-result.txt"), success ? "PASS\n" : "FAIL\n");
        if (success) Debug.Log("[CohesiveValidation] PASS\n" + report);
        else Debug.LogError("[CohesiveValidation] FAIL\n" + report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ExperimentCohesiveValidationRunner : MonoBehaviour
{
    public bool baselineOnly;
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private FluidExperimentComparison comparison;
    private FluidExperimentWorld world;
    private FluidExperimentGpuLiquid gpu;
    private Vector2 oldGravity;
    private bool finished;
    private string defaultFSettings;
    private FluidExperimentMode cohesiveMode;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        float deadline = Time.realtimeSinceStartup + 30;
        while (comparison != null && !comparison.Ready && Time.realtimeSinceStartup < deadline) yield return null;
        oldGravity = Physics2D.gravity;
        try
        {
            Check(Application.isBatchMode, "Validation runs without desktop input in the hidden GPU harness");
            Check(comparison != null && comparison.Ready, "Authored comparison scene initializes");
            world = comparison.World; gpu = comparison.Gpu;
            if (baselineOnly) report.Add("INFO Baseline authored default: " + comparison.initialMode);
            else Check(comparison.initialMode.ToString() == "FCoherentLiquid", "The authored default is Model F");
            comparison.automaticScenario = false; comparison.showControls = false;
            world.enabled = false; world.interactor.enabled = false;
            SnapshotLegacyModes();
            if (!baselineOnly) ValidateCohesiveMode();
            Check(errors.Count == 0, "No Unity runtime error, exception or assertion was emitted");
            Finish(true);
        }
        catch (Exception ex) { report.Add(ex.ToString()); Finish(false); }
    }

    private void SnapshotLegacyModes()
    {
        var snapshots = new List<ModeSnapshot>();
        for (int mode = 0; mode < 5; mode++)
        {
            comparison.SwitchMode((FluidExperimentMode)mode);
            Isolate();
            Physics2D.gravity = Vector2.zero;
            var ingredients = world.Items.Where(x => x.ingredient != null).Select(x => x.ingredient).Distinct().Take(2).ToArray();
            Check(ingredients.Length == 2, "Legacy baseline uses two actual authored ingredients");
            for (int i = 0; i < 8; i++)
                Check(gpu.TryEmit(new Vector2((i % 4) * .09f, 2 + (i / 4) * .09f),
                    new Vector2(i % 2 == 0 ? .15f : -.1f, (i % 3 - 1) * .08f), ingredients[i % 2], .17f + i * .037f, 0),
                    "Legacy baseline admits a uniquely weighted particle");
            for (int tick = 0; tick < 12; tick++) gpu.Step(.01f);
            gpu.ReadbackNow();
            CheckState("Legacy " + comparison.ActiveMode);
            var records = gpu.Snapshot.Select((particle, index) => new { particle, index }).Where(x => x.particle.Active != 0)
                .OrderBy(x => x.particle.VolumeMl).Select(x => new ParticleSnapshot
                {
                    position = x.particle.Position, velocity = x.particle.Velocity, volume = x.particle.VolumeMl,
                    ingredientA = gpu.CompositionSnapshot[x.index * gpu.IngredientStride],
                    ingredientB = gpu.CompositionSnapshot[x.index * gpu.IngredientStride + 1]
                }).ToArray();
            Check(records.Length == 8, "Legacy baseline retains all eight unique-volume particles");
            snapshots.Add(new ModeSnapshot { mode = comparison.ActiveMode.ToString(), radius = gpu.Radius,
                particleMl = gpu.ParticleVolumeMl, improved = gpu.useImprovedPhysics,
                improvedSurface = gpu.useImprovedSurface, particles = records });
        }
        var payload = new LegacySnapshot { modes = snapshots.ToArray() };
        File.WriteAllText(Path.Combine(ExperimentCohesiveValidation.Evidence, "legacy-a-e-snapshot.json"), JsonUtility.ToJson(payload, true));
        string previous = Environment.GetEnvironmentVariable("FLUID_COHESIVE_BASELINE");
        if (!string.IsNullOrWhiteSpace(previous))
        {
            var baseline = JsonUtility.FromJson<LegacySnapshot>(File.ReadAllText(previous));
            Check(baseline.modes.Length == payload.modes.Length, "A-E baseline covers every preserved comparison mode");
            for (int m = 0; m < payload.modes.Length; m++)
            {
                ModeSnapshot a = baseline.modes[m], b = payload.modes[m];
                Check(a.mode == b.mode && a.radius == b.radius && a.particleMl == b.particleMl
                    && a.improved == b.improved && a.improvedSurface == b.improvedSurface,
                    b.mode + " preserves baseline settings and feature flags");
                Check(a.particles.Length == b.particles.Length, b.mode + " preserves baseline active count");
                float maxPosition = 0, maxVelocity = 0, maxComposition = 0;
                for (int i = 0; i < b.particles.Length; i++)
                {
                    maxPosition = Mathf.Max(maxPosition, Vector2.Distance(a.particles[i].position, b.particles[i].position));
                    maxVelocity = Mathf.Max(maxVelocity, Vector2.Distance(a.particles[i].velocity, b.particles[i].velocity));
                    maxComposition = Mathf.Max(maxComposition, Mathf.Abs(a.particles[i].ingredientA - b.particles[i].ingredientA),
                        Mathf.Abs(a.particles[i].ingredientB - b.particles[i].ingredientB));
                }
                Check(maxPosition < .0001f && maxVelocity < .001f && maxComposition < .0001f,
                    $"{b.mode} matches pre-change GPU fixture (position={maxPosition:R}, velocity={maxVelocity:R}, composition={maxComposition:R})");
            }
        }
    }

    private void ValidateCohesiveMode()
    {
        cohesiveMode = (FluidExperimentMode)Enum.Parse(typeof(FluidExperimentMode), "FCoherentLiquid");
        comparison.SwitchMode(FluidExperimentMode.DImprovedSurface);
        float dRadius = gpu.Radius, dMl = gpu.ParticleVolumeMl;
        float dKernel = (float)typeof(FluidExperimentGpuLiquid).GetProperty("SolverSmoothingRadius", Private).GetValue(gpu);
        float dFill = FillProbe();
        foreach (var mode in new[] { cohesiveMode, FluidExperimentMode.DImprovedSurface,
            FluidExperimentMode.ECalibratedLiquid, cohesiveMode, FluidExperimentMode.DImprovedSurface, cohesiveMode })
        {
            comparison.SwitchMode(mode); Isolate();
            bool f = mode == cohesiveMode, e = mode == FluidExperimentMode.ECalibratedLiquid;
            Check(gpu.IsOperational && (bool)Get(gpu, "useCohesivePhysics") == f && gpu.useImprovedPhysics == e,
                mode + " activates exactly its requested physics family");
            Check(gpu.useImprovedSurface && gpu.ActiveSolver == FluidExperimentSolver.ReferenceSph,
                mode + " retains the improved surface and reference-neighborhood solver family");
            Check((bool)typeof(FluidExperimentGpuLiquid).GetProperty("CohesivePhysicsActive").GetValue(gpu) == f,
                mode + " cohesive solver operational flag follows mode switches");
            if (f || mode == FluidExperimentMode.DImprovedSurface)
            {
                Check(gpu.Radius == dRadius && gpu.ParticleVolumeMl == dMl
                    && (float)typeof(FluidExperimentGpuLiquid).GetProperty("SolverSmoothingRadius", Private).GetValue(gpu) == dKernel,
                    mode + " retains exact D particle, ml and smoothing scales");
                Check(Mathf.Abs(FillProbe() - dFill) < .0001f, mode + " retains D partial-quantity filling");
            }
        }
        Check(comparison.initialMode == cohesiveMode, "Mode switching never changes the authored F default");
        defaultFSettings = JsonUtility.ToJson(gpu.settings);
        var effects = world.GetComponent<FluidExperimentEffects>();
        Check(effects != null, "Existing isolated effects component is available for exclusion check");
        effects.RefreshSnapshot(); effects.ApplyIceResponse(.02f);
        Check(effects.LastAppliedIceCount == 0 && effects.ActiveCosmeticCount == 0,
            "F does not enable E ice response or secondary cosmetic effects");

        float noPressure = CompressionProbe(0), onePressure = CompressionProbe(1), sixPressure = CompressionProbe(6);
        Check(noPressure > .15f, "Density fixture begins measurably compressed");
        Check(onePressure < noPressure && sixPressure < onePressure,
            $"More compression iterations reduce the same-state density residual (0={noPressure:R}, 1={onePressure:R}, 6={sixPressure:R})");
        var neutral = PairProbe(0, 0, FluidExperimentMaterial.Water, false);
        var cohesive = PairProbe(18, 0, FluidExperimentMaterial.Water, false);
        Check(cohesive.distance < neutral.distance - .00001f && cohesive.leftVelocity.x > .0001f,
            $"Independent cohesion attracts a stationary underdense pair (off={neutral.distance:R}, on={cohesive.distance:R})");
        Check(cohesive.momentum.magnitude < .0001f && neutral.momentum.magnitude < .0001f,
            "Symmetric stationary pair conserves net momentum with and without cohesion");
        var viscosityOff = PairProbe(0, 0, FluidExperimentMaterial.Water, true);
        var water = PairProbe(0, 1, FluidExperimentMaterial.Water, true);
        var syrup = PairProbe(0, 1, FluidExperimentMaterial.Syrup, true);
        Check(water.shear < viscosityOff.shear - .0001f && syrup.shear < water.shear - .0001f,
            $"Full-vector material viscosity damps tangential shear (off={viscosityOff.shear:R}, water={water.shear:R}, syrup={syrup.shear:R})");
        Check(viscosityOff.momentum.magnitude < .0002f && water.momentum.magnitude < .0002f && syrup.momentum.magnitude < .0002f,
            "Unequal-volume tangential pairs conserve mass-weighted linear momentum");
        ValidateMixing();
        ValidateScenarios();
    }

    private float FillProbe()
    {
        Isolate();
        var vessel = world.Items.First(x => x.kind == LabItemKind.Glass && x.name.Contains("highball"));
        vessel.Teleport(new Vector2(0, 2), 0);
        float amount = gpu.Fill(vessel, Ingredient(), 10.125f);
        Check(Mathf.Abs(amount - 10.125f) < .0001f, "Authored vessel admits an explicit fractional ml fill");
        gpu.ResetSimulation();
        return amount;
    }

    private void ConfigureProbe(int iterations, float cohesion, float viscosity, FluidExperimentMaterial material)
    {
        gpu.Dispose();
        JsonUtility.FromJsonOverwrite(defaultFSettings, gpu.settings);
        Set(gpu.settings, "cohesiveDensityIterations", iterations);
        Set(gpu.settings, "cohesiveSurfaceTension", cohesion);
        Set(gpu.settings, "cohesiveShearViscosity", viscosity);
        Set(gpu.settings, "cohesiveWallDensity", 0f);
        gpu.settings.gpuLiquidSubsteps = 1;
        gpu.settings.gpuLiquidVelocityDamping = 0;
        gpu.settings.gpuLiquidViscosity = 0;
        gpu.settings.referenceViscosityRate = 0;
        gpu.settings.gpuLiquidPassiveMixRate = 0;
        gpu.settings.gpuLiquidAgitationMixRate = 0;
        Set(gpu, "cohesiveMaterial", material);
        gpu.Initialize(world); Isolate(); Physics2D.gravity = Vector2.zero;
        Check(gpu.IsOperational, "Controlled F GPU probe initializes");
    }

    private float CompressionProbe(int iterations)
    {
        ConfigureProbe(iterations, 0, 0, FluidExperimentMaterial.Water);
        for (int y = -3; y <= 3; y++)
        for (int x = -3; x <= 3; x++)
            Check(gpu.TryEmit(new Vector2(x * .045f, 4 + y * .045f), Vector2.zero, Ingredient(), .5f, 0),
                "Compression probe queues its controlled dense lattice");
        gpu.Step(.01f); gpu.ReadbackNow();
        CheckState("Compression " + iterations);
        Vector4[] diagnostics = ReadDiagnostics();
        var active = gpu.Snapshot.Select((p, i) => new { p, i }).Where(x => x.p.Active != 0).ToArray();
        Check(active.Length == 49, "Compression projection retains every lattice particle");
        double residual = 0;
        foreach (var item in active)
        {
            double density = item.p.VolumeMl / gpu.ParticleVolumeMl;
            foreach (var other in active)
            {
                if (other.i == item.i) continue;
                double q = Math.Max(0, 1 - Vector2.Distance(item.p.Position, other.p.Position) / gpu.settings.referenceSmoothingRadius);
                density += other.p.VolumeMl / gpu.ParticleVolumeMl * q * q;
            }
            float expected = (float)density / gpu.settings.referenceRestDensity;
            Check(Mathf.Abs(diagnostics[item.i].x - expected) < .002f,
                "GPU final density agrees with independent D-scale q-squared CPU oracle");
            residual += Math.Max(0, expected - 1);
        }
        return (float)(residual / active.Length);
    }

    private PairResult PairProbe(float cohesion, float viscosity, FluidExperimentMaterial material, bool shear)
    {
        ConfigureProbe(0, cohesion, viscosity, material);
        float half = shear ? .06f : .09f;
        Check(gpu.TryEmit(new Vector2(-half, 4), shear ? Vector2.up : Vector2.zero, Ingredient(), .5f, 0), "Pair admits first particle");
        Check(gpu.TryEmit(new Vector2(half, 4), shear ? Vector2.down * 2.5f : Vector2.zero, Ingredient(), shear ? .2f : .5f, 0),
            "Pair admits second particle with its explicit amount");
        // Observe attraction before an intentionally pressure-free pair can pass
        // through its equilibrium spacing on accumulated inertial velocity.
        for (int tick = 0; tick < (shear ? 1 : 3); tick++) gpu.Step(.01f);
        gpu.ReadbackNow(); CheckState("Pair " + cohesion + "/" + viscosity + "/" + material);
        var particles = gpu.Snapshot.Where(p => p.Active != 0).OrderBy(p => p.Position.x).ToArray();
        Check(particles.Length == 2, "Controlled pair retains exactly two particles");
        return new PairResult { distance = Vector2.Distance(particles[0].Position, particles[1].Position),
            shear = Mathf.Abs(particles[0].Velocity.y - particles[1].Velocity.y), leftVelocity = particles[0].Velocity,
            momentum = particles[0].Velocity * particles[0].VolumeMl + particles[1].Velocity * particles[1].VolumeMl };
    }

    private void ValidateMixing()
    {
        ConfigureProbe(0, 0, 0, FluidExperimentMaterial.Water);
        gpu.Dispose(); gpu.settings.gpuLiquidPassiveMixRate = 15;
        gpu.Initialize(world); gpu.ResetSimulation();
        var ingredients = world.Items.Where(x => x.ingredient != null).Select(x => x.ingredient).Distinct().Take(2).ToArray();
        double[] expected = new double[2];
        for (int i = 0; i < 12; i++)
        {
            float amount = i % 3 == 0 ? .125f : i % 3 == 1 ? .35f : .5f;
            int ingredient = i % 2; expected[ingredient] += amount;
            Check(gpu.TryEmit(new Vector2((i % 4) * .045f, 4 + (i / 4) * .045f), Vector2.zero,
                ingredients[ingredient], amount, 0), "Mixing fixture queues a labeled partial-volume particle");
        }
        for (int tick = 0; tick < 100; tick++)
        {
            gpu.Step(.01f);
            if (tick % 20 != 19) continue;
            gpu.ReadbackNow(); CheckState("Conservative partial mixing tick " + tick);
            foreach (var entry in gpu.Ledger.Ingredients)
            {
                int index = Array.IndexOf(ingredients, entry.Ingredient);
                Check(index >= 0 && Math.Abs(entry.ActiveMl - expected[index]) < .003
                    && Math.Abs(entry.ConservationErrorMl) < .003,
                    "Conservative mixing preserves each ingredient's explicit ml independently");
            }
        }
        int mixed = 0;
        for (int i = 0; i < gpu.Snapshot.Length; i++)
        {
            if (gpu.Snapshot[i].Active == 0) continue;
            float a = gpu.CompositionSnapshot[i * gpu.IngredientStride], b = gpu.CompositionSnapshot[i * gpu.IngredientStride + 1];
            Check(a >= 0 && b >= 0 && Mathf.Abs(a + b - 1) < .001f, "Every mixed particle has normalized nonnegative composition");
            if (a > .01f && b > .01f) mixed++;
        }
        Check(mixed >= 8, "The conservation fixture actually exchanges ingredients instead of merely leaving them unmixed");
    }

    private void ValidateScenarios()
    {
        JsonUtility.FromJsonOverwrite(defaultFSettings, gpu.settings);
        Set(gpu, "cohesiveMaterial", FluidExperimentMaterial.Auto);
        comparison.SwitchMode(cohesiveMode);
        Physics2D.gravity = oldGravity;
        var rows = new List<string> { "scenario,tick,active,generated_ml,active_ml,retired_ml,max_speed,mean_positive_compression" };
        foreach (var scenario in new[] { FluidExperimentScenario.Rest, FluidExperimentScenario.Pour,
            FluidExperimentScenario.Stir, FluidExperimentScenario.SealedShake })
        {
            comparison.StartScenario(scenario);
            world.enabled = false; world.interactor.enabled = false; gpu.automaticReadback = false;
            foreach (var body in world.Items) body.Body.simulated = false;
            for (int tick = 0; tick < 300; tick++)
            {
                comparison.AdvanceScenario(.02f); world.SendMessage("FixedUpdate"); world.TickLiquid(.02f);
                if (tick % 30 != 29) continue;
                gpu.ReadbackNow(); CheckState("F " + scenario + " tick " + tick);
                foreach (var entry in gpu.Ledger.Ingredients)
                    Check(Math.Abs(entry.ConservationErrorMl) < .01, "F scenario retains per-ingredient liquid including retired spill");
                var active = gpu.Snapshot.Select((p, i) => new { p, i }).Where(x => x.p.Active != 0).ToArray();
                Vector4[] diagnostics = ReadDiagnostics();
                float maxSpeed = active.Length == 0 ? 0 : active.Max(x => x.p.Velocity.magnitude);
                Check(maxSpeed <= gpu.settings.gpuLiquidMaximumSpeed + .02f, "F scenario keeps particle speed inside its configured bound");
                rows.Add(string.Join(",", scenario, tick, active.Length,
                    gpu.Ledger.Total.GeneratedMl.ToString("R", CultureInfo.InvariantCulture),
                    gpu.Ledger.Total.ActiveMl.ToString("R", CultureInfo.InvariantCulture),
                    gpu.Ledger.Total.RetiredMl.ToString("R", CultureInfo.InvariantCulture), maxSpeed.ToString("R", CultureInfo.InvariantCulture),
                    (active.Length == 0 ? 0 : active.Average(x => diagnostics[x.i].y)).ToString("R", CultureInfo.InvariantCulture)));
            }
            Check(gpu.EmittedMl > 0, "F " + scenario + " exercised actual GPU liquid");
            Capture("F-" + scenario);
        }
        File.WriteAllLines(Path.Combine(ExperimentCohesiveValidation.Evidence, "cohesive-scenarios.csv"), rows);
    }

    private void Capture(string name)
    {
        var camera = world.interactor.inputCamera;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var target = new RenderTexture(640, 480, 24); target.Create();
        var texture = new Texture2D(640, 480, TextureFormat.RGBA32, false);
        try
        {
            foreach (var body in world.Items) body.transform.SetPositionAndRotation(body.Position, Quaternion.Euler(0, 0, body.Angle));
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(ExperimentCohesiveValidation.Evidence, name + ".png"), texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            target.Release(); Destroy(target); Destroy(texture);
        }
    }

    private ItemDef Ingredient() => world.Items.First(x => x.ingredient != null).ingredient;
    private Vector4[] ReadDiagnostics() => (Vector4[])typeof(FluidExperimentGpuLiquid).GetMethod("ReadCohesiveDiagnostics").Invoke(gpu, null);
    private static object Get(object instance, string name) => instance.GetType().GetField(name, Private | BindingFlags.Public).GetValue(instance);
    private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Private | BindingFlags.Public).SetValue(instance, value);
    private struct PairResult { public float distance, shear; public Vector2 leftVelocity, momentum; }
    private void Isolate()
    {
        world.enabled = false; world.interactor.enabled = false; gpu.automaticReadback = false;
        foreach (var body in world.Items)
        {
            body.SetHeld(false); body.Teleport(new Vector2(-50 - body.Id * 4, 20), 0);
            body.Body.simulated = false; body.pourMlPerSecond = 0;
        }
        gpu.ResetSimulation();
    }
    private void CheckState(string label)
    {
        Check(gpu.IsOperational, label + " GPU is operational");
        Check(gpu.Snapshot.Where(p => p.Active != 0).All(p => float.IsFinite(p.Position.x) && float.IsFinite(p.Position.y)
            && float.IsFinite(p.Velocity.x) && float.IsFinite(p.Velocity.y) && p.VolumeMl > 0), label + " particles stay finite");
        Check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .005, label + " total liquid ledger is conserved");
    }
    private void Check(bool success, string label)
    {
        report.Add((success ? "PASS " : "FAIL ") + label);
        if (!success) throw new Exception(label);
    }
    private void OnLog(string message, string stack, LogType type)
    {
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) errors.Add(message + "\n" + stack);
    }
    private void Finish(bool success)
    {
        finished = true; Application.logMessageReceived -= OnLog; Physics2D.gravity = oldGravity;
        ExperimentCohesiveValidation.Finish(success, string.Join("\n", report));
    }
    [Serializable] private sealed class LegacySnapshot { public ModeSnapshot[] modes; }
    [Serializable] private sealed class ModeSnapshot
    {
        public string mode; public float radius, particleMl; public bool improved, improvedSurface; public ParticleSnapshot[] particles;
    }
    [Serializable] private sealed class ParticleSnapshot
    {
        public Vector2 position, velocity; public float volume, ingredientA, ingredientB;
    }
}
