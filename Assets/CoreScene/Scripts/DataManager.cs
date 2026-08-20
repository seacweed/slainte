using UnityEngine;
using System.IO;

public class DataManager : MonoSingleton<DataManager>
{
    private static int saveSuppressionDepth;

    public SaveData CurrentData { get; private set; } = new SaveData();
    private string savePath;

    public static bool AreDiskWritesSuppressed => saveSuppressionDepth > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSaveSuppression()
    {
        saveSuppressionDepth = 0;
    }

    public static void PushSaveSuppression()
    {
        saveSuppressionDepth++;
    }

    public static void PopSaveSuppression()
    {
        saveSuppressionDepth = Mathf.Max(0, saveSuppressionDepth - 1);
    }

    protected override void Awake()
    {
        base.Awake();
        savePath = Path.Combine(Application.persistentDataPath, "autosave.json");
        Load();
    }

    [ContextMenu("Save Game")]
    public void Save()
    {
        if (AreDiskWritesSuppressed)
        {
            Debug.Log("[DataManager] Playtest isolation is active; disk save skipped.");
            return;
        }

        GameProgress gp = GameProgress.Instance;
        if (gp != null)
        {
            CurrentData.dayCount            = gp.CurrentDay;
            CurrentData.flags               = gp.GetFlagList();
            CurrentData.completedEpisodeIds = gp.GetCompletedList();
            CurrentData.affinityKeys        = gp.GetAffinityKeys();
            CurrentData.affinityValues      = gp.GetAffinityValues();
            CurrentData.boardSlotKeys       = gp.GetBoardSlotKeys();
            CurrentData.boardSlotValues     = gp.GetBoardSlotValues();
            CurrentData.bottleAmountKeys    = gp.GetBottleAmountKeys();
            CurrentData.bottleAmountValues  = gp.GetBottleAmountValues();
            CurrentData.customerAppearanceKeys   = gp.GetCustomerAppearanceKeys();
            CurrentData.customerAppearanceValues = gp.GetCustomerAppearanceValues();
            CurrentData.upgradeKeys         = gp.GetUpgradeKeys();
            CurrentData.upgradeValues       = gp.GetUpgradeValues();
            CurrentData.currentChapterId    = gp.CurrentChapterId;
            CurrentData.currentMoney        = gp.CurrentMoney;
            CurrentData.reputation          = gp.Reputation;
            CurrentData.dayDrinkSalesCount  = gp.DayDrinkSalesCount;
            CurrentData.dayDrinkBaseRevenue = gp.DayDrinkBaseRevenue;
            CurrentData.dayDrinkTipRevenue  = gp.DayDrinkTipRevenue;
            CurrentData.dayDrinkRevenue     = gp.DayDrinkRevenue;
            CurrentData.dayTotalIncome      = gp.DayTotalIncome;
            CurrentData.dayPaidMoneyIncome  = gp.DayPaidMoneyIncome;
            CurrentData.dayStrangeCoinBaseRevenue = gp.DayStrangeCoinBaseRevenue;
            CurrentData.dayStrangeCoinTipRevenue = gp.DayStrangeCoinTipRevenue;
            CurrentData.dayStrangeCoinRevenue = gp.DayStrangeCoinRevenue;
            CurrentData.dayPaidStrangeCoinIncome = gp.DayPaidStrangeCoinIncome;
            CurrentData.dayReputationDelta  = gp.DayReputationDelta;
            CurrentData.dayDrinkSales       = gp.GetDayDrinkSales();
            CurrentData.tvForecastBroadcastId = gp.TVForecastBroadcastId;
            CurrentData.tvForecastRevealed = gp.TVForecastRevealed;
            CurrentData.tvActiveBroadcastId = gp.TVActiveBroadcastId;
            CurrentData.tvActiveBusinessDay = gp.TVActiveBusinessDay;
        }

        string json = JsonUtility.ToJson(CurrentData, true);
        File.WriteAllText(savePath, json);
        Debug.Log($"[DataManager] Saved: {savePath}");
    }

    [ContextMenu("Delete Save File")]
    public void DeleteSaveFile()
    {
        string path = string.IsNullOrEmpty(savePath)
            ? Path.Combine(Application.persistentDataPath, "autosave.json")
            : savePath;

        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log("[DataManager] Save file deleted.");
        }
        else
        {
            Debug.Log("[DataManager] No save file to delete.");
        }

        CurrentData = new SaveData();
    }

    public void Load()
    {
        if (File.Exists(savePath))
        {
            string json = File.ReadAllText(savePath);
            CurrentData = JsonUtility.FromJson<SaveData>(json);
            Debug.Log("[DataManager] Save data loaded.");
        }
        else
        {
            Debug.Log("[DataManager] No save file found. Using new data.");
        }

        GameProgress.Instance?.LoadFrom(CurrentData);
    }
}
