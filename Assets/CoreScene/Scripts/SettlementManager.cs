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

        SettlementData data = BuildSettlementSummary(gp);
        data.chapterName = ResolveChapterName(gp.CurrentChapterId);
        data.day = gp.CurrentDay;

        ApplyRecordedIncome(gp);
        data.totalIncome = gp.DayTotalIncome;
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

        int income = progress.DayTotalIncome - progress.DayPaidMoneyIncome;
        progress.AddMoney(income);
        int strangeCoinIncome =
            progress.DayStrangeCoinRevenue - progress.DayPaidStrangeCoinIncome;
        GameCurrencyWallet.Add(progress, GameCurrency.StrangeCoin, strangeCoinIncome);
        return income;
    }

    private SettlementData BuildSettlementSummary(GameProgress gp)
    {
        SettlementData data = new SettlementData
        {
            customRewards = gp.GetDaySettlementRewards()
        };

        List<BusinessSaleRecord> records = gp.GetDayDrinkSales();
        for (int i = 0; i < records.Count; i++)
        {
            BusinessSaleRecord record = records[i];
            if (record == null || record.paymentCurrency != GameCurrency.Money)
                continue;

            data.totalSalesCount += 1;
            data.totalSalesRevenue += record.listedPrice;

            if (record.tipAmount != 0)
            {
                data.goodCount += 1;
                data.tipTotal += record.tipAmount;
            }

            if (record.penaltyAmount != 0)
            {
                data.badCount += 1;
                data.missedRevenue += record.penaltyAmount;
            }
        }

        data.deliveryCount = gp.DayDeliveryCount;
        data.deliverySpend = gp.DayDeliverySpend;

        return data;
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
