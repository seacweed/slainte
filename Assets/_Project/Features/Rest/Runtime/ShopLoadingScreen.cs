using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 상점 로고 + 로딩바 연출. ShopUIManager가 씬 진입 후 첫 오픈에만 Play()를 호출한다.
public class ShopLoadingScreen : MonoBehaviour
{
    [Header("Root")]
    public GameObject root;

    [Header("Loading Bar")]
    // Image Type = Filled, Fill Method = Horizontal, Fill Origin = Left 로 세팅
    public Image fillImage;
    public float fillDuration = 1.5f;

    private void Awake()
    {
        if (root) root.SetActive(false);
    }

    public IEnumerator Play()
    {
        if (root) root.SetActive(true);
        if (fillImage) fillImage.fillAmount = 0f;

        float timer = 0f;
        while (timer < 1f)
        {
            timer += Time.unscaledDeltaTime / fillDuration;
            if (fillImage) fillImage.fillAmount = Mathf.Clamp01(timer);
            yield return null;
        }

        if (fillImage) fillImage.fillAmount = 1f;
        if (root) root.SetActive(false);
    }

    // Play()가 끝까지 돌지 못하고 코루틴이 강제로 중단된 경우(예: 재생 도중 상점이 닫힘) 대비용.
    // 그대로 두면 root가 활성 상태로 남아 다음에 열릴 때 홈 화면과 겹쳐 보임.
    public void ResetVisual()
    {
        if (root) root.SetActive(false);
        if (fillImage) fillImage.fillAmount = 0f;
    }
}
