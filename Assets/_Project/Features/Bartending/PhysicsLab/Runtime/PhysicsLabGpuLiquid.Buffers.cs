// Buffer layout and PBF parameter bindings derived from the existing GPU backend.
// This fork is private to PhysicsLab; the production implementation is unchanged.
using System;
using System.Runtime.InteropServices;
using UnityEngine;
namespace Slainte.Bartending.PhysicsLab
{
    public sealed partial class PhysicsLabGpuLiquid
    {
        private void CacheKernels()
        {
            resetKernel = simulationShader.FindKernel("ResetParticles");
            resetCompositionKernel = simulationShader.FindKernel("ResetCompositionBuffers");
            spawnKernel = simulationShader.FindKernel("SpawnParticles");
            integrateKernel = simulationShader.FindKernel("IntegrateParticles");
            sweepKernel = simulationShader.FindKernel("SweepBoundaries");
            snapshotVelocityKernel = simulationShader.FindKernel("SnapshotVelocities");
            clearGridKernel = simulationShader.FindKernel("ClearGrid");
            buildGridKernel = simulationShader.FindKernel("BuildGrid");
            lambdaKernel = simulationShader.FindKernel("CalculateDensityLambda");
            deltaKernel = simulationShader.FindKernel("CalculatePositionDelta");
            applyKernel = simulationShader.FindKernel("ApplyDeltaAndBoundaries");
            velocityKernel = simulationShader.FindKernel("UpdateVelocities");
            mixKernel = simulationShader.FindKernel("MixComposition");
            colorKernel = simulationShader.FindKernel("UpdateParticleColors");
            techniqueKernel = simulationShader.FindKernel("ApplyTechnique");
            translateVesselKernel = simulationShader.FindKernel("TranslateVesselContents");
            suspendVesselKernel = simulationShader.FindKernel("SetVesselSuspended");
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
            triggerBuffer = CreateStructured<GpuLiquidVesselTrigger>(MaximumVesselTriggers);
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
                Mathf.Max(
                    settings.gpuLiquidParticleRadius * 2f,
                    settings.gpuLiquidSmoothingRadius));
            simulationShader.SetFloat(
                "_RestDensity",
                Mathf.Max(0.01f, settings.gpuLiquidRestDensity));
            simulationShader.SetFloat(
                "_DensityCompliance",
                Mathf.Max(0f, settings.gpuLiquidDensityCompliance));
            simulationShader.SetFloat(
                "_LambdaEpsilon",
                Mathf.Max(0.000001f, settings.gpuLiquidLambdaEpsilon));
            simulationShader.SetFloat(
                "_ArtificialPressureStrength",
                Mathf.Clamp(settings.gpuLiquidArtificialPressureStrength, 0f, 0.02f));
            simulationShader.SetFloat(
                "_ArtificialPressureRadiusRatio",
                Mathf.Clamp(settings.gpuLiquidArtificialPressureRadiusRatio, 0.1f, 0.9f));
            simulationShader.SetFloat(
                "_BoundaryDensityScale",
                Mathf.Clamp(settings.gpuLiquidBoundaryDensityScale, 0f, 12f));
            simulationShader.SetFloat(
                "_WallFriction",
                Mathf.Clamp01(settings.gpuLiquidWallFriction));
            simulationShader.SetFloat(
                "_WallRestitution",
                Mathf.Clamp01(settings.gpuLiquidWallRestitution));
            simulationShader.SetFloat(
                "_SplashTransfer",
                Mathf.Clamp01(settings.gpuLiquidSplashTransfer));
            simulationShader.SetFloat(
                "_SplashMinimumImpactSpeed",
                Mathf.Max(0f, settings.gpuLiquidSplashMinimumImpactSpeed));
            simulationShader.SetFloat(
                "_BottomImpactSpread",
                Mathf.Clamp01(settings.gpuLiquidBottomImpactSpread));
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
                "_Viscosity",
                Mathf.Clamp01(settings.gpuLiquidViscosity));
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
            BindCommonBuffers(lambdaKernel);
            BindCommonBuffers(deltaKernel);
            BindCommonBuffers(applyKernel);
            BindCommonBuffers(velocityKernel);
            BindCommonBuffers(mixKernel);
            BindCommonBuffers(colorKernel);
            BindCommonBuffers(techniqueKernel);
            BindCommonBuffers(translateVesselKernel);
            BindCommonBuffers(suspendVesselKernel);
            BindCommonBuffers(streamSurfaceKernel);
            BindCommonBuffers(mergeStreamContactsKernel);
            BindStreamBuffers(resetStreamLookupKernel);
            simulationShader.SetBuffer(clearGridKernel, "_GridHeads", gridHeadBuffer);
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
            simulationShader.SetBuffer(kernel, "_Lambdas", lambdaBuffer);
            simulationShader.SetBuffer(kernel, "_GridHeads", gridHeadBuffer);
            simulationShader.SetBuffer(kernel, "_GridNext", gridNextBuffer);
            simulationShader.SetBuffer(kernel, "_FreeIndices", freeIndexBuffer);
            simulationShader.SetBuffer(kernel, "_FreeCount", freeCountBuffer);
            simulationShader.SetBuffer(kernel, "_SpawnCommands", spawnCommandBuffer);
            simulationShader.SetBuffer(kernel, "_IngredientVisuals", ingredientVisualBuffer);
            simulationShader.SetBuffer(kernel, "_Boundaries", boundaryBuffer);
            simulationShader.SetBuffer(kernel, "_VesselTriggers", triggerBuffer);
            simulationShader.SetBuffer(kernel, "_Agitators", agitatorBuffer);
            simulationShader.SetBuffer(kernel, "_Statistics", statisticsBuffer);
        }

        private void DispatchReset()
        {
            simulationTime = 0;
            compositionAIsCurrent = true;
            pendingSpawnCount = 0;
            activeParticleCount = 0;
            DispatchForCount(resetKernel, particleCapacity);
            DispatchForCount(resetCompositionKernel, particleCapacity);
            DispatchForCount(resetStreamLookupKernel, particleCapacity * 2);
        }

        private void RebuildGrid()
        {
            DispatchForCount(clearGridKernel, gridCellCount);
            DispatchForCount(buildGridKernel, particleCapacity);
        }

        private void DispatchMix()
        {
            GraphicsBuffer read = compositionAIsCurrent ? compositionA : compositionB;
            GraphicsBuffer write = compositionAIsCurrent ? compositionB : compositionA;
            simulationShader.SetBuffer(mixKernel, "_CompositionRead", read);
            simulationShader.SetBuffer(mixKernel, "_CompositionWrite", write);
            DispatchForCount(mixKernel, particleCapacity);
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
