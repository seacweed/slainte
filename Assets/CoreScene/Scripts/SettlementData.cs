using System;
using System.Collections.Generic;

[Serializable]
public struct DrinkSaleEntry
{
    public string drinkName;
    public int    count;
    public int    baseRevenue;
    public int    tipAmount;
    public int    revenue;
}

[Serializable]
public struct SettlementData
{
    public string chapterName;
    public int    day;
    public int    drinkSalesCount;
    public int    drinkBaseRevenue;
    public int    tipRevenue;
    public int    drinkRevenue;
    public int    reputationDelta;
    public int    totalIncome;
    public int    currentMoney;
    public List<DrinkSaleEntry> drinkSales;
}
