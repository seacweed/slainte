using System;
using System.Collections.Generic;
using UnityEngine;
using Slainte.Shared.Display;

// 앞 화면(FrontWorld)을 확대·이동해 대화 중인 캐릭터에 시선을 모으는 카메라 리그.
// 상태는 "확대 여부 + 카메라 중심 X" 두 값뿐이다. 확대는 QHD 윗변을 고정한 채 아래쪽
// croppedBottom 만큼이 잘리는 배율로 하고, 중심 X는 보이는 영역이 FrontWorld(QHD) 밖으로
// 나가지 않는 범위로 제한한다. 목표가 바뀔 때마다 ease를 처음부터 다시 틀면 이동 중 속도가
// 0으로 끊기므로, 속도를 이어받는 SmoothDamp로 목표를 따라가게 한다.
public class FrontCameraRig : MonoBehaviour
{
    private const float FocusSettleEpsilon = 0.05f;
    private const float ZoomSettleEpsilon = 0.0005f;

    [Header("Move this (FrontWorld RectTransform)")]
    [SerializeField] RectTransform frontWorld;

    [Header("Focus")]
    [SerializeField] Canvas rootCanvas;
    [Tooltip("Canvas pixels cut off at the bottom while zoomed in. The top edge stays fixed.")]
    [SerializeField, Min(0f)] float croppedBottom = 140f;
    [Tooltip("SmoothDamp time for both zoom and horizontal focus.")]
    [SerializeField, Min(0.01f)] float smoothTime = 0.25f;

    public RectTransform FrontWorld => frontWorld != null ? frontWorld : (RectTransform)transform;
    public bool IsAnimating => _animating;
    public bool IsAtFullView => !_animating && !_zoomTarget && _zoom <= ZoomSettleEpsilon;
    public event Action MoveStarted;
    public event Action MoveUpdated;
    public event Action MoveCompleted;

    // _zoom: 0 = QHD 전체, 1 = 확대. 실제 배율은 이 값으로 1과 ZoomedScale 사이를 보간한다.
    float _zoom;
    float _zoomVelocity;
    bool  _zoomTarget;
    float _focusX;
    float _focusVelocity;
    float _focusTargetX;
    bool  _animating;
    Vector2 _basePosition;
    readonly List<Action> _fullViewCallbacks = new List<Action>();

    float ZoomedScale
    {
        get
        {
            float height = frontWorld != null ? frontWorld.rect.height : 0f;
            return height > croppedBottom + 1f ? height / (height - croppedBottom) : 1f;
        }
    }

    void Awake()
    {
        if (frontWorld == null) frontWorld = (RectTransform)transform;
        if (rootCanvas == null) rootCanvas = GetComponentInParent<Canvas>();
        _basePosition = frontWorld.anchoredPosition;
        Apply();
    }

    void Update()
    {
        if (!_animating) return;

        float zoomTarget = _zoomTarget ? 1f : 0f;
        float dt = Time.unscaledDeltaTime;
        _zoom   = Mathf.SmoothDamp(_zoom, zoomTarget, ref _zoomVelocity, smoothTime, Mathf.Infinity, dt);
        _focusX = Mathf.SmoothDamp(_focusX, _focusTargetX, ref _focusVelocity, smoothTime, Mathf.Infinity, dt);

        // SmoothDamp는 목표에 점근만 하므로 충분히 가까워지면 정확히 맞추고 이동을 끝낸다.
        bool settled = IsNear(zoomTarget, _focusTargetX);
        if (settled)
        {
            _zoom = zoomTarget;
            _focusX = _focusTargetX;
            _zoomVelocity = 0f;
            _focusVelocity = 0f;
        }

        Apply();
        MoveUpdated?.Invoke();

        if (settled)
        {
            _animating = false;
            MoveCompleted?.Invoke();
            FlushFullViewCallbacks();
        }
    }

    // FrontWorld 로컬 X(확대 전 좌표)가 화면 가운데에 오도록 확대한다. 범위 밖이면 가능한 만큼만 이동한다.
    public void FocusOn(float localX)
    {
        SetTarget(true, ClampFocusX(localX));
    }

    // 현재 포커스를 유지한다. 아직 확대 전이면 지금 중심 그대로 확대만 한다.
    public void KeepFocus()
    {
        SetTarget(true, _zoomTarget ? _focusTargetX : ClampFocusX(_focusX));
    }

    // QHD 전체 화면으로 되돌린다. 이미 전체 화면이면 콜백을 즉시 호출한다.
    public void ResetView(Action onComplete = null)
    {
        SetTarget(false, 0f);
        if (onComplete == null)
            return;

        if (IsAtFullView)
            onComplete();
        else
            _fullViewCallbacks.Add(onComplete);
    }

    // 목표 상태(이동이 끝난 뒤) 기준으로, 확대 전 로컬 X가 화면 가운데에서 몇 캔버스 픽셀 떨어져
    // 보일지 계산한다. 이동 도중에도 최종 위치를 알 수 있어 말풍선을 처음부터 제자리에 둘 수 있다.
    public float ProjectToScreenAtTarget(float localX)
    {
        float scale = _zoomTarget ? ZoomedScale : 1f;
        float focus = _zoomTarget ? _focusTargetX : 0f;
        return (localX - focus) * scale;
    }

    // 확대 배율에서 보이는 반폭(로컬 단위)만큼 FrontWorld 양끝에서 안쪽으로 들인 범위로 중심을 제한한다.
    public float ClampFocusX(float localX)
    {
        if (frontWorld == null)
            return localX;

        float viewHalfWidth = ResolveCanvasHalfWidth() / ZoomedScale;
        float left  = -frontWorld.rect.width * frontWorld.pivot.x + viewHalfWidth;
        float right =  frontWorld.rect.width * (1f - frontWorld.pivot.x) - viewHalfWidth;
        return left > right ? (left + right) * 0.5f : Mathf.Clamp(localX, left, right);
    }

    private void SetTarget(bool zoomed, float focusX)
    {
        _zoomTarget = zoomed;
        _focusTargetX = zoomed ? focusX : 0f;

        if (_animating || IsNear(zoomed ? 1f : 0f, _focusTargetX))
            return;

        _animating = true;
        MoveStarted?.Invoke();
    }

    private bool IsNear(float zoomTarget, float focusTarget)
    {
        return Mathf.Abs(_zoom - zoomTarget) <= ZoomSettleEpsilon
            && Mathf.Abs(_focusX - focusTarget) <= FocusSettleEpsilon;
    }

    // 배율 s, 중심 X f일 때 로컬 (f, top)이 화면 (가운데, top)에 오도록 위치를 잡는다.
    // 피벗 기준 로컬 좌표가 s배 되므로 x = -s·f, y = top·(1 - s)이면 윗변이 제자리에 남는다.
    private void Apply()
    {
        if (frontWorld == null)
            return;

        float scale = Mathf.Lerp(1f, ZoomedScale, _zoom);
        float top = frontWorld.rect.height * (1f - frontWorld.pivot.y);
        frontWorld.localScale = new Vector3(scale, scale, 1f);
        frontWorld.anchoredPosition = _basePosition + new Vector2(-scale * _focusX, top * (1f - scale));
    }

    private float ResolveCanvasHalfWidth()
    {
        // 포커스 중앙 정렬은 띠를 뺀 16:9 게임 영역 너비 기준이어야 한다(Expand 캔버스는 화면비에 따라 더 넓어짐).
        RectTransform canvasRect = rootCanvas != null ? GameAspect.FindGameAreaRect(frontWorld) : null;
        if (canvasRect != null)
            return canvasRect.rect.width * 0.5f;
        return frontWorld != null ? frontWorld.rect.width * 0.5f : 0f;
    }

    private void FlushFullViewCallbacks()
    {
        if (!IsAtFullView || _fullViewCallbacks.Count == 0)
            return;

        // 콜백 안에서 다시 ResetView를 부를 수 있으므로 복사본으로 호출한다.
        Action[] callbacks = _fullViewCallbacks.ToArray();
        _fullViewCallbacks.Clear();
        for (int i = 0; i < callbacks.Length; i++)
            callbacks[i]?.Invoke();
    }

    private void OnDisable()
    {
        if (!_animating)
            return;

        // 비활성화되면 Update가 돌지 않으므로 목표 상태로 즉시 맞추고 대기 중인 콜백을 처리한다.
        _zoom = _zoomTarget ? 1f : 0f;
        _focusX = _focusTargetX;
        _zoomVelocity = 0f;
        _focusVelocity = 0f;
        _animating = false;
        Apply();
        MoveCompleted?.Invoke();
        FlushFullViewCallbacks();
    }
}
