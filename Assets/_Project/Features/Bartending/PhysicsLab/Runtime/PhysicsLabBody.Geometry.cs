using System;
using UnityEngine;

namespace Slainte.Bartending.PhysicsLab
{
    public sealed partial class PhysicsLabBody
    {
        public PhysicsLabCollisionProfile collisionProfile;

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
            else { liquidWall = p.solids[0].points; wallClosed = true; }
            extraSolidHulls = Array.Empty<PhysicsLabHull>();
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
            var path = collisionProfile.interior;
            if (!PhysicsLabCollisionProfile.Contains(path, local)) return false;
            Vector3 scale = transform.lossyScale;
            float radius = worldRadius / Mathf.Max(.0001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            return PhysicsLabCollisionProfile.EdgeDistance(path, local) >= radius;
        }

        // Evaluated in the unrotated, scaled body frame; independent of a wide pick box and rendered interpolation.
        public float SolidClearance(Vector2 point)
        {
            float best = float.PositiveInfinity;
            if (collisionProfile == null) return best;
            foreach (var hull in collisionProfile.solids)
            {
                bool inside = false;
                float distance = float.PositiveInfinity;
                for (int i = 0, j = hull.points.Length - 1; i < hull.points.Length; j = i++)
                {
                    Vector2 a = PointAt(hull.points[j], Vector2.zero, 0), b = PointAt(hull.points[i], Vector2.zero, 0);
                    distance = Mathf.Min(distance, Vector2.Distance(point, PhysicsLabCollisionProfile.Closest(point, a, b)));
                    if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x) inside = !inside;
                }
                best = Mathf.Min(best, inside ? -distance : distance);
            }
            return best;
        }
    }
}
