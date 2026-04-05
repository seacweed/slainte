using UnityEngine;
using Yarn.Unity; // Yarn Spinner 기능을 쓰기 위해 반드시 추가!

public class DialogueManager : MonoSingleton<DialogueManager>
{
    private DialogueRunner currentRunner;

    protected override void Awake()
    {
        base.Awake();
    }

    // 에피소드가 시작될 때 GameManager가 호출해 줄 함수
    public void StartEpisode(string nodeName)
    {
        // 1. 현재 켜져있는 씬(Scene_Episode)에서 DialogueRunner 컴포넌트를 찾습니다.
        currentRunner = FindFirstObjectByType<DialogueRunner>();

        if (currentRunner == null)
        {
            Debug.LogError("[DialogueManager] 씬에서 DialogueRunner를 찾을 수 없습니다!");
            return;
        }

        // 2. 대사가 이미 실행 중이라면 정지시키고
        if (currentRunner.IsDialogueRunning)
        {
            currentRunner.Stop();
        }

        // 3. 지정된 노드(예: "Start")부터 대사를 재생합니다.
        currentRunner.StartDialogue(nodeName);
        Debug.Log($"[DialogueManager] 대사 재생 시작: {nodeName}");
    }

    // 💡 Yarn 스크립트에서 <<EndEpisode>> 라고 쓰면 이 함수가 실행됩니다!
    [YarnCommand("EndEpisode")]
    public static void EndEpisode()
    {
        Debug.Log("[DialogueManager] 대본에서 에피소드 종료 명령 수신!");
        GameManager.Instance.ChangeState(GameState.Rest); // 휴식 상태로 전환 (이때 자동 저장도 발동됨)
    }
}