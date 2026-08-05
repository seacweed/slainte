using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Slainte.Bartending
{
    public enum BottleState
    {
        Idle,
        PickedUp,
        Tilting,
        Returning
    }

    public enum BottleRotationPivotMode
    {
        TransformOrigin,
        HeightPercentage,
        DistanceFromMouth
    }

    [RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
    public class BottleController : MonoBehaviour, IBartendingItem
    {
        [Header("Item Data")]
        [SerializeField] private ItemDef bottleData;
        
        public ItemDef BottleData => bottleData;

        [Header("Tilt Settings")]
        [SerializeField] private float maxTiltAngle = 120f;
        [SerializeField] private float tiltSensitivity = 0.5f;
        [SerializeField] private float returnSpeed = 0.8f;

        [Header("Rotation Pivot Settings")]
        [SerializeField] private BottleRotationPivotMode rotationPivotMode = BottleRotationPivotMode.TransformOrigin;
        [Tooltip("Normalized bottle height measured from the bottom (0 = bottom, 1 = mouth).")]
        [Range(0f, 1f)]
        [SerializeField] private float rotationPivotHeightPercentage = 0.75f;
        [Tooltip("World-space distance measured downward from the bottle mouth.")]
        [Min(0f)]
        [SerializeField] private float rotationPivotDistanceFromMouth = 0.25f;
        
        [Header("Drop Settings")]
        [SerializeField] private LayerMask slotLayer;
        
        [Header("Animation Settings")]
        [Tooltip("Curve for the smooth return when right-click is released.")]
        [SerializeField] private AnimationCurve returnEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Liquid Settings")]
        public Transform liquidSpawnPoint;
        public float maxCapacity = 100f;
        public float currentCapacity = 100f;
        public float pourRate = 0.05f;
        private float pourTimer = 0f;
        private float? initialCapacityOverride;

        public float CurrentCapacity => currentCapacity;
        public BottleRotationPivotMode RotationPivotMode => rotationPivotMode;
        public float BottleWorldHeight => GetBottleWorldHeight();
        public float RotationPivotHeightPercentage => GetRotationPivotHeightPercentage();
        public float RotationPivotDistanceFromMouth => (1f - RotationPivotHeightPercentage) * BottleWorldHeight;
        public Vector3 RotationPivotWorldPosition => hasRotationPivotAnchor
            ? rotationPivotAnchorWorld
            : GetConfiguredRotationPivotWorldPosition();
        public event System.Action<BottleController, float> CapacityChanged;

        private BottleState currentState = BottleState.Idle;
        private SpriteRenderer spriteRenderer;
        private Collider2D col;
        
        private int originalSortingOrder;
        private const int PICKUP_SORTING_ORDER = 100;
        private BartendingItemOrder interactionOrder;

        private Camera mainCamera;
        
        // Tilt state variables
        private float initialAngle;
        private float currentAngle = 0f;
        private Coroutine returnCoroutine;
        private bool hasRotationPivotAnchor;
        private Vector3 rotationPivotAnchorWorld;
        private Vector3 rootOffsetFromPivotAtTiltStart;
        private float angleAtPivotCapture;

        private SlotController currentSlot; // 현재 안착되어 있는 슬롯 레퍼런스
        private Vector3 dragVelocity = Vector3.zero;

        private void Start()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            col = GetComponent<Collider2D>();
            mainCamera = Camera.main;

            // 1. Rigidbody2D 키네마틱 물리 셋업 강제 보장 (2D 물리 트리거 상호작용 완벽 복구)
            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody2D>();
            }
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;

            // 2. slotLayer가 빈 값이거나 Nothing일 시 스마트 자동 보정
            if (slotLayer.value == 0)
            {
                int layerIdx = LayerMask.NameToLayer("Slot");
                if (layerIdx == -1) layerIdx = 0;
                slotLayer = 1 << layerIdx;
            }

            originalSortingOrder = spriteRenderer.sortingOrder;

            ApplyBottleData();
            interactionOrder = BartendingItemOrder.Attach(gameObject, col);
        }

        public void Init(ItemDef data)
        {
            if (data == null || data.type != ItemType.Bottle)
            {
                Debug.LogWarning("BottleController에는 종류가 Bottle인 ItemDef가 필요합니다.");
                return;
            }

            bottleData = data;
            ApplyBottleData();
        }
        private void ApplyBottleData()
        {
            if (bottleData == null || bottleData.type != ItemType.Bottle)
                return;

            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            if (bottleData.icon != null)
                spriteRenderer.sprite = bottleData.icon;

            ApplyBottleGeometryOverride();

            maxCapacity = bottleData.capacityMl;
            currentCapacity = initialCapacityOverride.HasValue
                ? Mathf.Clamp(initialCapacityOverride.Value, 0f, maxCapacity)
                : bottleData.capacityMl;
        }

        private void ApplyBottleGeometryOverride()
        {
            if (!bottleData.overrideBottleGeometry
                || spriteRenderer == null
                || spriteRenderer.sprite == null)
            {
                return;
            }

            Bounds spriteBounds = spriteRenderer.sprite.bounds;
            Vector2 mouthNormalized = ClampNormalized(bottleData.liquidSpawnNormalized);
            if (spriteRenderer.flipX)
                mouthNormalized.x = 1f - mouthNormalized.x;
            if (spriteRenderer.flipY)
                mouthNormalized.y = 1f - mouthNormalized.y;

            if (liquidSpawnPoint != null)
            {
                Vector3 localMouth = new Vector3(
                    Mathf.Lerp(spriteBounds.min.x, spriteBounds.max.x, mouthNormalized.x),
                    Mathf.Lerp(spriteBounds.min.y, spriteBounds.max.y, mouthNormalized.y),
                    0f);
                liquidSpawnPoint.position = transform.TransformPoint(localMouth);
            }

            if (col == null)
                col = GetComponent<Collider2D>();

            if (col is BoxCollider2D boxCollider)
            {
                Vector2 centerNormalized = ClampNormalized(bottleData.colliderCenterNormalized);
                if (spriteRenderer.flipX)
                    centerNormalized.x = 1f - centerNormalized.x;
                if (spriteRenderer.flipY)
                    centerNormalized.y = 1f - centerNormalized.y;

                Vector2 sizeNormalized = bottleData.colliderSizeNormalized;
                boxCollider.offset = new Vector2(
                    Mathf.Lerp(spriteBounds.min.x, spriteBounds.max.x, centerNormalized.x),
                    Mathf.Lerp(spriteBounds.min.y, spriteBounds.max.y, centerNormalized.y));
                boxCollider.size = new Vector2(
                    spriteBounds.size.x * Mathf.Max(0.01f, sizeNormalized.x),
                    spriteBounds.size.y * Mathf.Max(0.01f, sizeNormalized.y));
            }
        }

        private static Vector2 ClampNormalized(Vector2 value)
        {
            return new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
        }

        public void SetCurrentCapacity(float value, bool notify = false)
        {
            initialCapacityOverride = Mathf.Max(0f, value);
            float upperBound = bottleData != null && bottleData.type == ItemType.Bottle
                ? bottleData.capacityMl
                : Mathf.Max(maxCapacity, initialCapacityOverride.Value);
            currentCapacity = Mathf.Clamp(initialCapacityOverride.Value, 0f, upperBound);

            if (notify)
                CapacityChanged?.Invoke(this, currentCapacity);
        }

        public void UseTransformRotationPivot()
        {
            rotationPivotMode = BottleRotationPivotMode.TransformOrigin;
            RefreshRotationPivotAnchorIfActive();
        }

        public void SetRotationPivotByHeightPercentage(float heightPercentage)
        {
            rotationPivotMode = BottleRotationPivotMode.HeightPercentage;
            rotationPivotHeightPercentage = Mathf.Clamp01(heightPercentage);
            RefreshRotationPivotAnchorIfActive();
        }

        public void SetRotationPivotByMouthDistance(float worldDistance)
        {
            rotationPivotMode = BottleRotationPivotMode.DistanceFromMouth;
            rotationPivotDistanceFromMouth = Mathf.Max(0f, worldDistance);
            RefreshRotationPivotAnchorIfActive();
        }

        private void Update()
        {
            HandleInput();
            HandlePouring();
        }

        private void HandlePouring()
        {
            if (Mathf.Abs(currentAngle) >= 90f && currentCapacity > 0)
            {
                pourTimer += Time.deltaTime;
                if (pourTimer >= pourRate)
                {
                    pourTimer = 0f;
                    SpawnLiquid();
                }
            }
            else
            {
                pourTimer = 0f;
            }
        }

        private void SpawnLiquid()
        {
            if (LiquidPool.Instance == null) return;

            if (liquidSpawnPoint == null)
            {
                Debug.LogWarning("⚠️ Liquid Spawn Point가 인스펙터에 할당되지 않았습니다! 병의 중심(몸체)에서 스폰됩니다.");
            }

            Vector3 spawnPos = liquidSpawnPoint != null ? liquidSpawnPoint.position : transform.position;
            Vector3 randomOffset = new Vector3(Random.Range(-0.1f, 0.1f), 0, 0);

            float volumeMl = Mathf.Min(1f, currentCapacity);
            GameObject obj = LiquidPool.Instance.GetParticle(
                spawnPos + randomOffset,
                bottleData,
                volumeMl);
            if (obj != null)
            {
                currentCapacity = Mathf.Max(0f, currentCapacity - volumeMl);
                initialCapacityOverride = currentCapacity;
                CapacityChanged?.Invoke(this, currentCapacity);
            }
        }

        private void HandleInput()
        {
            // Left Click (Pickup / Drop Toggle)
            if (Input.GetMouseButtonDown(0))
            {
                if (currentState == BottleState.Idle && IsMouseOverBottle())
                {
                    PickupBottle();
                }
                else if (currentState == BottleState.PickedUp || currentState == BottleState.Returning)
                {
                    TryDropBottle();
                }
            }

            // A virtual pivot changes the root position during the return animation.
            // Keeping the mouse follow active at the same time would fight that motion.
            if (currentState == BottleState.PickedUp ||
                (currentState == BottleState.Returning && rotationPivotMode == BottleRotationPivotMode.TransformOrigin))
            {
                FollowMousePosition();
            }

            // Right Click (Tilt / Return)
            if (Input.GetMouseButtonDown(1))
            {
                if (currentState == BottleState.PickedUp || currentState == BottleState.Returning)
                {
                    StartTilting();
                }
            }
            else if (Input.GetMouseButtonUp(1))
            {
                if (currentState == BottleState.Tilting)
                {
                    StartReturning();
                }
            }

            // Perform Tilt while Right Click is held
            if (Input.GetMouseButton(1) && currentState == BottleState.Tilting)
            {
                PerformTilting();
            }
        }

        private void PickupBottle()
        {
            currentState = BottleState.PickedUp;
            hasRotationPivotAnchor = false;
            interactionOrder?.BringToFront();
            
            // 기존 슬롯에서 집어올려질 때, 슬롯 점유 해제
            if (currentSlot != null)
            {
                currentSlot.Vacate();
                currentSlot = null;
            }
            
            // Stop returning if it was returning while picked up again
            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }
        }

        private void FollowMousePosition()
        {
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                return;
            }
            
            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                // 댐핑을 걷어내고 0초 즉각 1:1 마우스 매핑 + 연속 물리(Sweep) 충돌 보장
                rb.MovePosition(mousePos);
            }
            else
            {
                transform.position = mousePos;
            }
        }

        private float GetPivotToBottomOffset()
        {
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                return (spriteRenderer.sprite.pivot.y / spriteRenderer.sprite.pixelsPerUnit) * transform.localScale.y;
            }
            return 0f;
        }

        private bool TryGetBottleLocalBounds(out float bottom, out float top, out float centerX)
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                Bounds spriteBounds = spriteRenderer.sprite.bounds;
                bottom = spriteBounds.min.y;
                top = spriteBounds.max.y;
                centerX = spriteBounds.center.x;
                return top > bottom;
            }

            if (col == null)
                col = GetComponent<Collider2D>();

            if (col is BoxCollider2D boxCollider)
            {
                bottom = boxCollider.offset.y - boxCollider.size.y * 0.5f;
                top = boxCollider.offset.y + boxCollider.size.y * 0.5f;
                centerX = boxCollider.offset.x;
                return top > bottom;
            }

            bottom = 0f;
            top = 0f;
            centerX = 0f;
            return false;
        }

        private float GetBottleWorldHeight()
        {
            if (!TryGetBottleLocalBounds(out float bottom, out float top, out float centerX))
                return 0f;

            Vector3 bottomWorld = transform.TransformPoint(centerX, bottom, 0f);
            Vector3 topWorld = transform.TransformPoint(centerX, top, 0f);
            return Vector3.Distance(bottomWorld, topWorld);
        }

        private float GetRotationPivotHeightPercentage()
        {
            if (rotationPivotMode == BottleRotationPivotMode.HeightPercentage)
                return Mathf.Clamp01(rotationPivotHeightPercentage);

            if (rotationPivotMode == BottleRotationPivotMode.DistanceFromMouth)
            {
                float height = GetBottleWorldHeight();
                return height > Mathf.Epsilon
                    ? 1f - Mathf.Clamp01(rotationPivotDistanceFromMouth / height)
                    : 1f;
            }

            if (!TryGetBottleLocalBounds(out float bottom, out float top, out _))
                return 0f;

            return Mathf.InverseLerp(bottom, top, 0f);
        }

        private Vector3 GetRotationPivotLocalPosition()
        {
            if (rotationPivotMode == BottleRotationPivotMode.TransformOrigin ||
                !TryGetBottleLocalBounds(out float bottom, out float top, out float centerX))
            {
                return Vector3.zero;
            }

            float heightPercentage = GetRotationPivotHeightPercentage();
            return new Vector3(centerX, Mathf.Lerp(bottom, top, heightPercentage), 0f);
        }

        private Vector3 GetConfiguredRotationPivotWorldPosition()
        {
            return transform.TransformPoint(GetRotationPivotLocalPosition());
        }

        private void CaptureRotationPivotAnchor()
        {
            rotationPivotAnchorWorld = GetConfiguredRotationPivotWorldPosition();
            rootOffsetFromPivotAtTiltStart = transform.position - rotationPivotAnchorWorld;
            angleAtPivotCapture = currentAngle;
            hasRotationPivotAnchor = true;
        }

        private void RefreshRotationPivotAnchorIfActive()
        {
            if (currentState == BottleState.Tilting || currentState == BottleState.Returning)
                CaptureRotationPivotAnchor();
        }

        private void ApplyRotationAroundConfiguredPivot(float targetAngle)
        {
            if (!hasRotationPivotAnchor)
                CaptureRotationPivotAnchor();

            float angleFromCapture = targetAngle - angleAtPivotCapture;
            Vector3 rotatedRootOffset =
                Quaternion.Euler(0f, 0f, angleFromCapture) * rootOffsetFromPivotAtTiltStart;
            Vector3 targetPosition = rotationPivotAnchorWorld + rotatedRootOffset;

            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.position = targetPosition;
                rb.rotation = targetAngle;
            }
            else
            {
                transform.SetPositionAndRotation(targetPosition, Quaternion.Euler(0f, 0f, targetAngle));
            }

            currentAngle = targetAngle;
        }

        private void TryDropBottle()
        {
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                return;
            }

            Collider2D[] hits = Physics2D.OverlapPointAll(mousePos, slotLayer);

            foreach (var hit in hits)
            {
                // 자기 자신 콜라이더는 건너뜁니다.
                if (hit == col) continue;

                SlotController slot = hit.GetComponent<SlotController>();
                if (slot != null)
                {
                    // 슬롯이 비어있는 경우에만 안착 허용
                    if (!slot.IsOccupied)
                    {
                        currentSlot = slot;
                        slot.Occupy(this);

                        // Snap to slot (바닥면 Y 오프셋 칼각 정렬!)
                        float bottomOffset = GetPivotToBottomOffset();
                        transform.position = new Vector3(hit.transform.position.x, hit.transform.position.y + bottomOffset, 0f);
                        transform.rotation = Quaternion.identity;
                        currentAngle = 0f;
                        
                        ReleaseBottle();
                        return;
                    }
                }
                else
                {
                    // Snap to slot (하위 호환용 단순 스냅)
                    float bottomOffset = GetPivotToBottomOffset();
                    transform.position = new Vector3(hit.transform.position.x, hit.transform.position.y + bottomOffset, 0f);
                    transform.rotation = Quaternion.identity;
                    currentAngle = 0f;
                    
                    ReleaseBottle();
                    return;
                }
            }
        }

        // IBartendingItem 인터페이스 완벽 구현부
        public GameObject GameObject => gameObject;
        public bool IsPickedUp => currentState == BottleState.PickedUp || currentState == BottleState.Tilting || currentState == BottleState.Returning;

        public void SnapToSlot(Transform slotTransform, SlotController slot)
        {
            currentSlot = slot;
            float bottomOffset = GetPivotToBottomOffset();
            transform.position = new Vector3(slotTransform.position.x, slotTransform.position.y + bottomOffset, 0f);
            transform.rotation = Quaternion.identity;
            currentAngle = 0f;
            ReleaseBottle();
        }

        public void OnPickedUp()
        {
            PickupBottle();
        }

        public void OnDropped()
        {
            TryDropBottle();
        }

        private void ReleaseBottle()
        {
            currentState = BottleState.Idle;
            hasRotationPivotAnchor = false;
            
            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }
        }

        private void StartTilting()
        {
            currentState = BottleState.Tilting;
            initialAngle = currentAngle;
            CaptureRotationPivotAnchor();
            
            // Lock and hide the cursor so it stays fixed relative to the bottle
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            
            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }
        }

        private void PerformTilting()
        {
            // GetAxis provides delta movement when the cursor is locked
            float deltaY = Input.GetAxis("Mouse Y");
            
            // Accumulate angle directly since we are getting delta movement per frame
            // Multiplied by 10 to keep the sensitivity feel roughly similar to pixel delta
            float targetAngle = currentAngle + deltaY * tiltSensitivity * 10f;
            targetAngle = Mathf.Clamp(targetAngle, -maxTiltAngle, maxTiltAngle);
            ApplyRotationAroundConfiguredPivot(targetAngle);
        }

        private void StartReturning()
        {
            currentState = BottleState.Returning;
            
            // Unlock and show the cursor when tilting stops
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            
            // Warp the cursor back to the bottle's position
            if (Mouse.current != null && mainCamera != null)
            {
                Vector2 screenPos = BartendingViewport.GetPointerScreenPosition(mainCamera, transform.position);
                Mouse.current.WarpCursorPosition(screenPos);
            }
            
            returnCoroutine = StartCoroutine(ReturnToUprightRoutine());
        }

        private IEnumerator ReturnToUprightRoutine()
        {
            float startAngle = currentAngle;
            float timeElapsed = 0f;

            while (timeElapsed < returnSpeed)
            {
                timeElapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(timeElapsed / returnSpeed);
                float curveValue = returnEase.Evaluate(normalizedTime);
                
                float targetAngle = Mathf.Lerp(startAngle, 0f, curveValue);
                ApplyRotationAroundConfiguredPivot(targetAngle);
                
                yield return null;
            }

            ApplyRotationAroundConfiguredPivot(0f);

            if (rotationPivotMode != BottleRotationPivotMode.TransformOrigin &&
                Mouse.current != null && mainCamera != null)
            {
                Vector2 screenPos = BartendingViewport.GetPointerScreenPosition(mainCamera, transform.position);
                Mouse.current.WarpCursorPosition(screenPos);
            }
            
            // Transition back to PickedUp state so they can move it or drop it again
            currentState = BottleState.PickedUp;
            hasRotationPivotAnchor = false;
            returnCoroutine = null;
        }

        private bool IsMouseOverBottle()
        {
            if (col == null) return false;
            
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                return false;
            }

            return interactionOrder != null
                ? interactionOrder.IsFrontmostAt(mousePos)
                : col.OverlapPoint(mousePos);
        }
    }
}
