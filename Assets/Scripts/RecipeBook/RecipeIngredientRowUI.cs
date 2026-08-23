using UnityEngine;
using TMPro;

// 레시피 상세 화면의 재료 한 줄: 재료명 + 양(ml).
public class RecipeIngredientRowUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text amountText;

    public void Setup(string ingredientName, float ml)
    {
        if (nameText) nameText.text = ingredientName;
        if (amountText) amountText.text = $"{ml:0.#}ml";
    }
}
