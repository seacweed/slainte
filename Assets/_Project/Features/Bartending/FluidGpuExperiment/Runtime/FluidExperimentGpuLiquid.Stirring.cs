using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        [Header("Spoon stirring (F/G Manual and Runtime)")]
        public bool enableSpoonStirring = true;
        [Min(0)] public float spoonFlowSpeed = .16f;
        [Min(0)] public float spoonFlowAcceleration = .6f;
        [Min(.01f)] public float spoonFlowRadius = .4f;
        [Min(0)] public float spoonFlowResponse = 6;
        [Min(0)] public float spoonMixRate = 1.6f;
        [Min(.01f)] public float spoonFullStirSpeed = .45f;
        [Min(0)] public float spoonSettleDuration = .3f;
        public int SpoonStirSources { get; private set; }
        public int SpoonMixSources { get; private set; }
        public float MaximumSpoonFlowTargetSpeed { get; private set; }
        private GraphicsBuffer spoonMixtureBuffer;
        private int collectSpoonMixKernel, applySpoonMixKernel;
        private readonly Dictionary<FluidExperimentBody, float> spoonTails = new Dictionary<FluidExperimentBody, float>();
        private readonly HashSet<FluidExperimentBody> seenSpoons = new HashSet<FluidExperimentBody>();
        private readonly HashSet<uint> stirredVessels = new HashSet<uint>();
        private readonly List<FluidExperimentBody> expiredSpoons = new List<FluidExperimentBody>();

        private void CacheSpoonStirKernels()
        { collectSpoonMixKernel = RequireKernel("CollectSpoonMixture"); applySpoonMixKernel = RequireKernel("ApplySpoonMixture"); }
        private void AllocateSpoonStir() => spoonMixtureBuffer = CreateStructured<Vector2>(MaximumAgitators * maximumIngredients);
        private void BindSpoonStir()
        {
            BindCommonBuffers(collectSpoonMixKernel); BindCommonBuffers(applySpoonMixKernel);
            simulationShader.SetBuffer(collectSpoonMixKernel, "_SpoonMixture", spoonMixtureBuffer);
            simulationShader.SetBuffer(applySpoonMixKernel, "_SpoonMixture", spoonMixtureBuffer);
        }
        private void ResetSpoonStir()
        {
            spoonTails.Clear(); SpoonStirSources = SpoonMixSources = 0; MaximumSpoonFlowTargetSpeed = 0;
            simulationShader.SetInt("_AgitatorCount", 0);
        }
        private void DisposeSpoonStir() { spoonMixtureBuffer?.Dispose(); spoonMixtureBuffer = null; spoonTails.Clear(); }

        private void PrepareSpoonStir(float dt)
        {
            SpoonStirSources = SpoonMixSources = 0; MaximumSpoonFlowTargetSpeed = 0;
            seenSpoons.Clear(); stirredVessels.Clear();
            var session = world.Cocktails;
            if (enableSpoonStirring && session != null && session.Available)
                foreach (var spoon in world.Items)
                {
                    if (spoon == null || spoon.kind != LabItemKind.Spoon) continue;
                    var vessel = spoon.SpoonLiquidVessel;
                    if (vessel == null || !session.TryGetStirContact(spoon, vessel, dt, out var relative, out var point, out bool moving)) continue;
                    seenSpoons.Add(spoon);
                    spoonTails.TryGetValue(spoon, out float tail);
                    tail = moving ? spoonSettleDuration : Mathf.Max(0, tail - dt);
                    spoonTails[spoon] = tail;
                    if ((!moving && tail <= 0) || SpoonStirSources >= MaximumAgitators || !stirredVessels.Add(vessel.Id)) continue;
                    float strength = moving ? Mathf.Clamp01(Mathf.Max(.05f, relative.magnitude / Mathf.Max(.01f, spoonFullStirSpeed)))
                        : tail / Mathf.Max(.001f, spoonSettleDuration);
                    Vector2 velocity = moving ? Vector2.ClampMagnitude(relative * .3f, Mathf.Max(0, spoonFlowSpeed)) : Vector2.zero;
                    velocity.y *= .35f;
                    MaximumSpoonFlowTargetSpeed = Mathf.Max(MaximumSpoonFlowTargetSpeed, velocity.magnitude);
                    agitatorUpload[SpoonStirSources++] = new GpuLiquidAgitator { A = point, B = point, Velocity = velocity,
                        Radius = spoonFlowRadius, Strength = strength, VesselId = vessel.Id, Active = moving ? 7u : 3u };
                    if (moving) SpoonMixSources++;
                }
            expiredSpoons.Clear();
            foreach (var spoon in spoonTails.Keys) if (!seenSpoons.Contains(spoon)) expiredSpoons.Add(spoon);
            foreach (var spoon in expiredSpoons) spoonTails.Remove(spoon);
            if (SpoonStirSources > 0) agitatorBuffer.SetData(agitatorUpload, 0, 0, SpoonStirSources);
            simulationShader.SetInt("_AgitatorCount", SpoonStirSources);
            simulationShader.SetFloat("_SpoonFlowResponse", Mathf.Max(0, spoonFlowResponse));
            simulationShader.SetFloat("_SpoonFlowAcceleration", Mathf.Max(0, spoonFlowAcceleration));
        }
        private void DispatchSpoonMix(float dt)
        {
            if (SpoonMixSources == 0 || spoonMixRate <= 0) return;
            BindCurrentComposition(collectSpoonMixKernel);
            DispatchForCount(collectSpoonMixKernel, SpoonStirSources * maximumIngredients);
            simulationShader.SetBuffer(applySpoonMixKernel, "_SpoonComposition", compositionAIsCurrent ? compositionA : compositionB);
            simulationShader.SetFloat("_SpoonMixStep", Mathf.Max(0, spoonMixRate) * dt);
            DispatchForCount(applySpoonMixKernel, particleCapacity);
        }
    }
}
