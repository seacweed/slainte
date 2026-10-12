using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    // Shards deliberately have no FluidExperimentBody: no pickup, vessel ownership,
    // liquid boundary, liquid mask, loose-content transport or chain-breaking policy.
    public sealed class FluidExperimentGlassShard : MonoBehaviour
    {
        public Rigidbody2D Body { get; private set; }
        public PolygonCollider2D SolidCollider { get; private set; }
        public FluidExperimentBreakableGlass Source { get; private set; }
        public int DefinitionIndex { get; private set; }
        private Vector2 initialVelocity;
        private float initialSpin;

        internal static FluidExperimentGlassShard Create(FluidExperimentBreakableGlass source,
            FluidExperimentWorld world, int index, float totalArea, Vector2 center)
        {
            var definition = source.profile.shards[index];
            var original = source.Body;
            Vector2 position = original.LocalToWorld(definition.localCenter);
            var root = new GameObject("HurricaneShard_" + (index + 1).ToString("00"));
            root.SetActive(false);
            root.layer = world.itemLayer;
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, original.Angle));
            root.transform.localScale = original.transform.lossyScale;
            root.transform.SetParent(world.transform, true);
            var shard = root.AddComponent<FluidExperimentGlassShard>();
            shard.Source = source; shard.DefinitionIndex = index;
            shard.Body = root.AddComponent<Rigidbody2D>();
            shard.Body.mass = original.Body.mass * definition.area / totalArea;
            shard.Body.gravityScale = original.Body.gravityScale;
            shard.Body.linearDamping = source.profile.linearDamping;
            shard.Body.angularDamping = source.profile.angularDamping;
            shard.Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            shard.Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            shard.initialVelocity = original.Body.GetPointVelocity(position);
            // Small coherent separation; area weighting makes the added net momentum zero.
            float extent = Mathf.Max(.1f, original.SolidBounds.extents.magnitude);
            shard.initialVelocity += (position - center) * (source.profile.separationSpeed / extent);
            shard.initialSpin = original.Body.angularVelocity;
            shard.SolidCollider = root.AddComponent<PolygonCollider2D>();
            shard.SolidCollider.points = definition.outline;
            shard.SolidCollider.sharedMaterial = source.profile.shardMaterial;
            var art = new GameObject("Art"); art.transform.SetParent(root.transform, false);
            art.layer = world.itemLayer;
            art.transform.localScale = new Vector3(definition.spriteScale.x, definition.spriteScale.y, 1);
            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = definition.sprite;
            var originalArt = original.GetComponentInChildren<SpriteRenderer>();
            if (originalArt != null)
            {
                renderer.sharedMaterial = originalArt.sharedMaterial;
                renderer.sortingLayerID = originalArt.sortingLayerID;
            }
            renderer.sortingOrder = 4;
            world.RegisterGlassShard(shard);
            return shard;
        }
        internal void Activate()
        {
            gameObject.SetActive(true);
            // Native body activation can reset values set while the GameObject was
            // inactive. Apply inherited motion after the collider/body exists in Physics2D.
            Body.linearVelocity = initialVelocity;
            Body.angularVelocity = initialSpin;
            Body.WakeUp();
        }
    }
}
