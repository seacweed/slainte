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

        internal void RegisterBreakable(FluidExperimentBreakableGlass glass)
        { if (!breakableGlasses.Contains(glass)) breakableGlasses.Add(glass); }
        internal void UnregisterBreakable(FluidExperimentBreakableGlass glass)
        { breakableGlasses.Remove(glass); pendingGlassBreakage.Remove(glass); }
        internal void QueueGlassBreakage(FluidExperimentBreakableGlass glass)
        { if (!pendingGlassBreakage.Contains(glass)) pendingGlassBreakage.Add(glass); }
        internal void RegisterGlassShard(FluidExperimentGlassShard shard) => glassShards.Add(shard);

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
