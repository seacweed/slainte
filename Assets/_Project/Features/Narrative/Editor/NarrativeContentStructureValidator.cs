using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class NarrativeContentStructureValidator
    {
        [MenuItem("Tools/Slainte/Validate Narrative Content Structure")]
        public static void ValidateFromMenu()
        {
            RunBatchValidation();
            EditorUtility.DisplayDialog(
                "Narrative Content",
                "Narrative source and generated content validation passed.",
                "OK");
        }

        public static void RunBatchValidation()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ProjectAssetValidationUtility.ValidateTextAssetTree(
                NarrativeAssetPaths.EpisodeSourceRoot, 15);
            ProjectAssetValidationUtility.ValidateTextAssetTree(
                NarrativeAssetPaths.ExportRoot, 3);

            Debug.Log(
                "[NarrativeContentStructureValidator] PASS: episode sources and "
                + "generated exports resolved from the Narrative feature.");
        }

    }
}
