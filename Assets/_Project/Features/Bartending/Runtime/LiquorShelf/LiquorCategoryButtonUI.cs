using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LiquorCategoryButtonUI : MonoBehaviour
{
    [SerializeField] private Image   iconImage;
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private Button  button;
    [Tooltip("Optional same-shape accent image tinted with the category's unique color. Left unassigned where the button shouldn't show the color (e.g. liquor shelf).")]
    [SerializeField] private Image   colorImage;

    private Image _disabledOverlay;
    
    public Button Button => button;
    public Sprite IconSprite => iconImage != null ? iconImage.sprite : null;
    public bool DisabledOverlayVisible =>
        _disabledOverlay != null && _disabledOverlay.gameObject.activeSelf;

    public void Bind(
        LiquorCategoryDef def,
        Action<LiquorCategoryDef> onClick)
    {
        if (def == null) return;

        Bind(
            def.displayName,
            def.icon,
            () => onClick?.Invoke(def));

        if (colorImage != null)
            colorImage.color = def.color;
    }

    public void Bind(
        string displayName,
        Sprite icon,
        Action onClick)
    {
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        if (_disabledOverlay != null)
            _disabledOverlay.sprite = icon;

        if (labelText != null)
            labelText.text = displayName ?? string.Empty;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(
                () => onClick?.Invoke());
        }
    }

    public void SetInteractable(bool on)
    {
        if (button) button.interactable = on;
    }

    public void SetDisabledOverlayVisible(bool visible)
    {
        if (!visible && _disabledOverlay == null)
            return;

        EnsureDisabledOverlay();
        _disabledOverlay.gameObject.SetActive(visible);
    }

    private void EnsureDisabledOverlay()
    {
        if (_disabledOverlay != null)
            return;

        GameObject overlayObject = new GameObject(
            "DisabledOverlay",
            typeof(RectTransform),
            typeof(Image));
        overlayObject.layer = gameObject.layer;

        RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
        overlayRect.SetParent(transform, false);
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlayRect.SetAsLastSibling();

        _disabledOverlay = overlayObject.GetComponent<Image>();
        _disabledOverlay.sprite = IconSprite;
        _disabledOverlay.type = iconImage != null ? iconImage.type : Image.Type.Simple;
        _disabledOverlay.preserveAspect = iconImage != null && iconImage.preserveAspect;
        _disabledOverlay.color = new Color(0.35f, 0.35f, 0.35f, 0.68f);
        _disabledOverlay.raycastTarget = false;
    }
}
