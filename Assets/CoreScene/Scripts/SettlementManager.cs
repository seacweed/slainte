using System.Collections.Generic;
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
            drinkRevenue    = gp.DayDrinkRevenue,
            totalIncome     = gp.DayTotalIncome,
            drinkSales      = BuildDrinkSalesPlaceholder(gp)
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

        int income = progress.DayTotalIncome;
        progress.AddMoney(income);
        return income;
    }

    // 영업 시스템이 아직 음료 종류별 판매를 기록하지 않아, 집계값을 한 줄짜리 placeholder로 노출.
    // 종류별 데이터가 생기면 이 메서드만 교체하면 됨 (SettlementUI는 그대로 사용 가능).
    private List<DrinkSaleEntry> BuildDrinkSalesPlaceholder(GameProgress gp)
    {
        List<DrinkSaleEntry> entries = new();
        if (gp.DayDrinkSalesCount > 0)
        {
            entries.Add(new DrinkSaleEntry
            {
                drinkName = "음료 판매",
                count     = gp.DayDrinkSalesCount,
                revenue   = gp.DayDrinkRevenue
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
