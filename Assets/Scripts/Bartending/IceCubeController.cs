using UnityEngine;

namespace Slainte.Bartending
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class IceCubeController : MonoBehaviour
    {
        private static readonly System.Collections.Generic.HashSet<IceCubeController> activeCubes = new();
        private Rigidbody2D body;
        private Collider2D cubeCollider;
        private Camera inputCamera;
        private BartendingItemOrder interactionOrder;
        private SpriteRenderer[] visualRenderers;
        private int[] freeSortingOrders;
        private VesselLiquidTracker vesselOwner;
        private Vector2 dragOffset;
        private float configuredGravityScale = 1f;
        private float cleanupY = -8f;
        private bool colliderWasTrigger;
        private bool isDragging;

        public VesselLiquidTracker VesselOwner => vesselOwner;
        public bool IsDragging => isDragging;
        internal Vector2 PhysicsPosition => body != null
            ? body.position
            : (Vector2)transform.position;
        internal Collider2D PhysicsCollider
        {
            get
            {
                EnsureComponents();
                return cubeCollider;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            activeCubes.Clear();
        }

        public static IceCubeController Create(
            Transform parent,
            BusinessBartendingSettings settings,
            int renderLayer,
            float itemScale,
            bool previewOnly)
        {
            if (parent == null || settings == null)
                return null;

            GameObject instance = settings.iceCubePrefab != null
                ? Instantiate(settings.iceCubePrefab, parent)
                : CreateFallbackObject(parent);
            instance.name = previewOnly ? "IceCubePreview" : "IceCube";
            instance.transform.localRotation = Quaternion.identity;
            // Ice prefabs are authored at one world unit so the shared setting remains
            // the single source of truth for both prefab and fallback cube sizes.
            instance.transform.localScale = Vector3.one * settings.iceCubeSize * itemScale;
            BartendingSessionBuilder.SetLayerRecursively(instance, renderLayer);

            IceCubeController controller = instance.GetComponent<IceCubeController>();
            if (controller == null)
                controller = instance.AddComponent<IceCubeController>();
            controller.Initialize(settings.iceGravityScale, settings.iceCleanupY, previewOnly);
            return controller;
        }

        public void BeginDrag(Vector2 pointerWorld, bool centerOnPointer = false)
        {
            EnsureComponents();
            ReleaseVesselOwner(vesselOwner);
            isDragging = true;
            interactionOrder?.BringToFront();
            dragOffset = centerOnPointer
                ? Vector2.zero
                : PhysicsPosition - pointerWorld;
            colliderWasTrigger = cubeCollider != null && cubeCollider.isTrigger;
            if (cubeCollider != null)
                cubeCollider.isTrigger = true;
            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Kinematic;
                body.gravityScale = 0f;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.position = pointerWorld + dragOffset;
            }
            else
            {
                transform.position = pointerWorld + dragOffset;
            }
        }

        public void ApplyVisualSprite(
            Sprite sprite,
            bool matchNativeCanvasPixelSize = false)
        {
            if (sprite == null)
                return;

            EnsureComponents();
            SpriteRenderer renderer = GetComponentInChildren<SpriteRenderer>(true);
            if (renderer == null)
            {
                renderer = gameObject.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = 18;
            }
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.enabled = true;
            if (matchNativeCanvasPixelSize)
            {
                BartendingNativeSpriteSizer.TryMatchRootToCanvasPixels(
                    transform,
                    renderer,
                    sprite.rect.size);
                transform.localScale *= 0.7f;
                MatchBoxColliderToRenderer(renderer);
            }
            CacheSortingOrders();
        }

        private void MatchBoxColliderToRenderer(SpriteRenderer renderer)
        {
            if (renderer == null
                || renderer.sprite == null
                || cubeCollider is not BoxCollider2D box)
            {
                return;
            }

            Bounds spriteBounds = renderer.sprite.bounds;
            Vector3 localMin = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                0f);
            Vector3 localMax = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                0f);
            Vector3[] corners =
            {
                new Vector3(spriteBounds.min.x, spriteBounds.min.y, 0f),
                new Vector3(spriteBounds.min.x, spriteBounds.max.y, 0f),
                new Vector3(spriteBounds.max.x, spriteBounds.min.y, 0f),
                new Vector3(spriteBounds.max.x, spriteBounds.max.y, 0f)
            };

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 rootLocal = transform.InverseTransformPoint(
                    renderer.transform.TransformPoint(corners[i]));
                localMin = Vector3.Min(localMin, rootLocal);
                localMax = Vector3.Max(localMax, rootLocal);
            }

            box.offset = (localMin + localMax) * 0.5f;
            box.size = new Vector2(
                Mathf.Abs(localMax.x - localMin.x),
                Mathf.Abs(localMax.y - localMin.y));
        }

        public void ReleaseFromSource(
            Vector2 worldPosition,
            Vector2 initialVelocity,
            float initialAngularVelocity = 0f)
        {
            EnsureComponents();
            ReleaseVesselOwner(vesselOwner);
            isDragging = false;
            dragOffset = Vector2.zero;
            colliderWasTrigger = false;
            if (cubeCollider != null)
                cubeCollider.isTrigger = false;

            transform.position = new Vector3(
                worldPosition.x,
                worldPosition.y,
                transform.position.z);
            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Dynamic;
                body.gravityScale = configuredGravityScale;
                body.position = worldPosition;
                body.linearVelocity = initialVelocity;
                body.angularVelocity = initialAngularVelocity;
                body.WakeUp();
            }
        }

        internal bool TryAssignVesselOwner(VesselLiquidTracker owner)
        {
            if (owner == null || isDragging)
                return false;
            if (vesselOwner == owner)
                return true;
            if (vesselOwner != null)
                return false;

            vesselOwner = owner;
            owner.RegisterOwnedIceCube(this);
            SetContainedSortingOrder();
            VesselLiquidTracker.RefreshIceIsolation(this);
            return true;
        }

        internal void ReleaseVesselOwner(VesselLiquidTracker expectedOwner)
        {
            if (vesselOwner == null || (expectedOwner != null && vesselOwner != expectedOwner))
                return;

            VesselLiquidTracker previous = vesselOwner;
            vesselOwner = null;
            previous.UnregisterOwnedIceCube(this);
            RestoreFreeSortingOrder();
            VesselLiquidTracker.RefreshIceIsolation(this);
        }

        internal void Translate(Vector2 delta)
        {
            if (isDragging || delta.sqrMagnitude <= 0.000001f)
                return;

            EnsureComponents();
            if (body != null)
            {
                Vector2 targetPosition = body.position + delta;
                body.position = targetPosition;
                transform.position = new Vector3(
                    targetPosition.x,
                    targetPosition.y,
                    transform.position.z);
                if (body.simulated)
                    body.WakeUp();
            }
            else
            {
                transform.position += (Vector3)delta;
            }
        }

        private void Initialize(float gravityScale, float destroyBelowY, bool previewOnly)
        {
            configuredGravityScale = Mathf.Max(0f, gravityScale);
            cleanupY = destroyBelowY;
            EnsureComponents();
            if (body != null)
            {
                body.gravityScale = configuredGravityScale;
                body.simulated = !previewOnly;
            }

            enabled = !previewOnly;
            if (!previewOnly)
                VesselLiquidTracker.RegisterIceCube(this);
        }

        private void Awake()
        {
            EnsureComponents();
            CacheSortingOrders();
        }

        private void CacheSortingOrders()
        {
            visualRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            freeSortingOrders = new int[visualRenderers.Length];
            for (int i = 0; i < visualRenderers.Length; i++)
                freeSortingOrders[i] = visualRenderers[i].sortingOrder;
        }

        private void SetContainedSortingOrder()
        {
            if (visualRenderers == null || freeSortingOrders == null)
                CacheSortingOrders();
            for (int i = 0; i < visualRenderers.Length; i++)
                visualRenderers[i].sortingOrder = Mathf.Min(freeSortingOrders[i], 13);
        }

        private void RestoreFreeSortingOrder()
        {
            if (visualRenderers == null || freeSortingOrders == null)
                return;
            for (int i = 0; i < visualRenderers.Length && i < freeSortingOrders.Length; i++)
                visualRenderers[i].sortingOrder = freeSortingOrders[i];
        }

        private void OnEnable()
        {
            activeCubes.Add(this);
            if (Application.isPlaying && enabled)
                VesselLiquidTracker.RegisterIceCube(this);
        }

        private void OnDisable()
        {
            activeCubes.Remove(this);
            ReleaseVesselOwner(vesselOwner);
            VesselLiquidTracker.UnregisterIceCube(this);
        }

        private void Update()
        {
            if (isDragging)
            {
                UpdateDrag();
                return;
            }

            if (PhysicsPosition.y < cleanupY)
            {
                Destroy(gameObject);
                return;
            }

            if (Input.GetMouseButtonDown(0)
                && TryGetPointerWorld(out Vector2 pointer)
                && FindFrontmostIceAt(pointer) == this)
            {
                BeginDrag(pointer);
            }
        }

        private void UpdateDrag()
        {
            if (TryGetPointerWorld(out Vector2 pointer))
            {
                Vector2 target = pointer + dragOffset;
                if (body != null)
                    body.position = target;
                else
                    transform.position = target;
            }

            if (Input.GetMouseButtonUp(0))
                EndDrag();
        }

        private void EndDrag()
        {
            isDragging = false;
            if (IceBinController.Active != null
                && IceBinController.Active.ContainsWorldPoint(PhysicsPosition))
            {
                Destroy(gameObject);
                return;
            }

            if (cubeCollider != null)
                cubeCollider.isTrigger = colliderWasTrigger;
            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Dynamic;
                body.gravityScale = configuredGravityScale;
                body.WakeUp();
            }
        }

        private static IceCubeController FindFrontmostIceAt(Vector2 pointer)
        {
            IceCubeController frontmost = null;
            int bestRank = int.MinValue;
            foreach (IceCubeController cube in activeCubes)
            {
                if (cube == null
                    || !cube.isActiveAndEnabled
                    || cube.isDragging
                    || cube.cubeCollider == null
                    || !cube.cubeCollider.OverlapPoint(pointer))
                {
                    continue;
                }

                int rank = cube.interactionOrder != null ? cube.interactionOrder.Rank : 0;
                if (frontmost == null
                    || rank > bestRank
                    || (rank == bestRank && cube.GetInstanceID() > frontmost.GetInstanceID()))
                {
                    frontmost = cube;
                    bestRank = rank;
                }
            }

            return frontmost;
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

        private void EnsureComponents()
        {
            body ??= GetComponent<Rigidbody2D>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody2D>();
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            cubeCollider ??= GetComponent<Collider2D>();
            if (cubeCollider == null)
                cubeCollider = gameObject.AddComponent<BoxCollider2D>();

            inputCamera ??= Camera.main;
            interactionOrder ??= BartendingItemOrder.Attach(gameObject, cubeCollider);
        }

        private static GameObject CreateFallbackObject(Transform parent)
        {
            GameObject cube = new GameObject("IceCube");
            cube.transform.SetParent(parent, false);

            SpriteRenderer renderer = cube.AddComponent<SpriteRenderer>();
            renderer.sprite = IceBinController.GetFallbackSprite();
            renderer.color = new Color(0.68f, 0.93f, 1f, 0.9f);
            renderer.sortingOrder = 18;

            BoxCollider2D collider = cube.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one * 0.9f;
            Rigidbody2D rigidbody = cube.AddComponent<Rigidbody2D>();
            rigidbody.mass = 0.25f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
            return cube;
        }
    }
}
