using System.Collections.Generic;
using UnityEngine;

public class EpisodeTriggerManager : MonoBehaviour
{
    [SerializeField] private GameProgress    progress;
    [SerializeField] private GameModeManager modeManager;
    [SerializeField] private EpisodeRunner episodeRunner;
    [SerializeField] private List<EpisodeData> episodes = new();

    public void CheckAndLaunchEpisode()
    {
        EpisodeData episode = FindFirstAvailableEpisode();
        if (episode == null) return;

        modeManager?.RequestModeChange(GameMode.EpisodeMode);
        episodeRunner?.Begin(episode);
    }

    // RestScene의 상황판에서 선택된 에피소드 ID로 직접 에피소드를 시작합니다.
    // episodeId가 비어있거나 매칭되는 앱소드가 없으면 FindFirstAvailableEpisode로 폴백합니다.
    public void LaunchEpisodeById(string episodeId)
    {
        EpisodeData episode = null;

        if (!string.IsNullOrEmpty(episodeId))
        {
            for (int i = 0; i < episodes.Count; i++)
            {
                if (episodes[i] != null && episodes[i].episodeId == episodeId)
                {
                    episode = episodes[i];
                    break;
                }
            }

            if (episode == null)
            {
                Debug.LogWarning($"[EpisodeTriggerManager] ID '{episodeId}'에 해당하는 EpisodeData가 없습니다. 첫 번째 가능한 에피소드로 폴백합니다.");
            }
        }

        // 폴백: 조건에 맞는 첫 번째 에피소드 선택
        if (episode == null)
            episode = FindFirstAvailableEpisode();

        if (episode == null)
        {
            Debug.LogWarning("[EpisodeTriggerManager] 실행 가능한 에피소드가 없습니다.");
            return;
        }

        modeManager?.RequestModeChange(GameMode.EpisodeMode);
        episodeRunner?.Begin(episode);
    }

    public EpisodeData FindFirstAvailableEpisode()
    {
        if (progress == null)
            progress = GameProgress.Instance;

        if (progress == null)
        {
            Debug.LogWarning("[EpisodeTriggerManager] GameProgress is missing.");
            return null;
        }

        for (int i = 0; i < episodes.Count; i++)
        {
            EpisodeData ep = episodes[i];
            if (ep == null) continue;
            if (progress.IsEpisodeCompleted(ep.episodeId)) continue;
            if (CanStart(ep, progress)) return ep;
        }

        return null;
    }

    public bool CanStart(EpisodeData episode, GameProgress gp)
    {
        if (episode == null || gp == null) return false;

        EpisodeTriggerCondition cond = episode.triggerCondition;
        if (cond == null) return true;

        if (gp.CurrentDay < cond.minDay)
            return false;

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
}
