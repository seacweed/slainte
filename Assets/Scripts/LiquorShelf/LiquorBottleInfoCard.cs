using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LiquorBottleInfoCard : MonoBehaviour
{
    public static LiquorBottleInfoCard Instance { get; private set; }

    [Header("Content")]
    [SerializeField] private RectTransform     rect;
    [SerializeField] private TextMeshProUGUI   nameText;
    [SerializeField] private TextMeshProUGUI   subCategoryText;
    [Tooltip("Bottle icons in display order (left to right).")]
    [SerializeField] private Image[]           stateImages;
    [SerializeField] private TextMeshProUGUI   amountText;

    [Header("State Sprites")]
    [SerializeField] private Sprite fullSprite;
    [SerializeField] private Sprite inUseSprite;
    [SerializeField] private Sprite emptySprite;

    void Awake()
    {
        Instance = this;
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

        for (int i = 0; i < stateImages.Length; i++)
        {
            Image img = stateImages[i];
            if (img == null) continue;

            bool inRange = i < def.bottleCount;
            img.gameObject.SetActive(inRange);
            if (!inRange) continue;

            Sprite stateSprite = i < fullCount ? fullSprite
                                : i < fullCount + inUseCount ? inUseSprite
                                : emptySprite;
            img.enabled = stateSprite != null;
            img.sprite  = stateSprite;
        }

        if (amountText != null)
        {
            amountText.gameObject.SetActive(hasInUse);
            if (hasInUse)
                amountText.text = $"{Mathf.RoundToInt(remainder)}/{Mathf.RoundToInt(def.unitVolume)}ml";
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
