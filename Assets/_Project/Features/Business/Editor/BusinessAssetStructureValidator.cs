using System;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class BusinessAssetStructureValidator
    {
        [MenuItem("Tools/Slainte/Validate Business Asset Structure")]
        public static void ValidateFromMenu()
        {
            RunBatchValidation();
            EditorUtility.DisplayDialog(
                "Business Assets",
                "Business art, audio and prefab validation passed.",
                "OK");
        }

        public static void RunBatchValidation()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ValidateSpriteTree(BusinessAssetPaths.EnvironmentSpriteRoot, 12);
            ValidateSpriteTree(BusinessAssetPaths.CharacterSpriteRoot, 210);
            ValidateSpriteTree(BusinessAssetPaths.CustomerSpriteRoot, 97);
            ValidateSpriteTree(BusinessAssetPaths.OrderTicketSpriteRoot, 3);
            ValidateSpriteTree(BusinessAssetPaths.ConversationSpriteRoot, 2);
            ValidateRequiredAsset<AudioClip>(BusinessAssetPaths.TypewriterSfx);

            ValidatePrefab(BusinessAssetPaths.AffinityNotificationPrefab);
            ValidatePrefab(BusinessAssetPaths.CharacterPrefab);
            ValidatePrefab(BusinessAssetPaths.ChoiceButtonPrefab);
            ValidatePrefab(BusinessAssetPaths.ItemRowPrefab);

            Debug.Log(
                "[BusinessAssetStructureValidator] PASS: environment, character, customer, "
                + "conversation, order-ticket, audio and prefab assets resolved from the "
                + "Business feature.");
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
                ValidateRequiredAsset<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static void ValidatePrefab(string path)
        {
            GameObject prefab = ValidateRequiredAsset<GameObject>(path);
            int missingScriptCount = 0;
            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                missingScriptCount +=
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            }

            if (missingScriptCount > 0)
            {
                throw new InvalidOperationException(
                    $"Business prefab has {missingScriptCount} missing script(s): {path}");
            }
        }

        private static T ValidateRequiredAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"Business asset is missing: {path}");
            return asset;
        }
    }
}
