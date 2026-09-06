using System.Collections;
using UnityEngine;

namespace Slainte.Bartending
{
    public enum BottleState
    {
        Idle,
        PickedUp,
        Tilting,
        Returning
    }

    // 병을 기울일 때 어느 점을 축으로 회전시킬지 결정한다. TransformOrigin(오브젝트 원점)으로 그냥
    // 돌리면 병입구가 아니라 병 전체가 허공에서 스핀하는 것처럼 보이므로, 실제로는 입구 근처의
    // 점(HeightPercentage 또는 DistanceFromMouth)을 축으로 잡아 "따르는" 동작처럼 보이게 한다.
    public enum BottleRotationPivotMode
    {
        TransformOrigin,
        HeightPercentage,
        DistanceFromMouth
    }

    // 드래그로 들고 기울여 액체를 따르는 병 오브젝트. GlassController와 상태 머신/포인터 동기화
    // 구조는 동일하지만, 회전축을 병 입구 쪽으로 옮기는 로직(ApplyRotationAroundConfiguredPivot)과
    // 기울기 각도에 따라 실제 액체 입자를 스폰하는 로직(HandlePouring)이 이 클래스의 핵심이다.
    [RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
    public class BottleController : MonoBehaviour, IBartendingItem,
        IBartendingViewTransitionParticipant
    {
        [Header("Item Data")]
        [SerializeField] private ItemDef bottleData;
        private Sprite bottleVisualOverride;
        private bool hasBottleVisualOverride;
        
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
        [Tooltip("Liquid volume emitted per second, in milliliters.")]
        [Min(0.01f)] public float pourMlPerSecond = 20f;
        [Tooltip("Safety limit for catch-up emission after a slow frame.")]
        [SerializeField, Min(1)] private int maxParticlesPerFrame = 8;
        [Tooltip("Bottle angle at which liquid starts leaving the mouth.")]
        [SerializeField, Range(45f, 120f)] private float pourStartAngle = 90f;
        [Tooltip("Bottle angle at which the configured ml/s and exit speed are fully reached.")]
        [SerializeField, Range(90f, 180f)] private float fullPourAngle = 120f;
        [Tooltip("Fraction of the configured flow emitted immediately after the pour angle is crossed.")]
        [SerializeField, Range(0.1f, 1f)] private float minimumPourFlowFactor = 0.65f;
        [Tooltip("Initial liquid speed along the bottle mouth direction, in world units per second.")]
        [SerializeField, Min(0f)] private float pourExitSpeed = 2.4f;
        [Tooltip("How much of the moving bottle mouth velocity is inherited by emitted liquid.")]
        [SerializeField, Range(0f, 1f)] private float mouthVelocityInheritance;
        [Tooltip("Maximum inherited bottle-mouth speed, preventing teleports from launching liquid.")]
        [SerializeField, Min(0f)] private float maximumInheritedMouthSpeed = 3f;
        [Tooltip("Half-width of the liquid nozzle. Jitter is applied perpendicular to the exit direction.")]
        [SerializeField, Min(0f)] private float pourSpawnHalfWidth;
        private float pourTimer = 0f;
        private float? initialCapacityOverride;
        private Vector2 previousLiquidSpawnPosition;
        private Vector2 liquidMouthVelocity;
        private bool hasLiquidMouthSample;

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
        private float rotationHorizontalSensitivity = 1f;
        private float rotationHorizontalScreenPadding = 12f;
        private Vector3 rotationPivotAnchorWorld;
        private Vector3 rootOffsetFromPivotAtTiltStart;
        private float angleAtPivotCapture;
        private const int PointerSyncFrameBudget = 6;
        private bool pointerSyncPending;
        private bool completeReturnAfterPointerSync;
        private int pointerSyncFramesRemaining;
        private Vector2 pointerSyncScreenPosition;
        private Vector3 pointerSyncPivotWorld;
        private Vector3 pointerPivotOffset;
        private bool viewTransitionSuspended;

        private SlotController currentSlot; // 현재 안착되어 있는 슬롯 레퍼런스
        private Vector3 dragVelocity = Vector3.zero;

        public void ConfigureHorizontalRotationMovement(float sensitivity, float screenPadding)
        {
            rotationHorizontalSensitivity = Mathf.Max(0f, sensitivity);
            rotationHorizontalScreenPadding = Mathf.Max(0f, screenPadding);
        }

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
            ResetLiquidMouthKinematics();
            interactionOrder = BartendingItemOrder.Attach(gameObject, col);
        }

        public void Init(ItemDef data)
        {
            Init(data, null, false);
        }

        public void Init(ItemDef data, Sprite visualOverride)
        {
            Init(data, visualOverride, true);
        }

        private void Init(ItemDef data, Sprite visualOverride, bool hasVisualOverride)
        {
            if (data == null || data.type != ItemType.Bottle)
            {
                Debug.LogWarning("BottleController에는 종류가 Bottle인 ItemDef가 필요합니다.");
                return;
            }

            bottleData = data;
            bottleVisualOverride = visualOverride;
            hasBottleVisualOverride = hasVisualOverride;
            ApplyBottleData();
        }
        private void ApplyBottleData()
        {
            if (bottleData == null || bottleData.type != ItemType.Bottle)
                return;

            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            Sprite visualSprite = hasBottleVisualOverride
                ? bottleVisualOverride
                : bottleData.icon;
            if (hasBottleVisualOverride || visualSprite != null)
                spriteRenderer.sprite = visualSprite;

            ApplyBottleGeometryOverride();
            ResetLiquidMouthKinematics();

            maxCapacity = bottleData.capacityMl;
            currentCapacity = initialCapacityOverride.HasValue
                ? Mathf.Clamp(initialCapacityOverride.Value, 0f, maxCapacity)
                : bottleData.capacityMl;
        }

        // ItemDef가 지정한 정규화 좌표(스프라이트 bounds 기준 0~1)로 액체 스폰 지점과 클릭 콜라이더를
        // 재배치한다. 아트마다 병입구 위치가 다르고 flipX/flipY로 좌우·상하 반전될 수 있으므로,
        // 픽셀 좌표 대신 정규화 좌표로 저장해두고 매번 실제 스프라이트 bounds에 맞춰 환산한다.
        private void ApplyBottleGeometryOverride()
        {
            if (spriteRenderer == null
                || spriteRenderer.sprite == null)
            {
                return;
            }

            Bounds spriteBounds = spriteRenderer.sprite.bounds;
            bool useConfiguredLiquidSpawn = bottleData.overrideBottleGeometry
                || bottleData.overrideBottleLiquidSpawn;
            if (useConfiguredLiquidSpawn && liquidSpawnPoint != null)
            {
                Vector2 mouthNormalized = ClampNormalized(bottleData.liquidSpawnNormalized);
                if (spriteRenderer.flipX)
                    mouthNormalized.x = 1f - mouthNormalized.x;
                if (spriteRenderer.flipY)
                    mouthNormalized.y = 1f - mouthNormalized.y;

                Vector3 localMouth = new Vector3(
                    Mathf.Lerp(spriteBounds.min.x, spriteBounds.max.x, mouthNormalized.x),
                    Mathf.Lerp(spriteBounds.min.y, spriteBounds.max.y, mouthNormalized.y),
                    0f);
                if (bottleData.overrideBottleLiquidSpawn)
                {
                    float pixelsPerUnit = Mathf.Max(1f, spriteRenderer.sprite.pixelsPerUnit);
                    float outwardDirection = spriteRenderer.flipY ? -1f : 1f;
                    localMouth.y += outwardDirection
                        * Mathf.Max(0f, bottleData.liquidSpawnOutwardPixels)
                        / pixelsPerUnit;
                }

                liquidSpawnPoint.position = spriteRenderer.transform.TransformPoint(localMouth);
            }

            if (col == null)
                col = GetComponent<Collider2D>();

            if (col is BoxCollider2D boxCollider)
            {
                bool useConfiguredCollider = bottleData.overrideBottleGeometry
                    || bottleData.overrideBottleClickCollider;
                Vector2 centerNormalized = useConfiguredCollider
                    ? ClampNormalized(bottleData.colliderCenterNormalized)
                    : new Vector2(0.5f, 0.5f);
                if (spriteRenderer.flipX)
                    centerNormalized.x = 1f - centerNormalized.x;
                if (spriteRenderer.flipY)
                    centerNormalized.y = 1f - centerNormalized.y;

                Vector2 sizeNormalized = useConfiguredCollider
                    ? bottleData.colliderSizeNormalized
                    : Vector2.one;
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
            if (viewTransitionSuspended)
                return;

            bool synchronizingPointer = UpdatePointerSynchronization();
            if (!synchronizingPointer)
                HandleInput();
            UpdateLiquidMouthKinematics();
            HandlePouring();
        }

        public void SuspendForViewTransition()
        {
            if (viewTransitionSuspended)
                return;

            viewTransitionSuspended = true;
            pourTimer = 0f;
            pointerSyncPending = false;
            completeReturnAfterPointerSync = false;
            pointerSyncFramesRemaining = 0;
        }

        public void ResumeAfterViewTransition()
        {
            if (!viewTransitionSuspended)
                return;

            viewTransitionSuspended = false;
            ResetLiquidMouthKinematics();
            if (!IsPickedUp)
                return;

            if (mainCamera == null)
                mainCamera = Camera.main;
            if (!BartendingViewport.TryGetPointerWorldPosition(
                    mainCamera,
                    Input.mousePosition,
                    out Vector3 pointerWorld))
            {
                return;
            }

            Vector3 pivotWorld = hasRotationPivotAnchor
                ? rotationPivotAnchorWorld
                : GetConfiguredRotationPivotWorldPosition();
            pointerPivotOffset = pivotWorld - pointerWorld;
            pointerPivotOffset.z = 0f;
        }

        public void UpdateForViewTransition(Vector3 pointerWorld)
        {
            if (!viewTransitionSuspended)
                return;

            if (currentState == BottleState.Returning)
            {
                MoveToPointerPosition(pointerWorld);
                return;
            }
            if (currentState != BottleState.PickedUp)
                return;

            Vector3 targetPivot = pointerWorld + pointerPivotOffset;
            Vector3 targetPosition = BartendingPointerAnchor.CalculateRootPosition(
                transform.position,
                GetConfiguredRotationPivotWorldPosition(),
                targetPivot);
            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body != null)
                body.position = targetPosition;
            else
                transform.position = targetPosition;
        }

        private void OnDisable()
        {
            CancelPointerSynchronization();
            ResetLiquidMouthKinematics();
            BartendingPointerAnchor.Release(this);
            BartendingSelection.Release(this);
        }

        // 기울기가 90도를 넘으면(옆으로 눕기 시작하면) 붓는 것으로 간주해 pourMlPerSecond 유량을
        // pourTimer 누적 방식으로 입자 스폰 타이밍으로 변환한다. 프레임 드랍 후 한꺼번에 몰아
        // 스폰되는 것을 막기 위해 한 프레임당 스폰 개수를 maxParticlesPerFrame으로 제한한다.
        private void HandlePouring()
        {
            float flowFactor = CalculatePourFlowFactor();
            if (flowFactor <= 0f || currentCapacity <= 0f)
            {
                pourTimer = 0f;
                return;
            }

            ILiquidSimulationBackend backend = LiquidSimulationRuntime.ActiveBackend;
            if (backend == null || !backend.IsOperational)
            {
                pourTimer = 0f;
                return;
            }

            pourTimer += Time.deltaTime;
            int emittedParticleCount = 0;
            int emissionLimit = Mathf.Max(1, maxParticlesPerFrame);
            float mlPerSecond = Mathf.Max(0.01f, pourMlPerSecond * flowFactor);

            while (currentCapacity > 0f && emittedParticleCount < emissionLimit)
            {
                float volumeMl = Mathf.Min(backend.DefaultParticleVolumeMl, currentCapacity);
                float emissionInterval = volumeMl / mlPerSecond;
                if (pourTimer < emissionInterval)
                    break;

                if (!TrySpawnLiquid(
                    volumeMl,
                    emittedParticleCount * emissionInterval))
                {
                    pourTimer = Mathf.Min(pourTimer, emissionInterval);
                    break;
                }

                pourTimer -= emissionInterval;
                emittedParticleCount++;
            }
        }

        private bool TrySpawnLiquid(
            float requestedVolumeMl,
            float streamOffsetSeconds)
        {
            ILiquidSimulationBackend backend = LiquidSimulationRuntime.ActiveBackend;
            if (backend == null
                || !backend.IsOperational
                || requestedVolumeMl <= 0f
                || currentCapacity <= 0f)
                return false;

            if (liquidSpawnPoint == null)
            {
                Debug.LogWarning("⚠️ Liquid Spawn Point가 인스펙터에 할당되지 않았습니다! 병의 중심(몸체)에서 스폰됩니다.");
            }

            Vector2 spawnPosition = liquidSpawnPoint != null
                ? liquidSpawnPoint.position
                : transform.position;
            Vector2 exitDirection = CalculateLiquidExitDirection(spawnPosition);
            Vector2 randomOffset = Vector2.zero;
            if (pourSpawnHalfWidth > 0f)
            {
                Vector2 nozzleDirection = Vector2.Perpendicular(exitDirection);
                randomOffset = nozzleDirection
                    * Random.Range(-pourSpawnHalfWidth, pourSpawnHalfWidth);
            }
            float flowFactor = Mathf.Max(minimumPourFlowFactor, CalculatePourFlowFactor());
            Vector2 inheritedMouthVelocity = Vector2.ClampMagnitude(
                liquidMouthVelocity,
                Mathf.Max(0f, maximumInheritedMouthSpeed));
            Vector2 initialVelocity = exitDirection
                * Mathf.Max(0f, pourExitSpeed)
                * Mathf.Lerp(0.72f, 1f, flowFactor)
                + inheritedMouthVelocity * Mathf.Clamp01(mouthVelocityInheritance);
            Vector2 streamOffset = initialVelocity
                * Mathf.Max(0f, streamOffsetSeconds);

            float volumeMl = Mathf.Min(requestedVolumeMl, currentCapacity);
            if (!backend.TryEmit(
                    spawnPosition + randomOffset + streamOffset,
                    initialVelocity,
                    bottleData,
                    volumeMl))
                return false;

            currentCapacity = Mathf.Max(0f, currentCapacity - volumeMl);
            initialCapacityOverride = currentCapacity;
            CapacityChanged?.Invoke(this, currentCapacity);
            return true;
        }

        private float CalculatePourFlowFactor()
        {
            float absoluteAngle = Mathf.Abs(currentAngle);
            float startAngle = Mathf.Clamp(pourStartAngle, 45f, 120f);
            if (absoluteAngle < startAngle)
                return 0f;

            float endAngle = Mathf.Max(startAngle + 0.1f, fullPourAngle);
            float angleProgress = Mathf.InverseLerp(startAngle, endAngle, absoluteAngle);
            return Mathf.Lerp(
                Mathf.Clamp(minimumPourFlowFactor, 0.1f, 1f),
                1f,
                angleProgress);
        }

        private Vector2 CalculateLiquidExitDirection(Vector2 spawnPosition)
        {
            Vector2 bottleCenter = spriteRenderer != null && spriteRenderer.sprite != null
                ? spriteRenderer.bounds.center
                : (Vector2)transform.position;
            Vector2 direction = spawnPosition - bottleCenter;
            if (direction.sqrMagnitude <= 0.000001f)
                direction = transform.up;
            return direction.normalized;
        }

        private void UpdateLiquidMouthKinematics()
        {
            Vector2 currentPosition = liquidSpawnPoint != null
                ? liquidSpawnPoint.position
                : transform.position;
            if (!hasLiquidMouthSample)
            {
                previousLiquidSpawnPosition = currentPosition;
                liquidMouthVelocity = Vector2.zero;
                hasLiquidMouthSample = true;
                return;
            }

            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            liquidMouthVelocity = (currentPosition - previousLiquidSpawnPosition)
                / deltaTime;
            previousLiquidSpawnPosition = currentPosition;
        }

        private void ResetLiquidMouthKinematics()
        {
            previousLiquidSpawnPosition = liquidSpawnPoint != null
                ? liquidSpawnPoint.position
                : transform.position;
            liquidMouthVelocity = Vector2.zero;
            hasLiquidMouthSample = true;
        }

        internal void SetStressTestPourPose(Vector2 desiredMouthPosition, float angle)
        {
            ReleaseBottle();
            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = transform.position;
                body.rotation = 0f;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
            else
            {
                transform.rotation = Quaternion.identity;
            }

            currentAngle = 0f;
            hasRotationPivotAnchor = false;
            ApplyRotationAroundConfiguredPivot(Mathf.Clamp(angle, -maxTiltAngle, maxTiltAngle));
            if (body != null)
            {
                transform.SetPositionAndRotation(
                    body.position,
                    Quaternion.Euler(0f, 0f, body.rotation));
            }
            Physics2D.SyncTransforms();

            Vector2 currentMouthPosition = liquidSpawnPoint != null
                ? liquidSpawnPoint.position
                : transform.position;
            Vector2 correction = desiredMouthPosition - currentMouthPosition;
            transform.position += (Vector3)correction;
            if (body != null)
                body.position = transform.position;
            Physics2D.SyncTransforms();

            ResetLiquidMouthKinematics();
        }

        internal Vector2 GetStressTestMouthPosition()
        {
            return liquidSpawnPoint != null
                ? liquidSpawnPoint.position
                : transform.position;
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
                else if (currentState == BottleState.PickedUp)
                {
                    TryDropBottle();
                }
            }

            if (pointerSyncPending)
                return;

            if (currentState == BottleState.PickedUp)
            {
                FollowMousePosition();
            }

            // Right Click (Tilt / Return)
            if (Input.GetMouseButtonDown(1))
            {
                if (currentState == BottleState.PickedUp)
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

            if (currentState == BottleState.Returning && !pointerSyncPending)
                FollowPointerWhileReturning();
        }

        private void PickupBottle()
        {
            if (!BartendingSelection.TryAcquire(this))
                return;

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

            BeginPointerSynchronization(
                GetConfiguredRotationPivotWorldPosition(),
                unlockCursor: false,
                completeReturn: false);
        }

        private void FollowMousePosition()
        {
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                return;
            }
            
            Vector3 targetPivot = mousePos + pointerPivotOffset;
            Vector3 targetPosition = BartendingPointerAnchor.CalculateRootPosition(
                transform.position,
                GetConfiguredRotationPivotWorldPosition(),
                targetPivot);

            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                // 댐핑을 걷어내고 0초 즉각 1:1 마우스 매핑 + 연속 물리(Sweep) 충돌 보장
                rb.MovePosition(targetPosition);
            }
            else
            {
                transform.position = targetPosition;
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

        // 기울이기 시작하는 순간 "축이 될 월드 좌표"와 "그 축에서 오브젝트 원점까지의 오프셋"을
        // 고정해둔다. 이후 ApplyRotationAroundConfiguredPivot은 이 오프셋을 회전시켜 원점 위치를
        // 재계산하므로, 축(rotationPivotAnchorWorld)은 화면에서 움직이지 않고 병 몸체만 그 주위로 돈다.
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

        private void PerformHorizontalRotationMovement()
        {
            if (currentState == BottleState.Tilting)
                return;

            Bounds bounds = col != null
                ? col.bounds
                : new Bounds(transform.position, Vector3.one);
            if (!BartendingPointerAnchor.TryGetHorizontalWorldDelta(
                    mainCamera,
                    bounds,
                    rotationHorizontalSensitivity,
                    rotationHorizontalScreenPadding,
                    out float worldDeltaX))
            {
                return;
            }

            rotationPivotAnchorWorld.x += worldDeltaX;
            ApplyRotationAroundConfiguredPivot(currentAngle);
        }

        private void FollowPointerWhileReturning()
        {
            if (!BartendingViewport.TryGetPointerWorldPosition(
                    mainCamera,
                    Input.mousePosition,
                    out Vector3 pointerWorld))
            {
                return;
            }

            MoveToPointerPosition(pointerWorld);
        }

        private void MoveToPointerPosition(Vector3 pointerWorld)
        {
            rotationPivotAnchorWorld = pointerWorld + pointerPivotOffset;
            rotationPivotAnchorWorld.z = 0f;
            ApplyRotationAroundConfiguredPivot(currentAngle);
        }

        private void TryDropBottle()
        {
            if (LiquorShelfUI.TryReturnHeldBottle(Input.mousePosition))
                return;

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

        public void PrepareForShelfReturn()
        {
            if (currentSlot != null)
            {
                currentSlot.Vacate();
                currentSlot = null;
            }

            ReleaseBottle();
            gameObject.SetActive(false);
        }

        private void ReleaseBottle()
        {
            currentState = BottleState.Idle;
            hasRotationPivotAnchor = false;
            CancelPointerSynchronization();
            BartendingPointerAnchor.Release(this);
            BartendingSelection.Release(this);
            
            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }
        }

        private void StartTilting()
        {
            if (!BartendingPointerAnchor.TryLock(this))
                return;

            currentState = BottleState.Tilting;
            initialAngle = currentAngle;
            CaptureRotationPivotAnchor();
            
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
            BeginPointerSynchronization(
                rotationPivotAnchorWorld,
                unlockCursor: true,
                completeReturn: false);
            returnCoroutine = StartCoroutine(ReturnToUprightRoutine());
        }

        private IEnumerator ReturnToUprightRoutine()
        {
            float startAngle = currentAngle;
            float timeElapsed = 0f;

            while (timeElapsed < returnSpeed)
            {
                if (viewTransitionSuspended)
                {
                    yield return null;
                    continue;
                }

                timeElapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(timeElapsed / returnSpeed);
                float curveValue = returnEase.Evaluate(normalizedTime);
                
                float targetAngle = Mathf.Lerp(startAngle, 0f, curveValue);
                ApplyRotationAroundConfiguredPivot(targetAngle);
                
                yield return null;
            }

            ApplyRotationAroundConfiguredPivot(0f);
            returnCoroutine = null;
            currentState = BottleState.PickedUp;
            hasRotationPivotAnchor = false;
        }

        private void BeginPointerSynchronization(
            Vector3 pivotWorld,
            bool unlockCursor,
            bool completeReturn)
        {
            pointerSyncPivotWorld = pivotWorld;
            pointerPivotOffset = Vector3.zero;
            completeReturnAfterPointerSync = completeReturn;

            bool requested = unlockCursor
                ? BartendingPointerAnchor.UnlockAndWarp(
                    this,
                    mainCamera,
                    pivotWorld,
                    out pointerSyncScreenPosition)
                : BartendingPointerAnchor.TryWarpToWorld(
                    mainCamera,
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
                    mainCamera,
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
            if (!success
                && BartendingViewport.TryGetPointerWorldPosition(
                    mainCamera,
                    Input.mousePosition,
                    out Vector3 pointerWorld))
            {
                pointerPivotOffset = pointerSyncPivotWorld - pointerWorld;
                pointerPivotOffset.z = 0f;
                Debug.LogWarning(
                    $"[{name}] Cursor warp was not confirmed; preserving the current grab offset.");
            }
            else if (success)
            {
                pointerPivotOffset = Vector3.zero;
            }

            if (completeReturnAfterPointerSync)
            {
                currentState = BottleState.PickedUp;
                hasRotationPivotAnchor = false;
            }

            completeReturnAfterPointerSync = false;
        }

        private void CancelPointerSynchronization()
        {
            pointerSyncPending = false;
            completeReturnAfterPointerSync = false;
            pointerSyncFramesRemaining = 0;
            pointerPivotOffset = Vector3.zero;
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
