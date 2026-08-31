using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Slainte.Editor
{
    public static class BartendingAssetStructureValidator
    {
        [MenuItem("Tools/Slainte/Validate Bartending Asset Structure")]
        public static void ValidateFromMenu()
        {
            RunBatchValidation();
            EditorUtility.DisplayDialog(
                "Bartending Assets",
                "Bartending art and tool asset validation passed.",
                "OK");
        }

        public static void RunBatchValidation()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            BartendingArtContractValidator.ValidateAllOrThrow();
            GlassCollisionProfileValidator.ValidateAll();
            JiggerCollisionProfileValidator.ValidateAll();
            ValidateToolCabinetArt();
            ValidatePrefabs();

            Debug.Log(
                "[BartendingAssetStructureValidator] PASS: bottle, glass, tool cabinet, "
                + "ice, collision-profile and prefab assets resolved from the Bartending feature.");
        }

        private static void ValidateToolCabinetArt()
        {
            ValidateSprite(BartendingAssetPaths.ToolCabinetArtRoot + "tool_cabinet.png");
            ValidateSpriteFolder(BartendingAssetPaths.ToolArtRoot, 17);
            ValidateSpriteFolder(BartendingAssetPaths.IceArtRoot, 3);
        }

        private static void ValidateSpriteFolder(string path, int expectedCount)
        {
            string[] files = Directory.GetFiles(Path.GetFullPath(path), "*.png");
            if (files.Length != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedCount} sprites under {path}, found {files.Length}.");
            }

            foreach (string file in files)
                ValidateSprite(path + Path.GetFileName(file));
        }

        private static void ValidateSprite(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
                throw new InvalidOperationException("Bartending sprite is missing: " + path);
        }

        private static void ValidatePrefabs()
        {
            string[] paths =
            {
                BartendingAssetPaths.BeakerPrefab,
                BartendingAssetPaths.BottlePrefab,
                BartendingAssetPaths.CobblerShakerPrefab,
                BartendingAssetPaths.GlassPrefab,
                BartendingAssetPaths.IceCubePrefab,
                BartendingAssetPaths.OrangeJuiceBottlePrefab,
                BartendingAssetPaths.TestSlotPrefab,
                BartendingAssetPaths.LiquorShelfPrefabRoot + "BottleSlot.prefab",
                BartendingAssetPaths.LiquorShelfPrefabRoot + "CategoryButton.prefab",
                BartendingAssetPaths.LiquorShelfPrefabRoot + "LiquorInfoCard.prefab",
                BartendingAssetPaths.RecipeBookPrefabRoot + "RecipeIngredientRowUI.prefab",
                BartendingAssetPaths.RecipeBookPrefabRoot + "RecipeListItemUI.prefab",
                BartendingAssetPaths.RecipeBookPrefabRoot + "SearchOption.prefab"
            };

            foreach (string path in paths)
                ValidatePrefab(path);
        }

        private static void ValidatePrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException("Bartending prefab is missing: " + path);

            int missingScriptCount = 0;
            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                missingScriptCount +=
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            }

            if (missingScriptCount > 0)
            {
                throw new InvalidOperationException(
                    $"Bartending prefab has {missingScriptCount} missing script(s): {path}");
            }
        }
    }
}
