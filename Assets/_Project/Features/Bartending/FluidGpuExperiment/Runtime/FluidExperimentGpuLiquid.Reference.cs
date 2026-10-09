using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum FluidExperimentSolver { BaselinePbf, ReferenceSph }

    public sealed partial class FluidExperimentGpuLiquid
    {
        [Tooltip("Select before Initialize. Dispose and Initialize again to change solver; the comparison controller resets both particles and settings.")]
        public FluidExperimentSolver solver = FluidExperimentSolver.ReferenceSph;
        public bool useImprovedPhysics;

        private FluidExperimentSolver activeSolver;
        private GraphicsBuffer referenceDensityPressureBuffer;
        private int referenceDensityKernel, referenceDeltaKernel;
        public FluidExperimentSolver ActiveSolver => activeSolver;

        private float SolverSmoothingRadius => useImprovedPhysics
            ? ReferenceFinite(settings.improvedMaximumKernelRadius, .3f, .05f, 1f)
            : activeSolver == FluidExperimentSolver.ReferenceSph
            ? Mathf.Max(Radius * 2, ReferenceFinite(settings.referenceSmoothingRadius, .22f, .02f, 2f))
            : Mathf.Max(Radius * 2, settings.gpuLiquidSmoothingRadius);

        private static float ReferenceFinite(float value, float fallback, float min, float max)
            => Mathf.Clamp(float.IsNaN(value) || float.IsInfinity(value) ? fallback : value, min, max);

        private void CacheReferenceKernels()
        {
            referenceDensityKernel = RequireKernel("CalculateReferenceDensityPressure");
            referenceDeltaKernel = RequireKernel("CalculateReferenceDelta");
            CacheImprovedKernels();
            CacheCohesiveKernels();
        }

        private void AllocateReferenceBuffers()
        {
            referenceDensityPressureBuffer = CreateStructured<Vector4>(particleCapacity);
            AllocateImprovedBuffers();
            AllocateCohesiveBuffers();
        }

        private void BindReferenceParameters()
        {
            simulationShader.SetInt("_ReferenceSolverEnabled", activeSolver == FluidExperimentSolver.ReferenceSph ? 1 : 0);
            // Explicitly clear the F contact branch when returning to any A-E mode.
            simulationShader.SetInt("_CohesiveIceContacts", useCohesivePhysics && !useImprovedPhysics
                && activeSolver == FluidExperimentSolver.ReferenceSph ? 1 : 0);
            simulationShader.SetFloat("_ReferenceParticleVolume", ReferenceFinite(settings.gpuLiquidParticleVolumeMl, .5f, .001f, 100f));
            simulationShader.SetFloat("_ReferenceRestDensity", ReferenceFinite(settings.referenceRestDensity, 2.8f, .1f, 32f));
            simulationShader.SetFloat("_ReferencePressureStiffness", ReferenceFinite(settings.referencePressureStiffness, 40f, 0f, 1000f));
            simulationShader.SetFloat("_ReferenceNearPressureStiffness", ReferenceFinite(settings.referenceNearPressureStiffness, 80f, 0f, 2000f));
            simulationShader.SetFloat("_ReferenceViscosityRate", ReferenceFinite(settings.referenceViscosityRate, 8f, 0f, 100f));
            simulationShader.SetFloat("_ReferenceMaximumTensionRatio", ReferenceFinite(settings.referenceMaximumTensionRatio, .25f, 0f, 1f));
            simulationShader.SetFloat("_ReferenceMaximumAcceleration", ReferenceFinite(settings.referenceMaximumAcceleration, 250f, 1f, 2000f));
            simulationShader.SetFloat("_ReferenceMaximumDisplacementRatio", ReferenceFinite(settings.referenceMaximumDisplacementRatio, .5f, .05f, .75f));
            BindCommonBuffers(referenceDensityKernel);
            BindCommonBuffers(referenceDeltaKernel);
            BindImprovedParameters();
            BindCohesiveParameters();
        }

        private void StepReferenceFluid()
        {
            if (useImprovedPhysics) { StepImprovedFluid(); return; }
            if (useCohesivePhysics) { StepCohesiveFluid(); return; }
            // Integration and continuous boundary sweeps already ran. Gather from a stable
            // state, write only per-particle outputs, then apply the shared wall/owner rules.
            // One explicit pressure update per fixed substep; no PBF or movement-driven steps.
            RebuildGrid();
            DispatchForCount(snapshotVelocityKernel, particleCapacity);
            DispatchForCount(referenceDensityKernel, particleCapacity);
            DispatchForCount(referenceDeltaKernel, particleCapacity);
            DispatchForCount(applyKernel, particleCapacity);
        }

        private void DisposeReferenceBuffers()
        {
            referenceDensityPressureBuffer?.Dispose();
            referenceDensityPressureBuffer = null;
            DisposeImprovedBuffers();
            DisposeCohesiveBuffers();
        }
    }
}
