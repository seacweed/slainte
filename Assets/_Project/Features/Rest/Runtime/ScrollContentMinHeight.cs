using UnityEngine;
using UnityEngine.UI;

// ScrollRect의 Content에 부착. Content Size Fitter를 대체한다.
// 자식(아이템 목록 + 하단 오버레이)의 실제 필요 높이가 뷰포트보다 작으면 뷰포트 높이로 고정해
// Vertical Layout Group의 Flexible 자식(Spacer)이 남는 공간을 채우게 하고,
// 필요 높이가 뷰포트보다 크면 그 값을 그대로 사용해 정상적으로 스크롤되게 한다.
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(LayoutGroup))]
public class ScrollContentMinHeight : MonoBehaviour
{
    [Tooltip("비워두면 부모 RectTransform(Viewport)을 그대로 사용")]
    public RectTransform viewportOverride;

    private RectTransform _content;
    private RectTransform _viewport;

    private void Awake()
    {
        _content = (RectTransform)transform;
        _viewport = viewportOverride != null ? viewportOverride : transform.parent as RectTransform;
    }

    // 패널이 열려 있는 동안에만 호출되므로(비활성 오브젝트는 LateUpdate 미실행) 매 프레임 비교해도 비용이 미미함
    private void LateUpdate()
    {
        if (_viewport == null) return;

        float preferredHeight = LayoutUtility.GetPreferredHeight(_content);
        float targetHeight = Mathf.Max(preferredHeight, _viewport.rect.height);

        if (!Mathf.Approximately(_content.sizeDelta.y, targetHeight))
        {
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, targetHeight);
        }
    }
}
