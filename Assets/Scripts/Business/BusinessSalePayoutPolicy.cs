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

            progress.RecordDrinkSale(result.moneyDelta);
            progress.AddReputation(result.reputationDelta);
        }
    }
}
