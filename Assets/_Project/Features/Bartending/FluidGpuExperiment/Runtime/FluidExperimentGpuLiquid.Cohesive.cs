using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        [Tooltip("F-only compression, cohesion and shear. Select before Initialize; does not enable E calibration or effects.")]
        public bool useCohesivePhysics;
        public FluidExperimentMaterial cohesiveMaterial = FluidExperimentMaterial.Auto;
        public bool CohesivePhysicsActive => IsOperational && useCohesivePhysics && !useImprovedPhysics
            && activeSolver == FluidExperimentSolver.ReferenceSph;

        private GraphicsBuffer cohesiveStateBuffer, cohesiveMaterialBuffer, cohesiveDiagnosticsBuffer;
        private Vector4[] cohesiveMaterials;
        private int cohesiveDensityKernel, cohesiveDeltaKernel, cohesiveMaterialKernel;
        private int uploadedCohesiveMaterialCount = -1;

        private void CacheCohesiveKernels()
        {
            if (!useCohesivePhysics) return;
            cohesiveDensityKernel = RequireKernel("CalculateCohesiveDensity");
            cohesiveDeltaKernel = RequireKernel("CalculateCohesiveDelta");
            cohesiveMaterialKernel = RequireKernel("CalculateCohesiveMaterialForces");
        }

        private void AllocateCohesiveBuffers()
        {
            if (!useCohesivePhysics) return;
            cohesiveStateBuffer = CreateStructured<Vector4>(particleCapacity);
            cohesiveMaterialBuffer = CreateStructured<Vector4>(maximumIngredients);
            cohesiveDiagnosticsBuffer = CreateStructured<Vector4>(particleCapacity);
            cohesiveMaterials = new Vector4[maximumIngredients];
            cohesiveStateBuffer.SetData(new Vector4[particleCapacity]);
            cohesiveDiagnosticsBuffer.SetData(new Vector4[particleCapacity]);
            cohesiveMaterialBuffer.SetData(cohesiveMaterials);
        }

        private void BindCohesiveParameters()
        {
            if (!useCohesivePhysics) return;
            simulationShader.SetFloat("_CohesiveCompliance", ReferenceFinite(settings.cohesiveDensityCompliance, .000001f, 0, 1));
            simulationShader.SetFloat("_CohesiveCorrectionRatio", ReferenceFinite(settings.cohesiveMaximumCorrectionRatio, .35f, .05f, .75f));
            simulationShader.SetFloat("_CohesiveTension", ReferenceFinite(settings.cohesiveSurfaceTension, 18, 0, 100));
            simulationShader.SetFloat("_CohesiveShear", ReferenceFinite(settings.cohesiveShearViscosity, 1, 0, 10));
            simulationShader.SetFloat("_CohesiveRestRatio", ReferenceFinite(settings.cohesiveRestDistanceRatio, .6f, .35f, .9f));
            simulationShader.SetFloat("_CohesiveWallDensity", ReferenceFinite(settings.cohesiveWallDensity, 0, 0, 1));
            foreach (int kernel in new[] { cohesiveDensityKernel, cohesiveDeltaKernel, cohesiveMaterialKernel })
            {
                BindCommonBuffers(kernel);
                simulationShader.SetBuffer(kernel, "_CohesiveState", cohesiveStateBuffer);
                simulationShader.SetBuffer(kernel, "_CohesiveMaterials", cohesiveMaterialBuffer);
                simulationShader.SetBuffer(kernel, "_CohesiveDiagnostics", cohesiveDiagnosticsBuffer);
            }
        }

        private void UploadCohesiveMaterials()
        {
            bool changed = uploadedCohesiveMaterialCount != ingredients.Count;
            for (int i = 0; i < ingredients.Count; i++)
            {
                FluidExperimentMaterialPreset preset = FluidExperimentMaterials.Resolve(ingredients[i], cohesiveMaterial);
                var value = new Vector4(preset.ViscosityRate, preset.SurfaceTension, 0, 0);
                changed |= !cohesiveMaterials[i].Equals(value);
                cohesiveMaterials[i] = value;
            }
            if (changed && ingredients.Count > 0)
                cohesiveMaterialBuffer.SetData(cohesiveMaterials, 0, 0, ingredients.Count);
            uploadedCohesiveMaterialCount = ingredients.Count;
        }

        private void StepCohesiveFluid()
        {
            UploadCohesiveMaterials();
            RebuildGrid();
            simulationShader.SetInt("_CohesiveResetDiagnostics", 1);
            DispatchForCount(cohesiveDensityKernel, particleCapacity);
            simulationShader.SetInt("_CohesiveResetDiagnostics", 0);
            DispatchForCount(snapshotVelocityKernel, particleCapacity);
            BindCurrentComposition(cohesiveMaterialKernel);
            DispatchForCount(cohesiveMaterialKernel, particleCapacity);
            DispatchForCount(applyKernel, particleCapacity);

            // The fixed integration step and shared continuous collisions ran once before this.
            // These iterations only project compression; they never replay gravity or body motion.
            for (int iteration = 0; iteration < Mathf.Clamp(settings.cohesiveDensityIterations, 0, 12); iteration++)
            {
                RebuildGrid();
                DispatchForCount(cohesiveDensityKernel, particleCapacity);
                DispatchForCount(cohesiveDeltaKernel, particleCapacity);
                DispatchForCount(applyKernel, particleCapacity);
            }
            // Report the final state, rather than the density from before the last correction.
            RebuildGrid();
            DispatchForCount(cohesiveDensityKernel, particleCapacity);
        }

        /// <summary>Last substep per slot: final density/rest density, positive compression,
        /// number of limited corrections, and applied material delta-speed. Explicit blocking diagnostic readback.</summary>
        public Vector4[] ReadCohesiveDiagnostics()
        {
            var result = new Vector4[particleCapacity];
            if (CohesivePhysicsActive && cohesiveDiagnosticsBuffer != null) cohesiveDiagnosticsBuffer.GetData(result);
            return result;
        }

        private void DisposeCohesiveBuffers()
        {
            cohesiveStateBuffer?.Dispose(); cohesiveStateBuffer = null;
            cohesiveMaterialBuffer?.Dispose(); cohesiveMaterialBuffer = null;
            cohesiveDiagnosticsBuffer?.Dispose(); cohesiveDiagnosticsBuffer = null;
            cohesiveMaterials = null;
            uploadedCohesiveMaterialCount = -1;
        }
    }
}
