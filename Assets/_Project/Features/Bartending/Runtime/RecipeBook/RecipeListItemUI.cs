using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Slainte.Bartending;

// 도감 레시피 리스트(태그 결과, 이름 검색 결과 공용) 한 줄: 왼쪽 아이콘 + 오른쪽 이름.
public class RecipeListItemUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Image    icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Button   button;

    void Awake()
    {
        if (!button) button = GetComponent<Button>();
    }

    public void Setup(CocktailRecipe recipe, Action<CocktailRecipe> onClick)
    {
        if (recipe == null) return;

        if (icon)
        {
            icon.sprite = recipe.icon;
            icon.enabled = recipe.icon != null;
        }
        if (nameText) nameText.text = recipe.displayName;

        if (button)
        {
            button.onClick.RemoveAllListeners();
            if (onClick != null)
                button.onClick.AddListener(() => onClick(recipe));
        }
    }
}
