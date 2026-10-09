using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentBody
    {
        public uint ContainingVesselId => iceContainer != null ? iceContainer.Id : 0;
        internal FluidExperimentBody ContainingVessel => iceContainer;
        public bool IsInHeldVessel => iceContainer != null && iceContainer.IsHeld;
        // Positional escape recovery is not physical wall motion. GPU collision sweeps
        // still use the corrected pose, but may exclude this displacement from response speed.
        public Vector2 IceRecoveryTranslation { get; private set; }
        private FluidExperimentBody iceContainer;
        private Vector2 previousIcePosition, previousIceFramePosition;
        private bool hasIcePosition;
        private Vector2[] iceLocalHull, iceFrameHull;
        private Vector3 iceHullScale;
        private FluidExperimentCollisionProfile iceHullProfile;

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
            float turn = Mathf.DeltaAngle(fromAngle, toAngle);
            if ((to - from).sqrMagnitude < 1e-12f && Mathf.Abs(turn) < .00001f) return;
            foreach (FluidExperimentBody ice in World.Items)
            {
                if (ice == null || ice.iceContainer != this || ice.IsHeld || !ice.Body.simulated) continue;
                // F garnish is a world-space dynamic body. Moving an empty part of its
                // vessel must not translate it or rotate it as if it were attached.
                if (ice.UsesLiquidGarnishMotion) continue;
                ice.Body.position = to + Rotate(ice.Position - from, turn);
                ice.Body.rotation += turn;
                ice.IceRecoveryTranslation = Rotate(ice.IceRecoveryTranslation, turn);
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
                if (ice.UsesLiquidGarnishMotion) continue;
                ice.Body.linearVelocity += velocity + new Vector2(-offset.y, offset.x) * omega;
                ice.Body.angularVelocity += angularVelocity;
            }
        }

        internal bool UpdateIceContainment()
        {
            if (!IsLooseSolid) return false;
            FluidExperimentBody old = iceContainer;
            if (IsHeld || World == null || !Body.simulated)
            {
                ClearIceContainer();
                return old != null;
            }
            if (iceContainer != null && (!iceContainer.isActiveAndEnabled || iceContainer.World != World
                || !iceContainer.IsVessel)) iceContainer = null;
            if (iceContainer != null)
            {
                Vector2 current = iceContainer.IceFramePoint(Position);
                Vector2 start = previousIceFramePosition;
                BuildIceFrameHull(iceContainer);
                // Normal stacking belongs to Physics2D's real polygon contacts. This is
                // only escape recovery, using the ice shape rather than its enclosing disk.
                current = ResolveIceWalls(iceContainer, start, current);
                Vector2 corrected = iceContainer.Position + Rotate(current, iceContainer.Angle);
                Vector2 recovery = corrected - Position;
                if (recovery.sqrMagnitude > 1e-10f)
                {
                    Body.position = corrected;
                    IceRecoveryTranslation += recovery;
                }
                previousIceFramePosition = current;
                if (!iceContainer.BlocksIceMouth && iceContainer.BeyondIceMouth(current, this))
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

        private void BuildIceFrameHull(FluidExperimentBody vessel)
        {
            Vector3 scale = transform.lossyScale;
            if (iceLocalHull == null || iceHullProfile != collisionProfile || iceHullScale != scale)
            {
                var points = new List<Vector2>();
                if (collisionProfile != null)
                {
                    foreach (FluidExperimentHull hull in collisionProfile.solids)
                        foreach (Vector2 point in hull.points)
                            points.Add(new Vector2(point.x * scale.x, point.y * scale.y));
                }
                else foreach (Collider2D collider in solidColliders)
                {
                    if (collider is PolygonCollider2D polygon)
                        for (int path = 0; path < polygon.pathCount; path++)
                            foreach (Vector2 point in polygon.GetPath(path))
                                points.Add(new Vector2((point.x + polygon.offset.x) * scale.x,
                                    (point.y + polygon.offset.y) * scale.y));
                    else if (collider is BoxCollider2D box)
                        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
                            points.Add(new Vector2((box.offset.x + x * box.size.x * .5f) * scale.x,
                                (box.offset.y + y * box.size.y * .5f) * scale.y));
                    else if (collider is CircleCollider2D circle)
                        for (int vertex = 0; vertex < 16; vertex++)
                        {
                            float theta = vertex * Mathf.PI / 8;
                            float radius = circle.radius / Mathf.Cos(Mathf.PI / 16);
                            points.Add(new Vector2((circle.offset.x + Mathf.Cos(theta) * radius) * scale.x,
                                (circle.offset.y + Mathf.Sin(theta) * radius) * scale.y));
                        }
                }
                // The authored ice is polygonal. A convex envelope is used only for
                // rare recovery; ordinary concave/multi-path contacts remain native.
                if (points.Count < 3) points.AddRange(new[] { new Vector2(-.01f, -.01f),
                    new Vector2(.01f, -.01f), new Vector2(.01f, .01f), new Vector2(-.01f, .01f) });
                points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                var hullPoints = new List<Vector2>(points.Count * 2);
                foreach (Vector2 point in points)
                {
                    while (hullPoints.Count >= 2 && IceCross(hullPoints[hullPoints.Count - 1]
                        - hullPoints[hullPoints.Count - 2], point - hullPoints[hullPoints.Count - 1]) <= 0)
                        hullPoints.RemoveAt(hullPoints.Count - 1);
                    hullPoints.Add(point);
                }
                int lowerCount = hullPoints.Count;
                for (int i = points.Count - 2; i >= 0; i--)
                {
                    Vector2 point = points[i];
                    while (hullPoints.Count > lowerCount && IceCross(hullPoints[hullPoints.Count - 1]
                        - hullPoints[hullPoints.Count - 2], point - hullPoints[hullPoints.Count - 1]) <= 0)
                        hullPoints.RemoveAt(hullPoints.Count - 1);
                    hullPoints.Add(point);
                }
                hullPoints.RemoveAt(hullPoints.Count - 1);
                iceLocalHull = hullPoints.ToArray();
                iceFrameHull = new Vector2[iceLocalHull.Length];
                iceHullProfile = collisionProfile; iceHullScale = scale;
            }
            float angle = Angle - vessel.Angle;
            for (int i = 0; i < iceLocalHull.Length; i++) iceFrameHull[i] = Rotate(iceLocalHull[i], angle);
        }

        private static float IceCross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private void IceProjection(Vector2 axis, out float minimum, out float maximum)
        {
            minimum = float.PositiveInfinity; maximum = float.NegativeInfinity;
            foreach (Vector2 point in iceFrameHull)
            {
                float projection = Vector2.Dot(point, axis);
                minimum = Mathf.Min(minimum, projection); maximum = Mathf.Max(maximum, projection);
            }
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

        private bool BeyondIceMouth(Vector2 point, FluidExperimentBody ice)
        {
            if (!IceMouth(out Vector2 left, out _, out Vector2 outward)) return false;
            ice.IceProjection(outward, out float minimum, out _);
            return Vector2.Dot(point - left, outward) + minimum > .002f;
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

        private Vector2 ResolveIceWalls(FluidExperimentBody vessel, Vector2 start, Vector2 end)
        {
            Vector2[] path = vessel.IceInterior;
            if (path.Length < 3) return end;
            float slop = Mathf.Max(.003f, Physics2D.defaultContactOffset * 2);
            float skin = Mathf.Max(.0005f, Physics2D.defaultContactOffset * .1f);
            float winding = 0;
            for (int i = 0; i < path.Length; i++)
            {
                Vector2 a = vessel.IceFrameVertex(path[i]), b = vessel.IceFrameVertex(path[(i + 1) % path.Length]);
                winding += a.x * b.y - b.x * a.y;
            }
            int edges = path.Length - (vessel.BlocksIceMouth ? 0 : 1);
            Vector2 position = start, remaining = end - start;
            // Small resting/contact corrections are entirely Physics2D's responsibility.
            // Large relative motion can have crossed a wall between native contact steps.
            bool sweep = remaining.sqrMagnitude > slop * slop;
            for (int iteration = 0; sweep && iteration < 4 && remaining.sqrMagnitude > 1e-12f; iteration++)
            {
                float first = 1, separation = skin;
                Vector2 normal = Vector2.zero;
                for (int i = 0; i < edges; i++)
                {
                    Vector2 a = vessel.IceFrameVertex(path[i]), b = vessel.IceFrameVertex(path[(i + 1) % path.Length]);
                    Vector2 edge = b - a;
                    float length = edge.magnitude;
                    if (length < 1e-6f) continue;
                    Vector2 inward = new Vector2(-edge.y, edge.x) / length * (winding >= 0 ? 1 : -1);
                    if (!SweepIceHull(position, remaining, a, b, out float hit, out Vector2 hitNormal)) continue;
                    // An initial contact may overlap by the native solver's slop. It is
                    // recovered only when a large move would carry it farther through the wall.
                    if (hit < 0)
                    {
                        if (Vector2.Dot(remaining, inward) >= -slop) continue;
                        IceProjection(inward, out float support, out _);
                        float depth = -(Vector2.Dot(position - a, inward) + support);
                        if (depth < -slop) continue;
                        if (first > 0)
                        { first = 0; normal = inward; separation = Mathf.Max(skin, depth + skin); }
                    }
                    else if (hit < first && Vector2.Dot(hitNormal, inward) >= -.0001f)
                    { first = hit; normal = hitNormal; separation = skin; }
                }
                position += remaining * first;
                if (normal == Vector2.zero) { remaining = Vector2.zero; break; }
                position += normal * separation;
                remaining *= 1 - first;
                remaining -= normal * Mathf.Min(0, Vector2.Dot(remaining, normal));
                RemoveInwardIceVelocity(vessel, position, normal);
            }
            if (!sweep) position = end;
            // Native contacts may leave a small penetration, which must not trigger another
            // solver. Recover only a deep overlap with the finite edge and actual ice hull.
            for (int iteration = 0; iteration < 4; iteration++)
            {
                float deepest = slop;
                Vector2 normal = Vector2.zero;
                for (int i = 0; i < edges; i++)
                {
                    Vector2 a = vessel.IceFrameVertex(path[i]), b = vessel.IceFrameVertex(path[(i + 1) % path.Length]);
                    Vector2 edge = b - a;
                    if (edge.sqrMagnitude < 1e-12f) continue;
                    Vector2 inward = new Vector2(-edge.y, edge.x).normalized * (winding >= 0 ? 1 : -1);
                    IceProjection(inward, out float support, out _);
                    float depth = -(Vector2.Dot(position - a, inward) + support);
                    if (depth <= deepest || !SweepIceHull(position, Vector2.zero, a, b, out _, out _)) continue;
                    deepest = depth; normal = inward;
                }
                if (normal == Vector2.zero) break;
                position += normal * (deepest + skin);
                RemoveInwardIceVelocity(vessel, position, normal);
            }
            return position;
        }

        // Continuous SAT against a finite segment. Ice-edge axes handle the segment's
        // endpoints, so a corner uses actual shape support instead of an oversized circle.
        private bool SweepIceHull(Vector2 start, Vector2 travel, Vector2 a, Vector2 b,
            out float first, out Vector2 normal)
        {
            first = float.NegativeInfinity; float last = float.PositiveInfinity;
            normal = Vector2.zero;
            for (int axisIndex = -1; axisIndex < iceFrameHull.Length; axisIndex++)
            {
                Vector2 edge = axisIndex < 0 ? b - a
                    : iceFrameHull[(axisIndex + 1) % iceFrameHull.Length] - iceFrameHull[axisIndex];
                if (edge.sqrMagnitude < 1e-12f) continue;
                Vector2 axis = new Vector2(-edge.y, edge.x).normalized;
                IceProjection(axis, out float hullMin, out float hullMax);
                float projectionA = Vector2.Dot(a, axis), projectionB = Vector2.Dot(b, axis);
                float minimum = Mathf.Min(projectionA, projectionB) - hullMax;
                float maximum = Mathf.Max(projectionA, projectionB) - hullMin;
                float origin = Vector2.Dot(start, axis), movement = Vector2.Dot(travel, axis);
                if (Mathf.Abs(movement) < 1e-8f)
                {
                    if (origin < minimum || origin > maximum) return false;
                    continue;
                }
                float enter = (minimum - origin) / movement, leave = (maximum - origin) / movement;
                Vector2 entryNormal = -axis;
                if (enter > leave) { float swap = enter; enter = leave; leave = swap; entryNormal = axis; }
                if (enter > first) { first = enter; normal = entryNormal; }
                last = Mathf.Min(last, leave);
                if (first > last) return false;
            }
            return last >= 0 && first <= 1;
        }

        private void RemoveInwardIceVelocity(FluidExperimentBody vessel, Vector2 framePoint, Vector2 frameNormal)
        {
            Vector2 normal = Rotate(frameNormal, vessel.Angle);
            // Held contents are transported in a common frame and retain their free
            // velocity. A dynamic vessel instead has a genuine contact-point velocity.
            Vector2 wallVelocity = vessel.IsHeld ? Vector2.zero
                : vessel.Body.GetPointVelocity(vessel.Position + Rotate(framePoint, vessel.Angle));
            float inwardSpeed = Vector2.Dot(Body.linearVelocity - wallVelocity, normal);
            if (inwardSpeed < 0) Body.linearVelocity -= normal * inwardSpeed;
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
