using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    // 진열 슬롯(재료·도구)이 소유 컨트롤러에 묻는 계약. 호버·클릭 가능 여부와 실제 꺼내기는
    // 소유자가 판단하고, 슬롯은 표시와 입력 전달만 맡는다.
    public interface ISelectionSlotOwner
    {
        bool CanHover(Vector2 screenPosition);
        bool HasHeldItem();
        // 이 누름(pressFrame)이 들고 있던 물체를 진열대에 반납하는 데 이미 쓰였는지.
        bool WasPressConsumedByReturn(int pressFrame);
        void OnSlotClicked(SelectionSlotUI slot, Vector2 screenPosition);
    }

    // 술 선택 공간·도구 공간 슬롯의 공통 동작. 호버하면 visualRoot가 위로 슬라이드해 가려져 있던
    // 아래쪽이 드러나고 윤곽선과 이름·분류 라벨이 켜지며, 클릭하면 소유자에게 꺼내기를 요청한다.
    // 놓기·반납에 소비된 누름이 같은 클릭으로 다시 꺼내기로 이어지지 않도록 누른 시점의 상태로 가드한다.
    public abstract class SelectionSlotUI : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerClickHandler
    {
        [Header("Visual")]
        [Tooltip("Moves up on hover. The root stays under layout control, so only this child slides.")]
        [SerializeField] protected RectTransform visualRoot;
        [Tooltip("Mesh effect toggled on hover. Use IngredientOutlineEffect for a solid-color outline.")]
        [SerializeField] protected BaseMeshEffect hoverOutline;

        [Header("Hover Labels")]
        [SerializeField] protected GameObject hoverLabelRoot;
        [SerializeField] protected TMP_Text nameLabel;
        [SerializeField] protected TMP_Text subCategoryLabel;

        [Header("Motion")]
        [SerializeField] private float hoverRaise = 120f;
        [SerializeField, Min(0.01f)] private float hoverSlideDuration = 0.12f;

        [Header("Stock")]
        [SerializeField, Range(0f, 1f)] protected float depletedAlpha = 0.4f;

        private ISelectionSlotOwner owner;
        private Vector2 restingPosition;
        private bool initialized;
        private bool hovered;
        private int pressFrame = -1;
        private bool pressConsumedByDrop;
        private Coroutine slideRoutine;

        protected ISelectionSlotOwner Owner => owner;

        protected virtual void Awake()
        {
            EnsureInitialized();
        }

        protected virtual void OnDisable()
        {
            // 비활성화되는 순간 코루틴이 끊기므로 중간 위치에 멈추지 않게 즉시 원위치로 되돌린다.
            ForceUnhover();
        }

        // 비활성 부모 아래에서 생성되면 Awake가 늦어지므로 Setup류에서도 같은 초기화를 보장한다.
        protected void EnsureInitialized()
        {
            if (initialized)
                return;

            initialized = true;
            if (visualRoot == null)
                visualRoot = transform as RectTransform;
            restingPosition = visualRoot != null ? visualRoot.anchoredPosition : Vector2.zero;

            // 아이콘이 위로 움직이면 포인터가 아이콘 밖으로 빠졌다 다시 들어오며 호버가 깜빡이므로,
            // 움직이는 그래픽은 레이캐스트를 받지 않고 루트의 고정된 히트 영역만 이벤트를 받게 한다.
            if (nameLabel != null)
                nameLabel.raycastTarget = false;
            if (subCategoryLabel != null)
                subCategoryLabel.raycastTarget = false;
            OnInitialize();

            SetHoverDecorations(false);
        }

        // 하위 클래스가 자기 그래픽(아이콘 등)의 레이캐스트를 끄는 등 1회 초기화를 넣는 지점.
        protected virtual void OnInitialize()
        {
        }

        protected void BindOwner(ISelectionSlotOwner selectionOwner)
        {
            owner = selectionOwner;
        }

        protected void SetLabels(string displayName, string subCategory, Color? subCategoryColor)
        {
            if (nameLabel != null)
                nameLabel.text = displayName ?? string.Empty;

            if (subCategoryLabel != null)
            {
                subCategoryLabel.text = subCategory ?? string.Empty;
                if (subCategoryColor.HasValue)
                {
                    Color color = subCategoryColor.Value;
                    color.a = 1f;
                    subCategoryLabel.color = color;
                }
            }
        }

        public void ForceUnhover()
        {
            hovered = false;
            if (slideRoutine != null)
            {
                StopCoroutine(slideRoutine);
                slideRoutine = null;
            }

            if (visualRoot != null && initialized)
                visualRoot.anchoredPosition = restingPosition;
            SetHoverDecorations(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner == null || eventData == null || !owner.CanHover(eventData.position))
                return;

            SetHovered(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (hovered)
                SetHovered(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressFrame = Time.frameCount;
            // 물체 놓기는 누른 프레임에 처리되고 이 슬롯의 클릭은 뗀 프레임에 오므로, 뗄 때는
            // 이미 손이 비어 있어 가드(CanHover)를 그대로 통과한다. 그래서 "누른 순간"의
            // 상태로 판단해 놓기에 소비된 누름이면 꺼내지 않는다.
            pressConsumedByDrop = owner != null && owner.HasHeldItem();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (owner == null
                || eventData == null
                || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            // 반납은 물체 쪽 Update에서 처리되어 OnPointerDown보다 먼저 끝날 수 있으므로,
            // 손 상태 외에 "이번 누름으로 반납이 일어났는지"도 함께 확인한다.
            if (pressConsumedByDrop)
                return;
            if (pressFrame >= 0 && owner.WasPressConsumedByReturn(pressFrame))
                return;

            owner.OnSlotClicked(this, eventData.position);
        }

        private void SetHovered(bool value)
        {
            hovered = value;
            SetHoverDecorations(value);
            if (visualRoot == null)
                return;

            Vector2 target = restingPosition + (value ? Vector2.up * hoverRaise : Vector2.zero);
            if (slideRoutine != null)
                StopCoroutine(slideRoutine);
            slideRoutine = isActiveAndEnabled ? StartCoroutine(SlideTo(target)) : null;
            if (slideRoutine == null)
                visualRoot.anchoredPosition = target;
        }

        // 진입·이탈이 도중에 뒤집히면 현재 위치에서 새 목표로 이어서 움직인다. 남은 거리에 비례해
        // 시간을 줄여, 짧게 되돌아갈 때도 항상 같은 속도감(전체 거리 기준 hoverSlideDuration)을 유지한다.
        private IEnumerator SlideTo(Vector2 target)
        {
            Vector2 start = visualRoot.anchoredPosition;
            float fullDistance = Mathf.Max(0.01f, Mathf.Abs(hoverRaise));
            float duration = hoverSlideDuration
                * Mathf.Clamp01(Vector2.Distance(start, target) / fullDistance);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                visualRoot.anchoredPosition = Vector2.LerpUnclamped(start, target, eased);
                yield return null;
            }

            visualRoot.anchoredPosition = target;
            slideRoutine = null;
        }

        protected virtual void SetHoverDecorations(bool visible)
        {
            if (hoverOutline != null)
                hoverOutline.enabled = visible;
            if (hoverLabelRoot != null)
                hoverLabelRoot.SetActive(visible);
        }
    }
}
