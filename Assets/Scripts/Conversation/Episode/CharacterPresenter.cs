using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CharacterPresenter : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject characterPrefab;

    [Header("Database")]
    [SerializeField] private CharacterDatabase characterDB;

    [Header("Slots (Center, Left, Right, Left2, Right2...)")]
    [SerializeField] private List<RectTransform> slots = new();

    [Header("Animation")]
    [SerializeField] private float fadeDuration = 0.35f;
    [SerializeField] private float riseDistance = 50f;
    [SerializeField] private float popHeight = 35f;
    [SerializeField] private float popDuration = 0.12f;

    private readonly List<GameObject> _spawned = new();
    private int _pendingAppearCount;
    private Action _onAllShown;

    public void Clear()
    {
        foreach (var go in _spawned)
            if (go != null) Destroy(go);

        _spawned.Clear();
    }

    public void ShowCharacters(IReadOnlyList<string> keys, Action onComplete = null)
    {
        Clear();

        _onAllShown = onComplete;

        if (keys == null || keys.Count == 0)
        {
            _onAllShown?.Invoke();
            _onAllShown = null;
            return;
        }

        int count = Mathf.Min(keys.Count, slots.Count);
        _pendingAppearCount = 0;

        for (int i = 0; i < count; i++)
        {
            var data = characterDB.FindByKey(keys[i]);
            if (data == null)
            {
                Debug.LogWarning($"Character key not found in DB: {keys[i]}");
                continue;
            }

            RectTransform slot = slots[i];
            GameObject root = Instantiate(characterPrefab, slot);
            _spawned.Add(root);

            var rootRT = root.GetComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(0f, 0.5f);
            rootRT.anchorMax = new Vector2(1f, 0.5f);
            rootRT.pivot = new Vector2(0.5f, 0.5f);
            rootRT.offsetMin = new Vector2(0f, rootRT.offsetMin.y);
            rootRT.offsetMax = new Vector2(0f, rootRT.offsetMax.y);
            rootRT.anchoredPosition = Vector2.zero;
            rootRT.localScale = Vector3.one;

            Transform visualT = root.transform.Find("Visual");
            if (visualT == null)
            {
                Debug.LogError("Character prefab must have child named 'Visual'.");
                continue;
            }

            var img = visualT.GetComponent<Image>();
            var cg = visualT.GetComponent<CanvasGroup>();
            var arf = visualT.GetComponent<AspectRatioFitter>();
            var visualRT = visualT.GetComponent<RectTransform>();

            if (img != null) img.sprite = data.sprite;

            if (arf != null && data.sprite != null)
            {
                arf.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
                arf.aspectRatio = data.sprite.rect.width / data.sprite.rect.height;
            }

            visualRT.anchorMin = new Vector2(0f, 0f);
            visualRT.anchorMax = new Vector2(1f, 0f);
            visualRT.pivot = new Vector2(0.5f, 0f);
            visualRT.offsetMin = new Vector2(0f, visualRT.offsetMin.y);
            visualRT.offsetMax = new Vector2(0f, visualRT.offsetMax.y);
            visualRT.anchoredPosition = Vector2.zero;
            visualRT.localScale = Vector3.one;

            _pendingAppearCount++;
            StartCoroutine(AppearVisual(visualRT, cg));
        }

        if (_pendingAppearCount == 0)
        {
            _onAllShown?.Invoke();
            _onAllShown = null;
        }
    }

    private IEnumerator AppearVisual(RectTransform rt, CanvasGroup cg)
    {
        Vector2 target = rt.anchoredPosition;
        Vector2 start = target - new Vector2(0, riseDistance);

        rt.anchoredPosition = start;
        if (cg != null) cg.alpha = 0f;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeDuration);

            if (cg != null) cg.alpha = a;
            rt.anchoredPosition = Vector2.Lerp(start, target, a);

            yield return null;
        }

        Vector2 up = target + new Vector2(0, popHeight);

        t = 0f;
        while (t < popDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / popDuration);
            rt.anchoredPosition = Vector2.Lerp(target, up, a);
            yield return null;
        }

        t = 0f;
        while (t < popDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / popDuration);
            rt.anchoredPosition = Vector2.Lerp(up, target, a);
            yield return null;
        }

        rt.anchoredPosition = target;
        if (cg != null) cg.alpha = 1f;

        _pendingAppearCount--;
        if (_pendingAppearCount <= 0)
        {
            _onAllShown?.Invoke();
            _onAllShown = null;
        }
    }
}