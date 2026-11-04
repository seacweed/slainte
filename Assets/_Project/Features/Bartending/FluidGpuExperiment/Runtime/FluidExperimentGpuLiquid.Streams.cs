using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        private GraphicsBuffer streamParticleBuffer, spawnStreamBuffer, streamContactSnapshotBuffer;
        private GpuLiquidStreamParticle[] spawnStreams;
        private int mergeStreamContactsKernel;
        private uint nextStreamId, nextStreamToken;
        private float simulationTime;

        public uint NewPourStream() { if (++nextStreamId == 0) ++nextStreamId; return nextStreamId; }
        public bool TryEmitStream(Vector2 position, Vector2 velocity, ItemDef ingredient, float volume,
            uint source, uint stream, uint previousToken, float birthDelay, float radius, out uint token)
        {
            token = 0;
            if (!TryEmit(position, velocity, ingredient, volume, 0)) return false;
            if (++nextStreamToken == 0) ++nextStreamToken;
            token = nextStreamToken;
            spawnStreams[pendingSpawnCount - 1] = new GpuLiquidStreamParticle { Token = token,
                PreviousToken = previousToken, StreamId = stream, SourceId = source,
                Delay = Mathf.Max(0, birthDelay), BirthTime = simulationTime + Mathf.Max(0, birthDelay),
                Radius = Mathf.Max(.005f, radius), Pending = 1 };
            return true;
        }
        private void AllocateStreamBuffers()
        {
            mergeStreamContactsKernel = RequireKernel("MergeStreamContacts");
            streamParticleBuffer = CreateStructured<GpuLiquidStreamParticle>(particleCapacity);
            spawnStreamBuffer = CreateStructured<GpuLiquidStreamParticle>(particleCapacity);
            streamContactSnapshotBuffer = CreateStructured<uint>(particleCapacity);
            spawnStreams = new GpuLiquidStreamParticle[particleCapacity];
        }
        private void BindStreamBuffers(int kernel)
        {
            simulationShader.SetBuffer(kernel, "_StreamParticles", streamParticleBuffer);
            simulationShader.SetBuffer(kernel, "_SpawnStreams", spawnStreamBuffer);
            simulationShader.SetBuffer(kernel, "_StreamContactSnapshot", streamContactSnapshotBuffer);
        }
        public GpuLiquidStreamParticle[] ReadStreamParticles()
        {
            var result = new GpuLiquidStreamParticle[particleCapacity];
            streamParticleBuffer.GetData(result); return result;
        }
        private void DisposeStreamBuffers()
        {
            foreach (GraphicsBuffer buffer in new[] { streamParticleBuffer, spawnStreamBuffer, streamContactSnapshotBuffer }) buffer?.Dispose();
            streamParticleBuffer = spawnStreamBuffer = streamContactSnapshotBuffer = null;
        }
    }
}
