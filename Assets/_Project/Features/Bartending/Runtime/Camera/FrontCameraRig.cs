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

    [Header("Horizontal-Fixed Panels")]
    [Tooltip("Front-world children that must stay horizontally centered on screen while SetHorizontalFixed(true) is active (e.g. crafting slot row, ingredient selection).")]
    [SerializeField] RectTransform[] horizontalFixedPanels;

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

    bool    _horizontalFixed;
    float[] _fixedBaseX;

    void Awake()
    {
        if (frontWorld == null)  frontWorld  = (RectTransform)transform;
        if (rootCanvas == null)  rootCanvas  = GetComponentInParent<Canvas>();
        frontWorld.anchoredPosition = Vector2.zero;

        BuildVerticalFollowGroup();
        CacheHorizontalFixedBase();
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

    private void CacheHorizontalFixedBase()
    {
        if (horizontalFixedPanels == null || horizontalFixedPanels.Length == 0) return;

        _fixedBaseX = new float[horizontalFixedPanels.Length];
        for (int i = 0; i < horizontalFixedPanels.Length; i++)
        {
            RectTransform panel = horizontalFixedPanels[i];
            _fixedBaseX[i] = panel != null ? panel.anchoredPosition.x : 0f;
        }
    }

    // 서랍(SetDrawer)과 같은 방식으로, 지정된 패널을 팬 반대 방향으로 한 번에 옮겨 화면 가로
    // 위치를 고정한다. 보간하지 않는 이유: 패널이 가려져 있는 동안 자리만 바꿔 놓아야
    // 제작 공간이 열릴 때 재료가 좌우로 미끄러지며 등장하지 않기 때문이다.
    private void SyncHorizontalFixed()
    {
        if (_fixedBaseX == null) return;

        float compensation = _horizontalFixed ? -_focusX : 0f;
        for (int i = 0; i < horizontalFixedPanels.Length; i++)
        {
            RectTransform panel = horizontalFixedPanels[i];
            if (panel == null) continue;

            Vector2 p = panel.anchoredPosition;
            p.x = _fixedBaseX[i] + compensation;
            panel.anchoredPosition = p;
        }
    }

    // 제조처럼 조작 영역이 화면 중앙에 있어야 하는 구간에서 켠다. 모드 전환(GameModeManager)
    // 시점에 호출되므로, 바텐딩 세션이 만들어지기 전(진입)·해체되기 전(이탈) 같은 프레임에
    // 자리가 잡혀 슬롯 위의 잔·병이 튀지 않는다.
    public void SetHorizontalFixed(bool fixedOnScreen)
    {
        if (_horizontalFixed == fixedOnScreen) return;

        _horizontalFixed = fixedOnScreen;
        SyncHorizontalFixed();
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
        // 서랍과 마찬가지로 새 포커스 기준으로 자리를 다시 잡아, 고정 패널이 팬을 따라가지 않게 한다.
        SyncHorizontalFixed();
        BeginMove(new Vector2(_focusX, frontWorld.anchoredPosition.y));
    }

    public void ResetPan()
    {
        _focusX = 0f;
        SyncHorizontalFixed();
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
