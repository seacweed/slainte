using System;
using System.Collections.Generic;
using System.Linq;
using Slainte.TV;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Slainte.EditorTools
{
    public static class RestSceneFinalArtInstaller
    {
        public const string ScenePath =
            "Assets/_Project/Scenes/Production/RestScene.unity";
        public const string DatabasePath = "Assets/Resources/TV/TVBroadcastDatabase.asset";
        public const string TVPrefabPath = "Assets/RestScene/Prefabs/TVSystem.prefab";
        public const string PanelPrefabPath = "Assets/RestScene/Prefabs/TVPanel.prefab";

        public const string BackgroundColorPath = "Assets/RestScene/Sprites/Final/background_color.png";
        public const string BackgroundGrayPath = "Assets/RestScene/Sprites/Final/background_gray.png";
        public const string CompositeReferencePath = "Assets/RestScene/Sprites/Final/composite_reference.png";
        public const string TVWorldNormalPath = "Assets/RestScene/Sprites/Final/tv_world_normal.png";
        public const string TVWorldOutlinePath = "Assets/RestScene/Sprites/Final/tv_world_outline.png";
        public const string TVWorldGrayPath = "Assets/RestScene/Sprites/Final/tv_world_gray.png";
        public const string BoardNormalPath = "Assets/RestScene/Sprites/Final/board_normal.png";
        public const string BoardOutlinePath = "Assets/RestScene/Sprites/Final/board_outline.png";
        public const string BoardGrayPath = "Assets/RestScene/Sprites/Final/board_gray.png";
        public const string ShopNormalPath = "Assets/RestScene/Sprites/Final/shop_normal.png";
        public const string ShopOutlinePath = "Assets/RestScene/Sprites/Final/shop_outline.png";
        public const string ShopGrayPath = "Assets/RestScene/Sprites/Final/shop_gray.png";

        public const string TVBackgroundPath = "Assets/RestScene/Sprites/TVFinal/tv_background.png";
        public const string TVFramePath = "Assets/RestScene/Sprites/TVFinal/tv_frame.png";
        public const string TVHeadlinePath = "Assets/RestScene/Sprites/TVFinal/tv_headline.png";
        public const string TVCardBackgroundPath = "Assets/RestScene/Sprites/TVFinal/tv_card_background.png";

        public static readonly Rect TVCrop = new(1154f, 558f, 212f, 276f);
        public static readonly Rect BoardCrop = new(610f, 660f, 545f, 556f);
        public static readonly Rect ShopCrop = new(524f, 457f, 117f, 190f);

        private static readonly Vector3 FullCanvasWorldCenter = new(-5.6f, 0f, 0f);

        private static readonly string[] LegacyVisualNames =
        {
            "rest_idle_0",
            "rest_shop_idle_0",
            "rest_shop_hover_0",
            "rest_board_idle_0",
            "rest_board_hover_0"
        };

        private readonly struct CropAsset
        {
            public readonly string Path;
            public readonly Rect Rect;

            public CropAsset(string path, Rect rect)
            {
                Path = path;
                Rect = rect;
            }
        }

        [MenuItem("Slainte/Rest Scene/Install Final Artwork")]
        public static void InstallFromMenu()
        {
            InstallAll();
            EditorUtility.DisplayDialog(
                "Rest Scene",
                "휴식 배경, 전광판, 일반 상점, TV 최종 이미지를 적용했습니다.",
                "확인");
        }

        public static void InstallFromCommandLine()
        {
            InstallAll();
        }

        public static void InstallAll()
        {
            AssetDatabase.Refresh();
            ConfigureArtworkImports();

            TVBroadcastDatabase database = AssetDatabase.LoadAssetAtPath<TVBroadcastDatabase>(
                DatabasePath);
            if (database == null)
                throw new InvalidOperationException($"TV 방송 데이터베이스가 없습니다: {DatabasePath}");

            AssignBroadcastCards(database);
            GameObject panelPrefab = LoadPanelPrefab(database);
            GameObject tvPrefab = BuildTVPrefab(database, panelPrefab);
            InstallSceneArtwork(database, tvPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[RestSceneFinalArtInstaller] PASS: 1440px left-aligned room, "
                + "cropped state sprites, replacement colliders, TV panel/cards installed.");
        }

        private static void ConfigureArtworkImports()
        {
            string[] fullSprites =
            {
                BackgroundColorPath,
                BackgroundGrayPath,
                CompositeReferencePath,
                TVBackgroundPath,
                TVFramePath,
                TVHeadlinePath,
                TVCardBackgroundPath,
                CardPath(1), CardPath(2), CardPath(3),
                CardPath(4), CardPath(5), CardPath(6)
            };

            foreach (string path in fullSprites)
                ConfigureSprite(path, crop: null);

            CropAsset[] croppedSprites =
            {
                new(TVWorldNormalPath, TVCrop),
                new(TVWorldOutlinePath, TVCrop),
                new(TVWorldGrayPath, TVCrop),
                new(BoardNormalPath, BoardCrop),
                new(BoardOutlinePath, BoardCrop),
                new(BoardGrayPath, BoardCrop),
                new(ShopNormalPath, ShopCrop),
                new(ShopOutlinePath, ShopCrop),
                new(ShopGrayPath, ShopCrop),
                new("Assets/RestScene/Sprites/Final/Deferred/shop_strange_normal.png", ShopCrop),
                new("Assets/RestScene/Sprites/Final/Deferred/shop_strange_outline.png", ShopCrop),
                new("Assets/RestScene/Sprites/Final/Deferred/shop_strange_gray.png", ShopCrop)
            };

            foreach (CropAsset asset in croppedSprites)
                ConfigureSprite(asset.Path, asset.Rect);
        }

        private static void ConfigureSprite(string path, Rect? crop)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException($"이미지를 찾지 못했습니다: {path}");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = crop.HasValue
                ? SpriteImportMode.Multiple
                : SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;

            var spriteSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(spriteSettings);
            spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
            spriteSettings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(spriteSettings);
            importer.SaveAndReimport();

            if (!crop.HasValue)
                return;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            ISpriteEditorDataProvider provider =
                factory.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
                throw new InvalidOperationException($"Sprite Rect 편집기를 열 수 없습니다: {path}");

            provider.InitSpriteEditorDataProvider();
            string spriteName = System.IO.Path.GetFileNameWithoutExtension(path);
            SpriteRect existing = provider.GetSpriteRects()
                .FirstOrDefault(rect => string.Equals(
                    rect.name,
                    spriteName,
                    StringComparison.Ordinal));
            GUID spriteId = existing != null ? existing.spriteID : GUID.Generate();
            var spriteRect = new SpriteRect
            {
                name = spriteName,
                rect = crop.Value,
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                border = Vector4.zero,
                spriteID = spriteId
            };

            provider.SetSpriteRects(new[] { spriteRect });
            ISpriteNameFileIdDataProvider nameProvider =
                provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            nameProvider?.SetNameFileIdPairs(new[]
            {
                new SpriteNameFileIdPair(spriteName, spriteId)
            });
            provider.Apply();
            importer.SaveAndReimport();
        }

        private static void AssignBroadcastCards(TVBroadcastDatabase database)
        {
            var mapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["none"] = 1,
                ["delivery_outage"] = 2,
                ["shop_maintenance"] = 3,
                ["high_abv_orders"] = 4,
                ["tip_bonus"] = 5,
                ["district_9_patrol"] = 6
            };

            foreach (TVBroadcastEntry entry in database.broadcasts)
            {
                if (entry != null && mapping.TryGetValue(entry.id, out int cardNumber))
                    entry.eventSprite = LoadSprite(CardPath(cardNumber));
            }

            EditorUtility.SetDirty(database);
        }

        private static GameObject LoadPanelPrefab(TVBroadcastDatabase database)
        {
            GameObject panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            if (panelPrefab == null)
                throw new InvalidOperationException($"TVPanel 프리팹이 없습니다: {PanelPrefabPath}");

            TVUIManager manager = panelPrefab.GetComponent<TVUIManager>();
            if (manager == null)
                throw new InvalidOperationException("TVPanel 프리팹에 TVUIManager가 없습니다.");
            if (manager.database != database)
                throw new InvalidOperationException("TVPanel 프리팹에 올바른 방송 데이터베이스가 연결되지 않았습니다.");

            return panelPrefab;
        }

        private static GameObject BuildTVPrefab(
            TVBroadcastDatabase database,
            GameObject panelPrefab)
        {
            GameObject root = new("TVSystemRoot");
            TVRestBootstrap bootstrap = root.AddComponent<TVRestBootstrap>();
            bootstrap.database = database;

            SpriteRenderer normal = CreateWorldSprite(
                "FinalNormal",
                root.transform,
                LoadSprite(TVWorldNormalPath),
                sortingOrder: 1);
            SpriteRenderer outline = CreateWorldSprite(
                "FinalOutline",
                root.transform,
                LoadSprite(TVWorldOutlinePath),
                sortingOrder: 1);
            SpriteRenderer gray = CreateWorldSprite(
                "FinalGray",
                root.transform,
                LoadSprite(TVWorldGrayPath),
                sortingOrder: 1);
            outline.gameObject.SetActive(false);
            gray.gameObject.SetActive(false);

            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            body.gravityScale = 0f;
            BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
            collider.size = PixelSizeToWorld(TVCrop.size);
            collider.isTrigger = true;

            ObjectInteraction interaction = root.AddComponent<ObjectInteraction>();
            interaction.idleOverlay = normal.gameObject;
            interaction.hoverOverlay = outline.gameObject;
            interaction.grayOverlay = gray.gameObject;

            TVSystemController controller = root.AddComponent<TVSystemController>();
            controller.interaction = interaction;
            controller.panelPrefab = panelPrefab.GetComponent<TVUIManager>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, TVPrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        private static void InstallSceneArtwork(
            TVBroadcastDatabase database,
            GameObject tvPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Canvas canvas = FindScreenCanvas(scene);
            if (canvas == null)
                throw new InvalidOperationException("RestScene 화면 공간 Canvas를 찾지 못했습니다.");

            foreach (string name in LegacyVisualNames)
            {
                GameObject legacy = FindInScene(scene, name);
                if (legacy != null) legacy.SetActive(false);
            }

            GameObject artRoot = FindInScene(scene, "RestFinalVisualRoot");
            if (artRoot == null)
                artRoot = new GameObject("RestFinalVisualRoot");
            artRoot.transform.SetParent(null, false);
            artRoot.transform.position = FullCanvasWorldCenter;

            GameObject colorBackground = CreateOrReplaceWorldVisual(
                artRoot.transform,
                "BackgroundColor",
                LoadSprite(BackgroundColorPath),
                sortingOrder: 0);
            GameObject grayBackground = CreateOrReplaceWorldVisual(
                artRoot.transform,
                "BackgroundGray",
                LoadSprite(BackgroundGrayPath),
                sortingOrder: 0);
            grayBackground.SetActive(false);

            RestSceneVisualStateCoordinator coordinator =
                artRoot.GetComponent<RestSceneVisualStateCoordinator>();
            if (coordinator == null)
                coordinator = artRoot.AddComponent<RestSceneVisualStateCoordinator>();
            coordinator.colorBackground = colorBackground;
            coordinator.grayBackground = grayBackground;

            GameObject shop = FindInScene(scene, "ShopButton");
            ConfigureInteractionObject(
                shop,
                "일반 상점",
                ShopCrop,
                LoadSprite(ShopNormalPath),
                LoadSprite(ShopOutlinePath),
                LoadSprite(ShopGrayPath),
                restrictAsRestShop: true,
                database);

            GameObject board = FindInScene(scene, "EpisodeBoardButton");
            ConfigureInteractionObject(
                board,
                "전광판",
                BoardCrop,
                LoadSprite(BoardNormalPath),
                LoadSprite(BoardOutlinePath),
                LoadSprite(BoardGrayPath),
                restrictAsRestShop: false,
                database);

            GameObject existingTV = FindInScene(scene, "TVSystemRoot");
            if (existingTV != null)
                Object.DestroyImmediate(existingTV);

            GameObject tv = (GameObject)PrefabUtility.InstantiatePrefab(tvPrefab, scene);
            tv.name = "TVSystemRoot";
            tv.transform.SetParent(null, false);
            tv.transform.position = WorldCenterForCrop(TVCrop);
            TVSystemController controller = tv.GetComponent<TVSystemController>();
            controller.uiCanvas = canvas;
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(tv.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureInteractionObject(
            GameObject target,
            string displayName,
            Rect crop,
            Sprite normalSprite,
            Sprite outlineSprite,
            Sprite graySprite,
            bool restrictAsRestShop,
            TVBroadcastDatabase database)
        {
            if (target == null)
                throw new InvalidOperationException($"RestScene에서 {displayName} 오브젝트를 찾지 못했습니다.");

            target.transform.position = WorldCenterForCrop(crop);
            target.transform.rotation = Quaternion.identity;
            target.transform.localScale = Vector3.one;

            SpriteRenderer legacyRenderer = target.GetComponent<SpriteRenderer>();
            if (legacyRenderer != null) legacyRenderer.enabled = false;

            GameObject normal = CreateOrReplaceWorldVisual(
                target.transform,
                "FinalNormal",
                normalSprite,
                sortingOrder: 1);
            GameObject outline = CreateOrReplaceWorldVisual(
                target.transform,
                "FinalOutline",
                outlineSprite,
                sortingOrder: 1);
            GameObject gray = CreateOrReplaceWorldVisual(
                target.transform,
                "FinalGray",
                graySprite,
                sortingOrder: 1);
            outline.SetActive(false);
            gray.SetActive(false);

            foreach (Collider2D oldCollider in target.GetComponents<Collider2D>())
                Object.DestroyImmediate(oldCollider);
            BoxCollider2D collider = target.AddComponent<BoxCollider2D>();
            collider.size = PixelSizeToWorld(crop.size);
            collider.isTrigger = true;

            Rigidbody2D body = target.GetComponent<Rigidbody2D>();
            if (body == null) body = target.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            body.gravityScale = 0f;

            ObjectInteraction interaction = target.GetComponent<ObjectInteraction>();
            if (interaction == null) interaction = target.AddComponent<ObjectInteraction>();
            interaction.highlightOverlay = null;
            interaction.idleOverlay = normal;
            interaction.hoverOverlay = outline;
            interaction.grayOverlay = gray;
            interaction.disableWhenRestShopRestricted = restrictAsRestShop;
            interaction.tvDatabase = restrictAsRestShop ? database : null;
            EditorUtility.SetDirty(interaction);
        }

        private static SpriteRenderer CreateWorldSprite(
            string name,
            Transform parent,
            Sprite sprite,
            int sortingOrder)
        {
            GameObject gameObject = new(name, typeof(SpriteRenderer));
            gameObject.transform.SetParent(parent, false);
            SpriteRenderer renderer = gameObject.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static GameObject CreateOrReplaceWorldVisual(
            Transform parent,
            string name,
            Sprite sprite,
            int sortingOrder)
        {
            Transform old = parent.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            return CreateWorldSprite(name, parent, sprite, sortingOrder).gameObject;
        }

        private static Canvas FindScreenCanvas(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas.renderMode != RenderMode.WorldSpace && canvas.isRootCanvas)
                        return canvas;
                }
            }

            return null;
        }

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (string.Equals(transform.name, name, StringComparison.Ordinal))
                        return transform.gameObject;
                }
            }

            return null;
        }

        private static Vector3 WorldCenterForCrop(Rect crop)
        {
            return FullCanvasWorldCenter + new Vector3(
                (crop.center.x - 720f) / 100f,
                (crop.center.y - 720f) / 100f,
                0f);
        }

        private static Vector2 PixelSizeToWorld(Vector2 pixelSize)
        {
            return pixelSize / 100f;
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                string expectedName = System.IO.Path.GetFileNameWithoutExtension(path);
                IEnumerable<Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<Sprite>();
                sprite = sprites.FirstOrDefault(candidate => string.Equals(
                             candidate.name,
                             expectedName,
                             StringComparison.Ordinal))
                         ?? sprites.FirstOrDefault();
            }

            if (sprite == null)
                throw new InvalidOperationException($"Sprite를 불러오지 못했습니다: {path}");
            return sprite;
        }

        private static string CardPath(int cardNumber)
        {
            return $"Assets/RestScene/Sprites/TVFinal/tv_card_{cardNumber}.png";
        }

        private static GameObject CreateImage(
            string name,
            Transform parent,
            Sprite sprite,
            Color color)
        {
            GameObject gameObject = new(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = sprite != null;
            return gameObject;
        }

        private static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            string value,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color)
        {
            GameObject gameObject = new(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            Stretch(rect, 12f);
            TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static TextMeshProUGUI CreateTickerText(string name, RectTransform parent)
        {
            TextMeshProUGUI text = CreateText(
                name,
                parent,
                "방송 자막 준비 중",
                24f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(760f, 48f);
            rect.anchoredPosition = Vector2.zero;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            string label,
            Color color)
        {
            GameObject gameObject = CreateImage(name, parent, null, color);
            Button button = gameObject.AddComponent<Button>();
            CreateText(
                "Label",
                gameObject.transform,
                label,
                36f,
                TextAlignmentOptions.Center,
                new Color(0.08f, 0.06f, 0.01f, 1f));
            return button;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void SetFixedRect(
            RectTransform rect,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }
}
