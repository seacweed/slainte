using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        private struct GarnishFlowFrame { public Vector2 position; public float angle; }
        private Dictionary<uint, GarnishFlowFrame> garnishFlowFrames;

        private Dictionary<uint, GarnishFlowFrame> CaptureGarnishFlowFrames()
        {
            if (!useCohesivePhysics || world == null) return null;
            var frames = new Dictionary<uint, GarnishFlowFrame>();
            foreach (var body in world.Items)
                if (body != null && body.IsVessel)
                    frames[body.Id] = new GarnishFlowFrame { position = body.Position, angle = body.Angle };
            return frames;
        }

        // Read only completed snapshots. Never wait on the GPU in the physics loop.
        public bool TrySampleGarnishFlow(FluidExperimentBody garnish, out Vector2 velocity, out float submerged)
            => TrySampleGarnishFlow(garnish, out velocity, out submerged, out _, out _);

        public bool TrySampleGarnishFlow(FluidExperimentBody garnish, out Vector2 velocity, out float submerged,
            out float angularVelocity, out float angularAcceleration)
        {
            velocity = Vector2.zero; submerged = 0;
            angularVelocity = angularAcceleration = 0;
            if (!IsOperational || !useCohesivePhysics || Ledger == null || Ledger.Generation != generation
                || simulationTime - Ledger.SimulationTime > .35f || garnishFlowFrames == null) return false;
            uint owner = garnish.ContainingVesselId;
            if (owner == 0 || !garnishFlowFrames.TryGetValue(owner, out var frame)) return false;
            var vessel = garnish.ContainingVessel;
            if (vessel == null) return false;
            Bounds bounds = garnish.SolidBounds;
            garnish.GarnishFloatProbes(out Vector2 arm, out float thickness);
            Vector2 left = garnish.Position - arm, right = garnish.Position + arm;
            Vector2 leftFlow = Vector2.zero, rightFlow = Vector2.zero;
            float leftWeight = 0, rightWeight = 0, leftAmount = 0, rightAmount = 0;
            float leftSurface = float.NegativeInfinity, rightSurface = float.NegativeInfinity;
            float reach = Radius * 2, amount = 0, surface = float.NegativeInfinity;
            float probeReach = reach + thickness * .5f;
            foreach (var p in snapshotParticles)
            {
                if (p.Active == 0 || p.VesselId != owner || p.VolumeMl <= 0) continue;
                Vector2 offset = p.Position - frame.position;
                Vector2 point = vessel.Position + FluidExperimentBody.Rotate(offset, vessel.Angle - frame.angle);
                if (point.x < bounds.min.x - reach || point.x > bounds.max.x + reach
                    || point.y < bounds.min.y - reach || point.y > bounds.max.y + reach) continue;
                // GPU velocity and the native garnish velocity are both world-space.
                // Do not subtract vessel motion: the garnish is no longer carried by it.
                Vector2 flow = FluidExperimentBody.Rotate(p.Velocity, vessel.Angle - frame.angle);
                velocity += flow * p.VolumeMl;
                amount += p.VolumeMl; surface = Mathf.Max(surface, point.y + Radius);
                void Probe(Vector2 probe, ref Vector2 sum, ref float weight, ref float volume, ref float top)
                {
                    float q = Mathf.Clamp01(1 - Vector2.Distance(point, probe) / probeReach);
                    if (q <= 0) return;
                    float w = p.VolumeMl * q * q;
                    sum += flow * w; weight += w; volume += p.VolumeMl;
                    top = Mathf.Max(top, point.y + Radius);
                }
                Probe(left, ref leftFlow, ref leftWeight, ref leftAmount, ref leftSurface);
                Probe(right, ref rightFlow, ref rightWeight, ref rightAmount, ref rightSurface);
            }
            if (amount < 1) { velocity = Vector2.zero; return false; }
            velocity = Vector2.ClampMagnitude(velocity / amount, 6);
            submerged = Mathf.Clamp01((surface - bounds.min.y) / Mathf.Max(.02f, bounds.size.y)) * Mathf.Clamp01(amount / 3);
            if (leftWeight > .0001f && rightWeight > .0001f)
            {
                // The common translation cancels. Only a spatial velocity difference spins the peel.
                Vector2 difference = rightFlow / rightWeight - leftFlow / leftWeight;
                Vector2 span = arm * 2;
                float spin = (span.x * difference.y - span.y * difference.x) / Mathf.Max(.001f, span.sqrMagnitude);
                angularVelocity = Mathf.Clamp(spin * Mathf.Rad2Deg, -180, 180);
                if (Mathf.Abs(angularVelocity) < 3) angularVelocity = 0;
            }
            float depth = Mathf.Max(Radius * 2, thickness);
            float leftWet = Mathf.Clamp01((leftSurface - left.y + depth * .5f) / depth) * Mathf.Clamp01(leftAmount / 1.5f);
            float rightWet = Mathf.Clamp01((rightSurface - right.y + depth * .5f) / depth) * Mathf.Clamp01(rightAmount / 1.5f);
            Vector2 lift = -Physics2D.gravity * (1.15f * .5f * (rightWet - leftWet));
            float torque = (arm.x * lift.y - arm.y * lift.x) / Mathf.Max(.001f, arm.sqrMagnitude);
            angularAcceleration = Mathf.Clamp(torque * Mathf.Rad2Deg * .2f, -240, 240);
            if (Mathf.Abs(angularAcceleration) < 2) angularAcceleration = 0;
            return submerged > 0;
        }
    }
}
