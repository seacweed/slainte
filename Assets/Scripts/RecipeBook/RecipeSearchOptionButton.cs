using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 맛/분위기 태그 하나를 표시하는 공용 버튼. 클릭 가능한 옵션 목록(CategoryView),
// 클릭 불가능한 제목 표시(ResultsView), 레시피 상세의 태그 칩(RecipeDetailUI)
// 세 곳에서 동일 프리팹을 재사용한다.
public class RecipeSearchOptionButton : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button   button;
    [SerializeField] private Image    background;

    void Awake()
    {
        if (!button)     button     = GetComponent<Button>();
        if (!background) background = GetComponent<Image>();
    }

    public void Setup(
        string tag,
        Color backgroundColor,
        Color textColor,
        bool interactable,
        Action<string> onSelected = null)
    {
        if (label)
        {
            label.text  = tag;
            label.color = textColor;
        }
        if (background) background.color = backgroundColor;

        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = interactable;
            if (interactable && onSelected != null)
                button.onClick.AddListener(() => onSelected(tag));
        }
    }
}
