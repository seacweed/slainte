using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Slainte.Editor
{
    /// <summary>
    /// Keeps the supplied bartending art byte dimensions intact at import time.
    /// Runtime layout may scale a SpriteRenderer/Image, but the source texture is
    /// never cropped, re-encoded, or downsampled by this importer.
    /// </summary>
    public sealed class BartendingArtImportPostprocessor : AssetPostprocessor
    {
        public const string BottleRoot = "Assets/Art/Bartending/Bottles/";
        public const string GlassRoot = "Assets/Art/Bartending/GlassCollisionTests/";
        public const string ToolCabinetRoot = "Assets/Art/Bartending/ToolCabinet/";

        private void OnPreprocessTexture()
        {
            if (!IsManagedTexture(assetPath))
                return;

            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            int maxTextureSize = IsToolCabinetBackground(assetPath) ? 4096 : 2048;
            importer.maxTextureSize = maxTextureSize;

            TextureImporterSettings spriteSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(spriteSettings);
            spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
            spriteSettings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(spriteSettings);

            ApplyUncompressedPlatform(importer, "Standalone", maxTextureSize);
            ApplyUncompressedPlatform(importer, "WebGL", maxTextureSize);
        }

        private static void ApplyUncompressedPlatform(
            TextureImporter importer,
            string platform,
            int maxTextureSize)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
            settings.name = platform;
            settings.overridden = true;
            settings.maxTextureSize = maxTextureSize;
            settings.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            settings.format = TextureImporterFormat.Automatic;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            settings.crunchedCompression = false;
            importer.SetPlatformTextureSettings(settings);
        }

        public static bool IsManagedTexture(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalized = path.Replace('\\', '/');
            return normalized.StartsWith(BottleRoot, StringComparison.Ordinal)
                || normalized.StartsWith(GlassRoot, StringComparison.Ordinal)
                || normalized.StartsWith(ToolCabinetRoot, StringComparison.Ordinal);
        }

        private static bool IsToolCabinetBackground(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalized = path.Replace('\\', '/');
            return string.Equals(
                normalized,
                ToolCabinetRoot + "tool_cabinet.png",
                StringComparison.Ordinal);
        }
    }

    public static class BartendingArtContractValidator
    {
        private const int ExpectedWidth = 310;
        private const int ExpectedHeight = 590;

        private static readonly string[] BottleTriplets =
        {
            "beatha",
            "bless",
            "breezevodka",
            "burnhambourbon",
            "coffeepowder",
            "cotton",
            "hectar",
            "johnnydogs",
            "minutefizz",
            "nanangna",
            "siltrop",
            "slop",
            "syntheticlemon",
            "tropicaljuice"
        };

        private static readonly string[] GlassFiles =
        {
            "200coc.png",
            "200rock.png",
            "400high.png",
            "400hurricane.png"
        };

        [MenuItem("Tools/Slainte/Validate Bartending Art Contract")]
        public static void ValidateFromMenu()
        {
            ValidateAllOrThrow();
            Debug.Log("[BartendingArt] 43 bottle and 4 temporary-glass textures preserve the 310x590 source contract.");
        }

        public static void ValidateAllOrThrow()
        {
            List<string> failures = new List<string>();
            int validated = 0;

            foreach (string name in BottleTriplets)
            {
                validated += ValidateTexture(
                    BartendingArtImportPostprocessor.BottleRoot + name + ".png",
                    failures);
                validated += ValidateTexture(
                    BartendingArtImportPostprocessor.BottleRoot + name + "_blank.png",
                    failures);
                validated += ValidateTexture(
                    BartendingArtImportPostprocessor.BottleRoot + name + "_lid.png",
                    failures);
            }

            validated += ValidateTexture(
                BartendingArtImportPostprocessor.BottleRoot + "hotwater.png",
                failures);

            foreach (string file in GlassFiles)
            {
                validated += ValidateTexture(
                    BartendingArtImportPostprocessor.GlassRoot + file,
                    failures);
            }

            if (validated != 47)
                failures.Add($"Expected 47 textures but validated {validated}.");

            if (failures.Count > 0)
                throw new InvalidOperationException(
                    "Bartending art contract failed:\n- " + string.Join("\n- ", failures));
        }

        private static int ValidateTexture(string path, List<string> failures)
        {
            if (!File.Exists(Path.GetFullPath(path)))
            {
                failures.Add(path + " is missing.");
                return 0;
            }

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (importer == null || texture == null || sprite == null)
            {
                failures.Add(path + " is not imported as a single Sprite texture.");
                return 0;
            }

            if (texture.width != ExpectedWidth || texture.height != ExpectedHeight)
                failures.Add($"{path} imported as {texture.width}x{texture.height}, expected 310x590.");

            if (Mathf.RoundToInt(sprite.rect.width) != ExpectedWidth
                || Mathf.RoundToInt(sprite.rect.height) != ExpectedHeight)
            {
                failures.Add(path + " is cropped or sliced instead of using the full source rectangle.");
            }

            TextureImporterSettings spriteSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(spriteSettings);

            if (importer.spriteImportMode != SpriteImportMode.Single
                || spriteSettings.spriteMeshType != SpriteMeshType.FullRect
                || importer.mipmapEnabled
                || importer.npotScale != TextureImporterNPOTScale.None
                || importer.textureCompression != TextureImporterCompression.Uncompressed
                || spriteSettings.spriteGenerateFallbackPhysicsShape)
            {
                failures.Add(path + " has import settings that can alter size, canvas, or collision behavior.");
            }

            return 1;
        }
    }
}
