namespace Slainte.Economy
{
    public enum GameCurrency
    {
        Money = 0,
        StrangeCoin = 1
    }

    public static class GameCurrencyWallet
    {
        public const string StrangeCoinVariableName = "strange_coin";

        public static int GetBalance(GameProgress progress, GameCurrency currency)
        {
            if (progress == null)
                return 0;

            return currency == GameCurrency.StrangeCoin
                ? progress.GetAffinity(StrangeCoinVariableName)
                : progress.CurrentMoney;
        }

        public static void Add(GameProgress progress, GameCurrency currency, int amount)
        {
            if (progress == null || amount == 0)
                return;

            if (currency == GameCurrency.StrangeCoin)
                progress.AddAffinity(StrangeCoinVariableName, amount);
            else
                progress.AddMoney(amount);
        }

        public static bool TrySpend(GameProgress progress, GameCurrency currency, int amount)
        {
            if (progress == null)
                return false;
            if (amount <= 0)
                return true;

            return currency == GameCurrency.StrangeCoin
                ? progress.TrySpendAffinity(StrangeCoinVariableName, amount)
                : progress.TrySpendMoney(amount);
        }
    }
}
