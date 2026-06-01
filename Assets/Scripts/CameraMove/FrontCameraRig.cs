using UnityEngine;

public class FrontCameraRig : MonoBehaviour, ICameraInputHandler
{
    [Header("Move this (FrontWorld RectTransform)")]
    [SerializeField] RectTransform frontWorld;

    [Header("How much to shift to reveal")]
    [SerializeField] float drawerShiftY = 800f;

    [Header("Motion")]
    [SerializeField] float moveTime = 0.35f;
    [SerializeField] AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Drawer")]
    [SerializeField] RectTransform drawerArea;

    [Header("Focus")]
    [SerializeField] Canvas rootCanvas;

    public bool IsAnimating => _animating;

    bool  _drawerOpen = false;
    float _focusX     = 0f;

    Vector2        _fromPos, _toPos;
    float          _t;
    bool           _animating;
    System.Action  _onMoveComplete;

    void Awake()
    {
        if (frontWorld == null)  frontWorld  = (RectTransform)transform;
        if (rootCanvas == null)  rootCanvas  = GetComponentInParent<Canvas>();
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
            _onMoveComplete?.Invoke();
            _onMoveComplete = null;
        }
    }

    public void OnCameraInput(CameraDirection direction)
    {
        switch (direction)
        {
            case CameraDirection.DrawerOpen:
                if (!_drawerOpen) SetDrawer(true);
                break;
            case CameraDirection.DrawerClose:
                if (_drawerOpen) SetDrawer(false);
                break;
        }
    }

    public void PanToWorldCenterX(float worldX)
    {
        if (rootCanvas == null) return;
        float delta = (Screen.width * 0.5f - worldX) / rootCanvas.scaleFactor;
        _focusX = frontWorld.anchoredPosition.x + delta;
        BeginMove(new Vector2(_focusX, frontWorld.anchoredPosition.y));
    }

    public void ResetPan()
    {
        _focusX = 0f;
        BeginMove(Vector2.zero);
    }

    public void SetDrawer(bool open)
    {
        _drawerOpen = open;
        if (open)
        {
            if (drawerArea != null)
                drawerArea.anchoredPosition = new Vector2(-_focusX, drawerArea.anchoredPosition.y);
            BeginMove(new Vector2(_focusX, drawerShiftY));
        }
        else
        {
            BeginMove(new Vector2(_focusX, 0f), () =>
            {
                if (drawerArea != null)
                    drawerArea.anchoredPosition = new Vector2(0f, drawerArea.anchoredPosition.y);
            });
        }
    }

    private void BeginMove(Vector2 target, System.Action onComplete = null)
    {
        _fromPos        = frontWorld.anchoredPosition;
        _toPos          = target;
        _t              = 0f;
        _animating      = true;
        _onMoveComplete = onComplete;
    }
}
