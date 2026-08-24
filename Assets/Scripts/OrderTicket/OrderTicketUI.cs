using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OrderTicketUI : MonoBehaviour
{
    private enum TicketState { Closed, Open }

    [Header("그림")]
    [SerializeField] private Sprite spriteOpen;
    [SerializeField] private Sprite spriteClosed;

    [Header("스크롤")]
    [SerializeField] private ScrollRect    scrollRect;
    [SerializeField] private RectTransform contentRect;

    [Header("주문자")]
    [SerializeField] private TMP_Text customerNameText;

    [Header("숨길 상세 항목 영역")]
    [SerializeField] private Transform itemsContainer;
    [SerializeField] private ItemRowUI itemRowPrefab;

    [Header("주문 대사")]
    [SerializeField] private TMP_Text memoText;

    [Header("참조")]
    [SerializeField] private RectTransform ticketRect;
    [SerializeField] private CanvasGroup   canvasGroup;
    [SerializeField] private Button        toggleButton;
    [SerializeField] private Image         panelImage;

    [Header("이동 연출")]
    [SerializeField] private float hideOffsetY   = 250f;
    [SerializeField] private float slideDuration = 0.25f;

    private Vector2 _visiblePos;
    private Vector2 HiddenPos => _visiblePos + new Vector2(0f, hideOffsetY);

    private TicketState _state = TicketState.Closed;
    private Coroutine    _slideCo;

    void Awake()
    {
        if (!ticketRect)  ticketRect  = (RectTransform)transform;
        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();

        _visiblePos = ticketRect.anchoredPosition;
        ClearContent();
        ApplyClosedState();
        SetButtonInteractable(true);
    }

    public void Show(OrderTicketData data)
    {
        Show(data, data != null ? data.memo : string.Empty);
    }

    public void Show(OrderTicketData data, string memo)
    {
        if (data == null)
        {
            ClearContent();
            return;
        }

        if (customerNameText) customerNameText.text = data.customerName;
        if (memoText)         memoText.text         = memo ?? "";

        HideStructuredOrderDetails();
        ResetScrollToTop();

        BeginOpen();
    }

    public void ClearContent()
    {
        if (customerNameText) customerNameText.text = string.Empty;
        if (memoText) memoText.text = string.Empty;
        HideStructuredOrderDetails();
        ResetScrollToTop();
    }

    public void Toggle()
    {
        if (_slideCo != null) return;

        if      (_state == TicketState.Open)   BeginClose();
        else if (_state == TicketState.Closed) BeginOpen();
    }

    public void Open()
    {
        if (_state == TicketState.Closed) BeginOpen();
    }

    public void HideAnimated() => BeginClose();

    // EpisodeMode: close if open and disable interaction
    // OrderMode/CraftingMode: re-enable interaction
    public void SetInteractable(bool on)
    {
        SetButtonInteractable(on);

        if (!on && _state == TicketState.Open)
            BeginClose();
    }

    // Open: fade/raycast in immediately, swap sprite, then slide in
    private void BeginOpen()
    {
        _state = TicketState.Open;
        if (canvasGroup) canvasGroup.alpha = 1f;
        SetCanvasInteractable(true);
        ApplySprite(spriteOpen);
        Slide(_visiblePos, null);
    }

    // Close: slide out, then fade/raycast off
    private void BeginClose()
    {
        Slide(HiddenPos, () =>
        {
            _state = TicketState.Closed;
            ApplyClosedState();
        });
    }

    private void ApplyClosedState()
    {
        ticketRect.anchoredPosition = HiddenPos;
        if (canvasGroup) canvasGroup.alpha = 0f;
        SetCanvasInteractable(false);
        ApplySprite(spriteClosed);
    }

    private void SetCanvasInteractable(bool on)
    {
        if (!canvasGroup) return;
        canvasGroup.interactable   = on;
        canvasGroup.blocksRaycasts = on;
    }

    private void ApplySprite(Sprite s)
    {
        if (panelImage != null && s != null) panelImage.sprite = s;
    }

    private void SetButtonInteractable(bool on)
    {
        if (toggleButton) toggleButton.interactable = on;
    }

    private void ClearItems()
    {
        if (!itemsContainer) return;
        for (int i = itemsContainer.childCount - 1; i >= 0; i--)
            Destroy(itemsContainer.GetChild(i).gameObject);
    }

    private void HideStructuredOrderDetails()
    {
        if (!itemsContainer)
            return;

        ClearItems();
        itemsContainer.gameObject.SetActive(false);

        Transform header = itemsContainer.parent != null
            ? itemsContainer.parent.Find("ItemsHeader")
            : null;
        if (header != null)
            header.gameObject.SetActive(false);
    }

    private void ResetScrollToTop()
    {
        if (!scrollRect)
            return;

        scrollRect.StopMovement();
        Canvas.ForceUpdateCanvases();
        if (contentRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        scrollRect.verticalNormalizedPosition = 1f;
    }

    private void Slide(Vector2 target, Action onComplete)
    {
        StopSlide();
        _slideCo = StartCoroutine(SlideRoutine(target, onComplete));
    }

    private void StopSlide()
    {
        if (_slideCo == null) return;
        StopCoroutine(_slideCo);
        _slideCo = null;
    }

    private IEnumerator SlideRoutine(Vector2 target, Action onComplete)
    {
        Vector2 start = ticketRect.anchoredPosition;
        float   t     = 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, slideDuration);
            float ease = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            ticketRect.anchoredPosition = Vector2.LerpUnclamped(start, target, ease);
            yield return null;
        }

        ticketRect.anchoredPosition = target;
        _slideCo = null;
        onComplete?.Invoke();
    }
}
