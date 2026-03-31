using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EpisodeChoiceButtonUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;

    public void Setup(string text, Action onClick)
    {
        if (label != null)
            label.text = text;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick?.Invoke());
    }
}