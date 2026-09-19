using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Slainte.Bartending.PhysicsLab
{
    public sealed class PhysicsLabInteractor : MonoBehaviour
    {
        public PhysicsLabWorld world;
        public Camera inputCamera;
        public float rotationSensitivity = 5;
        public float swapMaximumSpeed = 1.4f;
        public float velocityWindow = .1f;
        public PhysicsLabBody Held { get; private set; }
        public bool Rotating { get; private set; }
        public Vector2 PickupOrigin { get; private set; }
        private Vector2 grabLocal;
        private Vector2 rotationAnchor;
        private Vector2 lastPointer;
        private float angle;
        private readonly List<MotionSample> samples = new List<MotionSample>(32);
        private struct MotionSample { public float time; public Vector2 position; public float angle; }

        private void Update()
        {
            if (world == null || inputCamera == null) return;
            Vector2 pointer = inputCamera.ScreenToWorldPoint(Input.mousePosition);
            lastPointer = pointer;
            if (Held != null)
            {
                if (Input.GetMouseButtonDown(1)) BeginRotation();
                if (Rotating && Input.GetMouseButton(1)) RotateBy(Input.GetAxisRaw("Mouse Y") * rotationSensitivity);
                if (Rotating && Input.GetMouseButtonUp(1)) EndRotation(pointer);
                if (!Rotating) MoveHeld(pointer);
                if (Input.GetKeyDown(KeyCode.C) && Held.kind == LabItemKind.Shaker)
                    Held.SetSealed(!Held.sealedVessel);
                Sample(Time.unscaledTime);
            }
            if (Input.GetMouseButtonDown(0)
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                if (Held == null) PickAt(pointer);
                else Drop(pointer);
            }
        }

        public bool PickAt(Vector2 point)
        {
            PhysicsLabBody candidate = null;
            float distance = float.MaxValue;
            foreach (PhysicsLabBody item in world.Items)
            {
                if (item == null || !item.Contains(point)) continue;
                float d = (item.Position - point).sqrMagnitude;
                if (d < distance) { candidate = item; distance = d; }
            }
            return Pick(candidate, point);
        }
        public bool Pick(PhysicsLabBody item, Vector2 point)
        {
            if (Held != null || item == null || item.World != world) return false;
            Held = item;
            PickupOrigin = item.Position;
            grabLocal = item.WorldToLocal(point);
            angle = item.Angle;
            item.SetHeld(true);
            samples.Clear();
            Sample(Time.unscaledTime);
            return true;
        }
        public void MoveHeld(Vector2 pointer)
        {
            if (Held == null || Rotating) return;
            Vector2 offset = Held.PointAt(grabLocal, Vector2.zero, angle);
            Held.SetHeldPose(pointer - offset, angle);
        }
        public void BeginRotation()
        {
            if (Held == null || Rotating) return;
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
            grabLocal = Held.WorldToLocal(pointer); // Preserve pose; no warp, snap, or upright return.
        }
        private void Sample(float time)
        {
            if (Held == null) return;
            // Sample input targets, so release velocity does not depend on render/fixed-step phasing.
            samples.Add(new MotionSample { time = time, position = Held.TargetPosition, angle = angle });
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
            if (Held == null) return false;
            if (Rotating) EndRotation(pointer);
            Held.ApplyHeldPose();
            EstimateRelease(out Vector2 velocity, out float spin);
            if (velocity.magnitude <= swapMaximumSpeed && Mathf.Abs(spin) < 45)
            {
                PhysicsLabBody target = world.FindSwapTarget(Held, pointer);
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
            PhysicsLabBody item = Held;
            Held = null;
            Rotating = false;
            samples.Clear();
            item.Release(velocity, spin);
        }
        internal void Forget(PhysicsLabBody item)
        {
            if (Held != item) return;
            Held = null; Rotating = false; samples.Clear();
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
