using System;
using System.Collections.Generic;

[Serializable]
public class SaveData
{
    public string gameVersion = "1.0.0";
    public int dayCount = 1;
    public List<string> flags = new();
    public List<string> completedEpisodeIds = new();
    public List<string> affinityKeys   = new();
    public List<int>    affinityValues = new();
    public List<string> boardSlotKeys   = new();
    public List<int>    boardSlotValues = new();
    public List<string> bottleAmountKeys   = new();
    public List<float>  bottleAmountValues = new();
    public List<string> customerAppearanceKeys   = new();
    public List<int>    customerAppearanceValues = new();
    public List<string> upgradeKeys   = new();
    public List<int>    upgradeValues = new();
    public string currentChapterId  = "";
    public int    currentMoney      = 0;
    public int    reputation        = 0;
    public int    dayDrinkSalesCount = 0;
    public int    dayDrinkRevenue    = 0;
    public int    dayTotalIncome     = 0;
}
