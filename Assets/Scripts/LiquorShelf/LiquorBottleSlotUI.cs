using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Slainte.Bartending;

public class LiquorBottleSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private static ItemDefCatalog cachedItemCatalog;

    [SerializeField] private LiquorBottleDef def;
    [Tooltip("Image Type = Filled / Horizontal. Shows remaining amount, always visible.")]
    [SerializeField] private Image amountFillImage;

    public LiquorBottleDef Definition => def;

    private Image         bottleImage;
    private RectTransform rectTransform;
    private GameProgress  subscribedProgress;
    private bool pointerInside;
    private bool hasCraftingItemDefinition;

    void Awake()
    {
        bottleImage   = GetComponent<Image>();
        rectTransform = GetComponent<RectTransform>();
        cachedItemCatalog ??= ItemDefCatalog.LoadFromResources("Items", null);
        hasCraftingItemDefinition = def != null
            && cachedItemCatalog.TryGet(def.id, out ItemDef item)
            && item != null
            && item.type == ItemType.Bottle;
        Refresh();
    }

    private void OnEnable()
    {
        subscribedProgress = GameProgress.Instance;
        if (subscribedProgress != null)
            subscribedProgress.BottleAmountChanged += HandleBottleAmountChanged;
        Refresh();
    }

    private void OnDisable()
    {
        pointerInside = false;
        if (subscribedProgress != null)
            subscribedProgress.BottleAmountChanged -= HandleBottleAmountChanged;
        subscribedProgress = null;
    }

    public void Refresh()
    {
        if (bottleImage == null) return;

        bool unlocked = IsUnlocked();

        bottleImage.enabled = unlocked && def.sprite != null;
        if (unlocked && def.sprite != null)
            bottleImage.sprite = def.sprite;

        RefreshAmountBar(unlocked);
    }

    private void RefreshAmountBar(bool unlocked)
    {
        if (amountFillImage == null) return;

        amountFillImage.enabled = unlocked;
        if (!unlocked) return;

        float amount = GameProgress.Instance.GetBottleAmount(def.id, def.MaxAmount);
        amountFillImage.fillAmount = def.MaxAmount > 0f ? Mathf.Clamp01(amount / def.MaxAmount) : 0f;
    }

    private void HandleBottleAmountChanged(string bottleId, float amount)
    {
        if (def == null || !string.Equals(def.id, bottleId, System.StringComparison.OrdinalIgnoreCase))
            return;

        RefreshAmountBar(IsUnlocked());
        if (pointerInside && LiquorBottleInfoCard.Instance != null && rectTransform != null)
            LiquorBottleInfoCard.Instance.Show(def, amount, rectTransform);
    }

    private bool IsUnlocked()
    {
        return def != null
            && hasCraftingItemDefinition
            && (string.IsNullOrEmpty(def.unlockFlagKey)
                || GameProgress.Instance.HasFlag(def.unlockFlagKey));
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        if (!IsUnlocked() || LiquorBottleInfoCard.Instance == null) return;

        float amount = GameProgress.Instance.GetBottleAmount(def.id, def.MaxAmount);
        LiquorBottleInfoCard.Instance.Show(def, amount, rectTransform);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        if (LiquorBottleInfoCard.Instance == null) return;
        LiquorBottleInfoCard.Instance.Hide();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left || !IsUnlocked())
            return;

        BusinessBartendingBootstrap bartending =
            FindFirstObjectByType<BusinessBartendingBootstrap>();
        if (bartending == null)
        {
            Debug.LogWarning("[LiquorShelf] 제작 세션을 찾을 수 없습니다.");
            return;
        }

        if (!bartending.TryPlaceBottleFromShelf(def, out string failure))
        {
            Debug.LogWarning("[LiquorShelf] " + failure);
            return;
        }

        Refresh();
        if (LiquorBottleInfoCard.Instance != null)
        {
            float amount = GameProgress.Instance.GetBottleAmount(def.id, def.MaxAmount);
            LiquorBottleInfoCard.Instance.Show(def, amount, rectTransform);
        }
    }
}
