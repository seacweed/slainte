using System;
using Slainte.Business;
using UnityEngine;

public enum GameState
{
    None,
    Episode,
    Business,
    Rest
}

public class GameManager : MonoSingleton<GameManager>
{
    public GameState CurrentState { get; private set; }

    public void ChangeState(GameState newState)
    {
        if (CurrentState == newState) return;

        if (CurrentState == GameState.Episode)
        {
            DataManager.Instance.Save();
        }

        CurrentState = newState;
        Debug.Log($"[GameManager] State changed: {CurrentState}");

        HandleSceneTransition(newState);
    }

    private void HandleSceneTransition(GameState state)
    {
        string sceneName = "";
        Action onTransitionComplete = null;

        switch (state)
        {
            case GameState.Episode:
                sceneName = "BusinessScene";
                onTransitionComplete = () =>
                {
                    string epId = EpisodeManager.Instance?.CurrentPlayingEpisodeID;
                    EpisodeData data = EpisodeManager.Instance?.GetEpisodeData(epId);
                    var runner = UnityEngine.Object.FindFirstObjectByType<EpisodeRunner>();
                    runner?.Begin(data);
                };
                break;
            case GameState.Business:
                sceneName = "BusinessScene";
                onTransitionComplete = () =>
                {
                    var bootstrap = UnityEngine.Object.FindFirstObjectByType<BusinessFlowBootstrap>();
                    if (bootstrap != null)
                        bootstrap.StartBusinessSequence();
                    else
                        Debug.LogError("[GameManager] BusinessFlowBootstrap was not found.");
                };
                break;
            case GameState.Rest:
                sceneName = "RestScene";
                break;
        }

        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneTransitionManager.Instance.TransitionToSubScene(sceneName, onTransitionComplete);
        }
    }
}
