using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ResetGameButton : MonoBehaviour
{
    private void Awake()
    {
        Button btn = GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(ResetGame);
        }
    }

    public void ResetGame()
    {
        Debug.Log("[ResetGameButton] Initializing save data...");

        // 1. 디스크 저장 파일 삭제 및 CurrentData 리셋
        if (DataManager.Instance != null)
        {
            DataManager.Instance.DeleteSaveFile();
            
            // 2. 세이브가 지워진 상태에서 로드를 재수행해 GameProgress 등 메모리 내부 진행 상황까지 완벽히 초기화
            DataManager.Instance.Load(); 
            Debug.Log("[ResetGameButton] DataManager and GameProgress successfully reset.");
        }
        else
        {
            Debug.LogWarning("[ResetGameButton] DataManager instance not found!");
        }

        // 3. 데이터 초기화를 반영하기 위해 휴식 씬을 화면 페이드 트랜지션 연출과 함께 안전하게 재로드
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.TransitionToSubScene("RestScene");
        }
        else
        {
            // Fallback: 트랜지션 매니저가 없을 시 일반 SceneManager를 활용해 다이렉트 재로드
            UnityEngine.SceneManagement.SceneManager.LoadScene("RestScene");
        }
    }
}
