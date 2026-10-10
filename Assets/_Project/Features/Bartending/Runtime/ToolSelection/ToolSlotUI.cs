using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    // 도구 공간의 잔·도구 하나. 호버·라벨·클릭 가드는 SelectionSlotUI가 맡고, 여기서는 여러 장으로
    // 겹친 도구 이미지(셰이커 부품, 잔 앞뒤 등)를 표시한다. 도구는 정의당 월드 인스턴스가 하나뿐이라
    // 꺼낸 동안에는 반투명으로 남겨 자리를 알려 주고, 보관 중에는 실제 상태(얼음 잔량 등)를 반영한다.
    public sealed class ToolSlotUI : SelectionSlotUI
    {
        [Header("Icon")]
        [Tooltip("Template image cloned once per sprite layer. Assign the SpriteSolidOutline material here.")]
        [SerializeField] private Image icon;
        [Tooltip("Gap between the top of the icon frame and the bottom of the hover labels.")]
        [SerializeField] private float labelGap = 12f;

        private readonly List<Image> layerImages = new List<Image>();
        private readonly List<Image> outlineImages = new List<Image>();
        private Sprite[] defaultSprites = Array.Empty<Sprite>();
        private RectTransform layerRoot;
        private RectTransform outlineRoot;
        private SlotController boundSlot;

        public string DefinitionId { get; private set; } = string.Empty;

        protected override void OnInitialize()
        {
            if (icon != null)
            {
                icon.raycastTarget = false;
                // 템플릿은 복제 원본으로만 쓰고 직접 그리지 않는다.
                icon.gameObject.SetActive(false);
            }
        }

        public void Setup(
            ISelectionSlotOwner owner,
            string definitionId,
            string displayName,
            string categoryLabel,
            Color categoryColor,
            Sprite[] sprites,
            Vector2 iconSize,
            bool flipVertical)
        {
            EnsureInitialized();
            BindOwner(owner);
            DefinitionId = definitionId ?? string.Empty;
            defaultSprites = sprites != null ? (Sprite[])sprites.Clone() : Array.Empty<Sprite>();
            SetLabels(displayName, categoryLabel, categoryColor);

            BuildLayers(iconSize, flipVertical);
            PlaceLabelsAboveLayers(iconSize);
            RefreshFromSlot();
        }

        // 라벨은 프리팹 배치(재료 아이콘 기준)가 아니라 런타임에 크기가 정해지는 레이어 기준으로 둔다.
        // 아트가 모두 같은 고정 프레임(310x590)이라 프레임 윗변 위에 두면 모든 도구의 라벨이 같은 높이에 뜬다.
        private void PlaceLabelsAboveLayers(Vector2 iconSize)
        {
            if (hoverLabelRoot == null || layerRoot == null)
                return;

            RectTransform labelRect = hoverLabelRoot.transform as RectTransform;
            if (labelRect == null)
                return;

            Vector3 world = layerRoot.TransformPoint(new Vector3(0f, iconSize.y + labelGap, 0f));
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.position = world;
        }

        public void BindSlot(SlotController slot)
        {
            if (boundSlot != null)
                boundSlot.OccupancyChanged -= HandleOccupancyChanged;
            boundSlot = slot;
            if (boundSlot != null)
                boundSlot.OccupancyChanged += HandleOccupancyChanged;
            RefreshFromSlot();
        }

        public bool IsAvailable => boundSlot != null && boundSlot.IsOccupied;

        // 보관 중이면 도구가 알려 주는 현재 모습(얼음 버킷 잔량 등)을, 꺼낸 동안에는 기본 모습을 반투명으로 그린다.
        public void RefreshFromSlot()
        {
            IBartendingItem item = IsAvailable ? boundSlot.OccupiedItem : null;
            IBartendingCabinetVisualProvider provider = item as IBartendingCabinetVisualProvider;
            float alpha = IsAvailable ? 1f : depletedAlpha;

            for (int i = 0; i < layerImages.Count; i++)
            {
                Sprite fallback = i < defaultSprites.Length ? defaultSprites[i] : null;
                Sprite sprite = provider != null
                    ? provider.GetCabinetVisualSprite(i, fallback)
                    : fallback;
                ApplyLayer(layerImages[i], sprite, alpha);
                if (i < outlineImages.Count)
                    ApplyLayer(outlineImages[i], sprite, alpha);
            }
        }

        // 겹친 레이어마다 윤곽선을 그리면 레이어 경계가 안쪽에 선으로 남으므로, 윤곽선용 복제본을
        // 실제 레이어 전체보다 뒤(outlineRoot)에 깔아 바깥 실루엣의 윤곽선만 보이게 한다.
        protected override void SetHoverDecorations(bool visible)
        {
            base.SetHoverDecorations(visible);
            if (outlineRoot != null)
                outlineRoot.gameObject.SetActive(visible);
        }

        private void BuildLayers(Vector2 iconSize, bool flipVertical)
        {
            ClearLayers();
            if (icon == null || visualRoot == null)
                return;

            outlineRoot = CreateLayerRoot("OutlineLayers", iconSize);
            layerRoot = CreateLayerRoot("Layers", iconSize);
            for (int i = 0; i < defaultSprites.Length; i++)
            {
                outlineImages.Add(CreateLayerImage(outlineRoot, i, flipVertical, outlined: true));
                layerImages.Add(CreateLayerImage(layerRoot, i, flipVertical, outlined: false));
            }

            outlineRoot.gameObject.SetActive(false);
        }

        // 아래 가운데 기준으로 세워 두어, 도구 밑부분이 제작 테이블 뒤로 자연스럽게 가려지게 한다.
        private RectTransform CreateLayerRoot(string rootName, Vector2 size)
        {
            GameObject rootObject = new GameObject(rootName, typeof(RectTransform));
            rootObject.layer = gameObject.layer;
            RectTransform rect = (RectTransform)rootObject.transform;
            rect.SetParent(visualRoot, false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            return rect;
        }

        private Image CreateLayerImage(RectTransform parent, int index, bool flipVertical, bool outlined)
        {
            GameObject clone = Instantiate(icon.gameObject, parent, false);
            clone.name = (outlined ? "Outline_" : "Layer_") + index;
            clone.SetActive(true);

            RectTransform rect = (RectTransform)clone.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = flipVertical ? new Vector3(1f, -1f, 1f) : Vector3.one;

            IngredientOutlineEffect outline = clone.GetComponent<IngredientOutlineEffect>();
            if (outlined && outline == null)
                outline = clone.AddComponent<IngredientOutlineEffect>();
            if (outline != null)
                outline.enabled = outlined;

            Image image = clone.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        private static void ApplyLayer(Image image, Sprite sprite, float alpha)
        {
            if (image == null)
                return;

            image.sprite = sprite;
            image.enabled = sprite != null;
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }

        private void ClearLayers()
        {
            if (layerRoot != null)
                Destroy(layerRoot.gameObject);
            if (outlineRoot != null)
                Destroy(outlineRoot.gameObject);
            layerRoot = null;
            outlineRoot = null;
            layerImages.Clear();
            outlineImages.Clear();
        }

        private void HandleOccupancyChanged(SlotController slot, IBartendingItem item)
        {
            RefreshFromSlot();
        }

        private void OnDestroy()
        {
            if (boundSlot != null)
                boundSlot.OccupancyChanged -= HandleOccupancyChanged;
        }
    }
}
