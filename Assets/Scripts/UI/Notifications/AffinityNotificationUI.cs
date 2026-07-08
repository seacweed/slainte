using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class AffinityNotificationUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private AnimatedSpriteUI upAnim;
    [SerializeField] private AnimatedSpriteUI downAnim;

    [Header("Animation")]
    [SerializeField] private float slideDistance = 120f;
    [SerializeField] private float fadeInDuration = 0.25f;
    [SerializeField] private float holdDuration = 1.5f;
    [SerializeField] private float fadeOutDuration = 0.25f;

    private CanvasGroup _canvasGroup;
    private RectTransform _rect;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _rect = GetComponent<RectTransform>();
    }

    public void Setup(string displayName, int delta)
    {
        nameText.text = displayName;

        bool isUp = delta >= 0;
        upAnim.gameObject.SetActive(isUp);
        downAnim.gameObject.SetActive(!isUp);
        (isUp ? upAnim : downAnim).Play();
    }

    public void PlayAndDestroy(Action onComplete)
    {
        StartCoroutine(AnimRoutine(onComplete));
    }

    private IEnumerator AnimRoutine(Action onComplete)
    {
        Vector2 origin = _rect.anchoredPosition;
        Vector2 startPos = origin + new Vector2(slideDistance, 0f);

        _canvasGroup.alpha = 0f;
        _rect.anchoredPosition = startPos;

        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.deltaTime;
            float s = Smoothstep(Mathf.Clamp01(t / fadeInDuration));
            _canvasGroup.alpha = s;
            _rect.anchoredPosition = Vector2.Lerp(startPos, origin, s);
            yield return null;
        }

        _canvasGroup.alpha = 1f;
        _rect.anchoredPosition = origin;

        yield return new WaitForSeconds(holdDuration);

        t = 0f;
        while (t < fadeOutDuration)
        {
            t += Time.deltaTime;
            _canvasGroup.alpha = 1f - Mathf.Clamp01(t / fadeOutDuration);
            yield return null;
        }

        onComplete?.Invoke();
        Destroy(gameObject);
    }

    private static float Smoothstep(float x) => x * x * (3f - 2f * x);
}
