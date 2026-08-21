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
            if (!TryGetInteractionLocalRect(out Rect localRect))
                return false;

            Vector2 local = transform.InverseTransformPoint(worldPoint);
            return localRect.Contains(local);
        }

        public bool TryGetInteractionLocalRect(out Rect localRect)
        {
            localRect = default;
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
            localRect = Rect.MinMaxRect(
                Mathf.Lerp(bounds.min.x, bounds.max.x, pixels.xMin / SourceSize.x),
                Mathf.Lerp(bounds.min.y, bounds.max.y, pixels.yMin / SourceSize.y),
                Mathf.Lerp(bounds.min.x, bounds.max.x, pixels.xMax / SourceSize.x),
                Mathf.Lerp(bounds.min.y, bounds.max.y, pixels.yMax / SourceSize.y));
            return true;
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
        private ShakerPartController strainerPart;
        private ShakerPartController capPart;
        private bool initialized;
        private TextMesh statusText;

        public bool IsStrainerAttached => strainerPart == null || strainerPart.IsAttached;
        public bool IsCapAttached => capPart == null || capPart.IsAttached;
        public bool IsFullyAssembled => IsStrainerAttached && IsCapAttached;
        internal Transform DetachedPartsParent => transform.parent;

        public void Configure()
        {
            shaker = GetComponent<BeakerController>();
            technique = GetComponent<CobblerShakerTechniqueController>();
            shaker?.ConfigureCollisionGeometry(
                1.5265f,
                2.053f,
                3.25f,
                -0.57f,
                new Vector2(1.765f, 3.12f),
                new Vector2(0f, -0.57f));
            layers = GetComponentsInChildren<ShakerVisualLayer>(true);
            for (int i = 0; i < layers.Length; i++)
            {
                ShakerVisualLayer layer = layers[i];
                if (layer == null)
                    continue;
                if (layer.Role == ShakerVisualRole.Strainer)
                {
                    strainerLayer = layer;
                    strainerPart = ConfigurePart(layer);
                }
                else if (layer.Role == ShakerVisualRole.Cap)
                {
                    capLayer = layer;
                    capPart = ConfigurePart(layer);
                }
            }

            if (!initialized)
            {
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
                bool capMovesWithHeldStrainer = capPart != null
                    && strainerPart != null
                    && strainerPart.IsPickedUp
                    && capPart.transform.IsChildOf(strainerPart.transform);
                if (capPart != null && capPart.IsAttached && !capMovesWithHeldStrainer)
                {
                    capPart.DetachAndPickUp(worldPoint);
                    return true;
                }
            }

            if (strainerLayer != null && strainerLayer.ContainsWorldPoint(worldPoint))
            {
                if (strainerPart == null || !strainerPart.IsAttached)
                    return false;

                strainerPart.DetachAndPickUp(worldPoint);
                if (capPart != null && capPart.IsAttached)
                    capPart.FollowAttachedCarrier(strainerPart.transform);
                return true;
            }

            return false;
        }

        internal bool TryAttachPart(ShakerPartController part)
        {
            if (part == null
                || part.Owner != this
                || part.IsAttached
                || (shaker != null && shaker.IsPickedUp))
            {
                return false;
            }
            if (part.Role == ShakerVisualRole.Cap && !IsStrainerAttached)
            {
                if (strainerPart == null
                    || strainerPart.IsPickedUp
                    || !part.IsNearCarrierPose(strainerPart))
                {
                    return false;
                }

                part.AttachToCarrier(strainerPart);
                return true;
            }

            if (!part.IsNearAttachmentPose())
                return false;

            part.AttachToShaker();
            return true;
        }

        internal void NotifyPartStateChanged(ShakerPartController part)
        {
            ApplyClosureState();
        }

        private ShakerPartController ConfigurePart(ShakerVisualLayer layer)
        {
            if (layer == null)
                return null;

            ShakerPartController part = layer.GetComponent<ShakerPartController>();
            if (part == null)
                part = layer.gameObject.AddComponent<ShakerPartController>();
            part.Configure(this, layer);
            return part;
        }

        private void ApplyClosureState()
        {
            if (technique == null)
                technique = GetComponent<CobblerShakerTechniqueController>();
            technique?.SetClosureState(IsStrainerAttached, IsCapAttached);
            RefreshPresentation();
        }

        private void RefreshPresentation()
        {
            if (!initialized)
                return;

            if (strainerLayer != null)
            {
                strainerLayer.gameObject.SetActive(true);
                strainerLayer.SetTint(IsStrainerAttached
                    ? Color.white
                    : new Color(0.88f, 0.95f, 1f, 1f));
            }

            if (capLayer != null)
            {
                capLayer.gameObject.SetActive(true);
                capLayer.SetTint(IsCapAttached
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
            if (!IsFullyAssembled)
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

        private void OnDestroy()
        {
            DestroyDetachedPart(strainerPart);
            DestroyDetachedPart(capPart);
        }

        private static void DestroyDetachedPart(ShakerPartController part)
        {
            if (part != null && !part.IsAttached)
                Destroy(part.gameObject);
        }
    }

    [DisallowMultipleComponent]
    public sealed class ShakerPartController : MonoBehaviour,
        IBartendingItem,
        IPointerAnchoredPickup,
        IBartendingViewTransitionParticipant
    {
        private CobblerShakerPresentation owner;
        private ShakerVisualLayer visualLayer;
        private ShakerVisualRole role;
        private Transform attachedParent;
        private Vector3 attachedLocalPosition;
        private Quaternion attachedLocalRotation;
        private Vector3 attachedLocalScale;
        private Rect interactionLocalRect;
        private BoxCollider2D pointerCollider;
        private Rigidbody2D body;
        private BartendingItemOrder interactionOrder;
        private SlotController currentSlot;
        private Camera inputCamera;
        private Vector3 pointerOffset;
        private bool configured;
        private bool attached = true;
        private bool pickedUp;
        private bool viewTransitionSuspended;
        private int pickupInputFrame = -1;

        public GameObject GameObject => gameObject;
        public bool IsPickedUp => pickedUp;
        public bool IsAttached => attached;
        public ShakerVisualRole Role => role;
        internal CobblerShakerPresentation Owner => owner;

        internal void Configure(
            CobblerShakerPresentation assemblyOwner,
            ShakerVisualLayer layer)
        {
            owner = assemblyOwner;
            visualLayer = layer != null ? layer : GetComponent<ShakerVisualLayer>();
            role = visualLayer != null ? visualLayer.Role : ShakerVisualRole.Body;

            if (!configured)
            {
                attachedParent = transform.parent;
                attachedLocalPosition = transform.localPosition;
                attachedLocalRotation = transform.localRotation;
                attachedLocalScale = transform.localScale;
                configured = true;
            }

            if (visualLayer == null
                || !visualLayer.TryGetInteractionLocalRect(out interactionLocalRect))
            {
                return;
            }

            EnsurePointerCollider();
            if (interactionOrder == null)
            {
                interactionOrder = BartendingItemOrder.Attach(
                    gameObject,
                    pointerCollider,
                    null,
                    ContainsAsDetachedPart);
            }

            interactionOrder.enabled = !attached;
            pointerCollider.enabled = !attached;
            if (attached)
            {
                transform.localPosition = attachedLocalPosition;
                transform.localRotation = attachedLocalRotation;
                transform.localScale = attachedLocalScale;
            }
        }

        private void Update()
        {
            if (!configured || attached || viewTransitionSuspended)
                return;

            if (pickedUp
                && TryGetPointerWorld(Input.mousePosition, out Vector3 pointerWorld))
            {
                MoveToPointer(pointerWorld);
            }

            if (!Input.GetMouseButtonDown(0) || pickupInputFrame == Time.frameCount)
                return;

            if (pickedUp)
            {
                TryDrop();
                return;
            }

            if (!TryGetPointerWorld(Input.mousePosition, out Vector3 clickWorld)
                || !ContainsAsDetachedPart(clickWorld)
                || (interactionOrder != null
                    && !interactionOrder.IsFrontmostAt(clickWorld)))
            {
                return;
            }

            PickUpAt(clickWorld);
        }

        internal void DetachAndPickUp(Vector3 pointerWorld)
        {
            if (!configured || !attached || owner == null)
                return;

            Vector3 worldPosition = transform.position;
            Quaternion worldRotation = transform.rotation;
            Vector3 worldScale = transform.lossyScale;
            Transform detachedParent = owner.DetachedPartsParent;
            transform.SetParent(detachedParent, true);
            transform.position = worldPosition;
            transform.rotation = worldRotation;
            SetWorldScale(worldScale);

            attached = false;
            EnsureDetachedPhysics();
            if (interactionOrder != null)
            {
                interactionOrder.enabled = true;
                interactionOrder.BringToFront();
            }

            owner.NotifyPartStateChanged(this);
            PickUpAt(pointerWorld);
        }

        internal bool IsNearAttachmentPose()
        {
            if (!configured || attachedParent == null)
                return false;

            Vector3 homeWorldPosition = attachedParent.TransformPoint(attachedLocalPosition);
            float worldScale = Mathf.Max(
                Mathf.Abs(attachedParent.lossyScale.x),
                Mathf.Abs(attachedParent.lossyScale.y));
            return Vector2.Distance(transform.position, homeWorldPosition)
                <= GetLocalAttachmentTolerance() * Mathf.Max(0.01f, worldScale);
        }

        internal bool IsNearCarrierPose(ShakerPartController carrier)
        {
            if (!configured || carrier == null || carrier.IsAttached)
                return false;

            float worldScale = Mathf.Max(
                Mathf.Abs(carrier.transform.lossyScale.x),
                Mathf.Abs(carrier.transform.lossyScale.y));
            return Vector2.Distance(transform.position, carrier.transform.position)
                <= GetLocalAttachmentTolerance() * Mathf.Max(0.01f, worldScale);
        }

        internal void AttachToShaker()
        {
            if (!configured || attached || attachedParent == null)
                return;

            VacateCurrentSlot();
            pickedUp = false;
            attached = true;
            if (interactionOrder != null)
                interactionOrder.enabled = false;
            DisableDetachedPhysics();

            transform.SetParent(attachedParent, false);
            transform.localPosition = attachedLocalPosition;
            transform.localRotation = attachedLocalRotation;
            transform.localScale = attachedLocalScale;
            owner?.NotifyPartStateChanged(this);
        }

        internal void AttachToCarrier(ShakerPartController carrier)
        {
            if (!configured || attached || carrier == null || carrier.IsAttached)
                return;

            VacateCurrentSlot();
            pickedUp = false;
            attached = true;
            if (interactionOrder != null)
                interactionOrder.enabled = false;
            DisableDetachedPhysics();

            // The cap and strainer layers share the same authored pivot, so a zero
            // local pose restores the original assembled alignment.
            transform.SetParent(carrier.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            owner?.NotifyPartStateChanged(this);
        }

        internal void FollowAttachedCarrier(Transform carrier)
        {
            if (!configured || !attached || carrier == null || transform.parent == carrier)
                return;

            transform.SetParent(carrier, true);
        }

        public void SnapToSlot(Transform slotTransform, SlotController slot)
        {
            if (attached || slotTransform == null)
                return;

            if (currentSlot != null && currentSlot != slot)
                currentSlot.Vacate();
            currentSlot = slot;
            if (slot != null && !ReferenceEquals(slot.OccupiedItem, this))
                slot.Occupy(this);

            EnsureDetachedPhysics();
            float bottomOffset = pointerCollider != null && pointerCollider.enabled
                ? transform.position.y - pointerCollider.bounds.min.y
                : 0f;
            MoveImmediately(new Vector3(
                slotTransform.position.x,
                slotTransform.position.y + bottomOffset,
                transform.position.z));
            pickedUp = false;
        }

        public void OnPickedUp()
        {
            if (TryGetPointerWorld(Input.mousePosition, out Vector3 pointerWorld))
                PickUpAt(pointerWorld);
        }

        public void OnPickedUpAt(Vector3 pointerWorld)
        {
            PickUpAt(pointerWorld);
        }

        public void OnDropped()
        {
            TryDrop();
        }

        public void SuspendForViewTransition()
        {
            viewTransitionSuspended = true;
        }

        public void UpdateForViewTransition(Vector3 pointerWorld)
        {
            if (viewTransitionSuspended && pickedUp && !attached)
                MoveToPointer(pointerWorld);
        }

        public void ResumeAfterViewTransition()
        {
            viewTransitionSuspended = false;
            if (!pickedUp || attached
                || !TryGetPointerWorld(Input.mousePosition, out Vector3 pointerWorld))
            {
                return;
            }

            pointerOffset = transform.position - pointerWorld;
            pointerOffset.z = 0f;
        }

        private void PickUpAt(Vector3 pointerWorld)
        {
            if (!configured || attached)
                return;

            VacateCurrentSlot();
            EnsureDetachedPhysics();
            pickedUp = true;
            pickupInputFrame = Time.frameCount;
            pointerOffset = transform.position - pointerWorld;
            pointerOffset.z = 0f;
            interactionOrder?.BringToFront();
        }

        private void TryDrop()
        {
            if (!pickedUp || attached
                || !TryGetPointerWorld(Input.mousePosition, out Vector3 pointerWorld))
            {
                return;
            }

            if (owner != null && owner.TryAttachPart(this))
                return;

            Collider2D[] hits = Physics2D.OverlapPointAll(pointerWorld);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null || hit == pointerCollider)
                    continue;

                SlotController slot = hit.GetComponent<SlotController>();
                if (slot == null || (slot.IsOccupied
                    && !ReferenceEquals(slot.OccupiedItem, this)))
                {
                    continue;
                }

                SnapToSlot(slot.transform, slot);
                return;
            }
        }

        private void MoveToPointer(Vector3 pointerWorld)
        {
            Vector3 target = pointerWorld + pointerOffset;
            target.z = transform.position.z;
            MoveImmediately(target);
        }

        private void MoveImmediately(Vector3 targetPosition)
        {
            transform.position = targetPosition;
            if (body != null)
                body.position = targetPosition;
        }

        private bool TryGetPointerWorld(Vector2 screenPosition, out Vector3 worldPosition)
        {
            if (inputCamera == null)
                inputCamera = Camera.main;
            return BartendingViewport.TryGetPointerWorldPosition(
                inputCamera,
                screenPosition,
                out worldPosition);
        }

        private bool ContainsAsDetachedPart(Vector2 worldPoint)
        {
            return !attached
                && visualLayer != null
                && visualLayer.ContainsWorldPoint(worldPoint);
        }

        private void EnsurePointerCollider()
        {
            if (pointerCollider == null)
                pointerCollider = GetComponent<BoxCollider2D>();
            if (pointerCollider == null)
                pointerCollider = gameObject.AddComponent<BoxCollider2D>();

            pointerCollider.isTrigger = true;
            pointerCollider.offset = interactionLocalRect.center;
            pointerCollider.size = interactionLocalRect.size;
        }

        private void EnsureDetachedPhysics()
        {
            EnsurePointerCollider();
            pointerCollider.enabled = true;
            if (body == null)
                body = GetComponent<Rigidbody2D>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody2D>();

            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.useFullKinematicContacts = true;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = true;
            body.position = transform.position;
        }

        private void DisableDetachedPhysics()
        {
            if (pointerCollider != null)
                pointerCollider.enabled = false;
            if (body == null)
                body = GetComponent<Rigidbody2D>();
            if (body == null)
                return;

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = false;
            Destroy(body);
            body = null;
        }

        private void VacateCurrentSlot()
        {
            if (currentSlot != null
                && ReferenceEquals(currentSlot.OccupiedItem, this))
            {
                currentSlot.Vacate();
            }
            currentSlot = null;
        }

        private void SetWorldScale(Vector3 worldScale)
        {
            Transform parent = transform.parent;
            Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(
                SafeScale(worldScale.x, parentScale.x),
                SafeScale(worldScale.y, parentScale.y),
                SafeScale(worldScale.z, parentScale.z));
        }

        private static float SafeScale(float world, float parent)
        {
            return Mathf.Abs(parent) > 0.0001f ? world / parent : world;
        }

        private float GetLocalAttachmentTolerance()
        {
            return Mathf.Max(
                0.4f,
                Mathf.Max(interactionLocalRect.width, interactionLocalRect.height) * 1.15f);
        }

        private void OnDisable()
        {
            viewTransitionSuspended = false;
            pickedUp = false;
            VacateCurrentSlot();
        }
    }
}
