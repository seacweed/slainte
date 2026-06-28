using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RecipeSearchOptionButton : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button   button;
    [SerializeField] private Image    background;

    private RecipeSearchUI _owner;
    private string _label;
    private Color  _color;
    private Color  _textColor;

    void Awake()
    {
        if (!button)     button     = GetComponent<Button>();
        if (!background) background = GetComponent<Image>();
        if (button) button.onClick.AddListener(OnClicked);
    }

    public void Setup(string text, Color color, Color textColor, RecipeSearchUI owner)
    {
        _owner     = owner;
        _label     = text;
        _color     = color;
        _textColor = textColor;

        if (label)
        {
            label.text  = text;
            label.color = textColor;
        }
        if (background) background.color = color;
    }

    private void OnClicked()
    {
        _owner?.NotifyOptionClicked(_label, _color, _textColor);
    }
}
