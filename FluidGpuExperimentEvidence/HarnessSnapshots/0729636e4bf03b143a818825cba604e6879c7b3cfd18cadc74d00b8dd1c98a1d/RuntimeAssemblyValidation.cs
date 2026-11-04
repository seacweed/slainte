using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Slainte.Bartending.FluidGpuExperiment;
using Slainte.Bartending.FluidGpuExperiment.Editor;

public static class ExperimentRuntimeAssemblyValidation
{
    const string Key = "FluidExperiment.RuntimeAssemblyValidation";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("RuntimeAssemblyValidation").AddComponent<RuntimeAssemblyValidationRunner>();
    };

    public static void Begin()
    {
        var report = new List<string>();
        void Check(bool ok, string message)
        { report.Add((ok ? "PASS " : "FAIL ") + message); if (!ok) throw new Exception(message); }
        try
        {
            FluidExperimentAssemblyBuilder.BuildAll();
            var profile = AssetDatabase.LoadAssetAtPath<FluidExperimentBreakageProfile>(FluidExperimentAssemblyBuilder.Root + "/Data/HurricaneBreakage.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<FluidExperimentAuthoringCatalog>(FluidExperimentAssemblyBuilder.CatalogPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FluidExperimentAssemblyBuilder.RuntimePrefabPath);
            var rig = prefab.GetComponent<FluidExperimentRuntimeRig>();
            Check(catalog.entries.Length == 28 && catalog.entries.Select(entry => entry.id).Distinct().Count() == 28
                && catalog.entries.All(entry => entry.prefab != null && !string.IsNullOrEmpty(entry.category)),
                "Catalog exposes all 28 original prefabs with stable distinct identifiers and categories");
            Check(rig != null && rig.world != null && rig.liquid != null && rig.interactor != null && rig.effects != null
                && prefab.GetComponent<FluidExperimentComparison>() == null,
                "Production prefab has all runtime modules and excludes comparison UI");
            Check(rig.liquidSettings != null && rig.liquidSettings.gpuLiquidComputeShader != null && rig.liquid.drawShader != null
                && rig.liquid.referenceAccumulationShader != null && rig.liquid.referenceCompositeShader != null
                && rig.liquid.improvedSurfaceCompute != null && rig.liquid.improvedAccumulationShader != null
                && rig.liquid.improvedCompositeShader != null && rig.defaultSplashProfile != null,
                "Runtime prefab includes simulation, renderer and natural splash asset references");
            Check(profile.generatedGeometry != null && profile.shards.Length == 16 && profile.shardTemplate != null,
                "Legacy shard geometry migrates into a generated asset while preserving the shards API");
            var shakerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FluidExperimentAssemblyBuilder.Root + "/Prefabs/Shaker.prefab");
            var shakerBody = shakerPrefab.GetComponent<FluidExperimentBody>();
            Check(shakerBody.shakerProfile != null && shakerBody.shakerStrainerArt != null && shakerBody.shakerCapArt != null,
                "Shaker prefab binds editable part geometry and direct artwork references");
            string[] paths = new[] { FluidExperimentAssemblyBuilder.RuntimePrefabPath, FluidExperimentAssemblyBuilder.EnvironmentPrefabPath,
                FluidExperimentAssemblyBuilder.ShardPrefabPath, FluidExperimentAssemblyBuilder.ShakerPartPrefabPath,
                FluidExperimentAssemblyBuilder.CatalogPath, FluidExperimentAssemblyBuilder.SplashProfilePath, FluidExperimentAssemblyBuilder.ShakerProfilePath,
                FluidExperimentAssemblyBuilder.SampleScenePath, AssetDatabase.GetAssetPath(profile), AssetDatabase.GetAssetPath(profile.generatedGeometry) };
            var bytes = paths.ToDictionary(path => path, File.ReadAllBytes);
            int openScenes = SceneManager.sceneCount;
            FluidExperimentAssemblyBuilder.BuildAll();
            Check(paths.All(path => bytes[path].SequenceEqual(File.ReadAllBytes(path))) && SceneManager.sceneCount == openScenes,
                "A second BuildAll preserves authored bytes and leaves no extra scenes open");
            float original = profile.minimumImpactSpeed;
            profile.minimumImpactSpeed = 7.25f; EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            FluidExperimentAssemblyBuilder.BuildAll();
            Check(Mathf.Abs(profile.minimumImpactSpeed - 7.25f) < .0001f, "BuildAll preserves a designer's changed breakage threshold");
            profile.minimumImpactSpeed = original; EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(FluidExperimentAssemblyBuilder.SampleScenePath);
            string secondPath = AssetDatabase.GenerateUniqueAssetPath(FluidExperimentAssemblyBuilder.Root + "/Scenes/AssemblyValidationSecond.unity");
            var second = FluidExperimentAssemblyBuilder.CreateWorkingScene(secondPath);
            var other = FluidExperimentAssemblyBuilder.CreateSandbox(second,
                AssetDatabase.LoadAssetAtPath<GameObject>(FluidExperimentAssemblyBuilder.Root + "/Prefabs/Glass_highball.prefab"));
            other.gameObject.name = "IndependentRuntimeRig";
            other.environment.gameObject.SetActive(false);
            other.inputCamera.enabled = false;
            other.seedLiquids = false;
            // Keep the second rig's native fixture separate while testing instance-scoped GPU resources.
            other.GetComponentInChildren<FluidExperimentBody>().transform.position = new Vector2(5, 4);
            SessionState.SetString(Key + ".Report", string.Join("\n", report));
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        catch (Exception error)
        {
            report.Add(error.ToString());
            string evidence = Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
            File.WriteAllText(Path.Combine(evidence, "runtime-assembly-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(evidence, "runtime-assembly-result.txt"), "FAIL");
            EditorApplication.Exit(1);
        }
    }
}

public sealed class RuntimeAssemblyValidationRunner : MonoBehaviour
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    readonly List<string> report = new List<string>(), errors = new List<string>();
    FluidExperimentRuntimeRig rig, second;
    FluidExperimentBody glass;
    FluidExperimentBreakableGlass breakable;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool ok, string message)
    { report.Add((ok ? "PASS " : "FAIL ") + message); if (!ok) throw new Exception(message); }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    object Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, Members).Invoke(value, args);

    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        report.Add(SessionState.GetString("FluidExperiment.RuntimeAssemblyValidation.Report", ""));
        yield return null;
        var oldSimulation = Physics2D.simulationMode;
        var oldGravity = Physics2D.gravity;
        bool success = false;
        try
        {
            var rigs = FindObjectsByType<FluidExperimentRuntimeRig>(FindObjectsSortMode.None);
            rig = rigs.First(value => value.name != "IndependentRuntimeRig");
            second = rigs.First(value => value.name == "IndependentRuntimeRig");
            Check(rigs.Length == 2 && rigs.All(value => value.Ready && value.Liquid.CohesivePhysicsActive),
                "Two assembled scenes initialize independent cohesive GPU runtimes without manual component wiring");
            Check(rig.inputCamera == rig.Interactor.inputCamera && rig.inputCamera == rig.Liquid.outputCamera
                && rig.World.leftBoundary == rig.environment.leftBoundary && rig.World.liquidSurfaces.Contains(rig.environment.table),
                "Camera, interaction, environment and liquid table references are explicitly connected");
            Check(!rig.World.ExplosiveGlassBreakage && rig.World.shardLiquidCollision && rig.seedLiquids,
                "Sample starts with natural breakage, liquid-contact fragments and seeded liquid");
            foreach (var value in rigs)
            { value.World.enabled = false; value.Interactor.enabled = false; value.Liquid.automaticReadback = false; }
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = new Vector2(0, -9.81f);
            glass = rig.World.Items.First(body => body.GetComponent<FluidExperimentBreakableGlass>() != null);
            breakable = glass.GetComponent<FluidExperimentBreakableGlass>();
            Check(breakable.splashProfile == rig.defaultSplashProfile, "Sample glass receives the natural splash profile before simulation");
            var otherGlass = second.World.Items.First();
            otherGlass.Body.bodyType = RigidbodyType2D.Kinematic;
            second.Liquid.ResetSimulation();
            Check(second.Liquid.Fill(otherGlass, second.defaultIngredient, 20) >= 19.99f, "Independent rig accepts a separate ingredient batch");
            second.World.TickLiquid(.02f); second.Liquid.ReadbackNow();
            float otherVolume = second.Liquid.SnapshotTotalMl;
            rig.ResetRig(); DisableAutomatic();
            second.Liquid.ReadbackNow();
            Check(otherVolume >= 19.99f && Mathf.Abs(second.Liquid.SnapshotTotalMl - otherVolume) < .001f
                && rig.Liquid.settings != second.Liquid.settings,
                "Resetting one rig preserves the other's liquid and runtime settings instance");
            TestSettingsReplacement();
            TestCatalogSpawning();
            TestShakerProfilePreservation();
            TestScaledFragments();
            NativeDrop(FluidExperimentBreakageStyle.Natural, "natural");
            NativeDrop(FluidExperimentBreakageStyle.Explosive, "explosive");
            rig.ResetRig(); DisableAutomatic();
            Check(rig.World.GlassShards.Count == 0 && !breakable.IsBroken
                && rig.World.GetComponentsInChildren<FluidExperimentShardLiquidProxy>().Length == 0,
                "Reset restores the authored glass and removes fragment contacts");
            Check(errors.Count == 0, "No Unity errors or assertions during runtime assembly validation");
            success = true;
        }
        catch (Exception error) { report.Add(error.ToString()); }
        finally
        {
            Physics2D.simulationMode = oldSimulation; Physics2D.gravity = oldGravity;
            Application.logMessageReceived -= Log;
            report.AddRange(errors);
            File.WriteAllText(Path.Combine(Evidence, "runtime-assembly-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(Evidence, "runtime-assembly-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }

    void DisableAutomatic()
    {
        rig.World.enabled = false; rig.Interactor.enabled = false; rig.Liquid.automaticReadback = false;
    }

    void TestSettingsReplacement()
    {
        var original = rig.liquidSettings;
        var replacement = Instantiate(original);
        replacement.gpuLiquidParticleCapacity = 512;
        rig.liquidSettings = replacement; rig.ResetRig(); DisableAutomatic();
        Check(rig.Ready && rig.Liquid.settings != replacement && rig.Liquid.settings.gpuLiquidParticleCapacity == 512,
            "Replacing settings on a running rig rebuilds GPU resources from a private clone");
        rig.liquidSettings = original; rig.ResetRig(); DisableAutomatic();
        Destroy(replacement);
    }

    void TestCatalogSpawning()
    {
        rig.ResetRig(); DisableAutomatic();
        int initial = rig.World.Items.Count;
        foreach (var entry in rig.catalog.entries.GroupBy(entry => entry.category).Select(group => group.First()))
        {
            var spawned = rig.SpawnObject(entry.prefab, new Vector2(-5, 4));
            var body = spawned.GetComponent<FluidExperimentBody>();
            if (body != null)
            {
                Check(body.World == rig.World && rig.World.Items.Contains(body), "Spawn attaches explicit ownership for " + entry.category);
                body.Body.simulated = false;
                if (body.kind == LabItemKind.Shaker)
                {
                    Check(body.ShakerCap != null && body.ShakerStrainer != null && body.HasCap,
                        "Reusable shaker template initializes both owned parts");
                    body.Body.simulated = true;
                    Check(body.ShakerCap.TryPickUp(body.ShakerCap.Position), "Spawned shaker cap detaches through the existing interaction contract");
                    body.ShakerCap.Release(Vector2.zero);
                    Check(body.ShakerCap.TryAttach() && body.HasCap, "Spawned shaker cap reattaches without rebuilding the owner");
                }
            }
            else Check(spawned.GetComponent<FluidExperimentGarnishSource>() != null
                && spawned.GetComponentInParent<FluidExperimentWorld>() == rig.World, "Spawn preserves a reusable garnish source's world context");
            spawned.SetActive(false); Destroy(spawned);
        }
        Check(rig.World.Items.Count == initial, "Removing spawned category examples leaves no registered body residue");
        var sourceEntry = rig.catalog.entries.First(entry => entry.prefab.GetComponent<FluidExperimentGarnishSource>() != null);
        var temporarySource = rig.SpawnObject(sourceEntry.prefab, Vector2.zero);
        rig.ResetRig(); DisableAutomatic();
        Check(!temporarySource.activeInHierarchy, "Rig reset also clears dynamically spawned sources outside the body registry");
    }

    void TestScaledFragments()
    {
        rig.ResetRig(); DisableAutomatic();
        var prefab = rig.catalog.entries.First(entry => entry.prefab.name == "Glass_hurricane").prefab;
        Vector3 rigScale = rig.transform.localScale;
        rig.transform.localScale = new Vector3(1.5f, 1.5f, 1);
        var source = rig.SpawnObject(prefab, new Vector2(-3, 4)).GetComponent<FluidExperimentBody>();
        source.Teleport(new Vector2(-3, 4), 0);
        var fracture = source.GetComponent<FluidExperimentBreakableGlass>();
        Vector3 sourceScale = source.transform.lossyScale;
        Invoke(fracture, "QueueImpact", 10f, source.Body.mass * 10f, source.Position);
        Invoke(fracture, "CompleteBreak");
        Check(fracture.IsBroken && rig.World.GlassShards.All(shard => Vector3.Distance(shard.transform.lossyScale, sourceScale) < .0001f),
            "Fragment template preserves the source world scale under a scaled runtime rig");
        rig.transform.localScale = rigScale;
        rig.ResetRig(); DisableAutomatic();
    }

    void TestShakerProfilePreservation()
    {
        rig.ResetRig(); DisableAutomatic();
        var prefab = rig.catalog.entries.First(entry => entry.prefab.name == "Shaker").prefab;
        var stage = new GameObject("Legacy shaker fixture staging"); stage.SetActive(false);
        var legacyTemplate = Instantiate(prefab, stage.transform, false);
        legacyTemplate.GetComponent<FluidExperimentBody>().shakerProfile = null;
        var authored = rig.SpawnObject(prefab, new Vector2(-4, 4)).GetComponent<FluidExperimentBody>();
        var legacy = rig.SpawnObject(legacyTemplate, new Vector2(4, 4)).GetComponent<FluidExperimentBody>();
        bool Same(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < .000001f;
        Check(Same(authored.ShakerStrainer.SolidCollider.size, legacy.ShakerStrainer.SolidCollider.size)
            && Same(authored.ShakerCap.SolidCollider.size, legacy.ShakerCap.SolidCollider.size)
            && Same(authored.ShakerOutletLocal, legacy.ShakerOutletLocal)
            && Mathf.Abs(authored.ShakerOutletWidth - legacy.ShakerOutletWidth) < .000001f
            && authored.ShakerStrainerLiquidHulls.Length == legacy.ShakerStrainerLiquidHulls.Length
            && authored.ShakerStrainerLiquidHulls.SelectMany(hull => hull.points)
                .Zip(legacy.ShakerStrainerLiquidHulls.SelectMany(hull => hull.points), (a, b) => Same(a, b)).All(value => value),
            "Editable shaker profile exactly preserves legacy part shapes, outlet and liquid guide geometry");
        stage.SetActive(false); Destroy(stage);
        rig.ResetRig(); DisableAutomatic();
    }

    void NativeDrop(FluidExperimentBreakageStyle style, string label)
    {
        rig.breakageStyle = style; rig.seedLiquids = false;
        rig.ResetRig(); DisableAutomatic();
        foreach (var body in rig.World.Items.ToArray())
        {
            if (body == glass) continue;
            body.Body.simulated = false;
            body.Teleport(new Vector2(-50, 20), 0); body.pourMlPerSecond = 0;
        }
        rig.PrepareDrop(glass, .9f, Vector2.zero, 40);
        glass.Body.bodyType = RigidbodyType2D.Kinematic;
        for (int i = 0; i < 80; i++) Step();
        rig.Liquid.ReadbackNow();
        Check(rig.Liquid.VolumeIn(glass.Id) >= 39.99f, label + " fixture has 40 ml settled in the intact glass");
        Capture(label + "-before.png");
        glass.Body.bodyType = RigidbodyType2D.Dynamic; glass.Body.linearVelocity = new Vector2(.4f, -9);
        for (int i = 0; i < 100 && !breakable.BreakQueued; i++)
        {
            rig.World.SendMessage("FixedUpdate"); Physics2D.SyncTransforms(); Physics2D.Simulate(.01f);
            if (!breakable.BreakQueued) rig.World.TickLiquid(.01f);
        }
        Check(breakable.BreakQueued && breakable.LastImpactSpeed >= breakable.profile.minimumImpactSpeed,
            label + " uses a native floor impact above the authored threshold");
        rig.Liquid.ReadbackNow();
        var before = (GpuLiquidParticle[])rig.Liquid.Snapshot.Clone();
        var composition = (float[])rig.Liquid.CompositionSnapshot.Clone();
        float sourceSpin = glass.Body.angularVelocity;
        uint owner = glass.Id;
        Invoke(breakable, "CompleteBreak");
        rig.Liquid.ReadbackNow();
        var after = rig.Liquid.Snapshot;
        int owned = Enumerable.Range(0, before.Length).Count(i => before[i].Active != 0 && before[i].VesselId == owner);
        int kicked = Enumerable.Range(0, before.Length).Count(i => before[i].Active != 0 && before[i].VesselId == owner
            && Vector2.Distance(before[i].Velocity, after[i].Velocity) > .0001f);
        Check(owned > 0 && kicked > 0 && Enumerable.Range(0, before.Length).All(i => before[i].Active == after[i].Active)
            && composition.SequenceEqual(rig.Liquid.CompositionSnapshot),
            label + " changes existing owned particle velocity without creating liquid or changing composition");
        Check(breakable.IsBroken && rig.World.GlassShards.Count == 16
            && rig.World.GlassShards.All(shard => shard.GetComponent<FluidExperimentShardLiquidProxy>() != null)
            && rig.World.GlassShards.Sum(shard => shard.GetComponent<FluidExperimentShardLiquidProxy>().Segments.Count) > 0,
            label + " creates 16 physical fragments with separate thin-wall liquid proxies");
        float expectedSpin = style == FluidExperimentBreakageStyle.Explosive ? breakable.profile.explosionAngularSpeed : 0;
        Check(rig.World.ExplosiveGlassBreakage == (style == FluidExperimentBreakageStyle.Explosive)
            && rig.World.GlassShards.All(shard => Mathf.Abs(Mathf.Abs(shard.Body.angularVelocity - sourceSpin) - expectedSpin) < .01f),
            label + " style applies the expected natural or explosive fragment spin");
        for (int i = 0; i < 18; i++) Step();
        rig.Liquid.ReadbackNow(); Capture(label + "-after.png");
        Check(Math.Abs(rig.Liquid.Ledger.Total.ConservationErrorMl) < .001
            && Math.Abs(rig.Liquid.Ledger.Total.QueueErrorMl) < .001,
            label + " preserves the liquid ledger through native fragment and table contacts");
        Check(rig.Liquid.Snapshot.Where(particle => particle.Active != 0).All(particle =>
            float.IsFinite(particle.Position.x) && float.IsFinite(particle.Position.y)
            && float.IsFinite(particle.Velocity.x) && float.IsFinite(particle.Velocity.y)),
            label + " keeps the released liquid state finite");
        for (int i = 0; i < 80; i++) Step();
        rig.Liquid.ReadbackNow();
        var aboveTable = rig.Liquid.Snapshot.Where(particle => particle.Active != 0 && Mathf.Abs(particle.Position.x) < 6.5f).ToArray();
        float top = rig.environment.table.bounds.max.y;
        Check(aboveTable.Length > 0 && aboveTable.Count(particle => particle.Position.y >= top - rig.Liquid.Radius * 2) >= aboveTable.Length * .95f,
            label + " table collision prevents released liquid from passing through the table interior");
        Capture(label + "-settled.png");
    }

    void Step()
    {
        rig.World.SendMessage("FixedUpdate"); Physics2D.SyncTransforms(); Physics2D.Simulate(.01f);
        rig.World.TickLiquid(.01f);
    }

    void Capture(string file)
    {
        var camera = rig.inputCamera;
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var previousPosition = camera.transform.position; float previousSize = camera.orthographicSize;
        var target = new RenderTexture(960, 720, 24);
        var pixels = new Texture2D(960, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.transform.position = new Vector3(0, 1.6f, -20); camera.orthographicSize = 3;
            Physics2D.SyncTransforms(); camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Evidence, "runtime-" + file), pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; camera.transform.position = previousPosition; camera.orthographicSize = previousSize;
            RenderTexture.active = previousActive; target.Release(); Destroy(target); Destroy(pixels);
        }
    }
}
