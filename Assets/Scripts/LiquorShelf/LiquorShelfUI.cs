using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LiquorShelfUI : MonoBehaviour
{
    [Serializable]
    private struct CategoryEntry
    {
        public LiquorCategoryDef def;
        public GameObject        container;
        public Sprite            backgroundSprite;
    }

    [Header("Panel")]
    [SerializeField] private RectTransform shelfPanelRect;
    [SerializeField] private float         closedX       = 1400f;
    [SerializeField] private float         openX         =    0f;
    [SerializeField] private float         slideDuration =   0.3f;

    [Header("Category Buttons")]
    [SerializeField] private Transform              categoryButtonContent;
    [SerializeField] private LiquorCategoryButtonUI categoryButtonPrefab;

    [Header("Categories")]
    [SerializeField] private CategoryEntry[] categoryEntries;

    [Header("Background")]
    [SerializeField] private Image             shelfBackgroundImage;
    [SerializeField] private AspectRatioFitter shelfAspectRatioFitter;

    [Header("Controls")]
    [SerializeField] private Button closeButton;

    private bool                                  _isOpen;
    private bool                                  _interactable = true;
    private Coroutine                             _slideCo;
    private LiquorCategoryDef                     _lastCategory;
    private readonly List<LiquorCategoryButtonUI> _categoryButtons = new();

    void Awake()
    {
        if (shelfPanelRect) SetPanelX(closedX);

        if (closeButton) closeButton.onClick.AddListener(Close);

        HideAllContainers();
        BuildCategoryButtons();
    }

    private void BuildCategoryButtons()
    {
        if (categoryButtonContent == null || categoryButtonPrefab == null) return;

        foreach (var entry in categoryEntries)
        {
            if (entry.def == null) continue;
            var btn = Instantiate(categoryButtonPrefab, categoryButtonContent);
            btn.Bind(entry.def, this);
            _categoryButtons.Add(btn);
        }
    }

    public void Toggle()
    {
        if (!_interactable) return;

        if (_isOpen)
            Close();
        else
            OpenCategory(_lastCategory ?? (categoryEntries.Length > 0 ? categoryEntries[0].def : null));
    }

    public void OpenCategory(LiquorCategoryDef def)
    {
        if (!_interactable || def == null) return;

        _lastCategory = def;
        ShowCategory(def);

        if (!_isOpen)
        {
            _isOpen = true;
            Slide(openX, null);
        }
    }

    public void Close()
    {
        if (!_isOpen) return;
        Slide(closedX, () => _isOpen = false);
    }

    public void SetInteractable(bool on)
    {
        _interactable = on;
        foreach (var btn in _categoryButtons) btn.SetInteractable(on);
        if (closeButton) closeButton.interactable = on;

        if (!on && _isOpen) Close();
    }

    private void ShowCategory(LiquorCategoryDef def)
    {
        foreach (var entry in categoryEntries)
        {
            bool active = entry.def == def;
            if (entry.container == null) continue;

            entry.container.SetActive(active);

            if (!active) continue;

            if (shelfBackgroundImage != null && entry.backgroundSprite != null)
            {
                shelfBackgroundImage.sprite = entry.backgroundSprite;
                if (shelfAspectRatioFitter != null)
                {
                    var r = entry.backgroundSprite.rect;
                    shelfAspectRatioFitter.aspectRatio = r.width / r.height;
                }
            }

            foreach (var slot in entry.container.GetComponentsInChildren<LiquorBottleSlotUI>())
                slot.Refresh();
        }
    }

    private void HideAllContainers()
    {
        foreach (var entry in categoryEntries)
            if (entry.container != null) entry.container.SetActive(false);
    }

    private void SetPanelX(float x)
    {
        Vector2 pos = shelfPanelRect.anchoredPosition;
        pos.x = x;
        shelfPanelRect.anchoredPosition = pos;
    }

    private void Slide(float targetX, Action onComplete)
    {
        if (_slideCo != null)
        {
            StopCoroutine(_slideCo);
            _slideCo = null;
        }
        _slideCo = StartCoroutine(SlideRoutine(targetX, onComplete));
    }

    private IEnumerator SlideRoutine(float targetX, Action onComplete)
    {
        float startX = shelfPanelRect.anchoredPosition.x;
        float y      = shelfPanelRect.anchoredPosition.y;
        float t      = 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, slideDuration);
            float ease = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            shelfPanelRect.anchoredPosition = new Vector2(Mathf.LerpUnclamped(startX, targetX, ease), y);
            yield return null;
        }

        shelfPanelRect.anchoredPosition = new Vector2(targetX, y);
        _slideCo = null;
        onComplete?.Invoke();
    }
}
