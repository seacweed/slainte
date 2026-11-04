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
        [MenuItem("Slainte/Fluid GPU Experiment/Open Sandbox")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        // Keeps the authored sandbox layout while binding the active liquid assets.
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before scene authoring.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var world = UnityEngine.Object.FindFirstObjectByType<FluidExperimentWorld>();
            if (world == null) throw new InvalidOperationException("Copied experiment world is missing.");
            FluidExperimentDispenserBuilder.BindIfMissing(world);
            var comparison = world.GetComponent<FluidExperimentComparison>();
            if (comparison == null) comparison = world.gameObject.AddComponent<FluidExperimentComparison>();
            comparison.world = world;
            world.showControls = false;
            ConfigureLiquid(world.liquid);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Sandbox scene save failed.");
            Debug.Log("[FluidGpuExperiment] Active liquid sandbox is ready: " + ScenePath);
        }

        internal static void ConfigureLiquid(FluidExperimentGpuLiquid liquid)
        {
            if (liquid == null) throw new ArgumentNullException(nameof(liquid));
            if (liquid.settings == null)
                liquid.settings = AssetDatabase.LoadAssetAtPath<FluidExperimentLiquidSettings>(Root + "/Data/FluidExperimentLiquidSettings.asset");
            liquid.drawShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/FluidExperimentLiquid.shader");
            liquid.improvedSurfaceCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/FluidExperimentImprovedSurface.compute");
            liquid.improvedAccumulationShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/FluidExperimentImprovedAccumulation.shader");
            liquid.improvedCompositeShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/FluidExperimentImprovedComposite.shader");
            if (liquid.settings == null || liquid.drawShader == null || liquid.improvedSurfaceCompute == null
                || liquid.improvedAccumulationShader == null || liquid.improvedCompositeShader == null)
                throw new InvalidOperationException("Active liquid settings or shader assets are missing.");
        }
    }
}
