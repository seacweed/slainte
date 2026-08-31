using System;
using System.Collections.Generic;
using System.IO;
using Slainte.Bartending;
using UnityEditor;
using UnityEngine;

namespace Slainte.Editor
{
    [InitializeOnLoad]
    public static class ToolCabinetAssetInstaller
    {
        private const string ArtRoot = BartendingAssetPaths.ToolCabinetArtRoot;
        private const string ToolRoot = BartendingAssetPaths.ToolArtRoot;
        private const string IceRoot = BartendingAssetPaths.IceArtRoot;
        private const string GlassRoot = BartendingAssetPaths.GlassArtRoot;
        private const string DefinitionRoot = "Assets/Resources/Bartending/ToolCabinet";
        private const string CatalogPath = DefinitionRoot + "/ToolCabinetCatalog.asset";

        static ToolCabinetAssetInstaller()
        {
            EditorApplication.delayCall += EnsureAssetsWhenMissing;
        }

        [MenuItem("Tools/Slainte/Tool Cabinet/Install Or Repair Assets")]
        public static void InstallOrRepairFromMenu()
        {
            InstallOrRepair(overwriteDefinitions: true);
            Debug.Log("[ToolCabinet] Art, definitions, and catalog are installed.");
        }

        [MenuItem("Tools/Slainte/Tool Cabinet/Validate Assets")]
        public static void ValidateFromMenu()
        {
            ValidateOrThrow();
            Debug.Log("[ToolCabinet] Asset contract is valid.");
        }

        private static void EnsureAssetsWhenMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || AssetDatabase.LoadAssetAtPath<ToolCabinetCatalog>(CatalogPath) != null
                || !File.Exists(Path.GetFullPath(ArtRoot + "tool_cabinet.png")))
            {
                return;
            }

            InstallOrRepair(overwriteDefinitions: false);
        }

        private static void InstallOrRepair(bool overwriteDefinitions)
        {
            EnsureFolder("Assets/Resources", "Bartending");
            EnsureFolder("Assets/Resources/Bartending", "ToolCabinet");

            ReimportArt();

            ToolDef jigger = EnsureTool(
                "Jigger",
                "tool_jigger",
                "Jigger",
                ToolKind.Jigger,
                new[] { "jigger_back", "jigger_front" },
                Array.Empty<string>(),
                Array.Empty<string>(),
                BartendingAssetPaths.BeakerPrefab,
                30f,
                0f,
                0,
                0.7f,
                false,
                overwriteDefinitions);

            ToolDef shaker = EnsureTool(
                "CobblerShaker",
                "tool_cobbler_shaker",
                "Cobbler Shaker",
                ToolKind.CobblerShaker,
                new[]
                {
                    "cobbler_cup_back_white",
                    "cobbler_cup_back_line",
                    "cobbler_cup_front_white",
                    "cobbler_cup_front_line",
                    "cobbler_strainer",
                    "cobbler_lid"
                },
                Array.Empty<string>(),
                Array.Empty<string>(),
                BartendingAssetPaths.CobblerShakerPrefab,
                0f,
                0f,
                0,
                0.7f,
                false,
                overwriteDefinitions);

            ToolDef spoon = EnsureTool(
                "BarSpoon",
                "tool_bar_spoon",
                "Bar Spoon",
                ToolKind.BarSpoon,
                new[] { "barspoon" },
                Array.Empty<string>(),
                Array.Empty<string>(),
                string.Empty,
                0f,
                0f,
                0,
                0.7f,
                false,
                overwriteDefinitions);

            ToolDef bucket = EnsureTool(
                "IceBucket",
                "tool_ice_bucket",
                "Ice Bucket",
                ToolKind.IceBucket,
                new[] { "bucket_back", "bucket_front_6" },
                BuildIndexedNames("bucket_front_", 7),
                new[] { "ice_01", "ice_02", "ice_03" },
                string.Empty,
                0f,
                0f,
                20,
                0.7f,
                false,
                overwriteDefinitions);

            GlassDef rock = EnsureGlass(
                "Rock",
                "glass_rock",
                "Rock",
                new[]
                {
                    "rock_back_white",
                    "rock_back_line",
                    "rock_front_white",
                    "rock_front_line"
                },
                "rock_front_line",
                200f,
                0.7f,
                overwriteDefinitions);
            GlassDef martini = EnsureGlass(
                "Martini",
                "glass_martini",
                "Martini",
                new[]
                {
                    "cocktail_back_white",
                    "cocktail_back_line",
                    "cocktail_front_white",
                    "cocktail_front_line"
                },
                "cocktail_front_line",
                200f,
                0.7f,
                overwriteDefinitions);
            GlassDef highball = EnsureGlass(
                "Highball",
                "glass_highball",
                "Highball",
                new[]
                {
                    "highball_back_white",
                    "highball_back_line",
                    "highball_front_color",
                    "highball_front_white"
                },
                "highball_front_white",
                400f,
                0.7f,
                overwriteDefinitions);
            GlassDef hurricane = EnsureGlass(
                "Hurricane",
                "glass_hurricane",
                "Hurricane",
                new[]
                {
                    "hurricane_back_white",
                    "hurricane_back_line",
                    "hurricane_front_white",
                    "hurricane_front_line"
                },
                "hurricane_front_line",
                400f,
                0.7f,
                overwriteDefinitions);

            ToolCabinetCatalog catalog =
                AssetDatabase.LoadAssetAtPath<ToolCabinetCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ToolCabinetCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            if (overwriteDefinitions || catalog.backgroundSprite == null)
            {
                catalog.backgroundSprite = LoadSprite(ArtRoot + "tool_cabinet.png");
                catalog.toolStoragePixels = new Rect(54f, 321f, 1334f, 447f);
                catalog.glassStoragePixels = new Rect(1456f, 321f, 1043f, 447f);
                catalog.iceMakerPixels = new Rect(1308f, 60f, 1190f, 187f);
                catalog.tools = new[] { jigger, shaker, spoon, bucket };
                catalog.glasses = new[] { rock, martini, highball, hurricane };
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateOrThrow();
        }

        private static ToolDef EnsureTool(
            string assetName,
            string id,
            string displayName,
            ToolKind kind,
            string[] layerNames,
            string[] stateNames,
            string[] iceNames,
            string prefabPath,
            float primaryCapacity,
            float secondaryCapacity,
            int maxCount,
            float worldScale,
            bool dedicatedAnchor,
            bool overwrite)
        {
            string path = DefinitionRoot + "/" + assetName + ".asset";
            ToolDef definition = AssetDatabase.LoadAssetAtPath<ToolDef>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ToolDef>();
                AssetDatabase.CreateAsset(definition, path);
                overwrite = true;
            }

            if (!overwrite)
                return definition;

            definition.id = id;
            definition.displayName = displayName;
            definition.kind = kind;
            definition.cabinetLayers = LoadSprites(ToolRoot, layerNames);
            definition.worldLayers = LoadSprites(ToolRoot, layerNames);
            definition.stateSprites = LoadSprites(ToolRoot, stateNames);
            definition.iceSprites = LoadSprites(IceRoot, iceNames);
            definition.worldPrefab = string.IsNullOrWhiteSpace(prefabPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            definition.primaryCapacityMl = primaryCapacity;
            definition.secondaryCapacityMl = secondaryCapacity;
            definition.maxCount = maxCount;
            if (kind == ToolKind.IceBucket)
            {
                definition.iceRefillPerSecond = 2f;
                definition.chargingShakeAmplitude = 0.055f;
                definition.chargingShakeFrequency = 18f;
            }
            definition.worldScale = worldScale;
            definition.worldVisualOffset = kind switch
            {
                ToolKind.Jigger => new Vector2(0f, 1.875f),
                ToolKind.CobblerShaker => new Vector2(0f, 0.7f),
                ToolKind.IceBucket => new Vector2(0f, 0.395f),
                _ => Vector2.zero
            };
            definition.useDedicatedAnchor = dedicatedAnchor;
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static GlassDef EnsureGlass(
            string assetName,
            string id,
            string displayName,
            string[] layerNames,
            string collisionReferenceName,
            float capacityMl,
            float worldScale,
            bool overwrite)
        {
            string path = DefinitionRoot + "/" + assetName + ".asset";
            GlassDef definition = AssetDatabase.LoadAssetAtPath<GlassDef>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<GlassDef>();
                AssetDatabase.CreateAsset(definition, path);
                overwrite = true;
            }

            if (!overwrite)
                return definition;

            Sprite[] layers = LoadSprites(GlassRoot, layerNames);
            Sprite collisionReference = LoadSprite(
                GlassRoot + collisionReferenceName + ".png");
            definition.id = id;
            definition.displayName = displayName;
            definition.cabinetSprite = collisionReference;
            definition.worldSprite = collisionReference;
            definition.cabinetLayers = layers;
            definition.worldLayers = (Sprite[])layers.Clone();
            definition.collisionReferenceSprite = collisionReference;
            definition.worldPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(BartendingAssetPaths.GlassPrefab);
            definition.glassId = id.Replace("glass_", string.Empty);
            definition.capacityMl = capacityMl;
            definition.worldScale = worldScale;
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void ReimportArt()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string[] paths = AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { ArtRoot, GlassRoot });
            for (int i = 0; i < paths.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(paths[i]);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        private static Sprite[] LoadSprites(string root, string[] names)
        {
            Sprite[] sprites = new Sprite[names.Length];
            for (int i = 0; i < names.Length; i++)
                sprites[i] = LoadSprite(root + names[i] + ".png");
            return sprites;
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                throw new InvalidOperationException("Missing bartending sprite: " + path);
            return sprite;
        }

        private static string[] BuildIndexedNames(string prefix, int count)
        {
            string[] names = new string[count];
            for (int i = 0; i < count; i++)
                names[i] = prefix + i;
            return names;
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, name);
        }

        private static void ValidateOrThrow()
        {
            List<string> failures = new();
            ValidateTexture(ArtRoot + "tool_cabinet.png", 2560, 820, 4096, failures);

            string[] toolFiles = Directory.GetFiles(Path.GetFullPath(ToolRoot), "*.png");
            if (toolFiles.Length != 17)
                failures.Add("Expected 17 tool textures but found " + toolFiles.Length + ".");
            for (int i = 0; i < toolFiles.Length; i++)
            {
                string path = ToolRoot + Path.GetFileName(toolFiles[i]);
                ValidateTexture(path, 310, 590, 2048, failures);
            }

            string[] iceFiles = Directory.GetFiles(Path.GetFullPath(IceRoot), "*.png");
            if (iceFiles.Length != 3)
                failures.Add("Expected 3 ice textures but found " + iceFiles.Length + ".");
            for (int i = 0; i < iceFiles.Length; i++)
            {
                string path = IceRoot + Path.GetFileName(iceFiles[i]);
                ValidateTexture(path, 70, 70, 2048, failures);
            }

            string[] glassFiles = Directory.GetFiles(Path.GetFullPath(GlassRoot), "*.png");
            if (glassFiles.Length != 16)
                failures.Add("Expected 16 layered glass textures but found " + glassFiles.Length + ".");
            for (int i = 0; i < glassFiles.Length; i++)
            {
                string path = GlassRoot + Path.GetFileName(glassFiles[i]);
                ValidateTexture(path, 310, 590, 2048, failures);
            }

            ToolCabinetCatalog catalog =
                AssetDatabase.LoadAssetAtPath<ToolCabinetCatalog>(CatalogPath);
            if (catalog == null || catalog.tools.Length != 4 || catalog.glasses.Length != 4)
                failures.Add("ToolCabinetCatalog must contain four tools and four glasses.");
            else
            {
                ToolKind[] expectedOrder =
                {
                    ToolKind.Jigger,
                    ToolKind.CobblerShaker,
                    ToolKind.BarSpoon,
                    ToolKind.IceBucket
                };
                for (int i = 0; i < expectedOrder.Length; i++)
                {
                    if (catalog.tools[i] == null || catalog.tools[i].kind != expectedOrder[i])
                        failures.Add("ToolCabinetCatalog tool order is invalid at index " + i + ".");
                }

                ToolDef bucket = catalog.tools[3];
                if (bucket != null && bucket.useDedicatedAnchor)
                    failures.Add("Ice Bucket must use a normal cabinet slot, not a dedicated anchor.");

                ToolDef jigger = catalog.tools[0];
                if (jigger != null
                    && (!Mathf.Approximately(
                            jigger.primaryCapacityMl,
                            JiggerCollisionProfiles.FixedCapacityMl)
                        || jigger.secondaryCapacityMl > 0f))
                {
                    failures.Add("Jigger must use one fixed 30 ml capacity.");
                }

                for (int i = 0; i < catalog.glasses.Length; i++)
                {
                    GlassDef glass = catalog.glasses[i];
                    if (glass == null
                        || glass.cabinetLayers == null
                        || glass.cabinetLayers.Length != 4
                        || glass.worldLayers == null
                        || glass.worldLayers.Length != 4
                        || glass.collisionReferenceSprite == null)
                    {
                        failures.Add("ToolCabinetCatalog glass layers are invalid at index " + i + ".");
                    }
                }

                ValidateCabinetRect(catalog, catalog.toolStoragePixels, "tool storage", failures);
                ValidateCabinetRect(catalog, catalog.glassStoragePixels, "glass storage", failures);
                ValidateCabinetRect(catalog, catalog.iceMakerPixels, "ice maker", failures);
            }

            if (failures.Count > 0)
                throw new InvalidOperationException(
                    "Tool-cabinet asset validation failed:\n- " + string.Join("\n- ", failures));
        }

        private static void ValidateTexture(
            string path,
            int expectedWidth,
            int expectedHeight,
            int expectedMaxSize,
            List<string> failures)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (texture == null || sprite == null || importer == null)
            {
                failures.Add("Missing imported texture: " + path);
                return;
            }

            if (texture.width != expectedWidth || texture.height != expectedHeight)
                failures.Add(path + " has an unexpected imported size.");
            if (Mathf.RoundToInt(sprite.rect.width) != expectedWidth
                || Mathf.RoundToInt(sprite.rect.height) != expectedHeight)
            {
                failures.Add(path + " is cropped or sliced.");
            }
            if (importer.maxTextureSize < expectedMaxSize)
                failures.Add(path + " has an insufficient max texture size.");

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (settings.spriteMeshType != SpriteMeshType.FullRect)
                failures.Add(path + " must use a Full Rect sprite mesh.");
        }

        private static void ValidateCabinetRect(
            ToolCabinetCatalog catalog,
            Rect pixelRect,
            string label,
            List<string> failures)
        {
            if (!catalog.TryGetNormalizedRect(pixelRect, out Rect normalized)
                || normalized.xMin < 0f
                || normalized.yMin < 0f
                || normalized.xMax > 1f
                || normalized.yMax > 1f)
            {
                failures.Add("The " + label + " rectangle is outside tool_cabinet.png.");
            }
        }
    }
}
