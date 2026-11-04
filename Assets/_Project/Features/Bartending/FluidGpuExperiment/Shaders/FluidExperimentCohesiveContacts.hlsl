// Local ice contact stabilization for the cohesive liquid solver.
// Eight constraints bound cost. Selection is by penetration, then geometric keys, rather
// than boundary enumeration order. A particle does not shrink to fit an impossible gap.
#define COHESIVE_CONTACT_CAPACITY 8
struct CohesiveContact
{
    float2 normal;
    float2 velocity;
    float2 anchor;
    float penetration;
    uint flags;
};

bool CohesiveContactBefore(CohesiveContact a, CohesiveContact b)
{
    if (a.penetration != b.penetration) return a.penetration > b.penetration;
    if (a.normal.x != b.normal.x) return a.normal.x < b.normal.x;
    if (a.normal.y != b.normal.y) return a.normal.y < b.normal.y;
    if (a.anchor.x != b.anchor.x) return a.anchor.x < b.anchor.x;
    if (a.anchor.y != b.anchor.y) return a.anchor.y < b.anchor.y;
    if (a.velocity.x != b.velocity.x) return a.velocity.x < b.velocity.x;
    if (a.velocity.y != b.velocity.y) return a.velocity.y < b.velocity.y;
    return a.flags < b.flags;
}

void InsertCohesiveContact(CohesiveContact candidate,
    inout CohesiveContact contacts[COHESIVE_CONTACT_CAPACITY], inout int count)
{
    int slot = -1;
    // Coplanar neighboring segments are one constraint; different moving surfaces
    // retain distinct velocity constraints even when their normals coincide.
    [loop] for (int i = 0; i < count; i++)
        if (dot(candidate.normal, contacts[i].normal) > .99999
            && dot(candidate.velocity - contacts[i].velocity, candidate.velocity - contacts[i].velocity) < 1e-10)
        {
            if (!CohesiveContactBefore(candidate, contacts[i])) return;
            slot = i;
            break;
        }
    if (slot < 0)
    {
        if (count < COHESIVE_CONTACT_CAPACITY) slot = count++;
        else
        {
            slot = count - 1;
            if (!CohesiveContactBefore(candidate, contacts[slot])) return;
        }
    }
    contacts[slot] = candidate;
    [loop] for (int insertion = 1; insertion < count; insertion++)
    {
        CohesiveContact value = contacts[insertion];
        int previous = insertion - 1;
        [loop] while (previous >= 0)
        {
            if (!CohesiveContactBefore(value, contacts[previous])) break;
            contacts[previous + 1] = contacts[previous];
            previous--;
        }
        contacts[previous + 1] = value;
    }
}

// Ice is a filled closed solid, unlike a two-sided wall or thin glass shard.
// A particle caught inside it must leave through the nearest face; collecting
// inward normals from several faces traps and compresses it into a liquid jet.
bool InteriorIceContact(uint first, uint end, float2 position, out CohesiveContact contact)
{
    contact = (CohesiveContact)0;
    bool inside = false;
    float nearestSquare = 1e30, nearestT = 0;
    uint nearest = first;
    float2 nearestPoint = position;
    [loop] for (uint i = first; i < end; i++)
    {
        BoundarySegment edge = _Boundaries[i];
        if ((edge.flags & 512u) == 0u) continue;
        if ((edge.a.y > position.y) != (edge.b.y > position.y))
            if (position.x < (edge.b.x - edge.a.x) * (position.y - edge.a.y)
                / (edge.b.y - edge.a.y) + edge.a.x) inside = !inside;
        float t;
        float2 closest = ClosestPointOnSegment(position, edge.a, edge.b, t);
        float square = dot(position - closest, position - closest);
        if (square < nearestSquare)
        { nearestSquare = square; nearest = i; nearestPoint = closest; nearestT = t; }
    }
    if (!inside) return false;
    BoundarySegment boundary = _Boundaries[nearest];
    float distance = sqrt(nearestSquare);
    float2 edgeDirection = boundary.b - boundary.a;
    contact.normal = distance > 1e-7 ? (nearestPoint - position) / distance
        : normalize(float2(-edgeDirection.y, edgeDirection.x));
    contact.anchor = nearestPoint;
    contact.penetration = _ParticleRadius + distance;
    contact.velocity = lerp(boundary.velocityA, boundary.velocityB, nearestT);
    contact.flags = boundary.flags;
    return true;
}

int GatherCohesiveContacts(float2 referencePosition, float2 position, uint vesselId, bool preserveSide,
    out CohesiveContact contacts[COHESIVE_CONTACT_CAPACITY], out bool hasIce)
{
    int count = 0;
    hasIce = false;
    [loop] for (int clear = 0; clear < COHESIVE_CONTACT_CAPACITY; clear++) contacts[clear] = (CohesiveContact)0;
    float contactDistance = _ParticleRadius * 1.1;
    float searchRadius = contactDistance + (preserveSide ? length(position - referencePosition) : 0.0);
    [loop] for (int group = 0; group < _BoundaryGroupCount; group++)
    {
        uint first, end;
#if defined(PHYSICSLAB_LINEAR_BOUNDARIES)
        // The reference skips culling but still needs each closed solid's range.
        first = _BoundaryGroups[group].first; end = _BoundaryGroups[group].end;
#else
        if (!BoundarySearchRange(group, position - searchRadius, position + searchRadius, false, first, end)) continue;
#endif
        if (first < end && (_Boundaries[first].flags & 512u) != 0u
            && IsBoundaryCompatible(vesselId, _Boundaries[first].vesselId, _Boundaries[first].flags))
        {
            CohesiveContact interior;
            if (InteriorIceContact(first, end, position, interior))
            {
                hasIce = true;
                InsertCohesiveContact(interior, contacts, count);
                continue;
            }
        }
        [loop] for (uint boundaryIndex = first; boundaryIndex < end; boundaryIndex++)
        {
            BoundarySegment boundary = _Boundaries[boundaryIndex];
            if (!IsBoundaryCompatible(vesselId, boundary.vesselId, boundary.flags)) continue;
            float edgeT;
            float2 anchor = ClosestPointOnSegment(position, boundary.a, boundary.b, edgeT);
            float2 difference = position - anchor;
            float distance = length(difference);
            float2 edge = boundary.b - boundary.a;
            if (dot(edge, edge) <= 1e-12) continue;
            float previousSide = Cross2D(edge, referencePosition - boundary.a);
            float currentSide = Cross2D(edge, position - boundary.a);
            bool crossed = preserveSide && previousSide * currentSide < 0.0 && edgeT > 0.0 && edgeT < 1.0;
            if (!crossed && distance > contactDistance) continue;
            CohesiveContact candidate;
            candidate.normal = distance > 1e-7 ? difference / distance : normalize(float2(-edge.y, edge.x));
            if (crossed)
                candidate.normal = normalize(float2(-edge.y, edge.x)) * (previousSide >= 0.0 ? 1.0 : -1.0);
            candidate.anchor = anchor;
            candidate.penetration = _ParticleRadius - dot(position - anchor, candidate.normal);
            candidate.velocity = lerp(boundary.velocityA, boundary.velocityB, edgeT);
            candidate.flags = boundary.flags;
            hasIce = hasIce || (boundary.flags & 4u) != 0u;
            InsertCohesiveContact(candidate, contacts, count);
        }
    }
    return count;
}

bool CohesiveFeasible(float2 candidate, float4 planes[COHESIVE_CONTACT_CAPACITY], int count, float slack)
{
    if (!all(isfinite(candidate))) return false;
    [loop] for (int i = 0; i < count; i++)
        if (dot(planes[i].xy, candidate) < planes[i].z - slack * planes[i].w - .00001) return false;
    return true;
}

void ConsiderCohesiveCandidate(float2 candidate, float4 planes[COHESIVE_CONTACT_CAPACITY], int count,
    float slack, inout bool found, inout float2 best, inout float bestSquare)
{
    float square = dot(candidate, candidate);
    if (!isfinite(square) || square > bestSquare || !CohesiveFeasible(candidate, planes, count, slack)) return;
    if (!found || square < bestSquare || candidate.x < best.x || (candidate.x == best.x && candidate.y < best.y))
    {
        found = true; best = candidate; bestSquare = square;
    }
}

bool ClosestCohesiveFeasible(float4 planes[COHESIVE_CONTACT_CAPACITY], int count, float slack, out float2 best)
{
    best = 0;
    if (CohesiveFeasible(0, planes, count, slack)) return true;
    bool found = false;
    float bestSquare = 1e30;
    // In 2D the closest feasible point has zero, one or two active planes.
    [loop] for (int i = 0; i < count; i++)
    {
        float ri = planes[i].z - slack * planes[i].w;
        ConsiderCohesiveCandidate(planes[i].xy * ri, planes, count, slack, found, best, bestSquare);
        [loop] for (int j = i + 1; j < count; j++)
        {
            float determinant = Cross2D(planes[i].xy, planes[j].xy);
            if (abs(determinant) <= 1e-6) continue;
            float rj = planes[j].z - slack * planes[j].w;
            float2 candidate = float2(ri * planes[j].y - planes[i].y * rj,
                planes[i].x * rj - ri * planes[j].x) / determinant;
            ConsiderCohesiveCandidate(candidate, planes, count, slack, found, best, bestSquare);
        }
    }
    return found;
}

float2 ProjectCohesivePlanes(float4 planes[COHESIVE_CONTACT_CAPACITY], int count)
{
    float2 best;
    if (ClosestCohesiveFeasible(planes, count, 0, best)) return best;
    // An impossible ice gap must not relax the containing glass wall too: that
    // pumps liquid through the thin wall. Keep vessel/shard planes hard and allow
    // only ice overlap, without shrinking particles or changing their ml.
    float low = 0, high = 0;
    float4 hardPlanes[COHESIVE_CONTACT_CAPACITY];
    int hardCount = 0;
    [loop] for (int clear = 0; clear < COHESIVE_CONTACT_CAPACITY; clear++) hardPlanes[clear] = 0;
    [loop] for (int hard = 0; hard < count; hard++)
        if (planes[hard].w == 0) hardPlanes[hardCount++] = planes[hard];
    if (ClosestCohesiveFeasible(hardPlanes, hardCount, 0, best))
    {
        [loop] for (int soft = 0; soft < count; soft++)
            if (planes[soft].w > 0)
                high = max(high, planes[soft].z - dot(planes[soft].xy, best));
    }
    else
    {
        // Preserve the bounded legacy fallback for incompatible non-ice geometry.
        best = 0;
        [loop] for (int fallback = 0; fallback < count; fallback++)
        { planes[fallback].w = 1; high = max(high, planes[fallback].z); }
    }
    [loop] for (int iteration = 0; iteration < 8; iteration++)
    {
        float middle = .5 * (low + high);
        float2 candidate;
        if (ClosestCohesiveFeasible(planes, count, middle, candidate)) { high = middle; best = candidate; }
        else low = middle;
    }
    return best;
}

bool TryProjectCohesiveIceContacts(float2 previous, float2 requested, uint vesselId,
    out float2 corrected, out float iceSpeedBudget)
{
    corrected = requested;
    iceSpeedBudget = -1;
    CohesiveContact contacts[COHESIVE_CONTACT_CAPACITY];
    bool hasIce;
    int count = GatherCohesiveContacts(previous, requested, vesselId, true, contacts, hasIce);
    if (!hasIce || count == 0) return false;
    [loop] for (int contact = 0; contact < count; contact++)
        if ((contacts[contact].flags & 512u) != 0u)
            iceSpeedBudget = max(iceSpeedBudget, length(contacts[contact].velocity));
    float4 planes[COHESIVE_CONTACT_CAPACITY];
    [loop] for (int i = 0; i < COHESIVE_CONTACT_CAPACITY; i++)
        planes[i] = i < count ? float4(contacts[i].normal, contacts[i].penetration,
            iceSpeedBudget < 0 || (contacts[i].flags & 512u) != 0u ? 1 : 0) : 0;
    float2 correction = ProjectCohesivePlanes(planes, count);
    float limit = _ParticleRadius * 2.0 + length(requested - previous);
    float correctionLength = length(correction);
    if (correctionLength > limit) correction *= limit / correctionLength;
    corrected += correction;
    return true;
}

float2 ResolveCohesiveContactVelocity(float2 incoming, float dt,
    CohesiveContact contacts[COHESIVE_CONTACT_CAPACITY], int count)
{
    float4 planes[COHESIVE_CONTACT_CAPACITY];
    bool nativeIce = false;
    [loop] for (int native = 0; native < count; native++) nativeIce = nativeIce || (contacts[native].flags & 512u) != 0u;
    [loop] for (int clear = 0; clear < COHESIVE_CONTACT_CAPACITY; clear++) planes[clear] = 0;
    [loop] for (int i = 0; i < count; i++)
    {
        float approach = dot(incoming - contacts[i].velocity, contacts[i].normal);
        float restitution = (contacts[i].flags & 512u) != 0u ? 0 : saturate(_WallRestitution);
        float outward = -restitution * min(0.0, approach);
        planes[i] = float4(contacts[i].normal, outward - approach,
            !nativeIce || (contacts[i].flags & 512u) != 0u ? 1 : 0);
    }
    float2 resolved = incoming + ProjectCohesivePlanes(planes, count);
    // Apply the existing friction coefficient once, averaged over active contacts.
    // Reproject afterwards because one contact's tangent can enter a second surface.
    float2 tangentSum = 0;
    [loop] for (int friction = 0; friction < count; friction++)
    {
        float2 relative = resolved - contacts[friction].velocity;
        tangentSum += relative - contacts[friction].normal * dot(relative, contacts[friction].normal);
    }
    float2 frictionVelocity = resolved - tangentSum * (TimeScaledBlend(_WallFriction, dt) / max(1, count));
    [loop] for (int constraint = 0; constraint < count; constraint++)
        planes[constraint].z += dot(contacts[constraint].normal, incoming - frictionVelocity);
    return frictionVelocity + ProjectCohesivePlanes(planes, count);
}

float IcePhysicalSpeedSquare(uint index, LiquidParticle p, float2 predicted, float dt)
{
    float2 beforeGravity = predicted - _Gravity * dt;
    // previousPosition is shifted by split contact repairs; it is not the true
    // height history and must not finance gravity work in a stationary ice gap.
    float work = 2.0 * dot(_Gravity, p.position - _IceStepOriginsRead[index]);
    // Gravity cannot continuously fuel an upward pressure correction at rest.
    // Falling fluid keeps its actual gravitational work and incoming momentum.
    return min(dot(predicted, predicted), max(0.0, dot(beforeGravity, beforeGravity) + work));
}

float IceAvailableSpeedSquare(uint index, LiquidParticle p)
{
    float budget = IcePhysicalSpeedSquare(index, p, _VelocityRead[index], _StreamParticles[index].stepDt);
    int2 origin = GetCell(p.position);
    [loop] for (int y = -1; y <= 1; y++) [loop] for (int x = -1; x <= 1; x++)
    {
        int2 cell = origin + int2(x, y);
        if (!IsCellValid(cell)) continue;
        int j = _GridHeads[GetCellIndex(cell)], guard = 0;
        [loop] while (j >= 0 && guard++ < _ParticleCapacity)
        {
            LiquidParticle other = _Particles[j];
            if (other.active != 0u && other.vesselId == p.vesselId
                && dot(other.position - p.position, other.position - p.position) < _SmoothingRadius * _SmoothingRadius)
                budget = max(budget, IcePhysicalSpeedSquare((uint)j, other, _VelocityRead[j], _StreamParticles[j].stepDt));
            j = _GridNext[j];
        }
    }
    return budget;
}

float2 RegularizeIcePressure(uint index, LiquidParticle p, float2 requested)
{
    // Dense ice can create mutually incompatible density/contact constraints.
    // Limit new relative motion in the incoming neighborhood's frame before the
    // physical wall impulse. Common flow, gravity and fast streams retain momentum.
    float2 mean = 0;
    float mass = 0;
    int2 origin = GetCell(p.position);
    [loop] for (int y = -1; y <= 1; y++) [loop] for (int x = -1; x <= 1; x++)
    {
        int2 cell = origin + int2(x, y);
        if (!IsCellValid(cell)) continue;
        int j = _GridHeads[GetCellIndex(cell)], guard = 0;
        [loop] while (j >= 0 && guard++ < _ParticleCapacity)
        {
            LiquidParticle other = _Particles[j];
            if (other.active != 0u && other.vesselId == p.vesselId
                && dot(other.position - p.position, other.position - p.position) < _SmoothingRadius * _SmoothingRadius)
            { mean += _VelocityRead[j] * other.volumeMl; mass += other.volumeMl; }
            j = _GridNext[j];
        }
    }
    if (mass <= 0) return _VelocityRead[index];
    mean /= mass;
    float maximumRelative = 0;
    [loop] for (int ry = -1; ry <= 1; ry++) [loop] for (int rx = -1; rx <= 1; rx++)
    {
        int2 cell = origin + int2(rx, ry);
        if (!IsCellValid(cell)) continue;
        int j = _GridHeads[GetCellIndex(cell)], guard = 0;
        [loop] while (j >= 0 && guard++ < _ParticleCapacity)
        {
            LiquidParticle other = _Particles[j];
            if (other.active != 0u && other.vesselId == p.vesselId
                && dot(other.position - p.position, other.position - p.position) < _SmoothingRadius * _SmoothingRadius)
                maximumRelative = max(maximumRelative, length(_VelocityRead[j] - mean));
            j = _GridNext[j];
        }
    }
    float2 relative = requested - mean;
    float speed = length(relative);
    // Unresolved ice-gap oscillations dissipate in the local flow frame. This
    // never damps the common translation of an incoming stream or falling drop.
    maximumRelative *= exp(-_IceContactRelaxationRate * _StreamParticles[index].stepDt);
    return mean + relative * (speed > maximumRelative ? maximumRelative / max(1e-7, speed) : 1.0);
}
