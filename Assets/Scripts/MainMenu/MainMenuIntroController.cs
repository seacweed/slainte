using System.Collections;
using UnityEngine;

public class MainMenuIntroController : MonoBehaviour
{
    [Header("Team Logo")]
    [SerializeField] private CanvasGroup teamLogoGroup;
    [SerializeField] private float teamLogoFadeInDuration = 1f;
    [SerializeField] private float teamLogoHoldDuration = 1.2f;
    [SerializeField] private float teamLogoFadeOutDuration = 0.8f;

    [Header("Title Logo")]
    [SerializeField] private CanvasGroup titleGroup;
    [SerializeField] private float titleFadeInDuration = 0.8f;
    [SerializeField] private int titleBlinkCount = 2;
    [SerializeField] private float titleBlinkDuration = 0.4f;
    [SerializeField] private float titleBlinkMinAlpha = 0.2f;
    [SerializeField] private Vector2 titleTopPosition = new Vector2(0f, 600f);

    [Header("Background")]
    [SerializeField] private CanvasGroup backgroundGroup;
    [SerializeField] private CanvasGroup backgroundDimmerGroup;
    [SerializeField] private float backgroundFadeInDuration = 2f;
    [SerializeField] private float moveStartDelay = 1f;
    [SerializeField] private float moveUpDuration = 1.2f;
    [SerializeField] private Vector2 backgroundRaisedPosition = new Vector2(0f, 300f);

    [Header("Pub Window Lighting")]
    [SerializeField] private CanvasGroup pubLightingGroup;
    [SerializeField] private int pubLightingFlickerCount = 2;
    [SerializeField] private float pubLightingFlickerDuration = 0.2f;

    [Header("Menu")]
    [SerializeField] private CanvasGroup menuGroup;
    [SerializeField] private float menuFadeInDuration = 0.5f;

    private RectTransform titleRect;
    private RectTransform backgroundRect;
    private Vector2 titleCenterPosition;
    private Vector2 backgroundRestPosition;
    private Coroutine introCoroutine;

    private void Awake()
    {
        titleRect = (RectTransform)titleGroup.transform;
        backgroundRect = (RectTransform)backgroundGroup.transform;
        titleCenterPosition = titleRect.anchoredPosition;
        backgroundRestPosition = backgroundRect.anchoredPosition;

        SetGroupState(teamLogoGroup, 0f, false);
        SetGroupState(titleGroup, 0f, false);
        SetGroupState(backgroundGroup, 1f, false);
        SetGroupState(backgroundDimmerGroup, 1f, false);
        SetGroupState(pubLightingGroup, 0f, false);
        SetGroupState(menuGroup, 0f, false);
    }

    private void Start()
    {
        introCoroutine = StartCoroutine(PlayIntro());
    }

    private void Update()
    {
        if (introCoroutine != null && Input.anyKeyDown)
            SkipIntro();
    }

    private void SkipIntro()
    {
        StopAllCoroutines();
        introCoroutine = null;
        ApplyFinalState();
    }

    private IEnumerator PlayIntro()
    {
        yield return FadeCanvasGroup(teamLogoGroup, 0f, 1f, teamLogoFadeInDuration);
        yield return new WaitForSeconds(teamLogoHoldDuration);
        yield return FadeCanvasGroup(teamLogoGroup, 1f, 0f, teamLogoFadeOutDuration);

        yield return FadeCanvasGroup(titleGroup, 0f, 1f, titleFadeInDuration);
        yield return BlinkCanvasGroup(titleGroup, titleBlinkCount, titleBlinkDuration, titleBlinkMinAlpha);

        StartCoroutine(FadeCanvasGroup(backgroundDimmerGroup, 1f, 0f, backgroundFadeInDuration));
        yield return new WaitForSeconds(moveStartDelay);
        yield return MoveRectTransforms(
            titleRect, titleCenterPosition, titleTopPosition,
            backgroundRect, backgroundRestPosition, backgroundRaisedPosition,
            moveUpDuration);

        yield return FlickerCanvasGroupOn(pubLightingGroup, pubLightingFlickerCount, pubLightingFlickerDuration);

        yield return FadeCanvasGroup(menuGroup, 0f, 1f, menuFadeInDuration);
        SetGroupState(menuGroup, 1f, true);

        introCoroutine = null;
    }

    private void ApplyFinalState()
    {
        SetGroupState(teamLogoGroup, 0f, false);
        SetGroupState(titleGroup, 1f, false);
        SetGroupState(backgroundGroup, 1f, false);
        SetGroupState(backgroundDimmerGroup, 0f, false);
        SetGroupState(pubLightingGroup, 1f, false);
        SetGroupState(menuGroup, 1f, true);

        titleRect.anchoredPosition = titleTopPosition;
        backgroundRect.anchoredPosition = backgroundRaisedPosition;
    }

    private static IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            group.alpha = to;
            yield break;
        }

        float elapsed = 0f;
        group.alpha = from;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            group.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        group.alpha = to;
    }

    private static IEnumerator BlinkCanvasGroup(CanvasGroup group, int count, float blinkDuration, float minAlpha)
    {
        float halfDuration = blinkDuration * 0.5f;
        for (int i = 0; i < count; i++)
        {
            yield return FadeCanvasGroup(group, 1f, minAlpha, halfDuration);
            yield return FadeCanvasGroup(group, minAlpha, 1f, halfDuration);
        }
    }

    private static IEnumerator FlickerCanvasGroupOn(CanvasGroup group, int flickerCount, float flickerDuration)
    {
        float halfDuration = flickerDuration * 0.5f;
        for (int i = 0; i < flickerCount; i++)
        {
            yield return FadeCanvasGroup(group, 0f, 1f, halfDuration);
            yield return FadeCanvasGroup(group, 1f, 0f, halfDuration);
        }
        yield return FadeCanvasGroup(group, 0f, 1f, halfDuration);
    }

    private static IEnumerator MoveRectTransforms(
        RectTransform a, Vector2 aFrom, Vector2 aTo,
        RectTransform b, Vector2 bFrom, Vector2 bTo,
        float duration)
    {
        if (duration <= 0f)
        {
            a.anchoredPosition = aTo;
            b.anchoredPosition = bTo;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            a.anchoredPosition = Vector2.Lerp(aFrom, aTo, t);
            b.anchoredPosition = Vector2.Lerp(bFrom, bTo, t);
            yield return null;
        }
        a.anchoredPosition = aTo;
        b.anchoredPosition = bTo;
    }

    private static void SetGroupState(CanvasGroup group, float alpha, bool interactable)
    {
        group.alpha = alpha;
        group.interactable = interactable;
        group.blocksRaycasts = interactable;
    }
}
