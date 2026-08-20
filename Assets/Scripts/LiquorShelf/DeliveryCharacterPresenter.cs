using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public sealed class DeliveryCharacterPresenter : MonoBehaviour
{
    private RectTransform rectTransform;
    private RectTransform referenceRect;
    private RectTransform renderParent;
    private Coroutine slideRoutine;
    private float widthRatio;
    private float topMargin;
    private float bottomMargin;
    private float slideDuration;
    private float hiddenPadding;
    private float visibleOffsetX;
    private float visibleX;
    private float hiddenX;
    private readonly Vector3[] referenceCorners = new Vector3[4];

    public bool IsTransitioning => slideRoutine != null;
    public bool IsShown { get; private set; }
    public float CurrentAnchoredX => rectTransform != null
        ? rectTransform.anchoredPosition.x
        : 0f;
    public float VisibleAnchoredX => visibleX;
    public float HiddenAnchoredX => hiddenX;
    public float TargetAnchoredX { get; private set; }

    public static DeliveryCharacterPresenter Create(
        RectTransform renderParent,
        RectTransform referenceRect,
        Sprite sprite,
        float widthRatio,
        float topMargin,
        float bottomMargin,
        float visibleX,
        float hiddenPadding,
        float slideDuration)
    {
        if (renderParent == null || referenceRect == null || sprite == null) return null;

        GameObject characterObject = new GameObject(
            "DeliveryCharacterPresenter",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        characterObject.layer = renderParent.gameObject.layer;
        characterObject.transform.SetParent(renderParent, false);

        RectTransform rect = characterObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);

        Image image = characterObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;

        characterObject.transform.SetAsLastSibling();

        DeliveryCharacterPresenter presenter = characterObject.AddComponent<DeliveryCharacterPresenter>();
        presenter.rectTransform = rect;
        presenter.referenceRect = referenceRect;
        presenter.renderParent = renderParent;
        presenter.widthRatio = Mathf.Clamp(widthRatio, 0.1f, 0.9f);
        presenter.topMargin = Mathf.Max(0f, topMargin);
        presenter.bottomMargin = Mathf.Max(0f, bottomMargin);
        presenter.visibleOffsetX = visibleX;
        presenter.hiddenPadding = Mathf.Max(0f, hiddenPadding);
        presenter.slideDuration = Mathf.Max(0.01f, slideDuration);
        presenter.RecalculateLayout();
        presenter.SetHiddenPosition();
        characterObject.SetActive(false);
        return presenter;
    }

    public void Show()
    {
        gameObject.SetActive(true);
        Canvas.ForceUpdateCanvases();
        RecalculateLayout();
        if (!IsShown && slideRoutine == null) SetHiddenPosition();
        IsShown = true;
        SlideTo(visibleX, null);
    }

    public void Hide(Action onComplete = null)
    {
        IsShown = false;
        if (!gameObject.activeSelf)
        {
            onComplete?.Invoke();
            return;
        }

        Canvas.ForceUpdateCanvases();
        RecalculateLayout();
        SlideTo(GetHiddenX(), () =>
        {
            gameObject.SetActive(false);
            onComplete?.Invoke();
        });
    }

    public void HideImmediate()
    {
        if (slideRoutine != null) StopCoroutine(slideRoutine);
        slideRoutine = null;
        IsShown = false;
        RecalculateLayout();
        SetHiddenPosition();
        gameObject.SetActive(false);
    }

    private void SetHiddenPosition()
    {
        if (rectTransform == null) return;
        Vector2 position = rectTransform.anchoredPosition;
        position.x = GetHiddenX();
        rectTransform.anchoredPosition = position;
    }

    private float GetHiddenX()
    {
        return hiddenX;
    }

    private void RecalculateLayout()
    {
        if (rectTransform == null || referenceRect == null || renderParent == null) return;

        referenceRect.GetWorldCorners(referenceCorners);
        Vector3 bottomLeft = renderParent.InverseTransformPoint(referenceCorners[0]);
        Vector3 topRight = renderParent.InverseTransformPoint(referenceCorners[2]);
        float anchorReferenceX = Mathf.Lerp(
            renderParent.rect.xMin,
            renderParent.rect.xMax,
            rectTransform.anchorMin.x);
        float left = Mathf.Min(bottomLeft.x, topRight.x) - anchorReferenceX;
        float right = Mathf.Max(bottomLeft.x, topRight.x) - anchorReferenceX;
        float referenceWidth = Mathf.Max(1f, right - left);
        float referenceHeight = Mathf.Max(1f, referenceRect.rect.height);
        float characterWidth = referenceWidth * widthRatio;
        rectTransform.sizeDelta = new Vector2(
            characterWidth,
            Mathf.Max(1f, referenceHeight - topMargin - bottomMargin));

        visibleX = left + visibleOffsetX;
        hiddenX = left - characterWidth - hiddenPadding;
        Vector2 position = rectTransform.anchoredPosition;
        position.y = (bottomMargin - topMargin) * 0.5f;
        rectTransform.anchoredPosition = position;
    }

    private void SlideTo(float targetX, Action onComplete)
    {
        if (slideRoutine != null) StopCoroutine(slideRoutine);
        TargetAnchoredX = targetX;
        slideRoutine = StartCoroutine(Slide(targetX, onComplete));
    }

    private IEnumerator Slide(float targetX, Action onComplete)
    {
        float startX = rectTransform.anchoredPosition.x;
        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / slideDuration);
            float eased = 1f - Mathf.Pow(1f - normalized, 3f);
            Vector2 position = rectTransform.anchoredPosition;
            position.x = Mathf.LerpUnclamped(startX, targetX, eased);
            rectTransform.anchoredPosition = position;
            yield return null;
        }

        Vector2 finalPosition = rectTransform.anchoredPosition;
        finalPosition.x = targetX;
        rectTransform.anchoredPosition = finalPosition;
        slideRoutine = null;
        onComplete?.Invoke();
    }
}
