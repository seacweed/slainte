using System;
using Slainte.Business;
using UnityEngine;

public enum GameState
{
    None,
    Episode,
    Business,
    Settlement,
    Rest
}

public class GameManager : MonoSingleton<GameManager>
{
    public GameState CurrentState { get; private set; }

    public void ChangeState(GameState newState)
    {
        if (CurrentState == newState) return;

        if (CurrentState == GameState.Episode || CurrentState == GameState.Business)
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
        Action onFadeOutComplete = null;

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
                    GameModeManager.Instance?.RequestModeChange(GameMode.OrderMode);
                };
                break;
            case GameState.Settlement:
                SettlementManager.Instance?.BeginSettlement();
                break;
            case GameState.Rest:
                sceneName = "RestScene";
                // Rest는 항상 정산 화면을 닫으면서 진입하므로, 화면이 완전히 검게 된 시점에
                // 정산 화면(셔터/모니터)을 원위치로 리셋한다.
                onFadeOutComplete = () => SettlementManager.Instance?.OnFadeOutComplete();
                break;
        }

        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneTransitionManager.Instance.TransitionToSubScene(sceneName, onTransitionComplete, onFadeOutComplete);
        }
    }
}
