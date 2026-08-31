using System;
using Slainte.Bartending;
using Slainte.Business;
using Slainte.Content;
using Slainte.TV;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class RuntimeResourceStructureValidator
    {
        [MenuItem("Tools/Slainte/Validate Runtime Resource Structure")]
        public static void ValidateFromMenu()
        {
            RunBatchValidation();
            EditorUtility.DisplayDialog(
                "Runtime Resources",
                "Feature-owned Resources validation passed.",
                "OK");
        }

        public static void RunBatchValidation()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ValidateCount<ItemDef>(ProjectResourcePaths.BartendingItems, 18);
            ValidateCount<ItemData>(ProjectResourcePaths.BartendingItems, 120);
            ValidateCount<CocktailRecipeDef>(ProjectResourcePaths.BartendingRecipes, 94);
            ValidateRequired<BusinessBartendingSettings>(
                ProjectResourcePaths.BartendingSettings);
            ValidateRequired<ToolCabinetCatalog>(
                ProjectResourcePaths.BartendingToolCabinetCatalog);
            ValidateRequired<LiquorShopCatalog>(
                ProjectResourcePaths.BartendingShopCatalog);

            ValidateRequired<BusinessOrderFlowSettings>(
                ProjectResourcePaths.BusinessOrderFlowSettings);
            ValidateRequired<CustomerVisitDatabase>(
                ProjectResourcePaths.BusinessCustomerVisitDatabase);

            ValidateCount<EpisodeData>(ProjectResourcePaths.NarrativeEpisodes, 15);
            ValidateCount<ChapterData>(ProjectResourcePaths.NarrativeChapters, 1);

            ValidateRequired<TVBroadcastDatabase>(ProjectResourcePaths.RestTvDatabase);
            ValidateCount<Sprite>(ProjectResourcePaths.RestEpisodeBoardSprites, 35);

            ValidateCount<AudioClip>(ProjectResourcePaths.CoreBgm, 9);
            ValidateCount<AudioClip>(ProjectResourcePaths.CoreSfx, 2);

            Debug.Log(
                "[RuntimeResourceStructureValidator] PASS: Bartending, Business, "
                + "Narrative, Rest and Core Resources paths are valid.");
        }

        private static void ValidateCount<T>(string path, int expectedCount)
            where T : UnityEngine.Object
        {
            T[] assets = Resources.LoadAll<T>(path);
            if (assets.Length != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedCount} {typeof(T).Name} assets under "
                    + $"Resources/{path}, found {assets.Length}.");
            }
        }

        private static void ValidateRequired<T>(string path)
            where T : UnityEngine.Object
        {
            if (Resources.Load<T>(path) == null)
            {
                throw new InvalidOperationException(
                    $"Required {typeof(T).Name} is missing: Resources/{path}");
            }
        }
    }
}
