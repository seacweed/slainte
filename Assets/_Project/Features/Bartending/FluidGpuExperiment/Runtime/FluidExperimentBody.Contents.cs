using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentBody
    {
        public uint ContainingVesselId => iceContainer != null ? iceContainer.Id : 0;
        public bool IsInHeldVessel => iceContainer != null && iceContainer.IsHeld;
        private FluidExperimentBody iceContainer;
        private Vector2 previousIcePosition, previousIceFramePosition;
        private bool hasIcePosition;
        private float iceCollisionRadius;

        internal void ClearIceContainer()
        {
            iceContainer = null;
            hasIcePosition = false;
        }

        // Physics continues in world space. Only the cursor-driven change of reference frame is
        // carried here, so an arbitrarily fast drag cannot leave contents behind a teleported wall.
        internal void TransportContainedIce(Vector2 from, float fromAngle, Vector2 to, float toAngle)
        {
            if (World == null || !IsVessel) return;
            float turn = toAngle - fromAngle;
            if ((to - from).sqrMagnitude < 1e-12f && Mathf.Abs(turn) < .00001f) return;
            foreach (FluidExperimentBody ice in World.Items)
            {
                if (ice == null || ice.iceContainer != this || ice.IsHeld || !ice.Body.simulated) continue;
                ice.Body.position = to + Rotate(ice.Position - from, turn);
                ice.Body.rotation += turn;
                // Preserve free velocity and spin: gravity and relative motion can still spill ice.
                ice.Body.WakeUp();
            }
        }

        private void ReleaseContainedIceVelocity(Vector2 velocity, float angularVelocity)
        {
            if (World == null || !IsVessel) return;
            float omega = angularVelocity * Mathf.Deg2Rad;
            foreach (FluidExperimentBody ice in World.Items)
            {
                if (ice == null || ice.iceContainer != this || ice.IsHeld) continue;
                Vector2 offset = ice.Position - Position;
                ice.Body.linearVelocity += velocity + new Vector2(-offset.y, offset.x) * omega;
                ice.Body.angularVelocity += angularVelocity;
            }
        }

        internal bool UpdateIceContainment()
        {
            if (kind != LabItemKind.Ice) return false;
            FluidExperimentBody old = iceContainer;
            if (IsHeld || World == null || !Body.simulated)
            {
                ClearIceContainer();
                return old != null;
            }
            float radius = IceRadius();
            if (iceContainer != null && (!iceContainer.isActiveAndEnabled || iceContainer.World != World
                || !iceContainer.IsVessel)) iceContainer = null;
            if (iceContainer != null)
            {
                Vector2 current = iceContainer.IceFramePoint(Position);
                Vector2 start = previousIceFramePosition;
                // A swept disk catches fast wall crossings; the open mouth is deliberately absent.
                current = ResolveIceWalls(iceContainer, start, current, radius);
                Vector2 corrected = iceContainer.Position + Rotate(current, iceContainer.Angle);
                if ((corrected - Position).sqrMagnitude > 1e-12f) Body.position = corrected;
                previousIceFramePosition = current;
                if (!iceContainer.BlocksIceMouth && iceContainer.BeyondIceMouth(current, radius))
                    iceContainer = null;
            }
            if (iceContainer == null)
            {
                foreach (FluidExperimentBody vessel in World.Items)
                {
                    if (vessel == null || !vessel.IsVessel || vessel == this || vessel.BlocksIceMouth
                        || !vessel.ContainsLiquid(Position)) continue;
                    // A held glass may receive ice through its opening, but sweeping its side
                    // through an unrelated cube must not silently turn that cube into contents.
                    if (vessel.IsHeld && (!hasIcePosition || !EnteredIceMouth(vessel, previousIcePosition, Position))) continue;
                    iceContainer = vessel;
                    previousIceFramePosition = vessel.IceFramePoint(Position);
                    break;
                }
            }
            previousIcePosition = Position;
            hasIcePosition = true;
            return old != iceContainer;
        }

        private float IceRadius()
        {
            if (iceCollisionRadius > 0) return iceCollisionRadius;
            float radius = .01f;
            if (collisionProfile != null)
            {
                foreach (FluidExperimentHull hull in collisionProfile.solids)
                    foreach (Vector2 point in hull.points)
                        radius = Mathf.Max(radius, PointAt(point, Vector2.zero, 0).magnitude);
            }
            else
            {
                foreach (Collider2D collider in solidColliders)
                {
                    if (collider is BoxCollider2D box)
                        radius = Mathf.Max(radius, PointAt(box.offset, Vector2.zero, 0).magnitude
                            + PointAt(box.size * .5f, Vector2.zero, 0).magnitude);
                    else if (collider is CircleCollider2D circle)
                        radius = Mathf.Max(radius, PointAt(circle.offset, Vector2.zero, 0).magnitude
                            + circle.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)));
                    else if (collider != null) radius = Mathf.Max(radius, collider.bounds.extents.magnitude);
                }
            }
            iceCollisionRadius = radius;
            return radius;
        }

        private Vector2 IceFramePoint(Vector2 worldPoint) => Rotate(worldPoint - Position, -Angle);
        private Vector2 IceFrameVertex(Vector2 local) => PointAt(local, Vector2.zero, 0);
        private Vector2[] IceInterior => collisionProfile != null ? collisionProfile.interior : liquidWall;

        private bool IceMouth(out Vector2 left, out Vector2 right, out Vector2 outward)
        {
            Vector2[] path = IceInterior;
            left = right = outward = Vector2.zero;
            if (path.Length < 3) return false;
            left = IceFrameVertex(path[0]); right = IceFrameVertex(path[path.Length - 1]);
            Vector2 edge = right - left;
            if (edge.sqrMagnitude < 1e-10f) return false;
            outward = new Vector2(-edge.y, edge.x).normalized;
            Vector2 interior = IceFrameVertex(path[path.Length / 2]);
            if (Vector2.Dot(interior - left, outward) > 0) outward = -outward;
            return true;
        }

        private bool BeyondIceMouth(Vector2 point, float radius)
        {
            if (!IceMouth(out Vector2 left, out _, out Vector2 outward)) return false;
            return Vector2.Dot(point - left, outward) > radius + .002f;
        }

        private static bool EnteredIceMouth(FluidExperimentBody vessel, Vector2 before, Vector2 after)
        {
            if (!vessel.IceMouth(out Vector2 a, out Vector2 b, out Vector2 outward)) return false;
            Vector2 p = Rotate(before - vessel.PreviousPosition, -vessel.PreviousAngle);
            Vector2 q = vessel.IceFramePoint(after);
            float from = Vector2.Dot(p - a, outward), to = Vector2.Dot(q - a, outward);
            if (from < 0 || to > 0 || from - to < 1e-7f) return false;
            Vector2 crossing = Vector2.Lerp(p, q, from / (from - to));
            float along = Vector2.Dot(crossing - a, b - a) / (b - a).sqrMagnitude;
            return along >= 0 && along <= 1;
        }

        private Vector2 ResolveIceWalls(FluidExperimentBody vessel, Vector2 start, Vector2 end, float radius)
        {
            Vector2[] path = vessel.IceInterior;
            if (path.Length < 3) return end;
            float winding = 0;
            for (int i = 0; i < path.Length; i++)
            {
                Vector2 a = vessel.IceFrameVertex(path[i]), b = vessel.IceFrameVertex(path[(i + 1) % path.Length]);
                winding += a.x * b.y - b.x * a.y;
            }
            int edges = path.Length - (vessel.BlocksIceMouth ? 0 : 1);
            Vector2 position = start, remaining = end - start;
            for (int iteration = 0; iteration < 4 && remaining.sqrMagnitude > 1e-12f; iteration++)
            {
                float first = 1, separation = .0005f;
                Vector2 normal = Vector2.zero;
                for (int i = 0; i < edges; i++)
                {
                    Vector2 a = vessel.IceFrameVertex(path[i]), b = vessel.IceFrameVertex(path[(i + 1) % path.Length]);
                    Vector2 edge = b - a;
                    float length = edge.magnitude;
                    if (length < 1e-6f) continue;
                    Vector2 tangent = edge / length;
                    Vector2 inward = new Vector2(-tangent.y, tangent.x) * (winding >= 0 ? 1 : -1);
                    float distance = Vector2.Dot(position - a, inward);
                    float travel = Vector2.Dot(remaining, inward);
                    if (travel < -1e-8f)
                    {
                        float t = (radius - distance) / travel;
                        float along = Vector2.Dot(position + remaining * t - a, tangent);
                        if (t >= 0 && t < first && along >= 0 && along <= length)
                        { first = t; normal = inward; separation = .0005f; }
                        // A cube may be acquired while resting closer than the conservative disk
                        // radius. Resolve that shallow overlap before allowing more inward motion.
                        float startAlong = Vector2.Dot(position - a, tangent);
                        if (first > 0 && distance < radius && distance > -radius
                            && startAlong >= 0 && startAlong <= length)
                        { first = 0; normal = inward; separation = radius - distance + .0005f; }
                    }
                    SweepIceCorner(position, remaining, a, radius, ref first, ref normal);
                    SweepIceCorner(position, remaining, b, radius, ref first, ref normal);
                }
                position += remaining * first;
                if (normal == Vector2.zero) return position;
                position += normal * separation;
                remaining *= 1 - first;
                remaining -= normal * Mathf.Min(0, Vector2.Dot(remaining, normal));
                Vector2 worldNormal = Rotate(normal, vessel.Angle);
                Body.linearVelocity -= worldNormal * Mathf.Min(0, Vector2.Dot(Body.linearVelocity, worldNormal));
            }
            // Remaining displacement after four contacts is discarded, never advanced through a wall.
            return position;
        }

        private static void SweepIceCorner(Vector2 start, Vector2 travel, Vector2 corner, float radius,
            ref float first, ref Vector2 normal)
        {
            Vector2 offset = start - corner;
            float a = travel.sqrMagnitude, b = Vector2.Dot(offset, travel), c = offset.sqrMagnitude - radius * radius;
            if (a < 1e-12f || c <= 0 || b >= 0) return;
            float discriminant = b * b - a * c;
            if (discriminant < 0) return;
            float t = (-b - Mathf.Sqrt(discriminant)) / a;
            if (t < 0 || t >= first) return;
            first = t;
            normal = (offset + travel * t).normalized;
        }
    }

    public sealed partial class FluidExperimentWorld
    {
        public void RefreshIceContainment()
        {
            bool changed = false;
            foreach (FluidExperimentBody body in items)
                if (body != null) changed |= body.UpdateIceContainment();
            if (changed) RefreshCollisionPairs();
        }
    }
}
