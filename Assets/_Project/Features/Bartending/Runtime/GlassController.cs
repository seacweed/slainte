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
        Returning
    }

    // 플레이어가 드래그해서 드는 잔(Glass) 오브젝트. 상태는 Idle → PickedUp → Tilting → Returning으로
    // 순환하며, 실제 충돌 형태(EdgeCollider2D)는 스프라이트별 GlassCollisionProfileDefinition을
    // 자동 감지해 곡선 실루엣과 내용물 감지용 트리거를 함께 생성한다(GenerateCurvedCollider/ApplyCollisionProfile).
    [ExecuteInEditMode]
    [RequireComponent(typeof(EdgeCollider2D), typeof(Collider2D))]
    public class GlassController : MonoBehaviour, IBartendingItem, IPointerAnchoredPickup,
        IBartendingViewTransitionParticipant
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
        [SerializeField] private LayerMask slotLayer;

        [Header("제출 조건")]
        [SerializeField] private string glassId = "rock";
        [SerializeField, Min(1f)] private float capacityMl = 200f;

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
        private bool serveGestureEnabled;
        private bool serveRequested;
        private IBartendingServeTarget serveTarget;
        private float rotationHorizontalSensitivity = 1f;
        private float rotationHorizontalScreenPadding = 12f;
        private BartendingItemOrder interactionOrder;
        private SlotController currentSlot; // 현재 점유 중인 슬롯 레퍼런스
        private GlassCollisionProfileDefinition activeCollisionProfile;
        private SpriteRenderer collisionVisual;
        private bool viewTransitionSuspended;

        private const string DefaultGlassId = "rock";
        private const string ContentTriggerPrefix = "__GlassContentTrigger_";

        public VesselLiquidTracker LiquidTracker => liquidTracker;
        public string GlassId => string.IsNullOrWhiteSpace(glassId) ? DefaultGlassId : glassId.Trim();
        public float CapacityMl => Mathf.Max(1f, capacityMl);
        public GlassCollisionProfileDefinition ActiveCollisionProfile => activeCollisionProfile;
        public event Action<GlassController> ServeRequested;
        public event Action<GlassController, bool> HeldStateChanged;

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

        public void ConfigureServingIdentity(string servingGlassId, float servingCapacityMl)
        {
            glassId = string.IsNullOrWhiteSpace(servingGlassId)
                ? DefaultGlassId
                : servingGlassId.Trim();
            capacityMl = Mathf.Max(1f, servingCapacityMl);
            if (Application.isPlaying)
                EnsureLiquidTracker();
        }

        private void Start()
        {
            TryApplyDetectedCollisionProfile(true);
            if (mainCollider == null)
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
                    liquidTracker,
                    ContainsInteractionPoint);
            }
        }

        private void EnsureLiquidTracker()
        {
            liquidTracker = GetComponent<VesselLiquidTracker>();
            if (liquidTracker == null)
                liquidTracker = gameObject.AddComponent<VesselLiquidTracker>();

            glassId = GlassId;
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

        public bool CanContain(float volumeMl)
        {
            return volumeMl >= 0f && volumeMl <= CapacityMl + 0.001f;
        }

        public bool TryApplyDetectedCollisionProfile()
        {
            return TryApplyDetectedCollisionProfile(true);
        }

        public void ApplyCollisionProfile(
            GlassCollisionProfileDefinition profile,
            SpriteRenderer visual)
        {
            ApplyCollisionProfile(profile, visual, true);
        }

        private void Reset()
        {
            edgeCollider = GetComponent<EdgeCollider2D>();
            GenerateCurvedCollider();
        }

        private void OnValidate()
        {
            if (!TryApplyDetectedCollisionProfile(false))
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
                || (currentState != GlassState.PickedUp
                    && currentState != GlassState.Returning))
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
            BartendingSelection.Release(this);
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

            if (currentState == GlassState.Returning && !pointerSyncPending)
                FollowMousePosition();
        }

        private void PickupGlass()
        {
            if (!PreparePickup())
                return;

            BeginPointerSynchronization(
                transform.position,
                unlockCursor: false,
                completeReturn: false);
        }

        private bool PreparePickup()
        {
            if (!BartendingSelection.TryAcquire(this))
                return false;

            bool wasHeld = IsPickedUp;
            CancelPointerSynchronization();
            CancelPendingPhysicsMotion();
            currentState = GlassState.PickedUp;
            if (!wasHeld)
                HeldStateChanged?.Invoke(this, true);
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

            return true;
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
                if (hit == null || hit.transform.IsChildOf(transform)) continue;

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
        public bool IsPickedUp => currentState == GlassState.PickedUp || currentState == GlassState.Tilting || currentState == GlassState.Returning;

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
            ReleaseGlass();
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
            PickupGlass();
        }

        public void OnPickedUpAt(Vector3 pointerWorld)
        {
            if (!PreparePickup())
                return;

            pointerPivotOffset = transform.position - pointerWorld;
            pointerPivotOffset.z = 0f;
        }

        public void OnDropped()
        {
            TryDropGlass();
        }

        private void ReleaseGlass()
        {
            bool wasHeld = IsPickedUp;
            currentState = GlassState.Idle;
            CancelPendingPhysicsMotion();
            CancelPointerSynchronization();
            BartendingPointerAnchor.Release(this);
            BartendingSelection.Release(this);

            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }

            if (wasHeld)
                HeldStateChanged?.Invoke(this, false);
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
            currentState = GlassState.PickedUp;
        }

        // 위치/회전을 Update에서 바로 적용하지 않고 큐에 담아뒀다가 FixedUpdate(ApplyPendingPhysicsMotion)에서
        // Rigidbody2D.MovePosition/MoveRotation으로 적용한다 — Kinematic Rigidbody는 물리 스텝 밖에서
        // transform을 직접 바꾸면 트리거 충돌 감지가 불안정해지므로 반드시 물리 스텝에 맞춰야 한다.
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

        // 잔을 집거나(픽업) 원위치 복귀가 끝났을 때 마우스 커서를 잔의 새 화면 좌표로 강제 이동시켜,
        // 다음 프레임부터 "커서가 곧 잔"이 되게 만드는 동기화 절차. OS 커서 워프는 한 프레임 만에
        // 반영되지 않을 수 있어 최대 PointerSyncFrameBudget 프레임 동안 확인을 재시도한다.
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
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                return false;
            }

            // 다른 겹치는 오브젝트(액체 입자 등)에 방해받지 않는 단독 격리 판정
            return interactionOrder != null
                ? interactionOrder.IsFrontmostAt(mousePos)
                : ContainsInteractionPoint(mousePos);
        }

        private bool ContainsInteractionPoint(Vector2 worldPoint)
        {
            if (activeCollisionProfile != null
                && collisionVisual != null
                && collisionVisual.sprite != null)
            {
                return activeCollisionProfile.ContainsInteractionPoint(
                    collisionVisual.sprite,
                    collisionVisual.transform,
                    worldPoint);
            }

            return mainCollider != null
                && mainCollider.enabled
                && mainCollider.OverlapPoint(worldPoint);
        }

        private float GetPivotToBottomOffset()
        {
            if (activeCollisionProfile != null
                && collisionVisual != null
                && collisionVisual.sprite != null)
            {
                float localBottom = activeCollisionProfile.GetVisibleBottomLocalY(
                    collisionVisual.sprite);
                float worldBottom = collisionVisual.transform.TransformPoint(
                    new Vector3(0f, localBottom, 0f)).y;
                return Mathf.Max(0f, transform.position.y - worldBottom);
            }

            float rendererBottom = float.PositiveInfinity;
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer != null && renderer.enabled && renderer.sprite != null)
                    rendererBottom = Mathf.Min(rendererBottom, renderer.bounds.min.y);
            }
            if (!float.IsPositiveInfinity(rendererBottom))
                return Mathf.Max(0f, transform.position.y - rendererBottom);

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

        // 스프라이트 이름으로 GlassCollisionProfiles에 등록된 프로필을 찾아 자동 적용한다.
        // 아트가 바뀌어도(스프라이트 교체) 잔마다 콜라이더/트리거를 수동 세팅할 필요 없게 하기 위함.
        private bool TryApplyDetectedCollisionProfile(bool configureContentTriggers)
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null
                    || renderer.sprite == null
                    || !GlassCollisionProfiles.TryGetBySpriteName(
                        renderer.sprite.name,
                        out GlassCollisionProfileDefinition profile))
                {
                    continue;
                }

                ApplyCollisionProfile(profile, renderer, configureContentTriggers);
                return true;
            }

            return false;
        }

        private void ApplyCollisionProfile(
            GlassCollisionProfileDefinition profile,
            SpriteRenderer visual,
            bool configureContentTriggers)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (visual == null || visual.sprite == null)
                throw new ArgumentException("A SpriteRenderer with a sprite is required.", nameof(visual));

            if (edgeCollider == null)
                edgeCollider = GetComponent<EdgeCollider2D>();
            if (edgeCollider == null)
                edgeCollider = gameObject.AddComponent<EdgeCollider2D>();

            Vector2[] spritePoints = profile.BuildEdgePath(visual.sprite);
            Vector2[] rootPoints = new Vector2[spritePoints.Length];
            for (int i = 0; i < spritePoints.Length; i++)
            {
                Vector3 worldPoint = visual.transform.TransformPoint(spritePoints[i]);
                rootPoints[i] = transform.InverseTransformPoint(worldPoint);
            }

            edgeCollider.isTrigger = false;
            edgeCollider.edgeRadius = edgeRadius;
            edgeCollider.SetPoints(new List<Vector2>(rootPoints));

            activeCollisionProfile = profile;
            collisionVisual = visual;
            glassId = profile.GlassId;
            capacityMl = profile.CapacityMl;
            UpdateLegacyGeometryMetrics(rootPoints);

            if (configureContentTriggers)
            {
                ConfigureContentTriggers(profile, visual);
                mainCollider = edgeCollider;
            }

            if (Application.isPlaying)
            {
                interactionOrder = BartendingItemOrder.Attach(
                    gameObject,
                    mainCollider,
                    liquidTracker,
                    ContainsInteractionPoint);
            }

            liquidTracker?.ConfigureServingStyle(glassId);
            liquidTracker?.RefreshCollisionGeometry();
        }

        // 프로필이 정의한 개수만큼 "__GlassContentTrigger_N" 이름의 자식 BoxCollider2D를 만들어
        // VesselLiquidTracker가 액체 입자를 감지할 트리거로 쓴다. 기존에 남아있던 자식은 재사용하고,
        // 새 프로필의 트리거 개수보다 인덱스가 큰 것들은 비활성화한다(프로필 교체 시 잔여물 방지).
        private Collider2D ConfigureContentTriggers(
            GlassCollisionProfileDefinition profile,
            SpriteRenderer visual)
        {
            BoxCollider2D[] rootBoxes = GetComponents<BoxCollider2D>();
            for (int i = 0; i < rootBoxes.Length; i++)
                rootBoxes[i].enabled = false;

            int triggerCount = profile.ContentTriggerPixels.Count;
            BoxCollider2D primary = null;
            for (int i = 0; i < triggerCount; i++)
            {
                string triggerName = ContentTriggerPrefix + i;
                Transform triggerTransform = visual.transform.Find(triggerName);
                if (triggerTransform == null)
                {
                    GameObject triggerObject = new GameObject(triggerName);
                    triggerObject.layer = gameObject.layer;
                    triggerTransform = triggerObject.transform;
                    triggerTransform.SetParent(visual.transform, false);
                }

                triggerTransform.localPosition = Vector3.zero;
                triggerTransform.localRotation = Quaternion.identity;
                triggerTransform.localScale = Vector3.one;
                triggerTransform.gameObject.SetActive(true);

                BoxCollider2D trigger = triggerTransform.GetComponent<BoxCollider2D>();
                if (trigger == null)
                    trigger = triggerTransform.gameObject.AddComponent<BoxCollider2D>();

                Rect localRect = profile.BuildContentTrigger(visual.sprite, i);
                trigger.enabled = true;
                trigger.isTrigger = true;
                trigger.offset = localRect.center;
                trigger.size = localRect.size;
                primary ??= trigger;
            }

            for (int i = 0; i < visual.transform.childCount; i++)
            {
                Transform child = visual.transform.GetChild(i);
                if (!child.name.StartsWith(ContentTriggerPrefix, StringComparison.Ordinal))
                    continue;

                string suffix = child.name.Substring(ContentTriggerPrefix.Length);
                if (!int.TryParse(suffix, out int index) || index < 0 || index >= triggerCount)
                    child.gameObject.SetActive(false);
            }

            return primary != null ? primary : edgeCollider;
        }

        // 프로필 기반 실제 콜라이더 포인트로부터 height/topWidth/bottomWidth 같은 기존 필드를
        // 역산해 채워둔다 — 이 값들을 참조하는 다른 코드(예: GetPivotToBottomOffset 폴백)와의
        // 하위 호환을 위해서다.
        private void UpdateLegacyGeometryMetrics(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count == 0)
                return;

            float minX = points[0].x;
            float maxX = points[0].x;
            float minY = points[0].y;
            float maxY = points[0].y;
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 point = points[i];
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }

            height = Mathf.Max(0.1f, maxY - minY);
            colliderYOffset = (minY + maxY) * 0.5f;
            topWidth = Mathf.Max(0.1f, Mathf.Abs(points[points.Count - 1].x - points[0].x));
            bottomWidth = Mathf.Max(0.1f, maxX - minX);
        }
    }
}
