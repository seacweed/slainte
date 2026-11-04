using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentWorld
    {
        [Header("Visual sorting (F/G only)")]
        public bool enableObjectSorting = true;
        public bool ObjectSortingActive => enableObjectSorting && liquid != null && liquid.CohesivePhysicsActive;
        internal const int LiquidVisualOffset = 4, ContentsVisualOffset = 6;
        private sealed class VisualGroup
        {
            public Component owner;
            public long sequence, initialSequence;
            public int order, layer;
            public bool wasHeld;
        }
        private struct AuthoredSorting { public int order, layer; }
        [System.Serializable]
        private sealed class SavedAuthoredSorting { public SpriteRenderer renderer; public int order, layer; }
        [SerializeField, HideInInspector] private List<SavedAuthoredSorting> savedAuthoredSorting = new List<SavedAuthoredSorting>();
        private readonly Dictionary<SpriteRenderer, AuthoredSorting> authoredSorting = new Dictionary<SpriteRenderer, AuthoredSorting>();
        private readonly Dictionary<Component, VisualGroup> visualGroups = new Dictionary<Component, VisualGroup>();
        private readonly List<VisualGroup> activeVisualGroups = new List<VisualGroup>();
        private readonly List<SpriteRenderer> activeVisualRenderers = new List<SpriteRenderer>();
        private readonly Dictionary<FluidExperimentBody, FluidExperimentBody> visualContainers = new Dictionary<FluidExperimentBody, FluidExperimentBody>();
        private long visualBirth, visualForeground = 1000000000;
        private bool appliedVisualSorting;

        private void LateUpdate() => UpdateVisualSorting();

        private void RememberVisualRenderer(SpriteRenderer renderer)
        {
            if (renderer != null && !authoredSorting.ContainsKey(renderer))
            {
                foreach (var saved in savedAuthoredSorting)
                    if (saved.renderer == renderer)
                    { authoredSorting.Add(renderer, new AuthoredSorting { order = saved.order, layer = saved.layer }); return; }
                var original = new AuthoredSorting { order = renderer.sortingOrder, layer = renderer.sortingLayerID };
                authoredSorting.Add(renderer, original);
                savedAuthoredSorting.Add(new SavedAuthoredSorting { renderer = renderer, order = original.order, layer = original.layer });
            }
        }
        private void RememberVisualItem(FluidExperimentBody body)
        {
            foreach (var renderer in body.GetComponentsInChildren<SpriteRenderer>(true)) RememberVisualRenderer(renderer);
        }
        private VisualGroup EnsureVisualGroup(Component owner)
        {
            if (visualGroups.TryGetValue(owner, out var group)) return group;
            int initialOrder = -100, layer = 0;
            foreach (var renderer in owner.GetComponentsInChildren<SpriteRenderer>(true))
            {
                RememberVisualRenderer(renderer);
                var authored = authoredSorting[renderer];
                if (authored.order >= initialOrder) { initialOrder = authored.order; layer = authored.layer; }
            }
            long sequence = initialOrder * 1000000L + ++visualBirth;
            group = new VisualGroup { owner = owner, sequence = sequence, initialSequence = sequence, layer = layer };
            visualGroups.Add(owner, group);
            return group;
        }
        private void AddVisualOwner(Component owner, bool held)
        {
            if (owner == null) return;
            var group = EnsureVisualGroup(owner);
            if (activeVisualGroups.Contains(group)) return;
            if (held && !group.wasHeld) group.sequence = ++visualForeground;
            group.wasHeld = held;
            activeVisualGroups.Add(group);
        }
        private Component RendererEntity(SpriteRenderer renderer)
        {
            foreach (var part in renderer.GetComponentsInParent<FluidExperimentShakerPart>(true))
                if (!part.IsAttached) return part;
            var body = renderer.GetComponentInParent<FluidExperimentBody>();
            if (body != null) return body;
            var source = renderer.GetComponentInParent<FluidExperimentGarnishSource>();
            if (source != null) return source;
            return renderer.GetComponentInParent<FluidExperimentGlassShard>();
        }
        private Component DisplayOwner(Component entity)
        {
            if (entity is FluidExperimentBody body && visualContainers.TryGetValue(body, out var vessel)) return vessel;
            return entity;
        }
        private FluidExperimentBody SpoonContainer(FluidExperimentBody spoon)
        {
            if (spoon.collisionProfile == null) return null;
            Vector2 tip = Vector2.zero; float low = float.PositiveInfinity;
            foreach (var hull in spoon.collisionProfile.solids)
                foreach (var point in hull.points) if (point.y < low) { low = point.y; tip = point; }
            Vector2 probe = spoon.LocalToWorld(tip);
            FluidExperimentBody best = null;
            foreach (var vessel in items)
                if (vessel != null && vessel != spoon && vessel.IsVessel && vessel.Body.simulated && vessel.ContainsLiquid(probe)
                    && (best == null || EnsureVisualGroup(vessel).sequence > EnsureVisualGroup(best).sequence)) best = vessel;
            return best;
        }
        public void UpdateVisualSorting()
        {
            if (!ObjectSortingActive)
            {
                if (appliedVisualSorting)
                    foreach (var pair in authoredSorting) if (pair.Key != null)
                    { pair.Key.sortingOrder = pair.Value.order; pair.Key.sortingLayerID = pair.Value.layer; }
                appliedVisualSorting = false;
                return;
            }
            activeVisualGroups.Clear(); activeVisualRenderers.Clear(); visualContainers.Clear();
            foreach (var body in items)
            {
                if (body == null || !body.isActiveAndEnabled) continue;
                RememberVisualItem(body);
                if (body.IsLooseSolid && !body.IsHeld && body.ContainingVessel != null)
                    visualContainers[body] = body.ContainingVessel;
                else if (body.kind == LabItemKind.Spoon)
                {
                    var container = SpoonContainer(body);
                    if (container != null) visualContainers[body] = container;
                }
                Component owner = DisplayOwner(body);
                AddVisualOwner(owner, owner is FluidExperimentBody ownerBody && ownerBody.IsHeld);
                foreach (var renderer in body.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (!renderer.gameObject.activeInHierarchy) continue;
                    var entity = RendererEntity(renderer);
                    if (entity is FluidExperimentShakerPart part) AddVisualOwner(part, part.IsHeld);
                    activeVisualRenderers.Add(renderer);
                }
            }
            // Detached parts live beside their owner, and a strainer can carry its attached cap.
            foreach (var part in GetComponentsInChildren<FluidExperimentShakerPart>())
            {
                if (part.IsAttached) continue;
                AddVisualOwner(part, part.IsHeld);
                foreach (var renderer in part.GetComponentsInChildren<SpriteRenderer>())
                { RememberVisualRenderer(renderer); activeVisualRenderers.Add(renderer); }
            }
            foreach (var source in GetComponentsInChildren<FluidExperimentGarnishSource>())
            {
                AddVisualOwner(source, false);
                foreach (var renderer in source.GetComponentsInChildren<SpriteRenderer>())
                { RememberVisualRenderer(renderer); activeVisualRenderers.Add(renderer); }
            }
            foreach (var shard in glassShards)
            {
                if (shard == null || !shard.isActiveAndEnabled) continue;
                AddVisualOwner(shard, false);
                foreach (var renderer in shard.GetComponentsInChildren<SpriteRenderer>())
                { RememberVisualRenderer(renderer); activeVisualRenderers.Add(renderer); }
            }
            activeVisualGroups.Sort((a, b) => a.sequence.CompareTo(b.sequence));
            int ordinary = 0, foreground = 0;
            foreach (var group in activeVisualGroups)
                group.order = group.wasHeld ? 28000 + Mathf.Min(200, foreground++) * 16 : 100 + Mathf.Min(1600, ordinary++) * 16;
            foreach (var renderer in activeVisualRenderers)
            {
                var entity = RendererEntity(renderer); var owner = DisplayOwner(entity);
                if (owner == null || !visualGroups.TryGetValue(owner, out var group)) continue;
                renderer.sortingLayerID = group.layer;
                renderer.sortingOrder = group.order + RendererVisualOffset(renderer, entity, owner);
            }
            appliedVisualSorting = true;
        }
        private int RendererVisualOffset(SpriteRenderer renderer, Component entity, Component owner)
        {
            if (entity != owner) return ContentsVisualOffset;
            string sprite = renderer.sprite != null ? renderer.sprite.name : string.Empty;
            if (sprite.Contains("_back")) return sprite.Contains("line") ? 1 : 0;
            if (sprite.Contains("_front")) return sprite.Contains("line") || sprite.Contains("white") ? 9 : 8;
            if (sprite.Contains("strainer")) return 10;
            if (sprite.Contains("lid")) return 11;
            if (entity is FluidExperimentGarnishSource)
                return renderer.name == "ContentsIcon" ? 9 : 8;
            return 8;
        }
        internal bool TryGetLiquidVisualOrder(uint vesselId, out int layer, out int order)
        {
            layer = 0; order = 0;
            if (!ObjectSortingActive || vesselId == 0) return vesselId == 0;
            foreach (var body in items)
                if (body != null && body.Id == vesselId && body.IsVessel && visualGroups.TryGetValue(body, out var group))
                { layer = group.layer; order = group.order + LiquidVisualOffset; return true; }
            return false;
        }
        public long VisualSortKey(Component candidate)
        {
            long best = long.MinValue;
            if (candidate == null) return best;
            foreach (var renderer in candidate.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!renderer.enabled || (!(candidate is FluidExperimentShakerPart) && RendererEntity(renderer) != candidate)) continue;
                long key = (long)SortingLayer.GetLayerValueFromID(renderer.sortingLayerID) * 65536L + renderer.sortingOrder + 32768L;
                if (key > best) best = key;
            }
            return best;
        }
        private void ResetVisualSorting()
        {
            foreach (var group in visualGroups.Values) { group.sequence = group.initialSequence; group.wasHeld = false; }
            visualForeground = 1000000000;
            UpdateVisualSorting();
        }
    }
}
