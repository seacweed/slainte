using UnityEngine;
using Yarn.Unity; // Yarn Spinner 기능을 쓰기 위해 반드시 추가!

public class DialogueManager : MonoSingleton<DialogueManager>
{
    private DialogueRunner currentRunner;

    protected override void Awake()
    {
        base.Awake();
    }

    private void Update()
    {
        // 에피소드 재생 상태일 때 스페이스바(또는 엔터)를 누르면 즉시 완료 처리
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Episode)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                ManualCompleteEpisode();
            }
        }
    }

    private void ManualCompleteEpisode()
    {
        string epId = EpisodeManager.Instance != null ? EpisodeManager.Instance.CurrentPlayingEpisodeID : "알 수 없음";
        Debug.Log($"({epId}) 에피소드 완료. restscene으로 넘어갑니다.");

        // 클리어 처리
        if (EpisodeManager.Instance != null && !string.IsNullOrEmpty(EpisodeManager.Instance.CurrentPlayingEpisodeID))
        {
            EpisodeManager.Instance.ClearEpisode(EpisodeManager.Instance.CurrentPlayingEpisodeID);
        }

        // 휴식 씬으로 전환
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ChangeState(GameState.Rest);
        }
    }

    // 에피소드가 시작될 때 GameManager가 호출해 줄 함수
    public void StartEpisode(string nodeName)
    {
        currentRunner = FindFirstObjectByType<DialogueRunner>();

        if (currentRunner == null)
        {
            // 💡 Yarn 연동 중단 임시 모드: 에러 없이 로그만 띄우고 단축키 입력 대기
            Debug.Log($"[DialogueManager] 얀스피너 임시 패스: 현재 ({nodeName}) 에피소드 재생 중... (스페이스바를 누르면 클리어 후 복귀합니다)");
            return;
        }

        // 2. 대사가 이미 실행 중이라면 정지시키고
        if (currentRunner.IsDialogueRunning)
        {
            currentRunner.Stop();
        }

        // 3. 지정된 노드(예: "Start")부터 대사를 재생합니다.
        currentRunner.StartDialogue(nodeName);
        Debug.Log($"[DialogueManager] 대사 재생 시작: {nodeName} (단축키로 강제 클리어 가능)");
    }

    // 💡 Yarn 스크립트에서 <<EndEpisode>> 라고 쓰면 이 함수가 실행됩니다!
    [YarnCommand("EndEpisode")]
    public static void EndEpisode()
    {
        Debug.Log("[DialogueManager] 대본에서 에피소드 종료 명령 수신!");
        
        // 현재 플레이 중인 에피소드를 클리어 처리
        if (EpisodeManager.Instance != null && !string.IsNullOrEmpty(EpisodeManager.Instance.CurrentPlayingEpisodeID))
        {
            EpisodeManager.Instance.ClearEpisode(EpisodeManager.Instance.CurrentPlayingEpisodeID);
        }

        GameManager.Instance.ChangeState(GameState.Rest); // 휴식 상태로 전환 (이때 자동 저장도 발동됨)
    }
}