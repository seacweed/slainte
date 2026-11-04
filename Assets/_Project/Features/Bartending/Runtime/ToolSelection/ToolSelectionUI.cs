using System;
using System.Collections.Generic;
using Slainte.Content;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    // 술 선택 공간과 제작 공간 사이, 테이블(table_serve_2) 양옆 블록에 잔·도구를 진열하는 도구 공간.
    // 왼쪽 줄에는 잔 4종, 오른쪽 줄에는 지거·셰이커·바스푼이 놓이며 A/D 대분류 전환과는
    // 무관하게 항상 같은 자리에 있다. 도구는 정의당 월드 인스턴스가 하나뿐이므로 실제 보관·꺼내기·반납은
    // BusinessBartendingBootstrap의 캐비닛 슬롯 API에 위임하고, 여기서는 진열 표시와 입력 가드만 맡는다.
    // 들고 있는 도구로 진열 영역을 클릭하면 어느 쪽이든 그 도구의 자리로 반납된다.
    [DisallowMultipleComponent]
    public sealed class ToolSelectionUI : MonoBehaviour, ISelectionSlotOwner, IToolReturnZone
    {
        private const string CatalogResourcePath = ProjectResourcePaths.BartendingToolCabinetCatalog;
        private const string SettingsResourcePath = ProjectResourcePaths.BartendingSettings;

        private static readonly ToolKind[] ToolOrder =
        {
            ToolKind.Jigger,
            ToolKind.CobblerShaker,
            // 얼음 버킷은 기획 변경으로 진열에서 뺐다. 카탈로그·캐비닛 인스턴스와 얼음 판정은 새 얼음
            // 기획이 정해질 때까지 그대로 둔다.
            ToolKind.BarSpoon
        };
        private static readonly string[] GlassOrder =
        {
            "glass_rock",
            "glass_martini",
            "glass_highball",
            "glass_hurricane"
        };

        [Header("Layout")]
        [SerializeField] private ToolSlotUI slotPrefab;
        [Tooltip("Left table block. Holds the glasses and doubles as a return area.")]
        [SerializeField] private RectTransform glassRow;
        [Tooltip("Right table block. Holds the tools and doubles as a return area.")]
        [SerializeField] private RectTransform toolRow;
        [SerializeField] private float slotSpacing = 0f;
        [SerializeField] private TextAnchor rowAlignment = TextAnchor.LowerCenter;
        [Tooltip("Icon size = sprite pixels x definition worldScale x this value. 1 matches the crafting table size.")]
        [SerializeField, Min(0.05f)] private float iconScale = 1f;

        [Header("Hover Labels")]
        [Tooltip("Fixed text shown under the glass name.")]
        [SerializeField] private string glassCategoryLabel = string.Empty;
        [SerializeField] private Color glassCategoryColor = Color.white;
        [Tooltip("Fixed text shown under the tool name.")]
        [SerializeField] private string toolCategoryLabel = string.Empty;
        [SerializeField] private Color toolCategoryColor = Color.white;

        private readonly Dictionary<string, ToolSlotUI> slots =
            new Dictionary<string, ToolSlotUI>(StringComparer.OrdinalIgnoreCase);
        private BusinessBartendingBootstrap bootstrap;
        private ToolCabinetCatalog catalog;

        public bool IsConfigured { get; private set; }

        private void OnEnable()
        {
            ToolReturnZones.Register(this);
        }

        private void OnDisable()
        {
            ToolReturnZones.Unregister(this);
        }

        // 부트스트랩이 씬 로드 직후 호출한다. 설정에서 도구장을 끄거나 데이터가 없으면 IsConfigured가
        // false로 남아, 세션 조립이 고정 도구를 테이블에 바로 놓는 방식으로 폴백한다.
        public void Initialize(BusinessBartendingBootstrap owner)
        {
            bootstrap = owner;
            BusinessBartendingSettings settings =
                Resources.Load<BusinessBartendingSettings>(SettingsResourcePath);
            if (settings == null || !settings.useToolCabinet)
                return;

            catalog = Resources.Load<ToolCabinetCatalog>(CatalogResourcePath);
            if (catalog == null || slotPrefab == null || glassRow == null || toolRow == null)
            {
                Debug.LogWarning("[ToolSelection] Catalog, slot prefab or rows are missing; fixed tools remain enabled.");
                return;
            }

            BuildSlots();
            IsConfigured = slots.Count > 0;
        }

        public void NotifySessionReady()
        {
            bootstrap?.PrepareCabinetInventory(catalog);
            foreach (ToolSlotUI slot in slots.Values)
                slot?.RefreshFromSlot();
        }

        public void NotifySessionDestroyed()
        {
            foreach (ToolSlotUI slot in slots.Values)
            {
                if (slot == null)
                    continue;
                slot.ForceUnhover();
                slot.BindSlot(null);
            }
        }

        public void BindCabinetSlot(string definitionId, SlotController cabinetSlot)
        {
            if (!string.IsNullOrWhiteSpace(definitionId)
                && slots.TryGetValue(definitionId, out ToolSlotUI slot))
            {
                slot?.BindSlot(cabinetSlot);
            }
        }

        public void NotifyAvailability(string definitionId, bool available)
        {
            if (!string.IsNullOrWhiteSpace(definitionId)
                && slots.TryGetValue(definitionId, out ToolSlotUI slot))
            {
                slot?.RefreshFromSlot();
            }
        }

        public bool CanHover(Vector2 screenPosition)
        {
            return bootstrap != null
                && bootstrap.IsSessionReady
                && !bootstrap.HasHeldBartendingItem()
                && !bootstrap.IsPointerOverWorldItem(screenPosition);
        }

        public bool HasHeldItem()
        {
            return bootstrap != null && bootstrap.HasHeldBartendingItem();
        }

        // 진열 영역은 술 선택 공간 줄과 겹치므로 병 반환에 쓰인 누름도 함께 걸러낸다.
        public bool WasPressConsumedByReturn(int pressFrame)
        {
            return ToolReturnZones.LastReturnFrame >= pressFrame
                || BottleReturnZones.LastReturnFrame >= pressFrame;
        }

        public void OnSlotClicked(SelectionSlotUI slot, Vector2 screenPosition)
        {
            if (slot is not ToolSlotUI toolSlot
                || !toolSlot.IsAvailable
                || !CanHover(screenPosition))
            {
                return;
            }

            if (!bootstrap.TryPickUpCabinetItem(toolSlot.DefinitionId, screenPosition, out string failure)
                && !string.IsNullOrWhiteSpace(failure))
            {
                Debug.LogWarning("[ToolSelection] " + failure);
            }
        }

        public bool ContainsReturnPoint(Vector2 screenPosition)
        {
            if (bootstrap == null || !bootstrap.IsSessionReady)
                return false;

            return ContainsScreenPoint(glassRow, screenPosition)
                || ContainsScreenPoint(toolRow, screenPosition);
        }

        // 진열대 도구가 아닌 물체(병 등)는 반납 대상이 아니므로 false로 돌려 일반 놓기로 넘긴다.
        public bool TryReturn(IBartendingItem item)
        {
            ToolCabinetRuntimeTag tag = item?.GameObject != null
                ? item.GameObject.GetComponent<ToolCabinetRuntimeTag>()
                : null;
            if (tag == null || string.IsNullOrWhiteSpace(tag.DefinitionId) || bootstrap == null)
                return false;

            if (!bootstrap.TryReturnCabinetItem(item, tag.DefinitionId, out string failure)
                && !string.IsNullOrWhiteSpace(failure))
            {
                Debug.LogWarning("[ToolSelection] " + failure);
            }
            return true;
        }

        private void BuildSlots()
        {
            slots.Clear();
            ConfigureRow(glassRow);
            ConfigureRow(toolRow);

            GlassDef[] glasses = catalog.glasses ?? Array.Empty<GlassDef>();
            for (int i = 0; i < GlassOrder.Length; i++)
            {
                GlassDef definition = FindGlass(glasses, GlassOrder[i]);
                if (definition == null || string.IsNullOrWhiteSpace(definition.StableId))
                    continue;

                CreateSlot(
                    glassRow,
                    definition.StableId,
                    definition.displayName,
                    glassCategoryLabel,
                    glassCategoryColor,
                    definition.GetCabinetLayers(),
                    definition.shelfInSprite,
                    definition.shelfDisabledSprite,
                    definition.worldScale,
                    false);
            }

            ToolDef[] tools = catalog.tools ?? Array.Empty<ToolDef>();
            for (int i = 0; i < ToolOrder.Length; i++)
            {
                ToolDef definition = FindTool(tools, ToolOrder[i]);
                if (definition == null || string.IsNullOrWhiteSpace(definition.StableId))
                    continue;

                // 바스푼 아트는 손잡이가 아래로 그려져 있어 진열 때만 뒤집어 세운다(기존 도구장과 동일).
                CreateSlot(
                    toolRow,
                    definition.StableId,
                    definition.displayName,
                    toolCategoryLabel,
                    toolCategoryColor,
                    definition.cabinetLayers ?? Array.Empty<Sprite>(),
                    definition.shelfInSprite,
                    definition.shelfDisabledSprite,
                    definition.worldScale,
                    definition.kind == ToolKind.BarSpoon);
            }
        }

        // 월드 도구는 "스프라이트 원본 픽셀 크기 × worldScale"로 캔버스에 맞춰지므로(BartendingNativeSpriteSizer),
        // 같은 식으로 아이콘 크기를 정하면 세션이 없어도 제작 테이블 위 크기와 같은 비율로 진열된다.
        private void CreateSlot(
            RectTransform row,
            string definitionId,
            string displayName,
            string categoryLabel,
            Color categoryColor,
            Sprite[] sprites,
            Sprite shelfIn,
            Sprite shelfDisabled,
            float worldScale,
            bool flipVertical)
        {
            Vector2 measured = shelfIn != null ? shelfIn.rect.size : MeasureSprites(sprites);
            Vector2 iconSize = measured * Mathf.Max(0.05f, worldScale) * iconScale;
            ToolSlotUI slot = Instantiate(slotPrefab, row);
            slot.name = "ToolSlot_" + definitionId;
            RectTransform slotRect = (RectTransform)slot.transform;
            slotRect.sizeDelta = iconSize;
            slot.Setup(
                this,
                definitionId,
                string.IsNullOrWhiteSpace(displayName) ? definitionId : displayName,
                categoryLabel,
                categoryColor,
                sprites,
                shelfIn,
                shelfDisabled,
                iconSize,
                flipVertical);
            slots[definitionId] = slot;
        }

        private static Vector2 MeasureSprites(Sprite[] sprites)
        {
            Vector2 size = Vector2.zero;
            if (sprites == null)
                return size;

            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null)
                    continue;
                size = Vector2.Max(size, sprites[i].rect.size);
            }
            return size;
        }

        private void ConfigureRow(RectTransform row)
        {
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
                layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = rowAlignment;
            layout.spacing = slotSpacing;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        private static bool ContainsScreenPoint(RectTransform rect, Vector2 screenPosition)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy)
                return false;

            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, canvasCamera);
        }

        private static ToolDef FindTool(ToolDef[] tools, ToolKind kind)
        {
            for (int i = 0; i < tools.Length; i++)
            {
                if (tools[i] != null && tools[i].kind == kind)
                    return tools[i];
            }
            return null;
        }

        private static GlassDef FindGlass(GlassDef[] glasses, string stableId)
        {
            for (int i = 0; i < glasses.Length; i++)
            {
                if (glasses[i] != null
                    && string.Equals(glasses[i].StableId, stableId, StringComparison.OrdinalIgnoreCase))
                {
                    return glasses[i];
                }
            }
            return null;
        }
    }
}
