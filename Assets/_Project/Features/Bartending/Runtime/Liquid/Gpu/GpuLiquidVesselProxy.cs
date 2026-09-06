using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    internal sealed class GpuLiquidVesselProxy
    {
        private const int CircleSegments = 12;

        private readonly VesselLiquidTracker tracker;
        private Matrix4x4 previousRootLocalToWorld;
        private bool hasPreviousTransform;

        public GpuLiquidVesselProxy(
            VesselLiquidTracker tracker,
            uint vesselId,
            int maximumIngredients)
        {
            this.tracker = tracker;
            VesselId = vesselId;
            Snapshot = new GpuLiquidVesselSnapshot(maximumIngredients);
        }

        public VesselLiquidTracker Tracker => tracker;
        public uint VesselId { get; }
        public GpuLiquidVesselSnapshot Snapshot { get; }

        public void SynchronizeTransformHistory()
        {
            if (tracker == null)
                return;

            previousRootLocalToWorld = tracker.transform.localToWorldMatrix;
            hasPreviousTransform = true;
        }

        public bool ContainsPoint(Vector2 worldPoint)
        {
            if (tracker == null || !tracker.isActiveAndEnabled)
                return false;

            Collider2D[] colliders = tracker.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider != null
                    && collider.enabled
                    && collider.isTrigger
                    && collider.OverlapPoint(worldPoint))
                {
                    return true;
                }
            }

            return false;
        }

        public void CollectGeometry(
            List<GpuLiquidBoundarySegment> boundaries,
            List<GpuLiquidVesselTrigger> triggers,
            float deltaTime)
        {
            if (tracker == null || !tracker.isActiveAndEnabled)
                return;

            Matrix4x4 currentRoot = tracker.transform.localToWorldMatrix;
            Matrix4x4 previousRoot = hasPreviousTransform
                ? previousRootLocalToWorld
                : currentRoot;
            float safeDeltaTime = Mathf.Max(0.0001f, deltaTime);
            Collider2D[] colliders = tracker.GetComponentsInChildren<Collider2D>();

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null
                    || !collider.enabled
                    || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (collider.isTrigger)
                {
                    AddTrigger(collider, triggers);
                    continue;
                }

                if (collider.GetComponent<IceOnlyVesselBarrier>() != null)
                    continue;

                if (collider is EdgeCollider2D edge)
                {
                    AddEdge(edge, boundaries, currentRoot, previousRoot, safeDeltaTime);
                }
                else if (collider is BoxCollider2D box)
                {
                    AddBox(box, boundaries, currentRoot, previousRoot, safeDeltaTime);
                }
                else if (collider is CircleCollider2D circle)
                {
                    AddCircle(circle, boundaries, currentRoot, previousRoot, safeDeltaTime);
                }
                else if (collider is PolygonCollider2D polygon)
                {
                    AddPolygon(polygon, boundaries, currentRoot, previousRoot, safeDeltaTime);
                }
            }

            previousRootLocalToWorld = currentRoot;
            hasPreviousTransform = true;
        }

        private void AddTrigger(
            Collider2D collider,
            List<GpuLiquidVesselTrigger> triggers)
        {
            CobblerShakerTechniqueController shaker =
                tracker.GetComponent<CobblerShakerTechniqueController>();
            uint flags = shaker != null && shaker.IsFullyClosed ? 1u : 0u;
            if (collider is BoxCollider2D box)
            {
                Vector2 center = box.transform.TransformPoint(box.offset);
                Vector2 axisXVector = box.transform.TransformVector(Vector2.right * box.size.x);
                Vector2 axisYVector = box.transform.TransformVector(Vector2.up * box.size.y);
                triggers.Add(new GpuLiquidVesselTrigger
                {
                    Center = center,
                    AxisX = axisXVector.sqrMagnitude > 0.000001f
                        ? axisXVector.normalized
                        : Vector2.right,
                    AxisY = axisYVector.sqrMagnitude > 0.000001f
                        ? axisYVector.normalized
                        : Vector2.up,
                    HalfExtents = new Vector2(
                        axisXVector.magnitude * 0.5f,
                        axisYVector.magnitude * 0.5f),
                    VesselId = VesselId,
                    Priority = tracker.InteractionPriority,
                    Active = 1,
                    Flags = flags
                });
                return;
            }

            Bounds bounds = collider.bounds;
            triggers.Add(new GpuLiquidVesselTrigger
            {
                Center = bounds.center,
                AxisX = Vector2.right,
                AxisY = Vector2.up,
                HalfExtents = bounds.extents,
                VesselId = VesselId,
                Priority = tracker.InteractionPriority,
                Active = 1,
                Flags = flags
            });
        }

        private void AddEdge(
            EdgeCollider2D edge,
            List<GpuLiquidBoundarySegment> boundaries,
            Matrix4x4 currentRoot,
            Matrix4x4 previousRoot,
            float deltaTime)
        {
            Vector2[] points = edge.points;
            for (int i = 0; i + 1 < points.Length; i++)
            {
                AddSegment(
                    edge.transform,
                    points[i] + edge.offset,
                    points[i + 1] + edge.offset,
                    boundaries,
                    currentRoot,
                    previousRoot,
                    deltaTime);
            }
        }

        private void AddBox(
            BoxCollider2D box,
            List<GpuLiquidBoundarySegment> boundaries,
            Matrix4x4 currentRoot,
            Matrix4x4 previousRoot,
            float deltaTime)
        {
            Vector2 half = box.size * 0.5f;
            Vector2 center = box.offset;
            Vector2 p0 = center + new Vector2(-half.x, -half.y);
            Vector2 p1 = center + new Vector2(half.x, -half.y);
            Vector2 p2 = center + new Vector2(half.x, half.y);
            Vector2 p3 = center + new Vector2(-half.x, half.y);
            AddSegment(box.transform, p0, p1, boundaries, currentRoot, previousRoot, deltaTime);
            AddSegment(box.transform, p1, p2, boundaries, currentRoot, previousRoot, deltaTime);
            AddSegment(box.transform, p2, p3, boundaries, currentRoot, previousRoot, deltaTime);
            AddSegment(box.transform, p3, p0, boundaries, currentRoot, previousRoot, deltaTime);
        }

        private void AddCircle(
            CircleCollider2D circle,
            List<GpuLiquidBoundarySegment> boundaries,
            Matrix4x4 currentRoot,
            Matrix4x4 previousRoot,
            float deltaTime)
        {
            for (int i = 0; i < CircleSegments; i++)
            {
                float angleA = i * Mathf.PI * 2f / CircleSegments;
                float angleB = (i + 1) * Mathf.PI * 2f / CircleSegments;
                Vector2 a = circle.offset
                    + new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA)) * circle.radius;
                Vector2 b = circle.offset
                    + new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB)) * circle.radius;
                AddSegment(
                    circle.transform,
                    a,
                    b,
                    boundaries,
                    currentRoot,
                    previousRoot,
                    deltaTime);
            }
        }

        private void AddPolygon(
            PolygonCollider2D polygon,
            List<GpuLiquidBoundarySegment> boundaries,
            Matrix4x4 currentRoot,
            Matrix4x4 previousRoot,
            float deltaTime)
        {
            for (int pathIndex = 0; pathIndex < polygon.pathCount; pathIndex++)
            {
                Vector2[] path = polygon.GetPath(pathIndex);
                for (int i = 0; i < path.Length; i++)
                {
                    AddSegment(
                        polygon.transform,
                        path[i] + polygon.offset,
                        path[(i + 1) % path.Length] + polygon.offset,
                        boundaries,
                        currentRoot,
                        previousRoot,
                        deltaTime);
                }
            }
        }

        private void AddSegment(
            Transform colliderTransform,
            Vector2 localA,
            Vector2 localB,
            List<GpuLiquidBoundarySegment> boundaries,
            Matrix4x4 currentRoot,
            Matrix4x4 previousRoot,
            float deltaTime)
        {
            Vector3 currentA3 = colliderTransform.TransformPoint(localA);
            Vector3 currentB3 = colliderTransform.TransformPoint(localB);
            Vector3 rootLocalA = tracker.transform.InverseTransformPoint(currentA3);
            Vector3 rootLocalB = tracker.transform.InverseTransformPoint(currentB3);
            Vector2 currentA = currentRoot.MultiplyPoint3x4(rootLocalA);
            Vector2 currentB = currentRoot.MultiplyPoint3x4(rootLocalB);
            Vector2 previousA = previousRoot.MultiplyPoint3x4(rootLocalA);
            Vector2 previousB = previousRoot.MultiplyPoint3x4(rootLocalB);

            boundaries.Add(new GpuLiquidBoundarySegment
            {
                A = currentA,
                B = currentB,
                VelocityA = (currentA - previousA) / deltaTime,
                VelocityB = (currentB - previousB) / deltaTime,
                VesselId = VesselId,
                Flags = 0
            });
        }
    }
}
