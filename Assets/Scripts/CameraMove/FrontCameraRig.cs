using UnityEngine;

public class FrontCameraRig : MonoBehaviour, ICameraInputHandler
{
    [Header("Move this (FrontWorld RectTransform)")]
    [SerializeField] RectTransform frontWorld;

    [Header("How much to shift to reveal")]
    [SerializeField] float drawerShiftY = 800f;
    [SerializeField] float shelfShiftX  = 2560f;

    [Header("Motion")]
    [SerializeField] float moveTime = 0.35f;
    [SerializeField] AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    public bool IsAnimating => _animating;

    bool _drawerOpen = false;
    bool _shelfOpen  = false;

    Vector2 _fromPos, _toPos;
    float   _t;
    bool    _animating;

    void Awake()
    {
        if (frontWorld == null) frontWorld = (RectTransform)transform;
        frontWorld.anchoredPosition = Vector2.zero;
    }

    void Update()
    {
        if (!_animating) return;

        _t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, moveTime);
        float k = ease.Evaluate(Mathf.Clamp01(_t));
        frontWorld.anchoredPosition = Vector2.LerpUnclamped(_fromPos, _toPos, k);

        if (_t >= 1f)
        {
            frontWorld.anchoredPosition = _toPos;
            _animating = false;
        }
    }

    public void OnCameraInput(CameraDirection direction)
    {
        switch (direction)
        {
            case CameraDirection.DrawerOpen:
                if (!_drawerOpen && !_shelfOpen) SetDrawer(true);
                break;
            case CameraDirection.DrawerClose:
                if (_drawerOpen) SetDrawer(false);
                break;
            case CameraDirection.ShelfOpen:
                if (!_shelfOpen && !_drawerOpen) SetShelf(true);
                break;
            case CameraDirection.ShelfClose:
                if (_shelfOpen) SetShelf(false);
                break;
        }
    }

    public void SetDrawer(bool open)
    {
        _drawerOpen = open;
        BeginMove(open ? new Vector2(0f, drawerShiftY) : Vector2.zero);
    }

    public void SetShelf(bool open)
    {
        _shelfOpen = open;
        BeginMove(open ? new Vector2(-shelfShiftX, 0f) : Vector2.zero);
    }

    private void BeginMove(Vector2 target)
    {
        _fromPos   = frontWorld.anchoredPosition;
        _toPos     = target;
        _t         = 0f;
        _animating = true;
    }
}
