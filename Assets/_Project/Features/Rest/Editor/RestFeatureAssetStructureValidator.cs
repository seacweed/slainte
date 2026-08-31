using System;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class RestFeatureAssetStructureValidator
    {
        [MenuItem("Tools/Slainte/Validate Rest Feature Asset Structure")]
        public static void ValidateFromMenu()
        {
            RunBatchValidation();
            EditorUtility.DisplayDialog(
                "Rest Assets",
                "Rest episode, shop and prefab validation passed.",
                "OK");
        }

        public static void RunBatchValidation()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateSpriteTree(RestAssetPaths.EpisodeBoardSpriteRoot, 21);
            ValidateSpriteTree(RestAssetPaths.ShopSpriteRoot, 81);

            ValidatePrefab(RestAssetPaths.EpisodeTemplatePrefab);
            ValidatePrefab(RestAssetPaths.RecipeBookSlotPrefab);
            ValidatePrefab(RestAssetPaths.ShopCategoryButtonPrefab);
            ValidatePrefab(RestAssetPaths.StrangeCategoryButtonPrefab);
            ValidatePrefab(RestAssetPaths.UpgradeSlotPrefab);
            ProjectAssetValidationUtility.ValidateAssetTree(
                RestAssetPaths.RecipeBookContentRoot, 2, "Rest");
            ProjectAssetValidationUtility.ValidateAssetTree(
                RestAssetPaths.UpgradeContentRoot, 4, "Rest");

            Debug.Log(
                "[RestFeatureAssetStructureValidator] PASS: episode-board, shop and "
                + "prefab assets resolved from the Rest feature.");
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
                    throw new InvalidOperationException("Rest sprite is missing: " + assetPath);
            }
        }

        private static void ValidatePrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException("Rest prefab is missing: " + path);

            int missingScriptCount = 0;
            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                missingScriptCount +=
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            }

            if (missingScriptCount > 0)
            {
                throw new InvalidOperationException(
                    $"Rest prefab has {missingScriptCount} missing script(s): {path}");
            }
        }

    }
}
