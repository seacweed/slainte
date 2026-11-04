using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        public int LastShardBoundaryCount { get; private set; }
        public int SkippedShardBoundaryCount { get; private set; }

        private void UploadShardGeometry(float from, float to, float dt,
            ref int boundaryCount, ref int groupCount, ref int contactCount)
        {
            LastShardBoundaryCount = SkippedShardBoundaryCount = 0;
            if (!world.shardLiquidCollision || !CohesivePhysicsActive) return;
            int budget = Mathf.Min(Mathf.Clamp(world.maximumShardLiquidSegments, 0, 1024), MaximumBoundarySegments - boundaryCount);
            foreach (var proxy in world.ShardLiquidProxies)
            {
                if (proxy == null || !proxy.isActiveAndEnabled || proxy.Body == null || !proxy.Body.simulated) continue;
                int count = proxy.Segments.Count;
                // The vessel budget has priority. Skip a complete proxy rather than uploading
                // an arbitrary partial wall; expose the omission to the designer diagnostics.
                if (count > budget) { SkippedShardBoundaryCount += count; continue; }
                if (count == 0) continue;
                float turn = Mathf.DeltaAngle(proxy.PreviousAngle, proxy.Body.rotation);
                Vector2 previous = Vector2.Lerp(proxy.PreviousPosition, proxy.Body.position, from);
                Vector2 position = Vector2.Lerp(proxy.PreviousPosition, proxy.Body.position, to);
                float startAngle = proxy.PreviousAngle + turn * from;
                float angle = proxy.PreviousAngle + turn * to;
                float deltaAngle = turn * (to - from) * Mathf.Deg2Rad;
                Vector2 velocity = (position - previous) / dt;
                float spin = deltaAngle / dt;
                Vector3 scale = proxy.transform.lossyScale;
                int first = boundaryCount;
                foreach (var segment in proxy.Segments)
                {
                    Vector2 localA = Vector2.Scale(segment.A, scale), localB = Vector2.Scale(segment.B, scale);
                    Vector2 offsetA = FluidExperimentBody.Rotate(localA, angle), offsetB = FluidExperimentBody.Rotate(localB, angle);
                    boundaryUpload[boundaryCount++] = new GpuLiquidBoundarySegment
                    {
                        A = position + offsetA, B = position + offsetB,
                        LocalA = localA, LocalB = localB, StartPosition = previous, EndPosition = position,
                        StartAngle = startAngle * Mathf.Deg2Rad, AngleDelta = deltaAngle,
                        VelocityA = velocity + new Vector2(-offsetA.y, offsetA.x) * spin,
                        VelocityB = velocity + new Vector2(-offsetB.y, offsetB.x) * spin,
                        // Reuse simultaneous moving-solid contact stabilization without an owner or trigger.
                        VesselId = 0, Flags = 4u
                    };
                }
                UploadBoundaryGroup(groupCount++, 0, first, boundaryCount, first, first);
                LastShardBoundaryCount += count; contactCount += count; budget -= count;
            }
        }
    }
}
