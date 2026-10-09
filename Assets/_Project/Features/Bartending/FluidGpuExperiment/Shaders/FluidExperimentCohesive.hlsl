// F: D-scale q^2 density with compression-only projection, independent finite-range
// cohesion and material shear. No E area calibration, wetting or negative pressure.
RWStructuredBuffer<float4> _CohesiveState; // normalized density, shear weight, compression, cohesion weight
StructuredBuffer<float4> _CohesiveMaterials; // material shear rate, cohesion multiplier, reserved
RWStructuredBuffer<float4> _CohesiveDiagnostics; // final density, compression, limited corrections, material delta-speed
float _CohesiveCompliance, _CohesiveCorrectionRatio, _CohesiveTension;
float _CohesiveShear, _CohesiveRestRatio, _CohesiveWallDensity;
int _CohesiveResetDiagnostics;

bool CohesiveActive(uint index, LiquidParticle particle)
{
    return ReferenceParticleActive(index, particle) && particle.volumeMl > 0
        && isfinite(particle.volumeMl) && all(isfinite(particle.position)) && all(isfinite(particle.velocity));
}

float2 CohesiveGradient(uint i, uint j, float2 offset)
{
    float distance = length(offset);
    float q = saturate(1.0 - distance / _SmoothingRadius);
    // Antisymmetric fallback separates coincident particles without random impulses.
    float2 direction = normalize(GetPairSeparationOffset(i, j, offset));
    return -direction * (2.0 * q / (_SmoothingRadius * _ReferenceRestDensity));
}

float CohesiveAttractionWeight(float distance)
{
    float start = _SmoothingRadius * _CohesiveRestRatio;
    float t = saturate((distance - start) / max(1e-5, _SmoothingRadius - start));
    // Zero inside nominal spacing and outside support. Pressure handles compression;
    // this independent attraction never supplies a short-range repulsive force.
    return 4.0 * t * (1.0 - t);
}

void CohesiveWall(LiquidParticle particle, out float density, out float2 gradient)
{
    density = 0; gradient = 0;
    if (_CohesiveWallDensity <= 0) return;
    float2 contactPoint, normal, velocity; float distance, edgeT;
    if (!TryFindNearestCompatibleBoundary(particle.position, particle.vesselId, _SmoothingRadius,
        contactPoint, normal, distance, edgeT, velocity)) return;
    // Optional local flat-wall surrogate, not an exact q^2 integral or a corner model.
    // At the wall it contributes half the normalized target density and vanishes at h.
    // Default off: F does not silently transplant E's per-vessel effective depth.
    float q = saturate(1.0 - distance / _SmoothingRadius);
    density = .5 * q * q * q * _CohesiveWallDensity;
    gradient = -normal * (1.5 * q * q / _SmoothingRadius) * _CohesiveWallDensity;
}

[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void CalculateCohesiveDensity(uint3 dispatchId : SV_DispatchThreadID)
{
    uint i = dispatchId.x;
    if (i >= (uint)_ParticleCapacity) return;
    LiquidParticle p = _Particles[i];
    if (!CohesiveActive(i, p))
    {
        _Lambdas[i] = 0; _CohesiveState[i] = 0; _CohesiveDiagnostics[i] = 0;
        return;
    }
    float mass = ReferenceMass(p);
    float density = mass / _ReferenceRestDensity;
    float2 selfGradient = 0;
    float gradientSquares = 0, shearWeight = 0, cohesionWeight = 0;
    int2 origin = GetCell(p.position);
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
    {
        int2 cell = origin + int2(x, y);
        if (!IsCellValid(cell)) continue;
        int j = _GridHeads[GetCellIndex(cell)], guard = 0;
        while (j >= 0 && guard++ < _ParticleCapacity)
        {
            LiquidParticle other = _Particles[j];
            if (j != (int)i && CohesiveActive((uint)j, other) && p.vesselId == other.vesselId)
            {
                float2 offset = p.position - other.position;
                float distance = length(offset);
                if (distance < _SmoothingRadius)
                {
                    float otherMass = ReferenceMass(other);
                    float q = 1.0 - distance / _SmoothingRadius;
                    density += otherMass * q * q / _ReferenceRestDensity;
                    float2 gradient = otherMass * CohesiveGradient(i, (uint)j, offset);
                    selfGradient += gradient;
                    gradientSquares += dot(gradient, gradient) / otherMass;
                    float pairShare = 2.0 * otherMass / (mass + otherMass);
                    shearWeight += q * pairShare;
                    cohesionWeight += CohesiveAttractionWeight(distance) * pairShare;
                }
            }
            j = _GridNext[j];
        }
    }
    float wallDensity; float2 wallGradient;
    CohesiveWall(p, wallDensity, wallGradient);
    density += wallDensity; selfGradient += wallGradient;
    gradientSquares += dot(selfGradient, selfGradient) / mass;
    float compression = max(0.0, density - 1.0);
    float dt = _StreamParticles[i].stepDt;
    float lambda = -compression / max(1e-5, gradientSquares + _CohesiveCompliance / max(1e-10, dt * dt));
    _Lambdas[i] = isfinite(lambda) ? lambda : 0;
    _CohesiveState[i] = float4(density, shearWeight, compression, cohesionWeight);
    float4 diagnostics = _CohesiveResetDiagnostics != 0 ? 0 : _CohesiveDiagnostics[i];
    diagnostics.xy = float2(density, compression);
    _CohesiveDiagnostics[i] = diagnostics;
}

[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void CalculateCohesiveDelta(uint3 dispatchId : SV_DispatchThreadID)
{
    uint i = dispatchId.x;
    if (i >= (uint)_ParticleCapacity) return;
    LiquidParticle p = _Particles[i];
    if (!CohesiveActive(i, p)) { _PositionDeltas[i] = 0; return; }
    float mass = ReferenceMass(p), dt = _StreamParticles[i].stepDt;
    float lifetime = saturate(dt / max(1e-6, _DeltaTime));
    float2 delta = 0;
    int2 origin = GetCell(p.position);
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
    {
        int2 cell = origin + int2(x, y);
        if (!IsCellValid(cell)) continue;
        int j = _GridHeads[GetCellIndex(cell)], guard = 0;
        while (j >= 0 && guard++ < _ParticleCapacity)
        {
            LiquidParticle other = _Particles[j];
            if (j != (int)i && CohesiveActive((uint)j, other) && p.vesselId == other.vesselId)
            {
                float2 offset = p.position - other.position;
                if (dot(offset, offset) < _SmoothingRadius * _SmoothingRadius)
                {
                    float otherMass = ReferenceMass(other);
                    float sharedLifetime = saturate(min(dt, _StreamParticles[j].stepDt) / max(1e-6, _DeltaTime));
                    // Inverse-mass constraint weighting. Before contact/safety clamps, the
                    // pair contributes equal/opposite reconstructed momenta, including births.
                    delta += (_Lambdas[i] * otherMass + _Lambdas[j] * mass) / mass
                        * CohesiveGradient(i, (uint)j, offset) * sharedLifetime * lifetime;
                }
            }
            j = _GridNext[j];
        }
    }
    float wallDensity; float2 wallGradient;
    CohesiveWall(p, wallDensity, wallGradient);
    delta += _Lambdas[i] * wallGradient / mass * lifetime * lifetime;
    float limit = _ParticleRadius * _CohesiveCorrectionRatio * lifetime;
    float lengthDelta = length(delta);
    if (lengthDelta > limit)
    {
        delta *= limit / lengthDelta;
        _CohesiveDiagnostics[i].z += 1;
    }
    _PositionDeltas[i] = all(isfinite(delta)) ? delta : float2(0, 0);
}

float2 CohesiveMaterial(uint index)
{
    float2 result = 0; float total = 0;
    for (int ingredient = 0; ingredient < _IngredientCount; ingredient++)
    {
        float ratio = max(0, _CompositionRead[index * _MaximumIngredients + ingredient]);
        result += ratio * _CohesiveMaterials[ingredient].xy;
        total += ratio;
    }
    return total > 1e-6 ? result / total : float2(8, 1);
}

[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void CalculateCohesiveMaterialForces(uint3 dispatchId : SV_DispatchThreadID)
{
    uint i = dispatchId.x;
    if (i >= (uint)_ParticleCapacity) return;
    LiquidParticle p = _Particles[i];
    if (!CohesiveActive(i, p)) { _PositionDeltas[i] = 0; return; }
    float dt = _StreamParticles[i].stepDt, mass = ReferenceMass(p);
    float2 material = CohesiveMaterial(i), deltaVelocity = 0;
    float4 state = _CohesiveState[i];
    int2 origin = GetCell(p.position);
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
    {
        int2 cell = origin + int2(x, y);
        if (!IsCellValid(cell)) continue;
        int j = _GridHeads[GetCellIndex(cell)], guard = 0;
        while (j >= 0 && guard++ < _ParticleCapacity)
        {
            LiquidParticle other = _Particles[j];
            if (j != (int)i && CohesiveActive((uint)j, other) && p.vesselId == other.vesselId)
            {
                float2 offset = p.position - other.position;
                float distance = length(offset);
                if (distance < _SmoothingRadius)
                {
                    float otherMass = ReferenceMass(other), share = otherMass / (mass + otherMass);
                    float pairDt = min(dt, _StreamParticles[j].stepDt);
                    float2 pairMaterial = .5 * (material + CohesiveMaterial((uint)j));
                    float4 otherState = _CohesiveState[j];
                    float q = 1.0 - distance / _SmoothingRadius;
                    float blend = 1.0 - exp(-pairMaterial.x * _CohesiveShear * pairDt);
                    float normalization = max(1.0, max(state.y, otherState.y));
                    // Symmetric normalization bounds the aggregate velocity blend and retains
                    // equal/opposite mass-weighted exchange for unequal partial particles.
                    deltaVelocity += (_VelocitySnapshot[j] - _VelocitySnapshot[i])
                        * blend * q * share / normalization;
                    if (distance > 1e-6)
                    {
                        float attraction = CohesiveAttractionWeight(distance);
                        float cohesionNormalization = max(1.0, max(state.w, otherState.w));
                        deltaVelocity -= offset / distance * attraction * pairMaterial.y
                            * _CohesiveTension * pairDt * share / cohesionNormalization;
                    }
                }
            }
            j = _GridNext[j];
        }
    }
    float2 delta = deltaVelocity * dt;
    float limit = _ParticleRadius * _CohesiveCorrectionRatio * saturate(dt / max(1e-6, _DeltaTime));
    float lengthDelta = length(delta);
    if (lengthDelta > limit)
    {
        delta *= limit / lengthDelta;
        _CohesiveDiagnostics[i].z += 1;
    }
    if (!all(isfinite(delta))) delta = 0;
    _PositionDeltas[i] = delta;
    _CohesiveDiagnostics[i].w = length(delta) / max(1e-6, dt);
}
