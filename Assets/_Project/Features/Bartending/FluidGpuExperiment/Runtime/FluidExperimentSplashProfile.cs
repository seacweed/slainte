using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    [CreateAssetMenu(menuName = "Slainte/Fluid Experiment/Liquid Splash Profile", fileName = "LiquidSplash")]
    public sealed class FluidExperimentSplashProfile : ScriptableObject
    {
        [Header("Impact Energy")]
        [Tooltip("Fraction of the smaller of impact speed and impulse/glass mass transferred to liquid as extra speed. Zero disables the splash; existing liquid motion is preserved.")]
        [Min(0)] public float strength = .65f;
        [Tooltip("Maximum extra liquid speed per break, in world units/second. This limits the added impulse, not the liquid's existing fall speed.")]
        [Min(0)] public float maximumDeltaSpeed = 6.5f;
        [Tooltip("Multiplier for G explosive breakage. The maximum extra speed still applies.")]
        [Min(0)] public float explosiveMultiplier = 1.6f;

        [Header("Splash Direction")]
        [Tooltip("Relative contribution pointing away from the liquid container's center.")]
        [Min(0)] public float radialWeight = .35f;
        [Tooltip("Relative contribution spreading to both sides along the impact surface. Side is determined by particle position, so queued and active liquid behave identically.")]
        [Min(0)] public float tangentWeight = 1f;
        [Tooltip("Relative contribution away from the struck surface: upward for a floor impact, sideways for a wall impact. Never adds velocity into that surface.")]
        [Min(0)] public float liftWeight = .7f;

        [Header("Impact Reach")]
        [Tooltip("Distance from the impact point affected by the splash, in world units. Liquid outside this radius is released without extra speed.")]
        [Min(.001f)] public float impactRadius = 2.5f;
        [Tooltip("Distance falloff exponent. Zero gives uniform strength inside the radius; larger values concentrate the splash near the impact.")]
        [Min(0)] public float impactFalloff = 1f;

        // Packed once for both the queued CPU path and the GPU kernel. Keep Evaluate
        // and EvaluateBreakSplashDelta in the compute shader mathematically identical.
        internal struct Parameters
        {
            public Vector2 Center, Point, Normal;
            public Vector4 Direction; // radial, tangent, lift, falloff exponent
            public Vector4 Impact; // radius, available delta speed, maximum delta speed, unused

            public Vector2 Evaluate(Vector2 position)
            {
                if (!Finite(position)) return Vector2.zero;
                float distance = (position - Point).magnitude;
                if (distance >= Impact.x || Impact.y <= 0 || Impact.z <= 0) return Vector2.zero;
                Vector2 offset = position - Center;
                float radialLength = offset.magnitude;
                Vector2 radial = radialLength > .00001f ? offset / radialLength : Vector2.zero;
                Vector2 tangent = new Vector2(Normal.y, -Normal.x);
                float side = Vector2.Dot(offset, tangent);
                float sideSign = side < -.00001f ? -1f : side > .00001f ? 1f : 0f;
                Vector2 direction = radial * Direction.x + tangent * (sideSign * Direction.y) + Normal * Direction.z;
                direction -= Normal * Mathf.Min(0, Vector2.Dot(direction, Normal));
                float directionLength = direction.magnitude;
                if (directionLength <= .00001f) return Vector2.zero;
                float falloff = Mathf.Pow(Mathf.Max(0, 1f - distance / Impact.x), Direction.w);
                return direction / directionLength * Mathf.Min(Impact.z, Impact.y * falloff);
            }
        }

        internal static bool Finite(Vector2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);
        private static float Nonnegative(float value, float maximum) => float.IsFinite(value) ? Mathf.Clamp(value, 0, maximum) : 0;

        internal Parameters BuildParameters(Vector2 center, Vector2 point, Vector2 normal,
            float impactSpeed, float impulsePerMass, bool explosive)
        {
            if (!Finite(normal) || !float.IsFinite(normal.sqrMagnitude) || normal.sqrMagnitude <= .0000000001f) normal = Vector2.up;
            else normal.Normalize();
            float energy = Mathf.Min(Nonnegative(impactSpeed, 1000), Nonnegative(impulsePerMass, 1000))
                * Nonnegative(strength, 10) * (explosive ? Nonnegative(explosiveMultiplier, 10) : 1f);
            return new Parameters
            {
                Center = center, Point = point, Normal = normal,
                Direction = new Vector4(Nonnegative(radialWeight, 10), Nonnegative(tangentWeight, 10),
                    Nonnegative(liftWeight, 10), Nonnegative(impactFalloff, 16)),
                Impact = new Vector4(Mathf.Max(.001f, Nonnegative(impactRadius, 1000)), energy,
                    Nonnegative(maximumDeltaSpeed, 100), 0)
            };
        }

        /// <summary>Extra velocity only; does not include or replace the liquid's existing motion.</summary>
        public Vector2 EvaluateVelocityDelta(Vector2 position, Vector2 center, Vector2 point, Vector2 normal,
            float impactSpeed, float impulsePerMass, bool explosive = false)
        {
            if (!Finite(center) || !Finite(point)) return Vector2.zero;
            return BuildParameters(center, point, normal, impactSpeed, impulsePerMass, explosive).Evaluate(position);
        }
    }
}
