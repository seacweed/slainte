using System;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    [CreateAssetMenu(menuName = "Slainte/Fluid Experiment/Glass Breakage")]
    public sealed class FluidExperimentBreakageProfile : ScriptableObject
    {
        [Serializable]
        public sealed class Shard
        {
            public Sprite sprite;
            public Vector2 localCenter;
            public Vector2[] outline = Array.Empty<Vector2>();
            public float area;
            public Vector2 spriteScale = Vector2.one;
        }
        [Header("Breakage Thresholds (both must be reached)")]
        [Tooltip("Minimum normal contact speed in world units/second, including rotation. Increase to resist faster impacts.")]
        [Min(0)] public float minimumImpactSpeed = 6f;
        [Tooltip("Minimum normal contact impulse divided by the original glass mass. Increase to resist stronger impacts. Both thresholds must be reached to break.")]
        [Min(0)] public float minimumImpulsePerMass = 4f;

        [Header("Shard Motion (after breaking)")]
        [Tooltip("Extra outward shard speed after breaking. Does not affect when the glass breaks.")]
        [Min(0)] public float separationSpeed = .12f;
        [Tooltip("How quickly shards lose linear speed after breaking.")]
        [Min(0)] public float linearDamping = .2f;
        [Tooltip("How quickly shards lose rotation speed after breaking.")]
        [Min(0)] public float angularDamping = .3f;
        [Tooltip("Friction and bounce of the broken shards.")]
        public PhysicsMaterial2D shardMaterial;

        [Header("Authored Shard Geometry")]
        public Shard[] shards = Array.Empty<Shard>();
    }
}
