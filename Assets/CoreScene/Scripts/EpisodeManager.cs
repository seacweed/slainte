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
            if (ep.episodeType != EpisodeType.Default) continue;
            if (gp != null && gp.IsEpisodeCompleted(ep.episodeId)) continue;
            if (IsUnlocked(ep, gp)) available.Add(ep);
        }
        return available;
    }

    // Rest 보드에서 플레이어가 직접 선택 가능한 에피소드만 (기본 에피소드). 필수 에피소드는 DayFlowController가 자동으로 큐잉함.
    public List<EpisodeData> GetBoardEpisodes()
    {
        GameProgress gp = GameProgress.Instance;
        var visible = new List<EpisodeData>();
        foreach (var ep in allEpisodes)
        {
            if (ep.episodeType != EpisodeType.Default) continue;
            if (gp != null && gp.IsEpisodeCompleted(ep.episodeId)) continue;
            if (IsVisible(ep, gp)) visible.Add(ep);
        }
        return visible;
    }

    // 챕터 스코프로 다음에 진행해야 할 미완료 필수 에피소드 1개를 반환 (없으면 null).
    public EpisodeData GetNextMandatoryEpisode()
    {
        GameProgress gp = GameProgress.Instance;
        if (gp == null) return null;

        foreach (var ep in allEpisodes)
        {
            if (ep.episodeType != EpisodeType.Mandatory) continue;
            if (gp.IsEpisodeCompleted(ep.episodeId)) continue;
            if (!string.IsNullOrEmpty(gp.CurrentChapterId) && ep.chapterId != gp.CurrentChapterId) continue;
            if (!IsUnlocked(ep, gp)) continue;
            return ep;
        }
        return null;
    }

    public bool HasPendingMandatoryEpisode() => GetNextMandatoryEpisode() != null;

    public bool IsVisible(EpisodeData ep, GameProgress gp)
    {
        if (ep == null) return false;
        
        // 해금 조건이 참이라면 무조건 보입니다.
        if (IsUnlocked(ep, gp)) return true;

        if (gp == null) return false;

        // 해금 조건은 못 채웠지만, 플레이 중 에피소드 정보를 얻은 경우(플래그 존재 시) 보드에 표시됩니다.
        if (gp.HasFlag($"{ep.episodeId}_Discovered")) return true;

        return false;
    }

    // 해금 조건 — 만족하면 작전판에 노출됨
    public bool IsUnlocked(EpisodeData ep, GameProgress gp)
    {
        if (ep == null || gp == null) return false;
        return EvaluateCondition(ep.triggerCondition, gp);
    }

    // 플레이 조건 — 만족해야 Play 버튼이 활성화됨 (보드에 이미 뜬 에피소드 대상)
    public bool IsPlayable(EpisodeData ep, GameProgress gp)
    {
        if (ep == null || gp == null) return false;
        return EvaluateCondition(ep.playCondition, gp);
    }

    // 선택 조건 옵션 하나(조건은 항상 하나)를 평가
    public bool EvaluateSelectCondition(SelectSingleCondition cond, GameProgress gp)
    {
        if (cond == null || cond.type == SelectConditionType.None) return true;
        if (gp == null) return false;

        return cond.type switch
        {
            SelectConditionType.MinDay => gp.CurrentDay >= cond.minDay,
            SelectConditionType.RequiredFlag => gp.HasFlag(cond.requiredFlag),
            SelectConditionType.PrerequisiteEpisode => gp.IsEpisodeCompleted(cond.prerequisiteEpisodeId),
            SelectConditionType.RequiredVar => new VarCondition { varName = cond.varName, op = cond.varOp, threshold = cond.varThreshold }.Evaluate(gp.GetAffinity(cond.varName)),
            _ => true
        };
    }

    // 해금/플레이 조건(여러 항목이 AND로 결합) 평가에 재사용
    public bool EvaluateCondition(EpisodeTriggerCondition cond, GameProgress gp)
    {
        if (cond == null) return true;
        if (gp == null) return false;

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
            if (!vc.Evaluate(gp.GetAffinity(vc.varName))) return false;
        }

        for (int i = 0; i < cond.requiredCustomerAppearances.Count; i++)
        {
            CustomerAppearanceCondition cac = cond.requiredCustomerAppearances[i];
            if (gp.GetCustomerAppearance(cac.characterId) < cac.count) return false;
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
