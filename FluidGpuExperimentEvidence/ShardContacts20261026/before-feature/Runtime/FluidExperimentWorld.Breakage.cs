using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentWorld
    {
        private readonly List<FluidExperimentBreakableGlass> breakableGlasses = new List<FluidExperimentBreakableGlass>();
        private readonly List<FluidExperimentBreakableGlass> pendingGlassBreakage = new List<FluidExperimentBreakableGlass>();
        private readonly List<FluidExperimentGlassShard> glassShards = new List<FluidExperimentGlassShard>();
        public IReadOnlyList<FluidExperimentGlassShard> GlassShards => glassShards;
        public bool ExplosiveGlassBreakage { get; internal set; }
        [Tooltip("Upload only the original thin glass walls intersecting each fragment to the liquid solver.")]
        public bool shardLiquidCollision;
        [Range(0, 1024)] public int maximumShardLiquidSegments = 512;
        private readonly List<FluidExperimentShardLiquidProxy> shardLiquidProxies = new List<FluidExperimentShardLiquidProxy>();
        internal IReadOnlyList<FluidExperimentShardLiquidProxy> ShardLiquidProxies => shardLiquidProxies;

        internal void RegisterBreakable(FluidExperimentBreakableGlass glass)
        { if (!breakableGlasses.Contains(glass)) breakableGlasses.Add(glass); }
        internal void UnregisterBreakable(FluidExperimentBreakableGlass glass)
        { breakableGlasses.Remove(glass); pendingGlassBreakage.Remove(glass); }
        internal void QueueGlassBreakage(FluidExperimentBreakableGlass glass)
        { if (!pendingGlassBreakage.Contains(glass)) pendingGlassBreakage.Add(glass); }
        internal void RegisterGlassShard(FluidExperimentGlassShard shard)
        {
            glassShards.Add(shard);
            if (!shardLiquidCollision) return;
            var proxy = shard.gameObject.AddComponent<FluidExperimentShardLiquidProxy>();
            proxy.Initialize(shard);
            shardLiquidProxies.Add(proxy);
        }

        private void SynchronizeShardHistory()
        {
            for (int i = shardLiquidProxies.Count - 1; i >= 0; i--)
            {
                var proxy = shardLiquidProxies[i];
                if (proxy == null) { shardLiquidProxies.RemoveAt(i); continue; }
                proxy.SynchronizeHistory();
            }
        }

        private void CaptureGlassBreakageMotion()
        {
            foreach (var glass in breakableGlasses) if (glass != null) glass.CaptureBeforePhysics();
        }
        private void ProcessGlassBreakage()
        {
            for (int i = 0; i < pendingGlassBreakage.Count; i++)
                if (pendingGlassBreakage[i] != null) pendingGlassBreakage[i].CompleteBreak();
            pendingGlassBreakage.Clear();
            if (liquid == null || liquid.settings == null) return;
            Vector2 min = liquid.settings.gpuLiquidWorldMin - Vector2.one * 2;
            Vector2 max = liquid.settings.gpuLiquidWorldMax + Vector2.one * 2;
            for (int i = glassShards.Count - 1; i >= 0; i--)
            {
                var shard = glassShards[i];
                if (shard == null) { glassShards.RemoveAt(i); continue; }
                Vector2 p = shard.Body.position;
                if (p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y) continue;
                shard.gameObject.SetActive(false); Destroy(shard.gameObject); glassShards.RemoveAt(i);
            }
        }
        private void ResetGlassBreakage()
        {
            pendingGlassBreakage.Clear();
            foreach (var shard in glassShards)
                if (shard != null) { shard.gameObject.SetActive(false); Destroy(shard.gameObject); }
            glassShards.Clear();
            shardLiquidProxies.Clear();
            // Original components survive fracture, keeping comparison references valid.
            foreach (var glass in breakableGlasses) if (glass != null) glass.RestoreIntact();
        }
        internal void RefreshGlassShardContacts()
        {
            if (glassShards.Count == 0) return;
            var environment = new HashSet<Collider2D>();
            foreach (var floor in floorColliders) if (floor != null) environment.Add(floor);
            foreach (var hull in EnvironmentHulls) if (hull.Collider != null) environment.Add(hull.Collider);
            // Per-collider pairs keep this policy local to the experiment. Do not alter
            // project layers or the global collision matrix used by other scenes.
            var colliders = GetComponentsInChildren<Collider2D>(true);
            foreach (var shard in glassShards)
            {
                if (shard == null || !shard.isActiveAndEnabled) continue;
                foreach (var collider in colliders)
                    if (collider != null && collider != shard.SolidCollider && collider.isActiveAndEnabled)
                        Physics2D.IgnoreCollision(shard.SolidCollider, collider, !environment.Contains(collider));
                foreach (var collider in environment)
                    if (collider.isActiveAndEnabled) Physics2D.IgnoreCollision(shard.SolidCollider, collider, false);
            }
        }
    }
}
