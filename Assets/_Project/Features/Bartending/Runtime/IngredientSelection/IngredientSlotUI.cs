using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    // 술 선택 공간의 재료 하나. 호버·라벨·클릭 가드는 SelectionSlotUI가 맡고, 여기서는 재료 정의를
    // 아이콘·이름·소분류로 표시하고 재고 없음을 _dis 이미지(없으면 반투명)로 나타낸다. 호버 라벨에는
    // 술장 잔량을 stock_ 아이콘 줄로, 재고가 없고 배송 가능하면 배송 가격 줄을 덧붙인다. 클릭하면 소유
    // 컨트롤러(IngredientSelectionUI)가 병이나 가니쉬를 꺼내 손에 쥐여 주거나 배송을 주문한다.
    // 호버 순간 최신 재고·소지금으로 라벨을 다시 채워 달라고 요청받는 소유자.
    // 소지금은 변경 이벤트가 없어, 라벨이 보이는 시점에 다시 묻는 것이 가장 싸고 정확하다.
    public interface IIngredientSlotRefresher
    {
        void RefreshSlot(IngredientSlotUI slot);
    }

    public sealed class IngredientSlotUI : SelectionSlotUI
    {
        [Header("Icon")]
        [SerializeField] private Image icon;

        [Header("Stock Icons")]
        [SerializeField] private LiquorStockLevelPalette stockLevelPalette;
        [Tooltip("Row that holds one icon per bottleCount. The template child is cloned at runtime.")]
        [SerializeField] private RectTransform stockIconRow;
        [SerializeField] private Image stockIconTemplate;

        [Header("Delivery")]
        [SerializeField] private GameObject deliveryPriceRoot;
        [SerializeField] private TMP_Text deliveryPriceLabel;
        [SerializeField] private Color affordablePriceColor = Color.white;
        [SerializeField] private Color unaffordablePriceColor = new Color(1f, 0.35f, 0.35f, 1f);

        private readonly List<Image> stockIcons = new List<Image>();

        public LiquorBottleDef Definition { get; private set; }
        public bool IsDepleted { get; private set; }

        protected override void OnInitialize()
        {
            if (icon != null)
                icon.raycastTarget = false;
            if (stockIconTemplate != null)
            {
                stockIconTemplate.raycastTarget = false;
                stockIconTemplate.gameObject.SetActive(false);
            }
            if (deliveryPriceLabel != null)
                deliveryPriceLabel.raycastTarget = false;
            if (deliveryPriceRoot != null)
                deliveryPriceRoot.SetActive(false);
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

        // 술장 재고(제작대에 꺼낸 병 제외)와 배송 제안을 반영한다. 재고·배송 상태가 바뀔 때마다
        // 소유자가 호출하므로 호버 중이어도 라벨이 즉시 갱신된다.
        public void SetStock(float shelfAmount, BottleDeliveryOffer offer)
        {
            EnsureInitialized();
            if (Definition == null)
                return;

            IsDepleted = shelfAmount <= 0.01f;
            ApplyAvailabilityVisual(!IsDepleted);
            RefreshStockIcons(shelfAmount);
            RefreshDeliveryPrice(offer);
        }

        // 재고 없음은 전용 _dis 이미지로 교체해 표시한다. 전용 이미지가 없는 재료(가니쉬 등)만
        // 기존처럼 반투명으로 대신한다 — _dis 아트 자체가 비활성 표현이라 알파를 겹치면 이중으로 흐려진다.
        private void ApplyAvailabilityVisual(bool available)
        {
            if (icon == null)
                return;

            Sprite disabledSprite = Definition.GetShelfDisabledSprite();
            bool swapSprite = !available && disabledSprite != null;
            Sprite target = swapSprite ? disabledSprite : Definition.GetShelfSprite();
            if (icon.sprite != target)
                icon.sprite = target;

            Color color = icon.color;
            color.a = available || swapSprite ? 1f : depletedAlpha;
            icon.color = color;
        }

        // 기존 술장 정보카드와 같은 규칙: 가득 찬 병 → 따 둔 병 1칸(남은 비율) → 빈 병 순으로
        // bottleCount칸을 채운다.
        private void RefreshStockIcons(float shelfAmount)
        {
            if (stockLevelPalette == null || stockIconRow == null || stockIconTemplate == null)
                return;

            int bottleCount = Mathf.Max(0, Definition.bottleCount);
            float unit = Mathf.Max(0.01f, Definition.unitVolume);
            float clamped = Mathf.Clamp(shelfAmount, 0f, Definition.MaxAmount);
            int fullCount = Mathf.Min(bottleCount, Mathf.FloorToInt((clamped + 0.01f) / unit));
            float remainder = Mathf.Max(0f, clamped - fullCount * unit);
            bool hasInUse = remainder > 0.01f && fullCount < bottleCount;

            EnsureStockIconCount(bottleCount);
            for (int i = 0; i < stockIcons.Count; i++)
            {
                Image stockIcon = stockIcons[i];
                bool inRange = i < bottleCount;
                stockIcon.gameObject.SetActive(inRange);
                if (!inRange)
                    continue;

                float ratio = i < fullCount ? 1f
                    : hasInUse && i == fullCount ? remainder / unit
                    : 0f;
                Sprite sprite = stockLevelPalette.GetSprite(ratio);
                stockIcon.sprite = sprite;
                stockIcon.enabled = sprite != null;
            }
        }

        // 아이콘은 필요한 수만큼만 한 번 복제해 재사용한다(재고 변화마다 생성·파괴하지 않음).
        private void EnsureStockIconCount(int count)
        {
            while (stockIcons.Count < count)
            {
                Image clone = Instantiate(stockIconTemplate, stockIconRow);
                clone.name = stockIconTemplate.name + "_" + stockIcons.Count;
                clone.raycastTarget = false;
                stockIcons.Add(clone);
            }
        }

        protected override void SetHoverDecorations(bool visible)
        {
            if (visible && Owner is IIngredientSlotRefresher refresher)
                refresher.RefreshSlot(this);
            base.SetHoverDecorations(visible);
        }

        private void RefreshDeliveryPrice(BottleDeliveryOffer offer)
        {
            if (deliveryPriceRoot == null)
                return;

            deliveryPriceRoot.SetActive(offer.Show);
            if (!offer.Show || deliveryPriceLabel == null)
                return;

            deliveryPriceLabel.text = offer.Price.ToString("N0");
            deliveryPriceLabel.color = offer.Affordable ? affordablePriceColor : unaffordablePriceColor;
        }
    }
}
