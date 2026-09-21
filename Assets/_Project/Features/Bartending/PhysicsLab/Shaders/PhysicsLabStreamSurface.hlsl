// Runs after simulation. Writes only display buffers, never particle positions or composition.
[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void ResetStreamLookup(uint3 dispatchId : SV_DispatchThreadID)
{
    if (dispatchId.x < (uint)_ParticleCapacity * 2u) _StreamLookup[dispatchId.x] = 0u;
}
bool ResolveStreamToken(uint token, out uint index)
{
    index = 0u;
    if (token == 0u) return false;
    uint2 entry = _StreamLookup[token % ((uint)_ParticleCapacity * 2u)];
    if (entry.x != token || entry.y >= (uint)_ParticleCapacity) return false;
    index = entry.y;
    return _StreamParticles[index].token == token && _StreamParticles[index].detached == 0u
        && _Particles[index].active != 0u && _Particles[index].vesselId == 0u;
}
bool StreamBlocked(float2 a, float2 b, float radius, uint ignoreSource)
{
    for (int i = 0; i < _BoundaryCount; i++)
    {
        BoundarySegment edge = _Boundaries[i];
        // Visible held tools must also occlude a ribbon. Keep the existing physical contact policy separate.
        if ((ignoreSource != 0u && edge.vesselId == ignoreSource) || (edge.flags & 16u) != 0u) continue;
        if (any(max(a, b) + radius < min(edge.a, edge.b)) || any(min(a, b) - radius > max(edge.a, edge.b))) continue;
        float denominator = Cross2D(b - a, edge.b - edge.a);
        if (abs(denominator) > 1e-7)
        {
            float t = Cross2D(edge.a - a, edge.b - edge.a) / denominator;
            float u = Cross2D(edge.a - a, b - a) / denominator;
            if (t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0) return true;
        }
        float unused;
        if (distance(a, ClosestPointOnSegment(a, edge.a, edge.b, unused)) < radius
            || distance(b, ClosestPointOnSegment(b, edge.a, edge.b, unused)) < radius
            || distance(edge.a, ClosestPointOnSegment(edge.a, a, b, unused)) < radius
            || distance(edge.b, ClosestPointOnSegment(edge.b, a, b, unused)) < radius) return true;
    }
    return false;
}
float StreamRadius(uint index)
{
    // Visual continuity only; logical ml and collision radius remain unchanged.
    return _StreamParticles[index].radius * clamp(sqrt(2.4 / max(.5, length(_Particles[index].velocity))), .5, 1.2);
}
[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void BuildStreamSurface(uint3 dispatchId : SV_DispatchThreadID)
{
    uint index = dispatchId.x;
    if (index >= (uint)_ParticleCapacity + 64u) return;
    StreamSegment segment = (StreamSegment)0;
    if (index < (uint)_ParticleCapacity)
    {
        LiquidParticle particle = _Particles[index];
        StreamParticle stream = _StreamParticles[index];
        LiquidParticle display = particle;
        if (_StreamRenderingEnabled != 0 && particle.active != 0u && particle.vesselId == 0u
            && stream.token != 0u && stream.detached == 0u)
        {
            display.active = 0u;
            segment.a = segment.b = particle.position;
            segment.radiusA = segment.radiusB = StreamRadius(index);
            segment.active = 1u; segment.particleIndex = index;
            uint previous;
            if (ResolveStreamToken(stream.previousToken, previous))
            {
                StreamParticle older = _StreamParticles[previous];
                float gap = stream.birthTime - older.birthTime;
                float2 other = _Particles[previous].position;
                float radius = max(segment.radiusA, StreamRadius(previous));
                if (older.streamId == stream.streamId && older.sourceId == stream.sourceId
                    && gap > 0.0 && gap <= _StreamMaximumGap
                    && distance(other, particle.position) <= _StreamMaximumLength
                    && !StreamBlocked(particle.position, other, radius, 0u))
                { segment.b = other; segment.radiusB = StreamRadius(previous); }
            }
        }
        _SurfaceParticles[index] = display;
    }
    else if (_StreamRenderingEnabled != 0 && index - (uint)_ParticleCapacity < (uint)_StreamHeadCount)
    {
        StreamHead head = _StreamHeads[index - (uint)_ParticleCapacity];
        uint newest;
        if (ResolveStreamToken(head.token, newest))
        {
            StreamParticle stream = _StreamParticles[newest];
            float2 position = _Particles[newest].position;
            if (stream.streamId == head.streamId && stream.sourceId == head.sourceId
                && _SimulationTime - stream.birthTime <= _StreamMaximumGap
                && distance(head.lip, position) <= _StreamMaximumLength
                && !StreamBlocked(head.lip, position, head.radius, head.sourceId))
            {
                segment.a = head.lip; segment.b = position;
                segment.radiusA = head.radius; segment.radiusB = StreamRadius(newest);
                segment.active = 1u; segment.particleIndex = newest;
            }
        }
    }
    _StreamSegments[index] = segment;
}
