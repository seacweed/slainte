using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment.Editor
{
    public static class FluidExperimentBuilder
    {
        public const string Root = "Assets/_Project/Features/Bartending/FluidGpuExperiment";
        public const string ScenePath = Root + "/Scenes/FluidGpuComparison.unity";
        [MenuItem("Slainte/Fluid GPU Experiment/Open Comparison")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        // Used only to finish wiring the new copied scene. No original assets are saved.
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before scene authoring.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var world = UnityEngine.Object.FindFirstObjectByType<FluidExperimentWorld>();
            if (world == null) throw new InvalidOperationException("Copied experiment world is missing.");
            var comparison = world.GetComponent<FluidExperimentComparison>();
            if (comparison == null) comparison = world.gameObject.AddComponent<FluidExperimentComparison>();
            comparison.world = world; comparison.initialMode = FluidExperimentMode.CReferenceSurface;
            world.showControls = false;
            world.liquid.referenceAccumulationShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/FluidExperimentReferenceAccumulation.shader");
            world.liquid.referenceCompositeShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/FluidExperimentReferenceComposite.shader");
            if (world.liquid.referenceAccumulationShader == null || world.liquid.referenceCompositeShader == null)
                throw new InvalidOperationException("Reference surface shaders are missing.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Comparison scene save failed.");
            Debug.Log("[FluidGpuExperiment] Authored isolated A/B/C comparison: " + ScenePath);
        }
    }
}
