using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EpisodeTriggerManager : MonoBehaviour
{
    [SerializeField] private GameProgress progress;
    [SerializeField] private List<EpisodeData> episodes = new();
    [SerializeField] private string episodeSceneName = "EpisodeScene";

    public void CheckAndLaunchEpisode()
    {
        var episode = FindFirstAvailableEpisode();
        if (episode == null) return;

        EpisodeRuntimeContext.PendingEpisode = episode;
        SceneManager.LoadScene(episodeSceneName);
    }

    public EpisodeData FindFirstAvailableEpisode()
    {
        if (progress == null)
            progress = GameProgress.Instance;

        if (progress == null)
        {
            Debug.LogWarning("GameProgress is missing.");
            return null;
        }

        for (int i = 0; i < episodes.Count; i++)
        {
            var ep = episodes[i];
            if (ep == null) continue;
            if (progress.IsEpisodeCompleted(ep.episodeId)) continue;
            if (CanStart(ep, progress)) return ep;
        }

        return null;
    }

    public bool CanStart(EpisodeData episode, GameProgress gp)
    {
        if (episode == null || gp == null) return false;

        var cond = episode.triggerCondition;
        if (cond == null) return true;

        if (gp.CurrentDay < cond.minDay)
            return false;

        for (int i = 0; i < cond.requiredFlags.Count; i++)
        {
            if (!gp.HasFlag(cond.requiredFlags[i]))
                return false;
        }

        for (int i = 0; i < cond.blockedFlags.Count; i++)
        {
            if (gp.HasFlag(cond.blockedFlags[i]))
                return false;
        }

        for (int i = 0; i < cond.prerequisiteEpisodeIds.Count; i++)
        {
            if (!gp.IsEpisodeCompleted(cond.prerequisiteEpisodeIds[i]))
                return false;
        }

        return true;
    }
}