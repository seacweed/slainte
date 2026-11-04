using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentCocktailSession
    {
        [Header("Technique observation (metadata only)")]
        [Min(.01f)] public float stirMinimumSpeed = .04f;
        [Min(0)] public float stirMinimumAngularSpeed = 5;
        [Min(.05f)] public float stirAttemptDuration = .35f;
        [Min(.1f)] public float stirCompletionDuration = 1;
        [Min(.02f)] public float stirMotionGrace = .15f;
        [Range(0, 1)] public float stirCompositionTolerance = .08f;
        [Min(0)] public float stirUniformDuration = .15f;
        [Min(.1f)] public float shakeMinimumSpeed = 2;
        [Min(.2f)] public float shakeCompletionDuration = 1;
        [Min(1)] public int shakeMinimumReversals = 2;
        [Min(.1f)] public float shakeMaximumReversalInterval = 1;

        private sealed class ContentSignature
        {
            public ulong slots;
            public readonly Dictionary<ItemDef, double> ingredients = new Dictionary<ItemDef, double>();
            public bool Same(ContentSignature other)
            {
                if (other == null || slots != other.slots || ingredients.Count != other.ingredients.Count) return false;
                foreach (var pair in ingredients)
                    if (!other.ingredients.TryGetValue(pair.Key, out double amount)
                        || System.Math.Abs(pair.Value - amount) > .001) return false;
                return true;
            }
        }
        private sealed class TechniqueObservation
        {
            public ContentSignature signature;
            public int revision;
            public float stirTime, shakeTime, lastReversal, lastStir, uniformSince = -1, nextCheck;
            public Vector2 previousFastDirection;
            public int reversals;
            public bool attempted, stirred, shaken, ice;
        }
        private readonly Dictionary<FluidExperimentBody, TechniqueObservation> observations =
            new Dictionary<FluidExperimentBody, TechniqueObservation>();
        private float techniqueTime;

        private void ResetTechniqueTracking() { observations.Clear(); techniqueTime = 0; }

        private ContentSignature CaptureSignature(FluidExperimentBody vessel)
        {
            var signature = new ContentSignature { slots = 1469598103934665603UL };
            var particles = Gpu.Snapshot;
            for (int i = 0; i < particles.Length; i++)
                if (particles[i].Active != 0 && particles[i].VesselId == vessel.Id)
                    signature.slots = unchecked((signature.slots ^ (ulong)(i + 1)) * 1099511628211UL);
            foreach (var pair in Gpu.IngredientsIn(vessel.Id))
                if (pair.Value > .00001) signature.ingredients.Add(pair.Key, pair.Value);
            return signature;
        }

        private bool HasIce(FluidExperimentBody vessel)
        {
            foreach (var body in world.Items)
                if (body != null && body.kind == LabItemKind.Ice && !body.IsHeld && body.ContainingVesselId == vessel.Id
                    && body.Body != null && body.Body.simulated) return true;
            return false;
        }

        private TechniqueObservation BeginObservation(FluidExperimentBody vessel)
        {
            Gpu.ReadbackNow();
            var observation = new TechniqueObservation { signature = CaptureSignature(vessel),
                revision = Gpu.SnapshotRevision, ice = HasIce(vessel), lastReversal = techniqueTime };
            observations[vessel] = observation;
            return observation;
        }

        /// <summary>Called after a completed physics/liquid tick, before body history is synchronized.</summary>
        public void ObserveTechniques(float dt)
        {
            SynchronizeGeneration();
            if (!Available || dt <= 0 || !float.IsFinite(dt)) { observations.Clear(); return; }
            techniqueTime += dt;
            foreach (var vessel in world.Items)
            {
                if (vessel == null || !vessel.IsVessel || !vessel.isActiveAndEnabled) continue;
                bool stirring = IsStirring(vessel, dt);
                bool shaking = vessel.kind == LabItemKind.Shaker && vessel.HasCap && vessel.IsHeld
                    && (vessel.Position - vessel.PreviousPosition).magnitude / dt >= shakeMinimumSpeed;
                if (!stirring && !shaking)
                {
                    if (observations.TryGetValue(vessel, out var idle))
                    {
                        if (techniqueTime - idle.lastStir > stirMotionGrace)
                        { idle.stirTime = 0; idle.uniformSince = -1; }
                        if (!vessel.HasCap || !vessel.IsHeld || techniqueTime - idle.lastReversal > shakeMaximumReversalInterval)
                        { idle.shakeTime = 0; idle.reversals = 0; idle.previousFastDirection = Vector2.zero; }
                    }
                    continue;
                }
                if (!observations.TryGetValue(vessel, out var state)) state = BeginObservation(vessel);
                else if (state.revision != Gpu.SnapshotRevision || state.ice != HasIce(vessel))
                {
                    if (!state.signature.Same(CaptureSignature(vessel)) || state.ice != HasIce(vessel))
                        state = BeginObservation(vessel);
                    else state.revision = Gpu.SnapshotRevision;
                }
                if (state.signature.ingredients.Count == 0) { observations.Remove(vessel); continue; }
                if (stirring) ObserveStir(vessel, state, dt);
                if (shaking) ObserveShake(vessel, state, dt);
            }
        }

        private bool RefreshUnchanged(FluidExperimentBody vessel, TechniqueObservation state)
        {
            Gpu.ReadbackNow();
            if (!state.signature.Same(CaptureSignature(vessel)) || state.ice != HasIce(vessel))
            { BeginObservation(vessel); return false; }
            state.revision = Gpu.SnapshotRevision;
            return true;
        }

        private void ObserveStir(FluidExperimentBody vessel, TechniqueObservation state, float dt)
        {
            state.stirTime += dt;
            state.lastStir = techniqueTime;
            if (!state.attempted && state.stirTime >= stirAttemptDuration)
            {
                Gpu.MarkCocktailTechnique(vessel, CocktailTechnique.None, true, false);
                state.attempted = true;
            }
            if (state.stirred || state.stirTime < stirCompletionDuration || techniqueTime < state.nextCheck) return;
            state.nextCheck = techniqueTime + .1f;
            if (!RefreshUnchanged(vessel, state)) return;
            if (!IsStirring(vessel, dt)) { state.uniformSince = -1; return; }
            if (!Gpu.CocktailMixtureUniform(vessel, stirCompositionTolerance)) { state.uniformSince = -1; return; }
            if (state.uniformSince < 0) state.uniformSince = techniqueTime;
            if (techniqueTime - state.uniformSince < stirUniformDuration) return;
            Gpu.MarkCocktailTechnique(vessel, CocktailTechnique.Stir, true, false);
            state.stirred = true;
        }

        private void ObserveShake(FluidExperimentBody vessel, TechniqueObservation state, float dt)
        {
            if (state.shaken) return;
            if (techniqueTime - state.lastReversal > shakeMaximumReversalInterval)
            { state.reversals = 0; state.shakeTime = 0; state.previousFastDirection = Vector2.zero; }
            Vector2 direction = (vessel.Position - vessel.PreviousPosition).normalized;
            if (state.previousFastDirection.sqrMagnitude > .5f && Vector2.Dot(state.previousFastDirection, direction) <= 0)
            { state.reversals++; state.lastReversal = techniqueTime; }
            state.previousFastDirection = direction;
            if (state.reversals > 0) state.shakeTime += dt;
            if (state.reversals < shakeMinimumReversals || state.shakeTime < shakeCompletionDuration
                || !RefreshUnchanged(vessel, state)) return;
            Gpu.MarkCocktailTechnique(vessel, CocktailTechnique.Shake, false, state.ice);
            state.shaken = true;
        }

        private bool IsStirring(FluidExperimentBody vessel, float dt)
        {
            foreach (var spoon in world.Items)
            {
                if (TryGetStirContact(spoon, vessel, dt, out _, out _, out bool moving) && moving) return true;
            }
            return false;
        }

        internal bool TryGetStirContact(FluidExperimentBody spoon, FluidExperimentBody vessel, float dt,
            out Vector2 relative, out Vector2 wetPoint, out bool moving)
        {
            relative = wetPoint = Vector2.zero; moving = false;
            if (dt <= 0 || spoon == null || spoon.kind != LabItemKind.Spoon || !spoon.IsHeld || spoon.collisionProfile == null
                || !spoon.SpoonContactEnabled || spoon.SpoonLiquidVessel != vessel) return false;
            relative = (spoon.Position - spoon.PreviousPosition - (vessel.Position - vessel.PreviousPosition)) / dt;
            float angular = Mathf.Abs(Mathf.DeltaAngle(spoon.PreviousAngle, spoon.Angle)
                - Mathf.DeltaAngle(vessel.PreviousAngle, vessel.Angle)) / dt;
            moving = relative.magnitude >= stirMinimumSpeed || angular >= stirMinimumAngularSpeed;
            return SpoonTouchesLiquid(spoon, vessel, out wetPoint);
        }

        private bool SpoonTouchesLiquid(FluidExperimentBody spoon, FluidExperimentBody vessel, out Vector2 wetPoint)
        {
            wetPoint = Vector2.zero; int count = 0;
            float reachSquared = Gpu.Radius * Gpu.Radius * 2.25f;
            Bounds bounds = spoon.SolidBounds; bounds.Expand(Gpu.Radius * 3);
            foreach (var particle in Gpu.Snapshot)
            {
                if (particle.Active == 0 || particle.VesselId != vessel.Id || !bounds.Contains(particle.Position)) continue;
                foreach (var hull in spoon.collisionProfile.solids)
                {
                    bool touches = FluidExperimentCollisionProfile.Contains(hull.points, spoon.WorldToLocal(particle.Position));
                    for (int i = 0; !touches && i < hull.points.Length; i++)
                    {
                        Vector2 a = spoon.LocalToWorld(hull.points[i]);
                        Vector2 edge = spoon.LocalToWorld(hull.points[(i + 1) % hull.points.Length]) - a;
                        float t = edge.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(particle.Position - a, edge) / edge.sqrMagnitude) : 0;
                        if ((particle.Position - a - edge * t).sqrMagnitude <= reachSquared) touches = true;
                    }
                    if (touches) { wetPoint += particle.Position; count++; break; }
                }
            }
            if (count > 0) wetPoint /= count;
            return count > 0;
        }
    }
}
