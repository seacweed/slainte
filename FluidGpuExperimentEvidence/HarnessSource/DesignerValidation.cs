using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Slainte.Bartending.FluidGpuExperiment;
using Slainte.Bartending.FluidGpuExperiment.Editor;

// Runs only in the disposable Unity harness. All authored mutations target fixture assets.
public static class ExperimentDesignerValidation
{
    const string Folder = FluidExperimentBuilder.Root + "/DesignerValidationTemp";
    static readonly List<string> report = new List<string>();
    const string PlayKey = "FluidDesigner.Validation.Phase";
    const string PlayReport = "FluidDesigner.Validation.Report";
    static double phaseStart;
    [InitializeOnLoadMethod] static void RegisterPlayValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            string phase = SessionState.GetString(PlayKey, "");
            if (string.IsNullOrEmpty(phase)) return;
            if (state == PlayModeStateChange.EnteredPlayMode && phase == "ordinary-start")
            { SessionState.SetString(PlayKey, "ordinary-running"); phaseStart = EditorApplication.timeSinceStartup; }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (phase == "designer-start") SessionState.SetString(PlayKey, "designer-return");
                if (phase == "ordinary-running") SessionState.SetString(PlayKey, "ordinary-return");
                phaseStart = EditorApplication.timeSinceStartup;
            }
        };
        EditorApplication.update += ContinuePlayValidation;
    }
    public static void RunPlayTest()
    {
        try
        {
            FluidExperimentAssemblyBuilder.BuildAll();
            // Deliberately leave this scene unnamed and dirty: authoring must not save or close it.
            var unsaved = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("DesignerUnsavedMarker"); EditorSceneManager.MarkSceneDirty(unsaved);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(FluidExperimentBuilder.Root, "DesignerValidationTemp");
            string source = Folder + "/PlaySource.prefab";
            if (!AssetDatabase.CopyAsset(FluidExperimentBuilder.Root + "/Prefabs/Glass_hurricane.prefab", source))
                throw new IOException("Could not create play-test fixture prefab.");
            SessionState.SetString("FluidDesigner.Validation.Source", source);
            SessionState.SetString("FluidDesigner.Validation.SourceHash", Hash(source));
            var authored = AssetDatabase.LoadAssetAtPath<GameObject>(source).GetComponent<FluidExperimentBreakableGlass>();
            string profile = AssetDatabase.GetAssetPath(authored.profile);
            SessionState.SetString("FluidDesigner.Validation.Profile", profile);
            SessionState.SetString("FluidDesigner.Validation.ProfileHash", Hash(profile));
            var window = EditorWindow.GetWindow<FluidExperimentDesignerWindow>(); window.SelectPrefab(source);
            foreach (var edit in window.Session.Profiles) edit.cloneForPrefab = true;
            window.Session.Body.capacityMl = 654; window.Session.Breakable.profile.explosionSpeed = 7.25f; window.Session.MarkDirty();
            SessionState.SetString(PlayReport, ""); SessionState.SetString(PlayKey, "designer-start");
            FluidExperimentDesignerPlayTest.Start(window.Session, new FluidExperimentDesignerPlayTest.Request
            { height = 3, fillMl = 20, duration = 2, repeats = 2, explosive = true });
        }
        catch (Exception ex) { FinishPlayValidation(ex); }
    }
    static void ContinuePlayValidation()
    {
        string phase = SessionState.GetString(PlayKey, "");
        if (string.IsNullOrEmpty(phase)) return;
        if (phase == "ordinary-running")
        { if (EditorApplication.timeSinceStartup - phaseStart > .5) EditorApplication.isPlaying = false; return; }
        if (phase != "designer-return" && phase != "ordinary-return") return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            if (phase == "designer-return" && FluidExperimentDesignerPlayTest.PendingRestore() != null) return;
            var window = Resources.FindObjectsOfTypeAll<FluidExperimentDesignerWindow>().FirstOrDefault();
            if (window == null || window.Session == null)
            {
                if (EditorApplication.timeSinceStartup - phaseStart < 15) return;
                throw new Exception("Designer window did not recover its draft after Play Mode.");
            }
            bool ordinary = phase == "ordinary-return";
            PlayCheck(window.Session.Body.capacityMl == (ordinary ? 777 : 654)
                && Mathf.Abs(window.Session.Breakable.profile.explosionSpeed - 7.25f) < .0001f && window.Session.Dirty,
                ordinary ? "Ordinary Play button preserves the complete unsaved designer draft" : "Isolated play test restores the unsaved body/profile draft");
            string source = SessionState.GetString("FluidDesigner.Validation.Source", "");
            string profile = SessionState.GetString("FluidDesigner.Validation.Profile", "");
            PlayCheck(Hash(source) == SessionState.GetString("FluidDesigner.Validation.SourceHash", "")
                && Hash(profile) == SessionState.GetString("FluidDesigner.Validation.ProfileHash", ""),
                "Play preview and recovery leave authored prefab and shared profile bytes unchanged");
            var marker = GameObject.Find("DesignerUnsavedMarker");
            PlayCheck(marker != null && marker.scene.path == "" && marker.scene.isDirty && SceneManager.sceneCount == 1,
                "Play return restores the unnamed dirty scene and its unsaved marker without saving or replacing it");
            if (!ordinary)
            {
                string results = FluidExperimentDesignerPlayTest.Results;
                PlayCheck(results.Contains("Completed run 1:") && results.Contains("Completed run 2:")
                    && !results.Contains("TEST FAILED") && !results.Contains("ERROR:") && results.Contains("errors=0"),
                    "Two actual GPU/native-physics play-test repeats complete with metrics and no errors");
                SessionState.SetString(PlayReport, SessionState.GetString(PlayReport, "") + "INFO " + results);
                PlayCheck(EditorSceneManager.playModeStartScene == null, "Designer restores the previous default Play Mode start scene");
                window.Session.Body.capacityMl = 777; window.Session.MarkDirty();
                SessionState.SetString(PlayKey, "ordinary-start");
                EditorApplication.isPlaying = true;
            }
            else
            {
                PlayCheck(window.Session.Profiles.All(p => p.cloneForPrefab), "Private-profile save choices survive both Play Mode recovery paths");
                window.Session.Save();
                PlayCheck(AssetDatabase.LoadAssetAtPath<GameObject>(source).GetComponent<FluidExperimentBody>().capacityMl == 777
                    && Hash(profile) == SessionState.GetString("FluidDesigner.Validation.ProfileHash", ""),
                    "Recovered draft can explicitly save private profiles without changing the shared original");
                FinishPlayValidation(null);
            }
        }
        catch (Exception ex) { FinishPlayValidation(ex); }
    }
    static void PlayCheck(bool condition, string message)
    {
        SessionState.SetString(PlayReport, SessionState.GetString(PlayReport, "") + (condition ? "PASS " : "FAIL ") + message + "\n");
        if (!condition) throw new Exception(message);
    }
    static string Hash(string path)
    { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
    static void FinishPlayValidation(Exception error)
    {
        string text = SessionState.GetString(PlayReport, "") + (error != null ? error.ToString() : "");
        SessionState.EraseString(PlayKey);
        foreach (var window in Resources.FindObjectsOfTypeAll<FluidExperimentDesignerWindow>())
        { window.DiscardChanges(); window.Close(); }
        if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        string evidence = Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
        File.WriteAllText(Path.Combine(evidence, "designer-play-validation.txt"), text);
        File.WriteAllText(Path.Combine(evidence, "designer-play-result.txt"), error == null ? "PASS" : "FAIL");
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static void Check(bool condition, string message)
    { report.Add((condition ? "PASS " : "FAIL ") + message); if (!condition) throw new Exception(message); }
    public static void Run()
    {
        bool success = false; report.Clear();
        try { Validate(); success = true; }
        catch (Exception ex) { report.Add(ex.ToString()); }
        finally
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            string evidence = Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
            File.WriteAllText(Path.Combine(evidence, "designer-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(evidence, "designer-result.txt"), success ? "PASS" : "FAIL");
        }
        EditorApplication.Exit(success ? 0 : 1);
    }
    static void Validate()
    {
        FluidExperimentAssemblyBuilder.BuildAll();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(FluidExperimentBuilder.Root, "DesignerValidationTemp");
        string original = FluidExperimentBuilder.Root + "/Prefabs/Glass_hurricane.prefab";
        string a = Folder + "/GlassA.prefab", b = Folder + "/GlassB.prefab";
        Check(AssetDatabase.CopyAsset(original, a), "Fixture copies the authored hurricane without changing it");
        var contents = PrefabUtility.LoadPrefabContents(a);
        try
        {
            var body = contents.GetComponent<FluidExperimentBody>(); var breakable = contents.GetComponent<FluidExperimentBreakableGlass>();
            body.collisionProfile = Copy(body.collisionProfile, Folder + "/SharedCollision.asset");
            breakable.profile = Copy(breakable.profile, Folder + "/SharedBreakage.asset");
            // A missing optional splash gets an isolated default, never an eager authored write.
            breakable.splashProfile = null;
            PrefabUtility.SaveAsPrefabAsset(contents, a);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        Check(AssetDatabase.CopyAsset(a, b), "Two fixture prefabs start with shared settings");
        var shared = AssetDatabase.LoadAssetAtPath<FluidExperimentCollisionProfile>(Folder + "/SharedCollision.asset");
        var sharedBreakage = AssetDatabase.LoadAssetAtPath<FluidExperimentBreakageProfile>(Folder + "/SharedBreakage.asset");
        string unchangedBreakageBytes = File.ReadAllText(AssetDatabase.GetAssetPath(sharedBreakage));
        sharedBreakage.minimumImpactSpeed += .25f; EditorUtility.SetDirty(sharedBreakage);
        var unrelated = ScriptableObject.CreateInstance<FluidExperimentSplashProfile>();
        string unrelatedPath = Folder + "/UnrelatedDirty.asset"; AssetDatabase.CreateAsset(unrelated, unrelatedPath);
        string unrelatedBytes = File.ReadAllText(unrelatedPath);
        unrelated.strength = 3; EditorUtility.SetDirty(unrelated);
        var originalSharedMouth = shared.mouth; float originalSpeed = sharedBreakage.explosionSpeed;
        string prefabBytes = File.ReadAllText(a), profileBytes = File.ReadAllText(AssetDatabase.GetAssetPath(shared));
        using (var session = new FluidExperimentDesignerSession(a))
        {
            Check(session.Body != null && session.Root.scene.IsValid()
                && EditorSceneManager.IsPreviewScene(session.Root.scene), "Designer edits a loaded isolated prefab scene");
            Check(session.Body.collisionProfile != shared && !AssetDatabase.Contains(session.Body.collisionProfile)
                && session.Breakable.profile != sharedBreakage, "Every mutable existing profile is an unsaved clone");
            Check(session.Breakable.splashProfile != null && !AssetDatabase.Contains(session.Breakable.splashProfile)
                && AssetDatabase.LoadAssetAtPath<GameObject>(a).GetComponent<FluidExperimentBreakableGlass>().splashProfile == null,
                "Missing splash settings exist only on the preview until explicit Save");
            Check(session.Profiles.First(p => p.label == "Collision").references >= 2,
                "Shared settings show both dependent fixture prefabs");
            Undo.IncrementCurrentGroup(); Undo.RecordObject(session.Breakable.profile, "Designer test edit");
            session.Breakable.profile.explosionSpeed = originalSpeed + 3; Undo.FlushUndoRecordObjects(); session.MarkDirty();
            Check(sharedBreakage.explosionSpeed == originalSpeed, "Working profile changes cannot leak into authored settings");
            Undo.PerformUndo();
            Check(Mathf.Abs(session.Breakable.profile.explosionSpeed - originalSpeed) < .0001f, "Undo restores a working profile edit");
            Undo.PerformRedo();
            Check(Mathf.Abs(session.Breakable.profile.explosionSpeed - originalSpeed - 3) < .0001f, "Redo restores the staged profile change");
            session.Body.capacityMl = 123; session.Body.collisionProfile.mouth += new Vector2(.025f, .04f); session.MarkDirty();
            Check(File.ReadAllText(a) == prefabBytes && File.ReadAllText(AssetDatabase.GetAssetPath(shared)) == profileBytes,
                "Changing body and collision preview leaves authored prefab and profile bytes unchanged");
            string snapshot = session.SaveSnapshot(Folder + "/Snapshot");
            var snapshotBody = AssetDatabase.LoadAssetAtPath<GameObject>(snapshot).GetComponent<FluidExperimentBody>();
            Check(snapshotBody.capacityMl == 123 && snapshotBody.collisionProfile != shared
                && AssetDatabase.GetAssetPath(snapshotBody.collisionProfile).StartsWith(Folder + "/Snapshot/", StringComparison.Ordinal),
                "Play-test snapshot captures current body values with its own persisted profile assets");
            Check(File.ReadAllText(a) == prefabBytes && File.ReadAllText(AssetDatabase.GetAssetPath(shared)) == profileBytes,
                "Creating a play-test snapshot never saves the authored draft");
            Check(File.ReadAllText(unrelatedPath) == unrelatedBytes && EditorUtility.IsDirty(unrelated),
                "Snapshot export leaves unrelated dirty assets unsaved");
        }
        using (var reverted = new FluidExperimentDesignerSession(a))
            Check(reverted.Body.capacityMl != 123 && reverted.Body.collisionProfile.mouth == originalSharedMouth,
                "Reopening the source discards unsaved body and collision changes");
        using (var session = new FluidExperimentDesignerSession(a))
        {
            session.Body.capacityMl = 321;
            session.Body.collisionProfile.mouth = originalSharedMouth + Vector2.right * .03f;
            session.MarkDirty(); session.Save();
            Check(!session.Dirty && shared.mouth == originalSharedMouth + Vector2.right * .03f,
                "Explicit shared Save persists the modified shared profile");
            Check(File.ReadAllText(AssetDatabase.GetAssetPath(sharedBreakage)) == unchangedBreakageBytes
                && EditorUtility.IsDirty(sharedBreakage),
                "A shared profile unchanged in the designer is neither reserialized nor saved even when it was already dirty");
            Check(File.ReadAllText(unrelatedPath) == unrelatedBytes && EditorUtility.IsDirty(unrelated),
                "Explicit designer Save only persists its own profiles, preserving unrelated unsaved assets");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(b).GetComponent<FluidExperimentBody>().collisionProfile == shared,
                "Shared Save preserves the dependent prefab reference");
            Check(session.Breakable.splashProfile != null && !AssetDatabase.Contains(session.Breakable.splashProfile),
                "After saving, further preview edits still target a clone");
        }
        using (var reloaded = new FluidExperimentDesignerSession(a))
        {
            Check(reloaded.Body.capacityMl == 321 && reloaded.Body.collisionProfile.mouth == shared.mouth,
                "Saved body and geometry settings survive prefab reload");
            var splash = reloaded.Profiles.First(p => p.label == "Splash");
            Check(splash.original != null && AssetDatabase.Contains(splash.original), "Explicit Save creates and references the previously missing splash asset");
            string unchangedCollisionBytes = File.ReadAllText(AssetDatabase.GetAssetPath(shared));
            reloaded.Body.capacityMl += 1; reloaded.MarkDirty(); reloaded.Save();
            Check(File.ReadAllText(AssetDatabase.GetAssetPath(shared)) == unchangedCollisionBytes
                && File.ReadAllText(AssetDatabase.GetAssetPath(sharedBreakage)) == unchangedBreakageBytes,
                "Body-only Save preserves exact bytes of all existing unchanged shared profiles");
            reloaded.Body.capacityMl = 321;
            foreach (var profile in reloaded.Profiles) profile.cloneForPrefab = true;
            reloaded.Body.collisionProfile.mouth += Vector2.up * .12f; reloaded.Breakable.profile.explosionSpeed = 8;
            Vector2 oldMouth = shared.mouth; reloaded.MarkDirty(); reloaded.Save();
            var savedA = AssetDatabase.LoadAssetAtPath<GameObject>(a).GetComponent<FluidExperimentBody>();
            var savedB = AssetDatabase.LoadAssetAtPath<GameObject>(b).GetComponent<FluidExperimentBody>();
            Check(savedA.collisionProfile != shared && savedB.collisionProfile == shared && shared.mouth == oldMouth,
                "Clone-for-prefab saves a private asset and leaves the other prefab's shared geometry unchanged");
            Check(savedA.GetComponent<FluidExperimentBreakableGlass>().profile != sharedBreakage && sharedBreakage.explosionSpeed == originalSpeed,
                "Private breakage tuning cannot alter the shared source profile");
            Check(!savedA.collisionProfile.name.Contains("preview") && savedA.collisionProfile.hideFlags == HideFlags.None,
                "Saved private settings have normal persistent asset flags and names");
            string bytes = File.ReadAllText(a);
            reloaded.Body.rotationPivotLocal = new Vector2(float.NaN, 0);
            bool rejected = false; try { reloaded.Save(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && File.ReadAllText(a) == bytes, "Non-finite pivot edits fail validation before touching the saved prefab");
        }
        VariantChecks(a);
        string sourceOriginal = FluidExperimentBuilder.Root + "/Prefabs/nanangaPeel_Source.prefab";
        string sourceFixture = Folder + "/GarnishSource.prefab";
        Check(AssetDatabase.CopyAsset(sourceOriginal, sourceFixture), "Source fixture copies the fixed garnish dispenser");
        using (var source = new FluidExperimentDesignerSession(sourceFixture))
        {
            Check(source.Body == null && source.Source != null, "Catalog source objects can open without a physics body");
            source.Source.displayName = "Designer source fixture"; source.MarkDirty(); source.Save();
        }
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(sourceFixture).GetComponent<FluidExperimentGarnishSource>().displayName == "Designer source fixture",
            "Garnish source settings save and reload through the same designer workflow");
        string plainGlass = Folder + "/PlainGlass.prefab";
        Check(AssetDatabase.CopyAsset(FluidExperimentBuilder.Root + "/Prefabs/Glass_highball.prefab", plainGlass),
            "Unbreakable-glass fixture is available for component Undo");
        using (var plain = new FluidExperimentDesignerSession(plainGlass))
        {
            Undo.IncrementCurrentGroup(); plain.EnableBreakage(); Undo.FlushUndoRecordObjects();
            Check(plain.Breakable != null && plain.Profiles.Any(p => p.label == "Breakage"),
                "Enable breakage stages a component and private tuning without modifying the source prefab");
            Undo.PerformUndo(); plain.ReconcileUndo();
            Check(plain.Breakable == null && plain.Profiles.All(p => p.owner != null),
                "Undoing component creation removes its profile editors without stale owners");
            plain.Save();
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(plainGlass).GetComponent<FluidExperimentBreakableGlass>() == null,
                "Saving after component Undo remains valid and keeps the prefab unbreakable");
        }
        var invalid = ScriptableObject.CreateInstance<FluidExperimentCollisionProfile>();
        try
        {
            invalid.solids = new[] { new FluidExperimentHull { points = new[] { Vector2.zero, Vector2.right, Vector2.right * 2 } } };
            Check(!FluidExperimentDesignerSession.IsValidGeometry(invalid, out _), "Collinear zero-area solid hulls are rejected");
            invalid.solids[0].points = new[] { Vector2.zero, Vector2.one, Vector2.up, Vector2.right };
            Check(!FluidExperimentDesignerSession.IsValidGeometry(invalid, out _), "Crossing solid hull edges are rejected");
        }
        finally { UnityEngine.Object.DestroyImmediate(invalid); }
        string shakerFixture = Folder + "/Shaker.prefab";
        Check(AssetDatabase.CopyAsset(FluidExperimentBuilder.Root + "/Prefabs/Shaker.prefab", shakerFixture),
            "Shaker fixture uses the generated reusable settings");
        using (var shaker = new FluidExperimentDesignerSession(shakerFixture))
        {
            var profile = shaker.Profiles.First(p => p.label == "Shaker");
            Check(shaker.Body.shakerProfile != profile.original && !AssetDatabase.Contains(shaker.Body.shakerProfile),
                "Shaker geometry and part physics are isolated in a cloned profile");
            float mass = shaker.Body.shakerProfile.capMass;
            profile.cloneForPrefab = true; shaker.Body.shakerProfile.capMass = mass * 2; shaker.MarkDirty(); shaker.Save();
            Check(((FluidExperimentShakerProfile)profile.original).capMass == mass * 2,
                "Private shaker profile values persist through explicit Save");
        }
        Scene oldActive = SceneManager.GetActiveScene();
        var setupBefore = EditorSceneManager.GetSceneManagerSetup();
        bool wasDirty = oldActive.isDirty;
        Scene test = FluidExperimentAssemblyBuilder.CreateWorkingScene(Folder + "/Assembly.unity");
        try
        {
            var rig = FluidExperimentAssemblyBuilder.CreateSandbox(test, AssetDatabase.LoadAssetAtPath<GameObject>(a));
            Check(rig != null && rig.World != null && rig.Liquid != null && rig.Interactor != null && rig.inputCamera != null,
                "Scene assembly wires a standalone runtime, GPU liquid, input and camera");
            Check(rig.GetComponent<FluidExperimentComparison>() == null && rig.World.shardLiquidCollision
                && rig.breakageStyle == FluidExperimentBreakageStyle.Natural,
                "Production assembly defaults to natural breakage and shard-liquid contact without comparison UI");
            Check(rig.GetComponentsInChildren<FluidExperimentBody>(true).Any(body => body.capacityMl == 321),
                "Scene assembly uses the edited prefab's saved body settings");
            Check(PrefabUtility.IsPartOfPrefabInstance(rig.gameObject)
                && PrefabUtility.IsPartOfPrefabInstance(rig.environment.gameObject), "Runtime and environment remain reusable prefab instances");
            Check(rig.defaultSplashProfile != null && rig.catalog != null && rig.catalog.entries.Length >= 28,
                "Runtime composition supplies default splash tuning and the complete object catalog");
        }
        finally { EditorSceneManager.CloseScene(test, true); if (oldActive.IsValid()) SceneManager.SetActiveScene(oldActive); }
        var setupAfter = EditorSceneManager.GetSceneManagerSetup();
        Check(setupAfter.Length == setupBefore.Length && oldActive.isLoaded && oldActive.isDirty == wasDirty,
            "Additive sandbox cleanup preserves existing scene setup and unsaved-state flags");
    }
    static T Copy<T>(T original, string path) where T : ScriptableObject
    {
        var copy = UnityEngine.Object.Instantiate(original); copy.name = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(copy, path); return copy;
    }
    static void VariantChecks(string source)
    {
        // Outside the feature folder: the exact UI operation must register this persistently.
        const string variantPath = "Assets/FluidDesignerValidationVariant.prefab";
        var catalog = AssetDatabase.LoadAssetAtPath<FluidExperimentAuthoringCatalog>(FluidExperimentAssemblyBuilder.CatalogPath);
        var entriesBefore = catalog.entries;
        var baseBody = AssetDatabase.LoadAssetAtPath<GameObject>(source).GetComponent<FluidExperimentBody>();
        string baseBytes = File.ReadAllText(source);
        string baseProfile = AssetDatabase.GetAssetPath(baseBody.GetComponent<FluidExperimentBreakableGlass>().profile);
        string profileBytes = File.ReadAllText(baseProfile);
        var createdProfiles = new List<string>();
        try
        {
            var variant = FluidExperimentDesignerSession.CreateVariant(source, variantPath);
            Check(PrefabUtility.GetPrefabAssetType(variant) == PrefabAssetType.Variant
                && AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(variant)) == source,
                "Create Variant produces a connected Unity prefab variant of the selected source");
            Check(catalog.entries.Any(e => e != null && e.prefab == variant)
                && FluidExperimentDesignerSession.CatalogPrefabs().Contains(variant),
                "A variant outside the feature folder is persistently registered and visible in the catalog");
            using (var session = new FluidExperimentDesignerSession(variantPath))
            {
                foreach (var edit in session.Profiles) edit.cloneForPrefab = true;
                session.Body.capacityMl = 432; session.Breakable.profile.explosionSpeed = 11;
                session.MarkDirty(); session.Save();
                createdProfiles.AddRange(session.Profiles.Select(p => AssetDatabase.GetAssetPath(p.original)));
            }
            variant = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
            var body = variant.GetComponent<FluidExperimentBody>();
            Check(PrefabUtility.GetPrefabAssetType(variant) == PrefabAssetType.Variant
                && AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(variant)) == source
                && body.capacityMl == 432 && body.GetComponent<FluidExperimentBreakableGlass>().profile.explosionSpeed == 11,
                "Saving edited variant settings preserves its base link and persists its overrides");
            Check(File.ReadAllText(source) == baseBytes && File.ReadAllText(baseProfile) == profileBytes
                && body.GetComponent<FluidExperimentBreakableGlass>().profile != baseBody.GetComponent<FluidExperimentBreakableGlass>().profile,
                "Variant edits with private profiles leave base prefab and base tuning bytes unchanged");
            FluidExperimentAssemblyBuilder.BuildAll();
            Check(FluidExperimentDesignerSession.CatalogPrefabs().Contains(variant),
                "Rebuilding reusable assets retains the externally registered variant");
        }
        finally
        {
            catalog.entries = entriesBefore; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
            AssetDatabase.DeleteAsset(variantPath);
            foreach (string path in createdProfiles) if (path.StartsWith("Assets/FluidDesignerValidationVariant_", StringComparison.Ordinal)) AssetDatabase.DeleteAsset(path);
        }
    }
}
