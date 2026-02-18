using UnityEngine;
using TMPro;
using UnityEngine.UI;

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

    private void UpdatePosition(RectTransform targetRect)
    {
        // 1. 타겟 이미지의 세계 좌표를 캔버스 로컬 좌표로 변환
        Vector3[] corners = new Vector3[4];
        targetRect.GetWorldCorners(corners);
        // corners[2]는 우측 상단, [3]은 우측 하단 -> 우측 중앙 위치 계산
        Vector3 targetRightCenter = (corners[2] + corners[3]) * 0.5f;

        // 2. 팝업이 오른쪽으로 떴을 때 화면 밖으로 나가는지 체크
        float popupWidth = rect.rect.width;
        float screenWidth = Screen.width;

        // 오른쪽 끝 좌표가 화면 너비를 넘어가면 왼쪽으로 배치
        if (targetRightCenter.x + popupWidth > screenWidth)
        {
            // 왼쪽 배치 로직
            rect.pivot = new Vector2(1, 0.5f); // 피봇을 우측 중앙으로 변경
            Vector3 targetLeftCenter = (corners[0] + corners[1]) * 0.5f;
            transform.position = targetLeftCenter - new Vector3(10, 0, 0); // 약간의 여백
        }
        else
        {
            // 오른쪽 배치 로직
            rect.pivot = new Vector2(0, 0.5f); // 피봇을 좌측 중앙으로 변경
            transform.position = targetRightCenter + new Vector3(10, 0, 0); // 약간의 여백
        }
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}