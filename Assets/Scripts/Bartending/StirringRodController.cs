using UnityEngine;
using UnityEngine.InputSystem;

namespace Slainte.Bartending
{
    public enum StirringRodState
    {
        Idle,
        PickedUp,
        Rotating
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class StirringRodController : MonoBehaviour, IBartendingItem
    {
        [Header("Interaction")]
        [SerializeField] private float maxRotationAngle = 180f;
        [SerializeField] private float rotationSensitivity = 0.5f;
        [SerializeField] private LayerMask slotLayer;
        [SerializeField] private bool allowDropAnywhere = true;
        [SerializeField] private int pickupSortingOrder = 100;

        [Header("Stirring")]
        [SerializeField, Range(0f, 1f)] private float velocityBlend = 0.35f;
        [SerializeField] private float linearVelocityScale = 0.45f;
        [SerializeField] private float angularVelocityScale = 0.015f;
        [SerializeField] private float maxInjectedSpeed = 2.5f;
        [SerializeField] private float stirMinimumSpeed = 0.04f;
        [SerializeField] private float stirMinimumAngularSpeed = 5f;

        [Header("Generated Visual Fallback")]
        [SerializeField] private float generatedVisualLength = 3f;
        [SerializeField] private float generatedVisualWidth = 0.08f;
        [SerializeField] private Color generatedVisualColor = new Color(0.7f, 0.48f, 0.28f, 1f);

        private StirringRodState currentState = StirringRodState.Idle;
        private Collider2D mainCollider;
        private Rigidbody2D rb;
        private Camera mainCamera;
        private SlotController currentSlot;
        private SpriteRenderer[] spriteRenderers;
        private int[] originalSortingOrders;
        private LineRenderer generatedVisual;
        private float currentAngle;
        private Vector2 previousPosition;
        private Vector2 rodVelocity;
        private float previousAngle;
        private float rodAngularVelocity;

        public GameObject GameObject => gameObject;
        public bool IsPickedUp => currentState == StirringRodState.PickedUp || currentState == StirringRodState.Rotating;

        private void Start()
        {
            mainCamera = Camera.main;
            mainCollider = GetComponent<Collider2D>();
            EnsureRigidbody();
            EnsureCollider();
            EnsureVisualFallback();
            CacheSortingOrders();

            currentAngle = NormalizeAngle(transform.eulerAngles.z);
            previousPosition = rb != null ? rb.position : (Vector2)transform.position;
            previousAngle = currentAngle;

            if (slotLayer.value == 0)
            {
                int layerIndex = LayerMask.NameToLayer("Slot");
                if (layerIndex >= 0)
                    slotLayer = 1 << layerIndex;
            }
        }

        private void OnDisable()
        {
            if (currentState == StirringRodState.Rotating)
                UnlockCursor();
        }

        private void Update()
        {
            HandleInput();
        }

        private void FixedUpdate()
        {
            Vector2 position = rb != null ? rb.position : (Vector2)transform.position;
            rodVelocity = (position - previousPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            previousPosition = position;

            float angle = NormalizeAngle(rb != null ? rb.rotation : transform.eulerAngles.z);
            rodAngularVelocity = Mathf.DeltaAngle(previousAngle, angle) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            previousAngle = angle;
        }

        private void HandleInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (currentState == StirringRodState.Idle && IsMouseOverRod())
                {
                    PickupRod();
                }
                else if (currentState == StirringRodState.PickedUp)
                {
                    TryDropRod();
                }
            }

            if (currentState == StirringRodState.PickedUp)
                FollowMousePosition();

            if (Input.GetMouseButtonDown(1) && currentState == StirringRodState.PickedUp)
                StartRotating();

            if (Input.GetMouseButton(1) && currentState == StirringRodState.Rotating)
                PerformRotation();

            if (Input.GetMouseButtonUp(1) && currentState == StirringRodState.Rotating)
                StopRotating();
        }

        private void PickupRod()
        {
            currentState = StirringRodState.PickedUp;
            currentSlot?.Vacate();
            currentSlot = null;
            SetPickedSortingOrder();
        }

        private void TryDropRod()
        {
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePosition))
                return;

            Collider2D[] hits = Physics2D.OverlapPointAll(mousePosition, slotLayer);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null || hit == mainCollider)
                    continue;

                SlotController slot = hit.GetComponent<SlotController>();
                if (slot == null || slot.IsOccupied)
                    continue;

                currentSlot = slot;
                slot.Occupy(this);

                float bottomOffset = GetBottomOffset();
                SetPosition(new Vector3(hit.transform.position.x, hit.transform.position.y + bottomOffset, 0f));
                ReleaseRod();
                return;
            }

            if (allowDropAnywhere)
                ReleaseRod();
        }

        public void SnapToSlot(Transform slotTransform, SlotController slot)
        {
            if (slotTransform == null)
                return;

            currentSlot = slot;
            float bottomOffset = GetBottomOffset();
            SetPosition(new Vector3(slotTransform.position.x, slotTransform.position.y + bottomOffset, 0f));
            ReleaseRod();
        }

        public void OnPickedUp()
        {
            PickupRod();
        }

        public void OnDropped()
        {
            TryDropRod();
        }

        private void ReleaseRod()
        {
            currentState = StirringRodState.Idle;
            RestoreSortingOrder();
        }

        private void FollowMousePosition()
        {
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePosition))
                return;

            SetPosition(mousePosition);
        }

        private void StartRotating()
        {
            currentState = StirringRodState.Rotating;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void PerformRotation()
        {
            float deltaY = Input.GetAxis("Mouse Y");
            currentAngle += deltaY * rotationSensitivity * 10f;
            currentAngle = Mathf.Clamp(currentAngle, -maxRotationAngle, maxRotationAngle);
            SetRotation(currentAngle);
        }

        private void StopRotating()
        {
            UnlockCursor();

            if (Mouse.current != null && mainCamera != null)
            {
                Vector2 screenPosition = BartendingViewport.GetPointerScreenPosition(mainCamera, transform.position);
                Mouse.current.WarpCursorPosition(screenPosition);
            }

            currentState = StirringRodState.PickedUp;
        }

        private void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void SetPosition(Vector3 position)
        {
            position.z = 0f;
            if (rb != null)
                rb.MovePosition(position);
            else
                transform.position = position;
        }

        private void SetRotation(float angle)
        {
            if (rb != null)
                rb.MoveRotation(angle);
            else
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        private bool IsMouseOverRod()
        {
            if (mainCollider == null)
                return false;

            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePosition))
                return false;

            return mainCollider.OverlapPoint(mousePosition);
        }

        private float GetBottomOffset()
        {
            Bounds bounds = mainCollider != null ? mainCollider.bounds : new Bounds(transform.position, Vector3.zero);
            return transform.position.y - bounds.min.y;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            StirLiquidParticle(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            StirLiquidParticle(other);
        }

        private void StirLiquidParticle(Collider2D other)
        {
            if (!IsPickedUp || velocityBlend <= 0f)
                return;

            bool hasLinearMotion = rodVelocity.sqrMagnitude >= stirMinimumSpeed * stirMinimumSpeed;
            bool hasAngularMotion = Mathf.Abs(rodAngularVelocity) >= stirMinimumAngularSpeed;
            if (!hasLinearMotion && !hasAngularMotion)
                return;

            if (other == null || !other.TryGetComponent(out LiquidParticleData _))
                return;

            Rigidbody2D otherRb = other.attachedRigidbody;
            if (otherRb == null)
                return;

            Vector2 targetVelocity = Vector2.zero;
            if (hasLinearMotion)
                targetVelocity += rodVelocity * linearVelocityScale;

            if (hasAngularMotion)
            {
                Vector2 center = rb != null ? rb.position : (Vector2)transform.position;
                Vector2 offset = otherRb.position - center;
                Vector2 tangent = offset.sqrMagnitude > 0.0001f
                    ? Vector2.Perpendicular(offset).normalized
                    : (Vector2)transform.up;

                targetVelocity += tangent
                    * Mathf.Sign(rodAngularVelocity)
                    * Mathf.Abs(rodAngularVelocity)
                    * angularVelocityScale;
            }

            targetVelocity = Vector2.ClampMagnitude(targetVelocity, Mathf.Max(0f, maxInjectedSpeed));
            if (targetVelocity.sqrMagnitude <= 0.0001f)
                return;

            otherRb.WakeUp();
            otherRb.linearVelocity = Vector2.Lerp(
                otherRb.linearVelocity,
                targetVelocity,
                Mathf.Clamp01(velocityBlend));

            if (other.TryGetComponent(out LiquidReaction reaction))
                reaction.WakeUp();
        }

        private void EnsureRigidbody()
        {
            rb = GetComponent<Rigidbody2D>();
            if (rb == null)
                rb = gameObject.AddComponent<Rigidbody2D>();

            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        private void EnsureCollider()
        {
            if (mainCollider == null)
                mainCollider = GetComponent<Collider2D>();

            if (mainCollider != null)
                mainCollider.isTrigger = true;
        }

        private void EnsureVisualFallback()
        {
            if (GetComponentInChildren<SpriteRenderer>() != null || GetComponentInChildren<LineRenderer>() != null)
                return;

            generatedVisual = gameObject.AddComponent<LineRenderer>();
            generatedVisual.useWorldSpace = false;
            generatedVisual.positionCount = 2;
            generatedVisual.SetPosition(0, Vector3.down * generatedVisualLength * 0.5f);
            generatedVisual.SetPosition(1, Vector3.up * generatedVisualLength * 0.5f);
            generatedVisual.widthMultiplier = generatedVisualWidth;
            generatedVisual.startColor = generatedVisualColor;
            generatedVisual.endColor = generatedVisualColor;
            generatedVisual.sortingOrder = pickupSortingOrder;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
                generatedVisual.material = new Material(shader);
        }

        private void CacheSortingOrders()
        {
            spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            originalSortingOrders = new int[spriteRenderers.Length];
            for (int i = 0; i < spriteRenderers.Length; i++)
                originalSortingOrders[i] = spriteRenderers[i].sortingOrder;
        }

        private void SetPickedSortingOrder()
        {
            if (spriteRenderers == null)
                return;

            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null)
                    spriteRenderers[i].sortingOrder = pickupSortingOrder + i;
            }
        }

        private void RestoreSortingOrder()
        {
            if (spriteRenderers == null || originalSortingOrders == null)
                return;

            for (int i = 0; i < spriteRenderers.Length && i < originalSortingOrders.Length; i++)
            {
                if (spriteRenderers[i] != null)
                    spriteRenderers[i].sortingOrder = originalSortingOrders[i];
            }
        }

        private static float NormalizeAngle(float angle)
        {
            return Mathf.DeltaAngle(0f, angle);
        }
    }
}
