using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed class FluidExperimentEnvironmentHull
    {
        public BoxCollider2D Collider;
        public readonly Vector2[] Points = new Vector2[4];
        private Matrix4x4 matrix;
        private Vector2 size, offset;
        private bool ready;

        internal void Refresh()
        {
            Matrix4x4 next = Collider.transform.localToWorldMatrix;
            if (ready && next == matrix && size == Collider.size && offset == Collider.offset) return;
            matrix = next; size = Collider.size; offset = Collider.offset; ready = true;
            Vector2 half = size * .5f;
            Points[0] = matrix.MultiplyPoint3x4(offset - half);
            Points[1] = matrix.MultiplyPoint3x4(offset + new Vector2(half.x, -half.y));
            Points[2] = matrix.MultiplyPoint3x4(offset + half);
            Points[3] = matrix.MultiplyPoint3x4(offset + new Vector2(-half.x, half.y));
        }
    }

    public sealed partial class FluidExperimentWorld
    {
        [Header("Liquid environment (the bottom remains a drain)")]
        public BoxCollider2D leftBoundary, rightBoundary, topBoundary;
        private readonly List<FluidExperimentEnvironmentHull> environmentHulls = new List<FluidExperimentEnvironmentHull>(3);
        private bool environmentResolved;

        public IReadOnlyList<FluidExperimentEnvironmentHull> EnvironmentHulls
        {
            get
            {
                ResolveEnvironment();
                foreach (var hull in environmentHulls) if (hull.Collider != null) hull.Refresh();
                return environmentHulls;
            }
        }

        private void ResolveEnvironment()
        {
            if (environmentResolved) return;
            environmentResolved = true;
            // Older copies of the authored comparison scene migrate without editor-side mutation.
            if (leftBoundary == null) leftBoundary = transform.Find("LeftBoundary")?.GetComponent<BoxCollider2D>();
            if (rightBoundary == null) rightBoundary = transform.Find("RightBoundary")?.GetComponent<BoxCollider2D>();
            if (topBoundary == null) topBoundary = transform.Find("TopBoundary")?.GetComponent<BoxCollider2D>();
            foreach (var collider in new[] { leftBoundary, rightBoundary, topBoundary })
                if (collider != null) environmentHulls.Add(new FluidExperimentEnvironmentHull { Collider = collider });
        }

        public Vector2 ConstrainHeldPosition(FluidExperimentBody body, Vector2 position, float angle)
        {
            ResolveEnvironment();
            if (body == null || environmentHulls.Count == 0) return position;
            Vector2 min = Vector2.zero, max = Vector2.zero;
            bool found = false;
            void Include(Vector2 local)
            {
                Vector2 p = body.PointAt(local, Vector2.zero, angle);
                if (!found) { min = max = p; found = true; }
                else { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            }
            if (body.collisionProfile != null)
            {
                foreach (var hull in body.collisionProfile.solids) foreach (var p in hull.points) Include(p);
                if (body.kind == LabItemKind.Shaker)
                    foreach (var p in body.collisionProfile.lid) Include(p);
            }
            else foreach (var p in body.liquidWall) Include(p);
            const float margin = .005f;
            if (leftBoundary != null && leftBoundary.isActiveAndEnabled)
                position.x = Mathf.Max(position.x, leftBoundary.bounds.max.x - min.x + margin);
            if (rightBoundary != null && rightBoundary.isActiveAndEnabled)
                position.x = Mathf.Min(position.x, rightBoundary.bounds.min.x - max.x - margin);
            if (topBoundary != null && topBoundary.isActiveAndEnabled)
                position.y = Mathf.Min(position.y, topBoundary.bounds.min.y - max.y - margin);
            return position;
        }
    }
}
