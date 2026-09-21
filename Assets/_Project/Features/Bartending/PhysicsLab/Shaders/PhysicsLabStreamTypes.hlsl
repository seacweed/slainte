#ifndef PHYSICSLAB_STREAM_TYPES
#define PHYSICSLAB_STREAM_TYPES
struct StreamParticle
{
    uint token, previousToken, streamId, sourceId;
    float delay, stepDt, startFraction, birthTime;
    float radius;
    uint detached, pending, padding;
};
struct StreamHead
{
    float2 lip;
    uint token, streamId, sourceId;
    float radius;
};
struct StreamSegment
{
    float2 a, b;
    float radiusA, radiusB;
    uint active, particleIndex;
};
#endif
