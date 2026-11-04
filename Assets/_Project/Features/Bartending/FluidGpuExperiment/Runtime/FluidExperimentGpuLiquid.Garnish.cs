using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        private struct GarnishFlowFrame { public Vector2 position; public float angle; }
        private Dictionary<uint, GarnishFlowFrame> garnishFlowFrames;
        private readonly List<Vector2> iceSurfaceSamples = new List<Vector2>(256);
        private readonly List<Vector2> iceFallbackSurfaceSamples = new List<Vector2>(256);
        private readonly List<Bounds> iceWakeBounds = new List<Bounds>(32);
        private GpuLiquidParticle[] previousIceFlowParticles;
        private Vector2[] iceTransportVelocities;
        private float iceTransportTime = -1;
        private int iceTransportGeneration = -1;

        // Collision pressure can retain a downward solver velocity even while
        // particle positions stay in a still pool. That must not cancel buoyancy
        // as a fictitious current. Average signed completed travel for ice drag;
        // GPU velocities and their release inertia are never rewritten here.
        private void CaptureIceFlowTransport()
        {
            if (previousIceFlowParticles == null || iceTransportGeneration != generation
                || previousIceFlowParticles.Length != snapshotParticles.Length)
            {
                previousIceFlowParticles = (GpuLiquidParticle[])snapshotParticles.Clone();
                iceTransportVelocities = new Vector2[snapshotParticles.Length];
                for (int i = 0; i < snapshotParticles.Length; i++) iceTransportVelocities[i] = snapshotParticles[i].Velocity;
                iceTransportGeneration = generation; iceTransportTime = Ledger.SimulationTime;
                BuildIceFlowComponents();
                return;
            }
            float dt = Ledger.SimulationTime-iceTransportTime;
            if (dt <= .00001f) return;
            for (int i = 0; i < snapshotParticles.Length; i++)
            {
                var p = snapshotParticles[i]; var previous = previousIceFlowParticles[i];
                iceTransportVelocities[i] = p.Active != 0 && previous.Active != 0 && p.VesselId == previous.VesselId
                    ? Vector2.ClampMagnitude((p.Position-previous.Position)/dt,6) : p.Velocity;
                previousIceFlowParticles[i] = p;
            }
            iceTransportTime = Ledger.SimulationTime;
            BuildIceFlowComponents();
        }

        private Vector2 IceTransportVelocity(int index, GpuLiquidParticle p)
        {
            if (previousIceFlowParticles == null) return p.Velocity;
            var sample = previousIceFlowParticles[index];
            return sample.Position == p.Position && sample.Velocity == p.Velocity ? iceTransportVelocities[index] : p.Velocity;
        }
        private sealed class IcePool
        {
            public int generation = -1, revision = -1;
            public float areaPerMl, time = -1, ml, level;
            public readonly List<Vector2> interior = new List<Vector2>(32);
            public readonly List<List<Vector2>> solids = new List<List<Vector2>>(16);
        }
        private readonly Dictionary<uint, IcePool> icePools = new Dictionary<uint, IcePool>();
        private readonly List<Vector2> iceReferenceHeights = new List<Vector2>(256);

        private IcePool PoolFor(FluidExperimentBody vessel)
        {
            if (!icePools.TryGetValue(vessel.Id, out var pool)) icePools[vessel.Id] = pool = new IcePool();
            if (pool.generation != generation)
            { pool.generation = generation; pool.areaPerMl = 0; pool.time = -1; pool.revision = -1; }
            return pool;
        }

        // Calibrate from a settled, undisplaced pool, before collision ice enters
        // it (or while ice/liquid collision is disabled). Never calibrate from a
        // cube's carried film. The reference remains world-area per conserved ml.
        private void CaptureIcePoolReferences()
        {
            if (world == null || Ledger.SimulationTime < 1) return;
            foreach (var vessel in world.Items)
            {
                if (vessel == null || !vessel.IsVessel || !vessel.Body.simulated) continue;
                var pool = PoolFor(vessel); if (pool.areaPerMl > 0) continue;
                iceReferenceHeights.Clear(); float ml = 0, energy = 0;
                foreach (var p in snapshotParticles)
                    if (p.Active != 0 && p.VesselId == vessel.Id && p.VolumeMl > 0)
                    { ml += p.VolumeMl; energy += p.Velocity.sqrMagnitude*p.VolumeMl; iceReferenceHeights.Add(new Vector2(p.Position.y+Radius,p.VolumeMl)); }
                if (ml < 3 || energy/ml > .0225f) continue;
                float level = IceSurfaceBandMean(iceReferenceHeights,.8f,.98f);
                bool displaced = false;
                if (IceLiquidCollisionEnabled)
                    foreach (var ice in world.Items)
                        if (ice != null && ice.kind == LabItemKind.Ice && !ice.IsHeld && ice.Body.simulated
                            && ice.ContainingVesselId == vessel.Id && ice.IceBuoyancyBounds.min.y < level)
                        { displaced = true; break; }
                if (displaced) continue;
                pool.interior.Clear(); foreach (var p in vessel.LiquidInteriorPath) pool.interior.Add(vessel.LocalToWorld(p));
                float area = IceAreaBelow(pool.interior,level);
                if (area > 0) pool.areaPerMl = area/ml;
            }
        }

        // A fixed amount of water cannot follow its own collision film upwards
        // indefinitely. Estimate a hydrostatic plane from actual polygon displacement,
        // without expanded AABBs or calibration after the ice has displaced water.
        private float IcePoolLevel(FluidExperimentBody vessel, float ml)
        {
            var pool = PoolFor(vessel);
            if (pool.revision == SnapshotRevision && pool.time == simulationTime && Mathf.Approximately(pool.ml,ml)) return pool.level;
            pool.revision = SnapshotRevision; pool.time = simulationTime; pool.ml = ml;
            pool.interior.Clear(); float low = float.PositiveInfinity, high = float.NegativeInfinity;
            foreach (var p in vessel.LiquidInteriorPath)
            { Vector2 point = vessel.LocalToWorld(p); pool.interior.Add(point); low = Mathf.Min(low,point.y); high = Mathf.Max(high,point.y); }
            if (pool.interior.Count < 3) return high;
            if (!vessel.sealedVessel) high = Mathf.Min(pool.interior[0].y,pool.interior[pool.interior.Count-1].y);
            int count = 0;
            foreach (var ice in world.Items)
                if (ice != null && ice.kind == LabItemKind.Ice && !ice.IsHeld && ice.Body.simulated
                    && ice.ContainingVesselId == vessel.Id && ice.collisionProfile != null)
                    foreach (var hull in ice.collisionProfile.solids)
                    {
                        if (count == pool.solids.Count) pool.solids.Add(new List<Vector2>(16));
                        var polygon = pool.solids[count++]; polygon.Clear();
                        foreach (var p in hull.points) polygon.Add(ice.LocalToWorld(p));
                    }
            // The cohesion start distance is not the liquid's rest spacing.
            // Without a plain-pool reference, use the q^2 density kernel's 2D
            // integral (pi*h^2/6) and its target density, not an oversized lattice.
            float restDensity = Mathf.Max(.1f,settings.referenceRestDensity);
            float nominalArea = Mathf.PI*SolverSmoothingRadius*SolverSmoothingRadius/(6*restDensity*Mathf.Max(.001f,ParticleVolumeMl));
            float areaPerMl = pool.areaPerMl > 0 ? pool.areaPerMl : nominalArea;
            float target = ml*areaPerMl;
            for (int iteration = 0; iteration < 14; iteration++)
            {
                float level = (low+high)*.5f, available = IceAreaBelow(pool.interior,level);
                for (int i = 0; i < count; i++) available -= IceAreaBelow(pool.solids[i],level);
                if (available < target) low = level; else high = level;
            }
            return pool.level = (low+high)*.5f;
        }

        private static float IceAreaBelow(List<Vector2> polygon, float level)
        {
            if (polygon.Count < 3) return 0;
            Vector2 origin = polygon[0], first = Vector2.zero, previous = Vector2.zero;
            double area = 0; bool found = false;
            void Add(Vector2 world)
            {
                Vector2 p = world-origin;
                if (!found) { first = p; found = true; }
                else area += (double)previous.x*p.y-(double)p.x*previous.y;
                previous = p;
            }
            Vector2 a = polygon[polygon.Count-1];
            foreach (var b in polygon)
            {
                bool aWet = a.y <= level, bWet = b.y <= level;
                if (aWet != bWet) Add(Vector2.LerpUnclamped(a,b,(level-a.y)/(b.y-a.y)));
                if (bWet) Add(b);
                a = b;
            }
            if (found) area += (double)previous.x*first.y-(double)first.x*previous.y;
            return (float)(System.Math.Abs(area)*.5);
        }
        private void InvalidateSolidFlowSnapshot()
        {
            garnishFlowFrames = null;
            previousIceFlowParticles = null;
            iceTransportTime = -1;
            snapshotRequestToken++;
            readbackInFlight = false;
            nextReadback = 0;
        }

        private Dictionary<uint, GarnishFlowFrame> CaptureGarnishFlowFrames()
        {
            if (world == null) return null;
            var frames = new Dictionary<uint, GarnishFlowFrame>();
            foreach (var body in world.Items)
                if (body != null && body.IsVessel)
                    frames[body.Id] = new GarnishFlowFrame { position = body.Position, angle = body.Angle };
            return frames;
        }

        // Read only completed snapshots. Never wait on the GPU in the physics loop.
        public bool TrySampleGarnishFlow(FluidExperimentBody garnish, out Vector2 velocity, out float submerged)
            => TrySampleGarnishFlow(garnish, out velocity, out submerged, out _, out _);

        public bool TrySampleGarnishFlow(FluidExperimentBody garnish, out Vector2 velocity, out float submerged,
            out float angularVelocity, out float angularAcceleration)
        {
            velocity = Vector2.zero; submerged = 0;
            angularVelocity = angularAcceleration = 0;
            if (!IsOperational || Ledger == null || Ledger.Generation != generation
                || simulationTime - Ledger.SimulationTime > settings.solidMaximumSnapshotAge || garnishFlowFrames == null) return false;
            uint owner = garnish.ContainingVesselId;
            if (owner == 0 || !garnishFlowFrames.ContainsKey(owner)) return false;
            var vessel = garnish.ContainingVessel;
            if (vessel == null) return false;
            if (garnish.kind == LabItemKind.Ice)
                return TrySampleIceFlow(garnish, owner, out velocity, out submerged, out angularVelocity, out angularAcceleration);
            Bounds bounds = garnish.SolidBounds;
            garnish.GarnishFloatProbes(out Vector2 arm, out float thickness);
            Vector2 left = garnish.Position - arm, right = garnish.Position + arm;
            Vector2 leftFlow = Vector2.zero, rightFlow = Vector2.zero;
            float leftWeight = 0, rightWeight = 0, leftAmount = 0, rightAmount = 0;
            float leftSurface = float.NegativeInfinity, rightSurface = float.NegativeInfinity;
            float reach = Radius * 2, amount = 0, surface = float.NegativeInfinity;
            float probeReach = reach + thickness * .5f;
            float prediction = Mathf.Clamp(simulationTime - Ledger.SimulationTime, 0, settings.solidFlowPredictionSeconds);
            foreach (var p in snapshotParticles)
            {
                if (p.Active == 0 || p.VesselId != owner || p.VolumeMl <= 0) continue;
                // Liquid positions and velocities are world-space. Never attach an old
                // snapshot to the new vessel pose; predict only its own bounded motion.
                Vector2 point = p.Position + Vector2.ClampMagnitude(p.Velocity * prediction, Radius * 2);
                if (point.x < bounds.min.x - reach || point.x > bounds.max.x + reach
                    || point.y < bounds.min.y - reach || point.y > bounds.max.y + reach) continue;
                // GPU velocity and the native garnish velocity are both world-space.
                // Do not subtract vessel motion: the garnish is no longer carried by it.
                Vector2 flow = p.Velocity;
                velocity += flow * p.VolumeMl;
                amount += p.VolumeMl; surface = Mathf.Max(surface, point.y + Radius);
                void Probe(Vector2 probe, ref Vector2 sum, ref float weight, ref float volume, ref float top)
                {
                    float q = Mathf.Clamp01(1 - Vector2.Distance(point, probe) / probeReach);
                    if (q <= 0) return;
                    float w = p.VolumeMl * q * q;
                    sum += flow * w; weight += w; volume += p.VolumeMl;
                    top = Mathf.Max(top, point.y + Radius);
                }
                Probe(left, ref leftFlow, ref leftWeight, ref leftAmount, ref leftSurface);
                Probe(right, ref rightFlow, ref rightWeight, ref rightAmount, ref rightSurface);
            }
            if (amount < 1) { velocity = Vector2.zero; return false; }
            velocity = Vector2.ClampMagnitude(velocity / amount, 6);
            submerged = Mathf.Clamp01((surface - bounds.min.y) / Mathf.Max(.02f, bounds.size.y)) * Mathf.Clamp01(amount / 3);
            if (leftWeight > .0001f && rightWeight > .0001f)
            {
                // The common translation cancels. Only a spatial velocity difference spins the peel.
                Vector2 difference = rightFlow / rightWeight - leftFlow / leftWeight;
                Vector2 span = arm * 2;
                float spin = (span.x * difference.y - span.y * difference.x) / Mathf.Max(.001f, span.sqrMagnitude);
                angularVelocity = Mathf.Clamp(spin * Mathf.Rad2Deg, -180, 180);
                if (Mathf.Abs(angularVelocity) < 3) angularVelocity = 0;
            }
            float depth = Mathf.Max(Radius * 2, thickness);
            float leftWet = Mathf.Clamp01((leftSurface - left.y + depth * .5f) / depth) * Mathf.Clamp01(leftAmount / 1.5f);
            float rightWet = Mathf.Clamp01((rightSurface - right.y + depth * .5f) / depth) * Mathf.Clamp01(rightAmount / 1.5f);
            float relativeDensity = garnish.kind == LabItemKind.Ice ? settings.iceRelativeDensity : settings.garnishRelativeDensity;
            Vector2 lift = -Physics2D.gravity * (.5f * (rightWet - leftWet) / Mathf.Max(.1f, relativeDensity));
            float torque = (arm.x * lift.y - arm.y * lift.x) / Mathf.Max(.001f, arm.sqrMagnitude);
            angularAcceleration = Mathf.Clamp(torque * Mathf.Rad2Deg * .2f, -240, 240);
            if (Mathf.Abs(angularAcceleration) < 2) angularAcceleration = 0;
            return submerged > 0;
        }

        // Seed from nearby water so disconnected crests cannot drive this cube.
        // Non-colliding ice samples the local surface; colliding ice uses a
        // volume plane and connected bulk transport instead of its own wake.
        private bool TrySampleIceFlow(FluidExperimentBody ice, uint owner,
            out Vector2 velocity, out float submerged, out float angularVelocity, out float angularAcceleration)
        {
            velocity = Vector2.zero; submerged = angularVelocity = angularAcceleration = 0;
            Bounds bounds = ice.IceBuoyancyBounds;
            float reach = Radius * (IceLiquidCollisionEnabled ? 4 : 2);
            float left = bounds.min.x - reach, right = bounds.max.x + reach;
            float prediction = Mathf.Clamp(simulationTime - Ledger.SimulationTime, 0, settings.solidFlowPredictionSeconds);
            float flowWeight = 0, localAmount = 0, supportAmount = 0, ambientAmount = 0, ownerAmount = 0;
            float bulkAmount = 0; Vector2 bulkVelocity = Vector2.zero;
            Vector2 velocitySum = Vector2.zero;
            iceSurfaceSamples.Clear(); iceFallbackSurfaceSamples.Clear(); iceWakeBounds.Clear();
            if (IceLiquidCollisionEnabled)
                foreach (var other in world.Items)
                    if (other != null && other.kind == LabItemKind.Ice && !other.IsHeld
                        && other.Body.simulated && other.ContainingVesselId == owner)
                    {
                        Bounds wake = other.IceBuoyancyBounds;
                        wake.Expand(Radius * 4);
                        iceWakeBounds.Add(wake);
                    }
            for (int index = 0; index < snapshotParticles.Length; index++)
            {
                var p = snapshotParticles[index];
                if (p.Active == 0 || p.VesselId != owner || p.VolumeMl <= 0) continue;
                ownerAmount += p.VolumeMl;
                Vector2 flow = IceTransportVelocity(index, p);
                Vector2 point = p.Position + Vector2.ClampMagnitude(flow * prediction, reach);
                if (point.x < left || point.x > right) continue;
                localAmount += p.VolumeMl;
                // A detached stream or film above the cube is not water around
                // its lower half. A high column alone must never lift ice in air.
                float supportCentre = bounds.min.y + bounds.size.y*.25f;
                float supportY = Mathf.Clamp01(1-Mathf.Abs(point.y-supportCentre)/(bounds.size.y*.25f+reach));
                float supportX = Mathf.Clamp01(1-Mathf.Abs(point.x-bounds.center.x)/(bounds.extents.x+reach));
                if (supportY > 0 && supportX > 0)
                {
                    supportAmount += p.VolumeMl*supportX*supportY;
                    if (TryIceBulkFlow(index,p,out var poolFlow,out float poolAmount) && poolAmount > bulkAmount)
                    { bulkAmount = poolAmount; bulkVelocity = poolFlow; }
                }
                iceFallbackSurfaceSamples.Add(new Vector2(point.y + Radius, p.VolumeMl));
                bool surfaceBlocked = false;
                foreach (var wake in iceWakeBounds)
                    if (point.x >= wake.min.x && point.x <= wake.max.x)
                    {
                        // Surface columns must not appear/disappear as the cube
                        // moves vertically. A moving 2D wake mask changes its own
                        // buoyancy reference and repeatedly pumps a floating cube.
                        surfaceBlocked = true;
                    }
                if (!surfaceBlocked)
                {
                    float columnWeight = Mathf.Clamp01(1 - Mathf.Abs(point.x - bounds.center.x) / (bounds.extents.x + reach));
                    float volume = p.VolumeMl * columnWeight;
                    iceSurfaceSamples.Add(new Vector2(point.y + Radius, volume));
                    ambientAmount += volume;
                }
                if (surfaceBlocked) continue;
                // Flow drag is local in both axes. Liquid deep below or far above
                // this cube cannot supply its current through the vessel owner ID.
                if (point.y < bounds.min.y - reach || point.y > bounds.max.y + reach) continue;
                float q = Mathf.Clamp01(1 - Vector2.Distance(point, bounds.center) / (bounds.extents.magnitude + reach));
                float weight = p.VolumeMl * q * q;
                velocitySum += Vector2.ClampMagnitude(flow, 6) * weight;
                flowWeight += weight;
            }
            if (localAmount < 1) return false;
            // Integrating a band of the local upper bulk gives a continuous
            // surface estimate. A discrete percentile jumps by one particle row
            // when a sample crosses a moving bin, repeatedly pumping a floating cube.
            float fallback = IceSurfaceBandMean(iceFallbackSurfaceSamples, .35f, .65f);
            float surface = ambientAmount > .001f
                ? Mathf.Lerp(fallback, IceSurfaceBandMean(iceSurfaceSamples, .8f, .98f), Mathf.Clamp01(ambientAmount / 3))
                : fallback;
            // Colliding particles do not occupy the solid's interior. Their
            // moving contact films cannot define its hydrostatic immersion.
            // Volume and actual solid displacement define that plane; nearby
            // liquid evidence still gates forces during real slosh/separation.
            if (IceLiquidCollisionEnabled) surface = IcePoolLevel(ice.ContainingVessel,ownerAmount);
            ice.SampleIceImmersion(surface, out submerged, out Vector2 centre);
            submerged *= Mathf.Clamp01(supportAmount/Mathf.Max(.001f,ParticleVolumeMl));
            if (flowWeight > .0001f) velocity = velocitySum / flowWeight;
            if (bulkAmount >= 1) velocity = bulkVelocity;
            Vector2 lift = -Physics2D.gravity * (ice.Body.mass * ice.Body.gravityScale * submerged / Mathf.Max(.1f, settings.iceRelativeDensity));
            Vector2 arm = centre - ice.Body.worldCenterOfMass;
            angularAcceleration = (arm.x * lift.y - arm.y * lift.x) / Mathf.Max(1e-8f, ice.Body.inertia) * Mathf.Rad2Deg;
            return submerged > 0;
        }

        private static float IceSurfaceBandMean(List<Vector2> samples, float lower, float upper)
        {
            samples.Sort((a, b) => a.x.CompareTo(b.x));
            float total = 0; foreach (var sample in samples) total += sample.y;
            float begin = total * lower, end = total * upper, amount = 0, weight = 0, sum = 0;
            foreach (var sample in samples)
            {
                float overlap = Mathf.Max(0, Mathf.Min(amount + sample.y, end) - Mathf.Max(amount, begin));
                sum += sample.x * overlap; weight += overlap; amount += sample.y;
            }
            return weight > 0 ? sum / weight : float.NegativeInfinity;
        }
    }
}
