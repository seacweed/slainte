using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    public enum BartendingSessionBuildMode
    {
        Runtime,
        Preview
    }

    public sealed class BartendingSessionInstance
    {
        public GameObject Root { get; internal set; }
        public Transform World { get; internal set; }
        public Camera WorldCamera { get; internal set; }
        public BartendingViewport Viewport { get; internal set; }
        public RectTransform SlotLayout { get; internal set; }
        public LiquidPool LiquidPool { get; internal set; }
        public IceBinController IceBin { get; internal set; }
        public IBartendingItem Beaker { get; internal set; }
        public IBartendingItem CobblerShaker { get; internal set; }
        public GlassController ServingGlass { get; internal set; }
        public BartendingInteractionOverlay InteractionOverlay { get; internal set; }
        public int RenderLayer { get; internal set; }
        public float ItemScale { get; internal set; } = 1f;
        public readonly List<SlotController> Slots = new();
        public readonly List<IBartendingItem> StartingTools = new();

        public void Destroy(bool immediate = false)
        {
            if (LiquidPool != null && LiquidPool.Instance == LiquidPool)
                LiquidPool.Instance = null;

            if (Viewport != null)
                Viewport.gameObject.SetActive(false);

            if (InteractionOverlay != null)
                InteractionOverlay.gameObject.SetActive(false);

            if (SlotLayout != null)
                SlotLayout.gameObject.SetActive(false);

            if (Root != null)
            {
                Root.SetActive(false);
                if (immediate || !Application.isPlaying)
                    Object.DestroyImmediate(Root);
                else
                    Object.Destroy(Root);
            }

            if (SlotLayout != null)
            {
                if (immediate || !Application.isPlaying)
                    Object.DestroyImmediate(SlotLayout.gameObject);
                else
                    Object.Destroy(SlotLayout.gameObject);
            }

            if (Viewport != null)
            {
                if (immediate || !Application.isPlaying)
                    Object.DestroyImmediate(Viewport.gameObject);
                else
                    Object.Destroy(Viewport.gameObject);
            }

            if (InteractionOverlay != null)
            {
                if (immediate || !Application.isPlaying)
                    Object.DestroyImmediate(InteractionOverlay.gameObject);
                else
                    Object.Destroy(InteractionOverlay.gameObject);
            }

            Root = null;
            World = null;
            WorldCamera = null;
            Viewport = null;
            SlotLayout = null;
            LiquidPool = null;
            IceBin = null;
            Beaker = null;
            CobblerShaker = null;
            ServingGlass = null;
            InteractionOverlay = null;
            Slots.Clear();
            StartingTools.Clear();
        }
    }

    public sealed class BartendingInteractionOverlay : MonoBehaviour, IBartendingServeTarget
    {
        private sealed class VesselLabel
        {
            public SlotController Slot;
            public IBartendingItem Item;
            public VesselLiquidTracker Tracker;
            public RectTransform Root;
            public TextMeshProUGUI Text;
        }

        private readonly Dictionary<SlotController, VesselLabel> labels = new();
        private readonly HashSet<SlotController> subscribedSlots = new();
        private readonly List<KeyValuePair<ItemDef, float>> visibleVolumes = new();
        private readonly StringBuilder contentsBuilder = new();

        private RectTransform rootRect;
        private Camera worldCamera;
        private GlassController servingGlass;
        private BusinessBartendingSettings settings;
        private CharacterStage characterStage;
        private Image servingTargetImage;
        private bool useFallbackServingTarget;
        private bool missingTargetWarningShown;
        private float nextContentsRefreshTime;

        public int VisibleVesselLabelCount => labels.Count;
        public bool IsServingTargetVisible =>
            servingTargetImage != null && servingTargetImage.gameObject.activeSelf;

        public bool TryGetVesselContentsText(IBartendingItem item, out string text)
        {
            foreach (VesselLabel label in labels.Values)
            {
                if (label != null && label.Item == item && label.Text != null)
                {
                    text = label.Text.text;
                    return true;
                }
            }

            text = string.Empty;
            return false;
        }

        public static BartendingInteractionOverlay Create(
            BartendingViewport viewport,
            Camera camera,
            GlassController glass,
            IReadOnlyList<SlotController> slots,
            BusinessBartendingSettings sessionSettings)
        {
            if (viewport == null || sessionSettings == null)
                return null;

            GameObject overlayObject = new GameObject(
                "BartendingInteractionOverlay",
                typeof(RectTransform));
            RectTransform overlayRect = (RectTransform)overlayObject.transform;
            Transform parent = viewport.transform.parent != null
                ? viewport.transform.parent
                : viewport.transform;
            overlayRect.SetParent(parent, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.sizeDelta = Vector2.zero;
            overlayRect.anchoredPosition = Vector2.zero;
            overlayRect.SetAsLastSibling();

            BartendingInteractionOverlay overlay =
                overlayObject.AddComponent<BartendingInteractionOverlay>();
            overlay.Initialize(camera, glass, slots, sessionSettings);
            return overlay;
        }

        public void ConfigureServingTarget(CharacterStage stage, bool allowFallbackTarget)
        {
            characterStage = stage;
            useFallbackServingTarget = allowFallbackTarget;
            missingTargetWarningShown = false;
        }

        public bool TryGetServeTargetScreenRect(out Rect screenRect)
        {
            if (characterStage != null
                && characterStage.TryGetActiveGroupScreenRect(out screenRect))
            {
                screenRect = ExpandRect(screenRect, settings.serveTargetPaddingPixels);
                return true;
            }

            if (useFallbackServingTarget)
            {
                Vector4 normalized = settings.sandboxServeTargetNormalized;
                screenRect = new Rect(
                    normalized.x * Screen.width,
                    normalized.y * Screen.height,
                    Mathf.Max(0f, normalized.z) * Screen.width,
                    Mathf.Max(0f, normalized.w) * Screen.height);
                screenRect = ExpandRect(screenRect, settings.serveTargetPaddingPixels);
                return screenRect.width > Mathf.Epsilon && screenRect.height > Mathf.Epsilon;
            }

            screenRect = default;
            return false;
        }

        private void Initialize(
            Camera camera,
            GlassController glass,
            IReadOnlyList<SlotController> slots,
            BusinessBartendingSettings sessionSettings)
        {
            rootRect = transform as RectTransform;
            worldCamera = camera;
            servingGlass = glass;
            settings = sessionSettings;
            servingTargetImage = CreateServingTargetImage();

            if (slots == null)
                return;

            for (int i = 0; i < slots.Count; i++)
            {
                SlotController slot = slots[i];
                if (slot == null)
                    continue;
                slot.OccupancyChanged += HandleSlotOccupancyChanged;
                subscribedSlots.Add(slot);
                HandleSlotOccupancyChanged(slot, slot.OccupiedItem);
            }
        }

        private Image CreateServingTargetImage()
        {
            GameObject targetObject = new GameObject(
                "ServingTarget",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Outline));
            RectTransform targetRect = (RectTransform)targetObject.transform;
            targetRect.SetParent(rootRect, false);
            targetRect.anchorMin = new Vector2(0.5f, 0.5f);
            targetRect.anchorMax = new Vector2(0.5f, 0.5f);
            targetRect.pivot = new Vector2(0.5f, 0.5f);

            Image image = targetObject.GetComponent<Image>();
            image.color = settings.serveTargetFillColor;
            image.raycastTarget = false;

            Outline outline = targetObject.GetComponent<Outline>();
            outline.effectColor = settings.serveTargetOutlineColor;
            float width = Mathf.Max(0f, settings.serveTargetOutlineWidth);
            outline.effectDistance = new Vector2(width, -width);
            outline.useGraphicAlpha = false;

            targetObject.SetActive(false);
            return image;
        }

        private void LateUpdate()
        {
            UpdateServingTarget();
            UpdateLabelPositions();

            if (Time.unscaledTime >= nextContentsRefreshTime)
            {
                nextContentsRefreshTime = Time.unscaledTime
                    + Mathf.Max(0.02f, settings.contentsRefreshInterval);
                RefreshLabelContents();
            }
        }

        private void UpdateServingTarget()
        {
            if (servingTargetImage == null)
                return;

            Rect targetRect = default;
            bool shouldShow = servingGlass != null
                && servingGlass.IsPickedUp
                && TryGetServeTargetScreenRect(out targetRect);
            servingTargetImage.gameObject.SetActive(shouldShow);

            if (shouldShow)
            {
                ApplyScreenRect(servingTargetImage.rectTransform, targetRect);
                missingTargetWarningShown = false;
                return;
            }

            if (servingGlass != null
                && servingGlass.IsPickedUp
                && !useFallbackServingTarget
                && !missingTargetWarningShown)
            {
                missingTargetWarningShown = true;
                Debug.LogWarning(
                    "[Bartending] 활성 손님 표시 영역이 없어 서빙 판정을 비활성화했습니다.");
            }
        }

        private void HandleSlotOccupancyChanged(SlotController slot, IBartendingItem item)
        {
            RemoveLabel(slot);
            if (slot == null || item == null)
                return;

            VesselLiquidTracker tracker = item.GameObject != null
                ? item.GameObject.GetComponentInChildren<VesselLiquidTracker>(true)
                : null;
            if (tracker == null)
                return;

            VesselLabel label = CreateVesselLabel(slot, item, tracker);
            labels.Add(slot, label);
            RefreshLabelContent(label);
        }

        private VesselLabel CreateVesselLabel(
            SlotController slot,
            IBartendingItem item,
            VesselLiquidTracker tracker)
        {
            GameObject labelObject = new GameObject(
                "VesselContents_" + slot.name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(rootRect, false);
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            Image background = labelObject.GetComponent<Image>();
            background.color = settings.contentsLabelBackgroundColor;
            background.raycastTarget = false;

            GameObject textObject = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.SetParent(labelRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 8f);
            textRect.offsetMax = new Vector2(-10f, -8f);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = Mathf.Max(8, settings.contentsLabelFontSize);
            text.color = settings.contentsLabelTextColor;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;

            return new VesselLabel
            {
                Slot = slot,
                Item = item,
                Tracker = tracker,
                Root = labelRect,
                Text = text
            };
        }

        private void RefreshLabelContents()
        {
            foreach (VesselLabel label in labels.Values)
                RefreshLabelContent(label);
        }

        private void RefreshLabelContent(VesselLabel label)
        {
            if (label == null || label.Tracker == null || label.Text == null)
                return;

            CocktailComposition composition = label.Tracker.BuildComposition();
            visibleVolumes.Clear();
            foreach (KeyValuePair<ItemDef, float> pair in composition.Volumes)
            {
                if (pair.Key != null
                    && pair.Value >= Mathf.Max(0f, settings.contentsMinimumVisibleMl))
                {
                    visibleVolumes.Add(pair);
                }
            }

            visibleVolumes.Sort((left, right) =>
            {
                int volumeOrder = right.Value.CompareTo(left.Value);
                if (volumeOrder != 0)
                    return volumeOrder;
                return string.Compare(
                    GetItemName(left.Key),
                    GetItemName(right.Key),
                    System.StringComparison.Ordinal);
            });

            contentsBuilder.Clear();
            if (visibleVolumes.Count == 0)
            {
                contentsBuilder.AppendLine("비어 있음");
            }
            else
            {
                for (int i = 0; i < visibleVolumes.Count; i++)
                {
                    KeyValuePair<ItemDef, float> pair = visibleVolumes[i];
                    contentsBuilder.Append(GetItemName(pair.Key));
                    contentsBuilder.Append(' ');
                    contentsBuilder.Append(pair.Value.ToString("0.#"));
                    contentsBuilder.AppendLine(" ml");
                }
                contentsBuilder.AppendLine("─────────");
            }

            contentsBuilder.Append("합계 ");
            contentsBuilder.Append(composition.TotalVolumeMl.ToString("0.#"));
            contentsBuilder.Append(" ml");
            label.Text.text = contentsBuilder.ToString();
        }

        private void UpdateLabelPositions()
        {
            if (!BartendingViewport.TryGetInputScreenRect(out Rect viewportRect))
                return;

            foreach (VesselLabel label in labels.Values)
            {
                if (label?.Item?.GameObject == null || label.Root == null)
                    continue;

                if (!TryGetVisualBounds(label.Item.GameObject, out Bounds bounds))
                    continue;

                Vector2 left = BartendingViewport.GetPointerScreenPosition(
                    worldCamera,
                    new Vector3(bounds.min.x, bounds.center.y, bounds.center.z));
                Vector2 right = BartendingViewport.GetPointerScreenPosition(
                    worldCamera,
                    new Vector3(bounds.max.x, bounds.center.y, bounds.center.z));
                Vector2 center = BartendingViewport.GetPointerScreenPosition(
                    worldCamera,
                    bounds.center);

                Vector2 size = settings.contentsLabelSizePixels;
                size.x = Mathf.Max(80f, size.x);
                size.y = Mathf.Max(50f, size.y);
                float x = Mathf.Max(left.x, right.x)
                    + Mathf.Max(0f, settings.contentsLabelOffsetPixels.x);
                if (x + size.x > viewportRect.xMax)
                {
                    x = Mathf.Min(left.x, right.x)
                        - Mathf.Max(0f, settings.contentsLabelOffsetPixels.x)
                        - size.x;
                }

                float y = center.y + settings.contentsLabelOffsetPixels.y - size.y * 0.5f;
                x = Mathf.Clamp(x, viewportRect.xMin, viewportRect.xMax - size.x);
                y = Mathf.Clamp(y, viewportRect.yMin, viewportRect.yMax - size.y);
                ApplyScreenRect(label.Root, new Rect(x, y, size.x, size.y));
            }
        }

        private static bool TryGetVisualBounds(GameObject item, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            foreach (SpriteRenderer renderer in item.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer == null || !renderer.enabled)
                    continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (hasBounds)
                return true;

            Collider2D collider = item.GetComponentInChildren<Collider2D>();
            if (collider == null)
                return false;
            bounds = collider.bounds;
            return true;
        }

        private void ApplyScreenRect(RectTransform target, Rect screenRect)
        {
            if (target == null || rootRect == null)
                return;

            Camera canvasCamera = GetCanvasCamera(rootRect);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rootRect,
                    screenRect.min,
                    canvasCamera,
                    out Vector2 localMin)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rootRect,
                    screenRect.max,
                    canvasCamera,
                    out Vector2 localMax))
            {
                return;
            }

            target.anchoredPosition = (localMin + localMax) * 0.5f;
            target.sizeDelta = new Vector2(
                Mathf.Abs(localMax.x - localMin.x),
                Mathf.Abs(localMax.y - localMin.y));
        }

        private void RemoveLabel(SlotController slot)
        {
            if (slot == null || !labels.TryGetValue(slot, out VesselLabel label))
                return;

            labels.Remove(slot);
            if (label?.Root != null)
                Destroy(label.Root.gameObject);
        }

        private void OnDestroy()
        {
            foreach (SlotController slot in subscribedSlots)
            {
                if (slot != null)
                    slot.OccupancyChanged -= HandleSlotOccupancyChanged;
            }
            subscribedSlots.Clear();
            labels.Clear();
        }

        private static string GetItemName(ItemDef item)
        {
            if (item == null)
                return "알 수 없음";
            if (!string.IsNullOrWhiteSpace(item.displayName))
                return item.displayName;
            if (!string.IsNullOrWhiteSpace(item.id))
                return item.id;
            return item.name;
        }

        private static Rect ExpandRect(Rect rect, float padding)
        {
            float safePadding = Mathf.Max(0f, padding);
            return Rect.MinMaxRect(
                rect.xMin - safePadding,
                rect.yMin - safePadding,
                rect.xMax + safePadding,
                rect.yMax + safePadding);
        }

        private static Camera GetCanvasCamera(RectTransform rectTransform)
        {
            Canvas canvas = rectTransform != null
                ? rectTransform.GetComponentInParent<Canvas>()
                : null;
            return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
        }
    }

    public static class BartendingSessionBuilder
    {
        public static BartendingSessionInstance Build(
            Transform parent,
            RectTransform counter,
            RectTransform slotLayoutTemplate,
            BusinessBartendingSettings settings,
            BartendingSessionBuildMode mode = BartendingSessionBuildMode.Runtime)
        {
            if (parent == null || counter == null || settings == null)
                return null;

            bool isPreview = mode == BartendingSessionBuildMode.Preview;
            int renderLayer = Mathf.Clamp(settings.renderLayer, 8, 31);
            BartendingSessionInstance session = new BartendingSessionInstance
            {
                RenderLayer = renderLayer
            };

            GameObject root = new GameObject(
                isPreview ? "__BartendingLayoutPreview" : "BartendingSession");
            root.transform.SetParent(parent, false);
            if (isPreview)
                root.hideFlags = HideFlags.DontSaveInEditor;
            session.Root = root;

            GameObject world = new GameObject("BartendingWorld");
            world.transform.SetParent(root.transform, false);
            session.World = world.transform;

            session.WorldCamera = CreateWorldCamera(world.transform, settings, renderLayer);
            session.Viewport = CreateViewport(counter, session.WorldCamera, settings, !isPreview);
            Canvas.ForceUpdateCanvases();
            session.SlotLayout = CreateSessionSlotLayout(
                slotLayoutTemplate,
                session.Viewport != null ? session.Viewport.transform as RectTransform : null,
                counter);
            Canvas.ForceUpdateCanvases();

            GetSlotLayout(
                session.SlotLayout,
                session.Viewport,
                settings,
                out List<Vector3> slotPositions,
                out float itemScale);
            session.ItemScale = itemScale;
            session.Slots.AddRange(CreateSlots(world.transform, settings, slotPositions, itemScale));

            session.Beaker = CreateItem(
                settings.beakerPrefab,
                world.transform,
                "Beaker",
                settings.beakerPosition,
                renderLayer,
                itemScale);
            session.CobblerShaker = CreateItem(
                settings.cobblerShakerPrefab,
                world.transform,
                "CobblerShaker",
                settings.cobblerShakerPosition,
                renderLayer,
                itemScale);
            session.ServingGlass = CreateItem(
                settings.glassPrefab,
                world.transform,
                "Glass",
                settings.glassPosition,
                renderLayer,
                itemScale) as GlassController;

            ConfigureRotatingMovement(session.Beaker, settings);
            ConfigureRotatingMovement(session.CobblerShaker, settings);
            ConfigureRotatingMovement(session.ServingGlass, settings);

            session.StartingTools.Add(session.Beaker);
            session.StartingTools.Add(session.CobblerShaker);
            session.StartingTools.Add(session.ServingGlass);

            session.IceBin = IceBinController.Create(
                world.transform,
                settings,
                renderLayer,
                itemScale,
                isPreview);

            if (!isPreview)
            {
                session.LiquidPool = CreateLiquidPool(world.transform, settings, renderLayer, itemScale);
                session.InteractionOverlay = BartendingInteractionOverlay.Create(
                    session.Viewport,
                    session.WorldCamera,
                    session.ServingGlass,
                    session.Slots,
                    settings);
            }

            if (isPreview)
            {
                SnapStartingItemsImmediately(session.StartingTools, session.Slots);
                SetDontSaveInEditor(session.Root);
                SetDontSaveInEditor(session.Viewport != null ? session.Viewport.gameObject : null);
                SetDontSaveInEditor(session.SlotLayout != null ? session.SlotLayout.gameObject : null);
            }

            return session;
        }

        public static void ConfigureRotatingMovement(
            IBartendingItem item,
            BusinessBartendingSettings settings)
        {
            if (item == null || settings == null)
                return;

            if (item is BottleController bottle)
            {
                bottle.ConfigureHorizontalRotationMovement(
                    settings.rotationHorizontalSensitivity,
                    settings.rotationHorizontalScreenPadding);
            }
            else if (item is BeakerController beaker)
            {
                beaker.ConfigureHorizontalRotationMovement(
                    settings.rotationHorizontalSensitivity,
                    settings.rotationHorizontalScreenPadding);
            }
            else if (item is GlassController glass)
            {
                glass.ConfigureHorizontalRotationMovement(
                    settings.rotationHorizontalSensitivity,
                    settings.rotationHorizontalScreenPadding);
            }
        }

        public static Camera CreateWorldCamera(
            Transform parent,
            BusinessBartendingSettings settings,
            int renderLayer)
        {
            GameObject cameraObject = new GameObject("BartendingCamera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.orthographic = true;
            camera.orthographicSize = settings.cameraOrthographicSize;
            camera.cullingMask = 1 << renderLayer;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.depth = -10f;
            return camera;
        }

        public static BartendingViewport CreateViewport(
            RectTransform counter,
            Camera camera,
            BusinessBartendingSettings settings,
            bool registerForInput)
        {
            GameObject viewObject = new GameObject(
                "BartendingViewport",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            RectTransform viewRect = (RectTransform)viewObject.transform;
            Transform viewportParent = counter.parent != null ? counter.parent : counter;
            viewRect.SetParent(viewportParent, false);
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.pivot = new Vector2(0.5f, 0.5f);
            viewRect.sizeDelta = Vector2.zero;
            viewRect.anchoredPosition = Vector2.zero;

            if (counter.parent != null)
                viewRect.SetSiblingIndex(counter.GetSiblingIndex() + 1);
            else
                viewRect.SetAsFirstSibling();

            RawImage image = viewObject.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.color = Color.white;

            BartendingViewport viewport = viewObject.AddComponent<BartendingViewport>();
            viewport.Initialize(camera, settings.renderTextureSize, registerForInput);
            return viewport;
        }

        public static void GetSlotLayout(
            RectTransform tableSlots,
            BartendingViewport viewport,
            BusinessBartendingSettings settings,
            out List<Vector3> positions,
            out float itemScale)
        {
            positions = settings.slotPositions != null
                ? new List<Vector3>(settings.slotPositions)
                : new List<Vector3>();
            itemScale = 1f;

            if (tableSlots == null || viewport == null)
                return;

            List<MappedSlot> mappedSlots = new List<MappedSlot>();
            foreach (UIDropSlot dropSlot in tableSlots.GetComponentsInChildren<UIDropSlot>(true))
            {
                if (dropSlot.transform is RectTransform rect
                    && viewport.TryMapRectToWorld(rect, out Vector3 center, out Vector2 size))
                {
                    mappedSlots.Add(new MappedSlot(center, size.x));
                }
            }

            if (mappedSlots.Count == 0)
                return;

            mappedSlots.Sort((left, right) => left.Position.x.CompareTo(right.Position.x));
            positions.Clear();
            float accumulatedWidth = 0f;
            foreach (MappedSlot mappedSlot in mappedSlots)
            {
                positions.Add(mappedSlot.Position);
                accumulatedWidth += mappedSlot.Width;
            }

            BoxCollider2D referenceCollider = settings.slotPrefab != null
                ? settings.slotPrefab.GetComponent<BoxCollider2D>()
                : null;
            float referenceWidth = referenceCollider != null ? referenceCollider.size.x : 1f;
            if (referenceWidth > Mathf.Epsilon)
            {
                float averageWidth = accumulatedWidth / mappedSlots.Count;
                itemScale = Mathf.Clamp(averageWidth / referenceWidth, 0.1f, 2f);
            }
        }

        public static RectTransform CreateSessionSlotLayout(
            RectTransform template,
            RectTransform viewport,
            RectTransform counter)
        {
            if (template == null)
                return null;

            Transform targetParent = viewport != null && viewport.parent != null
                ? viewport.parent
                : template.parent;
            RectTransform layout = Object.Instantiate(template, targetParent, false);
            layout.name = "BartendingSessionSlots";
            layout.anchorMin = new Vector2(0.5f, 0.5f);
            layout.anchorMax = new Vector2(0.5f, 0.5f);
            layout.pivot = new Vector2(0.5f, 0.5f);
            layout.localRotation = Quaternion.identity;
            layout.localScale = Vector3.one;
            layout.anchoredPosition = Vector2.zero;
            if (viewport != null)
                layout.SetSiblingIndex(viewport.GetSiblingIndex());

            foreach (UIDropSlot dropSlot in layout.GetComponentsInChildren<UIDropSlot>(true))
                dropSlot.enabled = false;
            foreach (Graphic graphic in layout.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
                graphic.enabled = false;
            }

            layout.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(layout);
            Canvas.ForceUpdateCanvases();
            AlignSlotLayoutToVisibleTable(layout, counter, viewport);
            return layout;
        }

        public static List<SlotController> CreateSlots(
            Transform parent,
            BusinessBartendingSettings settings,
            List<Vector3> positions,
            float itemScale)
        {
            List<SlotController> slots = new List<SlotController>();
            if (settings.slotPrefab == null || positions == null)
                return slots;

            int slotLayer = LayerMask.NameToLayer("Slot");
            if (slotLayer < 0)
                slotLayer = 0;

            for (int i = 0; i < positions.Count; i++)
            {
                GameObject slot = Object.Instantiate(settings.slotPrefab, parent);
                slot.name = "BartendingSlot_" + i;
                slot.transform.localPosition = positions[i];
                slot.transform.localRotation = Quaternion.identity;
                slot.transform.localScale = Vector3.one * itemScale;
                SetLayerRecursively(slot, slotLayer);

                SlotController slotController = slot.GetComponent<SlotController>();
                if (slotController != null)
                    slots.Add(slotController);
            }

            return slots;
        }

        public static IBartendingItem CreateItem(
            GameObject prefab,
            Transform parent,
            string instanceName,
            Vector3 position,
            int renderLayer,
            float itemScale)
        {
            if (prefab == null)
            {
                Debug.LogWarning(instanceName + " prefab is not assigned in bartending settings.");
                return null;
            }

            GameObject item = Object.Instantiate(prefab, parent);
            item.name = instanceName;
            item.transform.localPosition = position;
            item.transform.localRotation = Quaternion.identity;
            item.transform.localScale = Vector3.one * itemScale;
            SetLayerRecursively(item, renderLayer);
            foreach (VesselLiquidTracker tracker
                     in item.GetComponentsInChildren<VesselLiquidTracker>(true))
            {
                tracker.SetDebugViewEnabled(false);
            }

            return item.GetComponent<IBartendingItem>();
        }

        public static LiquidPool CreateLiquidPool(
            Transform parent,
            BusinessBartendingSettings settings,
            int renderLayer,
            float itemScale)
        {
            if (settings.liquidParticlePrefab == null || LiquidPool.Instance != null)
                return null;

            GameObject poolObject = new GameObject("LiquidPoolManager");
            poolObject.SetActive(false);
            poolObject.transform.SetParent(parent, false);
            poolObject.transform.localScale = Vector3.one * itemScale;

            LiquidPool pool = poolObject.AddComponent<LiquidPool>();
            pool.particlePrefab = settings.liquidParticlePrefab;
            pool.poolSize = settings.liquidPoolSize;

            poolObject.SetActive(true);
            SetLayerRecursively(poolObject, renderLayer);
            return pool;
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
                return;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        private static void SetDontSaveInEditor(GameObject root)
        {
            if (root == null)
                return;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.hideFlags |= HideFlags.DontSaveInEditor;
        }

        private static void SnapStartingItemsImmediately(
            List<IBartendingItem> tools,
            List<SlotController> slots)
        {
            for (int i = 0; i < tools.Count && i < slots.Count; i++)
            {
                IBartendingItem item = tools[i];
                SlotController slot = slots[i];
                if (item == null || slot == null)
                    continue;

                slot.Occupy(item);
                item.SnapToSlot(slot.transform, slot);
            }
        }

        private static void AlignSlotLayoutToVisibleTable(
            RectTransform layout,
            RectTransform content,
            RectTransform viewport)
        {
            if (layout == null || content == null || viewport == null)
                return;

            RectTransform visibleViewport = GetRootCanvasRect(viewport);
            if (visibleViewport == null)
                visibleViewport = viewport;

            Rect contentScreenRect = GetScreenRect(content);
            Rect viewportScreenRect = GetScreenRect(visibleViewport);
            float xMin = Mathf.Max(contentScreenRect.xMin, viewportScreenRect.xMin);
            float xMax = Mathf.Min(contentScreenRect.xMax, viewportScreenRect.xMax);
            float yMin = Mathf.Max(contentScreenRect.yMin, viewportScreenRect.yMin);
            float yMax = Mathf.Min(contentScreenRect.yMax, viewportScreenRect.yMax);
            if (xMax <= xMin || yMax <= yMin
                || !TryGetSlotLayoutScreenCenter(layout, out Vector2 slotScreenCenter))
            {
                return;
            }

            Vector2 targetScreenCenter = new Vector2(
                (xMin + xMax) * 0.5f,
                (yMin + yMax) * 0.5f);
            Camera layoutCamera = GetCanvasCamera(layout);
            Vector2 layoutScreenPosition = RectTransformUtility.WorldToScreenPoint(
                layoutCamera,
                layout.position);
            Vector2 alignedScreenPosition =
                layoutScreenPosition + targetScreenCenter - slotScreenCenter;

            RectTransform parent = layout.parent as RectTransform;
            if (parent != null
                && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parent,
                    alignedScreenPosition,
                    GetCanvasCamera(parent),
                    out Vector3 alignedWorldPosition))
            {
                layout.position = alignedWorldPosition;
            }
        }

        private static bool TryGetSlotLayoutScreenCenter(
            RectTransform layout,
            out Vector2 screenCenter)
        {
            UIDropSlot[] dropSlots = layout.GetComponentsInChildren<UIDropSlot>(true);
            bool hasBounds = false;
            Rect bounds = default;
            foreach (UIDropSlot dropSlot in dropSlots)
            {
                if (dropSlot.transform is not RectTransform slotRect)
                    continue;

                Rect slotScreenRect = GetScreenRect(slotRect);
                if (!hasBounds)
                {
                    bounds = slotScreenRect;
                    hasBounds = true;
                }
                else
                {
                    bounds = Rect.MinMaxRect(
                        Mathf.Min(bounds.xMin, slotScreenRect.xMin),
                        Mathf.Min(bounds.yMin, slotScreenRect.yMin),
                        Mathf.Max(bounds.xMax, slotScreenRect.xMax),
                        Mathf.Max(bounds.yMax, slotScreenRect.yMax));
                }
            }

            screenCenter = hasBounds ? bounds.center : default;
            return hasBounds;
        }

        private static RectTransform GetRootCanvasRect(RectTransform rectTransform)
        {
            Canvas canvas = rectTransform != null
                ? rectTransform.GetComponentInParent<Canvas>()
                : null;
            return canvas != null && canvas.rootCanvas != null
                ? canvas.rootCanvas.transform as RectTransform
                : null;
        }

        private static Rect GetScreenRect(RectTransform rectTransform)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Camera canvasCamera = GetCanvasCamera(rectTransform);
            Vector2 first = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[0]);
            float xMin = first.x;
            float xMax = first.x;
            float yMin = first.y;
            float yMax = first.y;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[i]);
                xMin = Mathf.Min(xMin, point.x);
                xMax = Mathf.Max(xMax, point.x);
                yMin = Mathf.Min(yMin, point.y);
                yMax = Mathf.Max(yMax, point.y);
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static Camera GetCanvasCamera(RectTransform rectTransform)
        {
            Canvas canvas = rectTransform != null
                ? rectTransform.GetComponentInParent<Canvas>()
                : null;
            return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
        }

        private readonly struct MappedSlot
        {
            public MappedSlot(Vector3 position, float width)
            {
                Position = position;
                Width = width;
            }

            public Vector3 Position { get; }
            public float Width { get; }
        }
    }
}
