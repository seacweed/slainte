using Slainte.Shared.Lifecycle;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LiquorBottleInfoCard : SceneSingleton<LiquorBottleInfoCard>
{
    private const int FirstRowIconCount = 5;

    [Header("Content")]
    [SerializeField] private RectTransform     rect;
    [SerializeField] private TextMeshProUGUI   nameText;
    [SerializeField] private TextMeshProUGUI   subCategoryText;
    [Tooltip("Bottle icons in display order (left to right, first row then second row).")]
    [SerializeField] private Image[]           stateImages;
    [Tooltip("Second row container, shown only when bottleCount exceeds the first row's capacity.")]
    [SerializeField] private GameObject        secondRowContainer;
    [SerializeField] private TextMeshProUGUI   amountText;

    [Header("State Sprites")]
    [SerializeField] private LiquorStockLevelPalette stockLevelPalette;

    protected override void Awake()
    {
        base.Awake();
        gameObject.SetActive(false);
    }

    public void Show(LiquorBottleDef def, float amount, RectTransform targetRect)
    {
        if (def == null || rect == null || targetRect == null) return;

        gameObject.SetActive(true);

        if (nameText != null)        nameText.text = def.displayName;
        if (subCategoryText != null) subCategoryText.text = def.subCategory;

        float clampedAmount = Mathf.Clamp(amount, 0f, def.MaxAmount);
        int   fullCount     = Mathf.Min(def.bottleCount, Mathf.FloorToInt(clampedAmount / def.unitVolume));
        float remainder     = clampedAmount - fullCount * def.unitVolume;
        bool  hasInUse      = remainder > 0f && fullCount < def.bottleCount;
        int   inUseCount    = hasInUse ? 1 : 0;

        if (secondRowContainer != null)
            secondRowContainer.SetActive(def.bottleCount > FirstRowIconCount);

        for (int i = 0; i < stateImages.Length; i++)
        {
            Image img = stateImages[i];
            if (img == null) continue;

            bool inRange = i < def.bottleCount;
            img.gameObject.SetActive(inRange);
            if (!inRange) continue;

            Sprite stateSprite = i < fullCount ? stockLevelPalette.GetSprite(1f)
                                : i < fullCount + inUseCount ? stockLevelPalette.GetSprite(remainder / def.unitVolume)
                                : stockLevelPalette.GetSprite(0f);
            img.enabled = stateSprite != null;
            img.sprite  = stateSprite;
        }

        // 마지막 병이 정확히 꽉 찬 경우(remainder == 0)에도 잔량 텍스트를 그 병의 용량 그대로 표시한다.
        // 재고가 아예 없을 때(clampedAmount == 0)만 숨긴다.
        bool  showAmount    = clampedAmount > 0f;
        float displayAmount = remainder > 0f ? remainder : def.unitVolume;

        if (amountText != null)
        {
            amountText.gameObject.SetActive(showAmount);
            if (showAmount)
                amountText.text = $"{Mathf.RoundToInt(displayAmount)}/{Mathf.RoundToInt(def.unitVolume)}ml";
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        UpdatePosition(targetRect);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void UpdatePosition(RectTransform targetRect)
    {
        Vector3[] corners = new Vector3[4];
        targetRect.GetWorldCorners(corners);
        Vector3 targetRightCenter = (corners[2] + corners[3]) * 0.5f;
        float   cardWidth   = rect.rect.width;
        float   screenWidth = Screen.width;

        if (targetRightCenter.x + cardWidth > screenWidth)
        {
            rect.pivot = new Vector2(1, 0.5f);
            Vector3 targetLeftCenter = (corners[0] + corners[1]) * 0.5f;
            rect.position = targetLeftCenter - new Vector3(10, 0, 0);
        }
        else
        {
            rect.pivot = new Vector2(0, 0.5f);
            rect.position = targetRightCenter + new Vector3(10, 0, 0);
        }
    }
}
