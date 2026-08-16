using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

namespace Slainte.Bartending
{
    public interface IBartendingServeTarget
    {
        bool TryGetServeTargetScreenRect(out Rect screenRect);
    }

    public enum GlassState
    {
        Idle,
        PickedUp,
        Tilting,
        Returning,
        Snapping
    }

    [ExecuteInEditMode]
    [RequireComponent(typeof(EdgeCollider2D), typeof(Collider2D))]
    public class GlassController : MonoBehaviour, IBartendingItem
    {
        [Header("잔(Glass) 형태 및 실루엣 설정")]
        [Min(0.1f)] public float bottomWidth = 1.4f;
        [Min(0.1f)] public float topWidth = 1.8f;
        [Min(0.1f)] public float height = 2.4f;
        [Min(0f)] public float cornerRadius = 0.3f;
        [Range(4, 50)] public int curveSegments = 20;

        [Tooltip("잔의 곡선 단면 실루엣 왜곡율(곱셈자)을 결정하는 프로파일 커브입니다.\n수평 직선 1.0인 경우 완벽한 사다리꼴(비커형)이 됩니다.")]
        public AnimationCurve glassProfile = AnimationCurve.EaseInOut(0f, 1f, 1f, 1f);

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
        [SerializeField, Min(0f)] private float slotSnapDuration = 0.1f;
        [SerializeField] private LayerMask slotLayer;

        [Header("제출 조건")]
        [SerializeField] private string glassId = "rock";

        [Header("김 연출")]
        [SerializeField] private float steamStartTemperatureC = 55f;
        [SerializeField] private float steamStopTemperatureC = 48f;
        [SerializeField] private float steamTopOffset = 0.08f;
        [SerializeField] private float steamEmissionRate = 5f;

        [Header("물리 컴포넌트")]
        [SerializeField] private EdgeCollider2D edgeCollider;

        private GlassState currentState = GlassState.Idle;
        private Collider2D mainCollider; // 마우스 클릭용 터치 트리거 콜라이더
        private VesselLiquidTracker liquidTracker;
        private GlassSteamEmitter steamEmitter;
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
        private bool slotSnapActive;
        private Vector2 slotSnapStartPosition;
        private Vector2 slotSnapTargetPosition;
        private float slotSnapStartAngle;
        private float slotSnapElapsed;
        private bool serveGestureEnabled;
        private bool serveRequested;
        private IBartendingServeTarget serveTarget;
        private float rotationHorizontalSensitivity = 1f;
        private float rotationHorizontalScreenPadding = 12f;
        private BartendingItemOrder interactionOrder;
        private SlotController currentSlot; // 현재 점유 중인 슬롯 레퍼런스

        public VesselLiquidTracker LiquidTracker => liquidTracker;
        public event Action<GlassController> ServeRequested;

        public void ConfigureServeGesture(IBartendingServeTarget target)
        {
            serveTarget = target;
            serveGestureEnabled = target != null;
            serveRequested = false;
        }

        public void ConfigureHorizontalRotationMovement(float sensitivity, float screenPadding)
        {
            rotationHorizontalSensitivity = Mathf.Max(0f, sensitivity);
            rotationHorizontalScreenPadding = Mathf.Max(0f, screenPadding);
        }

        private void Start()
        {
            mainCollider = GetComponent<Collider2D>();
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

            if (Application.isPlaying)
            {
                currentState = GlassState.Idle;
                interactionOrder = BartendingItemOrder.Attach(
                    gameObject,
                    mainCollider,
                    liquidTracker);
            }
        }

        private void EnsureLiquidTracker()
        {
            liquidTracker = GetComponent<VesselLiquidTracker>();
            if (liquidTracker == null)
                liquidTracker = gameObject.AddComponent<VesselLiquidTracker>();

            liquidTracker.ConfigureServingStyle(glassId);

            steamEmitter = GetComponent<GlassSteamEmitter>();
            if (steamEmitter == null)
                steamEmitter = gameObject.AddComponent<GlassSteamEmitter>();
            steamEmitter.Initialize(
                liquidTracker,
                colliderYOffset + height * 0.5f + steamTopOffset,
                Mathf.Max(0.1f, topWidth * 0.65f),
                steamStartTemperatureC,
                steamStopTemperatureC,
                steamEmissionRate);

            if (!liquidTracker.HasTriggerCollider())
                Debug.LogWarning($"{name}에 액체 추적용 트리거 Collider2D가 필요합니다.");
        }

        public void SetContainsIce(bool containsIce)
        {
            liquidTracker?.SetHasIce(containsIce);
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

            if (!UpdatePointerSynchronization())
                HandleInput();
        }

        private void FixedUpdate()
        {
            if (!Application.isPlaying)
                return;

            if (slotSnapActive)
                ApplySlotSnapStep();
            else
                ApplyPendingPhysicsMotion();
        }

        private void OnDisable()
        {
            CancelPendingPhysicsMotion();
            slotSnapActive = false;
            CancelPointerSynchronization();
            BartendingPointerAnchor.Release(this);
        }

        private void HandleInput()
        {
            // Left Click (Pickup / Drop Toggle)
            if (Input.GetMouseButtonDown(0))
            {
                if (currentState == GlassState.Idle && IsMouseOverGlass())
                {
                    PickupGlass();
                }
                else if (currentState == GlassState.PickedUp)
                {
                    if (!TryRequestServe())
                        TryDropGlass();
                }
            }

            if (pointerSyncPending)
                return;

            // Follow Mouse (0초 무지연 1:1 매칭 & MovePosition 물리)
            if (currentState == GlassState.PickedUp)
            {
                FollowMousePosition();
            }

            // Right Click (Tilt / Return)
            if (Input.GetMouseButtonDown(1))
            {
                if (currentState == GlassState.PickedUp)
                {
                    StartTilting();
                }
            }
            else if (Input.GetMouseButtonUp(1))
            {
                if (currentState == GlassState.Tilting)
                {
                    StartReturning();
                }
            }

            // Perform Tilt (MoveRotation 물리)
            if (Input.GetMouseButton(1) && currentState == GlassState.Tilting)
            {
                PerformTilting();
            }

            if (currentState == GlassState.Tilting)
                PerformHorizontalRotationMovement();
            else if (currentState == GlassState.Returning && !pointerSyncPending)
                FollowMousePosition();
        }

        private void PickupGlass()
        {
            CancelPendingPhysicsMotion();
            currentState = GlassState.PickedUp;
            interactionOrder?.BringToFront();
            
            // 기존 슬롯 점유 해제 (독립)
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

            BeginPointerSynchronization(
                transform.position,
                unlockCursor: false,
                completeReturn: false);
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

        private bool TryRequestServe()
        {
            Vector3 center = body != null
                ? (Vector3)body.position
                : transform.position;
            Vector2 glassScreenPosition = BartendingViewport.GetPointerScreenPosition(
                mainCamera,
                center);
            return TryRequestServeAtScreenPosition(
                glassScreenPosition,
                true);
        }

        public bool TryRequestServeAtScreenPosition(Vector2 glassScreenPosition, bool isClick)
        {
            if (!serveGestureEnabled
                || serveRequested
                || !isClick
                || serveTarget == null
                || !serveTarget.TryGetServeTargetScreenRect(out Rect targetRect)
                || !targetRect.Contains(glassScreenPosition))
            {
                return false;
            }

            serveRequested = true;
            ReleaseGlass();
            ServeRequested?.Invoke(this);
            return true;
        }

        private void TryDropGlass()
        {
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
                        BeginSlotSnap(
                            new Vector3(hit.transform.position.x, hit.transform.position.y + bottomOffset, 0f),
                            slot);
                        return;
                    }
                }
                else
                {
                    // 슬롯 스냅 안착 (하위 호환용)
                    float bottomOffset = GetPivotToBottomOffset();
                    BeginSlotSnap(
                        new Vector3(hit.transform.position.x, hit.transform.position.y + bottomOffset, 0f),
                        null);
                    return;
                }
            }
        }

        // IBartendingItem 인터페이스 완벽 구현부
        public GameObject GameObject => gameObject;
        public bool IsPickedUp => currentState == GlassState.PickedUp || currentState == GlassState.Tilting || currentState == GlassState.Returning;

        public void SnapToSlot(Transform slotTransform, SlotController slot)
        {
            float bottomOffset = GetPivotToBottomOffset();
            BeginSlotSnap(
                new Vector3(slotTransform.position.x, slotTransform.position.y + bottomOffset, 0f),
                slot);
        }

        private void BeginSlotSnap(Vector3 targetPosition, SlotController slot)
        {
            CancelPendingPhysicsMotion();
            CancelPointerSynchronization();
            BartendingPointerAnchor.Release(this);

            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }

            if (currentSlot != null && currentSlot != slot)
                currentSlot.Vacate();

            currentSlot = slot;
            if (slot != null && !ReferenceEquals(slot.OccupiedItem, this))
                slot.Occupy(this);

            targetPosition.z = 0f;
            if (!Application.isPlaying || body == null || slotSnapDuration <= Mathf.Epsilon)
            {
                slotSnapActive = false;
                transform.position = targetPosition;
                SetRotationImmediately(0f);
                currentState = GlassState.Idle;
                return;
            }

            slotSnapStartPosition = body.position;
            slotSnapTargetPosition = targetPosition;
            slotSnapStartAngle = body.rotation;
            slotSnapElapsed = 0f;
            slotSnapActive = true;
            currentState = GlassState.Snapping;
        }

        private void ApplySlotSnapStep()
        {
            if (body == null)
            {
                slotSnapActive = false;
                currentState = GlassState.Idle;
                return;
            }

            slotSnapElapsed += Time.fixedDeltaTime;
            float normalizedTime = Mathf.Clamp01(slotSnapElapsed / Mathf.Max(slotSnapDuration, Mathf.Epsilon));
            float easedTime = normalizedTime * normalizedTime * (3f - 2f * normalizedTime);
            body.MovePosition(Vector2.Lerp(slotSnapStartPosition, slotSnapTargetPosition, easedTime));
            currentAngle = Mathf.LerpAngle(slotSnapStartAngle, 0f, easedTime);
            body.MoveRotation(currentAngle);

            if (normalizedTime >= 1f)
            {
                currentAngle = 0f;
                slotSnapActive = false;
                currentState = GlassState.Idle;
            }
        }

        public void OnPickedUp()
        {
            PickupGlass();
        }

        public void OnDropped()
        {
            TryDropGlass();
        }

        private void ReleaseGlass()
        {
            currentState = GlassState.Idle;
            slotSnapActive = false;
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
            currentState = GlassState.Tilting;
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
            currentState = GlassState.Returning;
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
            currentState = GlassState.PickedUp;
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
                body.rotation = targetAngle;
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
                currentState = GlassState.PickedUp;

            completeReturnAfterPointerSync = false;
        }

        private void CancelPointerSynchronization()
        {
            pointerSyncPending = false;
            completeReturnAfterPointerSync = false;
            pointerSyncFramesRemaining = 0;
            pointerPivotOffset = Vector3.zero;
        }

        private bool IsMouseOverGlass()
        {
            if (mainCollider == null) return false;
            
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                return false;
            }

            // 다른 겹치는 오브젝트(액체 입자 등)에 방해받지 않는 단독 격리 판정
            return interactionOrder != null
                ? interactionOrder.IsFrontmostAt(mousePos)
                : mainCollider.OverlapPoint(mousePos);
        }

        private float GetPivotToBottomOffset()
        {
            // Y 오프셋 보정 수식 (Y축 위로 올리면 피벗에서 하단 바닥까지의 물리 실측 거리는 그만큼 줄어듦)
            return (height / 2f) - colliderYOffset;
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

            // 1. 왼쪽 단면 생성 (Y축 상단에서 하단으로 샘플링 순회)
            for (int i = curveSegments; i >= 0; i--)
            {
                float t = (float)i / curveSegments;
                float y = -halfH + (height * t);
                
                // 사다리꼴 단면 선형 보간 너비
                float rBase = Mathf.Lerp(halfBottom, halfTop, t);
                // AnimationCurve 곡률 스케일 적용
                float rFinal = rBase * (glassProfile != null ? glassProfile.Evaluate(t) : 1f);
                
                float x = -rFinal;

                // [바닥 모서리 둥글기 원호 보정 적용]
                if (actualRadius > 0.001f && y < -halfH + actualRadius)
                {
                    float dy = y - (-halfH + actualRadius); // 음수 높이차
                    float cx = -halfBottom + actualRadius;
                    float xArc = cx - Mathf.Sqrt(actualRadius * actualRadius - dy * dy);
                    
                    x = Mathf.Max(x, xArc); // 더 안쪽으로 들어오는 값을 채택하여 모서리를 둥글게 함
                }

                // 콜라이더 Y축 상하 미세 평행이동 오프셋 보정 주입
                points.Add(new Vector2(x, y + colliderYOffset));
            }

            // 2. 오른쪽 단면 생성 (Y축 하단에서 상단으로 샘플링 대칭 순회)
            for (int i = 0; i <= curveSegments; i++)
            {
                float t = (float)i / curveSegments;
                float y = -halfH + (height * t);
                
                float rBase = Mathf.Lerp(halfBottom, halfTop, t);
                float rFinal = rBase * (glassProfile != null ? glassProfile.Evaluate(t) : 1f);
                
                float x = rFinal;

                if (actualRadius > 0.001f && y < -halfH + actualRadius)
                {
                    float dy = y - (-halfH + actualRadius);
                    float cx = halfBottom - actualRadius;
                    float xArc = cx + Mathf.Sqrt(actualRadius * actualRadius - dy * dy);
                    
                    x = Mathf.Min(x, xArc);
                }

                // 콜라이더 Y축 상하 미세 평행이동 오프셋 보정 주입
                points.Add(new Vector2(x, y + colliderYOffset));
            }

            edgeCollider.SetPoints(points);
        }
    }
}
