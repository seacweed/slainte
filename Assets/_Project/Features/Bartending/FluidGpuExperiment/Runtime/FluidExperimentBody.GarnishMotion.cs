using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentBody
    {
        public bool UsesLiquidGarnishMotion => kind == LabItemKind.Garnish && World != null
            && World.Liquid != null && World.Liquid.useCohesivePhysics;
        public Vector2 GarnishVelocity => Body.linearVelocity;
        public float GarnishAngularVelocity => Body.angularVelocity;
        private SpriteRenderer garnishRenderer;
        private int garnishOriginalSortingOrder;
        private readonly List<Collider2D> garnishObstacles = new List<Collider2D>(64);

        private void UpdateGarnishHoldCollision()
        {
            if (!UsesLiquidGarnishMotion) return;
            // Keep geometry available for picking and safe release, while removing
            // every physical contact during pickup (including caps and world walls).
            foreach (var collider in solidColliders)
                if (collider != null && collider.isTrigger != IsHeld) collider.isTrigger = IsHeld;
        }

        internal void GarnishFloatProbes(out Vector2 arm, out float thickness)
        {
            BuildIceFrameHull(this);
            Vector2 span = Vector2.right * .2f;
            float lengthSquared = 0;
            foreach (var a in iceLocalHull)
            {
                foreach (var b in iceLocalHull)
                    if ((b - a).sqrMagnitude > lengthSquared) { span = b - a; lengthSquared = span.sqrMagnitude; }
            }
            Vector2 axis = span.normalized, normal = new Vector2(-axis.y, axis.x);
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var point in iceLocalHull)
            { float d = Vector2.Dot(point, normal); min = Mathf.Min(min, d); max = Mathf.Max(max, d); }
            arm = Rotate(span * .3f, Angle);
            thickness = Mathf.Max(.04f, max - min);
        }

        internal void StepGarnishMotion(float dt)
        {
            if (!UsesLiquidGarnishMotion || !Body.simulated || dt <= 0) return;
            UpdateGarnishHoldCollision();
            if (IsHeld) { UpdateGarnishSorting(); return; }
            // Also migrate an existing piece after scripts reload during Play mode.
            if (Body.bodyType != RigidbodyType2D.Dynamic) Body.bodyType = RigidbodyType2D.Dynamic;
            // Native physics owns pose, gravity, inertia and all ordinary contacts.
            // Only large wall crossings are recovered when a cursor-driven vessel jumps.
            if (UpdateIceContainment()) World.RefreshCollisionPairs();
            World.Liquid.TrySampleGarnishFlow(this, out var velocity, out float submerged,
                out float flowSpin, out float floatAcceleration);
            Body.linearDamping = .35f;
            Body.angularDamping = .25f + .75f * submerged;
            Vector2 buoyancy = -Physics2D.gravity * (Body.gravityScale * 1.15f * submerged);
            float drag = (1 - Mathf.Exp(-8 * submerged * dt)) / dt;
            Body.AddForce(Body.mass * (buoyancy + (velocity - Body.linearVelocity) * drag));
            float spinDrag = (1 - Mathf.Exp(-2 * submerged * dt)) / dt;
            Body.AddTorque(Body.inertia * Mathf.Deg2Rad
                * (floatAcceleration + (flowSpin - Body.angularVelocity) * spinDrag));
            UpdateGarnishSorting();
        }

        internal void ResolveCompletedGarnishContacts()
        {
            if (!UsesLiquidGarnishMotion || !Body.simulated) return;
            if (!IsHeld) RecoverDeepGarnishContact();
            UpdateGarnishSorting();
        }

        private void RecoverDeepGarnishContact()
        {
            Vector3 scale = transform.lossyScale;
            float pixel = collisionProfile != null ? collisionProfile.sourcePixelSize : .005f;
            float limit = Mathf.Max(.003f, pixel * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            GatherGarnishObstacles();
            for (int iteration = 0; iteration < 4; iteration++)
            {
                bool corrected = false;
                foreach (var obstacle in garnishObstacles)
                foreach (var collider in solidColliders)
                {
                    if (Physics2D.GetIgnoreCollision(collider, obstacle)) continue;
                    var d = collider.Distance(obstacle);
                    if (!d.isValid || d.distance >= -limit) continue;
                    // Native contacts retain rotation and velocity. Only a visibly deep
                    // overlap is moved out, never a normal resting/contact-offset gap.
                    Body.position += d.normal * (d.distance - .0005f);
                    corrected = true;
                }
                if (!corrected) break;
            }
        }

        internal bool TryResolveGarnishRelease()
        {
            Vector2 origin = Position; float angle = Angle;
            float reach = SolidBounds.size.magnitude + .02f;
            GatherGarnishObstacles();
            bool Clear(Vector2 position)
            {
                Body.position = position;
                foreach (var obstacle in garnishObstacles)
                foreach (var collider in solidColliders)
                {
                    var d = collider.Distance(obstacle);
                    if (d.isValid && d.distance < .002f) return false;
                }
                return true;
            }
            if (Clear(origin)) return true;
            // A deeply intersected compound wall can alternate its nearest normal.
            // Search outwards from the clicked pose once, while contacts are still off.
            for (int ring = 1; ring <= 24; ring++)
            for (int direction = 0; direction < 16; direction++)
            {
                Vector2 candidate = origin + Rotate(Vector2.right * (reach * ring / 24), direction * 22.5f);
                if (!Clear(candidate)) continue;
                Teleport(candidate, angle);
                return true;
            }
            Body.position = origin;
            return false;
        }

        private void UpdateGarnishSorting()
        {
            if (garnishRenderer == null)
            {
                garnishRenderer = GetComponentInChildren<SpriteRenderer>();
                if (garnishRenderer == null) return;
                garnishOriginalSortingOrder = garnishRenderer.sortingOrder;
            }
            bool inside = false;
            foreach (var vessel in World.Items)
                if (vessel != null && vessel.IsVessel && vessel.Body.simulated && vessel.ContainsLiquid(Position))
                { inside = true; break; }
            // Liquid: 0, contained peel: 2, glass fronts: 4. Keep a freshly picked
            // peel visible over its supply jar while it is outside a receiver.
            garnishRenderer.sortingOrder = inside ? 2 : garnishOriginalSortingOrder;
        }

        private void GatherGarnishObstacles()
        {
            garnishObstacles.Clear();
            foreach (var other in World.Items)
            {
                if (other == null || other == this || !other.isActiveAndEnabled || !other.Body.simulated) continue;
                foreach (var collider in other.solidColliders)
                    if (collider != null && collider.isActiveAndEnabled && !collider.isTrigger) garnishObstacles.Add(collider);
                if (other.capCollider != null && other.capCollider.isActiveAndEnabled && other.collisionProfile != null)
                    garnishObstacles.Add(other.capCollider);
            }
            foreach (var hull in World.EnvironmentHulls)
                if (hull.Collider != null && hull.Collider.isActiveAndEnabled) garnishObstacles.Add(hull.Collider);
        }
    }
}
