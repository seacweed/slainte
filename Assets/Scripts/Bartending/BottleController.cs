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
    public class BottleController : MonoBehaviour
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

        private void Start()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            col = GetComponent<Collider2D>();
            mainCamera = Camera.main;

            originalSortingOrder = spriteRenderer.sortingOrder;

            ApplyBottleData();
        }

        public void Init(ItemDef data)
        {
            bottleData = data;
            ApplyBottleData();
        }

        private void ApplyBottleData()
        {
            if (bottleData != null && bottleData.icon != null)
            {
                if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
                spriteRenderer.sprite = bottleData.icon;
            }
        }

        private void Update()
        {
            HandleInput();
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
            spriteRenderer.sortingOrder = PICKUP_SORTING_ORDER;
            
            // Stop returning if it was returning while picked up again
            if (returnCoroutine != null)
            {
                StopCoroutine(returnCoroutine);
                returnCoroutine = null;
            }
        }

        private void FollowMousePosition()
        {
            Vector3 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0f;
            transform.position = mousePos;
        }

        private void TryDropBottle()
        {
            Vector3 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            Collider2D hit = Physics2D.OverlapPoint(mousePos, slotLayer);

            if (hit != null)
            {
                // Snap to slot
                transform.position = hit.transform.position;
                transform.rotation = Quaternion.identity;
                currentAngle = 0f;
                
                ReleaseBottle();
            }
            // If missed slot, do nothing (keep holding)
        }

        private void ReleaseBottle()
        {
            currentState = BottleState.Idle;
            spriteRenderer.sortingOrder = originalSortingOrder;
            
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
            
            transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);
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
                Vector2 screenPos = mainCamera.WorldToScreenPoint(transform.position);
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
                
                currentAngle = Mathf.Lerp(startAngle, 0f, curveValue);
                transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);
                
                yield return null;
            }

            currentAngle = 0f;
            transform.rotation = Quaternion.identity;
            
            // Transition back to PickedUp state so they can move it or drop it again
            currentState = BottleState.PickedUp;
            returnCoroutine = null;
        }

        private bool IsMouseOverBottle()
        {
            Vector3 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            Collider2D hit = Physics2D.OverlapPoint(mousePos);
            
            return hit != null && hit == col;
        }
    }
}
