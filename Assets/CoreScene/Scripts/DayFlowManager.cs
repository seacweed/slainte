using UnityEngine;

public class DayFlowManager : MonoSingleton<DayFlowManager>
{
    public bool StartInitialEpisode(string episodeId)
    {
        return StartEpisode(episodeId, false);
    }

    public bool StartEpisodeFromRest(string episodeId)
    {
        return StartEpisode(episodeId, true);
    }

    public void CompleteEpisode(string episodeId)
    {
        if (string.IsNullOrWhiteSpace(episodeId))
        {
            Debug.LogError("[DayFlowManager] Cannot complete an episode without an ID.");
            return;
        }

        EpisodeManager.Instance.ClearEpisode(episodeId);
        GameManager.Instance.ChangeState(GameState.Business);
    }

    public void CompleteBusinessDay()
    {
        GameProgress progress = GameProgress.Instance;
        if (progress != null)
        {
            progress.SetCurrentDay(progress.CurrentDay + 1);
        }

        DataManager.Instance.Save();
        GameManager.Instance.ChangeState(GameState.Rest);
    }

    private static bool StartEpisode(string episodeId, bool requireRestState)
    {
        if (string.IsNullOrWhiteSpace(episodeId))
        {
            Debug.LogError("[DayFlowManager] Cannot start an episode without an ID.");
            return false;
        }

        GameManager gameManager = GameManager.Instance;
        if (requireRestState
            && gameManager.CurrentState != GameState.Rest
            && gameManager.CurrentState != GameState.None)
        {
            Debug.LogWarning(
                $"[DayFlowManager] Episode '{episodeId}' can only start from Rest. Current state: {gameManager.CurrentState}");
            return false;
        }

        EpisodeManager episodeManager = EpisodeManager.Instance;
        EpisodeData episode = episodeManager.GetEpisodeData(episodeId);
        if (episode == null)
        {
            Debug.LogError($"[DayFlowManager] Episode data not found: {episodeId}");
            return false;
        }

        GameProgress progress = GameProgress.Instance;
        if (progress != null && !episodeManager.CanStart(episode, progress))
        {
            Debug.LogWarning($"[DayFlowManager] Episode start conditions are not met: {episodeId}");
            return false;
        }

        episodeManager.StartEpisode(episodeId);
        return true;
    }
}
