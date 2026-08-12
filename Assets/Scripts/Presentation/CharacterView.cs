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

    [Header("Blink")]
    [SerializeField] private float blinkDuration    = 1f;
    [SerializeField] private float blinkIntervalMin = 5f;
    [SerializeField] private float blinkIntervalMax = 15f;

    private Image             _image;
    private CanvasGroup       _canvasGroup;
    private AspectRatioFitter _arf;
    private RectTransform     _visualRT;

    private Image             _overlayImage;
    private CanvasGroup       _overlayCanvasGroup;
    private AspectRatioFitter _overlayArf;
    private RectTransform     _overlayRT;
    private Vector2           _overlayBasePos;
    private bool              _overlayAttached;

    private Coroutine _animRoutine;
    private Coroutine _blinkRoutine;

    private Sprite _currentSprite;
    private Sprite _currentOverlaySprite;
    private Sprite _blinkSprite;
    private Sprite _blinkOverlaySprite;

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

        Transform overlayT = transform.Find("VisualOverlay");
        if (overlayT != null)
        {
            _overlayImage       = overlayT.GetComponent<Image>();
            _overlayCanvasGroup = overlayT.GetComponent<CanvasGroup>();
            _overlayArf         = overlayT.GetComponent<AspectRatioFitter>();
            _overlayRT          = overlayT.GetComponent<RectTransform>();
        }
    }

    public void Setup(Sprite sprite, Sprite overlaySprite = null, Sprite blinkSprite = null, Sprite blinkOverlaySprite = null)
    {
        _currentSprite        = sprite;
        _currentOverlaySprite = overlaySprite;
        _blinkSprite          = blinkSprite;
        _blinkOverlaySprite   = blinkOverlaySprite;

        if (_image != null)
        {
            _image.sprite  = sprite;
            _image.enabled = sprite != null;
        }

        if (_arf != null && sprite != null)
        {
            _arf.aspectMode  = AspectRatioFitter.AspectMode.HeightControlsWidth;
            _arf.aspectRatio = sprite.rect.width / sprite.rect.height;
        }

        if (_canvasGroup != null)
            _canvasGroup.alpha = 0f;

        SetupOverlay(overlaySprite, sprite);
    }

    private void SetupOverlay(Sprite overlaySprite, Sprite baseSprite)
    {
        if (_overlayImage == null) return;

        _overlayImage.sprite  = overlaySprite;
        _overlayImage.enabled = overlaySprite != null;

        if (_overlayArf != null)
        {
            Sprite ratioRef = overlaySprite ?? baseSprite;
            if (ratioRef != null)
            {
                _overlayArf.aspectMode  = AspectRatioFitter.AspectMode.HeightControlsWidth;
                _overlayArf.aspectRatio = ratioRef.rect.width / ratioRef.rect.height;
            }
        }

        if (_overlayCanvasGroup != null)
            _overlayCanvasGroup.alpha = 0f;
    }

    public void AttachOverlayToFrontContainer(Transform frontContainer)
    {
        if (_overlayRT == null || frontContainer == null) return;
        _overlayRT.SetParent(frontContainer, worldPositionStays: true);
        _overlayBasePos  = _overlayRT.anchoredPosition;
        _overlayAttached = true;
    }

    public void ApplySlotLayout(RectTransform slot)
    {
        var rootRT = GetComponent<RectTransform>();
        ApplyRootLayout(rootRT);
        rootRT.anchoredPosition = Vector2.zero;
        rootRT.localScale       = Vector3.one;

        if (_visualRT != null)
            ApplyVisualLayout(_visualRT);

        if (_overlayRT != null)
            ApplyVisualLayout(_overlayRT);
    }

    public void SwapSprite(Sprite sprite, Sprite overlaySprite = null, Sprite blinkSprite = null, Sprite blinkOverlaySprite = null)
    {
        _currentSprite        = sprite;
        _currentOverlaySprite = overlaySprite;
        _blinkSprite          = blinkSprite;
        _blinkOverlaySprite   = blinkOverlaySprite;

        StopBlink();

        if (_image != null)
        {
            _image.sprite  = sprite;
            _image.enabled = sprite != null;
        }

        if (_arf != null && sprite != null)
            _arf.aspectRatio = sprite.rect.width / sprite.rect.height;

        if (_overlayImage != null)
        {
            _overlayImage.sprite  = overlaySprite;
            _overlayImage.enabled = overlaySprite != null;
        }

        StartBlink();
    }

    public void GetVisualWorldBoundsX(out float left, out float right)
    {
        left  = 0f;
        right = 0f;
        if (_visualRT == null) return;

        var corners = new Vector3[4];
        _visualRT.GetWorldCorners(corners);
        left  = corners[0].x;
        right = corners[0].x;
        for (int i = 1; i < 4; i++)
        {
            if (corners[i].x < left)  left  = corners[i].x;
            if (corners[i].x > right) right = corners[i].x;
        }
    }

    public bool TryGetVisualScreenRect(out Rect screenRect)
    {
        screenRect = default;
        if (_visualRT == null
            || !_visualRT.gameObject.activeInHierarchy
            || _image == null
            || !_image.enabled)
        {
            return false;
        }

        var corners = new Vector3[4];
        _visualRT.GetWorldCorners(corners);
        Canvas canvas = _visualRT.GetComponentInParent<Canvas>();
        Camera canvasCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;

        Vector2 first = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[0]);
        float xMin = first.x;
        float xMax = first.x;
        float yMin = first.y;
        float yMax = first.y;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[i]);
            xMin = Mathf.Min(xMin, point.x);
            xMax = Mathf.Max(xMax, point.x);
            yMin = Mathf.Min(yMin, point.y);
            yMax = Mathf.Max(yMax, point.y);
        }

        screenRect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return screenRect.width > Mathf.Epsilon && screenRect.height > Mathf.Epsilon;
    }

    private void ApplyRootLayout(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.sizeDelta = Vector2.zero;
    }

    private void ApplyVisualLayout(RectTransform rt)
    {
        rt.anchorMin        = new Vector2(0.5f, 0f);
        rt.anchorMax        = new Vector2(0.5f, 1f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.sizeDelta        = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.localScale       = Vector3.one;
    }

    public void PlayAppearAnimation(Action onComplete = null)
    {
        if (_animRoutine != null) StopCoroutine(_animRoutine);
        _animRoutine = StartCoroutine(AppearRoutine(onComplete));
    }

    public void PlayDisappearAnimation(Action onComplete = null)
    {
        StopBlink();
        if (_animRoutine != null) StopCoroutine(_animRoutine);
        _animRoutine = StartCoroutine(DisappearRoutine(onComplete));
    }

    private void StartBlink()
    {
        if (_blinkSprite == null) return;
        if (_blinkRoutine != null) StopCoroutine(_blinkRoutine);
        _blinkRoutine = StartCoroutine(BlinkRoutine());
    }

    private void StopBlink()
    {
        if (_blinkRoutine != null)
        {
            StopCoroutine(_blinkRoutine);
            _blinkRoutine = null;
        }
    }

    private IEnumerator BlinkRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(blinkIntervalMin, blinkIntervalMax));

            if (_image != null) _image.sprite = _blinkSprite;
            if (_overlayImage != null && _blinkOverlaySprite != null)
                _overlayImage.sprite = _blinkOverlaySprite;

            yield return new WaitForSeconds(blinkDuration);

            if (_image != null) _image.sprite = _currentSprite;
            if (_overlayImage != null && _blinkOverlaySprite != null)
                _overlayImage.sprite = _currentOverlaySprite;
        }
    }

    private IEnumerator AppearRoutine(Action onComplete)
    {
        if (_visualRT == null) { onComplete?.Invoke(); yield break; }

        Vector2 visualTarget = _visualRT.anchoredPosition;
        Vector2 visualStart  = visualTarget - new Vector2(0f, riseDistance);
        Vector2 overlayTarget = _overlayAttached ? _overlayBasePos : Vector2.zero;
        Vector2 overlayStart  = overlayTarget - new Vector2(0f, riseDistance);

        _visualRT.anchoredPosition = visualStart;
        if (_overlayAttached && _overlayRT != null) _overlayRT.anchoredPosition = overlayStart;
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        if (_overlayCanvasGroup != null) _overlayCanvasGroup.alpha = 0f;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeDuration);
            if (_canvasGroup != null) _canvasGroup.alpha = a;
            if (HasOverlay()) _overlayCanvasGroup.alpha = a;
            _visualRT.anchoredPosition = Vector2.Lerp(visualStart, visualTarget, a);
            if (_overlayAttached && _overlayRT != null)
                _overlayRT.anchoredPosition = Vector2.Lerp(overlayStart, overlayTarget, a);
            yield return null;
        }

        Vector2 visualUp  = visualTarget  + new Vector2(0f, popHeight);
        Vector2 overlayUp = overlayTarget + new Vector2(0f, popHeight);

        t = 0f;
        while (t < popDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / popDuration);
            _visualRT.anchoredPosition = Vector2.Lerp(visualTarget, visualUp, p);
            if (_overlayAttached && _overlayRT != null)
                _overlayRT.anchoredPosition = Vector2.Lerp(overlayTarget, overlayUp, p);
            yield return null;
        }

        t = 0f;
        while (t < popDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / popDuration);
            _visualRT.anchoredPosition = Vector2.Lerp(visualUp, visualTarget, p);
            if (_overlayAttached && _overlayRT != null)
                _overlayRT.anchoredPosition = Vector2.Lerp(overlayUp, overlayTarget, p);
            yield return null;
        }

        _visualRT.anchoredPosition = visualTarget;
        if (_overlayAttached && _overlayRT != null) _overlayRT.anchoredPosition = overlayTarget;
        if (_canvasGroup != null) _canvasGroup.alpha = 1f;
        if (HasOverlay()) _overlayCanvasGroup.alpha = 1f;

        _animRoutine = null;
        StartBlink();
        onComplete?.Invoke();
    }

    private IEnumerator DisappearRoutine(Action onComplete)
    {
        if (_visualRT == null) { onComplete?.Invoke(); yield break; }

        float startAlpha  = _canvasGroup != null ? _canvasGroup.alpha : 1f;
        Vector2 visualStart  = _visualRT.anchoredPosition;
        Vector2 visualEnd    = visualStart - new Vector2(0f, riseDistance);
        Vector2 overlayStart = _overlayAttached ? _overlayBasePos : Vector2.zero;
        Vector2 overlayEnd   = overlayStart - new Vector2(0f, riseDistance);

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeDuration);
            if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, a);
            if (HasOverlay()) _overlayCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, a);
            _visualRT.anchoredPosition = Vector2.Lerp(visualStart, visualEnd, a);
            if (_overlayAttached && _overlayRT != null)
                _overlayRT.anchoredPosition = Vector2.Lerp(overlayStart, overlayEnd, a);
            yield return null;
        }

        if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        if (_overlayCanvasGroup != null) _overlayCanvasGroup.alpha = 0f;
        _animRoutine = null;
        onComplete?.Invoke();
    }

    private bool HasOverlay() =>
        _overlayAttached && _overlayCanvasGroup != null && _overlayImage != null && _overlayImage.enabled;

    void OnDestroy()
    {
        if (_overlayAttached && _overlayRT != null)
            Destroy(_overlayRT.gameObject);
    }
}
