using System.Collections.Generic;
using Slainte.Business;
using Slainte.Economy;
using UnityEngine;

// 정산 화면(셔터+모니터) 진행을 담당. GameManager.ChangeState(GameState.Settlement)에서 호출됨.
public class SettlementManager : MonoSingleton<SettlementManager>
{
    [SerializeField] private SettlementUI settlementUI;

    private readonly List<ChapterData> _chapters = new();
    private bool settlementActive;

    protected override void Awake()
    {
        base.Awake();
        _chapters.AddRange(Resources.LoadAll<ChapterData>("ChapterData"));
    }

    public void BeginSettlement()
    {
        if (settlementActive)
        {
            Debug.LogWarning("[Settlement] Settlement is already active; duplicate payout skipped.");
            return;
        }

        GameProgress gp = GameProgress.Instance;
        if (gp == null) return;
        settlementActive = true;

        SettlementData data = new SettlementData
        {
            chapterName     = ResolveChapterName(gp.CurrentChapterId),
            day             = gp.CurrentDay,
            drinkSalesCount = gp.DayDrinkSalesCount,
            drinkBaseRevenue = gp.DayDrinkBaseRevenue,
            tipRevenue      = gp.DayDrinkTipRevenue,
            drinkRevenue    = gp.DayDrinkRevenue,
            reputationDelta = gp.DayReputationDelta,
            totalIncome     = gp.DayTotalIncome,
            strangeCoinBaseRevenue = gp.DayStrangeCoinBaseRevenue,
            strangeCoinTipRevenue = gp.DayStrangeCoinTipRevenue,
            strangeCoinRevenue = gp.DayStrangeCoinRevenue,
            drinkSales      = BuildDrinkSales(gp)
        };

        ApplyRecordedIncome(gp);
        data.currentMoney = gp.CurrentMoney;

        if (settlementUI != null)
            settlementUI.Show(data, OnSettlementClosed);
        else
            OnSettlementClosed();
    }

    public static int ApplyRecordedIncome(GameProgress progress)
    {
        if (progress == null)
            return 0;

        int income = Mathf.Max(0, progress.DayTotalIncome - progress.DayPaidMoneyIncome);
        progress.AddMoney(income);
        int strangeCoinIncome = Mathf.Max(
            0,
            progress.DayStrangeCoinRevenue - progress.DayPaidStrangeCoinIncome);
        GameCurrencyWallet.Add(progress, GameCurrency.StrangeCoin, strangeCoinIncome);
        return income;
    }

    private List<DrinkSaleEntry> BuildDrinkSales(GameProgress gp)
    {
        List<DrinkSaleEntry> entries = new();
        List<BusinessSaleRecord> records = gp.GetDayDrinkSales();
        Dictionary<string, int> indexByDrink = new(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < records.Count; i++)
        {
            BusinessSaleRecord record = records[i];
            if (record == null)
                continue;

            string drinkName = !string.IsNullOrWhiteSpace(record.requestedRecipeId)
                ? record.requestedRecipeId
                : !string.IsNullOrWhiteSpace(record.customerOrderKey)
                    ? record.customerOrderKey
                    : "음료 판매";

            string groupingKey = record.paymentCurrency + ":" + drinkName;
            if (indexByDrink.TryGetValue(groupingKey, out int entryIndex))
            {
                DrinkSaleEntry entry = entries[entryIndex];
                entry.count += 1;
                entry.baseRevenue += record.baseRevenue;
                entry.tipAmount += record.tipAmount;
                entry.revenue += record.totalRevenue;
                entries[entryIndex] = entry;
                continue;
            }

            indexByDrink.Add(groupingKey, entries.Count);
            entries.Add(new DrinkSaleEntry
            {
                drinkName = drinkName,
                currency = record.paymentCurrency,
                count = 1,
                baseRevenue = record.baseRevenue,
                tipAmount = record.tipAmount,
                revenue = record.totalRevenue
            });
        }

        if (entries.Count == 0 && gp.DayDrinkSalesCount > 0)
        {
            entries.Add(new DrinkSaleEntry
            {
                drinkName = "음료 판매",
                currency = Slainte.Economy.GameCurrency.Money,
                count = gp.DayDrinkSalesCount,
                baseRevenue = gp.DayDrinkBaseRevenue != 0
                    ? gp.DayDrinkBaseRevenue
                    : gp.DayDrinkRevenue,
                tipAmount = gp.DayDrinkTipRevenue,
                revenue = gp.DayDrinkRevenue
            });
        }

        return entries;
    }

    private string ResolveChapterName(string chapterId)
    {
        if (string.IsNullOrEmpty(chapterId)) return "";
        foreach (var chapter in _chapters)
            if (chapter != null && chapter.chapterId == chapterId) return chapter.chapterName;
        return chapterId;
    }

    private void OnSettlementClosed()
    {
        if (!settlementActive)
            return;

        settlementActive = false;
        GameProgress.Instance?.ResetDaySettlement();
        DataManager.Instance?.Save();
        GameManager.Instance?.ChangeState(GameState.Rest);
    }

    // GameManager -> SceneTransitionManager가 Rest씬으로 페이드아웃을 마친 직후 호출됨.
    // 화면이 완전히 검은 상태이므로 이 시점에 정산 화면을 감추고 셔터/모니터를 원위치로 되돌린다.
    public void OnFadeOutComplete()
    {
        settlementUI?.HideAndReset();
    }
}
