using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class ProjectAssetStructureValidator
    {
        [MenuItem("Tools/Slainte/Validate Project Asset Structure")]
        public static void ValidateFromMenu()
        {
            RunBatchValidation();
            EditorUtility.DisplayDialog(
                "Project Assets",
                "Core and feature asset validation passed.",
                "OK");
        }

        public static void RunBatchValidation()
        {
            CoreAssetStructureValidator.RunBatchValidation();
            MainMenuAssetStructureValidator.RunBatchValidation();
            BusinessAssetStructureValidator.RunBatchValidation();
            RestFeatureAssetStructureValidator.RunBatchValidation();
            Slainte.Editor.BartendingAssetStructureValidator.RunBatchValidation();

            Debug.Log(
                "[ProjectAssetStructureValidator] PASS: Core, MainMenu, Business, "
                + "Rest and Bartending asset structures are valid.");
        }
    }
}
