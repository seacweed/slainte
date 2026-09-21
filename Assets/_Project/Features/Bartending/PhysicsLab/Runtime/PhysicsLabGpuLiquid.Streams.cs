using UnityEngine;
using UnityEngine.Rendering;

namespace Slainte.Bartending.PhysicsLab
{
    public sealed partial class PhysicsLabGpuLiquid
    {
        public bool useStreamRendering = true;
        private const int MaximumStreamHeads = 64;
        private GraphicsBuffer streamParticleBuffer, spawnStreamBuffer, streamLookupBuffer, streamHeadBuffer,
            streamSegmentBuffer, surfaceParticleBuffer, streamContactSnapshotBuffer;
        private GpuLiquidStreamParticle[] spawnStreams;
        private readonly GpuLiquidStreamHead[] streamHeads = new GpuLiquidStreamHead[MaximumStreamHeads];
        private int streamSurfaceKernel, resetStreamLookupKernel, mergeStreamContactsKernel;
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
            streamSurfaceKernel = simulationShader.FindKernel("BuildStreamSurface");
            resetStreamLookupKernel = simulationShader.FindKernel("ResetStreamLookup");
            mergeStreamContactsKernel = simulationShader.FindKernel("MergeStreamContacts");
            streamParticleBuffer = CreateStructured<GpuLiquidStreamParticle>(particleCapacity);
            spawnStreamBuffer = CreateStructured<GpuLiquidStreamParticle>(particleCapacity);
            streamLookupBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, particleCapacity * 2, sizeof(uint) * 2);
            streamHeadBuffer = CreateStructured<GpuLiquidStreamHead>(MaximumStreamHeads);
            streamSegmentBuffer = CreateStructured<GpuLiquidStreamSegment>(particleCapacity + MaximumStreamHeads);
            surfaceParticleBuffer = CreateStructured<GpuLiquidParticle>(particleCapacity);
            streamContactSnapshotBuffer = CreateStructured<uint>(particleCapacity);
            spawnStreams = new GpuLiquidStreamParticle[particleCapacity];
        }
        private void BindStreamBuffers(int kernel)
        {
            simulationShader.SetBuffer(kernel, "_StreamParticles", streamParticleBuffer);
            simulationShader.SetBuffer(kernel, "_SpawnStreams", spawnStreamBuffer);
            simulationShader.SetBuffer(kernel, "_StreamLookup", streamLookupBuffer);
            simulationShader.SetBuffer(kernel, "_StreamHeads", streamHeadBuffer);
            simulationShader.SetBuffer(kernel, "_StreamSegments", streamSegmentBuffer);
            simulationShader.SetBuffer(kernel, "_SurfaceParticles", surfaceParticleBuffer);
            simulationShader.SetBuffer(kernel, "_StreamContactSnapshot", streamContactSnapshotBuffer);
        }
        private void PrepareStreamSurface(CommandBuffer commands = null)
        {
            int count = 0;
            foreach (PhysicsLabBody item in world.Items)
                if (item != null && count < MaximumStreamHeads && item.TryGetStreamHead(out GpuLiquidStreamHead head))
                    streamHeads[count++] = head;
            if (count > 0) streamHeadBuffer.SetData(streamHeads, 0, 0, count);
            simulationShader.SetInt("_StreamHeadCount", count);
            simulationShader.SetInt("_StreamRenderingEnabled", useStreamRendering ? 1 : 0);
            simulationShader.SetFloat("_SimulationTime", simulationTime);
            simulationShader.SetFloat("_StreamMaximumGap", Mathf.Max(.01f, settings.streamMaximumTimeGap));
            simulationShader.SetFloat("_StreamMaximumLength", Mathf.Max(.05f, settings.streamMaximumLength));
            int groups = Mathf.CeilToInt((particleCapacity + MaximumStreamHeads) / (float)ThreadGroupSize);
            if (commands == null) simulationShader.Dispatch(streamSurfaceKernel, groups, 1, 1);
            else commands.DispatchCompute(simulationShader, streamSurfaceKernel, groups, 1, 1);
        }
        public GpuLiquidStreamParticle[] ReadStreamParticles()
        {
            var result = new GpuLiquidStreamParticle[particleCapacity];
            streamParticleBuffer.GetData(result); return result;
        }
        public GpuLiquidStreamSegment[] ReadStreamSegments()
        {
            PrepareStreamSurface();
            var result = new GpuLiquidStreamSegment[particleCapacity + MaximumStreamHeads];
            streamSegmentBuffer.GetData(result); return result;
        }
        private void DisposeStreamBuffers()
        {
            foreach (GraphicsBuffer buffer in new[] { streamParticleBuffer, spawnStreamBuffer, streamLookupBuffer,
                streamHeadBuffer, streamSegmentBuffer, surfaceParticleBuffer, streamContactSnapshotBuffer }) buffer?.Dispose();
            streamParticleBuffer = spawnStreamBuffer = streamLookupBuffer = streamHeadBuffer = streamSegmentBuffer = surfaceParticleBuffer = streamContactSnapshotBuffer = null;
        }
    }
}
