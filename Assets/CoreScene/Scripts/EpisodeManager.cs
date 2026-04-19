using System.Collections.Generic;
using UnityEngine;

public class EpisodeManager : MonoSingleton<EpisodeManager>
{
    private List<EpisodeData> allEpisodes = new();
    public string CurrentPlayingEpisodeID { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        LoadAllEpisodes();
    }

    private void LoadAllEpisodes()
    {
        allEpisodes.Clear();
        allEpisodes.AddRange(Resources.LoadAll<EpisodeData>("EpisodeData"));
        Debug.Log($"[EpisodeManager] {allEpisodes.Count} episode(s) loaded.");
    }

    public List<EpisodeData> GetAvailableEpisodes()
    {
        GameProgress gp = GameProgress.Instance;
        var available = new List<EpisodeData>();
        foreach (var ep in allEpisodes)
        {
            if (gp != null && gp.IsEpisodeCompleted(ep.episodeId)) continue;
            if (CanStart(ep, gp)) available.Add(ep);
        }
        return available;
    }

    public List<EpisodeData> GetBoardEpisodes()
    {
        GameProgress gp = GameProgress.Instance;
        var visible = new List<EpisodeData>();
        foreach (var ep in allEpisodes)
        {
            if (gp != null && gp.IsEpisodeCompleted(ep.episodeId)) continue;
            if (IsVisible(ep, gp)) visible.Add(ep);
        }
        return visible;
    }

    public bool IsVisible(EpisodeData ep, GameProgress gp)
    {
        if (ep == null) return false;
        
        // 시작 조건이 참이라면 무조건 보입니다.
        if (CanStart(ep, gp)) return true;

        if (gp == null) return false;

        // 시작 조건은 못 채웠지만, 플레이 중 에피소드 정보를 얻은 경우(플래그 존재 시) 보드에 표시됩니다.
        if (gp.HasFlag($"{ep.episodeId}_Discovered")) return true;

        return false;
    }

    public bool CanStart(EpisodeData ep, GameProgress gp)
    {
        if (ep == null || gp == null) return false;

        EpisodeTriggerCondition cond = ep.triggerCondition;
        if (cond == null) return true;

        if (gp.CurrentDay < cond.minDay) return false;

        for (int i = 0; i < cond.requiredFlags.Count; i++)
            if (!gp.HasFlag(cond.requiredFlags[i])) return false;

        for (int i = 0; i < cond.blockedFlags.Count; i++)
            if (gp.HasFlag(cond.blockedFlags[i])) return false;

        for (int i = 0; i < cond.prerequisiteEpisodeIds.Count; i++)
            if (!gp.IsEpisodeCompleted(cond.prerequisiteEpisodeIds[i])) return false;

        for (int i = 0; i < cond.requiredVars.Count; i++)
        {
            VarCondition vc = cond.requiredVars[i];
            if (!vc.Evaluate(gp.GetVar(vc.varName))) return false;
        }

        return true;
    }

    public EpisodeData GetEpisodeData(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < allEpisodes.Count; i++)
            if (allEpisodes[i].episodeId == id) return allEpisodes[i];
        return null;
    }

    public void StartEpisode(string episodeId)
    {
        CurrentPlayingEpisodeID = episodeId;
        DataManager.Instance?.Save();
        GameManager.Instance?.ChangeState(GameState.Episode);
    }

    public void ClearEpisode(string episodeId)
    {
        GameProgress.Instance?.MarkEpisodeCompleted(episodeId);
        DataManager.Instance?.Save();
        Debug.Log($"[EpisodeManager] Episode cleared: {episodeId}");
    }
}
