using UnityEngine;

namespace Slainte.Bartending
{
    public enum StirringRodState
    {
        Idle,
        PickedUp,
        Rotating
    }

    // 젓는 막대(바스푼) 컨트롤러. Idle → PickedUp(좌클릭으로 집어 자유 이동) →
    // Rotating(우클릭 유지, 세로 드래그로 회전) 상태 머신이며, 손에 든 상태에서 막대의
    // 실제 이동/회전 속도를 액체 파티클에 주입해(StirLiquidParticle) 물리적으로 젓는 느낌을 낸다.
    // 일정 시간 이상 유효한 속도로 저었고 성분이 고르게 섞였으면(RegisterStirActivity)
    // VesselLiquidTracker에 "저어짐" 상태를 기록해 레시피 판정에 반영한다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class StirringRodController : MonoBehaviour, IBartendingItem, IPointerAnchoredPickup
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
        [SerializeField, Min(0.05f)] private float stirAttemptDuration = 0.35f;
        [SerializeField, Min(0.1f)] private float stirCompletionDuration = 1f;
        [SerializeField, Range(0f, 1f)] private float maximumCompositionDeviation = 0.1f;
        [SerializeField, Min(0.05f)] private float compositionCheckInterval = 0.2f;

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
        private BartendingItemOrder interactionOrder;
        private LineRenderer generatedVisual;
        private float currentAngle;
        private Vector2 previousPosition;
        private Vector2 rodVelocity;
        private float previousAngle;
        private float rodAngularVelocity;
        private Vector3 pointerOffset;
        private VesselLiquidTracker activeStirVessel;
        private int activeStirContentVersion = -1;
        private float activeStirTime;
        private float nextCompositionCheckTime;
        private float lastStirSampleFixedTime = float.MinValue;
        private bool stirAttemptRecorded;

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
            interactionOrder = BartendingItemOrder.Attach(gameObject, mainCollider);

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
            BartendingPointerAnchor.Release(this);
            BartendingSelection.Release(this);
            ResetStirProgress();
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
            if (!BartendingViewport.TryGetPointerWorldPosition(
                    mainCamera,
                    Input.mousePosition,
                    out Vector3 pointerWorld))
            {
                pointerWorld = transform.position;
            }
            PickupRod(pointerWorld);
        }

        private void PickupRod(Vector3 pointerWorld)
        {
            if (!BartendingSelection.TryAcquire(this))
                return;

            currentState = StirringRodState.PickedUp;
            currentSlot?.Vacate();
            currentSlot = null;
            pointerOffset = transform.position - pointerWorld;
            pointerOffset.z = 0f;
            SetPickedSortingOrder();
            interactionOrder?.BringToFront();
        }

        private void TryDropRod()
        {
            if (ToolCabinetController.TryReturnHeldItem(this, mainCamera, Input.mousePosition))
                return;

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
            SetPositionImmediately(new Vector3(
                slotTransform.position.x,
                slotTransform.position.y + bottomOffset,
                0f));
            ReleaseRod();
        }

        public void OnPickedUp()
        {
            PickupRod();
        }

        public void OnPickedUpAt(Vector3 pointerWorld)
        {
            PickupRod(pointerWorld);
        }

        public void OnDropped()
        {
            TryDropRod();
        }

        private void ReleaseRod()
        {
            currentState = StirringRodState.Idle;
            pointerOffset = Vector3.zero;
            ResetStirProgress();
            RestoreSortingOrder();
            BartendingSelection.Release(this);
        }

        private void FollowMousePosition()
        {
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePosition))
                return;

            SetPosition(mousePosition + pointerOffset);
        }

        private void StartRotating()
        {
            if (!BartendingPointerAnchor.TryLock(this))
                return;

            currentState = StirringRodState.Rotating;
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
            BartendingPointerAnchor.UnlockAndWarp(
                this,
                mainCamera,
                transform.position,
                out _);

            currentState = StirringRodState.PickedUp;
        }

        private void SetPosition(Vector3 position)
        {
            position.z = 0f;
            if (rb != null)
                rb.MovePosition(position);
            else
                transform.position = position;
        }

        private void SetPositionImmediately(Vector3 position)
        {
            position.z = 0f;
            if (rb != null)
            {
                rb.position = position;
                rb.linearVelocity = Vector2.zero;
            }
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

            return interactionOrder != null
                ? interactionOrder.IsFrontmostAt(mousePosition)
                : mainCollider.OverlapPoint(mousePosition);
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

            if (other == null || !other.TryGetComponent(out LiquidParticleData particle))
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

            RegisterStirActivity(particle.VesselOwner);
        }

        // 저어지는 시간(activeStirTime)을 누적해 두 단계로 판정한다: stirAttemptDuration을
        // 넘기면 "시도함"으로 한 번만 기록하고, stirCompletionDuration을 넘긴 뒤로는 주기적으로
        // 성분 분산도(CalculateMeanCompositionDeviation)를 확인해 충분히 고르게 섞였을 때만
        // "완료"로 표시한다. 그릇이 바뀌거나(vessel) 내용물이 변하면(ContentVersion) 처음부터 다시 센다.
        private void RegisterStirActivity(VesselLiquidTracker vessel)
        {
            if (vessel == null)
                return;

            if (activeStirVessel != vessel
                || activeStirContentVersion != vessel.ContentVersion)
            {
                activeStirVessel = vessel;
                activeStirContentVersion = vessel.ContentVersion;
                activeStirTime = 0f;
                nextCompositionCheckTime = Time.unscaledTime;
                lastStirSampleFixedTime = float.MinValue;
                stirAttemptRecorded = false;
            }

            if (Mathf.Approximately(lastStirSampleFixedTime, Time.fixedTime))
                return;

            lastStirSampleFixedTime = Time.fixedTime;
            activeStirTime += Time.fixedDeltaTime;
            if (activeStirTime >= stirAttemptDuration && !stirAttemptRecorded)
            {
                vessel.MarkContentsAsStirAttempted();
                stirAttemptRecorded = true;
            }

            if (activeStirTime < stirCompletionDuration
                || Time.unscaledTime < nextCompositionCheckTime)
            {
                return;
            }

            nextCompositionCheckTime = Time.unscaledTime
                + Mathf.Max(0.05f, compositionCheckInterval);
            if (vessel.CalculateMeanCompositionDeviation()
                <= Mathf.Clamp01(maximumCompositionDeviation))
            {
                vessel.MarkContentsAsStirred();
            }
        }

        private void ResetStirProgress()
        {
            activeStirVessel = null;
            activeStirContentVersion = -1;
            activeStirTime = 0f;
            nextCompositionCheckTime = 0f;
            lastStirSampleFixedTime = float.MinValue;
            stirAttemptRecorded = false;
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
