using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum FluidExperimentDevelopmentScenario { Manual, Rest, Pour, Tilt, Stir, SealedShake, VolumeCheck }

    /// <summary>Optional replay client copied into isolated validation projects only.</summary>
    [DefaultExecutionOrder(150)]
    public sealed class FluidExperimentDevelopmentScenarios : MonoBehaviour
    {
        public FluidExperimentComparison sandbox;
        public bool automaticAdvance = true;
        public FluidExperimentDevelopmentScenario ActiveScenario { get; private set; }
        public float ScenarioTime { get; private set; }
        public FluidExperimentBody VolumeVessel { get; private set; }
        public float RequestedVolumeMl { get; private set; } = 60;
        public float QueuedVolumeMl { get; private set; }
        public FluidExperimentVolumeMetrics VolumeMetrics { get; } = new FluidExperimentVolumeMetrics();
        public bool Ready => sandbox != null && sandbox.Ready;
        public FluidExperimentWorld World => sandbox != null ? sandbox.World : null;
        public FluidExperimentGpuLiquid Gpu => sandbox != null ? sandbox.Gpu : null;
        public bool VolumeSampleAvailable => ActiveScenario == FluidExperimentDevelopmentScenario.VolumeCheck
            && Gpu != null && Gpu.SnapshotRevision != volumeSnapshotAtStart;
        private readonly Dictionary<FluidExperimentBody, float> flowRates = new Dictionary<FluidExperimentBody, float>();
        private FluidExperimentBody bottle, glass, shaker, spoon, preferredVolumeVessel;
        private int volumeSnapshotAtStart, lastVolumeSnapshot = -1;

        private void Awake()
        {
            if (sandbox == null) sandbox = GetComponent<FluidExperimentComparison>();
        }

        public void StartVolumeCheck(FluidExperimentBody vessel, float requestedMl)
        {
            if (!Ready || vessel == null || vessel.World != World || vessel.kind != LabItemKind.Glass)
                throw new ArgumentException("Volume checks require a glass from the ready sandbox.", nameof(vessel));
            if (!float.IsFinite(requestedMl) || requestedMl <= 0) throw new ArgumentOutOfRangeException(nameof(requestedMl));
            preferredVolumeVessel = vessel; RequestedVolumeMl = requestedMl;
            StartScenario(FluidExperimentDevelopmentScenario.VolumeCheck);
        }

        public void StartScenario(FluidExperimentDevelopmentScenario scenario)
        {
            if (!Ready) throw new InvalidOperationException("Sandbox has not initialized.");
            foreach (var body in World.Items)
                if (!flowRates.ContainsKey(body)) flowRates[body] = body.pourMlPerSecond;
            foreach (var pair in flowRates) if (pair.Key != null) pair.Key.pourMlPerSecond = pair.Value;
            sandbox.ResetSandbox();
            ActiveScenario = scenario; ScenarioTime = 0;
            VolumeVessel = null; QueuedVolumeMl = 0;
            if (scenario == FluidExperimentDevelopmentScenario.Manual) return;
            World.SetDispenserActive(false); World.interactor.enabled = false;
            bottle = glass = shaker = spoon = null;
            foreach (var body in World.Items)
            {
                body.SetHeld(true); body.Teleport(new Vector2(-18, 10), 0); body.pourMlPerSecond = 0;
                if (body.kind == LabItemKind.Bottle && (bottle == null || body.name.Contains("1002"))) bottle = body;
                if (body.kind == LabItemKind.Glass && (glass == null || body.name.Contains("highball"))) glass = body;
                if (body.kind == LabItemKind.Shaker) shaker = body;
                if (body.kind == LabItemKind.Spoon) spoon = body;
            }
            if (bottle == null || glass == null || shaker == null || spoon == null)
                throw new InvalidOperationException("Replay requires a bottle, glass, shaker and spoon.");
            Gpu.ResetSimulation();
            Camera camera = World.interactor.inputCamera;
            camera.transform.position = new Vector3(0, 1.6f, -20); camera.orthographicSize = 4;
            if (scenario == FluidExperimentDevelopmentScenario.SealedShake)
            {
                shaker.Teleport(new Vector2(0, .2f), 0); shaker.SetSealed(true);
                Gpu.Fill(shaker, bottle.ingredient, 60);
            }
            else
            {
                if (scenario == FluidExperimentDevelopmentScenario.VolumeCheck && preferredVolumeVessel != null)
                    glass = preferredVolumeVessel;
                glass.Teleport(new Vector2(0, -.4f), 0);
                if (scenario == FluidExperimentDevelopmentScenario.VolumeCheck)
                {
                    VolumeVessel = glass; glass.SetHeld(false); glass.Body.bodyType = RigidbodyType2D.Kinematic;
                    glass.SetSealed(false); QueuedVolumeMl = Gpu.Fill(glass, bottle.ingredient, RequestedVolumeMl);
                    volumeSnapshotAtStart = Gpu.SnapshotRevision; lastVolumeSnapshot = -1;
                    VolumeMetrics.Measure(Gpu, glass, RequestedVolumeMl); FrameVolumeVessel();
                }
                else Gpu.Fill(glass, bottle.ingredient, 60);
                if (scenario == FluidExperimentDevelopmentScenario.Pour)
                {
                    glass.SetHeld(false); glass.Body.bodyType = RigidbodyType2D.Kinematic;
                    bottle.Teleport(new Vector2(0, 3.5f), 180); bottle.ResetSupply(700, 0);
                }
                if (scenario == FluidExperimentDevelopmentScenario.Stir)
                {
                    glass.SetHeld(false); glass.Body.bodyType = RigidbodyType2D.Kinematic;
                    spoon.Teleport(new Vector2(0, .7f), 0);
                    spoon.SetSpoonContactEnabled(true);
                }
            }
            Physics2D.SyncTransforms();
        }

        public void AdvanceScenario(float dt)
        {
            if (!Ready || dt <= 0 || ActiveScenario == FluidExperimentDevelopmentScenario.Manual) return;
            ScenarioTime += dt;
            float t = ScenarioTime;
            switch (ActiveScenario)
            {
                case FluidExperimentDevelopmentScenario.Pour:
                    bottle.pourMlPerSecond = t >= .5f && t < 4.5f ? 20 : 0;
                    bottle.SetHeldPose(new Vector2(0, 3.5f), t < 4.5f ? 180 : Mathf.Lerp(180, 0, Mathf.Clamp01((t - 4.5f) * 3)));
                    break;
                case FluidExperimentDevelopmentScenario.Tilt:
                    glass.SetHeldPose(new Vector2(0, -.4f), 135 * Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 1) / 2)));
                    break;
                case FluidExperimentDevelopmentScenario.Stir:
                    spoon.SetHeldPose(new Vector2(.28f * Mathf.Sin(t * 3), .7f + .12f * Mathf.Sin(t * 6)), 12 * Mathf.Sin(t * 3));
                    break;
                case FluidExperimentDevelopmentScenario.SealedShake:
                    shaker.SetHeldPose(new Vector2(.7f * Mathf.Sin(t * 4), .2f + .3f * Mathf.Sin(t * 8)), 65 * Mathf.Sin(t * 3));
                    break;
            }
        }

        private void FixedUpdate()
        {
            if (automaticAdvance) AdvanceScenario(Time.fixedDeltaTime);
        }

        private void Update()
        {
            if (ActiveScenario == FluidExperimentDevelopmentScenario.VolumeCheck) FrameVolumeVessel();
            if (!VolumeSampleAvailable || lastVolumeSnapshot == Gpu.SnapshotRevision) return;
            VolumeMetrics.Measure(Gpu, VolumeVessel, RequestedVolumeMl); lastVolumeSnapshot = Gpu.SnapshotRevision;
        }

        private void FrameVolumeVessel()
        {
            if (VolumeVessel == null) return;
            Camera camera = World.interactor.inputCamera;
            Rect interior = VolumeVessel.collisionProfile != null
                ? VolumeVessel.collisionProfile.InteriorBounds : VolumeVessel.contentRegions[0];
            Vector2 center = VolumeVessel.LocalToWorld(interior.center);
            Vector3 scale = VolumeVessel.transform.lossyScale;
            camera.orthographicSize = Mathf.Max(1.5f, interior.height * Mathf.Abs(scale.y) * .8f,
                interior.width * Mathf.Abs(scale.x) * .7f / camera.aspect);
            camera.transform.position = new Vector3(center.x, center.y, -20);
        }
    }
}
