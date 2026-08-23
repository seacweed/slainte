using System;
using System.Collections.Generic;
using UnityEngine;

public class EpisodeManager : MonoSingleton<EpisodeManager>
{
    private List<EpisodeData> allEpisodes = new();
    private Action businessEncounterCompleted;
    public string CurrentPlayingEpisodeID { get; private set; }
    public bool IsBusinessEncounterActive { get; private set; }

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
            if (IsUnlocked(ep, gp)) visible.Add(ep);
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
            SelectConditionType.MinMoney => gp.CurrentMoney >= cond.minMoney,
            _ => true
        };
    }

    // 해금/플레이 조건(여러 항목이 AND로 결합) 평가에 재사용
    public bool EvaluateCondition(EpisodeTriggerCondition cond, GameProgress gp)
    {
        if (cond == null) return true;
        if (gp == null) return false;

        if (gp.CurrentDay < cond.minDay) return false;
        if (gp.CurrentMoney < cond.minMoney) return false;

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

    public bool TryStartBusinessEncounter(string episodeId, Action onCompleted)
    {
        if (string.IsNullOrWhiteSpace(episodeId)
            || IsBusinessEncounterActive
            || !string.IsNullOrWhiteSpace(CurrentPlayingEpisodeID))
            return false;

        EpisodeData episode = GetEpisodeData(episodeId);
        EpisodeRunner runner = UnityEngine.Object.FindFirstObjectByType<EpisodeRunner>();
        GameProgress progress = GameProgress.Instance;
        if (episode == null
            || episode.episodeType != EpisodeType.Encounter
            || (progress != null && progress.IsEpisodeCompleted(episodeId))
            || runner == null)
        {
            Debug.LogError(
                $"[EpisodeManager] 미완료 Encounter 에피소드를 찾거나 실행할 수 없습니다: {episodeId}");
            return false;
        }

        GameState state = GameManager.Instance != null
            ? GameManager.Instance.CurrentState
            : GameState.None;
        if (state != GameState.None && state != GameState.Business)
        {
            Debug.LogWarning($"[EpisodeManager] Business 상태가 아니어서 영업 인카운터를 시작하지 않습니다: {state}");
            return false;
        }

        CurrentPlayingEpisodeID = episodeId;
        IsBusinessEncounterActive = true;
        businessEncounterCompleted = onCompleted;

        bool started = runner.BeginBusinessEncounter(
            episode,
            () => CompleteBusinessEncounter(episodeId));
        if (started)
            return true;

        CurrentPlayingEpisodeID = null;
        IsBusinessEncounterActive = false;
        businessEncounterCompleted = null;
        return false;
    }

    private void CompleteBusinessEncounter(string episodeId)
    {
        ClearEpisode(episodeId, saveImmediately: false);
        IsBusinessEncounterActive = false;

        Action callback = businessEncounterCompleted;
        businessEncounterCompleted = null;
        callback?.Invoke();
    }

    public void ClearEpisode(string episodeId)
    {
        ClearEpisode(episodeId, saveImmediately: true);
    }

    private void ClearEpisode(string episodeId, bool saveImmediately)
    {
        GameProgress.Instance?.MarkEpisodeCompleted(episodeId);
        if (CurrentPlayingEpisodeID == episodeId)
            CurrentPlayingEpisodeID = null;
        if (saveImmediately)
            DataManager.Instance?.Save();
        Debug.Log($"[EpisodeManager] Episode cleared: {episodeId}");
    }
}
