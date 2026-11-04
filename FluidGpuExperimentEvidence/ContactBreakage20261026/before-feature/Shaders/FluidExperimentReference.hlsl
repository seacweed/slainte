// Experimental GPU gather formulation inspired by David Westberg's Unity2DFluidSim:
// https://github.com/Zombie1111/Unity2DFluidSim/tree/73abe0aeae35ba717146e0195f2c2c3b73663e89
// q^2 density, q^3 near density, summed pair pressures and approaching-only
// radial viscosity. World units/seconds and volume weights replace its scaled
// timestep and sequential neighbor writes. This is not a bit-identical CPU port.

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

[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void CalculateReferenceDensityPressure(uint3 dispatchId : SV_DispatchThreadID)
{
    uint index = dispatchId.x;
    if (index >= (uint)_ParticleCapacity) return;
    LiquidParticle particle = _Particles[index];
    if (!ReferenceParticleActive(index, particle))
    {
        _ReferenceDensityPressure[index] = 0.0;
        return;
    }

    float selfMass = ReferenceMass(particle);
    float density = selfMass;
    float nearDensity = selfMass;
    float viscosityWeight = 0.0;
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
            if (neighborIndex != (int)index)
            {
                LiquidParticle neighbor = _Particles[neighborIndex];
                if (ReferenceParticleActive((uint)neighborIndex, neighbor)
                    && neighbor.vesselId == particle.vesselId)
                {
                    float distance = length(particle.position - neighbor.position);
                    float q = saturate(1.0 - distance / _SmoothingRadius);
                    float neighborMass = ReferenceMass(neighbor);
                    density += neighborMass * q * q;
                    nearDensity += neighborMass * q * q * q;
                    viscosityWeight += q * (2.0 * neighborMass / (selfMass + neighborMass));
                }
            }
            neighborIndex = _GridNext[neighborIndex];
        }
    }

    float pressure = _ReferencePressureStiffness * (density - _ReferenceRestDensity);
    float minimumPressure = -_ReferencePressureStiffness * _ReferenceRestDensity
        * _ReferenceMaximumTensionRatio;
    pressure = max(minimumPressure, pressure);
    _ReferenceDensityPressure[index] = float4(pressure,
        _ReferenceNearPressureStiffness * nearDensity, viscosityWeight, density);
}

[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void CalculateReferenceDelta(uint3 dispatchId : SV_DispatchThreadID)
{
    uint index = dispatchId.x;
    if (index >= (uint)_ParticleCapacity) return;
    LiquidParticle particle = _Particles[index];
    if (!ReferenceParticleActive(index, particle))
    {
        _PositionDeltas[index] = 0.0;
        return;
    }

    float dt = _StreamParticles[index].stepDt;
    float selfMass = ReferenceMass(particle);
    float4 selfState = _ReferenceDensityPressure[index];
    float2 selfVelocity = _VelocitySnapshot[index];
    float2 acceleration = 0.0;
    float2 viscosityDeltaVelocity = 0.0;
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
            if (neighborIndex != (int)index)
            {
                LiquidParticle neighbor = _Particles[neighborIndex];
                if (ReferenceParticleActive((uint)neighborIndex, neighbor)
                    && neighbor.vesselId == particle.vesselId)
                {
                    float2 offset = particle.position - neighbor.position;
                    float distance = length(offset);
                    if (distance < _SmoothingRadius)
                    {
                        // Keep overlapping particles separable with antisymmetric
                        // pair directions, without arbitrary energy-producing splash.
                        float2 direction = normalize(GetPairSeparationOffset(index, (uint)neighborIndex, offset));
                        float q = 1.0 - distance / _SmoothingRadius;
                        float neighborMass = ReferenceMass(neighbor);
                        float4 neighborState = _ReferenceDensityPressure[neighborIndex];
                        float pairDt = min(dt, _StreamParticles[neighborIndex].stepDt);
                        float pairPressure = (selfState.x + neighborState.x) * q * q
                            + (selfState.y + neighborState.y) * q * q * q;
                        acceleration += direction * pairPressure * neighborMass * (pairDt / dt);

                        float closingSpeed = max(0.0,
                            -dot(selfVelocity - _VelocitySnapshot[neighborIndex], direction));
                        // Symmetric pair normalization bounds the total gather
                        // impulse while preserving pair momentum for unequal ml.
                        float normalization = max(1.0, max(selfState.z, neighborState.z));
                        float blend = 1.0 - exp(-_ReferenceViscosityRate * pairDt);
                        float massShare = neighborMass / (selfMass + neighborMass);
                        viscosityDeltaVelocity += direction * closingSpeed * q
                            * blend * massShare / normalization;
                    }
                }
            }
            neighborIndex = _GridNext[neighborIndex];
        }
    }

    float accelerationLength = length(acceleration);
    if (accelerationLength > _ReferenceMaximumAcceleration)
        acceleration *= _ReferenceMaximumAcceleration / accelerationLength;
    // Apply force as a position increment so the existing contact projection and
    // post-contact velocity reconstruction remain the only owners of wall response.
    float2 delta = acceleration * dt * dt + viscosityDeltaVelocity * dt;
    float maximumDelta = _ParticleRadius * _ReferenceMaximumDisplacementRatio;
    float deltaLength = length(delta);
    if (deltaLength > maximumDelta) delta *= maximumDelta / deltaLength;
    _PositionDeltas[index] = all(isfinite(delta)) ? delta : float2(0.0, 0.0);
}

#include "FluidExperimentImproved.hlsl"
