using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class ContentHeightToBackground : MonoBehaviour
{
    [Header("Assign")]
    public RectTransform content;      // ScrollView/Viewport/Content
    public RectTransform background;   // Content/Background (AspectRatioFitter 붙은 이미지)

    [Tooltip("필요하면 살짝 여유를 줄 수 있음 (예: 20)")]
    public float extraPadding = 0f;

    void OnEnable()
    {
        Apply();
        Canvas.willRenderCanvases += Apply;
    }

    void OnDisable()
    {
        Canvas.willRenderCanvases -= Apply;
    }

    void OnRectTransformDimensionsChange()
    {
        Apply();
    }

    void Apply()
    {
        if (!content || !background) return;

        // 레이아웃(AspectRatioFitter 포함) 먼저 한 번 반영
        LayoutRebuilder.ForceRebuildLayoutImmediate(background);

        float h = background.rect.height + extraPadding;
        if (h <= 0 || float.IsNaN(h) || float.IsInfinity(h)) return;

        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
    }
}
