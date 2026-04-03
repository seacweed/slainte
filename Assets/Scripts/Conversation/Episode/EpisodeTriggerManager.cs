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

        return true;
    }
}
