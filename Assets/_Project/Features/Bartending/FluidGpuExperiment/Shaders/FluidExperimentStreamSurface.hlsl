// Stream lifecycle and display only; never changes particle positions, velocity or composition.
bool StreamBlocked(float2 a, float2 b, float radius, uint ignoreSource);
[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void MergeStreamContacts(uint3 dispatchId : SV_DispatchThreadID)
{
    uint index = dispatchId.x;
    if (index >= (uint)_ParticleCapacity) return;
    LiquidParticle particle = _Particles[index];
    StreamParticle stream = _StreamParticles[index];
    if (particle.active == 0u || particle.vesselId == 0u || stream.token == 0u
        || stream.detached != 0u || stream.stepDt <= 0.000001
        || (particle.stateFlags & STATE_SUSPENDED) != 0u) return;

    // Reuse the final solver grid. Other free-falling stream particles are not
    // a pool: only non-stream liquid or previously contacted liquid can absorb it.
    float contactRadius = min(_SmoothingRadius, _ParticleRadius * 2.2);
    int2 originCell = GetCell(particle.position);
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        int2 cell = originCell + int2(x, y);
        if (!IsCellValid(cell)) continue;
        int neighborIndex = _GridHeads[GetCellIndex(cell)];
        int guard = 0;
        while (neighborIndex >= 0 && guard++ < _ParticleCapacity)
        {
            LiquidParticle neighbor = _Particles[neighborIndex];
            float2 offset = particle.position - neighbor.position;
            if (neighborIndex != (int)index && neighbor.active != 0u
                && neighbor.vesselId == particle.vesselId
                && (neighbor.stateFlags & STATE_SUSPENDED) == 0u
                && (_StreamParticles[neighborIndex].token == 0u || _StreamContactSnapshot[neighborIndex] != 0u)
                && dot(offset, offset) <= contactRadius * contactRadius
                && !StreamBlocked(particle.position, neighbor.position, 0.0, 0u))
            {
                _StreamParticles[index].detached = 2u;
                return;
            }
            neighborIndex = _GridNext[neighborIndex];
        }
    }
}
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
    return _StreamParticles[index].token == token && _Particles[index].active != 0u;
}
bool StreamBlocked(float2 a, float2 b, float radius, uint ignoreSource)
{
    for (int group = 0; group < BoundarySearchGroupCount(); group++)
    {
        uint first, end;
        if (!BoundarySearchRange(group, min(a, b) - radius, max(a, b) + radius, false, first, end)) continue;
        for (uint i = first; i < end; i++)
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
        if (_StreamRenderingEnabled != 0 && particle.active != 0u
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
                uint otherVessel = _Particles[previous].vesselId;
                bool sameVessel = particle.vesselId != 0u && particle.vesselId == otherVessel;
                // A last segment may end on absorbed liquid, but never on a wall-hit
                // particle. The absorbed endpoint itself stays in the metaball surface.
                bool validEndpoint = older.detached == 0u || (older.detached == 2u && sameVessel);
                bool compatibleOwners = particle.vesselId == 0u || otherVessel == 0u || sameVessel;
                float radius = max(segment.radiusA, StreamRadius(previous));
                if (validEndpoint && compatibleOwners && older.streamId == stream.streamId && older.sourceId == stream.sourceId
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
            if (stream.detached == 0u && stream.streamId == head.streamId && stream.sourceId == head.sourceId
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
