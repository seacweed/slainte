using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    public sealed class ToolCabinetController : MonoBehaviour
    {
        private const string CatalogResourcePath =
            "Bartending/ToolCabinet/ToolCabinetCatalog";
        private const string SettingsResourcePath =
            "Bartending/BusinessBartendingSettings";
        private static readonly ToolKind[] CabinetToolOrder =
        {
            ToolKind.Jigger,
            ToolKind.CobblerShaker,
            ToolKind.BarSpoon,
            ToolKind.IceBucket
        };
        private static readonly string[] CabinetGlassOrder =
        {
            "glass_rock",
            "glass_martini",
            "glass_highball",
            "glass_hurricane"
        };

        private readonly Dictionary<string, ToolCabinetItemView> itemViews =
            new(StringComparer.OrdinalIgnoreCase);
        private BusinessBartendingBootstrap bootstrap;
        private ToolCabinetCatalog catalog;
        private RectTransform runtimeRoot;
        private RectTransform iceMakerArea;
        private Canvas hostCanvas;
        private bool suppressNextPickup;
        private int pickupSuppressionArmedFrame = -1;

        public static ToolCabinetController Active { get; private set; }
        public bool IsConfigured { get; private set; }
        public bool IsSessionReady => bootstrap != null && bootstrap.IsSessionReady;
        public RectTransform WorldRenderExtensionRect { get; private set; }

        public void Initialize(Scene scene, BusinessBartendingBootstrap owner)
        {
            bootstrap = owner;
            BusinessBartendingSettings settings =
                Resources.Load<BusinessBartendingSettings>(SettingsResourcePath);
            if (settings == null || !settings.useToolCabinet)
                return;

            catalog = Resources.Load<ToolCabinetCatalog>(CatalogResourcePath);
            RectTransform drawerArea = FindNamedRectTransform(scene, "DrawerArea");
            RectTransform drawer = FindNamedRectTransform(scene, "Drawer");
            RectTransform drawerContent = FindNamedRectTransform(scene, "DrawerContent");
            if (catalog == null
                || catalog.backgroundSprite == null
                || drawerArea == null
                || drawer == null
                || drawerContent == null)
            {
                Debug.LogWarning("Tool cabinet assets or scene anchors are unavailable; fixed tools remain enabled.");
                return;
            }

            hostCanvas = drawerArea.GetComponentInParent<Canvas>();
            ConfigureBackground(drawer, drawerContent);
            BuildRuntimeUi(drawerContent);
            IsConfigured = runtimeRoot != null;
            if (IsConfigured)
            {
                WorldRenderExtensionRect = drawer;
                Active = this;
            }
        }

        public void NotifySessionReady()
        {
            bootstrap?.PrepareCabinetInventory(catalog);
            foreach (ToolCabinetItemView view in itemViews.Values)
                view?.RefreshFromSlot();
        }

        public void NotifySessionDestroyed()
        {
            ClearPickupSuppression();
            foreach (ToolCabinetItemView view in itemViews.Values)
                view?.BindSlot(null);
        }

        public void BindCabinetSlot(string definitionId, SlotController slot)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                return;
            if (itemViews.TryGetValue(definitionId, out ToolCabinetItemView view))
                view?.BindSlot(slot);
        }

        public void NotifyAvailability(string definitionId, bool available)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                return;
            if (itemViews.TryGetValue(definitionId, out ToolCabinetItemView view))
                view?.RefreshFromSlot();
        }

        public static bool TryReturnHeldItem(
            IBartendingItem item,
            Camera inputCamera,
            Vector2 screenPosition)
        {
            ToolCabinetController cabinet = Active;
            if (cabinet == null || item == null)
                return false;

            ToolCabinetItemView target = cabinet.FindSlotAt(screenPosition);
            if (target == null)
                return false;

            bool returned = cabinet.bootstrap.TryReturnCabinetItem(
                    item,
                    target.DefinitionId,
                    out string failure);
            if (returned)
                cabinet.SuppressNextPickup();
            else if (!string.IsNullOrWhiteSpace(failure))
            {
                Debug.LogWarning(failure);
            }

            // 도구장 슬롯에서 처리된 클릭이 바 슬롯 드롭으로 새지 않게 한다.
            return true;
        }

        public static bool IsHeldItemOverIceMaker(IBartendingItem item, Camera worldCamera)
        {
            ToolCabinetController cabinet = Active;
            if (cabinet == null
                || item == null
                || !item.IsPickedUp
                || item.GameObject == null
                || cabinet.iceMakerArea == null)
            {
                return false;
            }

            Vector2 screenPosition = BartendingViewport.GetPointerScreenPosition(
                worldCamera,
                item.GameObject.transform.position);
            Camera canvasCamera = cabinet.hostCanvas != null
                && cabinet.hostCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? cabinet.hostCanvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(
                cabinet.iceMakerArea,
                screenPosition,
                canvasCamera);
        }

        internal bool TryPickUp(string definitionId, Vector2 screenPosition)
        {
            if (bootstrap == null
                || string.IsNullOrWhiteSpace(definitionId))
            {
                return false;
            }

            if (ConsumePickupSuppression())
                return false;

            bool success = bootstrap.TryPickUpCabinetItem(
                definitionId,
                screenPosition,
                out string failure);
            if (!success && !string.IsNullOrWhiteSpace(failure))
                Debug.LogWarning(failure);
            return success;
        }

        private void SuppressNextPickup()
        {
            suppressNextPickup = true;
            pickupSuppressionArmedFrame = Time.frameCount;
        }

        private bool ConsumePickupSuppression()
        {
            if (!suppressNextPickup)
                return false;

            ClearPickupSuppression();
            return true;
        }

        private void LateUpdate()
        {
            if (suppressNextPickup
                && Time.frameCount > pickupSuppressionArmedFrame
                && !Input.GetMouseButton(0))
            {
                ClearPickupSuppression();
            }
        }

        private void ClearPickupSuppression()
        {
            suppressNextPickup = false;
            pickupSuppressionArmedFrame = -1;
        }

        private void ConfigureBackground(RectTransform drawer, RectTransform drawerContent)
        {
            Image image = drawer.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = catalog.backgroundSprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }

            AspectRatioFitter fitter = drawer.GetComponent<AspectRatioFitter>();
            if (fitter != null)
            {
                fitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
                fitter.aspectRatio = 2560f / 820f;
            }

            drawerContent.gameObject.SetActive(true);
            drawerContent.anchorMin = new Vector2(0f, 1f);
            drawerContent.anchorMax = new Vector2(1f, 1f);
            drawerContent.pivot = new Vector2(0.5f, 1f);
            drawerContent.anchoredPosition = Vector2.zero;
            drawerContent.sizeDelta = new Vector2(0f, 820f);

            HorizontalLayoutGroup layout = drawerContent.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
                layout.enabled = false;
            SetNamedChildActive(drawerContent, "ToolInvenPanel", false);
            SetNamedChildActive(drawerContent, "GlassInvenPanel", false);
        }

        private void BuildRuntimeUi(RectTransform parent)
        {
            Transform existing = parent.Find("__ToolCabinetRuntimeUI");
            if (existing != null)
                Destroy(existing.gameObject);

            runtimeRoot = CreateRect("__ToolCabinetRuntimeUI", parent);
            Stretch(runtimeRoot, Vector2.zero, Vector2.one);

            RectTransform toolArea = CreateSpriteArea(
                "ToolArea",
                catalog.toolStoragePixels,
                new Rect(0.02f, 0.39f, 0.52f, 0.55f));
            RectTransform glassArea = CreateSpriteArea(
                "GlassArea",
                catalog.glassStoragePixels,
                new Rect(0.57f, 0.39f, 0.41f, 0.55f));
            iceMakerArea = CreateSpriteArea(
                "IceMakerArea",
                catalog.iceMakerPixels,
                new Rect(0.51f, 0.07f, 0.47f, 0.23f));

            BuildToolSlots(toolArea);
            BuildGlassSlots(glassArea);
        }

        private void BuildToolSlots(RectTransform parent)
        {
            ToolDef[] tools = catalog.tools ?? Array.Empty<ToolDef>();
            int slotIndex = 0;
            for (int orderIndex = 0; orderIndex < CabinetToolOrder.Length; orderIndex++)
            {
                ToolDef definition = FindTool(tools, CabinetToolOrder[orderIndex]);
                if (definition == null || string.IsNullOrWhiteSpace(definition.StableId))
                {
                    continue;
                }

                ToolCabinetItemView view = ToolCabinetItemView.Create(
                    parent,
                    this,
                    definition.StableId,
                    definition.displayName,
                    definition.cabinetLayers ?? Array.Empty<Sprite>(),
                    slotIndex++,
                    CabinetToolOrder.Length,
                    definition.kind == ToolKind.BarSpoon);
                itemViews[definition.StableId] = view;
            }
        }

        private void BuildGlassSlots(RectTransform parent)
        {
            GlassDef[] glasses = catalog.glasses ?? Array.Empty<GlassDef>();
            int slotIndex = 0;
            for (int orderIndex = 0; orderIndex < CabinetGlassOrder.Length; orderIndex++)
            {
                GlassDef definition = FindGlass(glasses, CabinetGlassOrder[orderIndex]);
                if (definition == null || string.IsNullOrWhiteSpace(definition.StableId))
                    continue;

                Sprite[] sprites = definition.cabinetSprite != null
                    ? new[] { definition.cabinetSprite }
                    : Array.Empty<Sprite>();
                ToolCabinetItemView view = ToolCabinetItemView.Create(
                    parent,
                    this,
                    definition.StableId,
                    definition.displayName,
                    sprites,
                    slotIndex++,
                    CabinetGlassOrder.Length,
                    false);
                itemViews[definition.StableId] = view;
            }
        }

        private RectTransform CreateSpriteArea(
            string areaName,
            Rect pixelRect,
            Rect fallbackNormalized)
        {
            Rect normalized = catalog != null
                && catalog.TryGetNormalizedRect(pixelRect, out Rect configured)
                    ? configured
                    : fallbackNormalized;
            RectTransform area = CreateRect(areaName, runtimeRoot);
            Stretch(area, normalized.min, normalized.max);
            return area;
        }

        private static ToolDef FindTool(ToolDef[] tools, ToolKind kind)
        {
            for (int i = 0; i < tools.Length; i++)
            {
                if (tools[i] != null && tools[i].kind == kind)
                    return tools[i];
            }
            return null;
        }

        private static GlassDef FindGlass(GlassDef[] glasses, string stableId)
        {
            for (int i = 0; i < glasses.Length; i++)
            {
                if (glasses[i] != null
                    && string.Equals(
                        glasses[i].StableId,
                        stableId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return glasses[i];
                }
            }
            return null;
        }

        private ToolCabinetItemView FindSlotAt(Vector2 screenPosition)
        {
            Camera canvasCamera = hostCanvas != null
                && hostCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? hostCanvas.worldCamera
                : null;
            foreach (ToolCabinetItemView view in itemViews.Values)
            {
                if (view != null && view.ContainsScreenPoint(screenPosition, canvasCamera))
                    return view;
            }
            return null;
        }

        private void OnDestroy()
        {
            if (Active == this)
                Active = null;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject instance = new GameObject(name, typeof(RectTransform));
            instance.layer = parent.gameObject.layer;
            RectTransform rect = instance.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetNamedChildActive(Transform parent, string childName, bool active)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName)
                    child.gameObject.SetActive(active);
            }
        }

        private static RectTransform FindNamedRectTransform(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rect.name == objectName)
                        return rect;
                }
            }
            return null;
        }
    }

    public sealed class ToolCabinetItemView : MonoBehaviour, IPointerClickHandler
    {
        private ToolCabinetController owner;
        private RectTransform contentRoot;
        private CanvasGroup contentGroup;
        private Image slotBackground;
        private SlotController boundSlot;
        private readonly List<Image> contentImages = new();
        private Sprite[] defaultSprites = Array.Empty<Sprite>();

        public string DefinitionId { get; private set; } = string.Empty;

        public static ToolCabinetItemView Create(
            RectTransform parent,
            ToolCabinetController owner,
            string definitionId,
            string displayName,
            Sprite[] sprites,
            int index,
            int count,
            bool flipVertical)
        {
            GameObject root = new GameObject(
                "CabinetSlot_" + (string.IsNullOrWhiteSpace(displayName) ? definitionId : displayName),
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(ToolCabinetItemView));
            root.layer = parent.gameObject.layer;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            float min = index / (float)Mathf.Max(1, count);
            float max = (index + 1f) / Mathf.Max(1, count);
            rect.anchorMin = new Vector2(min, 0f);
            rect.anchorMax = new Vector2(max, 1f);
            rect.offsetMin = new Vector2(4f, 4f);
            rect.offsetMax = new Vector2(-4f, -4f);

            Image background = root.GetComponent<Image>();
            background.color = Color.clear;
            background.raycastTarget = true;

            RectTransform contents = CreateRect("Contents", rect);
            contents.anchorMin = new Vector2(0.5f, 0.5f);
            contents.anchorMax = new Vector2(0.5f, 0.5f);
            contents.pivot = new Vector2(0.5f, 0.5f);
            contents.anchoredPosition = Vector2.zero;
            contents.sizeDelta = Vector2.zero;
            CanvasGroup group = contents.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            ToolCabinetItemView view = root.GetComponent<ToolCabinetItemView>();
            view.owner = owner;
            view.DefinitionId = definitionId ?? string.Empty;
            view.contentRoot = contents;
            view.contentGroup = group;
            view.slotBackground = background;
            view.BuildImages(sprites ?? Array.Empty<Sprite>(), flipVertical);
            view.RefreshFromSlot();
            return view;
        }

        public void BindSlot(SlotController slot)
        {
            if (boundSlot != null)
                boundSlot.OccupancyChanged -= HandleOccupancyChanged;
            boundSlot = slot;
            if (boundSlot != null)
                boundSlot.OccupancyChanged += HandleOccupancyChanged;
            RefreshFromSlot();
        }

        public void RefreshFromSlot()
        {
            bool occupied = boundSlot != null && boundSlot.IsOccupied;
            IBartendingItem item = occupied ? boundSlot.OccupiedItem : null;
            ApplyCabinetVisual(item);
            if (item != null)
                TryApplyWorldProjectedSize(item);
            if (contentGroup != null)
                contentGroup.alpha = occupied ? 1f : 0f;
            if (slotBackground != null)
                slotBackground.color = Color.clear;
        }

        public bool ContainsScreenPoint(Vector2 screenPosition, Camera canvasCamera)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(
                transform as RectTransform,
                screenPosition,
                canvasCamera);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || owner == null
                || !owner.IsSessionReady
                || boundSlot == null
                || !boundSlot.IsOccupied)
            {
                return;
            }

            owner.TryPickUp(DefinitionId, eventData.position);
        }

        private void HandleOccupancyChanged(SlotController slot, IBartendingItem item)
        {
            RefreshFromSlot();
        }

        private void BuildImages(Sprite[] sprites, bool flipVertical)
        {
            defaultSprites = sprites != null
                ? (Sprite[])sprites.Clone()
                : Array.Empty<Sprite>();
            contentImages.Clear();
            for (int i = 0; i < defaultSprites.Length; i++)
            {
                GameObject layer = new GameObject(
                    "Layer_" + i,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                layer.layer = gameObject.layer;
                RectTransform rect = layer.GetComponent<RectTransform>();
                rect.SetParent(contentRoot, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                Image image = layer.GetComponent<Image>();
                image.sprite = defaultSprites[i];
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.rectTransform.localScale = flipVertical
                    ? new Vector3(1f, -1f, 1f)
                    : Vector3.one;
                contentImages.Add(image);
            }
        }

        private void ApplyCabinetVisual(IBartendingItem item)
        {
            IBartendingCabinetVisualProvider provider =
                item as IBartendingCabinetVisualProvider;
            int count = Mathf.Min(contentImages.Count, defaultSprites.Length);
            for (int i = 0; i < count; i++)
            {
                Image image = contentImages[i];
                if (image == null)
                    continue;

                Sprite fallback = defaultSprites[i];
                image.sprite = provider != null
                    ? provider.GetCabinetVisualSprite(i, fallback)
                    : fallback;
            }
        }

        private void TryApplyWorldProjectedSize(IBartendingItem item)
        {
            if (contentRoot == null || item?.GameObject == null)
                return;

            SpriteRenderer[] renderers =
                item.GameObject.GetComponentsInChildren<SpriteRenderer>(true);
            Camera worldCamera = Camera.main;
            bool found = false;
            float screenMinX = float.PositiveInfinity;
            float screenMinY = float.PositiveInfinity;
            float screenMaxX = float.NegativeInfinity;
            float screenMaxY = float.NegativeInfinity;

            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || renderer.sprite == null)
                    continue;

                Bounds bounds = renderer.sprite.bounds;
                Vector3[] localCorners =
                {
                    new Vector3(bounds.min.x, bounds.min.y, 0f),
                    new Vector3(bounds.min.x, bounds.max.y, 0f),
                    new Vector3(bounds.max.x, bounds.min.y, 0f),
                    new Vector3(bounds.max.x, bounds.max.y, 0f)
                };
                for (int corner = 0; corner < localCorners.Length; corner++)
                {
                    Vector3 world = renderer.transform.TransformPoint(localCorners[corner]);
                    Vector2 screen = BartendingViewport.GetPointerScreenPosition(
                        worldCamera,
                        world);
                    screenMinX = Mathf.Min(screenMinX, screen.x);
                    screenMinY = Mathf.Min(screenMinY, screen.y);
                    screenMaxX = Mathf.Max(screenMaxX, screen.x);
                    screenMaxY = Mathf.Max(screenMaxY, screen.y);
                    found = true;
                }
            }

            if (!found)
                return;

            RectTransform slotRect = transform as RectTransform;
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera canvasCamera = canvas != null
                && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            if (slotRect == null
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    slotRect,
                    new Vector2(screenMinX, screenMinY),
                    canvasCamera,
                    out Vector2 localMin)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    slotRect,
                    new Vector2(screenMaxX, screenMaxY),
                    canvasCamera,
                    out Vector2 localMax))
            {
                return;
            }

            Vector2 projectedSize = new Vector2(
                Mathf.Abs(localMax.x - localMin.x),
                Mathf.Abs(localMax.y - localMin.y));
            if (projectedSize.x <= Mathf.Epsilon || projectedSize.y <= Mathf.Epsilon)
                return;

            contentRoot.sizeDelta = projectedSize;
        }

        private void OnDestroy()
        {
            if (boundSlot != null)
                boundSlot.OccupancyChanged -= HandleOccupancyChanged;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject instance = new GameObject(name, typeof(RectTransform));
            instance.layer = parent.gameObject.layer;
            RectTransform rect = instance.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
