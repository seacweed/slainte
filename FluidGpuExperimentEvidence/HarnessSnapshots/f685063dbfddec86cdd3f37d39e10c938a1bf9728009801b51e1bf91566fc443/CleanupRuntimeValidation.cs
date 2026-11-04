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
using Slainte.Bartending.FluidGpuExperiment.Editor;

public static class ExperimentCleanupRuntimeValidation
{
    const string Key = "FluidExperiment.CleanupRuntimeValidation";
    public const string ComparisonScene = FluidExperimentBuilder.Root + "/Scenes/FluidGpuComparison.unity";

    public static void RequireDisposableHarness()
    {
        string project = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath)).Replace('\\', '/').TrimEnd('/');
        if (!Application.isBatchMode || !project.StartsWith("A:/UnityHub/proj/slainte/Library/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cleanup fixtures require hidden batchmode in a disposable descendant of the workspace Library.");
        string evidence = Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(evidence) || !Directory.Exists(evidence))
            throw new InvalidOperationException("An existing isolated evidence directory is required.");
    }

    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("CleanupRuntimeValidation").AddComponent<CleanupRuntimeValidationRunner>();
    };

    public static void Begin()
    {
        RequireDisposableHarness();
        EditorSceneManager.OpenScene(ComparisonScene);
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }

    // Destructive rebuilds are confined to the disposable project. Original files, GUIDs and
    // loaded-scene setup are restored in finally even when authoring or an assertion fails.
    public static void BeginAuthoring()
    {
        RequireDisposableHarness();
        var report = new List<string>(); bool success = false;
        void Check(bool value, string label)
        { report.Add((value ? "PASS " : "FAIL ") + label); if (!value) throw new Exception(label); }
        string[] generated = { FluidExperimentAssemblyBuilder.RuntimePrefabPath,
            FluidExperimentAssemblyBuilder.EnvironmentPrefabPath, FluidExperimentAssemblyBuilder.ShardPrefabPath,
            FluidExperimentAssemblyBuilder.ShakerPartPrefabPath, FluidExperimentAssemblyBuilder.SampleScenePath };
        string[] protectedAssets = Directory.GetFiles(FluidExperimentBuilder.Root, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToArray();
        var bytes = protectedAssets.ToDictionary(path => path, File.ReadAllBytes);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        string quarantine = Path.Combine("Temp", "CleanupAuthoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(quarantine);
        string movedScene = Path.Combine(quarantine, "FluidGpuComparison.unity");
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            File.Move(ComparisonScene, movedScene);
            File.Move(ComparisonScene + ".meta", movedScene + ".meta");
            foreach (string path in generated) if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Check(!File.Exists(ComparisonScene) && generated.All(path => !File.Exists(path)),
                "Comparison scene and generated runtime/environment/shard/part/sample assets are absent before rebuild");
            FluidExperimentAssemblyBuilder.BuildAll();
            Check(!File.Exists(ComparisonScene), "BuildAll does not recreate or require the comparison scene");
            Check(generated.All(path => File.Exists(path)), "BuildAll recreates all missing reusable assets without the comparison scene");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FluidExperimentAssemblyBuilder.RuntimePrefabPath);
            var rig = prefab != null ? prefab.GetComponent<FluidExperimentRuntimeRig>() : null;
            Check(rig != null && rig.world != null && rig.liquid != null && rig.interactor != null
                && prefab.GetComponent<FluidExperimentComparison>() == null,
                "Fresh runtime has its reusable modules and no sandbox client");
            Check(rig.liquidSettings != null && rig.liquidSettings.gpuLiquidComputeShader != null
                && rig.liquid.drawShader != null && rig.liquid.improvedSurfaceCompute != null
                && rig.liquid.improvedAccumulationShader != null && rig.liquid.improvedCompositeShader != null,
                "Fresh runtime binds actual current simulation and surface assets");
            var scene = EditorSceneManager.OpenScene(FluidExperimentAssemblyBuilder.SampleScenePath, OpenSceneMode.Additive);
            try
            {
                Check(scene.GetRootGameObjects().Any(root => root.GetComponent<FluidExperimentRuntimeRig>() != null),
                    "Fresh sample scene contains an assembled reusable runtime");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            success = true;
        }
        catch (Exception exception) { report.Add(exception.ToString()); }
        finally
        {
            try
            {
                foreach (string path in generated) if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                foreach (var pair in bytes)
                { Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)); File.WriteAllBytes(pair.Key, pair.Value); }
                // The source scene bytes/GUID were restored from the same pre-test snapshot.
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (setup.Any(scene => scene.isLoaded && scene.isActive))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Check(bytes.All(pair => File.Exists(pair.Key) && pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))),
                    "Disposable feature assets and metadata restore to their exact pre-rebuild bytes");
            }
            catch (Exception exception) { success = false; report.Add("RESTORE FAILURE " + exception); }
            string evidence = Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
            File.WriteAllLines(Path.Combine(evidence, "cleanup-authoring-validation.txt"), report);
            File.WriteAllText(Path.Combine(evidence, "cleanup-authoring-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}

public sealed class CleanupRuntimeValidationRunner : MonoBehaviour
{
    const float Dt = .02f;
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    readonly List<string> report = new List<string>(), errors = new List<string>();
    readonly List<FluidExperimentBody> cubes = new List<FluidExperimentBody>();
    readonly List<IceDiagnostic> diagnostics = new List<IceDiagnostic>();
    FluidExperimentComparison sandbox;
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentBody glass, icePrefab;
    ItemDef ingredient;
    float floor;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");

    [Serializable] public sealed class IceDiagnostic
    {
        public int iceCount, ticks;
        public float seconds, requestedMl, acceptedMl, finalMl, floorY, initialWaterHeight;
        public float maxWaterSpeed, peakSurfaceRise, lateWaterEnergy, maxIceSpeed, maxIceAngularSpeed, maxIceWallPenetration;
        public string scope = "Per-tick completed GPU snapshots in a floor-supported stationary glass with dynamic gravity-enabled ice. Speed/energy/surface maxima are diagnostics, not naturalness acceptance.";
    }
    [Serializable] public sealed class DiagnosticReport
    { public string naturalness = "NOT_VALIDATED"; public IceDiagnostic[] fixtures; }

    void Check(bool value, string label)
    { report.Add((value ? "PASS " : "FAIL ") + label); if (!value) throw new Exception(label); }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    static bool Finite(Vector2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);
    GpuLiquidParticle[] Read() { gpu.ReadbackNow(); return gpu.Snapshot; }

    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null; yield return null;
        bool success = false; var previousMode = Physics2D.simulationMode; var previousGravity = Physics2D.gravity;
        try
        {
            ExperimentCleanupRuntimeValidation.RequireDisposableHarness();
            sandbox = FindFirstObjectByType<FluidExperimentComparison>();
            Check(sandbox != null && sandbox.Ready, "Authored sandbox initializes current cohesive GPU liquid");
            world = sandbox.World; gpu = sandbox.Gpu;
            Check(gpu.CohesivePhysicsActive && gpu.IsOperational, "Current cohesive physics is operational in the real GPU runtime");
            Check(typeof(FluidExperimentDevelopmentBenchmark) != null,
                "Optional development benchmark compiles against the current runtime API");
            TestReset();
            sandbox.enabled = false; sandbox.showControls = false;
            world.seedLiquids = false; world.enabled = false; world.interactor.enabled = false; gpu.automaticReadback = false;
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = new Vector2(0, -9.81f);
            glass = world.Items.First(body => body.kind == LabItemKind.Glass && body.name == "Glass_highball");
            ingredient = world.Items.First(body => body.ingredient != null).ingredient;
            icePrefab = world.Items.First(body => body.kind == LabItemKind.IceBucket).icePrefab;
            floor = world.floorColliders.First(collider => collider != null && collider.bounds.min.x < 0 && collider.bounds.max.x > 0).bounds.max.y;
            TestDevelopmentVolume();
            TestIceModeConservation();
            float natural = TestBreakage(FluidExperimentBreakageStyle.Natural);
            float explosive = TestBreakage(FluidExperimentBreakageStyle.Explosive);
            Check(explosive > natural + .1f, "Natural/Explosive option changes actual fragment motion under the same native floor impact");
            PrepareEmpty();
            ExperimentRelativePointerChecks.Run(world, Check);
            StationaryIce(1); StationaryIce(3);
            Check(errors.Count == 0, "No Unity, native simulation, compute or shader errors during cleanup runtime validation");
            success = true;
        }
        catch (Exception exception) { report.Add(exception.ToString()); }
        finally
        {
            FluidExperimentPerformance.IsRecording = false;
            Physics2D.simulationMode = previousMode; Physics2D.gravity = previousGravity;
            Application.logMessageReceived -= Log; report.AddRange(errors);
            File.WriteAllLines(Path.Combine(Evidence, "cleanup-runtime-validation.txt"), report);
            File.WriteAllText(Path.Combine(Evidence, "cleanup-runtime-result.txt"), success ? "PASS" : "FAIL");
            File.WriteAllText(Path.Combine(Evidence, "cleanup-stationary-ice-diagnostics.json"),
                JsonUtility.ToJson(new DiagnosticReport { fixtures = diagnostics.ToArray() }, true));
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }

    void TestReset()
    {
        sandbox.ResetSandbox();
        var layout = world.Items.ToDictionary(body => body, body => (body.Position, body.Angle, body.remainingMl, body.iceStock));
        var bottle = world.Items.First(body => body.kind == LabItemKind.Bottle);
        Vector3 cameraPosition = world.interactor.inputCamera.transform.position;
        float cameraSize = world.interactor.inputCamera.orthographicSize;
        bottle.Teleport(new Vector2(-2, 7), 73); bottle.ResetSupply(1, 0);
        world.interactor.inputCamera.transform.position += Vector3.one;
        world.interactor.inputCamera.orthographicSize += 2;
        sandbox.ResetSandbox();
        Check(layout.All(pair => pair.Key != null && Vector2.Distance(pair.Key.Position, pair.Value.Position) < .0001f
            && Mathf.Abs(Mathf.DeltaAngle(pair.Key.Angle, pair.Value.Angle)) < .0001f
            && Mathf.Abs(pair.Key.remainingMl - pair.Value.remainingMl) < .001f && pair.Key.iceStock == pair.Value.iceStock),
            "Sandbox reset restores the actual authored body layout, angles and supplies");
        Check(world.interactor.inputCamera.transform.position == cameraPosition
            && Mathf.Abs(world.interactor.inputCamera.orthographicSize - cameraSize) < .0001f && world.interactor.enabled,
            "Sandbox reset restores its camera and active pointer input");
    }

    void DisableAutomatic()
    { world.enabled = false; world.interactor.enabled = false; gpu.automaticReadback = false; }

    void PrepareEmpty()
    {
        cubes.Clear(); sandbox.ResetSandbox(); DisableAutomatic(); world.SetDispenserActive(false);
        foreach (var body in world.Items.ToArray())
        { body.SetHeld(false); body.Teleport(new Vector2(-60 - body.Id * 3, 20), 0); body.Body.simulated = false; body.pourMlPerSecond = 0; }
        gpu.ResetSimulation(); Physics2D.SyncTransforms();
    }

    void StationGlass()
    {
        glass.Body.simulated = true; glass.Body.bodyType = RigidbodyType2D.Kinematic;
        glass.Body.interpolation = RigidbodyInterpolation2D.None; glass.Teleport(new Vector2(0, floor + 2), 0);
        Physics2D.SyncTransforms(); glass.Teleport(glass.Position + Vector2.up * (floor + .005f - glass.SolidBounds.min.y), 0);
        glass.SetSealed(false); glass.SynchronizeHistory(); Physics2D.SyncTransforms();
        Check(Mathf.Abs(glass.SolidBounds.min.y - floor - .005f) < .002f,
            "Stationary glass is supported at the real floor instead of suspended in air");
    }

    void Step()
    {
        world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms(); Physics2D.Simulate(Dt); world.TickLiquid(Dt);
    }

    void TestDevelopmentVolume()
    {
        var replay = world.gameObject.AddComponent<FluidExperimentDevelopmentScenarios>();
        try
        {
            replay.sandbox = sandbox; replay.automaticAdvance = false;
            replay.StartVolumeCheck(glass, 30); DisableAutomatic();
            for (int i = 0; i < 12; i++) Step(); Read();
            Check(replay.VolumeVessel == glass && Mathf.Abs(replay.QueuedVolumeMl - 30) < .001f
                && Mathf.Abs(gpu.SnapshotTotalMl - 30) < .001f,
                "Moved development volume fixture creates a real 30 ml current-liquid batch");
        }
        finally { DestroyImmediate(replay); sandbox.ResetSandbox(); DisableAutomatic(); }
    }

    void TestIceModeConservation()
    {
        PrepareEmpty(); StationGlass();
        Check(Mathf.Abs(gpu.Fill(glass, ingredient, 40) - 40) < .001f, "Collision-toggle fixture queues an actual 40 ml batch");
        for (int i = 0; i < 12; i++) Step(); Read();
        int generation = gpu.SimulationGeneration, active = gpu.ActiveCount;
        float volume = gpu.SnapshotTotalMl; int recordings = 0;
        Action<long> record = ticks => { if (ticks > 0) recordings++; };
        FluidExperimentPerformance.PhysicsRecorded += record;
        try
        {
            FluidExperimentPerformance.IsRecording = true;
            foreach (var mode in new[] { FluidExperimentIceLiquidMode.Colliding, FluidExperimentIceLiquidMode.NonColliding })
            {
                gpu.iceLiquidMode = mode; Step(); Read();
                Check(gpu.SimulationGeneration == generation && gpu.ActiveCount == active
                    && Mathf.Abs(gpu.SnapshotTotalMl - volume) < .001f && Mathf.Abs(gpu.VolumeIn(glass.Id) - 40) < .001f,
                    "Ice collision option preserves the live generation, active particles and ingredient volume: " + mode);
            }
            Check(recordings == 2, "Optional timing hook records actual GPU-step CPU submission only while enabled");
        }
        finally { FluidExperimentPerformance.IsRecording = false; FluidExperimentPerformance.PhysicsRecorded -= record; }
    }

    float TestBreakage(FluidExperimentBreakageStyle style)
    {
        PrepareEmpty(); sandbox.SetBreakageStyle(style);
        var target = world.Items.First(body => body.kind == LabItemKind.Glass && body.GetComponent<FluidExperimentBreakableGlass>() != null);
        var fracture = target.GetComponent<FluidExperimentBreakableGlass>();
        target.Body.simulated = true; target.Body.bodyType = RigidbodyType2D.Dynamic; target.Body.gravityScale = 1;
        target.Body.constraints = RigidbodyConstraints2D.None; target.Body.interpolation = RigidbodyInterpolation2D.None;
        target.Teleport(new Vector2(0, floor + 3), 0); Physics2D.SyncTransforms();
        target.Teleport(target.Position + Vector2.up * (floor + .1f - target.SolidBounds.min.y), 0);
        target.Body.linearVelocity = Vector2.down * 12; target.Body.angularVelocity = 0; target.SynchronizeHistory();
        for (int i = 0; i < 80 && !fracture.IsBroken; i++) Step();
        Check(fracture.IsBroken && world.GlassShards.Count == fracture.profile.shards.Length,
            style + " option produces the complete authored fragment set through real native floor impact");
        float speedSquared = world.GlassShards.Average(shard => shard.Body.linearVelocity.sqrMagnitude);
        Check(world.GlassShards.All(shard => Finite(shard.Body.position) && Finite(shard.Body.linearVelocity)
            && float.IsFinite(shard.Body.angularVelocity)), style + " fragment state is finite");
        report.Add(FormattableString.Invariant($"MEASURE breakageStyle={style} fragmentMeanSpeedSquared={speedSquared:R}"));
        return speedSquared;
    }

    void StationaryIce(int count)
    {
        PrepareEmpty(); sandbox.SetBreakageStyle(FluidExperimentBreakageStyle.Natural);
        gpu.iceLiquidMode = FluidExperimentIceLiquidMode.NonColliding; StationGlass();
        var diagnostic = new IceDiagnostic { iceCount = count, ticks = 250, seconds = 250 * Dt, requestedMl = 40, floorY = floor };
        diagnostics.Add(diagnostic);
        diagnostic.acceptedMl = gpu.Fill(glass, ingredient, diagnostic.requestedMl);
        Check(Mathf.Abs(diagnostic.acceptedMl - diagnostic.requestedMl) < .001f, count + "-ice stationary fixture fills its actual requested volume");
        for (int i = 0; i < 100; i++) Step();
        var settled = Read(); diagnostic.initialWaterHeight = settled.Where(particle => particle.Active != 0).Max(particle => particle.Position.y);
        Rect interior = glass.collisionProfile.InteriorBounds;
        Vector2[] local = count == 1 ? new[] { new Vector2(0, interior.yMin + .25f) }
            : new[] { new Vector2(-.23f, interior.yMin + .25f), new Vector2(.23f, interior.yMin + .25f), new Vector2(0, interior.yMin + .70f) };
        foreach (Vector2 point in local)
        {
            var ice = Instantiate(icePrefab, glass.LocalToWorld(point), Quaternion.identity, world.transform);
            ice.Teleport(glass.LocalToWorld(point), 0); typeof(FluidExperimentBody).GetMethod("MarkPlaced", Members).Invoke(ice, null);
            ice.Body.simulated = true; ice.Body.bodyType = RigidbodyType2D.Dynamic; ice.Body.gravityScale = 1;
            ice.Body.interpolation = RigidbodyInterpolation2D.None; ice.Body.linearVelocity = Vector2.zero; ice.Body.angularVelocity = 0;
            ice.SynchronizeHistory(); cubes.Add(ice);
        }
        Physics2D.SyncTransforms(); world.RefreshCollisionPairs();
        Check(cubes.Count == count && cubes.All(ice => ice.Body.bodyType == RigidbodyType2D.Dynamic && ice.Body.gravityScale == 1)
            && WallPenetration() < .002f, count + "-ice fixture starts without wall penetration and keeps native gravity enabled");
        Vector2 glassPosition = glass.Position;
        for (int i = 0; i < diagnostic.ticks; i++)
        {
            Step(); var snapshot = Read();
            foreach (var particle in snapshot)
            {
                if (particle.Active == 0) continue;
                if (!Finite(particle.Position) || !Finite(particle.Velocity) || !float.IsFinite(particle.VolumeMl))
                    throw new Exception(count + "-ice fixture produced non-finite completed particle state");
                diagnostic.maxWaterSpeed = Mathf.Max(diagnostic.maxWaterSpeed, particle.Velocity.magnitude);
                diagnostic.peakSurfaceRise = Mathf.Max(diagnostic.peakSurfaceRise, particle.Position.y - diagnostic.initialWaterHeight);
            }
            diagnostic.lateWaterEnergy = snapshot.Where(particle => particle.Active != 0)
                .Sum(particle => .5f * particle.VolumeMl * particle.Velocity.sqrMagnitude);
            diagnostic.maxIceWallPenetration = Mathf.Max(diagnostic.maxIceWallPenetration, WallPenetration());
            foreach (var ice in cubes)
            {
                if (!Finite(ice.Position) || !Finite(ice.Body.linearVelocity) || !float.IsFinite(ice.Body.angularVelocity))
                    throw new Exception(count + "-ice fixture produced non-finite native ice state");
                diagnostic.maxIceSpeed = Mathf.Max(diagnostic.maxIceSpeed, ice.Body.linearVelocity.magnitude);
                diagnostic.maxIceAngularSpeed = Mathf.Max(diagnostic.maxIceAngularSpeed, Mathf.Abs(ice.Body.angularVelocity));
            }
        }
        diagnostic.finalMl = gpu.SnapshotTotalMl;
        Check(gpu.CompositionSnapshot.All(value => float.IsFinite(value)), count + "-ice completed ingredient composition remains finite");
        Check(Mathf.Abs(diagnostic.finalMl - diagnostic.acceptedMl) < .001f
            && Vector2.Distance(glass.Position, glassPosition) < .0001f,
            count + "-ice stationary replay preserves total material volume and supported glass pose");
        Check(diagnostic.maxIceWallPenetration <= .025f, count + "-ice replay respects vessel-wall geometry within native contact tolerance");
        report.Add("DIAGNOSTIC_ONLY " + JsonUtility.ToJson(diagnostic));
        report.Add("NOT_VALIDATED stationary visual naturalness, human input feel and splash quality; no speed/energy maximum was relabelled as an acceptance pass.");
    }

    float WallPenetration()
    {
        float maximum = 0;
        foreach (var ice in cubes) foreach (var own in ice.solidColliders) foreach (var wall in glass.solidColliders)
        {
            if (own == null || wall == null || !own.enabled || !wall.enabled) continue;
            var distance = own.Distance(wall);
            if (distance.isValid && distance.isOverlapped) maximum = Mathf.Max(maximum, -distance.distance);
        }
        return maximum;
    }
}
