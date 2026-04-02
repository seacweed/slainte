using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OrderTicketUI : MonoBehaviour
{
    [Header("Scroll")]
    [SerializeField] ScrollRect scrollRect;
    [SerializeField] RectTransform contentRect;

    [Header("Header")]
    [SerializeField] TMP_Text customerNameText;

    [Header("Items")]
    [SerializeField] Transform itemsContainer;
    [SerializeField] ItemRowUI itemRowPrefab;

    [Header("Memo")]
    [SerializeField] TMP_Text memoText;

    [Header("Slide")]
    [SerializeField] RectTransform ticketRect;
    [SerializeField] Vector2 upAnchoredPos = new Vector2(40f, -40f);
    [SerializeField] float foldedVisibleHeight = 36f;
    [SerializeField] float slideDuration = 0.25f;

    [Header("Behavior")]
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] bool startExpanded = true;

    Vector2 downAnchoredPos;
    bool isUp;
    Coroutine slideCo;

    void Awake()
    {
        if (!ticketRect) ticketRect = (RectTransform)transform;
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        HideImmediate();
    }

    public void Show(OrderTicketData data)
    {
        // 텍스트 세팅
        if (customerNameText) customerNameText.text = data.customerName;
        if (memoText) memoText.text = data.memo ?? "";

        // 아이템 라인 생성
        ClearItems();
        if (data.items != null)
        {
            foreach (var it in data.items)
            {
                var row = Instantiate(itemRowPrefab, itemsContainer);
                row.Set(it.name, it.qty, it.price);
            }
        }

        // 표시
        gameObject.SetActive(true);
        if (canvasGroup)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        // ✅ 레이아웃 갱신 + 스크롤 상단 리셋
        Canvas.ForceUpdateCanvases();
        if (contentRect) LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        if (scrollRect) scrollRect.verticalNormalizedPosition = 1f;

        // ✅ 접힘 위치 계산 (티켓 높이 고정이지만 안전하게)
        float h = ticketRect.rect.height;
        downAnchoredPos = upAnchoredPos + new Vector2(0f, h - foldedVisibleHeight);

        isUp = startExpanded;
        ticketRect.anchoredPosition = isUp ? upAnchoredPos : downAnchoredPos;
    }

    void ClearItems()
    {
        if (!itemsContainer) return;
        for (int i = itemsContainer.childCount - 1; i >= 0; i--)
            Destroy(itemsContainer.GetChild(i).gameObject);
    }

    public void HideImmediate()
    {
        if (canvasGroup)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
        gameObject.SetActive(false);
    }

    public void Toggle()
    {
        if (!gameObject.activeInHierarchy) return;

        isUp = !isUp;
        Vector2 target = isUp ? upAnchoredPos : downAnchoredPos;

        if (slideCo != null) StopCoroutine(slideCo);
        slideCo = StartCoroutine(SlideTo(target));
    }

    IEnumerator SlideTo(Vector2 target)
    {
        Vector2 start = ticketRect.anchoredPosition;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, slideDuration);
            float ease = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            ticketRect.anchoredPosition = Vector2.LerpUnclamped(start, target, ease);
            yield return null;
        }

        ticketRect.anchoredPosition = target;
        slideCo = null;
    }
}
