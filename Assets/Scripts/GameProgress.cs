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

    [Header("Business Progress")]
    [SerializeField] private int money;
    [SerializeField] private int reputation;
    [SerializeField] private BusinessDaySnapshot businessDay = new();
    [SerializeField] private List<CustomerVisitHistorySnapshot> customerVisitHistory = new();

    private HashSet<string>           _flagSet;
    private HashSet<string>           _completedSet;
    private Dictionary<string, int>   _affinity;
    private Dictionary<string, int>   _boardSlots;
    private Dictionary<string, float> _bottleAmounts;
    private Dictionary<string, CustomerVisitHistorySnapshot> _customerVisitHistory;

    public int CurrentDay => currentDay;
    public int Money => money;
    public int Reputation => reputation;
    public event System.Action<string, float> BottleAmountChanged;

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

        _customerVisitHistory = new Dictionary<string, CustomerVisitHistorySnapshot>(
            StringComparer.OrdinalIgnoreCase);
        customerVisitHistory ??= new List<CustomerVisitHistorySnapshot>();
        for (int i = 0; i < customerVisitHistory.Count; i++)
        {
            CustomerVisitHistorySnapshot history = customerVisitHistory[i];
            if (history != null && !string.IsNullOrWhiteSpace(history.visitKey))
                _customerVisitHistory[history.visitKey] = history;
        }
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
        money                = data.money;
        reputation           = data.reputation;
        businessDay          = data.businessDay != null ? data.businessDay.Clone() : new BusinessDaySnapshot();
        customerVisitHistory = CloneVisitHistory(data.customerVisitHistory);

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
    public BusinessDaySnapshot GetBusinessDaySnapshot() =>
        businessDay != null ? businessDay.Clone() : new BusinessDaySnapshot();
    public List<CustomerVisitHistorySnapshot> GetCustomerVisitHistory() =>
        CloneVisitHistory(customerVisitHistory);

    public int GetLastCustomerVisitDay(string visitKey)
    {
        if (string.IsNullOrWhiteSpace(visitKey) || _customerVisitHistory == null)
            return -1;

        return _customerVisitHistory.TryGetValue(visitKey, out CustomerVisitHistorySnapshot history)
            ? history.lastVisitedDay
            : -1;
    }

    public void RecordCustomerVisit(string visitKey, int day)
    {
        if (string.IsNullOrWhiteSpace(visitKey))
            return;

        _customerVisitHistory ??= new Dictionary<string, CustomerVisitHistorySnapshot>(
            StringComparer.OrdinalIgnoreCase);
        customerVisitHistory ??= new List<CustomerVisitHistorySnapshot>();
        string normalizedKey = visitKey.Trim();
        if (!_customerVisitHistory.TryGetValue(normalizedKey, out CustomerVisitHistorySnapshot history))
        {
            history = new CustomerVisitHistorySnapshot { visitKey = normalizedKey };
            _customerVisitHistory[normalizedKey] = history;
            customerVisitHistory.Add(history);
        }

        history.lastVisitedDay = day;
        history.totalVisits++;
    }

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
        BottleAmountChanged?.Invoke(bottleId, safeValue);
    }

    public void AddMoney(int amount)
    {
        money = Mathf.Max(0, money + amount);
    }

    public void AddReputation(int amount)
    {
        reputation += amount;
    }

    public void SetBusinessDaySnapshot(BusinessDaySnapshot snapshot)
    {
        businessDay = snapshot != null ? snapshot.Clone() : new BusinessDaySnapshot();
    }

    public void AdvanceBusinessSequence()
    {
        if (businessDay == null)
            businessDay = new BusinessDaySnapshot();

        businessDay.currentIndex = Mathf.Min(
            businessDay.currentIndex + 1,
            businessDay.entries != null ? businessDay.entries.Count : 0);
        businessDay.isCompleted = businessDay.entries == null
            || businessDay.currentIndex >= businessDay.entries.Count;
    }

    public void ClearBusinessDaySnapshot()
    {
        businessDay = new BusinessDaySnapshot();
    }

    private static List<CustomerVisitHistorySnapshot> CloneVisitHistory(
        List<CustomerVisitHistorySnapshot> source)
    {
        List<CustomerVisitHistorySnapshot> clone = new();
        if (source == null)
            return clone;

        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] != null)
                clone.Add(source[i].Clone());
        }

        return clone;
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
}
