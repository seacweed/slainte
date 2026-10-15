using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    // Shards deliberately have no FluidExperimentBody: no pickup, vessel ownership,
    // liquid ownership, loose-content transport or chain-breaking policy. Production
    // worlds can attach a separate thin-wall liquid contact proxy to these fragments.
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
            var root = FluidExperimentObjectFactory.CreateInactive(source.profile.shardTemplate,
                world.transform, original.name + "_Shard_" + (index + 1).ToString("00"));
            root.layer = world.itemLayer;
            root.transform.SetParent(null, true);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, original.Angle));
            root.transform.localScale = original.transform.lossyScale;
            root.transform.SetParent(world.transform, true);
            var shard = root.GetComponent<FluidExperimentGlassShard>();
            if (shard == null) shard = root.AddComponent<FluidExperimentGlassShard>();
            shard.Source = source; shard.DefinitionIndex = index;
            shard.Body = root.GetComponent<Rigidbody2D>();
            if (shard.Body == null) shard.Body = root.AddComponent<Rigidbody2D>();
            shard.Body.bodyType = RigidbodyType2D.Dynamic;
            shard.Body.simulated = true;
            shard.Body.constraints = RigidbodyConstraints2D.None;
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
            if (world.ExplosiveGlassBreakage)
            {
                // G deliberately adds burst energy to the fragments only. Keep the source's
                // inherited motion and the F path unchanged, including released contents.
                Vector2 outward = position - center;
                if (outward.sqrMagnitude > .000001f) outward.Normalize();
                else
                {
                    // A centered fragment still needs a stable direction without consuming
                    // global randomness that could alter unrelated comparison scenarios.
                    float angle = (original.Angle + index * 360f / source.profile.shards.Length) * Mathf.Deg2Rad;
                    outward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
                shard.initialVelocity += outward * Mathf.Max(0, source.profile.explosionSpeed)
                    + Vector2.up * Mathf.Max(0, source.profile.explosionUpwardSpeed);
                shard.initialSpin += (index % 2 == 0 ? 1 : -1) * Mathf.Max(0, source.profile.explosionAngularSpeed);
            }
            shard.SolidCollider = root.GetComponent<PolygonCollider2D>();
            if (shard.SolidCollider == null) shard.SolidCollider = root.AddComponent<PolygonCollider2D>();
            shard.SolidCollider.enabled = true; shard.SolidCollider.isTrigger = false;
            shard.SolidCollider.points = definition.outline;
            shard.SolidCollider.sharedMaterial = source.profile.shardMaterial;
            var artTransform = root.transform.Find("Art");
            var art = artTransform != null ? artTransform.gameObject : new GameObject("Art");
            art.transform.SetParent(root.transform, false);
            art.transform.localPosition = Vector3.zero; art.transform.localRotation = Quaternion.identity;
            art.layer = world.itemLayer;
            art.transform.localScale = new Vector3(definition.spriteScale.x, definition.spriteScale.y, 1);
            var renderer = art.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = art.AddComponent<SpriteRenderer>();
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
