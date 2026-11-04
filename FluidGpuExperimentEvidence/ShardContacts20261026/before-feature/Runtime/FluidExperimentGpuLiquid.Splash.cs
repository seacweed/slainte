using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        private ComputeShader splashBoundShader;
        private int applyBreakSplashKernel;

        /// <summary>
        /// Adds a single bounded impact to this vessel's existing liquid and queued births,
        /// then releases ownership. Calling again for the old owner cannot replay the impact.
        /// </summary>
        public void ReleaseWithSplash(uint owner, Vector2 center, Vector2 point, Vector2 normal,
            float impactSpeed, float impulsePerMass, FluidExperimentSplashProfile profile, bool explosive)
        {
            if (!IsOperational || owner == 0) return;
            if (profile == null || !FluidExperimentSplashProfile.Finite(center) || !FluidExperimentSplashProfile.Finite(point))
            { ReleaseOwner(owner); return; }

            if (splashBoundShader != simulationShader)
            {
                applyBreakSplashKernel = RequireKernel("ApplyBreakSplash");
                BindCommonBuffers(applyBreakSplashKernel);
                splashBoundShader = simulationShader;
            }
            if (profile.centerOnLiquid) center = CompletedSplashCenter(owner, center);
            var parameters = profile.BuildParameters(center, point, normal, impactSpeed, impulsePerMass, explosive);
            simulationShader.SetInt("_TransformTargetVessel", unchecked((int)owner));
            simulationShader.SetVector("_BreakSplashCenter", parameters.Center);
            simulationShader.SetVector("_BreakSplashPoint", parameters.Point);
            simulationShader.SetVector("_BreakSplashNormal", parameters.Normal);
            simulationShader.SetVector("_BreakSplashDirection", parameters.Direction);
            simulationShader.SetVector("_BreakSplashImpact", parameters.Impact);
            DispatchForCount(applyBreakSplashKernel, particleCapacity);

            // These births have not reached a GPU slot. Do not upload them early or alter
            // their stream sidecars: delay, birth time, token links and volume stay intact.
            for (int i = 0; i < pendingSpawnCount; i++)
            {
                if (spawnCommands[i].VesselId != owner) continue;
                spawnCommands[i].Velocity += parameters.Evaluate(spawnCommands[i].Position);
                spawnCommands[i].VesselId = 0;
            }
            // Releases active and GPU-reserved slots, invalidates cached surface history
            // and advances the state version. Body.OnDisable may safely release it again.
            ReleaseOwner(owner);
        }

        // Reuse the completed flow snapshot; never synchronously read GPU data
        // for a break. Reproject its centroid from the captured vessel pose.
        private Vector2 CompletedSplashCenter(uint owner, Vector2 fallback)
        {
            if (!IsOperational || !useCohesivePhysics || Ledger == null || Ledger.Generation != generation
                || world == null || settings == null || snapshotParticles == null || garnishFlowFrames == null
                || !garnishFlowFrames.TryGetValue(owner, out var frame)
                || simulationTime - Ledger.SimulationTime > settings.solidMaximumSnapshotAge) return fallback;
            Vector2 weighted = Vector2.zero; float ml = 0;
            foreach (var p in snapshotParticles)
                if (p.Active != 0 && p.VesselId == owner && p.VolumeMl > 0
                    && FluidExperimentSplashProfile.Finite(p.Position))
                { weighted += p.Position * p.VolumeMl; ml += p.VolumeMl; }
            if (ml <= .00001f || !float.IsFinite(ml) || !FluidExperimentSplashProfile.Finite(weighted)) return fallback;
            Vector2 center = weighted / ml;
            foreach (var body in world.Items)
                if (body.Id == owner)
                {
                    float angle = (body.Angle - frame.angle) * Mathf.Deg2Rad;
                    Vector2 offset = center - frame.position;
                    return body.Position + new Vector2(Mathf.Cos(angle)*offset.x-Mathf.Sin(angle)*offset.y,
                        Mathf.Sin(angle)*offset.x+Mathf.Cos(angle)*offset.y);
                }
            return fallback;
        }
    }
}
