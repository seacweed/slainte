using System;
using System.Collections.Generic;
using UnityEngine;

public class GameProgress : MonoSingleton<GameProgress>
{
    public static event Action<string, int> OnAffinityChanged;

    [SerializeField] private int currentDay = 1;
    [SerializeField] private List<string> flags = new();
    [SerializeField] private List<string> completedEpisodeIds = new();

    [Header("Affinity Variables")]
    [SerializeField] private List<string> affinityKeys   = new();
    [SerializeField] private List<int>    affinityValues = new();

    [Header("Board Slot Positions")]
    [SerializeField] private List<string> boardSlotKeys   = new();
    [SerializeField] private List<int>    boardSlotValues = new();

    [Header("Bottle Amounts")]
    [SerializeField] private List<string> bottleAmountKeys   = new();
    [SerializeField] private List<float>  bottleAmountValues = new();

    [Header("Upgrade Levels")]
    [SerializeField] private List<string> upgradeKeys   = new();
    [SerializeField] private List<int>    upgradeValues = new();

    [Header("Customer Appearances")]
    [SerializeField] private List<string> customerAppearanceKeys   = new();
    [SerializeField] private List<int>    customerAppearanceValues = new();

    [Header("Chapter")]
    [SerializeField] private string currentChapterId = "";

    [Header("Money")]
    [SerializeField] private int currentMoney = 0;

    [Header("Reputation")]
    [SerializeField] private int reputation = 0;

    [Header("Day Settlement (transient, resets each day)")]
    [SerializeField] private int dayDrinkSalesCount = 0;
    [SerializeField] private int dayDrinkRevenue     = 0;
    [SerializeField] private int dayTotalIncome      = 0;

    private HashSet<string>           _flagSet;
    private HashSet<string>           _completedSet;
    private Dictionary<string, int>   _affinity;
    private Dictionary<string, int>   _boardSlots;
    private Dictionary<string, float> _bottleAmounts;
    private Dictionary<string, int>   _customerAppearances;
    private Dictionary<string, int>   _upgradeLevels;

    public int    CurrentDay        => currentDay;
    public string CurrentChapterId  => currentChapterId;
    public int    CurrentMoney      => currentMoney;
    public int    Reputation        => reputation;
    public int    DayDrinkSalesCount => dayDrinkSalesCount;
    public int    DayDrinkRevenue    => dayDrinkRevenue;
    public int    DayTotalIncome     => dayTotalIncome;

    protected override void Awake()
    {
        base.Awake();
        RebuildRuntimeSets();
    }

    [ContextMenu("Rebuild Runtime Sets (Debug)")]
    private void RebuildRuntimeSets()
    {
        _flagSet      = new HashSet<string>(flags);
        _completedSet = new HashSet<string>(completedEpisodeIds);

        _affinity = new Dictionary<string, int>();
        int affinityCount = Mathf.Min(affinityKeys.Count, affinityValues.Count);
        for (int i = 0; i < affinityCount; i++)
            _affinity[affinityKeys[i]] = affinityValues[i];

        _boardSlots = new Dictionary<string, int>();
        int slotCount = Mathf.Min(boardSlotKeys.Count, boardSlotValues.Count);
        for (int i = 0; i < slotCount; i++)
            _boardSlots[boardSlotKeys[i]] = boardSlotValues[i];

        _bottleAmounts = new Dictionary<string, float>();
        int bottleCount = Mathf.Min(bottleAmountKeys.Count, bottleAmountValues.Count);
        for (int i = 0; i < bottleCount; i++)
            _bottleAmounts[bottleAmountKeys[i]] = bottleAmountValues[i];

        _customerAppearances = new Dictionary<string, int>();
        int appearanceCount = Mathf.Min(customerAppearanceKeys.Count, customerAppearanceValues.Count);
        for (int i = 0; i < appearanceCount; i++)
            _customerAppearances[customerAppearanceKeys[i]] = customerAppearanceValues[i];

        _upgradeLevels = new Dictionary<string, int>();
        int upgradeCount = Mathf.Min(upgradeKeys.Count, upgradeValues.Count);
        for (int i = 0; i < upgradeCount; i++)
            _upgradeLevels[upgradeKeys[i]] = upgradeValues[i];
    }

    public void LoadFrom(SaveData data)
    {
        if (data == null) return;

        currentDay          = data.dayCount;
        flags               = new List<string>(data.flags ?? new List<string>());
        completedEpisodeIds = new List<string>(data.completedEpisodeIds ?? new List<string>());
        affinityKeys        = new List<string>(data.affinityKeys ?? new List<string>());
        affinityValues      = new List<int>(data.affinityValues ?? new List<int>());
        boardSlotKeys       = new List<string>(data.boardSlotKeys ?? new List<string>());
        boardSlotValues     = new List<int>(data.boardSlotValues ?? new List<int>());
        bottleAmountKeys    = new List<string>(data.bottleAmountKeys ?? new List<string>());
        bottleAmountValues  = new List<float>(data.bottleAmountValues ?? new List<float>());
        customerAppearanceKeys   = new List<string>(data.customerAppearanceKeys ?? new List<string>());
        customerAppearanceValues = new List<int>(data.customerAppearanceValues ?? new List<int>());
        upgradeKeys        = new List<string>(data.upgradeKeys ?? new List<string>());
        upgradeValues       = new List<int>(data.upgradeValues ?? new List<int>());
        currentChapterId   = data.currentChapterId ?? "";
        currentMoney        = data.currentMoney;
        reputation          = data.reputation;
        dayDrinkSalesCount  = data.dayDrinkSalesCount;
        dayDrinkRevenue     = data.dayDrinkRevenue;
        dayTotalIncome      = data.dayTotalIncome;

        RebuildRuntimeSets();
    }

    public List<string> GetFlagList()         => new List<string>(flags);
    public List<string> GetCompletedList()    => new List<string>(completedEpisodeIds);
    public List<string> GetAffinityKeys()     => new List<string>(affinityKeys);
    public List<int>    GetAffinityValues()   => new List<int>(affinityValues);
    public List<string> GetBoardSlotKeys()    => new List<string>(boardSlotKeys);
    public List<int>    GetBoardSlotValues()  => new List<int>(boardSlotValues);
    public List<string> GetBottleAmountKeys()   => new List<string>(bottleAmountKeys);
    public List<float>  GetBottleAmountValues() => new List<float>(bottleAmountValues);
    public List<string> GetCustomerAppearanceKeys()   => new List<string>(customerAppearanceKeys);
    public List<int>    GetCustomerAppearanceValues() => new List<int>(customerAppearanceValues);
    public List<string> GetUpgradeKeys()   => new List<string>(upgradeKeys);
    public List<int>    GetUpgradeValues() => new List<int>(upgradeValues);

    // ── Flags ──────────────────────────────────────────────────

    public bool HasFlag(string flag)
    {
        if (string.IsNullOrWhiteSpace(flag)) return false;
        return _flagSet.Contains(flag);
    }

    public void SetFlag(string flag)
    {
        if (string.IsNullOrWhiteSpace(flag)) return;
        if (_flagSet.Add(flag)) flags.Add(flag);
    }

    public void ClearFlag(string flag)
    {
        if (string.IsNullOrWhiteSpace(flag)) return;
        if (_flagSet.Remove(flag)) flags.Remove(flag);
    }

    // ── Episodes ───────────────────────────────────────────────

    public bool IsEpisodeCompleted(string episodeId)
    {
        if (string.IsNullOrWhiteSpace(episodeId)) return false;
        return _completedSet.Contains(episodeId);
    }

    public void MarkEpisodeCompleted(string episodeId)
    {
        if (string.IsNullOrWhiteSpace(episodeId)) return;
        if (_completedSet.Add(episodeId)) completedEpisodeIds.Add(episodeId);
    }

    public void SetCurrentDay(int day)
    {
        currentDay = Mathf.Max(0, day);
    }

    // Rest 씬에서 하루를 시작할 때(영업 시작/기본 에피소드 시작) 호출.
    public void AdvanceDay()
    {
        currentDay++;
    }

    // ── Chapter ────────────────────────────────────────────────

    // 챕터가 실제로 바뀌는 경우, 새 챕터의 Day 1부터 다시 시작하도록 currentDay를 리셋한다.
    public void SetCurrentChapter(string chapterId)
    {
        chapterId ??= "";
        if (chapterId == currentChapterId) return;

        currentChapterId = chapterId;
        currentDay = 1;
    }

    // ── Affinity Variables ─────────────────────────────────────

    public int GetAffinity(string varName)
    {
        if (string.IsNullOrWhiteSpace(varName)) return 0;
        _affinity.TryGetValue(varName, out int value);
        return value;
    }

    public void SetAffinity(string varName, int value)
    {
        if (string.IsNullOrWhiteSpace(varName)) return;
        _affinity.TryGetValue(varName, out int current);
        int delta = value - current;
        _affinity[varName] = value;
        SyncAffinityToLists(varName, value);
        if (delta != 0)
            OnAffinityChanged?.Invoke(varName, delta);
    }

    public void AddAffinity(string varName, int delta)
    {
        if (string.IsNullOrWhiteSpace(varName)) return;
        _affinity.TryGetValue(varName, out int current);
        int next = current + delta;
        _affinity[varName] = next;
        SyncAffinityToLists(varName, next);
        if (delta != 0)
            OnAffinityChanged?.Invoke(varName, delta);
    }

    private void SyncAffinityToLists(string varName, int value)
    {
        int idx = affinityKeys.IndexOf(varName);
        if (idx >= 0)
            affinityValues[idx] = value;
        else
        {
            affinityKeys.Add(varName);
            affinityValues.Add(value);
        }
    }

    // ── Board Slot Positions ───────────────────────────────────

    public int GetBoardSlot(string episodeId)
    {
        if (string.IsNullOrWhiteSpace(episodeId)) return 0;
        _boardSlots.TryGetValue(episodeId, out int value);
        return value;
    }

    public void SetBoardSlot(string episodeId, int slotIndex)
    {
        if (string.IsNullOrWhiteSpace(episodeId)) return;
        _boardSlots[episodeId] = slotIndex;
        SyncBoardSlotToLists(episodeId, slotIndex);
    }

    public void ClearBoardSlot(string episodeId)
    {
        if (string.IsNullOrWhiteSpace(episodeId)) return;
        if (!_boardSlots.Remove(episodeId)) return;
        int idx = boardSlotKeys.IndexOf(episodeId);
        if (idx >= 0)
        {
            boardSlotKeys.RemoveAt(idx);
            boardSlotValues.RemoveAt(idx);
        }
    }

    private void SyncBoardSlotToLists(string episodeId, int slotIndex)
    {
        int idx = boardSlotKeys.IndexOf(episodeId);
        if (idx >= 0)
            boardSlotValues[idx] = slotIndex;
        else
        {
            boardSlotKeys.Add(episodeId);
            boardSlotValues.Add(slotIndex);
        }
    }

    // ── Bottle Amounts ─────────────────────────────────────────

    public float GetBottleAmount(string bottleId, float defaultValue)
    {
        if (string.IsNullOrWhiteSpace(bottleId)) return defaultValue;
        return _bottleAmounts.TryGetValue(bottleId, out float value) ? value : defaultValue;
    }

    public void SetBottleAmount(string bottleId, float value)
    {
        if (string.IsNullOrWhiteSpace(bottleId)) return;
        float safeValue = Mathf.Max(0f, value);
        _bottleAmounts[bottleId] = safeValue;
        SyncBottleAmountToLists(bottleId, safeValue);
    }

    private void SyncBottleAmountToLists(string bottleId, float value)
    {
        int idx = bottleAmountKeys.IndexOf(bottleId);
        if (idx >= 0)
            bottleAmountValues[idx] = value;
        else
        {
            bottleAmountKeys.Add(bottleId);
            bottleAmountValues.Add(value);
        }
    }

    // Adds delta to the current amount, clamped to [0, max]. Negative delta consumes stock.
    public float AddBottleAmount(string bottleId, float delta, float max)
    {
        float next = Mathf.Clamp(GetBottleAmount(bottleId, 0f) + delta, 0f, max);
        SetBottleAmount(bottleId, next);
        return next;
    }

    // ── Customer Appearances ───────────────────────────────────

    public int GetCustomerAppearance(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId)) return 0;
        _customerAppearances.TryGetValue(characterId, out int value);
        return value;
    }

    public void IncrementCustomerAppearance(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId)) return;
        _customerAppearances.TryGetValue(characterId, out int current);
        int next = current + 1;
        _customerAppearances[characterId] = next;
        SyncCustomerAppearanceToLists(characterId, next);
    }

    private void SyncCustomerAppearanceToLists(string characterId, int value)
    {
        int idx = customerAppearanceKeys.IndexOf(characterId);
        if (idx >= 0)
            customerAppearanceValues[idx] = value;
        else
        {
            customerAppearanceKeys.Add(characterId);
            customerAppearanceValues.Add(value);
        }
    }

    // ── Money ────────────────────────────────────────────────────

    public void AddMoney(int delta)
    {
        currentMoney += delta;
    }

    // Spends money only if the balance is sufficient. Returns false and does nothing otherwise.
    public bool TrySpendMoney(int amount)
    {
        if (amount <= 0) return true;
        if (currentMoney < amount) return false;
        currentMoney -= amount;
        return true;
    }

    // ── Reputation ───────────────────────────────────────────────

    public void AddReputation(int delta)
    {
        reputation += delta;
    }

    // ── Upgrade Levels ──────────────────────────────────────────

    public int GetUpgradeLevel(string upgradeId)
    {
        if (string.IsNullOrWhiteSpace(upgradeId)) return 0;
        _upgradeLevels.TryGetValue(upgradeId, out int value);
        return value;
    }

    public void SetUpgradeLevel(string upgradeId, int level)
    {
        if (string.IsNullOrWhiteSpace(upgradeId)) return;
        _upgradeLevels[upgradeId] = level;
        SyncUpgradeLevelToLists(upgradeId, level);
    }

    private void SyncUpgradeLevelToLists(string upgradeId, int level)
    {
        int idx = upgradeKeys.IndexOf(upgradeId);
        if (idx >= 0)
            upgradeValues[idx] = level;
        else
        {
            upgradeKeys.Add(upgradeId);
            upgradeValues.Add(level);
        }
    }

    // ── Day Settlement (transient) ─────────────────────────────

    public void RecordDrinkSale(int revenue)
    {
        dayDrinkSalesCount += 1;
        dayDrinkRevenue    += revenue;
        dayTotalIncome     += revenue;
    }

    public void AddDayIncome(int amount)
    {
        dayTotalIncome += amount;
    }

    public void ResetDaySettlement()
    {
        dayDrinkSalesCount = 0;
        dayDrinkRevenue    = 0;
        dayTotalIncome     = 0;
    }
}
