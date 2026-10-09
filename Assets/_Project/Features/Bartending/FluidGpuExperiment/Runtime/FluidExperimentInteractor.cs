using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentInteractor : MonoBehaviour
    {
        public FluidExperimentWorld world;
        public Camera inputCamera;
        [System.NonSerialized] public Rect pointerBlockRect;
        public float rotationSensitivity = 5;
        public float velocityWindow = .1f;
        [Min(.01f)] public float uprightReturnDuration = .15f;
        public FluidExperimentBody Held { get; private set; }
        public FluidExperimentShakerPart HeldPart { get; private set; }
        public bool Rotating { get; private set; }
        public bool Returning { get; private set; }
        public Vector2 ReturnGrabPoint { get; private set; }
        public Vector2 PickupOrigin { get; private set; }
        private Vector2 grabLocal;
        private Vector2 rotationAnchor;
        private Vector2 lastPointer;
        private float angle;
        private Vector2 returnPosition;
        private Vector2 returnStartPointer;
        private float returnStartAngle;
        private float returnElapsed;
        private float sampledUserAngle;
        public Vector2 RotationPointerWorld => Held != null
            ? Held.PointAt(Held.rotationPivotLocal, Held.TargetPosition, Held.TargetAngle) : rotationAnchor;
        private readonly List<MotionSample> samples = new List<MotionSample>(32);
        private struct MotionSample { public float time; public Vector2 position; public float angle; }

        private void Update()
        {
            if (world == null || inputCamera == null) return;
            if (!Application.isBatchMode && !Application.isFocused) { CancelPointerCapture(); return; }
            Mouse mouse = Mouse.current;
            if (mouse == null) { CancelPointerCapture(); return; }
            bool freshInput = ReadPointerInput(mouse, out Vector2 screen, out Vector2 delta);
            if (freshInput && !mouse.rightButton.isPressed) requirePointerRelease = false;
            Vector2 pointer = inputCamera.ScreenToWorldPoint(screen);
            lastPointer = pointer;
            bool cancelled = PointerCaptureLost(mouse);
            if (cancelled)
            {
                CancelPointerCapture();
                screen = LogicalPointerScreen;
                pointer = inputCamera.ScreenToWorldPoint(screen);
            }
            if (Held != null)
            {
                bool began = false;
                if (!cancelled && freshInput && mouse.rightButton.wasPressedThisFrame
                    && !IsPointerBlocked(screen) && CanCapturePointer())
                {
                    BeginRotation();
                    CapturePointer(mouse);
                    began = true;
                }
                if (Rotating)
                {
                    // Ignore only the entry snapshot: it can contain movement from before RMB.
                    // Relative motion is consumed once; neither cursor position nor a warp is rotation input.
                    if (freshInput && !began && !cancelled && PointerCaptured)
                        RotateBy(delta.y * .1f * rotationSensitivity);
                    pointer = RotationPointerWorld;
                    screen = inputCamera.WorldToScreenPoint(pointer);
                    LogicalPointerScreen = screen;
                    if (freshInput && !mouse.rightButton.isPressed)
                        EndRotation(pointer);
                }
                if (Returning) AdvanceUprightReturn(Time.unscaledDeltaTime, pointer);
                else if (!Rotating) MoveHeld(pointer);
                if (freshInput && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame
                    && Held.kind == LabItemKind.Shaker)
                    Held.CycleShakerClosure();
                Sample(Time.unscaledTime);
            }
            else if (HeldPart != null)
            {
                MoveHeld(pointer);
                Sample(Time.unscaledTime);
            }
            Vector2 clickScreen = PointerCaptured ? DisplayedPointerScreen : screen;
            if (!cancelled && freshInput && mouse.leftButton.wasPressedThisFrame && !IsPointerBlocked(clickScreen))
            {
                pointer = inputCamera.ScreenToWorldPoint(clickScreen);
                if (Held == null && HeldPart == null) PickAt(pointer);
                else Drop(pointer);
            }
        }

        public bool PickAt(Vector2 point)
        {
            if (Held != null || HeldPart != null) return false;
            // Part hit areas exclude the transparent margins of the source sprites.
            // Try the cap before the overlapping strainer and the shaker body.
            if (PickShakerPartAt(point, FluidExperimentShakerPartRole.Cap)
                || PickShakerPartAt(point, FluidExperimentShakerPartRole.Strainer)) return true;
            FluidExperimentBody candidate = null;
            float distance = float.MaxValue;
            foreach (FluidExperimentBody item in world.Items)
            {
                if (item == null || !item.CanBePicked || !item.Contains(point)) continue;
                float d = (item.Position - point).sqrMagnitude;
                bool garnish = item.kind == LabItemKind.Garnish;
                bool selectedGarnish = candidate != null && candidate.kind == LabItemKind.Garnish;
                if ((garnish && !selectedGarnish) || (garnish == selectedGarnish && d < distance))
                { candidate = item; distance = d; }
            }
            // Only fresh loose pieces are eligible. A used piece over a supply must
            // not block dispensing a new one; sources never become movable bodies.
            if (candidate != null && candidate.kind == LabItemKind.Garnish) return Pick(candidate, point);
            foreach (var source in world.GetComponentsInChildren<FluidExperimentGarnishSource>())
                if (source.Contains(point)) return source.TryDispense(this, point);
            return Pick(candidate, point);
        }

        private bool PickShakerPartAt(Vector2 point, FluidExperimentShakerPartRole role)
        {
            foreach (FluidExperimentBody item in world.Items)
            {
                if (item == null || item.kind != LabItemKind.Shaker || item.IsHeld) continue;
                FluidExperimentShakerPart part = role == FluidExperimentShakerPartRole.Cap
                    ? item.ShakerCap : item.ShakerStrainer;
                if (part != null && part.ContainsWorldPoint(point) && PickPart(part, point)) return true;
            }
            return false;
        }
        public bool PickPart(FluidExperimentShakerPart part, Vector2 point)
        {
            if (Held != null || HeldPart != null || part == null || part.Owner == null
                || part.Owner.World != world || !part.TryPickUp(point)) return false;
            HeldPart = part;
            lastPointer = point;
            sampledUserAngle = 0;
            samples.Clear();
            Sample(Time.unscaledTime);
            return true;
        }
        public bool Pick(FluidExperimentBody item, Vector2 point)
        {
            if (Held != null || HeldPart != null || item == null || item.World != world || !item.CanBePicked) return false;
            Held = item;
            PickupOrigin = item.Position;
            item.SetHeld(true);
            angle = 0;
            sampledUserAngle = 0;
            item.RestorePickupPose(PickupOrigin, angle);
            grabLocal = item.WorldToLocal(point);
            lastPointer = point;
            samples.Clear();
            Sample(Time.unscaledTime);
            return true;
        }
        public void MoveHeld(Vector2 pointer)
        {
            lastPointer = pointer;
            if (HeldPart != null) { HeldPart.MoveToPointer(pointer); return; }
            if (Held == null || Rotating) return;
            if (Returning)
            {
                Held.SetHeldPose(returnPosition + pointer - returnStartPointer, angle);
                return;
            }
            Vector2 offset = Held.PointAt(grabLocal, Vector2.zero, angle);
            Held.SetHeldPose(pointer - offset, angle);
        }
        public void BeginRotation()
        {
            if (Held == null || Rotating) return;
            if (Returning)
            {
                Returning = false;
                samples.Clear();
                sampledUserAngle = 0;
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
            sampledUserAngle += deltaDegrees;
        }
        public void EndRotation(Vector2 pointer)
        {
            if (Held == null || !Rotating) return;
            Held.ApplyHeldPose();
            Rotating = false;
            BeginPointerHandoff(pointer);
            returnPosition = Held.Position;
            returnStartPointer = pointer;
            returnStartAngle = Mathf.DeltaAngle(0, Held.HeldAngle);
            angle = returnStartAngle;
            Held.RestoreHeldPose(returnPosition, angle);
            ReturnGrabPoint = Held.LocalToWorld(grabLocal);
            returnElapsed = 0;
            Returning = true;
            lastPointer = pointer;
            // Only mouse translation is sampled during restoration. The angle driven
            // by this animation must never become a release spin.
            sampledUserAngle = 0;
            samples.Clear();
            Sample(Time.unscaledTime);
        }

        public void AdvanceUprightReturn(float dt) => AdvanceUprightReturn(dt, lastPointer);

        public void AdvanceUprightReturn(float dt, Vector2 pointer)
        {
            if (Held == null || !Returning) return;
            lastPointer = pointer;
            returnElapsed += Mathf.Max(0, dt);
            float t = Mathf.Clamp01(returnElapsed / Mathf.Max(.01f, uprightReturnDuration));
            angle = Mathf.Lerp(returnStartAngle, 0, Mathf.SmoothStep(0, 1, t));
            Held.RestoreHeldPose(returnPosition + pointer - returnStartPointer, angle);
            ReturnGrabPoint = Held.LocalToWorld(grabLocal);
            if (t < 1) return;
            Returning = false;
            // Rebase the normal drag at this exact pose, with no cursor warp or
            // extra blocked frames when the return animation finishes.
            grabLocal = Held.WorldToLocal(pointer);
        }
        private void Sample(float time)
        {
            if (Held == null && HeldPart == null) return;
            // Sample input targets, so release velocity does not depend on render/fixed-step phasing.
            samples.Add(new MotionSample { time = time,
                position = Held != null ? Held.TargetPosition : HeldPart.Position,
                angle = sampledUserAngle });
            while (samples.Count > 2 && samples[1].time < time - velocityWindow) samples.RemoveAt(0);
        }
        public void EstimateRelease(out Vector2 velocity, out float angularVelocity)
        {
            velocity = Vector2.zero; angularVelocity = 0;
            if (samples.Count < 2) return;
            MotionSample a = samples[0], b = samples[samples.Count - 1];
            float dt = b.time - a.time;
            if (dt <= .001f) return;
            velocity = (b.position - a.position) / dt;
            angularVelocity = (b.angle - a.angle) / dt;
        }
        public bool Drop(Vector2 pointer)
        {
            if (HeldPart != null)
            {
                FluidExperimentShakerPart part = HeldPart;
                EstimateRelease(out Vector2 partVelocity, out _);
                HeldPart = null;
                samples.Clear();
                if (!part.TryAttach()) part.Release(partVelocity);
                return true;
            }
            if (Held == null) return false;
            // Dropping the object keeps its throw pose; only ending RMB rotation restores upright.
            Held.ApplyHeldPose();
            Physics2D.SyncTransforms();
            EstimateRelease(out Vector2 velocity, out float spin);
            FluidExperimentBody target = Held.kind == LabItemKind.Garnish ? null : world.FindSwapTarget(Held, pointer);
            // Every drop retains the sampled motion. A target only adds the next pickup.
            if (!world.TryResolveRelease(Held, target)) return false;
            FinishDrop(velocity, spin);
            return target == null || Pick(target, pointer);
        }
        public void ReleaseWithVelocity(Vector2 velocity, float angularVelocity = 0)
        {
            ReleasePointer();
            if (Held != null) FinishDrop(velocity, angularVelocity);
            if (HeldPart != null)
            {
                FluidExperimentShakerPart part = HeldPart;
                HeldPart = null;
                samples.Clear();
                part.Release(velocity);
            }
        }
        private void FinishDrop(Vector2 velocity, float spin)
        {
            FluidExperimentBody item = Held;
            if (PointerCaptured) BeginPointerHandoff(RotationPointerWorld);
            Held = null;
            Rotating = false;
            Returning = false;
            samples.Clear();
            item.Release(velocity, spin);
        }
        internal void Forget(FluidExperimentBody item)
        {
            if (HeldPart != null && HeldPart.Owner == item) ForgetShakerPart(HeldPart);
            if (Held != item) return;
            ReleasePointer();
            Held = null; Rotating = Returning = false; samples.Clear();
        }
        internal void ForgetShakerPart(FluidExperimentShakerPart part)
        {
            if (HeldPart != part) return;
            HeldPart = null;
            samples.Clear();
        }
        private void OnDisable()
        {
            ReleasePointer();
            ReleaseWithVelocity(Vector2.zero);
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) CancelPointerCapture();
        }
    }
}
