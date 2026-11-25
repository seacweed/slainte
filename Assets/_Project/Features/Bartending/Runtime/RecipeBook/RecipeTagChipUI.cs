using UnityEngine;
using TMPro;

// 레시피 상세의 분위기 표시 칩(클릭 불가 장식). 배경 스프라이트는 프리팹(element-BG-yellow)이 정한다.
public class RecipeTagChipUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    public void Setup(string tag)
    {
        if (label) label.text = tag;
    }
}
