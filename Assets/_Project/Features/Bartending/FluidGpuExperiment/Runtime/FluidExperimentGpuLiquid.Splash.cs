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
    }
}
