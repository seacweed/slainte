using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LiquorCategoryButtonUI : MonoBehaviour
{
    [SerializeField] private Image   iconImage;
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private Button  button;

    public void Bind(LiquorCategoryDef def, Action<LiquorCategoryDef> onClick)
    {
        if (iconImage)  iconImage.sprite = def.icon;
        if (labelText)  labelText.text   = def.displayName;
        button.onClick.AddListener(() => onClick(def));
    }

    public void SetInteractable(bool on)
    {
        if (button) button.interactable = on;
    }
}
