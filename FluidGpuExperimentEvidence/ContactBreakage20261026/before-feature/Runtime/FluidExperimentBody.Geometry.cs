using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentBody
    {
        public FluidExperimentCollisionProfile collisionProfile;
        private readonly List<Vector2> buoyancyClip = new List<Vector2>(32);

        internal Bounds IceBuoyancyBounds
        {
            get
            {
                if (collisionProfile == null) return SolidBounds;
                Bounds bounds = new Bounds(Position, Vector3.zero); bool found = false;
                // Collider.bounds may still describe the pose before a completed
                // wall recovery. Sample the same native pose used for immersion.
                foreach (var hull in collisionProfile.solids) foreach (var local in hull.points)
                {
                    Vector2 point = LocalToWorld(local);
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
                return bounds;
            }
        }

        // Integrate the authored solid below a horizontal liquid surface. Using
        // its AABB makes immersion change merely because a cube rotates, and
        // loses the off-centre lift which should oppose an unstable orientation.
        internal void SampleIceImmersion(float surface, out float fraction, out Vector2 centre)
        {
            float fullArea = 0, wetArea = 0;
            Vector2 moment = Vector2.zero;
            if (collisionProfile != null)
                foreach (var hull in collisionProfile.solids)
                {
                    float crossSum = 0;
                    buoyancyClip.Clear();
                    Vector2 previous = LocalToWorld(hull.points[hull.points.Length - 1]);
                    foreach (Vector2 local in hull.points)
                    {
                        Vector2 point = LocalToWorld(local);
                        crossSum += previous.x * point.y - point.x * previous.y;
                        bool beforeWet = previous.y <= surface, pointWet = point.y <= surface;
                        if (beforeWet != pointWet)
                            buoyancyClip.Add(Vector2.LerpUnclamped(previous, point,
                                (surface - previous.y) / (point.y - previous.y)));
                        if (pointWet) buoyancyClip.Add(point);
                        previous = point;
                    }
                    fullArea += Mathf.Abs(crossSum) * .5f;
                    if (buoyancyClip.Count < 3) continue;
                    float wetCross = 0; Vector2 centroidSum = Vector2.zero;
                    previous = buoyancyClip[buoyancyClip.Count - 1];
                    foreach (Vector2 point in buoyancyClip)
                    {
                        float cross = previous.x * point.y - point.x * previous.y;
                        wetCross += cross; centroidSum += (previous + point) * cross;
                        previous = point;
                    }
                    if (Mathf.Abs(wetCross) < 1e-8f) continue;
                    float area = Mathf.Abs(wetCross) * .5f;
                    wetArea += area; moment += centroidSum / (3 * wetCross) * area;
                }
            fraction = fullArea > 1e-8f ? Mathf.Clamp01(wetArea / fullArea) : 0;
            centre = wetArea > 1e-8f && fraction < .99999f ? moment / wetArea : Body.worldCenterOfMass;
        }

        // Also used by the editor after profile edits. Pick trigger, art and gameplay settings are untouched.
        public void ApplyCollisionProfile()
        {
            if (collisionProfile == null) return;
            var p = collisionProfile;
            if (solidColliders.Length == 0 || !(solidColliders[0] is PolygonCollider2D))
                throw new InvalidOperationException(name + ": bake collision profile before use.");
            var solid = (PolygonCollider2D)solidColliders[0];
            solid.offset = Vector2.zero; solid.pathCount = p.solids.Length;
            for (int i = 0; i < p.solids.Length; i++) solid.SetPath(i, p.solids[i].points);
            if (capCollider is PolygonCollider2D cap) { cap.offset = Vector2.zero; cap.points = p.lid; }
            if (p.interior.Length > 0)
            {
                liquidWall = p.interior;
                contentRegions = new[] { p.InteriorBounds };
                wallClosed = false;
            }
            else
            {
                liquidWall = p.solids[0].points;
                wallClosed = true;
                // Profile changes from a receptacle to a stock source must remove stale triggers.
                contentRegions = Array.Empty<Rect>();
            }
            extraSolidHulls = Array.Empty<FluidExperimentHull>();
            mouthLocal = p.mouth;
            if (kind == LabItemKind.Bottle)
            {
                mouthLipLocal = p.mouth; overrideMouthLip = true;
                mouthDirectionLocal = p.exitDirection;
            }
        }

        public bool ContainsLiquidDisk(Vector2 local, float worldRadius)
        {
            if (collisionProfile == null) return ContainsLiquid(LocalToWorld(local));
            var path = LiquidInteriorPath;
            if (!FluidExperimentCollisionProfile.Contains(path, local)) return false;
            Vector3 scale = transform.lossyScale;
            float radius = worldRadius / Mathf.Max(.0001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            return FluidExperimentCollisionProfile.EdgeDistance(path, local) >= radius;
        }

        // Evaluated in the unrotated, scaled body frame; independent of a wide pick box and rendered interpolation.
        public float SolidClearance(Vector2 point)
        {
            float best = float.PositiveInfinity;
            if (collisionProfile == null) return best;
            // PointAt(local, Vector2.zero, 0) is local * lossyScale. The scale is
            // constant throughout this query; avoid two Transform reads and rotations per edge.
            Vector3 scale = transform.lossyScale;
            foreach (var hull in collisionProfile.solids)
            {
                bool inside = false;
                float distance = float.PositiveInfinity;
                for (int i = 0, j = hull.points.Length - 1; i < hull.points.Length; j = i++)
                {
                    Vector2 a = new Vector2(hull.points[j].x * scale.x, hull.points[j].y * scale.y);
                    Vector2 b = new Vector2(hull.points[i].x * scale.x, hull.points[i].y * scale.y);
                    distance = Mathf.Min(distance, Vector2.Distance(point, FluidExperimentCollisionProfile.Closest(point, a, b)));
                    if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x) inside = !inside;
                }
                best = Mathf.Min(best, inside ? -distance : distance);
            }
            return best;
        }
    }
}
