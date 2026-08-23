using UnityEngine;
using TMPro;

// 레시피 상세 화면의 재료 한 줄: 재료명 + 양(ml).
public class RecipeIngredientRowUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text amountText;

    public void Setup(string ingredientName, float ml, Color? nameColor = null)
    {
        if (nameText)
        {
            nameText.text = ingredientName;
            if (nameColor.HasValue) nameText.color = nameColor.Value;
        }
        if (amountText) amountText.text = $"{ml:0.#}ml";
    }
}
