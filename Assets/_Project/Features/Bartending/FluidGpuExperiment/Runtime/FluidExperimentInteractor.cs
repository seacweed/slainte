using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed class FluidExperimentInteractor : MonoBehaviour
    {
        public FluidExperimentWorld world;
        public Camera inputCamera;
        [System.NonSerialized] public Rect pointerBlockRect;
        public float rotationSensitivity = 5;
        public float swapMaximumSpeed = 1.4f;
        public float velocityWindow = .1f;
        [Min(.01f)] public float uprightReturnDuration = .15f;
        public FluidExperimentBody Held { get; private set; }
        public bool Rotating { get; private set; }
        public bool Returning { get; private set; }
        public Vector2 ReturnGrabPoint { get; private set; }
        public Vector2 PickupOrigin { get; private set; }
        private Vector2 grabLocal;
        private Vector2 rotationAnchor;
        private Vector2 lastPointer;
        private float angle;
        private Vector2 returnPosition;
        private Vector2 returnPointerScreen;
        private float returnStartAngle;
        private float returnElapsed;
        private int pointerSyncFrames;
        private bool pointerSyncPending;
        private readonly List<MotionSample> samples = new List<MotionSample>(32);
        private struct MotionSample { public float time; public Vector2 position; public float angle; }

        private void Update()
        {
            if (world == null || inputCamera == null) return;
            Vector2 screen = Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition;
            Vector2 pointer = inputCamera.ScreenToWorldPoint(screen);
            lastPointer = pointer;
            if (Held != null)
            {
                if (Input.GetMouseButtonDown(1) && !pointerBlockRect.Contains(new Vector2(screen.x, Screen.height - screen.y))) BeginRotation();
                if (Rotating && Input.GetMouseButton(1)) RotateBy(Input.GetAxisRaw("Mouse Y") * rotationSensitivity);
                if (Rotating && Input.GetMouseButtonUp(1)) EndRotation(pointer);
                if (Returning) AdvanceUprightReturn(Time.unscaledDeltaTime);
                else if (pointerSyncPending) SynchronizeReturnPointer(pointer);
                else if (!Rotating) MoveHeld(pointer);
                if (Input.GetKeyDown(KeyCode.C) && Held.kind == LabItemKind.Shaker)
                    Held.SetSealed(!Held.sealedVessel);
                Sample(Time.unscaledTime);
            }
            if (Input.GetMouseButtonDown(0)
                && !pointerBlockRect.Contains(new Vector2(screen.x, Screen.height - screen.y))
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                if (Held == null) PickAt(pointer);
                else Drop(pointer);
            }
        }

        public bool PickAt(Vector2 point)
        {
            FluidExperimentBody candidate = null;
            float distance = float.MaxValue;
            foreach (FluidExperimentBody item in world.Items)
            {
                if (item == null || !item.Contains(point)) continue;
                float d = (item.Position - point).sqrMagnitude;
                if (d < distance) { candidate = item; distance = d; }
            }
            return Pick(candidate, point);
        }
        public bool Pick(FluidExperimentBody item, Vector2 point)
        {
            if (Held != null || item == null || item.World != world) return false;
            Held = item;
            PickupOrigin = item.Position;
            item.SetHeld(true);
            angle = 0;
            item.RestoreHeldPose(PickupOrigin, angle);
            grabLocal = item.WorldToLocal(point);
            lastPointer = point;
            samples.Clear();
            Sample(Time.unscaledTime);
            return true;
        }
        public void MoveHeld(Vector2 pointer)
        {
            if (Held == null || Rotating || Returning || pointerSyncPending) return;
            Vector2 offset = Held.PointAt(grabLocal, Vector2.zero, angle);
            Held.SetHeldPose(pointer - offset, angle);
        }
        public void BeginRotation()
        {
            if (Held == null || Rotating) return;
            if (Returning || pointerSyncPending)
            {
                Returning = pointerSyncPending = false;
                samples.Clear();
            }
            Held.ApplyHeldPose();
            angle = Held.HeldAngle;
            rotationAnchor = Held.LocalToWorld(Held.rotationPivotLocal);
            Rotating = true;
        }
        public void RotateBy(float deltaDegrees)
        {
            if (Held == null || !Rotating) return;
            angle += deltaDegrees; // Deliberately unbounded, including multiple complete turns.
            Vector2 offset = Held.PointAt(Held.rotationPivotLocal, Vector2.zero, angle);
            Held.SetHeldPose(rotationAnchor - offset, angle);
        }
        public void EndRotation(Vector2 pointer)
        {
            if (Held == null || !Rotating) return;
            Held.ApplyHeldPose();
            Rotating = false;
            returnPosition = Held.Position;
            returnStartAngle = Mathf.DeltaAngle(0, Held.HeldAngle);
            angle = returnStartAngle;
            Held.RestoreHeldPose(returnPosition, angle);
            ReturnGrabPoint = Held.LocalToWorld(grabLocal);
            returnElapsed = 0;
            Returning = true;
            pointerSyncPending = false;
            lastPointer = pointer;
            // Automatic restoration must never become a throw or spin impulse.
            samples.Clear();
        }

        public void AdvanceUprightReturn(float dt)
        {
            if (Held == null || !Returning) return;
            returnElapsed += Mathf.Max(0, dt);
            float t = Mathf.Clamp01(returnElapsed / Mathf.Max(.01f, uprightReturnDuration));
            angle = Mathf.Lerp(returnStartAngle, 0, Mathf.SmoothStep(0, 1, t));
            Held.RestoreHeldPose(returnPosition, angle);
            ReturnGrabPoint = Held.LocalToWorld(grabLocal);
            bool requested = FollowReturnCursor();
            if (t < 1) return;
            Returning = false;
            pointerSyncPending = requested;
            pointerSyncFrames = 8;
            if (!requested) FinishPointerSynchronization(lastPointer);
        }

        private bool FollowReturnCursor()
        {
            // Batch validation must not move the user's desktop cursor. A legacy viewport
            // must also never redirect this isolated camera's pointer mapping.
            if (Application.isBatchMode || !Application.isFocused || inputCamera == null
                || BartendingViewport.Active != null) return false;
            return BartendingPointerAnchor.TryWarpToWorld(inputCamera, ReturnGrabPoint, out returnPointerScreen);
        }

        private void SynchronizeReturnPointer(Vector2 pointer)
        {
            if (BartendingPointerAnchor.IsPointerAt(returnPointerScreen)
                || --pointerSyncFrames <= 0 || !FollowReturnCursor())
                FinishPointerSynchronization(pointer);
        }

        private void FinishPointerSynchronization(Vector2 pointer)
        {
            pointerSyncPending = false;
            grabLocal = Held.WorldToLocal(pointer);
            lastPointer = pointer;
            samples.Clear();
            Sample(Time.unscaledTime);
        }
        private void Sample(float time)
        {
            if (Held == null || Returning || pointerSyncPending) return;
            // Sample input targets, so release velocity does not depend on render/fixed-step phasing.
            samples.Add(new MotionSample { time = time, position = Held.TargetPosition, angle = angle });
            while (samples.Count > 2 && samples[1].time < time - velocityWindow) samples.RemoveAt(0);
        }
        public void EstimateRelease(out Vector2 velocity, out float angularVelocity)
        {
            velocity = Vector2.zero; angularVelocity = 0;
            if (Returning || pointerSyncPending) return;
            if (samples.Count < 2) return;
            MotionSample a = samples[0], b = samples[samples.Count - 1];
            float dt = b.time - a.time;
            if (dt <= .001f) return;
            velocity = (b.position - a.position) / dt;
            angularVelocity = (b.angle - a.angle) / dt;
        }
        public bool Drop(Vector2 pointer)
        {
            if (Held == null) return false;
            // Dropping the object keeps its throw pose; only ending RMB rotation restores upright.
            Held.ApplyHeldPose();
            if (Rotating || Returning || pointerSyncPending)
            {
                if (Returning || pointerSyncPending) samples.Clear();
                Rotating = false;
                Returning = pointerSyncPending = false;
                grabLocal = Held.WorldToLocal(pointer);
                lastPointer = pointer;
            }
            EstimateRelease(out Vector2 velocity, out float spin);
            if (velocity.magnitude <= swapMaximumSpeed && Mathf.Abs(spin) < 45)
            {
                FluidExperimentBody target = world.FindSwapTarget(Held, pointer);
                if (target != null)
                {
                    if (!world.TrySwap(Held, target, PickupOrigin)) return false;
                    FinishDrop(Vector2.zero, 0);
                    return true;
                }
            }
            if (!world.TryResolveRelease(Held)) return false;
            FinishDrop(velocity, spin);
            return true;
        }
        public void ReleaseWithVelocity(Vector2 velocity, float angularVelocity = 0)
        {
            if (Held != null) FinishDrop(velocity, angularVelocity);
        }
        private void FinishDrop(Vector2 velocity, float spin)
        {
            FluidExperimentBody item = Held;
            Held = null;
            Rotating = false;
            Returning = pointerSyncPending = false;
            samples.Clear();
            item.Release(velocity, spin);
        }
        internal void Forget(FluidExperimentBody item)
        {
            if (Held != item) return;
            Held = null; Rotating = Returning = pointerSyncPending = false; samples.Clear();
        }
        private void OnDisable()
        {
            if (Held != null) FinishDrop(Vector2.zero, 0);
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!focused && Rotating) EndRotation(lastPointer);
        }
    }
}
