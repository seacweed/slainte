using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NarrativeFlow.Runtime;
using Slainte.Content;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class StreamingAssetStructureValidator
    {
        private static readonly string[] BartendingFiles =
        {
            ProjectStreamingAssetPaths.BartendingIngredients,
            ProjectStreamingAssetPaths.BartendingOrderTemplates,
            ProjectStreamingAssetPaths.BartendingRecipeIngredients,
            ProjectStreamingAssetPaths.BartendingRecipes
        };

        private static readonly string[] NarrativeFiles =
        {
            "New Narrative Graph_Baked.json",
            "StrangeCoin_0_Baked.json",
            "StrangeCoin_1_Baked.json"
        };

        [MenuItem("Tools/Slainte/Validate StreamingAssets Structure")]
        public static void ValidateFromMenu()
        {
            RunBatchValidation();
            EditorUtility.DisplayDialog(
                "StreamingAssets",
                "Bartending and Narrative StreamingAssets validation passed.",
                "OK");
        }

        public static void RunBatchValidation()
        {
            EnsureLegacyDirectoryIsMissing("Data");
            EnsureLegacyDirectoryIsMissing("NarrativeData");

            string bartendingRoot = ValidateFileSet(
                ProjectStreamingAssetPaths.Bartending,
                BartendingFiles);
            string narrativeRoot = ValidateFileSet(
                ProjectStreamingAssetPaths.Narrative,
                NarrativeFiles);

            ValidateCsvFiles(bartendingRoot);
            ValidateNarrativeFiles(narrativeRoot);

            Debug.Log(
                "[StreamingAssetStructureValidator] PASS: Bartending and Narrative "
                + "StreamingAssets paths and files are valid.");
        }

        private static string ValidateFileSet(string relativeDirectory, IEnumerable<string> expectedFiles)
        {
            string root = Path.Combine(Application.streamingAssetsPath, relativeDirectory);
            if (!Directory.Exists(root))
                throw new InvalidOperationException($"StreamingAssets directory is missing: {root}");

            string[] expected = expectedFiles.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            string[] actual = Directory
                .GetFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected StreamingAssets files in '{relativeDirectory}'. "
                    + $"Expected [{string.Join(", ", expected)}], "
                    + $"actual [{string.Join(", ", actual)}].");
            }

            return root;
        }

        private static void ValidateCsvFiles(string root)
        {
            foreach (string fileName in BartendingFiles)
            {
                string path = Path.Combine(root, fileName);
                string header = File.ReadLines(path).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(header))
                    throw new InvalidOperationException($"StreamingAssets CSV is empty: {path}");
            }
        }

        private static void ValidateNarrativeFiles(string root)
        {
            foreach (string fileName in NarrativeFiles)
            {
                string path = Path.Combine(root, fileName);
                RuntimeNarrativeData data =
                    JsonUtility.FromJson<RuntimeNarrativeData>(File.ReadAllText(path));
                if (data?.Nodes == null)
                    throw new InvalidOperationException($"Baked narrative JSON is invalid: {path}");
            }
        }

        private static void EnsureLegacyDirectoryIsMissing(string relativeDirectory)
        {
            string path = Path.Combine(Application.streamingAssetsPath, relativeDirectory);
            if (Directory.Exists(path))
                throw new InvalidOperationException($"Legacy StreamingAssets directory remains: {path}");
        }
    }
}
