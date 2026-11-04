// Shared particle activity and volume weighting used by cohesive liquid.
bool ReferenceParticleActive(uint index, LiquidParticle particle)
{
    return particle.active != 0u && (particle.stateFlags & STATE_SUSPENDED) == 0u
        && _StreamParticles[index].stepDt > 0.000001;
}

float ReferenceMass(LiquidParticle particle)
{
    // The existing ml value is authoritative, including a partial final drop.
    return max(0.000001, particle.volumeMl / max(0.001, _ReferenceParticleVolume));
}
