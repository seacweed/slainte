using Slainte.Economy;

// 상점 슬롯(ItemSlotUI)이 어떤 화폐로 결제되는지를 추상화한다.
// 일반 상점(원화)과 이상한 상점(이상한 동전)처럼 화폐만 다르고 나머지 구매 로직은
// 동일한 경우, ItemSlotUI 코드를 중복시키지 않고 이 구현체만 바꿔 끼우면 된다.
public interface IShopCurrency
{
    int GetPrice(LiquorBottleDef def);
    int CurrentAmount { get; }
    bool TrySpend(int amount);
}

// 일반 상점: 원화(GameProgress.CurrentMoney)로 결제
public class MoneyShopCurrency : IShopCurrency
{
    public int GetPrice(LiquorBottleDef def) => def != null ? def.GetPrice(GameCurrency.Money) : 0;
    public int CurrentAmount => GameCurrencyWallet.GetBalance(GameProgress.Instance, GameCurrency.Money);
    public bool TrySpend(int amount) => GameCurrencyWallet.TrySpend(GameProgress.Instance, GameCurrency.Money, amount);
}

// 이상한 상점: 이상한 동전(GameProgress의 범용 수치 변수 저장소)으로 결제
public class StrangeCoinShopCurrency : IShopCurrency
{
    public const string VarName = GameCurrencyWallet.StrangeCoinVariableName;

    public int GetPrice(LiquorBottleDef def) => def != null ? def.GetPrice(GameCurrency.StrangeCoin) : 0;
    public int CurrentAmount => GameCurrencyWallet.GetBalance(GameProgress.Instance, GameCurrency.StrangeCoin);
    public bool TrySpend(int amount) => GameCurrencyWallet.TrySpend(GameProgress.Instance, GameCurrency.StrangeCoin, amount);
}
