// Groups retain the original edge order and geometry. The linear variant is a
// validation reference; gameplay uses the conservative per-body broad phase.
struct BoundaryGroup
{
    float2 minPosition, maxPosition, sweptMin, sweptMax;
    uint first, end, contourFirst, contourEnd, vesselId;
};
StructuredBuffer<BoundaryGroup> _BoundaryGroups;
int _BoundaryGroupCount;

int BoundarySearchGroupCount()
{
#if defined(PHYSICSLAB_LINEAR_BOUNDARIES)
    return 1;
#else
    return _BoundaryGroupCount;
#endif
}

bool BoundarySearchRange(int groupIndex, float2 queryMin, float2 queryMax, bool swept, out uint first, out uint end)
{
#if defined(PHYSICSLAB_LINEAR_BOUNDARIES)
    first = 0u; end = (uint)_BoundaryCount;
    return true;
#else
    BoundaryGroup group = _BoundaryGroups[groupIndex];
    first = group.first; end = group.end;
    float2 boundsMin = swept ? group.sweptMin : group.minPosition;
    float2 boundsMax = swept ? group.sweptMax : group.maxPosition;
    return all(queryMax >= boundsMin) && all(queryMin <= boundsMax);
#endif
}

void VesselContourRange(uint vesselId, out uint first, out uint end)
{
    first = 0u; end = 0u;
#if defined(PHYSICSLAB_LINEAR_BOUNDARIES)
    end = (uint)_BoundaryCount;
#else
    if (vesselId == 0u) return;
    for (int i = 0; i < _BoundaryGroupCount; i++)
    {
        BoundaryGroup group = _BoundaryGroups[i];
        if (group.vesselId != vesselId) continue;
        first = group.contourFirst; end = group.contourEnd;
        return;
    }
#endif
}
