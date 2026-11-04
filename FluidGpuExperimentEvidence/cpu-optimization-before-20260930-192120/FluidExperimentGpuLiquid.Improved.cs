using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        private GraphicsBuffer improvedVesselBuffer, improvedMaterialBuffer, improvedDiagnosticsBuffer;
        private readonly Vector4[] improvedVessels = new Vector4[MaximumVesselTriggers];
        private readonly Vector4[] improvedMaterials = new Vector4[32];
        private int improvedVesselCount;
        private int improvedDensityKernel, improvedDeltaKernel, improvedViscosityKernel;
        public FluidExperimentMaterial improvedMaterial = FluidExperimentMaterial.Auto;

        private void CacheImprovedKernels()
        {
            improvedDensityKernel = RequireKernel("CalculateImprovedDensity");
            improvedDeltaKernel = RequireKernel("CalculateImprovedDelta");
            improvedViscosityKernel = RequireKernel("CalculateImprovedViscosity");
        }
        private void AllocateImprovedBuffers()
        {
            improvedVesselBuffer = CreateStructured<Vector4>(MaximumVesselTriggers);
            improvedMaterialBuffer = CreateStructured<Vector4>(32);
            improvedDiagnosticsBuffer = CreateStructured<Vector4>(particleCapacity);
        }
        private void BindImprovedParameters()
        {
            simulationShader.SetInt("_ImprovedPhysics", useImprovedPhysics ? 1 : 0);
            simulationShader.SetFloat("_ImprovedAreaPerMl", ReferenceFinite(settings.improvedAreaPerMl, .00678f, .0001f, 1));
            simulationShader.SetFloat("_ImprovedKernelRatio", ReferenceFinite(settings.improvedKernelRatio, 2.4f, 1.8f, 3.5f));
            simulationShader.SetFloat("_ImprovedCompliance", Mathf.Max(0, settings.improvedDensityCompliance));
            simulationShader.SetFloat("_ImprovedCorrectionRatio", Mathf.Clamp(settings.improvedMaximumCorrectionRatio, .1f, 1));
            simulationShader.SetFloat("_ImprovedWallDensity", Mathf.Clamp01(settings.improvedWallDensity));
            simulationShader.SetFloat("_ImprovedShear", Mathf.Max(0, settings.improvedShearViscosity));
            simulationShader.SetFloat("_ImprovedTension", Mathf.Max(0, settings.improvedSurfaceTension));
            simulationShader.SetFloat("_ImprovedWetting", Mathf.Max(0, settings.improvedWetting));
            foreach (int kernel in new[] { improvedDensityKernel, improvedDeltaKernel, improvedViscosityKernel })
            {
                BindCommonBuffers(kernel);
                simulationShader.SetBuffer(kernel, "_ImprovedVessels", improvedVesselBuffer);
                simulationShader.SetBuffer(kernel, "_ImprovedMaterials", improvedMaterialBuffer);
                simulationShader.SetBuffer(kernel, "_ImprovedDiagnostics", improvedDiagnosticsBuffer);
            }
        }
        private void UploadImprovedParameters()
        {
            improvedVesselCount = 0;
            foreach (FluidExperimentBody body in world.Items)
            {
                if (!body.IsVessel) continue;
                if (improvedVesselCount == improvedVessels.Length) break;
                improvedVessels[improvedVesselCount++] = new Vector4(body.Id, AreaPerMl(body.Id), ImprovedKernelRadius(body.Id), 0);
            }
            if (improvedVesselCount > 0) improvedVesselBuffer.SetData(improvedVessels, 0, 0, improvedVesselCount);
            simulationShader.SetInt("_ImprovedVesselCount", improvedVesselCount);
            for (int i = 0; i < ingredients.Count; i++)
            {
                FluidExperimentMaterialPreset preset = FluidExperimentMaterials.Resolve(ingredients[i], improvedMaterial);
                improvedMaterials[i] = new Vector4(preset.ViscosityRate, preset.SurfaceTension, preset.Wetting, preset.FoamLifetime);
            }
            if (ingredients.Count > 0) improvedMaterialBuffer.SetData(improvedMaterials, 0, 0, ingredients.Count);
        }
        private void StepImprovedFluid()
        {
            UploadImprovedParameters();
            RebuildGrid();
            DispatchForCount(improvedDensityKernel, particleCapacity);
            DispatchForCount(snapshotVelocityKernel, particleCapacity);
            BindCurrentComposition(improvedViscosityKernel);
            DispatchForCount(improvedViscosityKernel, particleCapacity);
            DispatchForCount(applyKernel, particleCapacity);
            for (int iteration = 0; iteration < Mathf.Clamp(settings.improvedSolverIterations, 2, 16); iteration++)
            {
                RebuildGrid();
                DispatchForCount(improvedDensityKernel, particleCapacity);
                DispatchForCount(improvedDeltaKernel, particleCapacity);
                DispatchForCount(applyKernel, particleCapacity);
            }
        }
        public Vector4[] ReadImprovedDiagnostics()
        {
            var result = new Vector4[particleCapacity];
            if (IsOperational && useImprovedPhysics) improvedDiagnosticsBuffer.GetData(result);
            return result;
        }
        private void DisposeImprovedBuffers()
        {
            improvedVesselBuffer?.Dispose(); improvedVesselBuffer = null;
            improvedMaterialBuffer?.Dispose(); improvedMaterialBuffer = null;
            improvedDiagnosticsBuffer?.Dispose(); improvedDiagnosticsBuffer = null;
        }
    }
}
