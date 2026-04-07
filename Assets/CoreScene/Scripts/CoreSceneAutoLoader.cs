using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 에디터에서 플레이 버튼을 누를 때, 현재 열려있는 씬과 상관없이
/// 항상 CoreScene을 백그라운드(Additive)로 자동 로드해주는 유틸리티입니다.
/// </summary>
public static class CoreSceneAutoLoader
{
    private const string CoreSceneName = "CoreScene";

    // 씬이 로드되기 직전(Awake 이전)에 최우선으로 실행되게 만드는 유니티 마법의 속성입니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void LoadCoreSceneAutomatically()
    {
        // 1. 이미 CoreScene이 열려있는지 (테스트 중이거나 빌드 환경인지) 확인
        int loadedSceneCount = SceneManager.sceneCount;
        for (int i = 0; i < loadedSceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).name == CoreSceneName)
            {
                return; // 이미 있으면 구울 필요 없음
            }
        }

        // 2. Build Settings에 씬이 등록되어 있는지 방어 코드
        if (Application.CanStreamedLevelBeLoaded(CoreSceneName))
        {
            // Additive(추가) 방식으로 기존 씬을 끄지 않고 CoreScene만 얹습니다.
            SceneManager.LoadScene(CoreSceneName, LoadSceneMode.Additive);
            Debug.Log($"<color=green>[System]</color> 현재 씬 테스트를 위해 {CoreSceneName}을 몰래 자동 로드했습니다!");
        }
        else
        {
            Debug.LogError($"<color=red>[System]</color> {CoreSceneName} 씬 자동 로드 실패! 상단 메뉴의 File -> Build Settings 창에 CoreScene이 등록되어 있는지 확인해 주세요.");
        }
    }
}
