using System;
using UnityEngine;

namespace Slainte.Bartending.PhysicsLab
{
    /// <summary>Authoritative geometry in body-local coordinates, shared by Box2D and the GPU.</summary>
    [CreateAssetMenu(menuName = "Slainte/Physics Lab/Collision Profile")]
    public sealed class PhysicsLabCollisionProfile : ScriptableObject
    {
        public PhysicsLabHull[] solids = Array.Empty<PhysicsLabHull>();
        public Vector2[] lid = Array.Empty<Vector2>();
        // Open U path, from the left lip along the bowl to the right lip. Closing it is ownership only.
        public Vector2[] interior = Array.Empty<Vector2>();
        public Vector2 mouth, exitDirection = Vector2.up;
        public float sourcePixelSize;
        public string sourceDescription;

        public Rect InteriorBounds => BoundsOf(interior);
        public int BoundaryCount
        {
            get
            {
                int count = interior.Length + lid.Length;
                foreach (var hull in solids) count += hull.points.Length;
                return count;
            }
        }
        public static Rect BoundsOf(Vector2[] points)
        {
            if (points.Length == 0) return default;
            Vector2 min = points[0], max = points[0];
            foreach (var p in points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        public static bool Contains(Vector2[] path, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = path.Length - 1; i < path.Length; j = i++)
                if ((path[i].y > p.y) != (path[j].y > p.y)
                    && p.x < (path[j].x - path[i].x) * (p.y - path[i].y) / (path[j].y - path[i].y) + path[i].x)
                    inside = !inside;
            return inside;
        }
        public static Vector2 Closest(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return a + d * Mathf.Clamp01(Vector2.Dot(p - a, d) / Mathf.Max(1e-12f, d.sqrMagnitude));
        }
        public static float EdgeDistance(Vector2[] path, Vector2 p, bool closed = true)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < path.Length - (closed ? 0 : 1); i++)
                best = Mathf.Min(best, Vector2.Distance(p, Closest(p, path[i], path[(i + 1) % path.Length])));
            return best;
        }
    }
}
