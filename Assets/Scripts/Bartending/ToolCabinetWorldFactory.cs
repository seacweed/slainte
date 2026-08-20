using UnityEngine;

namespace Slainte.Bartending
{
    public static class ToolCabinetWorldFactory
    {
        private static readonly int[] JiggerOrders = { 10, 13 };
        private static readonly int[] ShakerOrders = { 10, 11, 14, 15, 16, 17 };

        public static IBartendingItem CreateTool(
            ToolDef definition,
            Transform parent,
            BusinessBartendingSettings settings,
            int renderLayer,
            float sessionScale,
            ToolCabinetShiftState shiftState,
            out GameObject instance)
        {
            instance = null;
            if (definition == null || parent == null || settings == null)
                return null;

            switch (definition.kind)
            {
                case ToolKind.Jigger:
                case ToolKind.CobblerShaker:
                {
                    IBartendingItem item = BartendingSessionBuilder.CreateItem(
                        definition.worldPrefab,
                        parent,
                        definition.displayName,
                        Vector3.zero,
                        renderLayer,
                        sessionScale * Mathf.Max(0.05f, definition.worldScale));
                    instance = item?.GameObject;
                    if (item == null || instance == null)
                        return null;

                    ConfigureLayeredVisuals(
                        instance,
                        definition.worldLayers,
                        definition.kind == ToolKind.Jigger ? JiggerOrders : ShakerOrders,
                        renderLayer,
                        definition.kind == ToolKind.CobblerShaker,
                        definition.worldVisualOffset);

                    if (definition.kind == ToolKind.Jigger)
                    {
                        JiggerMeasureController jigger =
                            instance.GetComponent<JiggerMeasureController>();
                        if (jigger == null)
                            jigger = instance.AddComponent<JiggerMeasureController>();
                        jigger.Configure(
                            definition.primaryCapacityMl,
                            definition.secondaryCapacityMl);
                    }
                    else
                    {
                        CobblerShakerPresentation presentation =
                            instance.GetComponent<CobblerShakerPresentation>();
                        if (presentation == null)
                            presentation = instance.AddComponent<CobblerShakerPresentation>();
                        presentation.Configure();
                    }

                    return item;
                }

                case ToolKind.BarSpoon:
                {
                    instance = CreateBarSpoon(
                        definition,
                        parent,
                        renderLayer,
                        sessionScale);
                    return instance != null
                        ? instance.GetComponent<StirringRodController>()
                        : null;
                }

                case ToolKind.IceBucket:
                {
                    IceBinController bucket = IceBinController.CreateFromDefinition(
                        parent,
                        settings,
                        definition,
                        renderLayer,
                        sessionScale,
                        shiftState);
                    instance = bucket != null ? bucket.gameObject : null;
                    return bucket;
                }

                default:
                    return null;
            }
        }

        public static GlassController CreateGlass(
            GlassDef definition,
            Transform parent,
            BusinessBartendingSettings settings,
            int renderLayer,
            float sessionScale,
            out GameObject instance)
        {
            instance = null;
            if (definition == null || parent == null || settings == null)
                return null;

            IBartendingItem item = BartendingSessionBuilder.CreateItem(
                definition.worldPrefab,
                parent,
                definition.displayName,
                Vector3.zero,
                renderLayer,
                sessionScale * Mathf.Max(0.05f, definition.worldScale));
            GlassController glass = item as GlassController;
            instance = glass != null ? glass.gameObject : null;
            if (glass == null || definition.worldSprite == null)
                return glass;

            glass.ConfigureServingIdentity(definition.glassId, definition.capacityMl);

            DisableExistingVisuals(glass.gameObject);
            GameObject visualObject = new GameObject("__ToolCabinetGlassVisual");
            visualObject.transform.SetParent(glass.transform, false);
            visualObject.layer = renderLayer;
            SpriteRenderer renderer = visualObject.AddComponent<SpriteRenderer>();
            renderer.sprite = definition.worldSprite;
            renderer.sortingOrder = 13;

            if (GlassCollisionProfiles.TryGetByGlassId(
                    definition.glassId,
                    out GlassCollisionProfileDefinition profile))
            {
                glass.ApplyCollisionProfile(profile, renderer);
            }

            return glass;
        }

        private static GameObject CreateBarSpoon(
            ToolDef definition,
            Transform parent,
            int renderLayer,
            float sessionScale)
        {
            if (definition.worldLayers == null || definition.worldLayers.Length == 0)
                return null;

            GameObject root = new GameObject(definition.displayName);
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one
                * sessionScale
                * Mathf.Max(0.05f, definition.worldScale);
            root.layer = renderLayer;

            SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = definition.worldLayers[0];
            renderer.sortingOrder = 18;
            renderer.flipY = true;

            CapsuleCollider2D collider = root.AddComponent<CapsuleCollider2D>();
            Bounds bounds = renderer.sprite.bounds;
            collider.direction = CapsuleDirection2D.Vertical;
            collider.size = new Vector2(
                Mathf.Max(0.15f, bounds.size.x * 0.35f),
                Mathf.Max(0.5f, bounds.size.y * 0.9f));
            collider.offset = bounds.center;
            collider.isTrigger = true;

            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            root.AddComponent<StirringRodController>();
            return root;
        }

        private static void ConfigureLayeredVisuals(
            GameObject root,
            Sprite[] sprites,
            int[] sortingOrders,
            int renderLayer,
            bool markShakerLayers,
            Vector2 visualOffset)
        {
            DisableExistingVisuals(root);
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite == null)
                    continue;

                GameObject layerObject = new GameObject("__ToolCabinetLayer_" + i);
                layerObject.transform.SetParent(root.transform, false);
                layerObject.transform.localPosition = visualOffset;
                layerObject.layer = renderLayer;
                SpriteRenderer renderer = layerObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = i < sortingOrders.Length
                    ? sortingOrders[i]
                    : 10 + i;
                if (markShakerLayers && (i == 0 || i == 2))
                    renderer.color = new Color(1f, 1f, 1f, 0.65f);

                if (markShakerLayers)
                {
                    ShakerVisualLayer marker = layerObject.AddComponent<ShakerVisualLayer>();
                    marker.Configure(
                        i == 4
                            ? ShakerVisualRole.Strainer
                            : i == 5
                                ? ShakerVisualRole.Cap
                                : ShakerVisualRole.Body);
                }
            }
        }

        private static void DisableExistingVisuals(GameObject root)
        {
            SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = false;
        }
    }

    [DisallowMultipleComponent]
    public sealed class JiggerMeasureController : MonoBehaviour
    {
        private float smallCapacityMl = 30f;
        private float largeCapacityMl = 45f;
        private bool useLargeSide = true;
        private Collider2D pointerCollider;
        private BeakerController beaker;
        private VesselLiquidTracker tracker;
        private BoxCollider2D capacityStop;
        private bool capacityStopClosed;

        public float ActiveCapacityMl => useLargeSide ? largeCapacityMl : smallCapacityMl;

        public void Configure(float smallMl, float largeMl)
        {
            smallCapacityMl = Mathf.Max(1f, smallMl);
            largeCapacityMl = Mathf.Max(smallCapacityMl, largeMl);
            RefreshName();
        }

        private void Start()
        {
            pointerCollider = GetComponent<Collider2D>();
            beaker = GetComponent<BeakerController>();
            tracker = beaker != null ? beaker.LiquidTracker : GetComponent<VesselLiquidTracker>();
            CreateCapacityStop();
            RefreshName();
        }

        private void Update()
        {
            RefreshCapacityStop();
            if (!Input.GetMouseButtonDown(1)
                || pointerCollider == null
                || !BartendingViewport.TryGetPointerWorldPosition(
                    Camera.main,
                    Input.mousePosition,
                    out Vector3 world)
                || !pointerCollider.OverlapPoint(world))
            {
                return;
            }

            if (beaker != null && beaker.IsPickedUp)
                return;
            if (tracker != null && tracker.BuildComposition().TotalVolumeMl > 0.05f)
                return;

            useLargeSide = !useLargeSide;
            RefreshName();
        }

        private void CreateCapacityStop()
        {
            if (beaker == null || transform.Find("__JiggerCapacityStop") != null)
                return;

            GameObject stop = new GameObject("__JiggerCapacityStop");
            stop.layer = gameObject.layer;
            stop.transform.SetParent(transform, false);
            stop.transform.localPosition = new Vector3(
                0f,
                beaker.colliderYOffset + beaker.height * 0.5f - 0.03f,
                0f);
            capacityStop = stop.AddComponent<BoxCollider2D>();
            capacityStop.isTrigger = false;
            capacityStop.size = new Vector2(Mathf.Max(0.1f, beaker.topWidth * 0.92f), 0.06f);
            stop.SetActive(false);
            tracker?.RefreshCollisionGeometry();
        }

        private void RefreshCapacityStop()
        {
            if (tracker == null)
                tracker = GetComponent<VesselLiquidTracker>();
            if (capacityStop == null)
            {
                CreateCapacityStop();
                if (capacityStop == null)
                    return;
            }

            float volumeMl = tracker != null
                ? tracker.BuildComposition().TotalVolumeMl
                : 0f;
            bool shouldClose = (beaker == null || !beaker.IsPickedUp)
                && volumeMl >= ActiveCapacityMl - 0.5f;
            if (shouldClose == capacityStopClosed
                && capacityStop.gameObject.activeSelf == shouldClose)
            {
                return;
            }

            capacityStopClosed = shouldClose;
            capacityStop.gameObject.SetActive(shouldClose);
            tracker?.RefreshCollisionGeometry();
        }

        private void RefreshName()
        {
            gameObject.name = "Jigger_" + ActiveCapacityMl.ToString("0") + "ml";
        }
    }

    public enum ShakerVisualRole
    {
        Body,
        Strainer,
        Cap
    }

    [DisallowMultipleComponent]
    public sealed class ShakerVisualLayer : MonoBehaviour
    {
        private static readonly Vector2 SourceSize = new(310f, 590f);
        private SpriteRenderer spriteRenderer;

        public ShakerVisualRole Role { get; private set; }

        public void Configure(ShakerVisualRole role)
        {
            Role = role;
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        public bool ContainsWorldPoint(Vector2 worldPoint)
        {
            if (Role == ShakerVisualRole.Body)
                return false;
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null || spriteRenderer.sprite == null)
                return false;

            Rect pixels = Role == ShakerVisualRole.Strainer
                ? new Rect(53f, 271f, 203f, 160f)
                : new Rect(105f, 370f, 99f, 78f);
            Bounds bounds = spriteRenderer.sprite.bounds;
            Rect localRect = Rect.MinMaxRect(
                Mathf.Lerp(bounds.min.x, bounds.max.x, pixels.xMin / SourceSize.x),
                Mathf.Lerp(bounds.min.y, bounds.max.y, pixels.yMin / SourceSize.y),
                Mathf.Lerp(bounds.min.x, bounds.max.x, pixels.xMax / SourceSize.x),
                Mathf.Lerp(bounds.min.y, bounds.max.y, pixels.yMax / SourceSize.y));
            Vector2 local = transform.InverseTransformPoint(worldPoint);
            return localRect.Contains(local);
        }

        public void SetTint(Color color)
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
                spriteRenderer.color = color;
        }
    }

    [DisallowMultipleComponent]
    public sealed class CobblerShakerPresentation : MonoBehaviour
    {
        private BeakerController shaker;
        private CobblerShakerTechniqueController technique;
        private ShakerVisualLayer[] layers;
        private ShakerVisualLayer strainerLayer;
        private ShakerVisualLayer capLayer;
        private Vector3 strainerHome;
        private Vector3 capHome;
        private bool initialized;
        private bool strainerAttached = true;
        private bool capAttached = true;
        private TextMesh statusText;

        public void Configure()
        {
            shaker = GetComponent<BeakerController>();
            technique = GetComponent<CobblerShakerTechniqueController>();
            layers = GetComponentsInChildren<ShakerVisualLayer>(true);
            for (int i = 0; i < layers.Length; i++)
            {
                ShakerVisualLayer layer = layers[i];
                if (layer == null)
                    continue;
                if (layer.Role == ShakerVisualRole.Strainer)
                    strainerLayer = layer;
                else if (layer.Role == ShakerVisualRole.Cap)
                    capLayer = layer;
            }

            if (!initialized)
            {
                strainerAttached = true;
                capAttached = true;
                if (strainerLayer != null)
                    strainerHome = strainerLayer.transform.localPosition;
                if (capLayer != null)
                    capHome = capLayer.transform.localPosition;
                initialized = true;
            }

            EnsureStatusText();
            ApplyClosureState();
        }

        private void Start()
        {
            Configure();
        }

        private void LateUpdate()
        {
            RefreshPresentation();
        }

        public bool TryHandlePartClick(Camera inputCamera, Vector2 screenPosition)
        {
            if (shaker == null)
                shaker = GetComponent<BeakerController>();
            if (shaker == null || shaker.IsPickedUp)
                return false;
            if (!BartendingViewport.TryGetPointerWorldPosition(
                    inputCamera,
                    screenPosition,
                    out Vector3 worldPoint))
            {
                return false;
            }

            if (capLayer != null && capLayer.ContainsWorldPoint(worldPoint))
            {
                if (capAttached)
                    capAttached = false;
                else if (strainerAttached)
                    capAttached = true;
                ApplyClosureState();
                return true;
            }

            if (strainerLayer != null && strainerLayer.ContainsWorldPoint(worldPoint))
            {
                if (strainerAttached)
                {
                    strainerAttached = false;
                    capAttached = false;
                }
                else
                {
                    strainerAttached = true;
                }
                ApplyClosureState();
                return true;
            }

            return false;
        }

        private void ApplyClosureState()
        {
            if (!strainerAttached)
                capAttached = false;
            if (technique == null)
                technique = GetComponent<CobblerShakerTechniqueController>();
            technique?.SetClosureState(strainerAttached, capAttached);
            RefreshPresentation();
        }

        private void RefreshPresentation()
        {
            if (!initialized)
                return;

            if (strainerLayer != null)
            {
                strainerLayer.gameObject.SetActive(true);
                strainerLayer.transform.localPosition = strainerAttached
                    ? strainerHome
                    : strainerHome + new Vector3(-2.25f, -0.25f, 0f);
                strainerLayer.SetTint(strainerAttached
                    ? Color.white
                    : new Color(0.88f, 0.95f, 1f, 1f));
            }

            if (capLayer != null)
            {
                capLayer.gameObject.SetActive(true);
                capLayer.transform.localPosition = capAttached
                    ? capHome
                    : capHome + new Vector3(2.1f, -0.15f, 0f);
                capLayer.SetTint(capAttached
                    ? Color.white
                    : new Color(0.88f, 0.95f, 1f, 1f));
            }

            RefreshStatusText();
        }

        private void EnsureStatusText()
        {
            Transform existing = transform.Find("__ShakerStatusText");
            if (existing != null)
            {
                statusText = existing.GetComponent<TextMesh>();
                return;
            }

            GameObject label = new GameObject("__ShakerStatusText");
            label.transform.SetParent(transform, false);
            label.transform.localPosition = new Vector3(0f, 3.7f, 0f);
            label.transform.localScale = Vector3.one;
            label.layer = gameObject.layer;
            statusText = label.AddComponent<TextMesh>();
            statusText.anchor = TextAnchor.MiddleCenter;
            statusText.alignment = TextAlignment.Center;
            statusText.fontSize = 32;
            statusText.characterSize = 0.08f;
            statusText.color = new Color(1f, 0.84f, 0.15f, 1f);
            MeshRenderer renderer = statusText.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sortingOrder = 30;
        }

        private void RefreshStatusText()
        {
            if (statusText == null || shaker == null)
                return;
            if (!shaker.IsPickedUp)
            {
                statusText.text = string.Empty;
                return;
            }
            if (!strainerAttached || !capAttached)
                statusText.text = "CLOSE LIDS";
            else if (technique == null || !technique.HasLiquid)
                statusText.text = "ADD LIQUID";
            else if (!technique.HasRequiredIce)
                statusText.text = "ADD ICE";
            else if (technique.IsShakeComplete)
                statusText.text = "SHAKE OK";
            else
                statusText.text = "SHAKE "
                    + Mathf.RoundToInt(technique.ShakeProgress * 100f) + "%";

        }
    }
}
