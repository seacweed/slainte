using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Slainte.Bartending;
using UnityEngine;
using UnityEngine.UI;

public class LiquorShelfUI : MonoBehaviour
{
    public static LiquorShelfUI Active { get; private set; }

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
    [SerializeField] private LiquorBottleCatalog  catalog;
    [SerializeField] private LiquorBottleSlotUI   slotPrefab;

    [Header("Background")]
    [SerializeField] private Image             shelfBackgroundImage;
    [SerializeField] private AspectRatioFitter shelfAspectRatioFitter;

    [Header("Controls")]
    [SerializeField] private Button closeButton;

    [Header("Delivery")]
    [SerializeField] private bool enableDelivery = true;
    [SerializeField] private LiquorShopCatalog deliveryCatalog;
    [SerializeField, Min(0f)] private float deliveryPriceMultiplier = 2f;
    [SerializeField] private Button deliveryButton; // 술장 위에 별도로 배치된 배송 진입 버튼(카테고리 버튼 목록과 분리). 비활성 시 회색 표시는 Button의 Disabled Color(Color Tint)로 처리
    [SerializeField] private ShelfShutterUI shutter; // 배송 진입 시에만 재생되는 위/아래 셔터 연출

    [Header("Delivery Character")]
    [SerializeField] private RecipeBookUI recipeBook;
    [SerializeField, Range(0.1f, 0.9f)] private float deliveryCharacterWidthRatio = 0.46f;
    [SerializeField] private float deliveryCharacterVisibleX;
    [SerializeField, Min(0f)] private float deliveryCharacterTopMargin = 20f;
    [SerializeField, Min(0f)] private float deliveryCharacterBottomMargin;
    [SerializeField, Min(0f)] private float deliveryCharacterHiddenPadding = 80f;
    [SerializeField, Min(0.01f)] private float deliveryCharacterSlideDuration = 0.35f;
    [SerializeField, Min(0f)] private float deliveryCharacterHoldDuration = 1f;

    private bool                                  _isOpen;
    private bool                                  _interactable = true;
    private Coroutine                             _slideCo;
    private LiquorCategoryDef                     _lastCategory;
    private readonly List<LiquorCategoryButtonUI> _categoryButtons = new();
    private DeliveryShopPanelUI                   _deliveryPanel;
    private DeliveryCharacterPresenter            _deliveryCharacter;
    private bool                                  _deliveryAvailable = true;
    private string                                _deliveryUnavailableReason = string.Empty;
    private bool                                  _deliverySessionActive;
    private int                                   _deliveryTransitionVersion;
    private Coroutine                             _deliveryCharacterPresentation;
    private int                                   _bottleReturnFrame = -1;

    public bool IsDeliveryAvailable => _deliveryAvailable;
    public bool IsDeliveryOpen => _deliveryPanel != null && _deliveryPanel.IsVisible;
    public bool BlocksRecipeBook => _deliverySessionActive;
    public Button DeliveryTabButton => deliveryButton;
    public int DeliveryVisibleItemCount => _deliveryPanel != null
        ? _deliveryPanel.VisibleItemCount
        : 0;

    void Awake()
    {
        Active = this;
        if (recipeBook == null)
            recipeBook = FindFirstObjectByType<RecipeBookUI>(FindObjectsInactive.Include);

        if (shelfPanelRect) SetPanelX(closedX);

        if (closeButton) closeButton.onClick.AddListener(Close);

        HideAllContainers();
        BuildCategoryButtons();
        BuildCategorySlots();
        BuildDelivery();
    }

    public static bool TryReturnHeldBottle(Vector2 screenPosition)
    {
        LiquorShelfUI shelf = Active;
        if (shelf == null)
            return false;
        if (shelf._bottleReturnFrame == Time.frameCount)
            return true;
        if (!shelf.ContainsReturnPoint(screenPosition))
            return false;

        BusinessBartendingBootstrap bartending =
            FindFirstObjectByType<BusinessBartendingBootstrap>();
        if (bartending == null)
            return false;

        if (!bartending.TryReturnHeldBottleToShelf(out string failure))
        {
            if (!string.IsNullOrWhiteSpace(failure))
            {
                Debug.LogWarning("[LiquorShelf] " + failure);
                return true;
            }

            return false;
        }

        shelf._bottleReturnFrame = Time.frameCount;
        shelf.RefreshShelfSlots();
        return true;
    }

    private bool ContainsReturnPoint(Vector2 screenPosition)
    {
        if (!_isOpen
            || !_interactable
            || shelfPanelRect == null
            || !shelfPanelRect.gameObject.activeInHierarchy)
        {
            return false;
        }

        Canvas canvas = shelfPanelRect.GetComponentInParent<Canvas>();
        Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        return RectTransformUtility.RectangleContainsScreenPoint(
            shelfPanelRect,
            screenPosition,
            canvasCamera);
    }

    private void BuildCategoryButtons()
    {
        if (categoryButtonContent == null || categoryButtonPrefab == null) return;

        foreach (var entry in categoryEntries)
        {
            if (entry.def == null) continue;
            var btn = Instantiate(categoryButtonPrefab, categoryButtonContent);
            btn.Bind(entry.def, OpenCategory);
            _categoryButtons.Add(btn);
        }
    }

    private void BuildCategorySlots()
    {
        if (catalog == null || slotPrefab == null) return;

        foreach (var entry in categoryEntries)
        {
            if (entry.def == null || entry.container == null) continue;

            var bottles = catalog.bottles.Where(b => b != null && b.category == entry.def);
            foreach (var bottle in bottles)
            {
                var slot = Instantiate(slotPrefab, entry.container.transform);
                slot.Setup(bottle);
            }
        }
    }

    public void Toggle()
    {
        if (!_interactable) return;

        if (_isOpen)
        {
            if (IsDeliveryOpen)
                // 배송 내용/상태는 건드리지 않고 서랍만 슬라이드해서 같이 감춘다.
                Slide(closedX, () => _isOpen = false);
            else
                Close();
        }
        else if (IsDeliveryOpen)
        {
            // 감춰뒀던 배송 화면을 재생 없이 그대로 되돌린다.
            _isOpen = true;
            Slide(openX, null);
        }
        else
        {
            OpenCategory(_lastCategory ?? (categoryEntries.Length > 0 ? categoryEntries[0].def : null));
        }
    }

    public void OpenCategory(LiquorCategoryDef def)
    {
        if (!_interactable || def == null) return;

        EndDeliverySession();
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
        EndDeliverySession();
        Slide(closedX, () => _isOpen = false);
    }

    public void SetInteractable(bool on)
    {
        _interactable = on;
        foreach (var btn in _categoryButtons) btn.SetInteractable(on);
        ApplyDeliveryTabState();
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

    public bool TryOpenDelivery()
    {
        if (!_interactable || !_deliveryAvailable || _deliveryPanel == null)
        {
            if (!_deliveryAvailable && !string.IsNullOrWhiteSpace(_deliveryUnavailableReason))
                Debug.LogWarning("[Delivery] " + _deliveryUnavailableReason);
            return false;
        }

        recipeBook?.SetTemporarilyBlocked(true);
        _deliverySessionActive = true;
        _deliveryTransitionVersion++;
        if (closeButton != null) closeButton.gameObject.SetActive(false);
        if (!_isOpen)
        {
            _isOpen = true;
            Slide(openX, null);
        }

        if (shutter != null)
            shutter.PlayEnterDelivery(EnterDeliveryScreen);
        else
            EnterDeliveryScreen();

        return true;
    }

    private void EnterDeliveryScreen()
    {
        HideAllContainers();
        _deliveryPanel.Show();
    }

    public void SetDeliveryAvailable(bool available, string reason = "")
    {
        _deliveryAvailable = available;
        _deliveryUnavailableReason = available ? string.Empty : reason ?? string.Empty;
        ApplyDeliveryTabState();

        if (!available && IsDeliveryOpen)
        {
            EndDeliverySession();
            LiquorCategoryDef fallback = _lastCategory
                ?? (categoryEntries.Length > 0 ? categoryEntries[0].def : null);
            if (fallback != null) ShowCategory(fallback);
        }
    }

    private void BuildDelivery()
    {
        if (!enableDelivery || deliveryButton == null)
            return;

        deliveryCatalog ??= LiquorShopCatalog.LoadDefault();
        if (deliveryCatalog == null || deliveryCatalog.deliveryPanelPrefab == null)
        {
            Debug.LogError("[Delivery] 공용 상점 카탈로그 또는 배송 상점 프리팹을 찾을 수 없습니다.");
            return;
        }

        deliveryButton.onClick.RemoveAllListeners();
        deliveryButton.onClick.AddListener(() => TryOpenDelivery());

        RectTransform shelfRoot = closeButton != null
            ? closeButton.transform.parent as RectTransform
            : shelfPanelRect;
        _deliveryPanel = Instantiate(deliveryCatalog.deliveryPanelPrefab, shelfRoot);
        if (_deliveryPanel != null)
        {
            // 크기/앵커는 프리팹에 미리 잡아둔 RectTransform 값을 그대로 쓴다(강제로 부모를 꽉 채우지 않음).
            _deliveryPanel.transform.localScale = Vector3.one;
            // 셔터가 항상 배송 패널 위에 그려지도록, 셔터 바로 앞(형제 인덱스 기준) 자리에 끼워 넣는다.
            if (shutter != null)
                _deliveryPanel.transform.SetSiblingIndex(shutter.transform.GetSiblingIndex());
            _deliveryPanel.Initialize(deliveryCatalog, deliveryPriceMultiplier);
            _deliveryPanel.Purchased += HandleDeliveryPurchased;
            _deliveryPanel.CloseRequested += Close;
        }

        BuildDeliveryCharacter();

        ApplyDeliveryTabState();
    }

    private void ApplyDeliveryTabState()
    {
        if (deliveryButton == null)
            return;

        deliveryButton.interactable = _interactable && _deliveryAvailable;
    }

    private void RefreshShelfSlots()
    {
        foreach (var entry in categoryEntries)
        {
            if (entry.container == null) continue;
            foreach (var slot in entry.container.GetComponentsInChildren<LiquorBottleSlotUI>(true))
                slot.Refresh();
        }
    }

    private void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }

    private void HandleDeliveryPurchased(int price)
    {
        GameProgress.Instance?.RecordDeliveryPurchase(price);
        RefreshShelfSlots();
        if (_deliveryCharacter == null) return;

        if (_deliveryCharacterPresentation != null)
            StopCoroutine(_deliveryCharacterPresentation);

        _deliveryCharacter.Show();
        _deliveryCharacterPresentation = StartCoroutine(
            HideDeliveryCharacterAfterPurchase());
    }

    private IEnumerator HideDeliveryCharacterAfterPurchase()
    {
        float visibleTime = deliveryCharacterSlideDuration + deliveryCharacterHoldDuration;
        if (visibleTime > 0f)
            yield return new WaitForSecondsRealtime(visibleTime);

        _deliveryCharacterPresentation = null;
        _deliveryCharacter?.Hide();
    }

    private void BuildDeliveryCharacter()
    {
        if (deliveryCatalog == null || deliveryCatalog.deliveryPortrait == null) return;

        CharacterStage characterStage = FindFirstObjectByType<CharacterStage>(FindObjectsInactive.Include);
        RectTransform renderParent = characterStage != null
            ? characterStage.transform as RectTransform
            : null;
        Canvas canvas = renderParent != null ? renderParent.GetComponentInParent<Canvas>() : null;
        RectTransform canvasRoot = canvas != null ? canvas.transform as RectTransform : null;
        if (renderParent == null || canvasRoot == null)
        {
            Debug.LogError("[Delivery] 배송걸을 바테이블 뒤에 배치할 CharactersBackContainer를 찾지 못했습니다.");
            return;
        }

        _deliveryCharacter = DeliveryCharacterPresenter.Create(
            renderParent,
            canvasRoot,
            deliveryCatalog.deliveryPortrait,
            deliveryCharacterWidthRatio,
            deliveryCharacterTopMargin,
            deliveryCharacterBottomMargin,
            deliveryCharacterVisibleX,
            deliveryCharacterHiddenPadding,
            deliveryCharacterSlideDuration);
    }

    private void EndDeliverySession()
    {
        if (!_deliverySessionActive && !IsDeliveryOpen) return;

        if (_deliveryCharacterPresentation != null)
        {
            StopCoroutine(_deliveryCharacterPresentation);
            _deliveryCharacterPresentation = null;
        }

        _deliveryPanel?.SetInteractable(false);
        _deliveryPanel?.HideImmediate();
        shutter?.ResetImmediate();
        if (closeButton != null) closeButton.gameObject.SetActive(true);

        int transitionVersion = ++_deliveryTransitionVersion;
        if (_deliveryCharacter == null)
        {
            _deliverySessionActive = false;
            recipeBook?.SetTemporarilyBlocked(false);
            return;
        }

        _deliveryCharacter.Hide(() =>
        {
            if (transitionVersion == _deliveryTransitionVersion)
            {
                _deliverySessionActive = false;
                recipeBook?.SetTemporarilyBlocked(false);
            }
        });
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
