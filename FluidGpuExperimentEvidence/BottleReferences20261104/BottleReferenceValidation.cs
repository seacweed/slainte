using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;
using Slainte.Bartending.FluidGpuExperiment.Editor;

public static class BottleReferenceValidation
{
    public static void Begin()
    {
        var report = new List<string>();
        string evidence = Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
        void Check(bool value, string message)
        { report.Add((value ? "PASS " : "FAIL ") + message); if (!value) throw new Exception(message); }
        try
        {
            string root = FluidExperimentAssemblyBuilder.Root;
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
            var catalog = AssetDatabase.LoadAssetAtPath<FluidExperimentAuthoringCatalog>(FluidExperimentAssemblyBuilder.CatalogPath);
            var entry = catalog.entries.First(e => e.prefab.GetComponent<FluidExperimentBody>()?.kind == LabItemKind.Bottle);
            var body = entry.prefab.GetComponent<FluidExperimentBody>();
            var bottleEntryId = entry.id; var ingredient = entry.ingredient; var displayName = entry.displayName; var category = entry.category;
            var synchronize = typeof(FluidExperimentAssemblyBuilder).GetMethod("BuildCatalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            try
            {
                entry.ingredient = null; entry.displayName = "Stale bottle name"; entry.category = "Stale category";
                synchronize.Invoke(null, null);
                Check(entry.ingredient == body.ingredient && entry.displayName == body.displayName && entry.category == "Bottle"
                    && entry.id == bottleEntryId, "Rebuilding catalog refreshes stale bottle metadata while preserving stable identity");
                var entries = catalog.entries.ToArray(); synchronize.Invoke(null, null);
                Check(catalog.entries.SequenceEqual(entries), "Repeating catalog synchronization creates no duplicate entries");
            }
            finally
            {
                entry.ingredient = ingredient; entry.displayName = displayName; entry.category = category;
                EditorUtility.ClearDirty(catalog);
            }
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var errors = FluidExperimentPrefabAudit.ValidateAssets();
            Check(errors.Count == 0, "All catalog, prefab, script, runtime and scene bindings resolve: " + string.Join("; ", errors));
            Check(EditorSceneManager.GetSceneManagerSetup().Select(s => s.path).SequenceEqual(setup.Select(s => s.path)),
                "Read-only audit preserves the loaded scene setup");
            Check(before.All(pair => File.Exists(pair.Key) && pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)))
                && before.Count == Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length,
                "Read-only audit leaves every experiment asset byte-identical");
            foreach (string id in new[] { "rock", "highball", "hurricane", "martini" })
            {
                string path = root + "/Prefabs/Glass_" + id + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(prefab.GetComponent<FluidExperimentBody>().servingGlassId == id,
                    "Saved " + id + " prefab has its recipe identity");
                using (var session = new FluidExperimentDesignerSession(path))
                {
                    session.Validate();
                    Check(session.Body.servingGlassId == id, "Designer draft retains " + id + " identity with valid profiles");
                    session.Body.servingGlassId = string.Empty;
                    bool rejected = false;
                    try { session.Validate(); }
                    catch (InvalidOperationException error) { rejected = error.Message.Contains("recipe glass ID"); }
                    Check(rejected, "Designer rejects a missing " + id + " identity without saving the draft");
                }
            }
            foreach (string path in new[] { FluidExperimentBuilder.ScenePath, FluidExperimentAssemblyBuilder.SampleScenePath })
            {
                var scene = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    var glasses = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<FluidExperimentBody>(true))
                        .Where(b => b.kind == LabItemKind.Glass).ToArray();
                    Check(glasses.Length > 0 && glasses.All(b => !string.IsNullOrWhiteSpace(b.servingGlassId)
                        && PrefabUtility.IsPartOfPrefabInstance(b)),
                        Path.GetFileName(path) + " inherits identities through existing prefab connections");
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            Check(before.All(pair => pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))),
                "Designer validation and preview inspection preserve all authored bytes");
            File.WriteAllText(Path.Combine(evidence, "prefab-readiness-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(evidence, "prefab-readiness-result.txt"), "PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            report.Add(error.ToString());
            File.WriteAllText(Path.Combine(evidence, "prefab-readiness-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(evidence, "prefab-readiness-result.txt"), "FAIL");
            EditorApplication.Exit(1);
        }
    }
}
