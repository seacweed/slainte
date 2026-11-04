// Shared buffer layout derived from the existing GPU backend.
// This fork is private to FluidExperiment; the production implementation is unchanged.
using System;
using System.Runtime.InteropServices;
using UnityEngine;
namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        private int RequireKernel(string name)
        {
            if (!simulationShader.HasKernel(name))
                throw new InvalidOperationException("FluidExperiment compute kernel is missing: " + name);
            int kernel = simulationShader.FindKernel(name);
            if (!simulationShader.IsSupported(kernel))
                throw new InvalidOperationException("FluidExperiment compute kernel failed compilation or is unsupported: " + name);
            return kernel;
        }

        private void CacheKernels()
        {
            resetKernel = RequireKernel("ResetParticles");
            resetCompositionKernel = RequireKernel("ResetCompositionBuffers");
            spawnKernel = RequireKernel("SpawnParticles");
            integrateKernel = RequireKernel("IntegrateParticles");
            sweepKernel = RequireKernel("SweepCohesiveIceBoundaries");
            snapshotVelocityKernel = RequireKernel("SnapshotVelocities");
            clearGridKernel = RequireKernel("ClearGrid");
            buildGridKernel = RequireKernel("BuildGrid");
            applyKernel = RequireKernel("ApplyCohesiveIceContacts");
            velocityKernel = RequireKernel("UpdateCohesiveContactVelocities");
            colorKernel = RequireKernel("UpdateParticleColors");
            techniqueKernel = RequireKernel("ApplyTechnique");
            translateVesselKernel = RequireKernel("TranslateVesselContents");
            pickupContentsKernel = RequireKernel("CarryPickupContents");
            suspendVesselKernel = RequireKernel("SetVesselSuspended");
            CacheCohesiveKernels();
            CacheLedgerKernels();
            CacheSpoonStirKernels();
        }

        private void AllocateBuffers()
        {
            AllocateStreamBuffers();
            particleBuffer = CreateStructured<GpuLiquidParticle>(particleCapacity);
            compositionA = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCapacity * maximumIngredients,
                sizeof(float));
            compositionB = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCapacity * maximumIngredients,
                sizeof(float));
            particleColorBuffer = CreateStructured<Vector4>(particleCapacity);
            positionDeltaBuffer = CreateStructured<Vector2>(particleCapacity);
            velocitySnapshotBuffer = CreateStructured<Vector2>(particleCapacity);
            lambdaBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCapacity,
                sizeof(float));
            gridHeadBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                gridCellCount,
                sizeof(int));
            gridNextBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCapacity,
                sizeof(int));
            freeIndexBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                particleCapacity,
                sizeof(uint));
            freeCountBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                1,
                sizeof(int));
            spawnCommandBuffer = CreateStructured<GpuLiquidSpawnCommand>(particleCapacity);
            ingredientVisualBuffer = CreateStructured<GpuLiquidIngredientVisual>(maximumIngredients);
            boundaryBuffer = CreateStructured<GpuLiquidBoundarySegment>(MaximumBoundarySegments);
            // Every group owns at least one edge, so the existing edge budget also bounds groups.
            boundaryGroupBuffer = CreateStructured<GpuLiquidBoundaryGroup>(MaximumBoundarySegments);
            boundaryGroups = new GpuLiquidBoundaryGroup[MaximumBoundarySegments];
            triggerBuffer = CreateStructured<GpuLiquidVesselTrigger>(MaximumVesselTriggers);
            iceVesselBudgetBuffer = CreateStructured<GpuIceVesselBudget>(MaximumVesselTriggers);
            iceStepOriginBuffer = CreateStructured<Vector2>(particleCapacity);
            agitatorBuffer = CreateStructured<GpuLiquidAgitator>(MaximumAgitators);
            statisticsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                4,
                sizeof(uint));

            spawnCommands = new GpuLiquidSpawnCommand[particleCapacity];
            ingredientVisuals = new GpuLiquidIngredientVisual[maximumIngredients];
            boundaryUpload = new GpuLiquidBoundarySegment[MaximumBoundarySegments];
            triggerUpload = new GpuLiquidVesselTrigger[MaximumVesselTriggers];
            agitatorUpload = new GpuLiquidAgitator[MaximumAgitators];
            snapshotParticles = new GpuLiquidParticle[particleCapacity];
            snapshotComposition = new float[particleCapacity * maximumIngredients];
            AllocateCohesiveBuffers();
            AllocateLedgerBuffers();
            AllocateSpoonStir();
        }

        private static GraphicsBuffer CreateStructured<T>(int count) where T : struct => new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, Marshal.SizeOf<T>());

        private void BindStaticParameters()
        {
            simulationShader.SetInt("_ParticleCapacity", particleCapacity);
            simulationShader.SetInt("_MaximumIngredients", maximumIngredients);
            simulationShader.SetInt("_GridWidth", gridWidth);
            simulationShader.SetInt("_GridHeight", gridHeight);
            simulationShader.SetInt("_GridCellCount", gridCellCount);
            simulationShader.SetVector("_GridOrigin", settings.gpuLiquidWorldMin);
            simulationShader.SetVector("_WorldMin", settings.gpuLiquidWorldMin);
            simulationShader.SetVector("_WorldMax", settings.gpuLiquidWorldMax);
            simulationShader.SetFloat(
                "_ParticleRadius",
                Mathf.Max(0.01f, settings.gpuLiquidParticleRadius));
            simulationShader.SetFloat(
                "_SmoothingRadius",
                SolverSmoothingRadius);
            simulationShader.SetFloat(
                "_WallFriction",
                Mathf.Clamp01(settings.gpuLiquidWallFriction));
            simulationShader.SetFloat(
                "_WallRestitution",
                Mathf.Clamp01(settings.gpuLiquidWallRestitution));
            simulationShader.SetFloat(
                "_VelocityDamping",
                Mathf.Clamp01(settings.gpuLiquidVelocityDamping));
            simulationShader.SetFloat(
                "_MaximumSpeed",
                Mathf.Max(0.1f, settings.gpuLiquidMaximumSpeed));
            simulationShader.SetFloat(
                "_ClosedVesselDampingRate",
                Mathf.Max(0f, settings.gpuLiquidClosedVesselDampingRate));
            simulationShader.SetFloat(
                "_ClosedVesselMaximumRelativeSpeed",
                Mathf.Max(0.1f, settings.gpuLiquidClosedVesselMaximumRelativeSpeed));
            simulationShader.SetFloat(
                "_PassiveMixRate",
                Mathf.Max(0f, settings.gpuLiquidPassiveMixRate));
            simulationShader.SetFloat(
                "_AgitationMixRate",
                Mathf.Max(0f, settings.gpuLiquidAgitationMixRate));
            simulationShader.SetFloat(
                "_FullMixRelativeSpeed",
                Mathf.Max(0.01f, settings.gpuLiquidFullMixRelativeSpeed));
            simulationShader.SetFloat(
                "_MaximumMixPerSubstep",
                Mathf.Clamp(settings.gpuLiquidMaximumMixPerSubstep, 0.01f, 0.5f));
            simulationShader.SetFloat(
                "_MinimumVisibleAlpha",
                Mathf.Clamp01(settings.liquidMinimumVisibleAlpha));

            BindCommonBuffers(resetKernel);
            BindCommonBuffers(resetCompositionKernel);
            BindCommonBuffers(spawnKernel);
            BindCommonBuffers(integrateKernel);
            BindCommonBuffers(sweepKernel);
            BindCommonBuffers(snapshotVelocityKernel);
            BindCommonBuffers(buildGridKernel);
            BindCommonBuffers(applyKernel);
            BindCommonBuffers(velocityKernel);
            BindCommonBuffers(colorKernel);
            BindCommonBuffers(techniqueKernel);
            BindCommonBuffers(translateVesselKernel);
            BindCommonBuffers(pickupContentsKernel);
            BindCommonBuffers(suspendVesselKernel);
            BindCommonBuffers(mergeStreamContactsKernel);
            simulationShader.SetBuffer(clearGridKernel, "_GridHeads", gridHeadBuffer);
            BindLiquidScale();
            BindCohesiveParameters();
            BindCommonBuffers(conservativeMixWeightsKernel);
            BindCommonBuffers(conservativeMixKernel);
            BindCommonBuffers(collectLedgerKernel);
            BindCommonBuffers(transferContentsKernel);
            BindSpoonStir();
        }

        private void BindCommonBuffers(int kernel)
        {
            BindStreamBuffers(kernel);
            simulationShader.SetBuffer(kernel, "_Particles", particleBuffer);
            simulationShader.SetBuffer(kernel, "_CompositionA", compositionA);
            simulationShader.SetBuffer(kernel, "_CompositionB", compositionB);
            simulationShader.SetBuffer(kernel, "_ParticleColors", particleColorBuffer);
            simulationShader.SetBuffer(kernel, "_PositionDeltas", positionDeltaBuffer);
            simulationShader.SetBuffer(kernel, "_VelocitySnapshot", velocitySnapshotBuffer);
            simulationShader.SetBuffer(kernel, "_VelocityRead", velocitySnapshotBuffer);
            simulationShader.SetBuffer(kernel, "_Lambdas", lambdaBuffer);
            simulationShader.SetBuffer(kernel, "_GridHeads", gridHeadBuffer);
            simulationShader.SetBuffer(kernel, "_GridNext", gridNextBuffer);
            simulationShader.SetBuffer(kernel, "_FreeIndices", freeIndexBuffer);
            simulationShader.SetBuffer(kernel, "_FreeIndicesRead", freeIndexBuffer);
            simulationShader.SetBuffer(kernel, "_FreeCount", freeCountBuffer);
            simulationShader.SetBuffer(kernel, "_SpawnCommands", spawnCommandBuffer);
            simulationShader.SetBuffer(kernel, "_IngredientVisuals", ingredientVisualBuffer);
            simulationShader.SetBuffer(kernel, "_Boundaries", boundaryBuffer);
            simulationShader.SetBuffer(kernel, "_BoundaryGroups", boundaryGroupBuffer);
            simulationShader.SetBuffer(kernel, "_VesselTriggers", triggerBuffer);
            simulationShader.SetBuffer(kernel, "_IceVesselBudgets", iceVesselBudgetBuffer);
            simulationShader.SetBuffer(kernel, "_IceStepOrigins", iceStepOriginBuffer);
            simulationShader.SetBuffer(kernel, "_IceStepOriginsRead", iceStepOriginBuffer);
            simulationShader.SetBuffer(kernel, "_Agitators", agitatorBuffer);
            simulationShader.SetBuffer(kernel, "_Statistics", statisticsBuffer);
            BindLedgerBuffers(kernel);
        }

        private void DispatchReset()
        {
            simulationTime = 0;
            compositionAIsCurrent = true;
            pendingSpawnCount = 0;
            activeParticleCount = 0;
            ResetLedgerState();
            ResetSpoonStir();
            DispatchForCount(resetKernel, particleCapacity);
            DispatchForCount(resetCompositionKernel, particleCapacity);
        }

        private void RebuildGrid()
        {
            DispatchForCount(clearGridKernel, gridCellCount);
            DispatchForCount(buildGridKernel, particleCapacity);
        }

        private void DispatchMix()
        {
            DispatchForCount(conservativeMixWeightsKernel, particleCapacity);
            GraphicsBuffer read = compositionAIsCurrent ? compositionA : compositionB;
            GraphicsBuffer write = compositionAIsCurrent ? compositionB : compositionA;
            simulationShader.SetBuffer(conservativeMixKernel, "_CompositionRead", read);
            simulationShader.SetBuffer(conservativeMixKernel, "_CompositionWrite", write);
            DispatchForCount(conservativeMixKernel, particleCapacity);
            compositionAIsCurrent = !compositionAIsCurrent;
        }

        private void BindCurrentComposition(int kernel)
        {
            simulationShader.SetBuffer(
                kernel,
                "_CompositionRead",
                compositionAIsCurrent ? compositionA : compositionB);
        }

        private void DispatchForCount(int kernel, int count)
        {
            if (count <= 0)
                return;
            simulationShader.Dispatch(
                kernel,
                Mathf.CeilToInt(count / (float)ThreadGroupSize),
                1,
                1);
        }
    }
}
