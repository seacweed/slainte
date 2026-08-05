using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
    public class BeakerController : MonoBehaviour, IBartendingItem
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
        
        private float initialAngle;
        private float currentAngle = 0f;
        private Coroutine returnCoroutine;
        private Vector3 dragOffset;

        private SlotController currentSlot; // 현재 점유 중인 슬롯 레퍼런스
        private Vector3 dragVelocity = Vector3.zero;

        private SpriteRenderer[] childRenderers;
        private int[] originalSortingOrders;
        private const int PICKUP_SORTING_ORDER_BASE = 100;
        private BartendingItemOrder interactionOrder;

        public VesselLiquidTracker LiquidTracker => liquidTracker;

        private void Start()
        {
            mainCollider = GetComponent<Collider2D>();
            mainCamera = Camera.main;

            if (Application.isPlaying)
            {
                // 1. Rigidbody2D 키네마틱 물리 셋업 강제 보장 (2D 물리 트리거 상호작용 완벽 복구)
                EnsureLiquidTracker();

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
                    liquidTracker);
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

            HandleInput();
        }

        private void HandleInput()
        {
            // Left Click (Pickup / Drop Toggle)
            if (Input.GetMouseButtonDown(0))
            {
                if (currentState == BeakerState.Idle && IsMouseOverBeaker())
                {
                    PickupBeaker();
                }
                else if (currentState == BeakerState.PickedUp || currentState == BeakerState.Returning)
                {
                    TryDropBeaker();
                }
            }

            // Follow Mouse
            if (currentState == BeakerState.PickedUp || currentState == BeakerState.Returning)
            {
                FollowMousePosition();
            }

            // Right Click (Tilt / Return)
            if (Input.GetMouseButtonDown(1))
            {
                if (currentState == BeakerState.PickedUp || currentState == BeakerState.Returning)
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
        }

        private void PickupBeaker()
        {
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

            CaptureDragOffset();
        }

        private void FollowMousePosition()
        {
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                return;
            }

            Vector3 targetPosition = mousePos + dragOffset;
            
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

        private void CaptureDragOffset()
        {
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                dragOffset = Vector3.zero;
                return;
            }

            dragOffset = transform.position - mousePos;
            dragOffset.z = 0f;
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
                        currentSlot = slot;
                        slot.Occupy(this);

                        // 슬롯 스냅 안착 (바닥면 Y 오프셋 칼각 정렬!)
                        float bottomOffset = GetPivotToBottomOffset();
                        MoveVesselAndContents(new Vector3(hit.transform.position.x, hit.transform.position.y + bottomOffset, 0f));
                        transform.rotation = Quaternion.identity;
                        currentAngle = 0f;
                        
                        ReleaseBeaker();
                        return;
                    }
                }
                else
                {
                    // 슬롯 스냅 안착 (하위 호환용)
                    float bottomOffset = GetPivotToBottomOffset();
                    MoveVesselAndContents(new Vector3(hit.transform.position.x, hit.transform.position.y + bottomOffset, 0f));
                    transform.rotation = Quaternion.identity;
                    currentAngle = 0f;
                    
                    ReleaseBeaker();
                    return;
                }
            }
        }

        // IBartendingItem 인터페이스 완벽 구현부
        public GameObject GameObject => gameObject;
        public bool IsPickedUp => currentState == BeakerState.PickedUp || currentState == BeakerState.Tilting || currentState == BeakerState.Returning;

        public void SnapToSlot(Transform slotTransform, SlotController slot)
        {
            currentSlot = slot;
            float bottomOffset = GetPivotToBottomOffset();
            MoveVesselAndContents(new Vector3(slotTransform.position.x, slotTransform.position.y + bottomOffset, 0f));
            transform.rotation = Quaternion.identity;
            currentAngle = 0f;
            ReleaseBeaker();
        }

        private void MoveVesselAndContents(Vector3 targetPosition)
        {
            targetPosition.z = 0f;
            Vector2 delta = targetPosition - transform.position;
            liquidTracker?.TranslateTrackedParticles(delta);

            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.position = targetPosition;
            else
                transform.position = targetPosition;
        }

        public void OnPickedUp()
        {
            PickupBeaker();
        }

        public void OnDropped()
        {
            TryDropBeaker();
        }

        private void ReleaseBeaker()
        {
            currentState = BeakerState.Idle;
            
            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }
        }

        private void StartTilting()
        {
            currentState = BeakerState.Tilting;
            initialAngle = currentAngle;

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
            float deltaY = Input.GetAxis("Mouse Y");
            currentAngle += deltaY * tiltSensitivity * 10f;
            currentAngle = Mathf.Clamp(currentAngle, -maxTiltAngle, maxTiltAngle);
            
            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.MoveRotation(currentAngle);
            }
            else
            {
                transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);
            }
        }

        private void StartReturning()
        {
            currentState = BeakerState.Returning;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (Mouse.current != null && mainCamera != null)
            {
                Vector2 screenPos = BartendingViewport.GetPointerScreenPosition(mainCamera, transform.position);
                Mouse.current.WarpCursorPosition(screenPos);
            }

            dragOffset = Vector3.zero;

            returnCoroutine = StartCoroutine(ReturnToUprightRoutine());
        }

        private IEnumerator ReturnToUprightRoutine()
        {
            float startAngle = currentAngle;
            float timeElapsed = 0f;

            Rigidbody2D rb = GetComponent<Rigidbody2D>();

            while (timeElapsed < returnSpeed)
            {
                timeElapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(timeElapsed / returnSpeed);
                float curveValue = returnEase.Evaluate(normalizedTime);
                
                currentAngle = Mathf.Lerp(startAngle, 0f, curveValue);
                if (rb != null)
                {
                    rb.MoveRotation(currentAngle);
                }
                else
                {
                    transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);
                }
                
                yield return null;
            }

            currentAngle = 0f;
            if (rb != null)
            {
                rb.MoveRotation(0f);
            }
            else
            {
                transform.rotation = Quaternion.identity;
            }
            
            currentState = BeakerState.PickedUp;
            returnCoroutine = null;
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
                : mainCollider.OverlapPoint(mousePos);
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
