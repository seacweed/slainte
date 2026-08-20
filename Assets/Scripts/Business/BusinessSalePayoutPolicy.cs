using Slainte.Economy;

namespace Slainte.Business
{
    public interface IBusinessSalePayoutPolicy
    {
        void Apply(BusinessOrderSessionResult result, GameProgress progress);
    }

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
