using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentWorld
    {
        private sealed class ShardContactPair
        {
            public Collider2D a, b;
            public FluidExperimentBody item;
            public FluidExperimentGlassShard otherShard;
            public bool environment, waiting;
        }
        private readonly Dictionary<ulong, ShardContactPair> shardContactPairs = new Dictionary<ulong, ShardContactPair>();
        private readonly List<ulong> expiredShardContactPairs = new List<ulong>();
        private readonly HashSet<Collider2D> shardEnvironment = new HashSet<Collider2D>();
        public int ShardContactPairCount => shardContactPairs.Count;
        public int PendingShardContactCount { get; private set; }
        private const float ShardInitialOverlapTolerance = .0005f;
        private const float ShardContactRearmDistance = .002f;

        private static ulong ShardPairKey(Collider2D a, Collider2D b)
        {
            int x = a.GetInstanceID(), y = b.GetInstanceID();
            return x < y ? ((ulong)(uint)x << 32) | (uint)y : ((ulong)(uint)y << 32) | (uint)x;
        }
        private static bool ShardColliderReady(Collider2D collider) => collider != null && collider.isActiveAndEnabled
            && (collider.attachedRigidbody == null || collider.attachedRigidbody.simulated);
        private static void SetShardPairIgnored(Collider2D a, Collider2D b, bool ignored)
        {
            if (ShardColliderReady(a) && ShardColliderReady(b) && Physics2D.GetIgnoreCollision(a, b) != ignored)
                Physics2D.IgnoreCollision(a, b, ignored);
        }
        private static bool ShardItemCanCollide(FluidExperimentBody item) => item != null
            && item.isActiveAndEnabled && !item.SuppressHeldSolidContacts && !item.IsInHeldVessel && !item.IsLooseSolid
            && !item.DispenserCollisionSuppressed;

        private void RefreshShardContactPairs()
        {
            if (glassShards.Count == 0) { ClearShardContactPairs(); return; }
            shardEnvironment.Clear();
            foreach (var floor in floorColliders) if (floor != null) shardEnvironment.Add(floor);
            foreach (var hull in EnvironmentHulls) if (hull.Collider != null) shardEnvironment.Add(hull.Collider);
            var colliders = GetComponentsInChildren<Collider2D>(true);
            foreach (var shard in glassShards)
            {
                if (shard == null || !shard.isActiveAndEnabled) continue;
                foreach (var collider in colliders)
                    RegisterShardContactPair(shard.SolidCollider, collider, shardEnvironment.Contains(collider));
                // Bound environment references can live outside this World's hierarchy.
                foreach (var collider in shardEnvironment) RegisterShardContactPair(shard.SolidCollider, collider, true);
            }
            UpdateShardContactPairs();
        }
        private void RegisterShardContactPair(Collider2D a, Collider2D b, bool environment)
        {
            if (a == null || b == null || a == b) return;
            ulong key = ShardPairKey(a, b);
            if (!ShardColliderReady(a) || !ShardColliderReady(b)) { shardContactPairs.Remove(key); return; }
            var otherShard = b.GetComponentInParent<FluidExperimentGlassShard>();
            var item = otherShard == null ? b.GetComponentInParent<FluidExperimentBody>() : null;
            bool shakerPart = b.GetComponentInParent<FluidExperimentShakerPart>() != null;
            bool eligible = !b.isTrigger && (environment || otherShard != null || (!shakerPart && ShardItemCanCollide(item)));
            if (!eligible)
            {
                SetShardPairIgnored(a, b, true); shardContactPairs.Remove(key); return;
            }
            if (!shardContactPairs.TryGetValue(key, out var pair))
            {
                // Only the first encounter can defer a pair. Ordinary later
                // contact must never turn an armed collision off again.
                var distance = a.Distance(b);
                pair = new ShardContactPair { a = a, b = b, item = item, otherShard = otherShard,
                    environment = environment, waiting = !environment && (!distance.isValid
                        || distance.distance < -ShardInitialOverlapTolerance) };
                shardContactPairs.Add(key, pair);
            }
            SetShardPairIgnored(pair.a, pair.b, pair.waiting);
        }
        private void UpdateShardContactPairs()
        {
            expiredShardContactPairs.Clear(); PendingShardContactCount = 0;
            foreach (var entry in shardContactPairs)
            {
                var pair = entry.Value;
                bool ready = ShardColliderReady(pair.a) && ShardColliderReady(pair.b);
                bool eligible = pair.environment || (pair.otherShard != null && pair.otherShard.isActiveAndEnabled)
                    || ShardItemCanCollide(pair.item);
                if (!ready || !eligible || pair.b.isTrigger)
                {
                    SetShardPairIgnored(pair.a, pair.b, true); expiredShardContactPairs.Add(entry.Key); continue;
                }
                if (pair.waiting)
                {
                    var distance = pair.a.Distance(pair.b);
                    if (distance.isValid && distance.distance >= ShardContactRearmDistance) pair.waiting = false;
                }
                if (pair.waiting) PendingShardContactCount++;
                SetShardPairIgnored(pair.a, pair.b, pair.waiting);
            }
            foreach (ulong key in expiredShardContactPairs) shardContactPairs.Remove(key);
        }
        private void ClearShardContactPairs()
        {
            shardContactPairs.Clear(); expiredShardContactPairs.Clear(); PendingShardContactCount = 0;
        }
    }
}
