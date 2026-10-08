using System;
using Slainte.Business;
using Slainte.Shared.Lifecycle;
using UnityEngine;

public enum GameState
{
    None,
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

        if (CurrentState == GameState.Business)
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

        // 컷씬 직후 진입일 수 있으므로, 새 화면이 완전히 화면을 덮은 시점(onFadeOutComplete)에
        // 항상 컷씬 패널을 감춘다. 컷씬이 재생 중이 아니었다면 안전하게 무시된다.
        Action onFadeOutComplete = () => CutsceneManager.Instance?.HideImmediate();

        switch (state)
        {
            case GameState.Business:
                // 영업 모드 전환은 영업 흐름(BusinessShiftController.BeginShift)이 맡는다. 전환이 끝난 뒤
                // 여기서 OrderMode를 강제하면, 페이드 인 도중 이미 시작된 1번 슬롯 에피소드의 EpisodeMode를
                // 덮어써 클릭이 에피소드로 전달되지 않는다.
                sceneName = "BusinessScene";
                break;
            case GameState.Settlement:
                SettlementManager.Instance?.BeginSettlement();
                break;
            case GameState.Rest:
                sceneName = "RestScene";
                // Rest는 항상 정산 화면을 닫으면서 진입하므로, 화면이 완전히 검게 된 시점에
                // 정산 화면(셔터/모니터)을 원위치로 리셋하고, 컷씬 패널도 함께 감춘다.
                onFadeOutComplete = () =>
                {
                    CutsceneManager.Instance?.HideImmediate();
                    SettlementManager.Instance?.OnFadeOutComplete();
                };
                break;
        }

        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneTransitionManager.Instance.TransitionToSubScene(sceneName, onTransitionComplete, onFadeOutComplete);
        }
    }
}
