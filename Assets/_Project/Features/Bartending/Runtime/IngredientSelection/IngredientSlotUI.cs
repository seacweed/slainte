using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    // 술 선택 공간의 재료 하나. 호버하면 아이콘이 위로 슬라이드해 가려져 있던 아래쪽이 드러나고
    // 윤곽선과 이름·소분류 라벨이 켜지며, 클릭하면 IngredientSelectionUI를 통해 병을 꺼낸다.
    // 호버 가능 여부·스폰 가드는 소유 컨트롤러가 판단하고, 이 컴포넌트는 표시와 입력 전달만 맡는다.
    public sealed class IngredientSlotUI : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerClickHandler
    {
        [Header("Visual")]
        [Tooltip("Moves up on hover. The root stays under layout control, so only this child slides.")]
        [SerializeField] private RectTransform visualRoot;
        [SerializeField] private Image icon;
        [Tooltip("Mesh effect toggled on hover. Use IngredientOutlineEffect for a solid-color outline.")]
        [SerializeField] private BaseMeshEffect hoverOutline;

        [Header("Hover Labels")]
        [SerializeField] private GameObject hoverLabelRoot;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text subCategoryLabel;

        [Header("Motion")]
        [SerializeField] private float hoverRaise = 120f;
        [SerializeField, Min(0.01f)] private float hoverSlideDuration = 0.12f;

        [Header("Stock")]
        [SerializeField, Range(0f, 1f)] private float depletedAlpha = 0.4f;

        private IngredientSelectionUI owner;
        private Vector2 restingPosition;
        private bool initialized;
        private bool hovered;
        private int pressFrame = -1;
        private bool pressConsumedByDrop;
        private Coroutine slideRoutine;

        public LiquorBottleDef Definition { get; private set; }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDisable()
        {
            // 비활성화되는 순간 코루틴이 끊기므로 중간 위치에 멈추지 않게 즉시 원위치로 되돌린다.
            ForceUnhover();
        }

        // 비활성 부모 아래에서 생성되면 Awake가 늦어지므로 Setup에서도 같은 초기화를 보장한다.
        private void EnsureInitialized()
        {
            if (initialized)
                return;

            initialized = true;
            if (visualRoot == null)
                visualRoot = transform as RectTransform;
            restingPosition = visualRoot != null ? visualRoot.anchoredPosition : Vector2.zero;

            // 아이콘이 위로 움직이면 포인터가 아이콘 밖으로 빠졌다 다시 들어오며 호버가 깜빡이므로,
            // 움직이는 그래픽은 레이캐스트를 받지 않고 루트의 고정된 히트 영역만 이벤트를 받게 한다.
            if (icon != null)
                icon.raycastTarget = false;
            if (nameLabel != null)
                nameLabel.raycastTarget = false;
            if (subCategoryLabel != null)
                subCategoryLabel.raycastTarget = false;

            SetHoverDecorations(false);
        }

        public void Setup(LiquorBottleDef definition, IngredientSelectionUI selection)
        {
            EnsureInitialized();
            Definition = definition;
            owner = selection;

            if (icon != null)
            {
                Sprite sprite = definition != null ? definition.GetShelfSprite() : null;
                icon.sprite = sprite;
                icon.enabled = sprite != null;
                icon.preserveAspect = true;
            }

            if (nameLabel != null)
                nameLabel.text = definition != null ? definition.displayName : string.Empty;

            if (subCategoryLabel != null)
            {
                subCategoryLabel.text = definition != null ? definition.subCategory : string.Empty;
                if (definition != null && definition.category != null)
                {
                    Color categoryColor = definition.category.color;
                    categoryColor.a = 1f;
                    subCategoryLabel.color = categoryColor;
                }
            }
        }

        public void SetAvailable(bool available)
        {
            if (icon == null)
                return;

            Color color = icon.color;
            color.a = available ? 1f : depletedAlpha;
            icon.color = color;
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
            // 도구·병 놓기는 누른 프레임에 처리되고 이 슬롯의 클릭은 뗀 프레임에 오므로, 뗄 때는
            // 이미 손이 비어 있어 스폰 가드(CanHover)를 그대로 통과한다. 그래서 "누른 순간"의
            // 상태로 판단해 놓기에 소비된 누름이면 재료를 꺼내지 않는다.
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

            // 같은 누름이 들고 있던 것을 놓는 데(술장 반환 또는 슬롯 안착) 이미 소비됐다면
            // 새 병을 꺼내지 않는다.
            if (pressConsumedByDrop)
                return;
            if (pressFrame >= 0 && BottleReturnZones.LastReturnFrame >= pressFrame)
                return;

            owner.TrySpawn(this, eventData.position);
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

        private void SetHoverDecorations(bool visible)
        {
            if (hoverOutline != null)
                hoverOutline.enabled = visible;
            if (hoverLabelRoot != null)
                hoverLabelRoot.SetActive(visible);
        }
    }
}
