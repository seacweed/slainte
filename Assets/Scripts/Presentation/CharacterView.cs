using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class CharacterView : MonoBehaviour
{
    [Header("Animation")]
    [SerializeField] private float fadeDuration = 0.35f;
    [SerializeField] private float riseDistance = 50f;
    [SerializeField] private float popHeight    = 35f;
    [SerializeField] private float popDuration  = 0.12f;

    private Image            _image;
    private CanvasGroup      _canvasGroup;
    private AspectRatioFitter _arf;
    private RectTransform    _visualRT;

    private Coroutine _animRoutine;

    void Awake()
    {
        Transform visualT = transform.Find("Visual");
        if (visualT == null)
        {
            Debug.LogError($"[CharacterView] Prefab '{name}' requires a child named 'Visual'.");
            return;
        }

        _image       = visualT.GetComponent<Image>();
        _canvasGroup = visualT.GetComponent<CanvasGroup>();
        _arf         = visualT.GetComponent<AspectRatioFitter>();
        _visualRT    = visualT.GetComponent<RectTransform>();
    }

    public void Setup(Sprite sprite)
    {
        if (_image != null)
            _image.sprite = sprite;

        if (_arf != null && sprite != null)
        {
            _arf.aspectMode  = AspectRatioFitter.AspectMode.WidthControlsHeight;
            _arf.aspectRatio = sprite.rect.width / sprite.rect.height;
        }

        if (_canvasGroup != null)
            _canvasGroup.alpha = 0f;
    }

    public void ApplySlotLayout(RectTransform slot)
    {
        var rootRT = GetComponent<RectTransform>();
        rootRT.anchorMin        = new Vector2(0f, 0f);
        rootRT.anchorMax        = new Vector2(1f, 0f);
        rootRT.pivot            = new Vector2(0.5f, 0f);
        rootRT.offsetMin        = new Vector2(0f, rootRT.offsetMin.y);
        rootRT.offsetMax        = new Vector2(0f, rootRT.offsetMax.y);
        rootRT.anchoredPosition = Vector2.zero;
        rootRT.localScale       = Vector3.one;

        if (_visualRT == null) return;
        _visualRT.anchorMin        = new Vector2(0f, 0f);
        _visualRT.anchorMax        = new Vector2(1f, 0f);
        _visualRT.pivot            = new Vector2(0.5f, 0f);
        _visualRT.offsetMin        = new Vector2(0f, _visualRT.offsetMin.y);
        _visualRT.offsetMax        = new Vector2(0f, _visualRT.offsetMax.y);
        _visualRT.anchoredPosition = Vector2.zero;
        _visualRT.localScale       = Vector3.one;
    }

    public void SwapSprite(Sprite sprite)
    {
        if (_image != null)
            _image.sprite = sprite;

        if (_arf != null && sprite != null)
            _arf.aspectRatio = sprite.rect.width / sprite.rect.height;
    }

    public void PlayAppearAnimation(Action onComplete = null)
    {
        if (_animRoutine != null) StopCoroutine(_animRoutine);
        _animRoutine = StartCoroutine(AppearRoutine(onComplete));
    }

    public void PlayDisappearAnimation(Action onComplete = null)
    {
        if (_animRoutine != null) StopCoroutine(_animRoutine);
        _animRoutine = StartCoroutine(DisappearRoutine(onComplete));
    }

    private IEnumerator AppearRoutine(Action onComplete)
    {
        if (_visualRT == null) { onComplete?.Invoke(); yield break; }

        Vector2 target = _visualRT.anchoredPosition;
        Vector2 start  = target - new Vector2(0f, riseDistance);

        _visualRT.anchoredPosition = start;
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeDuration);
            if (_canvasGroup != null) _canvasGroup.alpha = a;
            _visualRT.anchoredPosition = Vector2.Lerp(start, target, a);
            yield return null;
        }

        Vector2 up = target + new Vector2(0f, popHeight);

        t = 0f;
        while (t < popDuration)
        {
            t += Time.deltaTime;
            _visualRT.anchoredPosition = Vector2.Lerp(target, up, Mathf.Clamp01(t / popDuration));
            yield return null;
        }

        t = 0f;
        while (t < popDuration)
        {
            t += Time.deltaTime;
            _visualRT.anchoredPosition = Vector2.Lerp(up, target, Mathf.Clamp01(t / popDuration));
            yield return null;
        }

        _visualRT.anchoredPosition = target;
        if (_canvasGroup != null) _canvasGroup.alpha = 1f;

        _animRoutine = null;
        onComplete?.Invoke();
    }

    private IEnumerator DisappearRoutine(Action onComplete)
    {
        if (_visualRT == null) { onComplete?.Invoke(); yield break; }

        float startAlpha = _canvasGroup != null ? _canvasGroup.alpha : 1f;
        Vector2 startPos = _visualRT.anchoredPosition;
        Vector2 endPos   = startPos - new Vector2(0f, riseDistance);

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeDuration);
            if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, a);
            _visualRT.anchoredPosition = Vector2.Lerp(startPos, endPos, a);
            yield return null;
        }

        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        _animRoutine = null;
        onComplete?.Invoke();
    }
}
