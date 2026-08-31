using System;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class CoreAssetStructureValidator
    {
        [MenuItem("Tools/Slainte/Validate Core Asset Structure")]
        public static void ValidateFromMenu()
        {
            RunBatchValidation();
            EditorUtility.DisplayDialog(
                "Core Assets",
                "Core cutscene and settlement asset validation passed.",
                "OK");
        }

        public static void RunBatchValidation()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateSpriteTree(CoreAssetPaths.CutsceneSpriteRoot, 9);
            ValidateSpriteTree(CoreAssetPaths.SettlementSpriteRoot, 2);
            ValidatePrefab(CoreAssetPaths.SettlementLinePrefab);
            ProjectAssetValidationUtility.ValidateAssetTree(
                CoreAssetPaths.CutsceneContentRoot,
                3,
                "Core");
            ValidateProjectSetting(CoreAssetPaths.DefaultVolumeProfile);
            ValidateProjectSetting(CoreAssetPaths.InputActions);
            ValidateProjectSetting(CoreAssetPaths.UniversalRenderPipelineGlobalSettings);

            Debug.Log(
                "[CoreAssetStructureValidator] PASS: cutscene, settlement and project "
                + "settings assets resolved from their owned areas.");
        }

        private static void ValidateSpriteTree(string path, int expectedCount)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { path });
            if (guids.Length != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedCount} sprites under {path}, found {guids.Length}.");
            }

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAssetAtPath<Sprite>(assetPath) == null)
                    throw new InvalidOperationException("Core sprite is missing: " + assetPath);
            }
        }

        private static void ValidatePrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException("Core prefab is missing: " + path);

            int missingScriptCount = 0;
            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                missingScriptCount +=
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            }

            if (missingScriptCount > 0)
            {
                throw new InvalidOperationException(
                    $"Core prefab has {missingScriptCount} missing script(s): {path}");
            }
        }

        private static void ValidateProjectSetting(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                throw new InvalidOperationException("Project setting asset is missing: " + path);
        }

    }
}
