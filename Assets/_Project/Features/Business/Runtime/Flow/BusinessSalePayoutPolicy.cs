using Slainte.Economy;

namespace Slainte.Business
{
    // 완료된 주문 하나의 판매 대금을 언제·어떻게 지갑에 반영할지 결정하는 전략.
    // BusinessShiftController.TrySetSalePayoutPolicy로 영업 시작 전 교체 가능하다.
    public interface IBusinessSalePayoutPolicy
    {
        void Apply(BusinessOrderSessionResult result, GameProgress progress);
    }

    // 판매 기록만 남기고 실제 지갑 반영(GameCurrencyWallet.Add)은 하지 않는다 — 하루치 판매를
    // 모아뒀다가 정산(Settlement) 화면에서 한꺼번에 지급하는 흐름에서 쓰는 정책.
    public sealed class DeferredSettlementSalePayoutPolicy : IBusinessSalePayoutPolicy
    {
        public void Apply(BusinessOrderSessionResult result, GameProgress progress)
        {
            if (result == null || progress == null)
                return;

            progress.RecordDrinkSale(result.ToSaleRecord());
            progress.AddReputation(result.reputationDelta);
        }
    }


    // 주문이 끝나는 즉시 지갑에 대금을 반영하는 기본 정책. BusinessShiftController.Initialize()가
    // 별도 설정이 없으면 이 정책을 기본값으로 사용한다.
    public sealed class ImmediateSalePayoutPolicy : IBusinessSalePayoutPolicy
    {
        public void Apply(BusinessOrderSessionResult result, GameProgress progress)
        {
            if (result == null || progress == null)
                return;

            GameCurrencyWallet.Add(progress, result.paymentCurrency, result.PaymentAmount);
            BusinessSaleRecord record = result.ToSaleRecord();
            record.paymentApplied = true;
            progress.RecordDrinkSale(record);
            progress.AddReputation(result.reputationDelta);
        }
    }
}
