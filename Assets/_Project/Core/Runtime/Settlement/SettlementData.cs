using System;
using System.Collections.Generic;

[Serializable]
public class SettlementRewardEntry
{
    public string label;
    public int    amount;
}

[Serializable]
public struct SettlementData
{
    public string chapterName;
    public int    day;
    public int    totalSalesCount;
    public int    totalSalesRevenue;
    public int    goodCount;
    public int    tipTotal;
    public int    badCount;
    public int    missedRevenue;
    public int    deliveryCount;
    public int    deliverySpend;
    public List<SettlementRewardEntry> customRewards;
    public int    totalIncome;
    public int    currentMoney;
}
