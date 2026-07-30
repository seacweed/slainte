using System; // 💡 1. System 추가 (Action을 사용하기 위해 필수!)
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionManager : MonoSingleton<SceneTransitionManager>
{
    [Header("UI Reference")]
    [Tooltip("Step 1에서 만든 Canvas Group을 여기에 드래그 앤 드롭")]
    public CanvasGroup fadeCanvasGroup; 
    public float fadeDuration = 0.5f;

    private string currentActiveScene = ""; // 현재 로드되어 있는 환경(서브) 씬

    protected override void Awake()
    {
        base.Awake();
        // 게임 시작 시 화면이 검은색에서 밝아지도록 초기화
        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 1f;
            fadeCanvasGroup.blocksRaycasts = true;
        }

        // 💡 [버그 픽스] 게임을 처음 켰을 때(예: RestScene에서 시작),
        // 비어있는 상태("")가 아니라 현재 활성화된 씬을 기억하도록 만듭니다.
        // 그래야 다음 씬으로 넘어갈 때 원래 있던 씬을 끄고 넘어갑니다.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            string sceneName = SceneManager.GetSceneAt(i).name;
            if (sceneName != "CoreScene")
            {
                currentActiveScene = sceneName;
                break;
            }
        }
    }

    private void Start()
    {
        StartCoroutine(InitialFadeIn());
    }

    private IEnumerator InitialFadeIn()
    {
        yield return Fade(0f); // 투명도를 0으로 서서히 변경
        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.blocksRaycasts = false; // 터치 방지 해제
        }
    }

    // 💡 2. 매개변수에 Action onComplete = null 추가
    // onFadeOutComplete: 화면이 완전히 검게 된 직후(씬 언로드 전) 호출. 이 시점 이후엔
    // 화면이 안 보이므로 이전 씬의 UI를 감추거나 상태를 리셋해도 티가 나지 않는다.
    public void TransitionToSubScene(string sceneToLoad, Action onComplete = null, Action onFadeOutComplete = null)
    {
        StartCoroutine(TransitionRoutine(sceneToLoad, onComplete, onFadeOutComplete));
    }

    // 💡 3. 매개변수에 Action onComplete 추가
    private IEnumerator TransitionRoutine(string sceneToLoad, Action onComplete, Action onFadeOutComplete)
    {
        Debug.Log($"[Transition] 1. 페이드 아웃 시작 (목표 씬: {sceneToLoad})");
        fadeCanvasGroup.blocksRaycasts = true;
        yield return Fade(1f);
        onFadeOutComplete?.Invoke();

        Debug.Log("[Transition] 2. 기존 씬 언로드 확인");
        if (!string.IsNullOrEmpty(currentActiveScene))
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(currentActiveScene);
            while (!unloadOp.isDone) yield return null;
        }

        Debug.Log($"[Transition] 3. 새로운 씬 로드 시작 ({sceneToLoad})");
        AsyncOperation loadOp = SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Additive);
        
        // 🚨 방어 코드: 씬 로드 명령이 실패해서 loadOp가 비어있을 경우 뻗지 않고 경고창을 띄움
        if (loadOp == null)
        {
            Debug.LogError($"[Transition] 🚨 씬 로드 실패! '{sceneToLoad}' 씬이 Build Settings에 없거나, 파일 이름의 오타가 있습니다.");
            yield break; // 여기서 코드를 안전하게 중단합니다.
        }

        while (!loadOp.isDone) yield return null; 

        Debug.Log("[Transition] 4. 활성 씬 변경 (조명/환경 세팅 기준 변경)");
        SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneToLoad));
        currentActiveScene = sceneToLoad;

        Debug.Log("[Transition] 5. 페이드 인 시작");
        yield return Fade(0f);
        fadeCanvasGroup.blocksRaycasts = false;

        Debug.Log("[Transition] 6. 전환 완료! 대사 시작 트리거 호출");
        onComplete?.Invoke();
    }

    // CanvasGroup의 투명도를 목표 수치까지 부드럽게 변경하는 코루틴
    private IEnumerator Fade(float targetAlpha)
    {
        if (fadeCanvasGroup == null) yield break;

        float startAlpha = fadeCanvasGroup.alpha;
        float time = 0f;

        while (time < fadeDuration)
        {
            time += Time.deltaTime;
            fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, time / fadeDuration);
            yield return null;
        }
        fadeCanvasGroup.alpha = targetAlpha;
    }
}