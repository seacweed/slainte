using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// CategoryView의 맛/분위기 태그 버튼 하나. 모든 태그가 같은 button-2 스프라이트를 쓰고,
// 호버 연출은 HoverSwapButton이, 클릭은 Button이 처리한다.
// (상세 화면의 분위기 칩은 클릭 불가 장식이라 RecipeTagChipUI로 분리했다.)
public class RecipeSearchOptionButton : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text        label;
    [SerializeField] private Button          button;
    [SerializeField] private HoverSwapButton hover;

    private bool _initialized;

    void Awake() => EnsureInitialized();

    // 비활성 부모(CategoryView) 아래에서 Instantiate되면 Awake가 미뤄지므로 Setup에서도 초기화를 보장한다.
    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        if (!button) button = GetComponent<Button>();
        if (!hover)  hover  = GetComponent<HoverSwapButton>();

        // 프리팹에 LayoutElement가 없으면 세로 목록에서 높이가 0으로 눌리므로 원래 높이를 고정한다.
        LayoutElement layoutElement = GetComponent<LayoutElement>();
        if (!layoutElement) layoutElement = gameObject.AddComponent<LayoutElement>();
        if (layoutElement.preferredHeight < 0f)
        {
            float height = ((RectTransform)transform).sizeDelta.y;
            layoutElement.minHeight = height;
            layoutElement.preferredHeight = height;
        }
    }

    public void Setup(string tag, Action<string> onSelected)
    {
        EnsureInitialized();

        if (label) label.text = tag;

        if (button)
        {
            button.onClick.RemoveAllListeners();
            if (onSelected != null)
                button.onClick.AddListener(() => onSelected(tag));
        }
        if (hover) hover.SetInteractable(onSelected != null);
    }
}
