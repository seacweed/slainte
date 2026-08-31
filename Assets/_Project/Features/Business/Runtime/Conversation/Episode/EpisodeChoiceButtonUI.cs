using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EpisodeChoiceButtonUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;
    [SerializeField] private CanvasGroup canvasGroup;

    public void Setup(string text, Action onClick)
    {
        if (label != null)
            label.text = text;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick?.Invoke());
    }

    public void HideImmediate()
    {
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        button.interactable = false;
    }

    public void FadeOutAndDestroy(float duration, Action onComplete = null)
    {
        button.interactable = false;
        StartCoroutine(FadeRoutine(duration, onComplete));
    }

    private IEnumerator FadeRoutine(float duration, Action onComplete)
    {
        if (canvasGroup == null)
        {
            onComplete?.Invoke();
            Destroy(gameObject);
            yield break;
        }

        float elapsed = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / duration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
        onComplete?.Invoke();
        Destroy(gameObject);
    }
}
