using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    internal static class ProjectAssetValidationUtility
    {
        public static void ValidateAssetTree(
            string path,
            int expectedCount,
            string owner)
        {
            string[] files = Directory.GetFiles(
                Path.GetFullPath(path),
                "*.asset",
                SearchOption.AllDirectories);
            if (files.Length != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedCount} assets under {path}, found {files.Length}.");
            }

            foreach (string file in files)
            {
                string assetPath = ToAssetPath(file);
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                    throw new InvalidOperationException(owner + " content is missing: " + assetPath);
            }
        }

        public static void ValidateTextAssetTree(string path, int expectedCount)
        {
            string[] files = Directory.GetFiles(
                Path.GetFullPath(path),
                "*.*",
                SearchOption.AllDirectories);
            int count = 0;
            foreach (string file in files)
            {
                string extension = Path.GetExtension(file);
                if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                count++;
                string assetPath = ToAssetPath(file);
                if (AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath) == null)
                    throw new InvalidOperationException("Narrative text asset is missing: " + assetPath);
            }

            if (count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedCount} text assets under {path}, found {count}.");
            }
        }

        private static string ToAssetPath(string fullPath)
        {
            string normalizedPath = Path.GetFullPath(fullPath).Replace('\\', '/');
            string assetsRoot = Application.dataPath.Replace('\\', '/');
            if (!normalizedPath.StartsWith(assetsRoot + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path is outside Assets: " + fullPath);

            return "Assets" + normalizedPath.Substring(assetsRoot.Length);
        }
    }
}
