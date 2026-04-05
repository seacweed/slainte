using System; // Action을 쓰기 위해 추가
using UnityEngine;

public enum GameState
{
    None,
    Episode,
    Rest,
    Business // 영업 파트는 추후 구현
}

public class GameManager : MonoSingleton<GameManager>
{
    public GameState CurrentState { get; private set; }

    private void Update()
    {
        // 🚨 임시 테스트용 (키보드 E를 누르면 에피소드 상태로 전환)
        if (Input.GetKeyDown(KeyCode.E))
        {
            ChangeState(GameState.Episode);
        }
    }

    public void ChangeState(GameState newState)
    {
        if (CurrentState == newState) return;

        // 이전 상태가 Episode였고, 이제 다른 상태로 넘어간다면 자동 저장
        if (CurrentState == GameState.Episode)
        {
            DataManager.Instance.Save();
        }

        CurrentState = newState;
        Debug.Log($"[GameManager] 게임 상태 변경: {CurrentState}");
        
        // 상태에 따른 씬 전환 로직 실행
        HandleSceneTransition(newState);
    }

    private void HandleSceneTransition(GameState state)
    {
        string sceneName = "";
        System.Action onTransitionComplete = null; // 💡 1. 여기서 먼저 주머니를 만들어줍니다!
        
        // 상태에 따라 불러올 씬의 정확한 이름 매핑
        switch (state)
        {
            case GameState.Episode:
                sceneName = "Sample_Episode";
                // 💡 에피소드 씬 로딩이 끝나면 DialogueManager의 StartEpisode 실행하도록 주머니에 담음
                onTransitionComplete = () => DialogueManager.Instance.StartEpisode("Start");
                break;
            case GameState.Rest:
                sceneName = "Sample_Rest";
                break;
            // case GameState.Business:
            //     sceneName = "Scene_Business";
            //     break;
        }

        if (!string.IsNullOrEmpty(sceneName))
        {
            // 💡 2. TransitionManager를 통해 씬 교체 지시할 때 주머니도 같이 던져줍니다!
            SceneTransitionManager.Instance.TransitionToSubScene(sceneName, onTransitionComplete);
        }
    }
}