using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;
using Slainte.Bartending.FluidGpuExperiment.Editor;

public static class CocktailSetupValidation
{
    public static void Begin()
    {
        string evidence = Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
        var report = new List<string>(); bool success = false;
        void Check(bool ok, string label) { report.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) throw new Exception(label); }
        try
        {
            string root = FluidExperimentAssemblyBuilder.Root;
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            var errors = FluidExperimentPrefabAudit.ValidateAssets();
            Check(errors.Count == 0, "Saved prefab and scene bindings remain valid: " + string.Join("; ", errors));
            FluidExperimentAssemblyBuilder.BuildAll();
            FluidExperimentAssemblyBuilder.BuildAll();
            Check(before.All(pair => pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))),
                "Repeated authoring preserves all existing prefab, scene, profile, shader and source bytes");
            var runtime = AssetDatabase.LoadAssetAtPath<GameObject>(FluidExperimentAssemblyBuilder.RuntimePrefabPath);
            var session = runtime.GetComponent<FluidExperimentCocktailSession>();
            Check(session != null && session.world == runtime.GetComponent<FluidExperimentWorld>() && session.uiFont != null && !session.panelOpen,
                "Reusable runtime serializes the explicit session/world/font references with its panel initially closed");
            foreach (string path in new[] { FluidExperimentBuilder.ScenePath, FluidExperimentAssemblyBuilder.SampleScenePath })
            {
                var scene = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    var sessions = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<FluidExperimentCocktailSession>(true)).ToArray();
                    Check(sessions.Length == 1 && sessions[0].world != null && sessions[0].uiFont != null,
                        Path.GetFileName(path) + " connects one independent cocktail session through saved assets");
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            success = true;
        }
        catch (Exception error) { report.Add(error.ToString()); }
        File.WriteAllLines(Path.Combine(evidence, "cocktail-setup-validation.txt"), report);
        File.WriteAllText(Path.Combine(evidence, "cocktail-setup-result.txt"), success ? "PASS" : "FAIL");
        EditorApplication.Exit(success ? 0 : 1);
    }
}
