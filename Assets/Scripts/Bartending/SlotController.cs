using UnityEngine;

namespace Slainte.Bartending
{
    [RequireComponent(typeof(BoxCollider2D), typeof(SpriteRenderer))]
    public class SlotController : MonoBehaviour
    {
        [Header("점유 상태")]
        [SerializeField] private bool isOccupied;
        private IBartendingItem occupiedItem;

        [Header("하이라이트 시각 설정")]
        [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0f); // 평소에는 100% 완전 투명
        [SerializeField] private Color highlightColor = new Color(0.4f, 1.0f, 0.4f, 0.5f); // 호버 시 은은하고 선명하게 반투명 에메랄드 연녹색으로 빛남

        private SpriteRenderer spriteRenderer;

        public bool IsOccupied => isOccupied;
        public IBartendingItem OccupiedItem => occupiedItem;

        private void Start()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            ResetVisual();
            
            BoxCollider2D col = GetComponent<BoxCollider2D>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        public void Occupy(IBartendingItem item)
        {
            if (item == null) return;

            occupiedItem = item;
            isOccupied = true;
            ResetVisual();
        }

        public void Vacate()
        {
            occupiedItem = null;
            isOccupied = false;
            ResetVisual();
        }

        private void ResetVisual()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.color = normalColor;
            }
        }

        private void SetHighlightVisual()
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = highlightColor;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (isOccupied) return;

            IBartendingItem item = other.GetComponentInParent<IBartendingItem>();
            if (item != null && item.IsPickedUp)
            {
                SetHighlightVisual();
            }
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (isOccupied)
            {
                ResetVisual();
                return;
            }

            IBartendingItem item = other.GetComponentInParent<IBartendingItem>();
            if (item != null && item.IsPickedUp)
            {
                SetHighlightVisual();
            }
            else
            {
                ResetVisual();
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            IBartendingItem item = other.GetComponentInParent<IBartendingItem>();
            if (item != null)
            {
                ResetVisual();
            }
        }
    }
}
