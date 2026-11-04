using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;

namespace NarrativeFlow.Editor
{
    // 빈 공간 왼쪽/가운데 드래그로 화면 이동. 움직이지 않고 떼면(클릭) 선택을 해제한다.
    // WHY: 빈 공간 마우스 입력을 이 매니퓰레이터가 가로채므로 GraphView 기본 "빈 곳 클릭 = 선택 해제"가
    // 동작하지 않는다. 누를 때 바로 해제하면 화면을 끌어 옮길 때마다 선택이 풀리므로 뗄 때 판정한다.
    public class LeftClickPanDragger : MouseManipulator
    {
        private const float ClickTolerance = 4f;

        private Vector2 _startPosition;
        private Vector2 _pressPosition;
        private bool _isDragging;
        private bool _moved;

        public LeftClickPanDragger()
        {
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse, modifiers = EventModifiers.None });
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.MiddleMouse, modifiers = EventModifiers.None });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove, TrickleDown.TrickleDown);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove, TrickleDown.TrickleDown);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp, TrickleDown.TrickleDown);
        }

        private void OnMouseDown(MouseDownEvent e)
        {
            if (_isDragging) return;

            // 빈 공간(GraphView 자체 또는 모눈종이 배경)을 눌렀을 때만 시작
            if ((e.target is GraphView || e.target is GridBackground) && CanStartManipulation(e) && target is GraphView)
            {
                _startPosition = e.localMousePosition;
                _pressPosition = e.localMousePosition;
                _moved = false;
                _isDragging = true;
                target.CaptureMouse();
                e.StopPropagation();
            }
        }

        private void OnMouseMove(MouseMoveEvent e)
        {
            if (!_isDragging || !target.HasMouseCapture()) return;

            var graphView = target as GraphView;
            if (graphView != null)
            {
                if ((e.localMousePosition - _pressPosition).sqrMagnitude > ClickTolerance * ClickTolerance)
                    _moved = true;

                Vector2 diff = e.localMousePosition - _startPosition;
#pragma warning disable CS0618 // Type or member is obsolete
                Vector3 currentPos = graphView.viewTransform.position;
                Vector3 currentScale = graphView.viewTransform.scale;
#pragma warning restore CS0618 // Type or member is obsolete

                graphView.UpdateViewTransform(currentPos + (Vector3)diff, currentScale);
                _startPosition = e.localMousePosition;
                e.StopPropagation();
            }
        }

        private void OnMouseUp(MouseUpEvent e)
        {
            if (!_isDragging || !target.HasMouseCapture() || !CanStopManipulation(e)) return;

            _isDragging = false;
            target.ReleaseMouse();
            e.StopPropagation();

            if (!_moved && e.button == (int)MouseButton.LeftMouse && target is GraphView graphView)
            {
                graphView.ClearSelection();
                if (graphView is NarrativeGraphView narrative)
                    narrative.window?.OnNodeSelectionChanged(null);
            }
        }
    }
}
