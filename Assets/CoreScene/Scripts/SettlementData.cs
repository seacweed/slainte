using System;
using System.Collections.Generic;
using Slainte.Economy;

[Serializable]
public struct DrinkSaleEntry
{
    public string drinkName;
    public GameCurrency currency;
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
    public int    strangeCoinBaseRevenue;
    public int    strangeCoinTipRevenue;
    public int    strangeCoinRevenue;
    public int    currentMoney;
    public List<DrinkSaleEntry> drinkSales;
}
