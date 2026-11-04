// Stream contact lifecycle; never changes particle positions, velocity or composition.
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
bool StreamBlocked(float2 a, float2 b, float radius, uint ignoreSource)
{
    for (int group = 0; group < BoundarySearchGroupCount(); group++)
    {
        uint first, end;
        if (!BoundarySearchRange(group, min(a, b) - radius, max(a, b) + radius, false, first, end)) continue;
        for (uint i = first; i < end; i++)
        {
            BoundarySegment edge = _Boundaries[i];
            // Respect solid boundaries when joining a stream to receiving liquid.
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
