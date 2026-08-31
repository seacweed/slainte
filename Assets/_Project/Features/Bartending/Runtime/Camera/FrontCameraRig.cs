using Slainte.Shared.Input;
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

    [Header("Vertical-Follow Panels")]
    [Tooltip("Panels that should follow the drawer's vertical shift but stay fixed on screen during horizontal camera pans (e.g. order ticket, recipe book, liquor shelf).")]
    [SerializeField] RectTransform[] verticalFollowPanels;

    public bool IsAnimating => _animating;
    public event System.Action MoveStarted;
    public event System.Action MoveUpdated;
    public event System.Action MoveCompleted;

    bool  _drawerOpen = false;
    float _focusX     = 0f;

    Vector2        _fromPos, _toPos;
    float          _t;
    bool           _animating;
    System.Action  _onMoveComplete;

    RectTransform _verticalFollowGroup;

    void Awake()
    {
        if (frontWorld == null)  frontWorld  = (RectTransform)transform;
        if (rootCanvas == null)  rootCanvas  = GetComponentInParent<Canvas>();
        frontWorld.anchoredPosition = Vector2.zero;

        BuildVerticalFollowGroup();
    }

    void Update()
    {
        if (!_animating) return;

        _t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, moveTime);
        float k = ease.Evaluate(Mathf.Clamp01(_t));
        frontWorld.anchoredPosition = Vector2.LerpUnclamped(_fromPos, _toPos, k);
        SyncVerticalFollow();
        MoveUpdated?.Invoke();

        if (_t >= 1f)
        {
            frontWorld.anchoredPosition = _toPos;
            SyncVerticalFollow();
            _animating = false;
            _onMoveComplete?.Invoke();
            _onMoveComplete = null;
            MoveCompleted?.Invoke();
        }
    }

    private void BuildVerticalFollowGroup()
    {
        if (verticalFollowPanels == null || verticalFollowPanels.Length == 0 || rootCanvas == null) return;

        var groupGO = new GameObject("VerticalCameraFollowGroup", typeof(RectTransform));
        _verticalFollowGroup = groupGO.GetComponent<RectTransform>();
        _verticalFollowGroup.SetParent(rootCanvas.transform, false);
        _verticalFollowGroup.SetSiblingIndex(verticalFollowPanels[0].GetSiblingIndex());
        _verticalFollowGroup.anchorMin = Vector2.zero;
        _verticalFollowGroup.anchorMax = Vector2.one;
        _verticalFollowGroup.offsetMin = Vector2.zero;
        _verticalFollowGroup.offsetMax = Vector2.zero;

        foreach (var panel in verticalFollowPanels)
        {
            if (panel != null) panel.SetParent(_verticalFollowGroup, false);
        }
    }

    private void SyncVerticalFollow()
    {
        if (_verticalFollowGroup == null) return;
        Vector2 p = _verticalFollowGroup.anchoredPosition;
        p.y = frontWorld.anchoredPosition.y;
        _verticalFollowGroup.anchoredPosition = p;
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
        bool wasAnimating = _animating;
        _fromPos        = frontWorld.anchoredPosition;
        _toPos          = target;
        _t              = 0f;
        _animating      = true;
        _onMoveComplete = onComplete;
        if (!wasAnimating)
            MoveStarted?.Invoke();
    }

    private void OnDisable()
    {
        if (!_animating)
            return;

        _animating = false;
        _onMoveComplete = null;
        MoveCompleted?.Invoke();
    }
}
