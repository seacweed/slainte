using UnityEngine;

namespace Slainte.Bartending
{
    // 가니쉬(필 등) 월드 아이템. Idle ↔ PickedUp 두 상태만 있는 단순 물체로, 좌클릭으로 집어 포인터를
    // 따라가다 빈 슬롯에 놓거나 술 선택 공간에 반환된다. 액체·물리 상호작용은 아직 없으므로 콜라이더는
    // 트리거로 두어 병·잔·액체 입자와 부딪히지 않고, 클릭 판정과 "포인터 아래 물체" 검사에만 쓰인다.
    // 코드로 생성되므로 Start에 기대지 않고 Initialize에서 필요한 구성 요소를 모두 갖춘다.
    [DisallowMultipleComponent]
    public sealed class GarnishController : MonoBehaviour, IBartendingItem, IPointerAnchoredPickup
    {
        private const int PickupSortingBoost = 100;

        private SpriteRenderer spriteRenderer;
        private BoxCollider2D mainCollider;
        private Rigidbody2D body;
        private Camera mainCamera;
        private LayerMask slotLayer;
        private BartendingItemOrder interactionOrder;
        private SlotController currentSlot;
        private Vector3 pointerOffset;
        private int restingSortingOrder;
        private bool pickedUp;

        public GarnishDef Definition { get; private set; }
        public GameObject GameObject => gameObject;
        public bool IsPickedUp => pickedUp;

        public void Initialize(GarnishDef definition, int sortingLayerId, int sortingOrder)
        {
            Definition = definition;
            mainCamera = Camera.main;

            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = definition != null ? definition.garnishSprite : null;
            spriteRenderer.sortingLayerID = sortingLayerId;
            spriteRenderer.sortingOrder = sortingOrder;
            restingSortingOrder = sortingOrder;

            body = GetComponent<Rigidbody2D>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            mainCollider = GetComponent<BoxCollider2D>();
            if (mainCollider == null)
                mainCollider = gameObject.AddComponent<BoxCollider2D>();
            mainCollider.isTrigger = true;
            if (spriteRenderer.sprite != null)
            {
                Bounds bounds = spriteRenderer.sprite.bounds;
                mainCollider.size = bounds.size;
                mainCollider.offset = bounds.center;
            }

            int slotLayerIndex = LayerMask.NameToLayer("Slot");
            slotLayer = slotLayerIndex >= 0 ? 1 << slotLayerIndex : 0;
            interactionOrder = BartendingItemOrder.Attach(gameObject, mainCollider);
        }

        private void OnDisable()
        {
            if (pickedUp)
                Release();
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (!pickedUp && IsMouseOver())
                    PickUp(GetPointerWorldOr(transform.position));
                else if (pickedUp)
                    TryDrop();
            }

            if (pickedUp)
                FollowPointer();
        }

        public void OnPickedUp()
        {
            PickUp(GetPointerWorldOr(transform.position));
        }

        public void OnPickedUpAt(Vector3 pointerWorld)
        {
            PickUp(pointerWorld);
        }

        public void OnDropped()
        {
            TryDrop();
        }

        public void SnapToSlot(Transform slotTransform, SlotController slot)
        {
            if (slotTransform == null)
                return;

            currentSlot = slot;
            SetPositionImmediately(new Vector3(
                slotTransform.position.x,
                slotTransform.position.y + GetBottomOffset(),
                0f));
            Release();
        }

        // 술 선택 공간 반환 직전에 호출된다. 슬롯 점유와 선택을 풀고 즉시 숨겨, 파괴되는 프레임에
        // 다른 입력 처리에 걸리지 않게 한다.
        public void PrepareForShelfReturn()
        {
            currentSlot?.Vacate();
            currentSlot = null;
            Release();
            gameObject.SetActive(false);
        }

        private void PickUp(Vector3 pointerWorld)
        {
            if (!BartendingSelection.TryAcquire(this))
                return;

            pickedUp = true;
            currentSlot?.Vacate();
            currentSlot = null;
            pointerOffset = transform.position - pointerWorld;
            pointerOffset.z = 0f;
            if (spriteRenderer != null)
                spriteRenderer.sortingOrder = restingSortingOrder + PickupSortingBoost;
            interactionOrder?.BringToFront();
        }

        // 병과 같은 규칙: 반환 영역이 우선이고, 그 밖에서는 빈 슬롯 위에서만 내려놓을 수 있다.
        private void TryDrop()
        {
            if (BottleReturnZones.TryReturnHeldBottle(Input.mousePosition))
                return;

            if (!BartendingViewport.TryGetPointerWorldPosition(
                    mainCamera,
                    Input.mousePosition,
                    out Vector3 pointerWorld))
            {
                return;
            }

            Collider2D[] hits = Physics2D.OverlapPointAll(pointerWorld, slotLayer);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null || hit == mainCollider)
                    continue;

                SlotController slot = hit.GetComponent<SlotController>();
                if (slot == null || slot.IsOccupied)
                    continue;

                slot.Occupy(this);
                SnapToSlot(slot.transform, slot);
                return;
            }
        }

        private void Release()
        {
            pickedUp = false;
            pointerOffset = Vector3.zero;
            if (spriteRenderer != null)
                spriteRenderer.sortingOrder = restingSortingOrder;
            BartendingSelection.Release(this);
        }

        private void FollowPointer()
        {
            if (!BartendingViewport.TryGetPointerWorldPosition(
                    mainCamera,
                    Input.mousePosition,
                    out Vector3 pointerWorld))
            {
                return;
            }

            Vector3 target = pointerWorld + pointerOffset;
            target.z = 0f;
            if (body != null)
                body.MovePosition(target);
            else
                transform.position = target;
        }

        private void SetPositionImmediately(Vector3 position)
        {
            position.z = 0f;
            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector2.zero;
            }
            transform.position = position;
        }

        private bool IsMouseOver()
        {
            if (mainCollider == null
                || !BartendingViewport.TryGetPointerWorldPosition(
                    mainCamera,
                    Input.mousePosition,
                    out Vector3 pointerWorld))
            {
                return false;
            }

            return interactionOrder != null
                ? interactionOrder.IsFrontmostAt(pointerWorld)
                : mainCollider.OverlapPoint(pointerWorld);
        }

        private Vector3 GetPointerWorldOr(Vector3 fallback)
        {
            return BartendingViewport.TryGetPointerWorldPosition(
                    mainCamera,
                    Input.mousePosition,
                    out Vector3 pointerWorld)
                ? pointerWorld
                : fallback;
        }

        private float GetBottomOffset()
        {
            if (mainCollider == null)
                return 0f;
            return transform.position.y - mainCollider.bounds.min.y;
        }
    }
}
