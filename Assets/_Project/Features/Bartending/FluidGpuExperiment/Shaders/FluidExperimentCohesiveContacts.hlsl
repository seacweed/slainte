// F-only local ice contact stabilization. The legacy wall-only/A-E paths are unchanged.
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

int GatherCohesiveContacts(float2 referencePosition, float2 position, uint vesselId, bool preserveSide,
    out CohesiveContact contacts[COHESIVE_CONTACT_CAPACITY], out bool hasIce)
{
    int count = 0;
    hasIce = false;
    [loop] for (int clear = 0; clear < COHESIVE_CONTACT_CAPACITY; clear++) contacts[clear] = (CohesiveContact)0;
    float contactDistance = _ParticleRadius * 1.1;
    float searchRadius = contactDistance + (preserveSide ? length(position - referencePosition) : 0.0);
    [loop] for (int group = 0; group < BoundarySearchGroupCount(); group++)
    {
        uint first, end;
        if (!BoundarySearchRange(group, position - searchRadius, position + searchRadius, false, first, end)) continue;
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

bool CohesiveFeasible(float2 candidate, float3 planes[COHESIVE_CONTACT_CAPACITY], int count, float slack)
{
    if (!all(isfinite(candidate))) return false;
    [loop] for (int i = 0; i < count; i++)
        if (dot(planes[i].xy, candidate) < planes[i].z - slack - .00001) return false;
    return true;
}

void ConsiderCohesiveCandidate(float2 candidate, float3 planes[COHESIVE_CONTACT_CAPACITY], int count,
    float slack, inout bool found, inout float2 best, inout float bestSquare)
{
    float square = dot(candidate, candidate);
    if (!isfinite(square) || square > bestSquare || !CohesiveFeasible(candidate, planes, count, slack)) return;
    if (!found || square < bestSquare || candidate.x < best.x || (candidate.x == best.x && candidate.y < best.y))
    {
        found = true; best = candidate; bestSquare = square;
    }
}

bool ClosestCohesiveFeasible(float3 planes[COHESIVE_CONTACT_CAPACITY], int count, float slack, out float2 best)
{
    best = 0;
    if (CohesiveFeasible(0, planes, count, slack)) return true;
    bool found = false;
    float bestSquare = 1e30;
    // In 2D the closest feasible point has zero, one or two active planes.
    [loop] for (int i = 0; i < count; i++)
    {
        float ri = planes[i].z - slack;
        ConsiderCohesiveCandidate(planes[i].xy * ri, planes, count, slack, found, best, bestSquare);
        [loop] for (int j = i + 1; j < count; j++)
        {
            float determinant = Cross2D(planes[i].xy, planes[j].xy);
            if (abs(determinant) <= 1e-6) continue;
            float rj = planes[j].z - slack;
            float2 candidate = float2(ri * planes[j].y - planes[i].y * rj,
                planes[i].x * rj - ri * planes[j].x) / determinant;
            ConsiderCohesiveCandidate(candidate, planes, count, slack, found, best, bestSquare);
        }
    }
    return found;
}

float2 ProjectCohesivePlanes(float3 planes[COHESIVE_CONTACT_CAPACITY], int count)
{
    float2 best;
    if (ClosestCohesiveFeasible(planes, count, 0, best)) return best;
    // A finite-radius particle can be trapped in an infeasible gap. Minimize the
    // maximum remaining penetration with a bounded common relaxation, then choose
    // the smallest displacement. This avoids selecting alternating opposing walls;
    // it cannot promise physical fit, and never changes ml, radius or ownership.
    float low = 0, high = 0;
    [loop] for (int i = 0; i < count; i++) high = max(high, planes[i].z);
    best = 0; // The original point satisfies all planes relaxed by high.
    [loop] for (int iteration = 0; iteration < 8; iteration++)
    {
        float middle = .5 * (low + high);
        float2 candidate;
        if (ClosestCohesiveFeasible(planes, count, middle, candidate)) { high = middle; best = candidate; }
        else low = middle;
    }
    return best;
}

bool TryProjectCohesiveIceContacts(float2 previous, float2 requested, uint vesselId, out float2 corrected)
{
    corrected = requested;
    CohesiveContact contacts[COHESIVE_CONTACT_CAPACITY];
    bool hasIce;
    int count = GatherCohesiveContacts(previous, requested, vesselId, true, contacts, hasIce);
    if (!hasIce || count == 0) return false;
    float3 planes[COHESIVE_CONTACT_CAPACITY];
    [loop] for (int i = 0; i < COHESIVE_CONTACT_CAPACITY; i++)
        planes[i] = i < count ? float3(contacts[i].normal, contacts[i].penetration) : 0;
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
    float3 planes[COHESIVE_CONTACT_CAPACITY];
    [loop] for (int clear = 0; clear < COHESIVE_CONTACT_CAPACITY; clear++) planes[clear] = 0;
    [loop] for (int i = 0; i < count; i++)
    {
        float approach = dot(incoming - contacts[i].velocity, contacts[i].normal);
        float outward = -saturate(_WallRestitution) * min(0.0, approach);
        planes[i] = float3(contacts[i].normal, outward - approach);
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
