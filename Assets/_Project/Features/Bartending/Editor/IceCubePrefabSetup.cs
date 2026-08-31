using System;
using System.IO;
using Slainte.Bartending;
using Slainte.Editor;
using UnityEditor;
using UnityEngine;

namespace Slainte.Bartending.EditorTools
{
    public static class IceCubePrefabSetup
    {
        private const string SpritePath = BartendingAssetPaths.DefaultIceSprite;
        private const string PrefabPath = BartendingAssetPaths.IceCubePrefab;
        private const string SettingsPath = "Assets/Resources/Bartending/BusinessBartendingSettings.asset";

        [MenuItem("Slainte/Bartending/Create or Refresh Ice Cube Prefab")]
        public static void CreateOrRefresh()
        {
            BusinessBartendingSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessBartendingSettings>(SettingsPath);
            if (settings == null)
                throw new InvalidOperationException($"Bartending settings not found: {SettingsPath}");

            ConfigureSpriteImporter();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
                throw new InvalidOperationException($"Ice cube sprite could not be loaded: {SpritePath}");

            string prefabDirectory = Path.GetDirectoryName(PrefabPath);
            if (!string.IsNullOrEmpty(prefabDirectory))
                Directory.CreateDirectory(prefabDirectory);

            GameObject root = new GameObject("IceCube");
            try
            {
                SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = Color.white;
                renderer.sortingOrder = 18;

                BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one * 0.88f;

                Rigidbody2D body = root.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Dynamic;
                body.mass = 0.25f;
                body.gravityScale = 1f;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;

                root.AddComponent<IceCubeController>();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null)
                    throw new InvalidOperationException($"Failed to save ice cube prefab: {PrefabPath}");

                settings.iceCubePrefab = prefab;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log(
                    $"[IceCubePrefabSetup] Created {PrefabPath} and assigned it to {SettingsPath}.",
                    prefab);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void RunFromCommandLine()
        {
            try
            {
                CreateOrRefresh();
                Debug.Log("[IceCubePrefabSetup] PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("[IceCubePrefabSetup] FAIL");
                EditorApplication.Exit(1);
            }
        }

        private static void ConfigureSpriteImporter()
        {
            AssetDatabase.ImportAsset(
                SpritePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException($"Texture importer not found: {SpritePath}");

            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = Mathf.Max(width, height);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }
    }
}
