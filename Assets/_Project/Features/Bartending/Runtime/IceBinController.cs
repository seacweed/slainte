using System;
using System.Collections;
using UnityEngine;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    public sealed class IceBinController : MonoBehaviour,
        IBartendingItem,
        IPointerAnchoredPickup,
        IBartendingCabinetVisualProvider
    {
        private enum BucketState
        {
            Idle,
            PickedUp,
            Tilting,
            Returning
        }

        private static Sprite fallbackSprite;
        private const float BucketSourceHeight = 590f;
        private const float BucketVisibleBottom = 3f;
        private const float BucketVisibleTop = 486f;
        private const float MaximumTiltAngle = 120f;
        private const float TiltSensitivity = 0.5f;
        private const float UprightReturnDuration = 0.8f;
        private const int PointerSyncFrameBudget = 6;

        private BusinessBartendingSettings settings;
        private Transform cubeParent;
        private Collider2D inputCollider;
        private Camera inputCamera;
        private int renderLayer;
        private float spawnedIceScale = 1f;
        private bool previewOnly;
        private ToolDef definition;
        private ToolCabinetShiftState shiftState;
        private SpriteRenderer bucketFrontRenderer;
        private Transform bucketVisualRoot;
        private int remainingIce;
        private Rigidbody2D body;
        private SlotController currentSlot;
        private BartendingItemOrder interactionOrder;
        private LayerMask slotLayer;
        private BucketState currentState;
        private Vector3 pointerOffset;
        private Vector3 bucketVisualHome;
        private float refillAccumulator;
        private bool isCharging;
        private float currentAngle;
        private float pourAccumulator;
        private float mouthLocalY;
        private float mouthHalfWidth;
        private Coroutine returnCoroutine;
        private bool pointerSyncPending;
        private int pointerSyncFramesRemaining;
        private Vector2 pointerSyncScreenPosition;
        private Vector3 pointerSyncPivotWorld;

        public static IceBinController Active { get; private set; }
        public GameObject GameObject => gameObject;
        public bool IsPickedUp => currentState != BucketState.Idle;
        public bool IsPreviewOnly => previewOnly;
        public int RemainingIce => remainingIce;
        public int MaximumIce => definition != null ? Mathf.Max(0, definition.maxCount) : 0;

        public Sprite GetCabinetVisualSprite(int layerIndex, Sprite fallback)
        {
            if (definition == null
                || definition.cabinetLayers == null
                || layerIndex != definition.cabinetLayers.Length - 1)
            {
                return fallback;
            }

            return GetBucketStateSprite() ?? fallback;
        }

        public static IceBinController Create(
            Transform parent,
            BusinessBartendingSettings settings,
            int renderLayer,
            float itemScale,
            bool previewOnly)
        {
            return CreateInternal(
                parent,
                settings,
                renderLayer,
                itemScale,
                itemScale,
                previewOnly);
        }

        private static IceBinController CreateInternal(
            Transform parent,
            BusinessBartendingSettings settings,
            int renderLayer,
            float bucketScale,
            float iceScale,
            bool previewOnly)
        {
            if (parent == null || settings == null)
                return null;

            GameObject instance = settings.iceBinPrefab != null
                ? Instantiate(settings.iceBinPrefab, parent)
                : CreateFallbackObject(parent, settings);
            instance.name = "IceBin";
            instance.transform.localPosition = settings.iceBinPosition;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * bucketScale;
            BartendingSessionBuilder.SetLayerRecursively(instance, renderLayer);

            IceBinController controller = instance.GetComponent<IceBinController>();
            if (controller == null)
                controller = instance.AddComponent<IceBinController>();
            controller.Initialize(settings, parent, renderLayer, iceScale, previewOnly);
            return controller;
        }

        public static IceBinController CreateFromDefinition(
            Transform parent,
            BusinessBartendingSettings settings,
            ToolDef definition,
            int renderLayer,
            float sessionScale,
            ToolCabinetShiftState shiftState)
        {
            IceBinController controller = CreateInternal(
                parent,
                settings,
                renderLayer,
                sessionScale,
                sessionScale,
                false);
            controller?.ConfigureDefinition(definition, shiftState);
            if (controller != null)
            {
                BartendingNativeSpriteSizer.TryMatchRootToSprite(
                    controller.transform,
                    BartendingNativeSpriteSizer.FindReferenceRenderer(
                        controller.gameObject));
                controller.transform.localScale *= Mathf.Max(
                    0.05f,
                    definition != null ? definition.worldScale : 1f);
            }
            return controller;
        }

        public bool Refill()
        {
            if (definition == null || MaximumIce <= 0)
                return false;

            remainingIce = MaximumIce;
            shiftState?.SetCount(definition.StableId, remainingIce, MaximumIce);
            RefreshBucketVisual();
            StopCharging();
            return true;
        }

        public bool ContainsWorldPoint(Vector2 worldPoint)
        {
            return inputCollider != null
                && inputCollider.enabled
                && inputCollider.OverlapPoint(worldPoint);
        }

        private void Initialize(
            BusinessBartendingSettings sessionSettings,
            Transform sessionCubeParent,
            int sessionRenderLayer,
            float sessionIceScale,
            bool isPreview)
        {
            settings = sessionSettings;
            cubeParent = sessionCubeParent;
            renderLayer = sessionRenderLayer;
            spawnedIceScale = sessionIceScale;
            previewOnly = isPreview;
            inputCollider = GetComponent<Collider2D>();
            if (inputCollider == null)
            {
                BoxCollider2D box = gameObject.AddComponent<BoxCollider2D>();
                box.size = new Vector2(settings.iceBinWidth, settings.iceBinHeight);
                box.isTrigger = true;
                inputCollider = box;
            }

            inputCamera = Camera.main;
            body = GetComponent<Rigidbody2D>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            int slotLayerIndex = LayerMask.NameToLayer("Slot");
            if (slotLayerIndex < 0)
                slotLayerIndex = 0;
            slotLayer = 1 << slotLayerIndex;

            enabled = !previewOnly;
            if (!previewOnly && Application.isPlaying)
            {
                Active = this;
                interactionOrder = BartendingItemOrder.Attach(
                    gameObject,
                    inputCollider);
            }
        }

        private void Update()
        {
            if (previewOnly || settings == null)
                return;

            if (UpdatePointerSynchronization())
                return;

            if (Input.GetMouseButtonDown(0))
            {
                if (currentState == BucketState.Idle
                    && TryGetPointerWorld(out Vector2 pointerDown)
                    && ContainsWorldPoint(pointerDown))
                {
                    PickupBucket(pointerDown);
                }
                else if (currentState == BucketState.PickedUp)
                {
                    TryDropBucket();
                }
            }

            if (currentState == BucketState.PickedUp
                || currentState == BucketState.Returning)
            {
                FollowPointer();
            }

            if (Input.GetMouseButtonDown(1)
                && currentState == BucketState.PickedUp)
            {
                StartTilting();
            }

            if (Input.GetMouseButton(1)
                && currentState == BucketState.Tilting)
            {
                PerformTilting();
            }

            if (Input.GetMouseButtonUp(1)
                && currentState == BucketState.Tilting)
            {
                StartReturning();
            }

            // A resting bucket keeps the existing one-cube pickup interaction.
            if (currentState == BucketState.Idle
                && Input.GetMouseButtonDown(1)
                && TryGetPointerWorld(out Vector2 icePointer)
                && ContainsWorldPoint(icePointer))
            {
                TryDispenseIce(icePointer);
            }

            HandlePouring();
            UpdateCharging();
        }

        public void SnapToSlot(Transform slotTransform, SlotController slot)
        {
            if (slotTransform == null)
                return;

            if (currentSlot != null && currentSlot != slot)
                currentSlot.Vacate();
            currentSlot = slot;
            if (slot != null && !ReferenceEquals(slot.OccupiedItem, this))
                slot.Occupy(this);

            Vector3 target = new Vector3(
                slotTransform.position.x,
                slotTransform.position.y + GetPivotToBottomOffset(),
                0f);
            SetPosition(target);
            SetRotationImmediately(0f);
            ReleaseBucket();
        }

        public void OnPickedUp()
        {
            Vector2 pointer = transform.position;
            TryGetPointerWorld(out pointer);
            PickupBucket(pointer);
        }

        public void OnPickedUpAt(Vector3 pointerWorld)
        {
            PickupBucket(pointerWorld);
        }

        public void OnDropped()
        {
            TryDropBucket();
        }

        private void PickupBucket(Vector2 pointerWorld)
        {
            if (!BartendingSelection.TryAcquire(this))
                return;

            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }

            CancelPointerSynchronization();

            currentState = BucketState.PickedUp;
            pointerOffset = transform.position - (Vector3)pointerWorld;
            pointerOffset.z = 0f;
            if (currentSlot != null)
            {
                currentSlot.Vacate();
                currentSlot = null;
            }
            interactionOrder?.BringToFront();
        }

        private void FollowPointer()
        {
            if (!TryGetPointerWorld(out Vector2 pointerWorld))
                return;
            SetPosition((Vector3)pointerWorld + pointerOffset);
        }

        private void TryDropBucket()
        {
            if (currentState != BucketState.PickedUp)
                return;

            if (ToolCabinetController.TryReturnHeldItem(
                    this,
                    inputCamera,
                    Input.mousePosition))
            {
                return;
            }

            if (!TryGetPointerWorld(out Vector2 pointerWorld))
                return;

            Collider2D[] hits = Physics2D.OverlapPointAll(pointerWorld, slotLayer);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null || hits[i] == inputCollider)
                    continue;
                SlotController slot = hits[i].GetComponent<SlotController>();
                if (slot == null || slot.IsOccupied)
                    continue;
                SnapToSlot(slot.transform, slot);
                return;
            }
        }

        private void ReleaseBucket()
        {
            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }
            CancelPointerSynchronization();
            currentState = BucketState.Idle;
            pointerOffset = Vector3.zero;
            pourAccumulator = 0f;
            SetRotationImmediately(0f);
            BartendingPointerAnchor.Release(this);
            BartendingSelection.Release(this);
            StopCharging();
        }

        private void SetPosition(Vector3 worldPosition)
        {
            worldPosition.z = 0f;
            if (body != null)
            {
                body.position = worldPosition;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.MovePosition(worldPosition);
            }
            transform.position = new Vector3(
                worldPosition.x,
                worldPosition.y,
                transform.position.z);
        }

        private void SetRotationImmediately(float angle)
        {
            currentAngle = angle;
            if (body != null)
            {
                body.rotation = angle;
                body.angularVelocity = 0f;
                body.MoveRotation(angle);
            }
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void StartTilting()
        {
            if (currentState != BucketState.PickedUp
                || !BartendingPointerAnchor.TryLock(this))
            {
                return;
            }

            StopCharging();
            pourAccumulator = 0f;
            currentState = BucketState.Tilting;
        }

        private void PerformTilting()
        {
            float deltaY = Input.GetAxis("Mouse Y");
            currentAngle = Mathf.Clamp(
                currentAngle + deltaY * TiltSensitivity * 10f,
                -MaximumTiltAngle,
                MaximumTiltAngle);
            SetRotationImmediately(currentAngle);
        }

        private void PerformHorizontalRotationMovement()
        {
            Bounds bounds = inputCollider != null
                ? inputCollider.bounds
                : new Bounds(transform.position, Vector3.one);
            if (!BartendingPointerAnchor.TryGetHorizontalWorldDelta(
                    inputCamera,
                    bounds,
                    settings.rotationHorizontalSensitivity,
                    settings.rotationHorizontalScreenPadding,
                    out float worldDeltaX))
            {
                return;
            }

            SetPosition(transform.position + new Vector3(worldDeltaX, 0f, 0f));
        }

        private void StartReturning()
        {
            if (currentState != BucketState.Tilting)
                return;

            currentState = BucketState.Returning;
            pourAccumulator = 0f;
            BeginPointerSynchronization(transform.position);
            if (returnCoroutine != null)
                StopCoroutine(returnCoroutine);
            returnCoroutine = StartCoroutine(ReturnToUprightRoutine());
        }

        private IEnumerator ReturnToUprightRoutine()
        {
            float startAngle = currentAngle;
            float elapsed = 0f;
            while (elapsed < UprightReturnDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / UprightReturnDuration);
                t = t * t * (3f - 2f * t);
                SetRotationImmediately(Mathf.Lerp(startAngle, 0f, t));
                yield return null;
            }

            SetRotationImmediately(0f);
            returnCoroutine = null;
            currentState = BucketState.PickedUp;
        }

        private float GetPivotToBottomOffset()
        {
            float visibleBottom = float.PositiveInfinity;
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null
                    && renderers[i].enabled
                    && renderers[i].sprite != null)
                {
                    visibleBottom = Mathf.Min(visibleBottom, renderers[i].bounds.min.y);
                }
            }
            return float.IsPositiveInfinity(visibleBottom)
                ? 0f
                : transform.position.y - visibleBottom;
        }

        private void TryDispenseIce(Vector2 pointerDown)
        {
            if (definition != null && remainingIce <= 0)
                return;

            IceCubeController cube = CreateIceCube();
            if (cube == null)
                return;

            if (!cube.BeginDrag(pointerDown, true))
            {
                Destroy(cube.gameObject);
                return;
            }

            ConsumeOneIce();
        }

        private void HandlePouring()
        {
            float startAngle = definition != null
                ? Mathf.Clamp(definition.icePourStartAngle, 1f, MaximumTiltAngle)
                : 90f;
            if (currentState != BucketState.Tilting
                || Mathf.Abs(currentAngle) < startAngle
                || (definition != null && remainingIce <= 0))
            {
                pourAccumulator = 0f;
                return;
            }

            float interval = definition != null
                ? Mathf.Max(0.05f, definition.icePourInterval)
                : 0.18f;
            pourAccumulator += Time.deltaTime;
            int emitted = 0;
            while (pourAccumulator >= interval
                && emitted < 3
                && (definition == null || remainingIce > 0))
            {
                if (!TrySpawnPouredIce())
                    break;
                pourAccumulator -= interval;
                emitted++;
            }
        }

        private bool TrySpawnPouredIce()
        {
            IceCubeController cube = CreateIceCube();
            if (cube == null)
                return false;

            float side = Mathf.Approximately(currentAngle, 0f)
                ? 0f
                : -Mathf.Sign(currentAngle);
            Transform visualFrame = bucketVisualRoot != null
                ? bucketVisualRoot
                : transform;
            Vector2 spawnPosition = visualFrame.TransformPoint(
                new Vector3(side * mouthHalfWidth, mouthLocalY, 0f));
            float exitSpeed = definition != null
                ? Mathf.Max(0f, definition.icePourExitSpeed)
                : 0.8f;
            Vector2 initialVelocity = (Vector2)transform.up * exitSpeed
                + Vector2.down * 0.15f;

            cube.ReleaseFromSource(
                spawnPosition,
                initialVelocity,
                UnityEngine.Random.Range(-90f, 90f));
            ConsumeOneIce();
            return true;
        }

        private IceCubeController CreateIceCube()
        {
            IceCubeController cube = IceCubeController.Create(
                cubeParent,
                settings,
                renderLayer,
                spawnedIceScale,
                false);
            if (cube != null)
                ApplyIceVisual(cube);
            return cube;
        }

        private void ApplyIceVisual(IceCubeController cube)
        {
            if (cube == null
                || definition == null
                || definition.iceSprites == null
                || definition.iceSprites.Length == 0)
            {
                return;
            }

            int start = UnityEngine.Random.Range(0, definition.iceSprites.Length);
            for (int i = 0; i < definition.iceSprites.Length; i++)
            {
                Sprite sprite = definition.iceSprites[(start + i) % definition.iceSprites.Length];
                if (sprite == null)
                    continue;
                cube.ApplyVisualSprite(sprite, true);
                return;
            }
        }

        private void ConsumeOneIce()
        {
            if (definition == null)
                return;

            remainingIce = Mathf.Max(0, remainingIce - 1);
            shiftState?.SetCount(
                definition.StableId,
                remainingIce,
                MaximumIce);
            RefreshBucketVisual();
        }

        private void UpdateCharging()
        {
            bool shouldCharge = currentState == BucketState.PickedUp
                && definition != null
                && remainingIce < MaximumIce
                && ToolCabinetController.IsHeldItemOverIceMaker(this, inputCamera);
            if (!shouldCharge)
            {
                StopCharging();
                return;
            }

            isCharging = true;
            refillAccumulator += Time.unscaledDeltaTime
                * Mathf.Max(0.05f, definition.iceRefillPerSecond);
            int gained = Mathf.FloorToInt(refillAccumulator);
            if (gained > 0)
            {
                refillAccumulator -= gained;
                remainingIce = Mathf.Min(MaximumIce, remainingIce + gained);
                shiftState?.SetCount(definition.StableId, remainingIce, MaximumIce);
                RefreshBucketVisual();
            }

            if (bucketVisualRoot != null)
            {
                float phase = Time.unscaledTime
                    * Mathf.Max(0f, definition.chargingShakeFrequency);
                float amplitude = Mathf.Max(0f, definition.chargingShakeAmplitude);
                bucketVisualRoot.localPosition = bucketVisualHome + new Vector3(
                    Mathf.Sin(phase) * amplitude,
                    Mathf.Sin(phase * 0.73f) * amplitude * 0.35f,
                    0f);
            }
        }

        private void StopCharging()
        {
            if (!isCharging && refillAccumulator <= 0f)
                return;
            isCharging = false;
            refillAccumulator = 0f;
            if (bucketVisualRoot != null)
                bucketVisualRoot.localPosition = bucketVisualHome;
        }

        private bool TryGetPointerWorld(out Vector2 pointer)
        {
            if (BartendingViewport.TryGetPointerWorldPosition(
                    inputCamera,
                    Input.mousePosition,
                    out Vector3 world))
            {
                pointer = world;
                return true;
            }

            pointer = default;
            return false;
        }

        private void BeginPointerSynchronization(Vector3 pivotWorld)
        {
            pointerSyncPivotWorld = pivotWorld;
            pointerOffset = Vector3.zero;
            bool requested = BartendingPointerAnchor.UnlockAndWarp(
                this,
                inputCamera,
                pivotWorld,
                out pointerSyncScreenPosition);
            if (!requested)
            {
                CompletePointerSynchronization(false);
                return;
            }

            pointerSyncFramesRemaining = PointerSyncFrameBudget;
            pointerSyncPending = true;
        }

        private bool UpdatePointerSynchronization()
        {
            if (!pointerSyncPending)
                return false;

            if (BartendingPointerAnchor.IsPointerAt(pointerSyncScreenPosition))
            {
                CompletePointerSynchronization(true);
                return true;
            }

            pointerSyncFramesRemaining--;
            if (pointerSyncFramesRemaining > 0)
            {
                BartendingPointerAnchor.TryWarpToWorld(
                    inputCamera,
                    pointerSyncPivotWorld,
                    out pointerSyncScreenPosition);
                return true;
            }

            CompletePointerSynchronization(false);
            return true;
        }

        private void CompletePointerSynchronization(bool success)
        {
            pointerSyncPending = false;
            if (!success && TryGetPointerWorld(out Vector2 pointerWorld))
            {
                pointerOffset = pointerSyncPivotWorld - (Vector3)pointerWorld;
                pointerOffset.z = 0f;
                Debug.LogWarning(
                    $"[{name}] Cursor warp was not confirmed; preserving the current grab offset.");
            }
            else if (success)
            {
                pointerOffset = Vector3.zero;
            }
        }

        private void CancelPointerSynchronization()
        {
            pointerSyncPending = false;
            pointerSyncFramesRemaining = 0;
            pointerSyncScreenPosition = Vector2.zero;
            pointerSyncPivotWorld = Vector3.zero;
        }

        private void ConfigureDefinition(
            ToolDef toolDefinition,
            ToolCabinetShiftState runtimeShiftState)
        {
            definition = toolDefinition;
            shiftState = runtimeShiftState;
            int maximum = MaximumIce;
            remainingIce = shiftState != null
                ? shiftState.GetCount(definition != null ? definition.StableId : string.Empty, maximum)
                : maximum;

            SpriteRenderer[] existing = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < existing.Length; i++)
                existing[i].enabled = false;

            if (definition == null
                || definition.worldLayers == null
                || definition.worldLayers.Length == 0)
                return;

            GameObject visualRoot = new GameObject("__ToolCabinetBucketVisualRoot");
            visualRoot.layer = renderLayer;
            visualRoot.transform.SetParent(transform, false);
            visualRoot.transform.localPosition = definition.worldVisualOffset;
            bucketVisualRoot = visualRoot.transform;
            bucketVisualHome = definition.worldVisualOffset;

            CreateBucketLayer("BucketBack", definition.worldLayers[0], 10);
            GameObject front = CreateBucketLayer("BucketFront", null, 14);
            bucketFrontRenderer = front != null ? front.GetComponent<SpriteRenderer>() : null;
            ConfigureInputCollider();
            RefreshBucketVisual();
        }

        private void ConfigureInputCollider()
        {
            if (inputCollider is not BoxCollider2D box || definition == null)
                return;

            Sprite sprite = definition.stateSprites != null
                && definition.stateSprites.Length > 6
                ? definition.stateSprites[6]
                : definition.worldLayers != null && definition.worldLayers.Length > 0
                    ? definition.worldLayers[0]
                    : null;
            if (sprite == null)
                return;

            Bounds bounds = sprite.bounds;
            float minY = Mathf.Lerp(
                bounds.min.y,
                bounds.max.y,
                BucketVisibleBottom / BucketSourceHeight);
            float maxY = Mathf.Lerp(
                bounds.min.y,
                bounds.max.y,
                BucketVisibleTop / BucketSourceHeight);
            mouthLocalY = maxY;
            mouthHalfWidth = bounds.extents.x * 0.78f;
            box.size = new Vector2(bounds.size.x, Mathf.Max(0.1f, maxY - minY));
            box.offset = new Vector2(
                bounds.center.x + definition.worldVisualOffset.x,
                (minY + maxY) * 0.5f + definition.worldVisualOffset.y);
            box.isTrigger = true;
        }

        private GameObject CreateBucketLayer(string layerName, Sprite sprite, int sortingOrder)
        {
            GameObject layer = new GameObject(layerName);
            layer.transform.SetParent(bucketVisualRoot != null ? bucketVisualRoot : transform, false);
            layer.layer = renderLayer;
            SpriteRenderer renderer = layer.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            return layer;
        }

        private void RefreshBucketVisual()
        {
            if (bucketFrontRenderer == null)
                return;

            Sprite stateSprite = GetBucketStateSprite();
            if (stateSprite != null)
                bucketFrontRenderer.sprite = stateSprite;
        }

        private Sprite GetBucketStateSprite()
        {
            if (definition == null
                || definition.stateSprites == null
                || definition.stateSprites.Length < 7)
            {
                return null;
            }

            int maximum = Mathf.Max(1, MaximumIce);
            int index = remainingIce <= 0
                ? 0
                : remainingIce >= maximum
                    ? 6
                    : Mathf.Clamp(
                        Mathf.FloorToInt(remainingIce / (float)maximum * 5f) + 1,
                        1,
                        5);
            return definition.stateSprites[index];
        }

        private void OnDisable()
        {
            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }
            CancelPointerSynchronization();
            BartendingPointerAnchor.Release(this);
            BartendingSelection.Release(this);
            StopCharging();
            if (Active == this)
                Active = null;
        }

        private static GameObject CreateFallbackObject(
            Transform parent,
            BusinessBartendingSettings settings)
        {
            GameObject root = new GameObject("IceBin");
            root.transform.SetParent(parent, false);

            GameObject visual = new GameObject("BinVisual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(
                settings.iceBinWidth,
                settings.iceBinHeight,
                1f);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = GetFallbackSprite();
            renderer.color = new Color(0.1f, 0.35f, 0.45f, 0.9f);
            renderer.sortingOrder = 14;

            BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(settings.iceBinWidth, settings.iceBinHeight);
            collider.isTrigger = true;
            return root;
        }

        internal static Sprite GetFallbackSprite()
        {
            if (fallbackSprite == null)
            {
                Texture2D texture = Texture2D.whiteTexture;
                fallbackSprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    texture.width);
                fallbackSprite.name = "BartendingFallbackSquare";
            }

            return fallbackSprite;
        }
    }
}
