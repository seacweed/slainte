using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Slainte.Bartending
{
    public enum GlassState
    {
        Idle,
        PickedUp,
        Tilting,
        Returning
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
        [SerializeField] private LayerMask slotLayer;

        [Header("물리 컴포넌트")]
        [SerializeField] private EdgeCollider2D edgeCollider;

        private GlassState currentState = GlassState.Idle;
        private Collider2D mainCollider; // 마우스 클릭용 터치 트리거 콜라이더
        private Camera mainCamera;
        
        private float initialAngle;
        private float currentAngle = 0f;
        private Coroutine returnCoroutine;

        private SlotController currentSlot; // 현재 점유 중인 슬롯 레퍼런스

        private void Start()
        {
            mainCollider = GetComponent<Collider2D>();
            mainCamera = Camera.main;

            if (Application.isPlaying)
            {
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
            }

            if (Application.isPlaying)
            {
                currentState = GlassState.Idle;
            }
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
                if (currentState == GlassState.Idle && IsMouseOverGlass())
                {
                    PickupGlass();
                }
                else if (currentState == GlassState.PickedUp || currentState == GlassState.Returning)
                {
                    TryDropGlass();
                }
            }

            // Follow Mouse (0초 무지연 1:1 매칭 & MovePosition 물리)
            if (currentState == GlassState.PickedUp || currentState == GlassState.Returning)
            {
                FollowMousePosition();
            }

            // Right Click (Tilt / Return)
            if (Input.GetMouseButtonDown(1))
            {
                if (currentState == GlassState.PickedUp || currentState == GlassState.Returning)
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
        }

        private void PickupGlass()
        {
            currentState = GlassState.PickedUp;
            
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
                // 댐핑 없는 즉각 1:1 추종 + 연속 물리(Sweep) 충돌 보장
                rb.MovePosition(mousePos);
            }
            else
            {
                transform.position = mousePos;
            }
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
                        currentSlot = slot;
                        slot.Occupy(this);

                        // 슬롯 스냅 안착 (바닥면 Y 오프셋 칼각 정렬!)
                        float bottomOffset = GetPivotToBottomOffset();
                        transform.position = new Vector3(hit.transform.position.x, hit.transform.position.y + bottomOffset, 0f);
                        transform.rotation = Quaternion.identity;
                        currentAngle = 0f;
                        
                        ReleaseGlass();
                        return;
                    }
                }
                else
                {
                    // 슬롯 스냅 안착 (하위 호환용)
                    float bottomOffset = GetPivotToBottomOffset();
                    transform.position = new Vector3(hit.transform.position.x, hit.transform.position.y + bottomOffset, 0f);
                    transform.rotation = Quaternion.identity;
                    currentAngle = 0f;
                    
                    ReleaseGlass();
                    return;
                }
            }
        }

        // IBartendingItem 인터페이스 완벽 구현부
        public GameObject GameObject => gameObject;
        public bool IsPickedUp => currentState == GlassState.PickedUp || currentState == GlassState.Tilting || currentState == GlassState.Returning;

        public void SnapToSlot(Transform slotTransform, SlotController slot)
        {
            currentSlot = slot;
            float bottomOffset = GetPivotToBottomOffset();
            transform.position = new Vector3(slotTransform.position.x, slotTransform.position.y + bottomOffset, 0f);
            transform.rotation = Quaternion.identity;
            currentAngle = 0f;
            ReleaseGlass();
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

            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }
        }

        private void StartTilting()
        {
            currentState = GlassState.Tilting;
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
            currentState = GlassState.Returning;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

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
            
            currentState = GlassState.PickedUp;
            returnCoroutine = null;
        }

        private bool IsMouseOverGlass()
        {
            if (mainCollider == null) return false;
            
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                return false;
            }

            // 다른 겹치는 오브젝트(액체 입자 등)에 방해받지 않는 단독 격리 판정
            return mainCollider.OverlapPoint(mousePos);
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
