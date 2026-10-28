using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    // 바 테이블과 도구 공간 사이에 놓이는 술 선택 공간. 대분류별 재료 줄을 A/D로 슬라이드 전환한다.
    // 항상 보이지만 제조 중에만 조작을 받고, 제조를 빠져나가도 현재 대분류를 유지해 다음 제조를
    // 같은 줄에서 시작한다. 재고 판단은 BusinessBartendingBootstrap의 재고 모델에, 배송 규칙은
    // BottleDeliveryService에 위임하고, 여기서는 표시와 입력 가드만 맡는다. 재고가 없는 재료를 클릭하면
    // 배송을 주문한다. 들고 있는 병·가니쉬를 이 영역에 놓으면 반환으로 처리된다.
    public sealed class IngredientSelectionUI : MonoBehaviour,
        IBottleReturnZone,
        ISelectionSlotOwner,
        IIngredientSlotRefresher
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
        [Tooltip("Clipping area (RectMask2D) that holds one row per category.")]
        [SerializeField] private RectTransform rowViewport;
        [SerializeField] private float slotSpacing = 40f;
        [SerializeField] private TextAnchor rowAlignment = TextAnchor.LowerCenter;

        [Header("Motion")]
        [SerializeField, Min(0.01f)] private float categorySlideDuration = 0.18f;

        [Header("Delivery")]
        [Tooltip("Rises from behind the bar table when an out-of-stock ingredient is delivered.")]
        [SerializeField] private DeliveryRisePresenter deliveryPresenter;

        private readonly List<CategoryRow> rows = new List<CategoryRow>();
        private readonly Dictionary<string, List<IngredientSlotUI>> slotsByInventoryId =
            new Dictionary<string, List<IngredientSlotUI>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<LiquorBottleDef> unlockedSignature = new List<LiquorBottleDef>();
        private readonly List<LiquorBottleDef> unlockedBuffer = new List<LiquorBottleDef>();

        private BusinessBartendingBootstrap bartending;
        private int currentRowIndex = -1;
        private bool initialized;
        private bool craftingAvailable;
        private Coroutine categoryRoutine;
        private CategoryRow slidingOutRow;
        private CategoryRow slidingInRow;
        private BottleDeliveryService delivery;

        public bool IsInteractable => craftingAvailable;

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
            LiquorShopCatalog shopCatalog = LiquorShopCatalog.LoadDefault();
            delivery = new BottleDeliveryService(
                new MoneyShopCurrency(),
                () => shopCatalog != null ? shopCatalog.shelfDeliveryPriceMultiplier : 10);
            RebuildRowsIfUnlockChanged();
        }

        // 해금 여부와 TV 방송 제한을 영업 흐름(BusinessFlowBootstrap)이 영업 시작 때 알려 준다.
        // 알려 주기 전에는 배송을 막아 둔다 — 제한을 모른 채 배송이 열리는 것보다 안전하다.
        public void SetDeliveryAvailable(bool available, string reason)
        {
            EnsureInitialized();
            delivery.SetAvailable(available, reason);
            RefreshAllAvailability();
        }

        // 표시는 항상 유지하고 조작 가능 여부만 바꾼다. 현재 대분류(currentRowIndex)는 건드리지 않아
        // 제조를 빠져나갔다 다시 들어와도 마지막으로 보던 줄에서 시작한다.
        public void SetCraftingAvailable(bool available)
        {
            EnsureInitialized();
            if (craftingAvailable == available)
                return;

            craftingAvailable = available;
            if (available)
            {
                ResolveBartending();
                RebuildRowsIfUnlockChanged();
                RefreshAllAvailability();
                return;
            }

            // 제조가 끝나는 즉시 조작을 막고, 올라와 있던 호버도 내린다.
            FinishCategorySlide();
            ClearHovers();
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

        // 슬롯이 바텐딩 부트스트랩을 직접 알지 않도록 "지금 손에 든 것이 있는지"만 중계한다.
        public bool HasHeldItem()
        {
            BusinessBartendingBootstrap session = ResolveBartending();
            return session != null && session.HasHeldBartendingItem();
        }

        public bool WasPressConsumedByReturn(int pressFrame)
        {
            return BottleReturnZones.LastReturnFrame >= pressFrame;
        }

        // 재료를 클릭하면 병(가니쉬 정의면 가니쉬)을 하나 꺼내 바로 손에 쥐여 준다.
        public void OnSlotClicked(SelectionSlotUI slot, Vector2 screenPosition)
        {
            if (slot is not IngredientSlotUI ingredient
                || ingredient.Definition == null
                || !CanHover(screenPosition))
            {
                return;
            }

            string failure;
            if (ingredient.IsDepleted)
            {
                // 돈이 모자라거나 배송이 막힌 경우도 여기서 걸러진다 — 아무 일도 일어나지 않는 게 정상이라
                // 경고 대신 일반 로그로만 남긴다.
                if (!delivery.TryOrder(ingredient.Definition, deliveryPresenter, HandleDelivered, out failure)
                    && !string.IsNullOrWhiteSpace(failure))
                {
                    Debug.Log("[IngredientSelection] " + failure);
                }
                RefreshAvailability(ingredient);
                return;
            }

            if (!bartending.TryTakeFromShelf(ingredient.Definition, screenPosition, out failure)
                && !string.IsNullOrWhiteSpace(failure))
            {
                Debug.LogWarning("[IngredientSelection] " + failure);
            }
        }

        public void RefreshSlot(IngredientSlotUI slot)
        {
            RefreshAvailability(slot);
        }

        // 연출이 끝난(또는 중단된) 순간 1병을 지급한다. StockChanged로 해당 슬롯이 다시 활성화된다.
        private void HandleDelivered(LiquorBottleDef definition)
        {
            BusinessBartendingBootstrap session = ResolveBartending();
            if (session != null)
            {
                session.AddDeliveredBottle(definition);
                return;
            }

            GameProgress progress = GameProgress.Instance;
            if (progress == null || definition == null)
                return;
            float total = progress.EnsureBottleAmount(definition.InventoryId, definition.DefaultAmount);
            progress.SetBottleAmount(
                definition.InventoryId,
                Mathf.Min(definition.MaxAmount, total + definition.unitVolume));
            RefreshAllAvailability();
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

            bool depleted = shelfAmount <= 0.01f;
            slot.SetStock(shelfAmount, delivery.GetOffer(definition, depleted));
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
