using System;
using UnityEditor;
using UnityEngine;

namespace Slainte.Bartending.EditorTools
{
    public static class MetaballFluidInfrastructureValidator
    {
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
            LoadRequired<GameObject>(MetaballFluidAssetPaths.WaterParticlePrefab);
            LoadRequired<GameObject>(MetaballFluidAssetPaths.BlueLiquidPrefab);
            LoadRequired<GameObject>(MetaballFluidAssetPaths.GreenLiquidPrefab);
            LoadRequired<GameObject>(MetaballFluidAssetPaths.RedLiquidPrefab);
            LoadRequired<GameObject>(MetaballFluidAssetPaths.BubblePanelPrefab);
            LoadRequired<Material>(MetaballFluidAssetPaths.AccumulationMaterial);
            LoadRequired<Material>(MetaballFluidAssetPaths.MetaballMaterial);
            LoadRequired<Shader>(MetaballFluidAssetPaths.AccumulationShader);
            LoadRequired<Shader>(MetaballFluidAssetPaths.CompositeShader);
            LoadRequired<PhysicsMaterial2D>(MetaballFluidAssetPaths.ParticlePhysicsMaterial);
            LoadRequired<MonoScript>(MetaballFluidAssetPaths.PoolScript);
            LoadRequired<MonoScript>(MetaballFluidAssetPaths.RendererScript);

            Debug.Log(
                "[MetaballFluidInfrastructureValidator] PASS: five prefabs, materials, "
                + "shaders, physics and runtime scripts resolved from Bartending infrastructure.");
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
