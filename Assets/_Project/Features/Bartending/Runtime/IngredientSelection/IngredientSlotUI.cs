using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    // 술 선택 공간의 재료 하나. 호버·라벨·클릭 가드는 SelectionSlotUI가 맡고, 여기서는 재료 정의를
    // 아이콘·이름·소분류로 표시하고 재고 없음을 반투명으로 나타낸다. 클릭하면 소유 컨트롤러
    // (IngredientSelectionUI)가 병이나 가니쉬를 꺼내 손에 쥐여 준다.
    public sealed class IngredientSlotUI : SelectionSlotUI
    {
        [Header("Icon")]
        [SerializeField] private Image icon;

        public LiquorBottleDef Definition { get; private set; }

        protected override void OnInitialize()
        {
            if (icon != null)
                icon.raycastTarget = false;
        }

        public void Setup(LiquorBottleDef definition, ISelectionSlotOwner selection)
        {
            EnsureInitialized();
            Definition = definition;
            BindOwner(selection);

            if (icon != null)
            {
                Sprite sprite = definition != null ? definition.GetShelfSprite() : null;
                icon.sprite = sprite;
                icon.enabled = sprite != null;
                icon.preserveAspect = true;
            }

            SetLabels(
                definition != null ? definition.displayName : string.Empty,
                definition != null ? definition.subCategory : string.Empty,
                definition != null && definition.category != null
                    ? definition.category.color
                    : (Color?)null);
        }

        public void SetAvailable(bool available)
        {
            if (icon == null)
                return;

            Color color = icon.color;
            color.a = available ? 1f : depletedAlpha;
            icon.color = color;
        }
    }
}
