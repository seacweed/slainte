using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Slainte.Bartending;

public class LiquorBottleSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private LiquorBottleDef def;
    [Tooltip("Image Type = Filled / Horizontal. Shows remaining amount, always visible.")]
    [SerializeField] private Image amountFillImage;

    public LiquorBottleDef Definition => def;

    private Image         bottleImage;
    private RectTransform rectTransform;

    void Awake()
    {
        bottleImage   = GetComponent<Image>();
        rectTransform = GetComponent<RectTransform>();
        Refresh();
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

    private bool IsUnlocked()
    {
        return def != null
            && (string.IsNullOrEmpty(def.unlockFlagKey)
                || GameProgress.Instance.HasFlag(def.unlockFlagKey));
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!IsUnlocked() || LiquorBottleInfoCard.Instance == null) return;

        float amount = GameProgress.Instance.GetBottleAmount(def.id, def.MaxAmount);
        LiquorBottleInfoCard.Instance.Show(def, amount, rectTransform);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
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
