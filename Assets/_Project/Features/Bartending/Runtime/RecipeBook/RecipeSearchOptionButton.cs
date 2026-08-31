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

    private LayoutElement _layoutElement;
    private bool          _initialized;

    void Awake() => EnsureInitialized();

    // Instantiate()로 만들어진 직후 부모(DetailView/ResultsView)가 아직 비활성 상태이면
    // Unity가 Awake() 호출을 활성화될 때까지 미룬다. Setup()은 그 전에 곧바로 호출되므로,
    // Awake에만 기대지 않고 Setup 쪽에서도 초기화를 보장한다.
    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        if (!button)     button     = GetComponent<Button>();
        if (!background) background = GetComponent<Image>();

        if (button)
        {
            // Setup()이 상태와 무관하게 배경/글자색을 직접 칠하므로, Selectable의
            // 자동 상태별 색 틴트(특히 disabled의 흐린 회색)가 그 위에 덮어써지지
            // 않도록 완전히 끈다.
            button.transition = Selectable.Transition.None;
        }

        _layoutElement = GetComponent<LayoutElement>();
        if (!_layoutElement) _layoutElement = gameObject.AddComponent<LayoutElement>();
        if (_layoutElement.preferredHeight < 0f)
        {
            float height = ((RectTransform)transform).sizeDelta.y;
            _layoutElement.minHeight = height;
            _layoutElement.preferredHeight = height;
        }
    }

    public void Setup(
        string tag,
        Color backgroundColor,
        Color textColor,
        bool interactable,
        Action<string> onSelected = null,
        float? fontSize = null,
        float? preferredHeight = null)
    {
        EnsureInitialized();

        if (label)
        {
            label.text  = tag;
            label.color = textColor;
            // TMP 오토사이징이 켜져 있으면 이 값은 무시될 수 있다.
            if (fontSize.HasValue) label.fontSize = fontSize.Value;
        }
        if (background) background.color = backgroundColor;

        if (preferredHeight.HasValue && _layoutElement)
        {
            _layoutElement.minHeight = preferredHeight.Value;
            _layoutElement.preferredHeight = preferredHeight.Value;
        }

        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.interactable = interactable;
            if (interactable && onSelected != null)
                button.onClick.AddListener(() => onSelected(tag));
        }
    }
}
