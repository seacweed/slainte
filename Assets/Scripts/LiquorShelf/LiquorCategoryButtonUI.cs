using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LiquorCategoryButtonUI : MonoBehaviour
{
    [SerializeField] private Image   iconImage;
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private Button  button;

    public Button Button => button;

    public void Bind(LiquorCategoryDef def, Action<LiquorCategoryDef> onClick)
    {
        if (def == null) return;
        Bind(def.displayName, def.icon, () => onClick?.Invoke(def));
    }

    public void Bind(string displayName, Sprite icon, Action onClick)
    {
        if (iconImage)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }
        if (labelText) labelText.text = displayName ?? string.Empty;
        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }

    public void SetInteractable(bool on)
    {
        if (button) button.interactable = on;
    }
}
