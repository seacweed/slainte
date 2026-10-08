using System;
using System.Collections.Generic;
using Slainte.Business;
using Slainte.Content;
using Slainte.Shared.Lifecycle;
using UnityEngine;

// 전체 에피소드 카탈로그를 로드해 하루 일정표(DayScheduleIndex)로 색인하고, 영업 중 인카운터 실행과
// 현재 재생 중인 에피소드 상태를 관리하는 싱글톤. 모든 에피소드는 영업 슬롯에 배정된 인카운터이므로
// 실행 진입점은 BusinessShiftController가 호출하는 TryStartBusinessEncounter 하나뿐이다.
public class EpisodeManager : MonoSingleton<EpisodeManager>, IDayScheduleSource
{
    private readonly List<EpisodeData> allEpisodes = new();
    private readonly DayScheduleIndex schedule = new();
    private Action businessEncounterCompleted;
    private string queuedDebugEncounterId;

    public string CurrentPlayingEpisodeID { get; private set; }
    public bool IsBusinessEncounterActive { get; private set; }
    public IReadOnlyList<EpisodeData> AllEpisodes => allEpisodes;

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
        schedule.Rebuild(allEpisodes);
        Debug.Log($"[EpisodeManager] {allEpisodes.Count} episode(s) loaded.");
    }

    public void CollectSlotCandidates(
        string chapterId,
        int day,
        int slot,
        List<EpisodeData> results)
    {
        schedule.CollectSlotCandidates(chapterId, day, slot, results);
    }

    public EpisodeData GetEpisodeData(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < allEpisodes.Count; i++)
            if (allEpisodes[i].episodeId == id) return allEpisodes[i];
        return null;
    }

    // 개발용(플레이테스트 런처): 다음 영업의 1번 슬롯을 일정·등장 조건과 무관하게 이 에피소드로 강제한다.
    // 영업 씬이 로드되며 자동으로 시작되므로 상태 전환 전에 미리 예약해 두고 영업 시작 시 소비한다.
    public void QueueDebugEncounter(string episodeId)
    {
        queuedDebugEncounterId = episodeId;
    }

    public EpisodeData ConsumeQueuedDebugEncounter()
    {
        string id = queuedDebugEncounterId;
        queuedDebugEncounterId = null;
        return GetEpisodeData(id);
    }

    public bool TryStartBusinessEncounter(string episodeId, Action onCompleted)
    {
        return TryStartBusinessEncounter(GetEpisodeData(episodeId), onCompleted);
    }

    // id가 아니라 EpisodeData를 직접 받는 이유: 플레이테스트가 카탈로그에 없는 런타임 복제본으로도
    // 같은 실행 경로를 검증할 수 있어야 하기 때문.
    public bool TryStartBusinessEncounter(EpisodeData episode, Action onCompleted)
    {
        string episodeId = episode != null ? episode.episodeId : null;
        if (string.IsNullOrWhiteSpace(episodeId)
            || IsBusinessEncounterActive
            || !string.IsNullOrWhiteSpace(CurrentPlayingEpisodeID))
            return false;

        EpisodeRunner runner = UnityEngine.Object.FindFirstObjectByType<EpisodeRunner>();
        GameProgress progress = GameProgress.Instance;
        if ((progress != null && progress.IsEpisodeCompleted(episodeId)) || runner == null)
        {
            Debug.LogError(
                $"[EpisodeManager] 미완료 에피소드를 찾거나 실행할 수 없습니다: {episodeId}");
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
        DayFlowController.Instance?.NotifyEpisodeCompleted(episodeId);

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
