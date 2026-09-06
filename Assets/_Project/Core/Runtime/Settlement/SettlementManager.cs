using System.Collections.Generic;
using Slainte.Business;
using Slainte.Content;
using Slainte.Economy;
using Slainte.Shared.Lifecycle;
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
        _chapters.AddRange(Resources.LoadAll<ChapterData>(
            ProjectResourcePaths.NarrativeChapters));
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

    // 음료 판매는 판매 즉시 지갑에 지급되므로 DayTotalIncome과 DayPaidMoneyIncome이 대부분 같다.
    // 그 차액은 아직 지급되지 않은 몫(예: 에피소드 커스텀 보상)이며, 여기서 실제로 지급한다.
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

            // 별도 등급 필드 대신 팁/페널티가 실제로 기록됐는지로 Good/Bad 건수를 센다
            // (팁이 있으면 Good, 페널티가 있으면 Bad — 둘 다 없는 Mid 등급은 이 집계에서 빠진다).
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

        string cutsceneId = DayFlowController.Instance?.ConsumePendingSettlementCutscene();
        if (string.IsNullOrEmpty(cutsceneId))
        {
            GameManager.Instance?.ChangeState(GameState.Rest);
            return;
        }

        // 컷씬이 화면을 곧바로 덮으므로, 원래 화면이 완전히 검게 된 후(OnFadeOutComplete)
        // 정리하던 정산 UI(셔터/모니터)를 여기서 먼저 리셋해둔다.
        settlementUI?.HideAndReset();

        bool isEnding = cutsceneId == CutsceneIds.Ending;
        CutsceneManager.Instance?.Play(cutsceneId, () =>
        {
            if (isEnding)
                SceneTransitionManager.Instance?.TransitionToSubScene(
                    "MainMenuScene",
                    onFadeOutComplete: () => CutsceneManager.Instance?.HideImmediate());
            else
                GameManager.Instance?.ChangeState(GameState.Rest);
        });
    }

    // GameManager -> SceneTransitionManager가 Rest씬으로 페이드아웃을 마친 직후 호출됨.
    // 화면이 완전히 검은 상태이므로 이 시점에 정산 화면을 감추고 셔터/모니터를 원위치로 되돌린다.
    public void OnFadeOutComplete()
    {
        settlementUI?.HideAndReset();
    }
}
