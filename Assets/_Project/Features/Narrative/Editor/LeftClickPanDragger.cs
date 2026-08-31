using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;

namespace NarrativeFlow.Editor
{
    public class LeftClickPanDragger : MouseManipulator
    {
        private Vector2 _startPosition;
        private bool _isDragging;

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

            // 빈 공간(GraphView 자체 또는 모눈종이 배경)을 클릭했을 때만 화면 이동 시작
            if (e.target is GraphView || e.target is GridBackground)
            {
                if (CanStartManipulation(e))
                {
                    var graphView = target as GraphView;
                    if (graphView != null)
                    {
                        graphView.ClearSelection();
                        _startPosition = e.localMousePosition;
                        _isDragging = true;
                        target.CaptureMouse();
                        e.StopPropagation();
                    }
                }
            }
        }

        private void OnMouseMove(MouseMoveEvent e)
        {
            if (!_isDragging || !target.HasMouseCapture()) return;

            var graphView = target as GraphView;
            if (graphView != null)
            {
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
        }
    }
}