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

    public void Bind(LiquorCategoryDef def, Action<LiquorCategoryDef> onClick)
    {
        if (iconImage)  iconImage.sprite = def.icon;
        if (labelText)  labelText.text   = def.displayName;
        if (colorImage) colorImage.color = def.color;
        button.onClick.AddListener(() => onClick(def));
    }

    public void SetInteractable(bool on)
    {
        if (button) button.interactable = on;
    }
}
