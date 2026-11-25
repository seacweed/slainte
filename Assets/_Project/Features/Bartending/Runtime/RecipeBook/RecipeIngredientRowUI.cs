using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 레시피 상세의 재료 한 줄: 라벨 배경(element-BG-*) 위 재료명 + 오른쪽 양(ml). 라벨은 클릭 불가 장식이다.
public class RecipeIngredientRowUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Image    labelBackground;

    public void Setup(string ingredientName, float ml, Sprite labelSprite = null)
    {
        if (nameText) nameText.text = ingredientName;
        if (amountText) amountText.text = $"{ml:0.#}ml";
        // null이면 프리팹에 지정된 기본 라벨 스프라이트를 그대로 둔다.
        if (labelBackground && labelSprite) labelBackground.sprite = labelSprite;
    }
}
