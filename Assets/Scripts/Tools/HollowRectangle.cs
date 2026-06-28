using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("UI/Hollow Rectangle")]
public class HollowRectangle : MonoBehaviour
{
    [SerializeField] private float thickness = 4f;
    [SerializeField] private Color color = Color.white;

    private Image _top;
    private Image _bottom;
    private Image _left;
    private Image _right;

    public float Thickness
    {
        get => thickness;
        set { thickness = Mathf.Max(0f, value); Apply(); }
    }

    public Color BorderColor
    {
        get => color;
        set { color = value; Apply(); }
    }

    private void OnEnable()
    {
        EnsureBars();
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void EnsureBars()
    {
        _top    = FindOrCreateBar("Top");
        _bottom = FindOrCreateBar("Bottom");
        _left   = FindOrCreateBar("Left");
        _right  = FindOrCreateBar("Right");
    }

    private Image FindOrCreateBar(string barName)
    {
        Transform existing = transform.Find(barName);
        if (existing) return existing.GetComponent<Image>();

        GameObject go = new GameObject(barName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);

        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private void Apply()
    {
        if (!_top || !_bottom || !_left || !_right) return;

        SetBar(_top.rectTransform,    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness), Vector2.zero);
        SetBar(_bottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, thickness));
        SetBar(_left.rectTransform,   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, thickness), new Vector2(thickness, -thickness));
        SetBar(_right.rectTransform,  new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thickness, thickness), new Vector2(0f, -thickness));

        _top.color    = color;
        _bottom.color = color;
        _left.color   = color;
        _right.color  = color;
    }

    private static void SetBar(RectTransform bar, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        bar.anchorMin = anchorMin;
        bar.anchorMax = anchorMax;
        bar.offsetMin = offsetMin;
        bar.offsetMax = offsetMax;
    }
}
