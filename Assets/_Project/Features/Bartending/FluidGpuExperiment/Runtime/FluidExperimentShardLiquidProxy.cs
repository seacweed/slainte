using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    // Front-view shard artwork covers the liquid interior. Intersect its silhouette
    // with the actual glass walls, so the GPU never treats the whole picture as solid.
    [DisallowMultipleComponent]
    public sealed class FluidExperimentShardLiquidProxy : MonoBehaviour
    {
        public readonly struct Segment
        {
            public readonly Vector2 A, B;
            public Segment(Vector2 a, Vector2 b) { A = a; B = b; }
        }
        private readonly List<Segment> segments = new List<Segment>();
        public IReadOnlyList<Segment> Segments => segments;
        public Rigidbody2D Body { get; private set; }
        public Vector2 PreviousPosition { get; private set; }
        public float PreviousAngle { get; private set; }

        internal void Initialize(FluidExperimentGlassShard shard)
        {
            Body = shard.Body;
            var definition = shard.Source.profile.shards[shard.DefinitionIndex];
            var geometry = shard.Source.Body.collisionProfile;
            if (geometry != null)
                foreach (var wall in geometry.solids)
                    AppendIntersection(wall.points, definition.outline, definition.localCenter, segments);
            SynchronizeHistory();
        }

        internal void SynchronizeHistory()
        {
            if (Body == null) return;
            PreviousPosition = Body.position;
            PreviousAngle = Body.rotation;
        }

        // Edge clipping works with concave silhouettes too. The intersection boundary
        // consists of each polygon's edges clipped to the other polygon; no convex hull
        // approximation may close the empty bowl or bridge separated wall sections.
        public static void AppendIntersection(Vector2[] wall, Vector2[] silhouette, Vector2 center, List<Segment> result)
        {
            if (wall == null || silhouette == null || wall.Length < 3 || silhouette.Length < 3) return;
            var localWall = new Vector2[wall.Length];
            for (int i = 0; i < wall.Length; i++) localWall[i] = wall[i] - center;
            ClipEdges(localWall, silhouette, result);
            ClipEdges(silhouette, localWall, result);
        }

        private static void ClipEdges(Vector2[] source, Vector2[] clip, List<Segment> result)
        {
            var cuts = new List<float>(clip.Length + 2);
            for (int i = 0; i < source.Length; i++)
            {
                Vector2 a = source[i], b = source[(i + 1) % source.Length], delta = b - a;
                float square = delta.sqrMagnitude;
                if (square < 1e-10f) continue;
                cuts.Clear(); cuts.Add(0); cuts.Add(1);
                for (int j = 0; j < clip.Length; j++)
                {
                    Vector2 c = clip[j], d = clip[(j + 1) % clip.Length], edge = d - c;
                    float denominator = Cross(delta, edge);
                    if (Mathf.Abs(denominator) > 1e-8f)
                    {
                        float t = Cross(c - a, edge) / denominator, u = Cross(c - a, delta) / denominator;
                        if (t > 0 && t < 1 && u >= -1e-6f && u <= 1.000001f) cuts.Add(t);
                    }
                    else if (Mathf.Abs(Cross(c - a, delta)) < 1e-7f)
                    {
                        cuts.Add(Mathf.Clamp01(Vector2.Dot(c - a, delta) / square));
                        cuts.Add(Mathf.Clamp01(Vector2.Dot(d - a, delta) / square));
                    }
                }
                cuts.Sort();
                for (int j = 1; j < cuts.Count; j++)
                {
                    if (cuts[j] - cuts[j - 1] < 1e-6f) continue;
                    Vector2 middle = a + delta * ((cuts[j] + cuts[j - 1]) * .5f);
                    if (!FluidExperimentCollisionProfile.Contains(clip, middle)
                        && FluidExperimentCollisionProfile.EdgeDistance(clip, middle) > .00001f) continue;
                    Vector2 start = a + delta * cuts[j - 1], end = a + delta * cuts[j];
                    if ((end - start).sqrMagnitude < 1e-8f) continue;
                    bool duplicate = false;
                    foreach (var existing in result)
                        if (((start - existing.A).sqrMagnitude < 1e-10f && (end - existing.B).sqrMagnitude < 1e-10f)
                            || ((end - existing.A).sqrMagnitude < 1e-10f && (start - existing.B).sqrMagnitude < 1e-10f))
                        { duplicate = true; break; }
                    if (!duplicate) result.Add(new Segment(start, end));
                }
            }
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
