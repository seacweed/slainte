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
    
    public Button Button => button;

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
}
