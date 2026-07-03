using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class RecipeBookUI : MonoBehaviour
{
    private enum BookState { Closed, Open }

    [Header("Sprites")]
    [SerializeField] private Sprite spriteOpen;
    [SerializeField] private Sprite spriteClosed;

    [Header("References")]
    [SerializeField] private Image         panelImage;
    [SerializeField] private RectTransform bookRect;
    [SerializeField] private Button        toggleButton;

    [Header("Anchored X Positions")]
    [SerializeField] private float closedX = -600f;
    [SerializeField] private float openX   =    0f;

    [Header("Animation")]
    [SerializeField] private float slideDuration = 0.3f;

    [Header("Search")]
    [SerializeField] private RecipeSearchUI recipeSearchUI;

    private BookState _state = BookState.Closed;
    private Coroutine _slideCo;
    private bool _interactable = true;

    void Awake()
    {
        if (!bookRect) bookRect = (RectTransform)transform;

        ApplySprite(spriteClosed);
        SetAnchoredX(closedX);
        SetButtonInteractable(true);
    }

    public void Toggle()
    {
        if (_slideCo != null) return;

        if      (_state == BookState.Open)   BeginClose();
        else if (_state == BookState.Closed) BeginOpen();
    }

    public void Open()
    {
        if (_state == BookState.Closed) BeginOpen();
    }

    // EpisodeMode: close if open and disable interaction
    // OrderMode/CraftingMode: re-enable interaction
    public void SetInteractable(bool on)
    {
        bool wasDisabled = !_interactable;
        _interactable = on;

        SetButtonInteractable(on);

        if (!on && _state == BookState.Open)
            BeginClose();

        // Only reset the search view when coming back from a disabled state (e.g. EpisodeMode),
        // not on a plain Tab-toggle open/close.
        if (on && wasDisabled)
            recipeSearchUI?.ResetToMain();
    }

    // Open: swap sprite immediately, then slide in
    private void BeginOpen()
    {
        _state = BookState.Open;
        ApplySprite(spriteOpen);
        Slide(openX, null);
    }

    // Close: slide out, then swap sprite
    private void BeginClose()
    {
        Slide(closedX, () =>
        {
            _state = BookState.Closed;
            ApplySprite(spriteClosed);
        });
    }

    private void ApplySprite(Sprite s)
    {
        if (panelImage != null && s != null) panelImage.sprite = s;
    }

    private void SetAnchoredX(float x)
    {
        if (!bookRect) return;
        Vector2 p = bookRect.anchoredPosition;
        p.x = x;
        bookRect.anchoredPosition = p;
    }

    private void SetButtonInteractable(bool on)
    {
        if (toggleButton) toggleButton.interactable = on;
    }

    private void Slide(float targetX, Action onComplete)
    {
        StopSlide();
        _slideCo = StartCoroutine(SlideRoutine(targetX, onComplete));
    }

    private void StopSlide()
    {
        if (_slideCo == null) return;
        StopCoroutine(_slideCo);
        _slideCo = null;
    }

    private IEnumerator SlideRoutine(float targetX, Action onComplete)
    {
        float startX = bookRect.anchoredPosition.x;
        float y      = bookRect.anchoredPosition.y;
        float t      = 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, slideDuration);
            float ease = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            bookRect.anchoredPosition = new Vector2(Mathf.LerpUnclamped(startX, targetX, ease), y);
            yield return null;
        }

        bookRect.anchoredPosition = new Vector2(targetX, y);
        _slideCo = null;
        onComplete?.Invoke();
    }
}
