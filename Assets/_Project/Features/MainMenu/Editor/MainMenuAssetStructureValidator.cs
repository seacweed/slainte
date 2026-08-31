using System;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class MainMenuAssetStructureValidator
    {
        [MenuItem("Tools/Slainte/Validate Main Menu Asset Structure")]
        public static void ValidateFromMenu()
        {
            RunBatchValidation();
            EditorUtility.DisplayDialog(
                "Main Menu Assets",
                "Main menu title asset validation passed.",
                "OK");
        }

        public static void RunBatchValidation()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string[] guids = AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { MainMenuAssetPaths.TitleSpriteRoot });

            if (guids.Length != 21)
            {
                throw new InvalidOperationException(
                    $"Expected 21 title sprites under "
                    + $"{MainMenuAssetPaths.TitleSpriteRoot}, found {guids.Length}.");
            }

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAssetAtPath<Sprite>(assetPath) == null)
                    throw new InvalidOperationException("Main menu sprite is missing: " + assetPath);
            }

            Debug.Log(
                "[MainMenuAssetStructureValidator] PASS: 21 title sprites "
                + "resolved from the MainMenu feature.");
        }
    }
}
