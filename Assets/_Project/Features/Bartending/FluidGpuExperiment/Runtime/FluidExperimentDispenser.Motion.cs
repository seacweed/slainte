using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentDispenser
    {
        private readonly RaycastHit2D[] motionHits = new RaycastHit2D[64];
        private readonly Collider2D[] overlapHits = new Collider2D[64];
        private Vector2 waypoint, lastMotionPosition;
        private bool hasWaypoint;
        private float retractSpeed = 20, stalledTime;
        private LineRenderer hose;
        private Vector2[] hosePoints, hosePrevious;

        /// <summary>Held poses are applied by input. Retraction clears any initial overlap,
        /// then uses native contacts and routes around the first blocking solid.</summary>
        public void StepMotion(float dt)
        {
            if (!IsAvailable || dt <= 0 || State != FluidExperimentDispenserState.Retracting) return;
            if (CollisionSuppressed)
            {
                ApplyDirectPose(Vector2.MoveTowards(body.Position, dockPosition, retractSpeed * dt));
                Physics2D.SyncTransforms();
                if (!CanRestoreReturnContacts()) return;
                ConfigureMotion(false);
                lastMotionPosition = body.Position;
            }
            Vector2 position = body.Position;
            if (State == FluidExperimentDispenserState.Retracting)
            {
                if ((position - dockPosition).sqrMagnitude < .0016f && !BlockedAt(dockPosition))
                {
                    State = FluidExperimentDispenserState.Docked;
                    ApplyDirectPose(dockPosition); ConfigureMotion(false);
                    BlockedReason = null; return;
                }
                stalledTime = (position - lastMotionPosition).sqrMagnitude < .000025f ? stalledTime + dt : 0;
                if (!SweepBlocked(position, dockPosition, out _)) { target = dockPosition; hasWaypoint = false; }
                else
                {
                    if (hasWaypoint && ((position - waypoint).sqrMagnitude < .04f || stalledTime > .3f
                        || SweepBlocked(position, waypoint, out _))) hasWaypoint = false;
                    if (!hasWaypoint && SweepBlocked(position, dockPosition, out Collider2D obstacle))
                    {
                        hasWaypoint = FindReturnWaypoint(position, obstacle, out waypoint);
                        if (!hasWaypoint) BlockedReason = "Return path blocked";
                        else { BlockedReason = null; stalledTime = 0; }
                    }
                    target = hasWaypoint ? waypoint : position;
                }
            }
            lastMotionPosition = position;
            Vector2 error = target - position;
            Vector2 desired = Vector2.ClampMagnitude(error / Mathf.Max(dt, .055f), retractSpeed);
            body.Body.linearVelocity = Vector2.MoveTowards(body.Body.linearVelocity, desired, 220 * dt);
            body.Body.angularVelocity = 0;
        }

        private Vector2 MotionSize => (Vector2)body.SolidBounds.size + Vector2.one * .025f;
        private Vector2 BoundsOffset => (Vector2)body.SolidBounds.center - body.Position;
        private ContactFilter2D SolidFilter => new ContactFilter2D { useTriggers = false };
        private bool CanRestoreReturnContacts()
        {
            var world = body.World;
            if (world != null)
            {
                // A tool wholly outside a wall or below the table may have no current
                // overlap, but restoring contacts there would trap its return outside.
                if ((world.ConstrainHeldPosition(body, body.Position, 0, .04f) - body.Position).sqrMagnitude > .000001f)
                    return false;
                Bounds bounds = body.SolidBounds;
                foreach (var floor in world.floorColliders)
                {
                    if (floor == null || !floor.isActiveAndEnabled || floor.isTrigger) continue;
                    // Runtime environment bindings also put walls and the ceiling in this list.
                    if (floor == world.leftBoundary || floor == world.rightBoundary || floor == world.topBoundary) continue;
                    Bounds surface = floor.bounds;
                    if (bounds.max.x >= surface.min.x && bounds.min.x <= surface.max.x
                        && bounds.min.y < surface.max.y + .025f) return false;
                }
            }
            return !BlockedAt(body.Position);
        }
        private bool IsObstacle(Collider2D collider)
        {
            if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) return false;
            foreach (Collider2D own in body.solidColliders)
                if (collider == own || (own != null && Physics2D.GetIgnoreCollision(own, collider))) return false;
            return true;
        }
        private bool BlockedAt(Vector2 position)
        {
            int count = Physics2D.OverlapBox(position + BoundsOffset, MotionSize, 0, SolidFilter, overlapHits);
            for (int i = 0; i < count; i++) if (IsObstacle(overlapHits[i])) return true;
            return count == overlapHits.Length;
        }
        private bool SweepBlocked(Vector2 from, Vector2 to, out Collider2D blocker)
        {
            blocker = null;
            Vector2 delta = to - from;
            if (delta.sqrMagnitude < .000001f) return false;
            int count = Physics2D.BoxCast(from + BoundsOffset, MotionSize, 0, delta.normalized, SolidFilter,
                motionHits, delta.magnitude);
            float closest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = motionHits[i];
                if (!IsObstacle(hit.collider) || hit.distance >= closest) continue;
                // Starting flush with a collider is harmless when the requested path goes away from it.
                if (hit.distance < .001f && Vector2.Dot(delta, from - (Vector2)hit.collider.bounds.center) > 0) continue;
                closest = hit.distance; blocker = hit.collider;
            }
            return blocker != null;
        }
        private bool FindReturnWaypoint(Vector2 from, Collider2D obstacle, out Vector2 selected)
        {
            selected = from;
            if (obstacle == null) return false;
            Bounds b = obstacle.bounds;
            Vector2 half = MotionSize * .5f + Vector2.one * .12f;
            Vector2 low = (Vector2)b.min - half - BoundsOffset;
            Vector2 high = (Vector2)b.max + half - BoundsOffset;
            float best = float.MaxValue;
            // Inflated bounding-box corners give a small deterministic visibility graph.
            // Upper corners win ties, matching the overhead hose's natural retract direction.
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = new Vector2((i & 1) == 0 ? low.x : high.x, i < 2 ? high.y : low.y);
                Vector2 constrained = body.World.ConstrainHeldPosition(body, p, 0);
                if ((constrained - p).sqrMagnitude > .001f || BlockedAt(p) || SweepBlocked(from, p, out _)) continue;
                float score = Vector2.Distance(from, p) + Vector2.Distance(p, dockPosition);
                if (SweepBlocked(p, dockPosition, out _)) score += 10;
                if (i >= 2) score += .1f;
                if (score >= best) continue;
                best = score; selected = p;
            }
            return best < float.MaxValue;
        }

        private void InitializeHose()
        {
            var go = new GameObject("DispenserHose"); go.transform.SetParent(transform, false);
            // This child is created after the body's registration has assigned its layer.
            // The comparison camera renders only that layer, not Unity's default layer.
            go.layer = gameObject.layer;
            hose = go.AddComponent<LineRenderer>(); hose.useWorldSpace = true;
            hose.startWidth = hose.endWidth = .065f; hose.numCornerVertices = 3; hose.numCapVertices = 3;
            hose.sortingOrder = -3;
            if (hoseMaterial != null) hose.sharedMaterial = hoseMaterial;
            else if (bodyRenderer != null) hose.sharedMaterial = bodyRenderer.sharedMaterial;
            hose.startColor = hose.endColor = new Color(.18f, .2f, .21f, 1);
            ResetHose();
        }
        private void ResetHose()
        {
            if (hose == null) return;
            int count = Mathf.Clamp(hoseSegments, 4, 32) + 1;
            if (hosePoints == null || hosePoints.Length != count)
            { hosePoints = new Vector2[count]; hosePrevious = new Vector2[count]; }
            hose.positionCount = count;
            Vector2 end = transform.TransformPoint(hoseAttachmentLocal);
            for (int i = 0; i < count; i++)
            { hosePoints[i] = hosePrevious[i] = Vector2.Lerp(hoseAnchor, end, i / (float)(count - 1)); hose.SetPosition(i, hosePoints[i]); }
        }
        private void LateUpdate()
        {
            if (hose == null || !IsAvailable) return;
            int last = hosePoints.Length - 1;
            Vector2 end = transform.TransformPoint(hoseAttachmentLocal);
            float dt = Mathf.Min(Time.deltaTime, .033f);
            for (int i = 1; i < last; i++)
            {
                Vector2 p = hosePoints[i];
                hosePoints[i] += (p - hosePrevious[i]) * .94f + Vector2.down * (8 * dt * dt);
                hosePrevious[i] = p;
            }
            float length = (Vector2.Distance(hoseAnchor, end) * 1.035f + .08f) / last;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                hosePoints[0] = hoseAnchor; hosePoints[last] = end;
                for (int i = 0; i < last; i++)
                {
                    Vector2 delta = hosePoints[i + 1] - hosePoints[i];
                    float distance = delta.magnitude;
                    if (distance < .00001f) continue;
                    Vector2 correction = delta * ((distance - length) / distance);
                    if (i == 0) hosePoints[i + 1] -= correction;
                    else if (i + 1 == last) hosePoints[i] += correction;
                    else { hosePoints[i] += correction * .5f; hosePoints[i + 1] -= correction * .5f; }
                }
            }
            hosePoints[0] = hoseAnchor; hosePoints[last] = end;
            for (int i = 0; i <= last; i++) hose.SetPosition(i, hosePoints[i]);
        }
    }
}
