// E: area-normalized, compression-only density projection. ml remains authoritative.
// Per-vessel effective depth comes from its authored capacity and shared interior polygon.
StructuredBuffer<float4> _ImprovedVessels; // id, world area/ml, support radius, reserved
StructuredBuffer<float4> _ImprovedMaterials; // shear rate, cohesion acceleration, wetting rate, foam lifetime
RWStructuredBuffer<float4> _ImprovedDiagnostics; // density, compression error, correction-limited, speed squared
int _ImprovedVesselCount;
float _ImprovedAreaPerMl, _ImprovedKernelRatio, _ImprovedCompliance, _ImprovedCorrectionRatio;
float _ImprovedWallDensity, _ImprovedShear, _ImprovedTension, _ImprovedWetting;

float2 ImprovedCalibration(uint id)
{
    for (int i = 0; i < _ImprovedVesselCount; i++)
        if ((uint)_ImprovedVessels[i].x == id) return _ImprovedVessels[i].yz;
    return float2(_ImprovedAreaPerMl, clamp(sqrt(_ReferenceParticleVolume * _ImprovedAreaPerMl)
        * _ImprovedKernelRatio, _ParticleRadius * 2.1, _SmoothingRadius));
}
float ImprovedKernel(float2 offset, float h)
{
    float h2 = h * h;
    float q = saturate(1.0 - dot(offset, offset) / h2);
    return 4.0 / (3.14159265 * h2) * q * q * q;
}
float2 ImprovedGradient(float2 offset, float h)
{
    float h2 = h * h;
    float q = saturate(1.0 - dot(offset, offset) / h2);
    return -24.0 / (3.14159265 * h2 * h2) * q * q * offset;
}
float ImprovedWallMarginal(float x)
{
    return 128.0 / (35.0 * 3.14159265) * pow(saturate(1.0 - x * x), 3.5);
}
// Integral of the normalized 2D poly6 kernel beyond a locally planar wall.
// The virtual open rim is excluded by TryFindNearestCompatibleBoundary.
float ImprovedMissingWallDensity(float ratio)
{
    float start = saturate(ratio), step = (1.0 - start) / 8.0;
    float sum = ImprovedWallMarginal(start) + ImprovedWallMarginal(1.0);
    [unroll] for (int i = 1; i < 8; i++)
        sum += ImprovedWallMarginal(start + step * i) * ((i & 1) != 0 ? 4.0 : 2.0);
    return sum * step / 3.0;
}
bool ImprovedWall(LiquidParticle p, float h, out float density, out float2 gradient,
    out float2 normal, out float2 velocity, out float distance)
{
    float2 boundaryPoint; float t;
    density = 0; gradient = 0; normal = 0; velocity = 0; distance = h;
    if (!TryFindNearestCompatibleBoundary(p.position, p.vesselId, h, boundaryPoint, normal, distance, t, velocity)) return false;
    density = ImprovedMissingWallDensity(distance / h) * _ImprovedWallDensity;
    gradient = -normal * (ImprovedWallMarginal(distance / h) / h) * _ImprovedWallDensity;
    return true;
}

[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void CalculateImprovedDensity(uint3 dispatchId : SV_DispatchThreadID)
{
    uint index = dispatchId.x;
    if (index >= (uint)_ParticleCapacity) return;
    LiquidParticle p = _Particles[index];
    if (!ReferenceParticleActive(index, p)) { _Lambdas[index] = 0; _ImprovedDiagnostics[index] = 0; return; }
    float2 calibration = ImprovedCalibration(p.vesselId);
    float h = calibration.y, selfArea = max(0, p.volumeMl) * calibration.x;
    float density = selfArea * ImprovedKernel(0, h);
    float2 selfGradient = 0;
    float gradientSquares = 0, viscosityWeight = 0;
    int2 cell = GetCell(p.position);
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
    {
        int2 otherCell = cell + int2(x, y);
        if (!IsCellValid(otherCell)) continue;
        int next = _GridHeads[GetCellIndex(otherCell)], guard = 0;
        while (next >= 0 && guard++ < _ParticleCapacity)
        {
            LiquidParticle q = _Particles[next];
            if (next != (int)index && ReferenceParticleActive((uint)next, q) && p.vesselId == q.vesselId)
            {
                float2 offset = p.position - q.position;
                float area = max(0, q.volumeMl) * calibration.x;
                density += area * ImprovedKernel(offset, h);
                float2 gradient = area * ImprovedGradient(GetPairSeparationOffset(index, (uint)next, offset), h);
                selfGradient += gradient;
                gradientSquares += dot(gradient, gradient);
                viscosityWeight += saturate(1.0 - length(offset) / h);
            }
            next = _GridNext[next];
        }
    }
    float wallDensity, wallDistance; float2 wallGradient, normal, wallVelocity;
    ImprovedWall(p, h, wallDensity, wallGradient, normal, wallVelocity, wallDistance);
    density += wallDensity; selfGradient += wallGradient;
    gradientSquares += dot(selfGradient, selfGradient);
    float compression = max(0, density - 1.0);
    float dt = _StreamParticles[index].stepDt;
    _Lambdas[index] = -compression / max(.0001, gradientSquares + _ImprovedCompliance / max(1e-8, dt * dt));
    _ImprovedDiagnostics[index] = float4(density, compression, 0, dot(p.velocity, p.velocity));
    _ReferenceDensityPressure[index] = float4(0, 0, 0, viscosityWeight);
}

[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void CalculateImprovedDelta(uint3 dispatchId : SV_DispatchThreadID)
{
    uint index = dispatchId.x;
    if (index >= (uint)_ParticleCapacity) return;
    LiquidParticle p = _Particles[index];
    if (!ReferenceParticleActive(index, p)) { _PositionDeltas[index] = 0; return; }
    float2 calibration = ImprovedCalibration(p.vesselId);
    float h = calibration.y, dt = _StreamParticles[index].stepDt;
    float2 delta = 0;
    int2 cell = GetCell(p.position);
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
    {
        int2 otherCell = cell + int2(x, y);
        if (!IsCellValid(otherCell)) continue;
        int next = _GridHeads[GetCellIndex(otherCell)], guard = 0;
        while (next >= 0 && guard++ < _ParticleCapacity)
        {
            LiquidParticle q = _Particles[next];
            if (next != (int)index && ReferenceParticleActive((uint)next, q) && p.vesselId == q.vesselId)
            {
                float2 offset = GetPairSeparationOffset(index, (uint)next, p.position - q.position);
                float pairTime = min(dt, _StreamParticles[next].stepDt) / max(dt, 1e-6);
                delta += (_Lambdas[index] + _Lambdas[next]) * max(0, q.volumeMl) * calibration.x
                    * ImprovedGradient(offset, h) * pairTime;
            }
            next = _GridNext[next];
        }
    }
    float wallDensity, wallDistance; float2 wallGradient, normal, wallVelocity;
    ImprovedWall(p, h, wallDensity, wallGradient, normal, wallVelocity, wallDistance);
    delta += _Lambdas[index] * wallGradient;
    float limit = _ParticleRadius * _ImprovedCorrectionRatio;
    float lengthDelta = length(delta);
    _ImprovedDiagnostics[index].z = lengthDelta > limit ? 1 : 0;
    if (lengthDelta > limit) delta *= limit / lengthDelta;
    _PositionDeltas[index] = all(isfinite(delta)) ? delta : 0;
}

float4 ImprovedMaterial(uint index)
{
    float4 material = 0;
    for (int i = 0; i < _IngredientCount; i++)
        material += max(0, _CompositionRead[index * _MaximumIngredients + i]) * _ImprovedMaterials[i];
    return material;
}

[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void CalculateImprovedViscosity(uint3 dispatchId : SV_DispatchThreadID)
{
    uint index = dispatchId.x;
    if (index >= (uint)_ParticleCapacity) return;
    LiquidParticle p = _Particles[index];
    if (!ReferenceParticleActive(index, p)) { _PositionDeltas[index] = 0; return; }
    float2 calibration = ImprovedCalibration(p.vesselId);
    float h = calibration.y, dt = _StreamParticles[index].stepDt;
    float4 material = ImprovedMaterial(index);
    float2 deltaVelocity = 0, acceleration = 0;
    int2 cell = GetCell(p.position);
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
    {
        int2 otherCell = cell + int2(x, y);
        if (!IsCellValid(otherCell)) continue;
        int next = _GridHeads[GetCellIndex(otherCell)], guard = 0;
        while (next >= 0 && guard++ < _ParticleCapacity)
        {
            LiquidParticle q = _Particles[next];
            if (next != (int)index && ReferenceParticleActive((uint)next, q) && p.vesselId == q.vesselId)
            {
                float2 offset = p.position - q.position;
                float distance = length(offset), weight = saturate(1.0 - distance / h);
                if (weight > 0 && distance > 1e-6)
                {
                    float pairDt = min(dt, _StreamParticles[next].stepDt);
                    float massShare = q.volumeMl / max(1e-6, p.volumeMl + q.volumeMl);
                    float4 otherMaterial = ImprovedMaterial((uint)next);
                    float blend = 1.0 - exp(-.5 * (material.x + otherMaterial.x) * _ImprovedShear * pairDt);
                    float normalization = max(1.0, max(_ReferenceDensityPressure[index].w, _ReferenceDensityPressure[next].w));
                    deltaVelocity += (_VelocitySnapshot[next] - _VelocitySnapshot[index]) * blend * weight * massShare / normalization;
                    float restDistance = sqrt(.5 * (p.volumeMl + q.volumeMl) * calibration.x / .8660254);
                    // Weak, finite-range cohesion only outside the nominal particle spacing.
                    acceleration -= offset / distance * saturate((distance - restDistance) / max(restDistance, .001))
                        * weight * weight * .5 * (material.y + otherMaterial.y) * _ImprovedTension * massShare * (pairDt / dt);
                }
            }
            next = _GridNext[next];
        }
    }
    float wallDensity, wallDistance; float2 wallGradient, normal, wallVelocity;
    if (ImprovedWall(p, h, wallDensity, wallGradient, normal, wallVelocity, wallDistance))
    {
        float contact = saturate(1.0 - wallDistance / max(.0001, _ParticleRadius * 2.5));
        float2 relative = _VelocitySnapshot[index] - wallVelocity;
        float2 tangentVelocity = relative - normal * dot(relative, normal);
        deltaVelocity -= tangentVelocity * (1.0 - exp(-material.z * _ImprovedWetting * dt)) * contact;
        acceleration -= normal * material.z * _ImprovedWetting * contact * .02;
    }
    float2 delta = (deltaVelocity + acceleration * dt) * dt;
    float limit = _ParticleRadius * _ImprovedCorrectionRatio;
    if (length(delta) > limit) delta *= limit / length(delta);
    _PositionDeltas[index] = all(isfinite(delta)) ? delta : 0;
}
