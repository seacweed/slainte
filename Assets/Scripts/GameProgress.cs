using System.Collections.Generic;
using UnityEngine;

public class GameProgress : MonoSingleton<GameProgress>
{
    [SerializeField] private int currentDay = 1;
    [SerializeField] private List<string> flags = new();
    [SerializeField] private List<string> completedEpisodeIds = new();

    [Header("Numeric Variables")]
    [SerializeField] private List<string> varKeys   = new();
    [SerializeField] private List<int>    varValues = new();

    private HashSet<string>         _flagSet;
    private HashSet<string>         _completedSet;
    private Dictionary<string, int> _vars;

    public int CurrentDay => currentDay;

    protected override void Awake()
    {
        base.Awake();
        RebuildRuntimeSets();
    }

    private void RebuildRuntimeSets()
    {
        _flagSet      = new HashSet<string>(flags);
        _completedSet = new HashSet<string>(completedEpisodeIds);

        _vars = new Dictionary<string, int>();
        int count = Mathf.Min(varKeys.Count, varValues.Count);
        for (int i = 0; i < count; i++)
            _vars[varKeys[i]] = varValues[i];
    }

    public void LoadFrom(SaveData data)
    {
        if (data == null) return;

        currentDay          = data.dayCount;
        flags               = new List<string>(data.flags ?? new List<string>());
        completedEpisodeIds = new List<string>(data.completedEpisodeIds ?? new List<string>());
        varKeys             = new List<string>(data.varKeys ?? new List<string>());
        varValues           = new List<int>(data.varValues ?? new List<int>());

        RebuildRuntimeSets();
    }

    public List<string> GetFlagList()      => new List<string>(flags);
    public List<string> GetCompletedList() => new List<string>(completedEpisodeIds);
    public List<string> GetVarKeys()       => new List<string>(varKeys);
    public List<int>    GetVarValues()     => new List<int>(varValues);

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

    public int GetVar(string varName)
    {
        if (string.IsNullOrWhiteSpace(varName)) return 0;
        _vars.TryGetValue(varName, out int value);
        return value;
    }

    public void SetVar(string varName, int value)
    {
        if (string.IsNullOrWhiteSpace(varName)) return;
        _vars[varName] = value;
        SyncVarToLists(varName, value);
    }

    public void AddVar(string varName, int delta)
    {
        if (string.IsNullOrWhiteSpace(varName)) return;
        _vars.TryGetValue(varName, out int current);
        int next = current + delta;
        _vars[varName] = next;
        SyncVarToLists(varName, next);
    }

    private void SyncVarToLists(string varName, int value)
    {
        int idx = varKeys.IndexOf(varName);
        if (idx >= 0)
            varValues[idx] = value;
        else
        {
            varKeys.Add(varName);
            varValues.Add(value);
        }
    }
}
