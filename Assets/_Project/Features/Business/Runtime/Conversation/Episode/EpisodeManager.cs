using System;
using System.Collections.Generic;
using Slainte.Content;
using Slainte.Shared.Lifecycle;
using UnityEngine;

// 전체 에피소드 카탈로그를 로드하고, 작전판 노출 여부(해금 조건)·필수 에피소드 큐잉·
// 현재 재생 중인 에피소드 상태를 관리하는 싱글톤. Rest 보드의 기본 에피소드 선택,
// DayFlowController의 필수 에피소드 자동 진행, Business 인카운터(TryStartBusinessEncounter)
// 세 가지 진입 경로가 모두 이 매니저를 거쳐 CurrentPlayingEpisodeID를 갱신한다.
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
        allEpisodes.AddRange(Resources.LoadAll<EpisodeData>(
            ProjectResourcePaths.NarrativeEpisodes));
        Debug.Log($"[EpisodeManager] {allEpisodes.Count} episode(s) loaded.");
    }

    public List<EpisodeData> GetAvailableEpisodes() => GetSelectableDefaultEpisodes();

    // Rest 보드에서 플레이어가 직접 선택 가능한 에피소드만 (기본 에피소드). 필수 에피소드는 DayFlowController가 자동으로 큐잉함.
    public List<EpisodeData> GetBoardEpisodes() => GetSelectableDefaultEpisodes();

    private List<EpisodeData> GetSelectableDefaultEpisodes()
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
    // dayOffset: 0 = 오늘(발동 체크, AdvanceDay 이후 시점), 1 = 내일(게이트 lookahead, 보드가 열리는 AdvanceDay 이전 시점)
    public EpisodeData GetNextMandatoryEpisode(int dayOffset = 0)
    {
        GameProgress gp = GameProgress.Instance;
        if (gp == null) return null;

        foreach (var ep in allEpisodes)
        {
            if (ep.episodeType != EpisodeType.Mandatory) continue;
            if (gp.IsEpisodeCompleted(ep.episodeId)) continue;
            if (!string.IsNullOrEmpty(gp.CurrentChapterId) && ep.chapterId != gp.CurrentChapterId) continue;
            if (!IsUnlocked(ep, gp, dayOffset)) continue;
            return ep;
        }
        return null;
    }

    public bool HasPendingMandatoryEpisode() => GetNextMandatoryEpisode(0) != null;

    // 다음 영업 시작(day+1) 시점에 발동될 필수 에피소드가 있는지 — 작전판 게이트를 하루 앞당겨 걸기 위한 lookahead
    public bool HasUpcomingMandatoryEpisode() => GetNextMandatoryEpisode(1) != null;

    // 해금 조건 — 만족하면 작전판에 노출됨
    public bool IsUnlocked(EpisodeData ep, GameProgress gp) => IsUnlocked(ep, gp, 0);

    public bool IsUnlocked(EpisodeData ep, GameProgress gp, int dayOffset)
    {
        if (ep == null || gp == null) return false;
        return EvaluateCondition(ep.triggerCondition, gp, gp.CurrentDay + dayOffset);
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
        => EvaluateCondition(cond, gp, gp != null ? gp.CurrentDay : 0);

    // effectiveDay: day 비교에 쓸 기준일(게이트 lookahead용으로 gp.CurrentDay와 다를 수 있음). 그 외 조건은 gp 실시간 상태를 그대로 사용.
    public bool EvaluateCondition(EpisodeTriggerCondition cond, GameProgress gp, int effectiveDay)
    {
        if (cond == null) return true;
        if (gp == null) return false;

        if (effectiveDay < cond.minDay) return false;
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

    // 복구 명령(디버그/장애 대응)으로 현재 진행 중인 에피소드를 강제로 완료 처리한다.
    // EpisodeRunner가 실행 중이면 러너를 통해 정상 종료시키고, 그게 아니라 Business
    // 인카운터였다면 그 완료 경로로, 둘 다 아니면(상태 불일치) 직접 ClearEpisode로 정리한다.
    public bool TryForceCompleteCurrentEpisode(out string completedEpisodeId)
    {
        completedEpisodeId = CurrentPlayingEpisodeID;
        EpisodeRunner runner = UnityEngine.Object.FindFirstObjectByType<EpisodeRunner>();
        GameProgress progress = GameProgress.Instance;

        if (progress == null)
        {
            Debug.LogError("[EpisodeManager] GameProgress가 없어 현재 에피소드를 강제 완료할 수 없습니다.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(completedEpisodeId))
            completedEpisodeId = runner?.CurrentEpisodeId;
        if (string.IsNullOrWhiteSpace(completedEpisodeId))
            return false;

        if (runner != null && runner.IsRunning)
        {
            if (!string.Equals(
                    runner.CurrentEpisodeId,
                    completedEpisodeId,
                    StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError(
                    "[EpisodeManager] 현재 에피소드 ID와 EpisodeRunner 상태가 달라 강제 완료하지 않습니다: "
                    + $"manager={completedEpisodeId}, runner={runner.CurrentEpisodeId}");
                return false;
            }

            if (!runner.TryForceCompleteForRecovery())
                return false;
        }
        else if (IsBusinessEncounterActive)
        {
            CompleteBusinessEncounter(completedEpisodeId);
        }
        else
        {
            ClearEpisode(completedEpisodeId, saveImmediately: false);
            DayFlowController.Instance?.OnEpisodeCompleted();
        }

        DataManager.Instance?.Save();
        Debug.LogWarning(
            $"[EpisodeManager] 복구 명령으로 에피소드를 강제 완료했습니다: {completedEpisodeId}");
        return progress.IsEpisodeCompleted(completedEpisodeId);
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
