using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    // 바 테이블과 제작 공간 사이에 놓이는 술 선택 공간. 대분류별 재료 줄을 A/D로 슬라이드 전환하고,
    // 제조가 가능할 때만 제작 공간 뒤에서 올라와 조작을 받는다(사용자가 직접 여닫을 수 없음).
    // 재고 판단은 BusinessBartendingBootstrap의 병 재고 모델에 위임하고, 여기서는 표시와 입력 가드만 맡는다.
    // 들고 있는 병을 이 영역에 놓으면 술장 반환으로 처리되도록 반환 영역도 제공한다.
    public sealed class IngredientSelectionUI : MonoBehaviour, IBottleReturnZone
    {
        private sealed class CategoryRow
        {
            public LiquorCategoryDef Category;
            public RectTransform Rect;
            public readonly List<IngredientSlotUI> Slots = new List<IngredientSlotUI>();
        }

        [Header("Data")]
        [SerializeField] private LiquorBottleCatalog catalog;
        [Tooltip("Category order for A/D cycling. Empty = order of first appearance in the catalog.")]
        [SerializeField] private LiquorCategoryDef[] categoryOrder;

        [Header("Layout")]
        [SerializeField] private IngredientSlotUI slotPrefab;
        [Tooltip("Slides down behind the crafting space while crafting is unavailable.")]
        [SerializeField] private RectTransform contentRoot;
        [Tooltip("Clipping area (RectMask2D) that holds one row per category.")]
        [SerializeField] private RectTransform rowViewport;
        [SerializeField] private float slotSpacing = 40f;
        [SerializeField] private TextAnchor rowAlignment = TextAnchor.LowerCenter;

        [Header("Motion")]
        [SerializeField, Min(0.01f)] private float categorySlideDuration = 0.18f;
        [Tooltip("Downward distance that fully hides the ingredients behind the crafting space.")]
        [SerializeField, Min(0f)] private float hiddenOffsetY = 600f;
        [SerializeField, Min(0.01f)] private float visibilitySlideDuration = 0.3f;

        private readonly List<CategoryRow> rows = new List<CategoryRow>();
        private readonly Dictionary<string, List<IngredientSlotUI>> slotsByInventoryId =
            new Dictionary<string, List<IngredientSlotUI>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<LiquorBottleDef> unlockedSignature = new List<LiquorBottleDef>();
        private readonly List<LiquorBottleDef> unlockedBuffer = new List<LiquorBottleDef>();

        private BusinessBartendingBootstrap bartending;
        private Vector2 shownPosition;
        private int currentRowIndex = -1;
        private bool initialized;
        private bool craftingAvailable;
        private bool fullyShown;
        private Coroutine visibilityRoutine;
        private Coroutine categoryRoutine;
        private CategoryRow slidingOutRow;
        private CategoryRow slidingInRow;

        public bool IsInteractable => craftingAvailable && fullyShown;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            BottleReturnZones.Register(this);
        }

        private void OnDisable()
        {
            BottleReturnZones.Unregister(this);
        }

        private void OnDestroy()
        {
            if (bartending != null)
                bartending.StockChanged -= HandleStockChanged;
        }

        // GameModeManager가 Start에서 먼저 호출할 수 있으므로 Awake와 공개 메서드 양쪽에서 초기화를 보장한다.
        private void EnsureInitialized()
        {
            if (initialized)
                return;

            initialized = true;
            if (contentRoot == null)
                contentRoot = transform as RectTransform;
            shownPosition = contentRoot != null ? contentRoot.anchoredPosition : Vector2.zero;

            // 첫 모드가 적용되기 전에는 제조 불가로 간주해 제작 공간 뒤에 숨긴 채 시작한다.
            if (contentRoot != null)
                contentRoot.anchoredPosition = HiddenPosition;
            RebuildRowsIfUnlockChanged();
        }

        private Vector2 HiddenPosition => shownPosition + Vector2.down * hiddenOffsetY;

        public void SetCraftingAvailable(bool available)
        {
            EnsureInitialized();
            // 같은 상태가 반복 통지되면(모드 재적용 등) 진행 중인 슬라이드를 처음부터 다시 틀지 않는다.
            bool alreadyApplied = available
                ? craftingAvailable && (fullyShown || visibilityRoutine != null)
                : !craftingAvailable;
            if (alreadyApplied)
                return;

            craftingAvailable = available;
            if (available)
            {
                ResolveBartending();
                RebuildRowsIfUnlockChanged();
                RefreshAllAvailability();
                StartVisibilitySlide(shownPosition, () => fullyShown = true);
                return;
            }

            // 숨기기 시작하는 순간 바로 조작을 막는다 — 내려가는 도중 클릭으로 병이 나오면 안 된다.
            fullyShown = false;
            ClearHovers();
            StartVisibilitySlide(HiddenPosition, null);
        }

        public void ShowNext()
        {
            SwitchCategory(1);
        }

        public void ShowPrevious()
        {
            SwitchCategory(-1);
        }

        public bool CanHover(Vector2 screenPosition)
        {
            if (!IsInteractable)
                return false;

            BusinessBartendingBootstrap session = ResolveBartending();
            return session != null
                && session.IsSessionReady
                && !session.HasHeldBartendingItem()
                && !session.IsPointerOverWorldItem(screenPosition);
        }

        public void TrySpawn(IngredientSlotUI slot, Vector2 screenPosition)
        {
            if (slot == null || slot.Definition == null || !CanHover(screenPosition))
                return;

            if (!bartending.TryPlaceBottleFromShelf(slot.Definition, out string failure)
                && !string.IsNullOrWhiteSpace(failure))
            {
                Debug.LogWarning("[IngredientSelection] " + failure);
            }
        }

        public bool ContainsReturnPoint(Vector2 screenPosition)
        {
            if (!IsInteractable || rowViewport == null || !rowViewport.gameObject.activeInHierarchy)
                return false;

            Canvas canvas = rowViewport.GetComponentInParent<Canvas>();
            Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(
                rowViewport,
                screenPosition,
                canvasCamera);
        }

        public void OnBottleReturned()
        {
            // 반환에 따른 재고 변화는 StockChanged로 이미 반영되므로 추가 처리가 없다.
        }

        private BusinessBartendingBootstrap ResolveBartending()
        {
            // 부트스트랩은 씬 로드 후 런타임에 설치되므로 Awake 시점에는 없을 수 있어 필요할 때 찾는다.
            if (bartending != null)
                return bartending;

            bartending = FindFirstObjectByType<BusinessBartendingBootstrap>();
            if (bartending != null)
                bartending.StockChanged += HandleStockChanged;
            return bartending;
        }

        // 해금된 재료 구성이 바뀐 경우에만 줄을 다시 만든다 — 제조 모드에 들어갈 때마다 슬롯을
        // 파괴·재생성하면 GC와 레이아웃 재계산이 반복되기 때문.
        private void RebuildRowsIfUnlockChanged()
        {
            CollectUnlockedDefinitions(unlockedBuffer);
            if (rows.Count > 0 && SameDefinitions(unlockedBuffer, unlockedSignature))
                return;

            unlockedSignature.Clear();
            unlockedSignature.AddRange(unlockedBuffer);
            BuildRows();
        }

        private void CollectUnlockedDefinitions(List<LiquorBottleDef> result)
        {
            result.Clear();
            if (catalog == null || catalog.bottles == null)
                return;

            GameProgress progress = GameProgress.Instance;
            for (int i = 0; i < catalog.bottles.Count; i++)
            {
                LiquorBottleDef definition = catalog.bottles[i];
                if (definition == null || definition.category == null)
                    continue;

                bool unlocked = string.IsNullOrEmpty(definition.unlockFlagKey)
                    || (progress != null && progress.HasFlag(definition.unlockFlagKey));
                if (unlocked)
                    result.Add(definition);
            }
        }

        private static bool SameDefinitions(List<LiquorBottleDef> left, List<LiquorBottleDef> right)
        {
            if (left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        private void BuildRows()
        {
            FinishCategorySlide();
            LiquorCategoryDef previousCategory = currentRowIndex >= 0 && currentRowIndex < rows.Count
                ? rows[currentRowIndex].Category
                : null;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Rect != null)
                    Destroy(rows[i].Rect.gameObject);
            }
            rows.Clear();
            slotsByInventoryId.Clear();
            currentRowIndex = -1;

            if (rowViewport == null || slotPrefab == null)
                return;

            List<LiquorCategoryDef> categories = ResolveCategoryOrder();
            for (int i = 0; i < categories.Count; i++)
            {
                CategoryRow row = CreateRow(categories[i]);
                // 해금된 재료가 하나도 없는 대분류는 빈 화면으로 넘어가지 않도록 순환에서 뺀다.
                if (row.Slots.Count == 0)
                {
                    Destroy(row.Rect.gameObject);
                    continue;
                }

                row.Rect.gameObject.SetActive(false);
                rows.Add(row);
                if (row.Category == previousCategory)
                    currentRowIndex = rows.Count - 1;
            }

            if (rows.Count > 0 && currentRowIndex < 0)
                currentRowIndex = 0;
            if (currentRowIndex >= 0)
                rows[currentRowIndex].Rect.gameObject.SetActive(true);
        }

        private List<LiquorCategoryDef> ResolveCategoryOrder()
        {
            List<LiquorCategoryDef> order = new List<LiquorCategoryDef>();
            if (categoryOrder != null)
            {
                for (int i = 0; i < categoryOrder.Length; i++)
                {
                    if (categoryOrder[i] != null && !order.Contains(categoryOrder[i]))
                        order.Add(categoryOrder[i]);
                }
            }

            if (order.Count > 0)
                return order;

            for (int i = 0; i < unlockedSignature.Count; i++)
            {
                LiquorCategoryDef category = unlockedSignature[i].category;
                if (!order.Contains(category))
                    order.Add(category);
            }
            return order;
        }

        private CategoryRow CreateRow(LiquorCategoryDef category)
        {
            GameObject rowObject = new GameObject(
                "IngredientRow_" + (category != null ? category.name : "None"),
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            RectTransform rect = (RectTransform)rowObject.transform;
            rect.SetParent(rowViewport, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            HorizontalLayoutGroup layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = rowAlignment;
            layout.spacing = slotSpacing;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            CategoryRow row = new CategoryRow { Category = category, Rect = rect };
            for (int i = 0; i < unlockedSignature.Count; i++)
            {
                LiquorBottleDef definition = unlockedSignature[i];
                if (definition.category != category)
                    continue;

                IngredientSlotUI slot = Instantiate(slotPrefab, rect);
                slot.Setup(definition, this);
                row.Slots.Add(slot);

                string inventoryId = definition.InventoryId;
                if (!slotsByInventoryId.TryGetValue(inventoryId, out List<IngredientSlotUI> slots))
                {
                    slots = new List<IngredientSlotUI>();
                    slotsByInventoryId.Add(inventoryId, slots);
                }
                slots.Add(slot);
            }

            return row;
        }

        // 나가는 줄은 이동 방향 반대편으로 빠지고 들어오는 줄은 반대편에서 들어와 가운데 멈춘다.
        // direction +1(D)이면 기존 재료가 왼쪽으로 사라지고 새 재료가 오른쪽에서 나온다.
        private void SwitchCategory(int direction)
        {
            if (!IsInteractable || rows.Count < 2 || currentRowIndex < 0)
                return;

            // 슬라이드 도중 다시 입력하면 진행 중인 전환을 끝난 상태로 확정하고 다음 전환을 시작한다.
            FinishCategorySlide();
            ClearHovers();

            CategoryRow outgoing = rows[currentRowIndex];
            currentRowIndex = (currentRowIndex + direction + rows.Count) % rows.Count;
            CategoryRow incoming = rows[currentRowIndex];

            slidingOutRow = outgoing;
            slidingInRow = incoming;
            incoming.Rect.gameObject.SetActive(true);
            categoryRoutine = StartCoroutine(SlideRows(outgoing, incoming, direction));
        }

        private IEnumerator SlideRows(CategoryRow outgoing, CategoryRow incoming, int direction)
        {
            float width = rowViewport.rect.width;
            float outgoingTarget = -direction * width;
            float incomingStart = direction * width;
            SetRowX(outgoing, 0f);
            SetRowX(incoming, incomingStart);

            float elapsed = 0f;
            while (elapsed < categorySlideDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float eased = EaseOutCubic(elapsed / categorySlideDuration);
                SetRowX(outgoing, Mathf.LerpUnclamped(0f, outgoingTarget, eased));
                SetRowX(incoming, Mathf.LerpUnclamped(incomingStart, 0f, eased));
                yield return null;
            }

            categoryRoutine = null;
            FinishCategorySlide();
        }

        private void FinishCategorySlide()
        {
            if (categoryRoutine != null)
            {
                StopCoroutine(categoryRoutine);
                categoryRoutine = null;
            }

            if (slidingOutRow != null && slidingOutRow != slidingInRow)
            {
                slidingOutRow.Rect.gameObject.SetActive(false);
                SetRowX(slidingOutRow, 0f);
            }
            if (slidingInRow != null)
                SetRowX(slidingInRow, 0f);

            slidingOutRow = null;
            slidingInRow = null;
        }

        private static void SetRowX(CategoryRow row, float x)
        {
            if (row?.Rect == null)
                return;
            Vector2 position = row.Rect.anchoredPosition;
            position.x = x;
            row.Rect.anchoredPosition = position;
        }

        private void StartVisibilitySlide(Vector2 target, Action onComplete)
        {
            if (visibilityRoutine != null)
            {
                StopCoroutine(visibilityRoutine);
                visibilityRoutine = null;
            }

            if (contentRoot == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (!isActiveAndEnabled)
            {
                contentRoot.anchoredPosition = target;
                onComplete?.Invoke();
                return;
            }

            visibilityRoutine = StartCoroutine(SlideContent(target, onComplete));
        }

        private IEnumerator SlideContent(Vector2 target, Action onComplete)
        {
            Vector2 start = contentRoot.anchoredPosition;
            float elapsed = 0f;
            while (elapsed < visibilitySlideDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float eased = EaseOutCubic(elapsed / visibilitySlideDuration);
                contentRoot.anchoredPosition = Vector2.LerpUnclamped(start, target, eased);
                yield return null;
            }

            contentRoot.anchoredPosition = target;
            visibilityRoutine = null;
            onComplete?.Invoke();
        }

        private void HandleStockChanged(string inventoryId)
        {
            if (string.IsNullOrWhiteSpace(inventoryId)
                || !slotsByInventoryId.TryGetValue(inventoryId, out List<IngredientSlotUI> slots))
            {
                return;
            }

            for (int i = 0; i < slots.Count; i++)
                RefreshAvailability(slots[i]);
        }

        private void RefreshAllAvailability()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                List<IngredientSlotUI> slots = rows[i].Slots;
                for (int j = 0; j < slots.Count; j++)
                    RefreshAvailability(slots[j]);
            }
        }

        private void RefreshAvailability(IngredientSlotUI slot)
        {
            if (slot == null || slot.Definition == null)
                return;

            LiquorBottleDef definition = slot.Definition;
            float shelfAmount;
            if (bartending != null)
            {
                shelfAmount = bartending.GetShelfAmount(definition);
            }
            else
            {
                GameProgress progress = GameProgress.Instance;
                shelfAmount = progress != null
                    ? progress.EnsureBottleAmount(definition.InventoryId, definition.DefaultAmount)
                    : definition.DefaultAmount;
            }

            slot.SetAvailable(shelfAmount > 0.01f);
        }

        private void ClearHovers()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                List<IngredientSlotUI> slots = rows[i].Slots;
                for (int j = 0; j < slots.Count; j++)
                {
                    if (slots[j] != null)
                        slots[j].ForceUnhover();
                }
            }
        }

        private static float EaseOutCubic(float t)
        {
            float clamped = Mathf.Clamp01(t);
            return 1f - Mathf.Pow(1f - clamped, 3f);
        }
    }
}
