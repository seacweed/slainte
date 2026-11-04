using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Slainte.Shared.Display;

public class TooltipPopup : MonoBehaviour
{
    public TextMeshProUGUI descText;
    private RectTransform rect;
    private Canvas parentCanvas;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
    }

    public void Show(string text, RectTransform targetRect)
    {
        descText.text = text;
        gameObject.SetActive(true);

        // 레이아웃 강제 갱신 (텍스트 길이에 따른 크기를 즉시 계산하기 위함)
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

        UpdatePosition(targetRect);
    }

    // 대상 오른쪽에 붙이고, 16:9 게임 영역 오른쪽 끝을 넘으면 왼쪽으로 뒤집는다.
    private void UpdatePosition(RectTransform targetRect)
    {
        GameAspect.PlaceBesideTarget(rect, targetRect);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}