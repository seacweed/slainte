using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        // Preserve the cohesive solver's authored neighborhood and density scale.
        private float SolverSmoothingRadius => Mathf.Max(Radius * 2,
            ReferenceFinite(settings.referenceSmoothingRadius, .22f, .02f, 2f));

        private static float ReferenceFinite(float value, float fallback, float min, float max)
            => Mathf.Clamp(float.IsNaN(value) || float.IsInfinity(value) ? fallback : value, min, max);

        private void BindLiquidScale()
        {
            simulationShader.SetFloat("_ReferenceParticleVolume", ReferenceFinite(settings.gpuLiquidParticleVolumeMl, .5f, .001f, 100f));
            simulationShader.SetFloat("_ReferenceRestDensity", ReferenceFinite(settings.referenceRestDensity, 2.8f, .1f, 32f));
        }
    }
}
