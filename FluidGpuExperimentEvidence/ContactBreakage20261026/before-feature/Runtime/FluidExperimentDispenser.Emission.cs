using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentDispenser
    {
        private readonly List<FluidExperimentBody> spawnedIce = new List<FluidExperimentBody>();
        private float waterCredit, iceClock, streamRadius;
        private uint streamId, lastToken;
        public int SpawnedIceCount { get; private set; }

        /// <summary>Run after native motion and before GPU submission. Only accepted emissions consume
        /// the shared stock; blocked or exhausted queues never accumulate a delayed burst.</summary>
        public void Tick(float dt)
        {
            CleanupIce();
            if (!IsDispensing || dt <= 0) { ClearEmissionClock(); return; }
            BlockedReason = null;
            if (Mode == FluidExperimentDispenserMode.Ice) EmitIce(dt);
            else if (Mode == FluidExperimentDispenserMode.Hot) EmitHotWater(dt);
        }
        private void StopEmission()
        { dispensing = false; ClearEmissionClock(); BlockedReason = null; }
        private void ClearEmissionClock()
        { waterCredit = iceClock = 0; streamId = lastToken = 0; }

        private bool SpawnBlocked(Vector2 point, float radius)
        {
            // The upright held body uses triggers, so check its authored solid explicitly.
            if (CollisionSuppressed && body.SolidClearance(point - body.Position) <= radius) return true;
            int count = Physics2D.OverlapCircle(point, radius, SolidFilter, overlapHits);
            for (int i = 0; i < count; i++)
            {
                Collider2D c = overlapHits[i];
                if (c == null || c.isTrigger || !c.enabled) continue;
                // Own geometry is included: an incorrectly authored nozzle must not generate inside the tool.
                return true;
            }
            return count == overlapHits.Length;
        }
        private void EmitHotWater(float dt)
        {
            var liquid = body.World.Liquid;
            if (hotWater == null || !liquid.IsOperational || hotFlowMlPerSecond <= 0)
            { ClearEmissionClock(); BlockedReason = "Water unavailable"; return; }
            Vector2 nozzle = MouthWorld + Vector2.down * (liquid.Radius * 1.5f);
            if (SpawnBlocked(nozzle, liquid.Radius * 1.05f))
            { ClearEmissionClock(); BlockedReason = "Nozzle blocked"; return; }
            float oldCredit = waterCredit;
            waterCredit += hotFlowMlPerSecond * dt;
            float particleVolume = liquid.ParticleVolumeMl;
            float speed = Mathf.Min(liquid.settings.gpuLiquidMaximumSpeed,
                Mathf.Max(exitSpeed, hotFlowMlPerSecond / Mathf.Max(.0001f, particleVolume) * liquid.Radius * 1.75f));
            // Vertical emission deliberately does not inherit cursor velocity or automatic return motion.
            Vector2 velocity = Vector2.down * speed;
            int emitted = 0;
            float consumed = 0;
            while (remainingMl > .000001f)
            {
                float amount = Mathf.Min(particleVolume, remainingMl);
                if (waterCredit + .000001f < amount) break;
                if (++emitted > 64) { ClearEmissionClock(); break; }
                if (streamId == 0) streamId = liquid.NewPourStream();
                float delay = Mathf.Clamp((consumed + amount - oldCredit) / hotFlowMlPerSecond, 0, dt);
                streamRadius = Mathf.Clamp(body.mouthWidth * .5f, .018f, liquid.Radius * 1.5f);
                if (!liquid.TryEmitStream(nozzle, velocity, hotWater, amount, body.Id, streamId,
                    lastToken, delay, streamRadius, out uint token))
                { ClearEmissionClock(); BlockedReason = "Liquid capacity reached"; return; }
                lastToken = token; remainingMl = Mathf.Max(0, remainingMl - amount);
                waterCredit = Mathf.Max(0, waterCredit - amount); consumed += amount;
            }
            if (remainingMl <= 0) ClearEmissionClock();
        }
        private void EmitIce(float dt)
        {
            if (icePrefab == null) { ClearEmissionClock(); BlockedReason = "Ice unavailable"; return; }
            iceClock += dt;
            if (iceClock + .000001f < Mathf.Max(.02f, iceInterval)) return;
            // At most one birth per physics tick. A blocked chute is retried at its regular cadence.
            iceClock = 0;
            float radius = .27f;
            Vector2 position = MouthWorld + Vector2.down * (radius + .025f);
            if (SpawnBlocked(position, radius)) { BlockedReason = "Nozzle blocked"; return; }
            if (!body.World.Liquid.CanAdmitDispenserIce(icePrefab))
            { BlockedReason = "Ice capacity reached"; return; }
            FluidExperimentBody piece = Instantiate(icePrefab, position, Quaternion.identity, body.World.transform);
            piece.MarkPlaced(); piece.Teleport(position, 0);
            piece.Body.linearVelocity = Vector2.down * Mathf.Max(.1f, exitSpeed);
            piece.Body.angularVelocity = (SpawnedIceCount % 2 == 0 ? 1 : -1) * 18;
            spawnedIce.Add(piece); SpawnedIceCount++;
            remainingMl = Mathf.Max(0, remainingMl - 10);
        }
        private void CleanupIce()
        {
            if (body == null || body.World == null || body.World.Liquid == null || body.World.Liquid.settings == null) return;
            var settings = body.World.Liquid.settings;
            Vector2 min = settings.gpuLiquidWorldMin - Vector2.one, max = settings.gpuLiquidWorldMax + Vector2.one;
            for (int i = spawnedIce.Count - 1; i >= 0; i--)
            {
                var ice = spawnedIce[i];
                if (ice == null || !ice.isActiveAndEnabled) { spawnedIce.RemoveAt(i); continue; }
                Vector2 p = ice.Position;
                if (p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y) continue;
                ice.gameObject.SetActive(false); Destroy(ice.gameObject); spawnedIce.RemoveAt(i);
            }
        }
        internal bool TryGetStreamHead(out GpuLiquidStreamHead head)
        {
            head = default;
            if (!IsDispensing || Mode != FluidExperimentDispenserMode.Hot || streamId == 0 || lastToken == 0) return false;
            head = new GpuLiquidStreamHead { Lip = transform.TransformPoint(mouthLocal), Token = lastToken,
                StreamId = streamId, SourceId = body.Id, Radius = streamRadius };
            return true;
        }
    }

    public sealed partial class FluidExperimentGpuLiquid
    {
        // Reserve space before instantiation. Existing solvers keep their fixed geometry budgets;
        // stock stays available if too many solids remain on screen at once.
        public bool CanAdmitDispenserIce(FluidExperimentBody prefab)
        {
            if (!IsOperational || world == null || prefab == null) return false;
            int count = 64 + BoundaryCost(prefab);
            foreach (var item in world.Items) if (item != null) count += BoundaryCost(item);
            // Runtime rigs can author more environment walls than the comparison scene.
            // Count their actual geometry rather than assuming the fixed reserve covers it.
            foreach (var hull in world.EnvironmentHulls)
                if (hull.Collider != null && hull.Collider.isActiveAndEnabled) count += hull.Points.Length;
            if (world.shardLiquidCollision && CohesivePhysicsActive)
            {
                // Mirror the complete-proxy selection used by UploadShardGeometry. Admitting
                // new ice must not consume the space already needed by active glass shards.
                int shardBudget = Mathf.Clamp(world.maximumShardLiquidSegments, 0, 1024);
                foreach (var proxy in world.ShardLiquidProxies)
                {
                    if (proxy == null || !proxy.isActiveAndEnabled || proxy.Body == null || !proxy.Body.simulated) continue;
                    int segments = proxy.Segments.Count;
                    if (segments > shardBudget) continue;
                    count += segments; shardBudget -= segments;
                }
            }
            // Each non-empty group owns at least one segment, so the same bound also
            // protects the equally sized group buffer (including environment and shards).
            return count < MaximumBoundarySegments;
        }
        private static int BoundaryCost(FluidExperimentBody item)
        {
            int count = 0;
            if (item.collisionProfile != null)
            {
                foreach (var hull in item.collisionProfile.solids) count += hull.points.Length;
                count += item.collisionProfile.lid.Length;
                if (item.IsVessel) count += item.LiquidInteriorPath.Length;
                if (item.kind == LabItemKind.Shaker) count += 64;
            }
            else count = item.liquidWall.Length;
            foreach (var hull in item.extraSolidHulls) count += hull.points.Length;
            return count;
        }
    }
}
