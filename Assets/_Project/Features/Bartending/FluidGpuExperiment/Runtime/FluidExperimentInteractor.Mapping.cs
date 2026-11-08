using System;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentInteractor
    {
        // 샌드박스는 기본 카메라 좌표를 유지한다. 게임만 Canvas 뷰포트 매핑을 주입한다.
        public Func<Vector2, Vector2> ScreenToWorldOverride;
        public Func<Vector3, Vector2> WorldToScreenOverride;
        public Func<Vector2, bool> IsScreenPointValid;
        public Func<Vector2, bool> ConsumePrimaryClick;
        public Func<GameObject, bool> IgnoreUiHit;
        public bool InputSuspended;
        public Vector2 LastWorldPointer => lastPointer;
        private Vector2 ScreenToWorld(Vector2 point) => ScreenToWorldOverride != null
            ? ScreenToWorldOverride(point) : (Vector2)inputCamera.ScreenToWorldPoint(point);
        private Vector2 WorldToScreen(Vector3 point) => WorldToScreenOverride != null
            ? WorldToScreenOverride(point) : (Vector2)inputCamera.WorldToScreenPoint(point);

        public void RebaseAfterViewTransition()
        {
            samples.Clear();
            sampledUserAngle = 0;
            rebasePointerOnResume = true;
        }
    }
}
