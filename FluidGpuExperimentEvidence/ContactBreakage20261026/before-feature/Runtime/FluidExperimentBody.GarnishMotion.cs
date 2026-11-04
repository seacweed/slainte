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
        private readonly List<ContactPoint2D> floatingSolidContacts = new List<ContactPoint2D>(16);
        private bool contentsInterpolationOverride;
        private RigidbodyInterpolation2D previousContentsInterpolation;

        private void UpdateHeldContentsInterpolation()
        {
            bool completedPose = World?.Liquid != null && World.Liquid.CohesivePhysicsActive
                && ((IsVessel && IsHeld) || (UsesLiquidSolidMotion && !IsHeld && IsInHeldVessel));
            if (completedPose && !contentsInterpolationOverride)
            {
                previousContentsInterpolation = Body.interpolation;
                contentsInterpolationOverride = true;
                // A cursor-driven vessel and its recovered contents must display the
                // same completed tick. Interpolating the old solid pose can briefly
                // draw it beyond a wall which has already moved and been resolved.
                Body.interpolation = RigidbodyInterpolation2D.None;
            }
            else if (!completedPose) RestoreContentsInterpolation();
        }

        private void RestoreContentsInterpolation()
        {
            if (!contentsInterpolationOverride || Body == null) return;
            Body.interpolation = previousContentsInterpolation;
            contentsInterpolationOverride = false;
        }

        private void SynchronizeCompletedContentsDisplay()
        {
            if (!contentsInterpolationOverride || Body == null || !Body.simulated) return;
            // A post-physics wall recovery changes Rigidbody.position after Unity's
            // normal transform synchronization. Publish that completed pose before
            // rendering instead of displaying the pre-recovery transform for a frame.
            Vector3 displayed = transform.position;
            transform.SetPositionAndRotation(new Vector3(Body.position.x, Body.position.y, displayed.z),
                Quaternion.Euler(0, 0, Body.rotation));
        }

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
            UpdateHeldContentsInterpolation();
            if (!UsesLiquidSolidMotion || !Body.simulated || dt <= 0) return;
            UpdateGarnishHoldCollision();
            if (IsHeld) { UpdateGarnishSorting(); return; }
            // Also migrate an existing piece after scripts reload during Play mode.
            if (Body.bodyType != RigidbodyType2D.Dynamic) Body.bodyType = RigidbodyType2D.Dynamic;
            // Native physics owns pose, gravity, inertia and all ordinary contacts.
            // Only large wall crossings are recovered when a cursor-driven vessel jumps.
            if (UpdateIceContainment()) World.RefreshCollisionPairs();
            UpdateHeldContentsInterpolation();
            World.Liquid.TrySampleGarnishFlow(this, out var velocity, out float submerged,
                out float flowSpin, out float floatAcceleration);
            var settings = World.Liquid.settings;
            bool ice = kind == LabItemKind.Ice;
            float relativeDensity = Mathf.Max(.1f, ice ? settings.iceRelativeDensity : settings.garnishRelativeDensity);
            float flowDrag = Mathf.Max(0, ice ? settings.iceFlowDrag : settings.garnishFlowDrag);
            float linearDamping = ice ? .05f : .15f;
            float angularDamping = (ice ? .1f : .15f) + .25f * submerged;
            if (!Mathf.Approximately(Body.linearDamping, linearDamping)) Body.linearDamping = linearDamping;
            if (!Mathf.Approximately(Body.angularDamping, angularDamping)) Body.angularDamping = angularDamping;
            Vector2 buoyancy = -Physics2D.gravity * (Body.gravityScale * submerged / relativeDensity);
            Vector2 slip = velocity - Body.linearVelocity;
            if (ice)
            {
                // Water resistance grows with speed. A constant two-gravity cap
                // lets a dropped cube reach the floor before slowing, after which
                // it spends many seconds climbing back through the water.
                float speedScale = Mathf.Sqrt(Mathf.Max(.01f, Physics2D.gravity.magnitude * SolidBounds.size.y));
                // Deep-water linear damping must not make a buoyant cube crawl
                // upwards for tens of seconds. Inertial drag catches fast entry;
                // extra free-surface damping settles the final buoyant bob.
                float exposed = Mathf.Clamp01((1-submerged)/Mathf.Max(.02f,1-relativeDensity));
                float speedRatio = slip.magnitude/speedScale;
                flowDrag *= .25f + 1.25f*exposed + speedRatio*speedRatio;
            }
            float drag = (1 - Mathf.Exp(-flowDrag * submerged * dt)) / dt;
            // Do not synchronize velocities. Bounded fluid acceleration leaves inertia
            // and relative slip intact, including at pickup/release state transitions.
            Vector2 resistance = slip * drag;
            if (!ice) resistance = Vector2.ClampMagnitude(resistance, Mathf.Max(1, Physics2D.gravity.magnitude * 2));
            Vector2 force = Body.mass * (buoyancy + resistance);
            if (force.sqrMagnitude > 1e-14f) Body.AddForce(force);
            float angularDrag = ice ? settings.iceAngularFlowDrag : settings.solidAngularFlowDrag;
            float spinDrag = (1 - Mathf.Exp(-Mathf.Max(0, angularDrag) * submerged * dt)) / dt;
            // Ice torque comes from the centroid of its actually submerged
            // polygon; the peel retains its separate two-probe approximation.
            float torque = Body.inertia * Mathf.Deg2Rad
                * (floatAcceleration + (flowSpin - Body.angularVelocity) * spinDrag);
            if (Mathf.Abs(torque) > 1e-9f) Body.AddTorque(torque);
            UpdateGarnishSorting();
        }

        internal void ResolveCompletedGarnishContacts()
        {
            if (!UsesLiquidSolidMotion || !Body.simulated) return;
            // Repair only native wall penetration, including the polygon contact
            // skin not represented by the authored interior. Loose-solid pairs
            // remain exclusively native; no second solver pushes the stack apart.
            if (!IsHeld && (kind != LabItemKind.Ice || !HasNativeLooseSolidContact())) RecoverDeepGarnishContact();
            UpdateHeldContentsInterpolation();
            UpdateGarnishSorting();
        }

        private bool HasNativeLooseSolidContact()
        {
            Body.GetContacts(floatingSolidContacts);
            foreach (var contact in floatingSolidContacts)
            {
                var a = contact.collider != null ? contact.collider.GetComponentInParent<FluidExperimentBody>() : null;
                var b = contact.otherCollider != null ? contact.otherCollider.GetComponentInParent<FluidExperimentBody>() : null;
                if ((a != null && a != this && a.IsLooseSolid) || (b != null && b != this && b.IsLooseSolid)) return true;
            }
            return false;
        }

        private void RecoverDeepGarnishContact()
        {
            Vector3 scale = transform.lossyScale;
            float pixel = collisionProfile != null ? collisionProfile.sourcePixelSize : .005f;
            float limit = Mathf.Max(.003f, pixel * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            // Loose-solid stacking is already solved by native polygon contacts.
            // A second, sequential depenetration pass between cubes fights that
            // solver and pumps a crowded stack even without any liquid present.
            GatherGarnishObstacles(false);
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
                    Vector2 recovery = d.normal * (d.distance - .0005f);
                    Body.position += recovery;
                    // This correction is not moving-ice momentum. Keep it out of
                    // GPU boundary response velocity, as with swept wall recovery.
                    IceRecoveryTranslation += recovery;
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
            if (kind != LabItemKind.Garnish) return;
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

        private void GatherGarnishObstacles(bool includeLooseSolids = true)
        {
            garnishObstacles.Clear();
            foreach (var other in World.Items)
            {
                if (other == null || other == this || !other.isActiveAndEnabled || !other.Body.simulated) continue;
                if (!includeLooseSolids && other.IsLooseSolid) continue;
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
