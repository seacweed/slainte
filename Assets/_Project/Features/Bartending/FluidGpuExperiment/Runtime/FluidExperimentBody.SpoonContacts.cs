using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentBody
    {
        public bool SpoonContactEnabled { get; private set; }
        public bool SuppressHeldSolidContacts => IsHeld;
        public bool SpoonWaitingForClearance { get; private set; }
        public FluidExperimentBody SpoonLiquidVessel => SpoonContactEnabled && IsHeld && spoonVessel != null
            && spoonVessel.isActiveAndEnabled && spoonVessel.World == World && spoonVessel.Body.simulated ? spoonVessel : null;
        private FluidExperimentBody spoonVessel;
        private Vector2 spoonFramePosition;
        private bool spoonAcquiredVessel;
        private bool[] spoonOriginalTriggers;
        private bool spoonOriginalFullKinematicContacts;

        public void SetSpoonContactEnabled(bool enabled)
        {
            enabled &= kind == LabItemKind.Spoon && IsHeld && isActiveAndEnabled;
            if (enabled == SpoonContactEnabled) return;
            SpoonContactEnabled = enabled;
            if (!enabled) { spoonVessel = null; spoonAcquiredVessel = false; SpoonWaitingForClearance = false; }
            UpdateSpoonHoldCollision();
            World?.RefreshCollisionPairs();
        }

        private void UpdateSpoonHoldCollision()
        {
            if (kind != LabItemKind.Spoon && spoonOriginalTriggers == null) return;
            if (IsHeld)
            {
                if (spoonOriginalTriggers == null)
                {
                    spoonOriginalTriggers = new bool[solidColliders.Length];
                    for (int i = 0; i < solidColliders.Length; i++)
                        if (solidColliders[i] != null) spoonOriginalTriggers[i] = solidColliders[i].isTrigger;
                    spoonOriginalFullKinematicContacts = Body.useFullKinematicContacts;
                }
                for (int i = 0; i < solidColliders.Length; i++)
                    if (solidColliders[i] != null)
                        solidColliders[i].isTrigger = true;
                // Vessel walls constrain only the spoon. Never give a native impulse to props.
                Body.useFullKinematicContacts = spoonOriginalFullKinematicContacts;
            }
            else
            {
                SpoonContactEnabled = false;
                spoonVessel = null;
                SpoonWaitingForClearance = false;
                if (spoonOriginalTriggers == null) return;
                for (int i = 0; i < solidColliders.Length; i++)
                    if (solidColliders[i] != null) solidColliders[i].isTrigger = spoonOriginalTriggers[i];
                Body.useFullKinematicContacts = spoonOriginalFullKinematicContacts;
                spoonOriginalTriggers = null;
            }
        }

        internal Vector2 SpoonTipLocal
        {
            get
            {
                Vector2 tip = Vector2.zero; float low = float.PositiveInfinity;
                if (collisionProfile == null) return tip;
                float scale = transform.lossyScale.y;
                foreach (var hull in collisionProfile.solids) foreach (var point in hull.points)
                    if (point.y * scale < low) { low = point.y * scale; tip = point; }
                return tip;
            }
        }

        private Vector2 ConstrainSpoonPosition(Vector2 requested)
        {
            if (kind != LabItemKind.Spoon || !IsHeld || !SpoonContactEnabled || World == null) return requested;
            if (SpoonLiquidVessel == null) spoonVessel = null;
            if (spoonVessel == null) return requested;
            BuildIceFrameHull(spoonVessel);
            Vector2 frame = spoonVessel.IceFramePoint(requested);
            frame = ResolveIceWalls(spoonVessel, spoonFramePosition, frame, true);
            spoonFramePosition = frame;
            Vector2 corrected = spoonVessel.Position + Rotate(frame, spoonVessel.Angle);
            if (spoonVessel.BeyondIceMouth(frame, this)) spoonVessel = null;
            return corrected;
        }

        private void TryAdmitSpoonVessel()
        {
            SpoonWaitingForClearance = false;
            Vector2 probe = LocalToWorld(SpoonTipLocal);
            FluidExperimentBody candidate = null;
            World.UpdateVisualSorting();
            foreach (var vessel in World.Items)
                if (vessel != null && vessel != this && vessel.IsVessel && vessel.isActiveAndEnabled
                    && vessel.Body.simulated && vessel.ContainsLiquid(probe)
                    && (candidate == null || World.VisualSortKey(vessel) > World.VisualSortKey(candidate))) candidate = vessel;
            if (candidate == null) return;
            Physics2D.SyncTransforms();
            foreach (var own in solidColliders) foreach (var wall in candidate.solidColliders)
            {
                if (own == null || wall == null || !own.isActiveAndEnabled || !wall.isActiveAndEnabled) continue;
                var distance = own.Distance(wall);
                if (!distance.isValid || distance.distance < .001f)
                { SpoonWaitingForClearance = true; return; }
            }
            spoonVessel = candidate;
            spoonAcquiredVessel = true;
            spoonFramePosition = spoonVessel.IceFramePoint(Position);
        }

        private void CompleteSpoonMotion()
        {
            if (kind != LabItemKind.Spoon || !IsHeld || !SpoonContactEnabled) return;
            if (SpoonLiquidVessel == null)
            {
                spoonVessel = null;
                TryAdmitSpoonVessel();
                if (spoonVessel == null) return;
            }
            // A freely moving/rotating vessel can reach a stationary spoon between ticks.
            // Correct only this spoon after native physics, never the vessel or its velocity.
            targetPosition = ConstrainSpoonPosition(Position);
            ApplyHeldPose();
            // Native polygons include their contact skin. Refine the geometric sweep
            // against the actual colliders, translating only the spoon.
            for (int iteration = 0; spoonVessel != null && iteration < 6; iteration++)
            {
                Physics2D.SyncTransforms();
                float closest = .001f;
                Vector2 correction = Vector2.zero;
                foreach (var own in solidColliders) foreach (var wall in spoonVessel.solidColliders)
                {
                    if (own == null || wall == null || !own.isActiveAndEnabled || !wall.isActiveAndEnabled) continue;
                    var distance = own.Distance(wall);
                    if (!distance.isValid || distance.distance >= closest) continue;
                    closest = distance.distance;
                    correction = distance.normal * (distance.distance - .001f);
                }
                if (correction.sqrMagnitude < 1e-12f) break;
                Body.position += correction;
                targetPosition = Position;
                spoonFramePosition = spoonVessel.IceFramePoint(Position);
            }
            if (spoonAcquiredVessel)
            {
                PreviousPosition = Position; PreviousAngle = Angle;
                spoonAcquiredVessel = false;
            }
        }
    }
}
