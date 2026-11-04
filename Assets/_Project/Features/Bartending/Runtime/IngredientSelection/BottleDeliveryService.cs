using System;
using UnityEngine;

namespace Slainte.Bartending
{
    // 술장 슬롯에 표시할 배송 정보. Show가 false면 배송 가격 줄을 숨긴다.
    public readonly struct BottleDeliveryOffer
    {
        public static readonly BottleDeliveryOffer None = default;

        public BottleDeliveryOffer(int price, bool affordable)
        {
            Show = true;
            Price = price;
            Affordable = affordable;
        }

        public bool Show { get; }
        public int Price { get; }
        public bool Affordable { get; }
    }

    // 재고가 떨어진 술장 재료의 배송 규칙. 가격 계산·배송 가능 여부(해금/TV 제한)·결제·정산 기록·
    // 연출 중 중복 방지·지급을 한곳에서 맡아, 슬롯 UI는 표시만 하고 구매 규칙을 알지 않게 한다.
    // 결제 수단은 IShopCurrency로 주입받아 상점과 같은 방식으로 다른 화폐에도 쓸 수 있다.
    public sealed class BottleDeliveryService
    {
        private readonly IShopCurrency currency;
        private readonly Func<int> priceMultiplier;
        private bool available;

        public BottleDeliveryService(IShopCurrency currency, Func<int> priceMultiplier)
        {
            this.currency = currency;
            this.priceMultiplier = priceMultiplier;
        }

        public string UnavailableReason { get; private set; } = string.Empty;
        public bool IsAvailable => available;

        // 해금되지 않았거나 TV 방송이 막은 날에는 false — 호출자가 사유를 함께 넘긴다.
        public void SetAvailable(bool value, string reason)
        {
            available = value;
            UnavailableReason = value ? string.Empty : reason ?? string.Empty;
        }

        public int GetPrice(LiquorBottleDef definition)
        {
            if (definition == null || currency == null)
                return 0;

            int basePrice = currency.GetPrice(definition);
            int multiplier = priceMultiplier != null ? Mathf.Max(1, priceMultiplier()) : 1;
            return basePrice > 0 ? basePrice * multiplier : 0;
        }

        // 재고가 남아 있거나, 배송이 막혔거나, 가격이 없는 재료(가니쉬 등)는 배송 대상이 아니다.
        public BottleDeliveryOffer GetOffer(LiquorBottleDef definition, bool depleted)
        {
            if (!depleted || !available)
                return BottleDeliveryOffer.None;

            int price = GetPrice(definition);
            if (price <= 0)
                return BottleDeliveryOffer.None;

            return new BottleDeliveryOffer(price, currency.CurrentAmount >= price);
        }

        // 결제는 클릭 즉시, 재고 지급은 연출이 끝날 때(onDelivered) 한다. 연출이 재생 중이면 결제 전에
        // 거절해 돈만 빠지는 중복 배송을 막는다.
        public bool TryOrder(
            LiquorBottleDef definition,
            DeliveryRisePresenter presenter,
            Action<LiquorBottleDef> onDelivered,
            out string failure)
        {
            failure = null;
            BottleDeliveryOffer offer = GetOffer(definition, true);
            if (!offer.Show)
            {
                failure = available ? "This ingredient cannot be delivered." : UnavailableReason;
                return false;
            }

            if (presenter != null && presenter.IsPlaying)
            {
                failure = "A delivery is already in progress.";
                return false;
            }

            if (!offer.Affordable || !currency.TrySpend(offer.Price))
            {
                failure = "Not enough money for delivery.";
                return false;
            }

            GameProgress.Instance?.RecordDeliveryPurchase(offer.Price);
            if (presenter == null || !presenter.Play(() => onDelivered?.Invoke(definition)))
                onDelivered?.Invoke(definition);
            return true;
        }
    }
}
