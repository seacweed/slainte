using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    public enum BeakerState
    {
        Idle,
        PickedUp,
        Tilting,
        Returning
    }

    [ExecuteInEditMode]
    [RequireComponent(typeof(EdgeCollider2D), typeof(Collider2D))]
    public class BeakerController : MonoBehaviour, IBartendingItem, IPointerAnchoredPickup,
        IBartendingViewTransitionParticipant
    {
        [Header("비커 크기 및 형태 설정")]
        [Min(0.1f)] public float bottomWidth = 1.4f;
        [Min(0.1f)] public float topWidth = 1.8f;
        [Min(0.1f)] public float height = 2.4f;
        [Min(0f)] public float cornerRadius = 0.3f;
        [Range(2, 30)] public int curveSegments = 12;

        [Header("콜라이더 미세 오프셋 조절")]
        [Tooltip("생성되는 콜라이더 전체를 로컬 Y축 상하 방향으로 자유롭게 평행 이동시킵니다.")]
        public float colliderYOffset = 0f;

        [Header("물리 및 최적화 설정")]
        [Tooltip("콜라이더 자체에 두께를 부여하여 Discrete(이산) 충돌 감지 하에서도 액체 입자가 관통(뚫림)하는 현상을 원천 방지합니다.")]
        [Range(0f, 0.2f)] public float edgeRadius = 0.08f; 

        [Header("인터랙션 설정")]
        [SerializeField] private float maxTiltAngle = 120f;
        [SerializeField] private float tiltSensitivity = 0.5f;
        [SerializeField] private float returnSpeed = 0.8f;
        [SerializeField] private AnimationCurve returnEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private LayerMask slotLayer;

        [Header("물리 컴포넌트")]
        [SerializeField] private EdgeCollider2D edgeCollider;

        private BeakerState currentState = BeakerState.Idle;
        private Collider2D mainCollider; // 부모 드래그 터치 감지용 트리거 콜라이더
        private VesselLiquidTracker liquidTracker;
        private Camera mainCamera;
        private Rigidbody2D body;
        
        private float initialAngle;
        private float currentAngle = 0f;
        private Coroutine returnCoroutine;
        private const int PointerSyncFrameBudget = 6;
        private bool pointerSyncPending;
        private bool completeReturnAfterPointerSync;
        private int pointerSyncFramesRemaining;
        private Vector2 pointerSyncScreenPosition;
        private Vector3 pointerSyncPivotWorld;
        private Vector3 pointerPivotOffset;
        private bool positionTargetPending;
        private Vector2 pendingPositionTarget;
        private bool rotationTargetPending;
        private float pendingRotationTarget;
        private float rotationHorizontalSensitivity = 1f;
        private float rotationHorizontalScreenPadding = 12f;

        private SlotController currentSlot; // 현재 점유 중인 슬롯 레퍼런스
        private bool viewTransitionSuspended;
        private Vector3 dragVelocity = Vector3.zero;

        private SpriteRenderer[] childRenderers;
        private int[] originalSortingOrders;
        private const int PICKUP_SORTING_ORDER_BASE = 100;
        private BartendingItemOrder interactionOrder;
        private System.Func<Vector2, bool> customInteractionContains;

        public VesselLiquidTracker LiquidTracker => liquidTracker;

        public void ConfigureMaximumTiltAngle(float maximumAngle)
        {
            maxTiltAngle = Mathf.Max(0f, maximumAngle);
        }

        public void ConfigureHorizontalRotationMovement(float sensitivity, float screenPadding)
        {
            rotationHorizontalSensitivity = Mathf.Max(0f, sensitivity);
            rotationHorizontalScreenPadding = Mathf.Max(0f, screenPadding);
        }

        public void ConfigureCollisionGeometry(
            float configuredBottomWidth,
            float configuredTopWidth,
            float configuredHeight,
            float configuredYOffset,
            Vector2 triggerSize,
            Vector2 triggerOffset)
        {
            bottomWidth = Mathf.Max(0.05f, configuredBottomWidth);
            topWidth = Mathf.Max(0.05f, configuredTopWidth);
            height = Mathf.Max(0.05f, configuredHeight);
            colliderYOffset = configuredYOffset;

            BoxCollider2D boxCollider = GetComponent<BoxCollider2D>();
            if (boxCollider != null)
            {
                mainCollider = boxCollider;
                boxCollider.isTrigger = true;
                boxCollider.size = new Vector2(
                    Mathf.Max(0.05f, triggerSize.x),
                    Mathf.Max(0.05f, triggerSize.y));
                boxCollider.offset = triggerOffset;
            }

            edgeCollider ??= GetComponent<EdgeCollider2D>();
            GenerateCurvedCollider();
            liquidTracker?.RefreshCollisionGeometry();
        }

        public void ConfigureCustomCollisionGeometry(
            IReadOnlyList<Vector2> edgePoints,
            float configuredEdgeRadius,
            System.Func<Vector2, bool> interactionContains)
        {
            if (edgePoints == null || edgePoints.Count < 3)
                return;

            edgeCollider ??= GetComponent<EdgeCollider2D>();
            if (edgeCollider == null)
                edgeCollider = gameObject.AddComponent<EdgeCollider2D>();

            List<Vector2> points = new List<Vector2>(edgePoints.Count);
            for (int i = 0; i < edgePoints.Count; i++)
                points.Add(edgePoints[i]);

            edgeCollider.enabled = true;
            edgeCollider.isTrigger = false;
            edgeCollider.edgeRadius = Mathf.Max(0f, configuredEdgeRadius);
            edgeCollider.SetPoints(points);

            BoxCollider2D[] rootBoxes = GetComponents<BoxCollider2D>();
            for (int i = 0; i < rootBoxes.Length; i++)
                rootBoxes[i].enabled = false;

            mainCollider = edgeCollider;
            customInteractionContains = interactionContains;
            if (Application.isPlaying)
            {
                interactionOrder = BartendingItemOrder.Attach(
                    gameObject,
                    mainCollider,
                    liquidTracker,
                    ContainsInteractionPoint);
            }
            liquidTracker?.RefreshCollisionGeometry();
        }

        private void Start()
        {
            if (mainCollider == null)
            {
                mainCollider = GetComponent<BoxCollider2D>();
                mainCollider ??= GetComponent<Collider2D>();
            }
            mainCamera = Camera.main;

            if (Application.isPlaying)
            {
                // 1. Rigidbody2D 키네마틱 물리 셋업 강제 보장 (2D 물리 트리거 상호작용 완벽 복구)
                EnsureLiquidTracker();

                body = GetComponent<Rigidbody2D>();
                if (body == null)
                {
                    body = gameObject.AddComponent<Rigidbody2D>();
                }
                body.bodyType = RigidbodyType2D.Kinematic;
                body.useFullKinematicContacts = true;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;

                // 2. slotLayer가 빈 값이거나 Nothing일 시 스마트 자동 보정
                if (slotLayer.value == 0)
                {
                    int layerIdx = LayerMask.NameToLayer("Slot");
                    if (layerIdx == -1) layerIdx = 0;
                    slotLayer = 1 << layerIdx;
                }
            }

            // 자식 SpriteRenderer 캐싱
            childRenderers = GetComponentsInChildren<SpriteRenderer>();
            originalSortingOrders = new int[childRenderers.Length];
            for (int i = 0; i < childRenderers.Length; i++)
            {
                originalSortingOrders[i] = childRenderers[i].sortingOrder;
            }

            if (Application.isPlaying)
            {
                currentState = BeakerState.Idle;
                interactionOrder = BartendingItemOrder.Attach(
                    gameObject,
                    mainCollider,
                    liquidTracker,
                    ContainsInteractionPoint);
            }
        }

        private void EnsureLiquidTracker()
        {
            liquidTracker = GetComponent<VesselLiquidTracker>();
            if (liquidTracker == null)
                liquidTracker = gameObject.AddComponent<VesselLiquidTracker>();

            if (!liquidTracker.HasTriggerCollider())
                Debug.LogWarning($"{name}에 액체 추적용 트리거 Collider2D가 필요합니다.");
        }

        private void Reset()
        {
            edgeCollider = GetComponent<EdgeCollider2D>();
            GenerateCurvedCollider();
        }

        private void OnValidate()
        {
            GenerateCurvedCollider();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            if (viewTransitionSuspended) return;

            if (!UpdatePointerSynchronization())
                HandleInput();
        }

        private void FixedUpdate()
        {
            if (!Application.isPlaying)
                return;
            if (viewTransitionSuspended)
                return;

            ApplyPendingPhysicsMotion();
        }

        public void SuspendForViewTransition()
        {
            if (viewTransitionSuspended)
                return;

            viewTransitionSuspended = true;
            liquidTracker?.BeginExternalMotion();
            CancelPendingPhysicsMotion();
            pointerSyncPending = false;
            completeReturnAfterPointerSync = false;
            pointerSyncFramesRemaining = 0;
        }

        public void ResumeAfterViewTransition()
        {
            if (!viewTransitionSuspended)
                return;

            CancelPendingPhysicsMotion();
            liquidTracker?.EndExternalMotion();
            viewTransitionSuspended = false;
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

            Vector3 pivotWorld = body != null
                ? (Vector3)body.position
                : transform.position;
            pointerPivotOffset = pivotWorld - pointerWorld;
            pointerPivotOffset.z = 0f;
        }

        public void UpdateForViewTransition(Vector3 pointerWorld)
        {
            if (!viewTransitionSuspended
                || (currentState != BeakerState.PickedUp
                    && currentState != BeakerState.Returning))
            {
                return;
            }

            Vector3 targetPivot = pointerWorld + pointerPivotOffset;
            Vector3 targetPosition = BartendingPointerAnchor.CalculateRootPosition(
                transform.position,
                transform.position,
                targetPivot);
            MoveVesselAndContents(targetPosition);
        }

        private void OnDisable()
        {
            CancelPendingPhysicsMotion();
            CancelPointerSynchronization();
            BartendingPointerAnchor.Release(this);
        }

        private void HandleInput()
        {
            // Left Click (Pickup / Drop Toggle)
            if (Input.GetMouseButtonDown(0))
            {
                CobblerShakerPresentation shakerPresentation =
                    GetComponent<CobblerShakerPresentation>();
                if (currentState == BeakerState.Idle
                    && shakerPresentation != null
                    && shakerPresentation.TryHandlePartClick(mainCamera, Input.mousePosition))
                {
                    return;
                }

                if (currentState == BeakerState.Idle && IsMouseOverBeaker())
                {
                    PickupBeaker();
                }
                else if (currentState == BeakerState.PickedUp)
                {
                    TryDropBeaker();
                }
            }

            if (pointerSyncPending)
                return;

            // Follow Mouse
            if (currentState == BeakerState.PickedUp)
            {
                FollowMousePosition();
            }

            // Right Click (Tilt / Return)
            if (Input.GetMouseButtonDown(1))
            {
                if (currentState == BeakerState.PickedUp)
                {
                    StartTilting();
                }
            }
            else if (Input.GetMouseButtonUp(1))
            {
                if (currentState == BeakerState.Tilting)
                {
                    StartReturning();
                }
            }

            // Perform Tilt
            if (Input.GetMouseButton(1) && currentState == BeakerState.Tilting)
            {
                PerformTilting();
            }

            if (currentState == BeakerState.Returning && !pointerSyncPending)
                FollowMousePosition();
        }

        private void PickupBeaker()
        {
            PreparePickup();
            BeginPointerSynchronization(
                transform.position,
                unlockCursor: false,
                completeReturn: false);
        }

        private void PreparePickup()
        {
            CancelPointerSynchronization();
            CancelPendingPhysicsMotion();
            currentState = BeakerState.PickedUp;
            interactionOrder?.BringToFront();
            
            // 기존 슬롯 점유 해제
            if (currentSlot != null)
            {
                currentSlot.Vacate();
                currentSlot = null;
            }

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

            MoveToPointerPosition(mousePos);
        }

        private void MoveToPointerPosition(Vector3 pointerWorld)
        {
            Vector3 targetPivot = pointerWorld + pointerPivotOffset;
            Vector3 targetPosition = BartendingPointerAnchor.CalculateRootPosition(
                transform.position,
                transform.position,
                targetPivot);
            QueuePositionTarget(targetPosition);
        }

        private float GetPivotToBottomOffset()
        {
            float visibleBottom = float.PositiveInfinity;
            foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.sprite != null && renderer.gameObject.activeInHierarchy)
                {
                    visibleBottom = Mathf.Min(visibleBottom, renderer.bounds.min.y);
                }
            }

            if (!float.IsPositiveInfinity(visibleBottom))
            {
                return transform.position.y - visibleBottom;
            }

            return ((height / 2f) - colliderYOffset) * Mathf.Abs(transform.lossyScale.y);
        }

        private void TryDropBeaker()
        {
            if (ToolCabinetController.TryReturnHeldItem(this, mainCamera, Input.mousePosition))
                return;

            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                return;
            }

            Collider2D[] hits = Physics2D.OverlapPointAll(mousePos, slotLayer);

            foreach (var hit in hits)
            {
                // 자기 자신 콜라이더는 건너뜁니다.
                if (hit == mainCollider || hit == edgeCollider) continue;

                SlotController slot = hit.GetComponent<SlotController>();
                if (slot != null)
                {
                    // 슬롯이 비어있는 경우에만 안착 허용
                    if (!slot.IsOccupied)
                    {
                        // 슬롯 스냅 안착 (바닥면 Y 오프셋 칼각 정렬!)
                        float bottomOffset = GetPivotToBottomOffset();
                        SnapVesselAndContentsToSlot(
                            new Vector3(hit.transform.position.x, hit.transform.position.y + bottomOffset, 0f),
                            slot);
                        return;
                    }
                }
                else
                {
                    // 슬롯 스냅 안착 (하위 호환용)
                    float bottomOffset = GetPivotToBottomOffset();
                    SnapVesselAndContentsToSlot(
                        new Vector3(hit.transform.position.x, hit.transform.position.y + bottomOffset, 0f),
                        null);
                    return;
                }
            }
        }

        // IBartendingItem 인터페이스 완벽 구현부
        public GameObject GameObject => gameObject;
        public bool IsPickedUp => currentState == BeakerState.PickedUp || currentState == BeakerState.Tilting || currentState == BeakerState.Returning;

        public void SnapToSlot(Transform slotTransform, SlotController slot)
        {
            float bottomOffset = GetPivotToBottomOffset();
            SnapVesselAndContentsToSlot(
                new Vector3(slotTransform.position.x, slotTransform.position.y + bottomOffset, 0f),
                slot);
        }

        private void SnapVesselAndContentsToSlot(Vector3 targetPosition, SlotController slot)
        {
            if (currentSlot != null && currentSlot != slot)
                currentSlot.Vacate();

            currentSlot = slot;
            if (slot != null && !ReferenceEquals(slot.OccupiedItem, this))
                slot.Occupy(this);

            MoveVesselAndContents(targetPosition);
            SetRotationImmediately(0f);
            ReleaseBeaker();
        }

        private void MoveVesselAndContents(Vector3 targetPosition)
        {
            CancelPendingPhysicsMotion();
            targetPosition.z = 0f;
            Vector2 currentPosition = body != null
                ? body.position
                : (Vector2)transform.position;
            liquidTracker?.TranslateTrackedParticles((Vector2)targetPosition - currentPosition);

            if (body != null)
            {
                body.position = targetPosition;
                transform.position = new Vector3(
                    targetPosition.x,
                    targetPosition.y,
                    transform.position.z);
                body.linearVelocity = Vector2.zero;
                body.MovePosition(targetPosition);
            }
            else
                transform.position = targetPosition;
        }

        public void OnPickedUp()
        {
            PickupBeaker();
        }

        public void OnPickedUpAt(Vector3 pointerWorld)
        {
            PreparePickup();
            pointerPivotOffset = transform.position - pointerWorld;
            pointerPivotOffset.z = 0f;
        }

        public void OnDropped()
        {
            TryDropBeaker();
        }

        private void ReleaseBeaker()
        {
            currentState = BeakerState.Idle;
            CancelPendingPhysicsMotion();
            CancelPointerSynchronization();
            BartendingPointerAnchor.Release(this);
            
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

            CancelPendingPhysicsMotion();
            currentState = BeakerState.Tilting;
            initialAngle = currentAngle;

            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }
        }

        private void PerformTilting()
        {
            float deltaY = Input.GetAxis("Mouse Y");
            currentAngle += deltaY * tiltSensitivity * 10f;
            currentAngle = Mathf.Clamp(currentAngle, -maxTiltAngle, maxTiltAngle);
            QueueRotationTarget(currentAngle);
        }

        private void PerformHorizontalRotationMovement()
        {
            Bounds bounds = mainCollider != null
                ? mainCollider.bounds
                : new Bounds(transform.position, Vector3.one);
            if (body != null && positionTargetPending)
                bounds.center += (Vector3)(pendingPositionTarget - body.position);

            if (!BartendingPointerAnchor.TryGetHorizontalWorldDelta(
                    mainCamera,
                    bounds,
                    rotationHorizontalSensitivity,
                    rotationHorizontalScreenPadding,
                    out float worldDeltaX))
            {
                return;
            }

            Vector2 currentPosition = positionTargetPending
                ? pendingPositionTarget
                : body != null
                    ? body.position
                    : (Vector2)transform.position;
            QueuePositionTarget(currentPosition + new Vector2(worldDeltaX, 0f));
        }

        private void StartReturning()
        {
            currentState = BeakerState.Returning;
            Vector3 pointerAnchor = positionTargetPending
                ? (Vector3)pendingPositionTarget
                : body != null
                    ? (Vector3)body.position
                    : transform.position;
            BeginPointerSynchronization(
                pointerAnchor,
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
                
                currentAngle = Mathf.Lerp(startAngle, 0f, curveValue);
                QueueRotationTarget(currentAngle);
                
                yield return null;
            }

            currentAngle = 0f;
            QueueRotationTarget(0f);

            while (body != null
                && (rotationTargetPending
                    || Mathf.Abs(Mathf.DeltaAngle(body.rotation, 0f)) > 0.05f))
            {
                yield return new WaitForFixedUpdate();
            }

            yield return null;

            returnCoroutine = null;
            currentState = BeakerState.PickedUp;
        }

        private void QueuePositionTarget(Vector3 targetPosition)
        {
            targetPosition.z = 0f;
            if (body == null)
            {
                transform.position = targetPosition;
                return;
            }

            pendingPositionTarget = targetPosition;
            positionTargetPending = true;
        }

        private void QueueRotationTarget(float targetAngle)
        {
            if (body == null)
            {
                transform.rotation = Quaternion.Euler(0f, 0f, targetAngle);
                return;
            }

            pendingRotationTarget = targetAngle;
            rotationTargetPending = true;
        }

        private void ApplyPendingPhysicsMotion()
        {
            if (body == null)
            {
                CancelPendingPhysicsMotion();
                return;
            }

            if (positionTargetPending)
            {
                body.MovePosition(pendingPositionTarget);
                positionTargetPending = false;
            }

            if (rotationTargetPending)
            {
                body.MoveRotation(pendingRotationTarget);
                rotationTargetPending = false;
            }
        }

        private void SetRotationImmediately(float targetAngle)
        {
            rotationTargetPending = false;
            currentAngle = targetAngle;
            if (body != null)
            {
                body.rotation = targetAngle;
                transform.rotation = Quaternion.Euler(0f, 0f, targetAngle);
                body.angularVelocity = 0f;
                body.MoveRotation(targetAngle);
            }
            else
                transform.rotation = Quaternion.Euler(0f, 0f, targetAngle);
        }

        private void CancelPendingPhysicsMotion()
        {
            positionTargetPending = false;
            rotationTargetPending = false;
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
                currentState = BeakerState.PickedUp;

            completeReturnAfterPointerSync = false;
        }

        private void CancelPointerSynchronization()
        {
            pointerSyncPending = false;
            completeReturnAfterPointerSync = false;
            pointerSyncFramesRemaining = 0;
            pointerPivotOffset = Vector3.zero;
        }

        private bool IsMouseOverBeaker()
        {
            if (mainCollider == null) return false;
            
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                return false;
            }

            return interactionOrder != null
                ? interactionOrder.IsFrontmostAt(mousePos)
                : ContainsInteractionPoint(mousePos);
        }

        private bool ContainsInteractionPoint(Vector2 worldPoint)
        {
            if (customInteractionContains != null)
                return customInteractionContains(worldPoint);

            return mainCollider != null
                && mainCollider.enabled
                && mainCollider.gameObject.activeInHierarchy
                && mainCollider.OverlapPoint(worldPoint);
        }

        public void GenerateCurvedCollider()
        {
            if (edgeCollider == null)
            {
                edgeCollider = GetComponent<EdgeCollider2D>();
                if (edgeCollider == null) return;
            }

            edgeCollider.edgeRadius = edgeRadius;

            float halfBottom = bottomWidth / 2f;
            float halfTop = topWidth / 2f;
            float halfH = height / 2f;
            
            float actualRadius = Mathf.Clamp(cornerRadius, 0f, Mathf.Min(halfBottom, halfH));

            List<Vector2> points = new List<Vector2>();

            points.Add(new Vector2(-halfTop, halfH));

            if (actualRadius > 0.001f)
            {
                float wallLength = Mathf.Sqrt(height * height + (halfTop - halfBottom) * (halfTop - halfBottom));
                float factor = (wallLength - (halfTop - halfBottom)) / height;
                
                float cx = -halfBottom + actualRadius * factor;
                float cy = -halfH + actualRadius;
                Vector2 centerL = new Vector2(cx, cy);

                float wallDx = halfBottom - halfTop;
                float wallDy = height;
                float phi = Mathf.Atan2(wallDy, wallDx);

                float startAngleRad = phi + Mathf.PI / 2f;

                float startAngleDeg = startAngleRad * Mathf.Rad2Deg;
                float endAngleDeg = 270f;

                Vector2 contactL = centerL + new Vector2(Mathf.Cos(startAngleRad) * actualRadius, Mathf.Sin(startAngleRad) * actualRadius);
                points.Add(contactL);

                for (int i = 0; i <= curveSegments; i++)
                {
                    float t = (float)i / curveSegments;
                    float angle = Mathf.Lerp(startAngleDeg, endAngleDeg, t) * Mathf.Deg2Rad;
                    points.Add(centerL + new Vector2(Mathf.Cos(angle) * actualRadius, Mathf.Sin(angle) * actualRadius));
                }

                Vector2 centerR = new Vector2(-centerL.x, centerL.y);
                
                float startAngleDegR = 270f;
                float endAngleDegR = 540f - startAngleDeg;

                for (int i = 0; i <= curveSegments; i++)
                {
                    float t = (float)i / curveSegments;
                    float angle = Mathf.Lerp(startAngleDegR, endAngleDegR, t) * Mathf.Deg2Rad;
                    points.Add(centerR + new Vector2(Mathf.Cos(angle) * actualRadius, Mathf.Sin(angle) * actualRadius));
                }

                Vector2 contactR = centerR + new Vector2(Mathf.Cos(endAngleDegR * Mathf.Deg2Rad) * actualRadius, Mathf.Sin(endAngleDegR * Mathf.Deg2Rad) * actualRadius);
                points.Add(contactR);
            }
            else
            {
                points.Add(new Vector2(-halfBottom, -halfH));
                points.Add(new Vector2(halfBottom, -halfH));
            }

            points.Add(new Vector2(halfTop, halfH));

            // Y축 상하 미세 평행이동 오프셋 보정 일괄 적용 (Post-processing)
            for (int i = 0; i < points.Count; i++)
            {
                points[i] = new Vector2(points[i].x, points[i].y + colliderYOffset);
            }

            edgeCollider.SetPoints(points);
        }
    }
}
