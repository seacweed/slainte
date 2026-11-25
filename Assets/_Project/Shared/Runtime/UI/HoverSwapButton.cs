using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 호버하면 배경을 on 스프라이트로 바꾸고 내용물(visualRoot)을 살짝 밀어내는 버튼 연출.
// 클릭 자체는 같은 오브젝트의 Button이 처리하고, 이 컴포넌트는 호버 표시와 사용 가능 여부만 맡는다.
// 버튼 루트(히트 영역)는 고정하고 visualRoot만 움직여, 가장자리에서 포인터가 들락거리며 깜빡이지 않게 한다.
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public class HoverSwapButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Sprites")]
    [SerializeField] private Image  targetImage;
    [SerializeField] private Sprite offSprite;
    [SerializeField] private Sprite onSprite;

    [Header("Label Colors (optional)")]
    [Tooltip("Graphics recolored with the background, e.g. light text on the brown off sprite, dark text on the yellow on sprite.")]
    [SerializeField] private Graphic[] colorTargets = new Graphic[0];
    [SerializeField] private Color offColor = new Color32(255, 224, 150, 255);
    [SerializeField] private Color onColor  = new Color32(74, 36, 0, 255);

    [Header("Motion")]
    [Tooltip("Child that slides on hover. The button root stays put as the hit area.")]
    [SerializeField] private RectTransform visualRoot;
    [SerializeField] private Vector2 hoverOffset = new Vector2(12f, 0f);
    [SerializeField, Min(0.01f)] private float slideDuration = 0.08f;

    private Button     _button;
    private Vector2    _restPosition;
    private bool       _hovered;
    private bool       _initialized;
    private Coroutine  _slideRoutine;

    public Button Button
    {
        get { EnsureInitialized(); return _button; }
    }

    public bool Interactable
    {
        get { EnsureInitialized(); return _button != null && _button.interactable; }
    }

    void Awake() => EnsureInitialized();

    void OnDisable()
    {
        // 패널이 닫히는 순간 코루틴이 끊기므로, 다음에 열렸을 때 중간 위치·on 상태로 남지 않게 즉시 되돌린다.
        SetHovered(false, instant: true);
    }

    // 비활성 부모 아래에서 Instantiate되면 Awake가 늦어지므로 공개 메서드에서도 초기화를 보장한다.
    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        _button = GetComponent<Button>();
        // 상태별 색 틴트가 스프라이트 교체 위에 덮이지 않게 Selectable 전환을 끈다.
        if (_button) _button.transition = Selectable.Transition.None;
        if (visualRoot) _restPosition = visualRoot.anchoredPosition;
        ApplySprite(false);
    }

    // 스프라이트를 코드에서 지정해야 하는 경우(생성형 목록 등)에 쓴다.
    public void SetSprites(Sprite off, Sprite on)
    {
        EnsureInitialized();
        offSprite = off;
        onSprite  = on;
        ApplySprite(_hovered);
    }

    public void SetInteractable(bool interactable)
    {
        EnsureInitialized();
        if (_button) _button.interactable = interactable;
        if (!interactable) SetHovered(false, instant: true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!Interactable) return;
        SetHovered(true, instant: false);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_hovered) SetHovered(false, instant: false);
    }

    private void SetHovered(bool hovered, bool instant)
    {
        EnsureInitialized();
        _hovered = hovered;
        ApplySprite(hovered);
        if (!visualRoot) return;

        Vector2 target = _restPosition + (hovered ? hoverOffset : Vector2.zero);
        if (_slideRoutine != null)
        {
            StopCoroutine(_slideRoutine);
            _slideRoutine = null;
        }

        if (instant || !isActiveAndEnabled)
            visualRoot.anchoredPosition = target;
        else
            _slideRoutine = StartCoroutine(SlideTo(target));
    }

    private void ApplySprite(bool hovered)
    {
        if (colorTargets != null)
        {
            Color color = hovered ? onColor : offColor;
            foreach (Graphic graphic in colorTargets)
                if (graphic) graphic.color = color;
        }

        if (!targetImage) return;
        Sprite sprite = hovered && onSprite ? onSprite : offSprite;
        if (sprite) targetImage.sprite = sprite;
    }

    // 도중에 진입·이탈이 뒤집혀도 현재 위치에서 이어서 움직여 튀지 않게 한다.
    private IEnumerator SlideTo(Vector2 target)
    {
        Vector2 start = visualRoot.anchoredPosition;
        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / slideDuration), 2f);
            visualRoot.anchoredPosition = Vector2.LerpUnclamped(start, target, t);
            yield return null;
        }
        visualRoot.anchoredPosition = target;
        _slideRoutine = null;
    }
}
