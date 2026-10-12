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
        public Shard[] shards = Array.Empty<Shard>();
        [Min(0)] public float minimumImpactSpeed = 3.5f;
        [Tooltip("Minimum normal contact impulse divided by the original glass mass.")]
        [Min(0)] public float minimumImpulsePerMass = 2;
        [Min(0)] public float separationSpeed = .12f;
        [Min(0)] public float linearDamping = .2f;
        [Min(0)] public float angularDamping = .3f;
        public PhysicsMaterial2D shardMaterial;
    }
}
