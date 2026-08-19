using UnityEngine;

/// <summary>
/// 휴식 씬의 오브젝트 UI가 열릴 때 컬러 배경과 흑백 배경을 전환한다.
/// 개별 TV/작전판/상점 상태는 ObjectInteraction이 담당한다.
/// </summary>
public sealed class RestSceneVisualStateCoordinator : MonoBehaviour
{
    public GameObject colorBackground;
    public GameObject grayBackground;

    private bool? lastAnyUIOpen;

    private void OnEnable()
    {
        Refresh(force: true);
    }

    private void Update()
    {
        Refresh(force: false);
    }

    private void Refresh(bool force)
    {
        bool anyUIOpen = ObjectInteraction.AnyUIOpen;
        if (!force && lastAnyUIOpen == anyUIOpen)
            return;

        lastAnyUIOpen = anyUIOpen;
        if (colorBackground != null) colorBackground.SetActive(!anyUIOpen);
        if (grayBackground != null) grayBackground.SetActive(anyUIOpen);
    }
}
