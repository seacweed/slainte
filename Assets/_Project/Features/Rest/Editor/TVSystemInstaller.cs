using System.Collections.Generic;
using System.IO;
using Slainte.TV;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Slainte.EditorTools
{
    public static class TVSystemInstaller
    {
        private const string ScenePath = ProjectScenePaths.Rest;
        private const string DatabasePath = "Assets/Resources/TV/TVBroadcastDatabase.asset";
        private const string PrefabPath = "Assets/RestScene/Prefabs/TVSystem.prefab";
        private const string PanelPrefabPath = "Assets/RestScene/Prefabs/TVPanel.prefab";
        private static readonly Vector3 DefaultWorldPosition = new(9.5f, 0.3f, 0f);

        [MenuItem("Slainte/TV/Install TV System")]
        public static void InstallFromMenu()
        {
            Install();
            EditorUtility.DisplayDialog("TV", "TV 데이터, 프리팹, RestScene 배치를 완료했습니다.", "확인");
        }

        public static void InstallFromCommandLine()
        {
            Install();
        }

        public static void CapturePreviewFromCommandLine()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            GameObject panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            if (canvas == null || panelPrefab == null)
                throw new System.InvalidOperationException("TV 미리보기 대상을 찾지 못했습니다.");

            GameObject panel = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, scene);
            panel.transform.SetParent(canvas.transform, false);
            panel.gameObject.SetActive(true);
            CanvasGroup group = panel.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = 1f;
            CaptureCanvas(canvas, Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../Temp/TVSystemPreview.png")));
            Object.DestroyImmediate(panel);
            Debug.Log("[TVSystemInstaller] Preview captured.");
        }

        private static void Install()
        {
            RestSceneFinalArtInstaller.InstallAll();
        }

        private static TVBroadcastDatabase EnsureDatabase()
        {
            EnsureFolder("Assets/Resources", "TV");
            TVBroadcastDatabase database =
                AssetDatabase.LoadAssetAtPath<TVBroadcastDatabase>(DatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<TVBroadcastDatabase>();
                AssetDatabase.CreateAsset(database, DatabasePath);
            }

            if (database.broadcasts == null || database.broadcasts.Count == 0)
            {
                database.broadcasts = BuildDefaultBroadcasts();
                EditorUtility.SetDirty(database);
            }

            return database;
        }

        private static GameObject EnsurePanelPrefab(TVBroadcastDatabase database)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            if (existing != null)
                return existing;

            GameObject panel = CreateImage(
                "TVPanel",
                null,
                new Color(0f, 0f, 0f, 0.5f));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            Stretch(panelRect);
            panel.AddComponent<CanvasGroup>();
            TVUIManager manager = panel.AddComponent<TVUIManager>();
            manager.database = database;

            GameObject frame = CreateImage(
                "TVFrame",
                panelRect,
                new Color(0.14f, 0.17f, 0.11f, 1f));
            RectTransform frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchorMin = frameRect.anchorMax = new Vector2(1f, 0.5f);
            frameRect.pivot = new Vector2(1f, 0.5f);
            frameRect.sizeDelta = new Vector2(1120f, 900f);
            frameRect.anchoredPosition = new Vector2(-110f, 0f);
            Outline frameOutline = frame.AddComponent<Outline>();
            frameOutline.effectColor = new Color(0.03f, 0.04f, 0.02f, 1f);
            frameOutline.effectDistance = new Vector2(14f, -14f);
            manager.tvFrame = frameRect;

            GameObject screen = CreateImage(
                "NewsScreen",
                frameRect,
                new Color(0.58f, 0.68f, 0.18f, 1f));
            RectTransform screenRect = screen.GetComponent<RectTransform>();
            Stretch(screenRect);
            screenRect.offsetMin = new Vector2(65f, 65f);
            screenRect.offsetMax = new Vector2(-65f, -65f);

            RectTransform newsText = CreateText(
                "NewsLabel",
                screenRect,
                "NEWS",
                36f,
                TextAlignmentOptions.Left,
                new Color(0.03f, 0.08f, 0.02f, 1f));
            SetAnchoredRect(newsText, new Vector2(0.03f, 0.9f), new Vector2(0.35f, 0.98f));
            RectTransform liveText = CreateText(
                "LiveLabel",
                screenRect,
                "LIVE  00:00",
                25f,
                TextAlignmentOptions.Right,
                new Color(0.03f, 0.08f, 0.02f, 1f));
            SetAnchoredRect(liveText, new Vector2(0.67f, 0.9f), new Vector2(0.97f, 0.98f));

            GameObject presenter = CreateImage(
                "PresenterImage",
                screenRect,
                new Color(0.18f, 0.24f, 0.14f, 1f));
            RectTransform presenterRect = presenter.GetComponent<RectTransform>();
            SetAnchoredRect(presenterRect, new Vector2(0.04f, 0.3f), new Vector2(0.42f, 0.88f));
            CreateText(
                "PresenterPlaceholder",
                presenterRect,
                "진행자 이미지\n(에셋 대기)",
                28f,
                TextAlignmentOptions.Center,
                Color.white);

            GameObject eventVisual = CreateImage(
                "EventImage",
                screenRect,
                new Color(0.18f, 0.24f, 0.14f, 1f));
            RectTransform eventRect = eventVisual.GetComponent<RectTransform>();
            SetAnchoredRect(eventRect, new Vector2(0.45f, 0.3f), new Vector2(0.96f, 0.88f));
            manager.eventImage = eventVisual.GetComponent<Image>();
            manager.eventPlaceholderText = CreateText(
                "EventPlaceholder",
                eventRect,
                "사건 이미지\n(에셋 대기)",
                28f,
                TextAlignmentOptions.Center,
                Color.white).GetComponent<TextMeshProUGUI>();

            GameObject titleBar = CreateImage(
                "TitleBar",
                screenRect,
                new Color(0.04f, 0.08f, 0.03f, 0.96f));
            RectTransform titleBarRect = titleBar.GetComponent<RectTransform>();
            SetAnchoredRect(titleBarRect, new Vector2(0.12f, 0.17f), new Vector2(0.88f, 0.3f));
            manager.titleText = CreateText(
                "TitleText",
                titleBarRect,
                "이상 없음",
                36f,
                TextAlignmentOptions.Center,
                Color.white).GetComponent<TextMeshProUGUI>();

            GameObject tickerViewportObject = CreateImage(
                "TickerViewport",
                screenRect,
                new Color(0.015f, 0.02f, 0.012f, 1f));
            RectTransform tickerViewport = tickerViewportObject.GetComponent<RectTransform>();
            SetAnchoredRect(tickerViewport, new Vector2(0.03f, 0.035f), new Vector2(0.97f, 0.145f));
            tickerViewportObject.AddComponent<RectMask2D>();
            TextMeshProUGUI firstTicker = CreateTickerText("TickerA", tickerViewport);
            TextMeshProUGUI secondTicker = CreateTickerText("TickerB", tickerViewport);
            TVTicker ticker = tickerViewportObject.AddComponent<TVTicker>();
            ticker.viewport = tickerViewport;
            ticker.firstText = firstTicker;
            ticker.secondText = secondTicker;
            manager.ticker = ticker;

            Button closeButton = CreateButton(
                "CloseButton",
                frameRect,
                "X",
                new Color(0.95f, 0.65f, 0.05f, 1f));
            RectTransform closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(82f, 82f);
            closeRect.anchoredPosition = new Vector2(0f, 0f);
            manager.closeButton = closeButton;

            panel.SetActive(false);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(panel, PanelPrefabPath);
            Object.DestroyImmediate(panel);
            return saved;
        }

        private static GameObject EnsurePrefab(
            TVBroadcastDatabase database,
            GameObject panelPrefab)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null
                && existing.transform is not RectTransform
                && existing.GetComponent<TVSystemController>() != null)
            {
                return existing;
            }

            Sprite placeholder = AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/UISprite.psd");
            if (placeholder == null)
                throw new System.InvalidOperationException("월드 TV 임시 Sprite를 찾지 못했습니다.");

            GameObject root = new("TVSystemRoot");
            TVRestBootstrap bootstrap = root.AddComponent<TVRestBootstrap>();
            bootstrap.database = database;

            SpriteRenderer body = CreateWorldSprite(
                "WorldVisual",
                root.transform,
                placeholder,
                new Color(0.13f, 0.2f, 0.16f, 0.95f),
                new Vector3(4f, 3.2f, 1f),
                sortingOrder: 2);

            SpriteRenderer highlight = CreateWorldSprite(
                "WorldHighlight",
                root.transform,
                placeholder,
                new Color(1f, 0.75f, 0.05f, 0.28f),
                new Vector3(4.2f, 3.4f, 1f),
                sortingOrder: 3);
            highlight.gameObject.SetActive(false);

            Rigidbody2D rigidbody = root.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = RigidbodyType2D.Static;
            rigidbody.gravityScale = 0f;
            BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(4f, 3.2f);
            collider.isTrigger = true;

            ObjectInteraction interaction = root.AddComponent<ObjectInteraction>();
            interaction.highlightOverlay = highlight.gameObject;

            TVSystemController controller = root.AddComponent<TVSystemController>();
            controller.interaction = interaction;
            controller.panelPrefab = panelPrefab != null
                ? panelPrefab.GetComponent<TVUIManager>()
                : null;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        private static void InstallInScene(GameObject prefab)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
                throw new System.InvalidOperationException("RestScene Canvas를 찾지 못했습니다.");

            GameObject existing = GameObject.Find("TVSystemRoot");
            if (existing != null)
                Object.DestroyImmediate(existing);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "TVSystemRoot";
            instance.transform.SetParent(null, false);
            instance.transform.position = DefaultWorldPosition;

            TVSystemController controller = instance.GetComponent<TVSystemController>();
            if (controller == null)
                throw new System.InvalidOperationException("TVSystem 프리팹에 컨트롤러가 없습니다.");
            controller.uiCanvas = canvas;
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static List<TVBroadcastEntry> BuildDefaultBroadcasts()
        {
            return new List<TVBroadcastEntry>
            {
                Entry("none", "이상 없음", 55f, TVBroadcastEffectType.None,
                    "오늘 밤 특별한 사건은 확인되지 않았습니다. 평온한 영업이 예상됩니다."),
                Entry("delivery_outage", "배송망 마비", 5f, TVBroadcastEffectType.DisableDelivery,
                    "긴급 점검으로 배송 서비스가 일시 중단됩니다.",
                    reason: "TV 예보: 배송망 마비"),
                Entry("shop_maintenance", "상점 정기 점검", 5f, TVBroadcastEffectType.DisableRestShop,
                    "상점 설비 점검으로 다음 휴식 동안 상점 이용이 제한됩니다.",
                    reason: "TV 예보: 상점 정기 점검"),
                Entry("high_abv_orders", "고도수 음료 섭취 권장", 10f,
                    TVBroadcastEffectType.BoostOrderTagWeight,
                    "기온 급강하가 예상됩니다. 고도수 음료 주문이 증가할 전망입니다.",
                    "high_abv", 2f),
                Entry("tip_bonus", "근로자 성과 배급금 지급", 15f,
                    TVBroadcastEffectType.BoostTips,
                    "특별 성과 배급금이 지급되어 손님들의 팁 지출이 증가할 전망입니다.",
                    multiplier: 1.5f),
                Entry("district_9_patrol", "[UPD] 9번가 대규모 순찰", 10f,
                    TVBroadcastEffectType.BoostCustomerTagWeight,
                    "9번가 일대의 대규모 순찰로 특정 손님들의 이동이 증가합니다.",
                    "customer_attribute:야간순찰", 2f,
                    exclusiveCustomerPool: true)
            };
        }

        private static TVBroadcastEntry Entry(
            string id,
            string title,
            float weight,
            TVBroadcastEffectType effect,
            string ticker,
            string targetTag = "",
            float multiplier = 1f,
            string reason = "",
            bool exclusiveCustomerPool = false)
        {
            return new TVBroadcastEntry
            {
                id = id,
                title = title,
                tickerText = ticker,
                weight = weight,
                effectType = effect,
                targetTag = targetTag,
                effectMultiplier = multiplier,
                exclusiveCustomerPool = exclusiveCustomerPool,
                minimumAbvPercent = -1f,
                restrictionReason = reason
            };
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static GameObject CreateImage(string name, Transform parent, Color color)
        {
            GameObject gameObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            return gameObject;
        }

        private static SpriteRenderer CreateWorldSprite(
            string name,
            Transform parent,
            Sprite sprite,
            Color color,
            Vector3 scale,
            int sortingOrder)
        {
            GameObject gameObject = new(name, typeof(SpriteRenderer));
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localScale = scale;
            SpriteRenderer renderer = gameObject.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static RectTransform CreateText(
            string name,
            Transform parent,
            string value,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color)
        {
            GameObject gameObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            Stretch(rect);
            TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return rect;
        }

        private static TextMeshProUGUI CreateTickerText(string name, RectTransform parent)
        {
            RectTransform rect = CreateText(
                name,
                parent,
                "방송 자막 준비 중",
                25f,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(800f, parent.rect.height);
            rect.anchoredPosition = Vector2.zero;
            TextMeshProUGUI text = rect.GetComponent<TextMeshProUGUI>();
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, Color color)
        {
            GameObject gameObject = CreateImage(name, parent, color);
            Button button = gameObject.AddComponent<Button>();
            CreateText(
                "Label",
                gameObject.transform,
                label,
                42f,
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

        private static void SetAnchoredRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void CaptureCanvas(Canvas canvas, string path)
        {
            const int width = 2560;
            const int height = 1440;
            GameObject cameraObject = new("__TVPreviewCamera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f;
            camera.cullingMask = ~0;

            RenderTexture target = new(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderMode previousMode = canvas.renderMode;
            Camera previousCamera = canvas.worldCamera;
            float previousDistance = canvas.planeDistance;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 100f;
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();

                RenderTexture.active = target;
                Texture2D texture = new(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = null;
                canvas.renderMode = previousMode;
                canvas.worldCamera = previousCamera;
                canvas.planeDistance = previousDistance;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
