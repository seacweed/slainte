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

        private BottleState currentState = BottleState.Idle;
        private SpriteRenderer spriteRenderer;
        private Collider2D col;
        
        private int originalSortingOrder;
        private const int PICKUP_SORTING_ORDER = 100;

        private Camera mainCamera;
        
        // Tilt state variables
        private float initialAngle;
        private float currentAngle = 0f;
        private Coroutine returnCoroutine;

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
        }

        public void Init(ItemDef data)
        {
            if (data == null || data.type != ItemType.Bottle)
            {
                Debug.LogWarning("BottleController requires ItemDef with type Bottle.");
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

            maxCapacity = bottleData.capacityMl;
            currentCapacity = bottleData.capacityMl;
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

            GameObject obj = LiquidPool.Instance.GetParticle(spawnPos + randomOffset);
            if (obj != null)
            {
                if (bottleData != null && bottleData.type == ItemType.Bottle)
                {
                    LiquidParticleData particleData = obj.GetComponent<LiquidParticleData>();
                    if (particleData != null)
                        particleData.SetPayload(bottleData, 1f);
                }
                Rigidbody2D rb = obj.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                }
                
                LiquidReaction reaction = obj.GetComponent<LiquidReaction>();
                if (reaction != null)
                {
                    reaction.WakeUp();
                }

                currentCapacity -= 1f; // Adjust amount per particle if needed
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

            // Follow Mouse when picked up or returning
            if (currentState == BottleState.PickedUp || currentState == BottleState.Returning)
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
            
            // Transition back to PickedUp state so they can move it or drop it again
            currentState = BottleState.PickedUp;
            returnCoroutine = null;
        }

        private bool IsMouseOverBottle()
        {
            if (col == null) return false;
            
            if (!BartendingViewport.TryGetPointerWorldPosition(mainCamera, Input.mousePosition, out Vector3 mousePos))
            {
                return false;
            }

            return col.OverlapPoint(mousePos);
        }
    }
}
