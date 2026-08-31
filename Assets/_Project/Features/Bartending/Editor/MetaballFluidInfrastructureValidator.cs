using System;
using Slainte.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Bartending.EditorTools
{
    public static class MetaballFluidInfrastructureValidator
    {
        private const string SampleScenePath =
            "Assets/_Project/Scenes/Development/Samples/Sample_Scene.unity";
        private const string BeakerPrefabPath = BartendingAssetPaths.BeakerPrefab;
        private const string GlassPrefabPath = BartendingAssetPaths.GlassPrefab;

        [MenuItem("Slainte/Bartending/Validate Metaball Infrastructure")]
        public static void ValidateFromMenu()
        {
            RunBatchValidation();
            EditorUtility.DisplayDialog(
                "MetaballFluid",
                "Bartending 액체 인프라 에셋 검증을 통과했습니다.",
                "확인");
        }

        public static void RunBatchValidation()
        {
            GameObject waterParticle =
                LoadRequired<GameObject>(MetaballFluidAssetPaths.WaterParticlePrefab);
            GameObject blueLiquid =
                LoadRequired<GameObject>(MetaballFluidAssetPaths.BlueLiquidPrefab);
            GameObject greenLiquid =
                LoadRequired<GameObject>(MetaballFluidAssetPaths.GreenLiquidPrefab);
            GameObject redLiquid =
                LoadRequired<GameObject>(MetaballFluidAssetPaths.RedLiquidPrefab);
            LoadRequired<Material>(MetaballFluidAssetPaths.AccumulationMaterial);
            LoadRequired<Material>(MetaballFluidAssetPaths.MetaballMaterial);
            LoadRequired<Shader>(MetaballFluidAssetPaths.AccumulationShader);
            LoadRequired<Shader>(MetaballFluidAssetPaths.CompositeShader);
            LoadRequired<PhysicsMaterial2D>(MetaballFluidAssetPaths.ParticlePhysicsMaterial);
            LoadRequiredScript(MetaballFluidAssetPaths.PoolScript);
            LoadRequiredScript(MetaballFluidAssetPaths.ReactionScript);
            LoadRequiredScript(MetaballFluidAssetPaths.RecyclerScript);
            LoadRequiredScript(MetaballFluidAssetPaths.RendererScript);
            LoadRequiredScript(MetaballFluidAssetPaths.FullScreenQuadScript);
            LoadRequiredScript(MetaballFluidAssetPaths.DraggableBarScript);

            ValidateNoMissingScripts(waterParticle, MetaballFluidAssetPaths.WaterParticlePrefab);
            ValidateNoMissingScripts(blueLiquid, MetaballFluidAssetPaths.BlueLiquidPrefab);
            ValidateNoMissingScripts(greenLiquid, MetaballFluidAssetPaths.GreenLiquidPrefab);
            ValidateNoMissingScripts(redLiquid, MetaballFluidAssetPaths.RedLiquidPrefab);
            ValidateNoMissingScripts(
                LoadRequired<GameObject>(BeakerPrefabPath),
                BeakerPrefabPath);
            ValidateNoMissingScripts(
                LoadRequired<GameObject>(GlassPrefabPath),
                GlassPrefabPath);
            ValidateSceneHasNoMissingScripts(SampleScenePath);

            Debug.Log(
                "[MetaballFluidInfrastructureValidator] PASS: four liquid prefabs, materials, "
                + "shaders and physics resolved from infrastructure; Bartending liquid "
                + "runtime scripts compiled and prefab/scene references are intact.");
        }

        private static void LoadRequiredScript(string path)
        {
            MonoScript script = LoadRequired<MonoScript>(path);
            if (script.GetClass() == null)
                throw new InvalidOperationException($"Runtime script class is unresolved: {path}");
        }

        private static void ValidateNoMissingScripts(GameObject prefab, string path)
        {
            int missingScriptCount =
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab);
            if (missingScriptCount > 0)
            {
                throw new InvalidOperationException(
                    $"Prefab contains {missingScriptCount} missing script(s): {path}");
            }
        }

        private static void ValidateSceneHasNoMissingScripts(string path)
        {
            LoadRequired<SceneAsset>(path);

            Scene scene = SceneManager.GetSceneByPath(path);
            bool wasAlreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!wasAlreadyOpen)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            try
            {
                int missingScriptCount = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                    foreach (Transform child in transforms)
                    {
                        missingScriptCount +=
                            GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                                child.gameObject);
                    }
                }

                if (missingScriptCount > 0)
                {
                    throw new InvalidOperationException(
                        $"Scene contains {missingScriptCount} missing script(s): {path}");
                }
            }
            finally
            {
                if (!wasAlreadyOpen && scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"MetaballFluid asset is missing: {path}");

            return asset;
        }
    }
}
