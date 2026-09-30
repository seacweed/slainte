using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum FluidExperimentMode { ACurrent, BReferencePhysics, CReferenceSurface, DImprovedSurface, ECalibratedLiquid }
    public enum FluidExperimentScenario { Manual, Rest, Pour, Tilt, Stir, SealedShake, VolumeCheck }

    /// <summary>One world at a time, reset to the same authored state for every comparison.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class FluidExperimentComparison : MonoBehaviour
    {
        public FluidExperimentWorld world;
        public Shader effectsShader;
        public FluidExperimentMode initialMode = FluidExperimentMode.ECalibratedLiquid;
        public bool automaticScenario = true;
        public bool showControls = true;
        public FluidExperimentWorld World => world;
        public FluidExperimentGpuLiquid Gpu => world != null ? world.Liquid : null;
        public FluidExperimentMode ActiveMode { get; private set; }
        public FluidExperimentScenario ActiveScenario { get; private set; }
        public bool Ready { get; private set; }
        public float ScenarioTime { get; private set; }
        public string Error { get; private set; }
        public FluidExperimentBody VolumeVessel { get; private set; }
        public float RequestedVolumeMl { get; private set; } = 60;
        public float QueuedVolumeMl { get; private set; }
        public FluidExperimentVolumeMetrics VolumeMetrics { get; } = new FluidExperimentVolumeMetrics();
        public bool VolumeSampleAvailable => ActiveScenario == FluidExperimentScenario.VolumeCheck
            && Gpu != null && Gpu.SnapshotRevision != volumeSnapshotAtStart;
        public static Rect ControlsRect => new Rect(12, 12, 545, 225);
        private Rect LogicalControlsRect => new Rect(12, 12, 545, 256
            + (ActiveScenario == FluidExperimentScenario.VolumeCheck ? 145 : 0)
            + (ActiveMode == FluidExperimentMode.ECalibratedLiquid ? 48 : 0));
        private float ControlsScale => Mathf.Min(1, Screen.width / 960f, Screen.height / 640f);
        private Rect ActiveControlsRect
        {
            get
            {
                Rect rect = LogicalControlsRect;
                float scale = ControlsScale;
                return new Rect(rect.x * scale, rect.y * scale, rect.width * scale, rect.height * scale);
            }
        }

        private FluidExperimentLiquidSettings runtimeSettings;
        private FluidExperimentLiquidSettings sourceSettings;
        private readonly Dictionary<FluidExperimentBody, float> flowRates = new Dictionary<FluidExperimentBody, float>();
        private Vector3 initialCameraPosition;
        private float initialCameraSize;
        private FluidExperimentBody bottle, glass, shaker, spoon;
        private FluidExperimentBody preferredVolumeVessel;
        private int volumeSnapshotAtStart, lastVolumeSnapshot = -1;

        private void Awake()
        {
            if (world == null) world = GetComponent<FluidExperimentWorld>();
            if (world == null || Gpu == null) { Error = "Comparison world is missing."; return; }
            world.showControls = false;
            var effects = GetComponent<FluidExperimentEffects>();
            if (effects == null) effects = gameObject.AddComponent<FluidExperimentEffects>();
            effects.world = world;
            effects.cosmeticShader = effectsShader;
            sourceSettings = Gpu.settings;
            if (sourceSettings != null)
            {
                runtimeSettings = Instantiate(sourceSettings);
                runtimeSettings.name = sourceSettings.name + " (runtime comparison)";
                runtimeSettings.hideFlags = HideFlags.DontSave;
                Gpu.settings = runtimeSettings;
            }
            ApplyMode(initialMode);
            world.interactor.pointerBlockRect = ControlsRect;
        }
        private IEnumerator Start()
        {
            // World.Start captures the original body/supply state before comparison resets.
            yield return null;
            if (world == null || Gpu == null) yield break;
            Camera camera = world.interactor.inputCamera;
            initialCameraPosition = camera.transform.position;
            initialCameraSize = camera.orthographicSize;
            foreach (var body in world.Items) flowRates[body] = body.pourMlPerSecond;
            SwitchMode(initialMode);
        }
        private void ApplyMode(FluidExperimentMode mode)
        {
            ActiveMode = mode;
            Gpu.useImprovedPhysics = mode == FluidExperimentMode.ECalibratedLiquid;
            Gpu.useImprovedSurface = mode == FluidExperimentMode.DImprovedSurface || Gpu.useImprovedPhysics;
            if (runtimeSettings != null && sourceSettings != null)
                runtimeSettings.gpuLiquidParticleRadius = Gpu.useImprovedPhysics
                    ? runtimeSettings.improvedParticleRadius : sourceSettings.gpuLiquidParticleRadius;
            Gpu.solver = mode == FluidExperimentMode.ACurrent
                ? FluidExperimentSolver.BaselinePbf : FluidExperimentSolver.ReferenceSph;
            Gpu.useReferenceSurface = mode >= FluidExperimentMode.CReferenceSurface;
            Gpu.useSurfaceRendering = true;
            Gpu.useStreamRendering = true;
        }
        public void SwitchMode(FluidExperimentMode mode)
        {
            if (world == null || Gpu == null) throw new InvalidOperationException("Missing comparison world.");
            FluidExperimentScenario repeat = ActiveScenario;
            bool wasEnabled = world.enabled;
            world.enabled = false;
            try
            {
                Ready = false; Error = null;
                // Dispose only this instance; assets and the original PhysicsLab stay untouched.
                Gpu.Dispose(); ApplyMode(mode); Gpu.Initialize(world);
                if (!Gpu.IsOperational) { Error = Gpu.Error; return; }
                Ready = true;
                StartScenario(repeat);
            }
            finally { world.enabled = wasEnabled; }
        }
        public void ResetComparison()
        {
            if (!Ready) return;
            ActiveScenario = FluidExperimentScenario.Manual; ScenarioTime = 0;
            foreach (var pair in flowRates) if (pair.Key != null) pair.Key.pourMlPerSecond = pair.Value;
            world.ResetSession();
            world.interactor.enabled = true;
            world.interactor.inputCamera.transform.position = initialCameraPosition;
            world.interactor.inputCamera.orthographicSize = initialCameraSize;
            bottle = glass = shaker = spoon = null;
            VolumeVessel = null; QueuedVolumeMl = 0;
            world.interactor.pointerBlockRect = ActiveControlsRect;
        }
        public void StartVolumeCheck(FluidExperimentBody vessel, float requestedMl)
        {
            if (vessel == null || vessel.World != world || vessel.kind != LabItemKind.Glass)
                throw new ArgumentException("Volume checks require a glass from this comparison world.", nameof(vessel));
            if (!float.IsFinite(requestedMl) || requestedMl <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestedMl));
            preferredVolumeVessel = vessel;
            RequestedVolumeMl = requestedMl;
            StartScenario(FluidExperimentScenario.VolumeCheck);
        }
        public void StartScenario(FluidExperimentScenario scenario)
        {
            if (!Ready) return;
            ResetComparison(); ActiveScenario = scenario;
            world.interactor.pointerBlockRect = ActiveControlsRect;
            if (scenario == FluidExperimentScenario.Manual) return;
            world.interactor.enabled = false;
            foreach (var body in world.Items)
            {
                body.SetHeld(true); body.Teleport(new Vector2(-18, 10), 0);
                body.pourMlPerSecond = 0;
                if (body.kind == LabItemKind.Bottle && (bottle == null || body.name.Contains("1002"))) bottle = body;
                if (body.kind == LabItemKind.Glass && (glass == null || body.name.Contains("highball"))) glass = body;
                if (body.kind == LabItemKind.Shaker) shaker = body;
                if (body.kind == LabItemKind.Spoon) spoon = body;
            }
            if (bottle == null || glass == null || shaker == null || spoon == null)
                throw new InvalidOperationException("Comparison scene needs a bottle, glass, shaker and spoon.");
            Gpu.ResetSimulation();
            Camera camera = world.interactor.inputCamera;
            camera.transform.position = new Vector3(0, 1.6f, -20); camera.orthographicSize = 4;
            if (scenario == FluidExperimentScenario.SealedShake)
            {
                shaker.Teleport(new Vector2(0, .2f), 0); shaker.SetSealed(true);
                Gpu.Fill(shaker, bottle.ingredient, 60);
            }
            else
            {
                if (scenario == FluidExperimentScenario.VolumeCheck && preferredVolumeVessel != null
                    && preferredVolumeVessel.World == world) glass = preferredVolumeVessel;
                glass.Teleport(new Vector2(0, -.4f), 0);
                if (scenario == FluidExperimentScenario.VolumeCheck)
                {
                    VolumeVessel = glass;
                    glass.SetHeld(false); glass.Body.bodyType = RigidbodyType2D.Kinematic;
                    glass.SetSealed(false);
                    QueuedVolumeMl = Gpu.Fill(glass, bottle.ingredient, RequestedVolumeMl);
                    volumeSnapshotAtStart = Gpu.SnapshotRevision;
                    lastVolumeSnapshot = -1;
                    VolumeMetrics.Measure(Gpu, glass, RequestedVolumeMl);
                    FrameVolumeVessel();
                }
                else Gpu.Fill(glass, bottle.ingredient, 60);
                if (scenario == FluidExperimentScenario.Pour)
                {
                    glass.SetHeld(false); glass.Body.bodyType = RigidbodyType2D.Kinematic;
                    bottle.Teleport(new Vector2(0, 3.5f), 180); bottle.ResetSupply(700, 0);
                }
                if (scenario == FluidExperimentScenario.Stir)
                {
                    // Held vessels intentionally ignore unrelated tool boundaries.
                    // Keep both participants unheld and kinematic for this replay.
                    glass.SetHeld(false); glass.Body.bodyType = RigidbodyType2D.Kinematic;
                    spoon.Teleport(new Vector2(0, Gpu.useImprovedPhysics ? -.35f : .7f), 0);
                    // The existing held-body policy excludes other held tools from fluid.
                    // A kinematic, unheld spoon exercises actual moving-solid contacts.
                    spoon.SetHeld(false); spoon.Body.bodyType = RigidbodyType2D.Kinematic;
                }
            }
            Physics2D.SyncTransforms();
        }
        public void AdvanceScenario(float dt)
        {
            if (!Ready || dt <= 0 || ActiveScenario == FluidExperimentScenario.Manual) return;
            ScenarioTime += dt;
            float t = ScenarioTime;
            switch (ActiveScenario)
            {
                case FluidExperimentScenario.Pour:
                    bottle.pourMlPerSecond = t >= .5f && t < 4.5f ? 20 : 0;
                    bottle.SetHeldPose(new Vector2(0, 3.5f), t < 4.5f ? 180 : Mathf.Lerp(180, 0, Mathf.Clamp01((t - 4.5f) * 3)));
                    break;
                case FluidExperimentScenario.Tilt:
                    glass.SetHeldPose(new Vector2(0, -.4f), 135 * Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 1) / 2)));
                    break;
                case FluidExperimentScenario.Stir:
                    float stirHeight = Gpu.useImprovedPhysics ? -.35f : .7f;
                    float stirAmplitude = Gpu.useImprovedPhysics ? .08f : .12f;
                    spoon.Body.position = new Vector2(.28f * Mathf.Sin(t * 3), stirHeight + stirAmplitude * Mathf.Sin(t * 6));
                    spoon.Body.rotation = 12 * Mathf.Sin(t * 3);
                    break;
                case FluidExperimentScenario.SealedShake:
                    shaker.SetHeldPose(new Vector2(.7f * Mathf.Sin(t * 4), .2f + .3f * Mathf.Sin(t * 8)), 65 * Mathf.Sin(t * 3));
                    break;
            }
        }
        private void FixedUpdate()
        {
            if (automaticScenario) AdvanceScenario(Time.fixedDeltaTime);
        }
        private void Update()
        {
            if (!Ready) return;
            world.interactor.pointerBlockRect = ActiveControlsRect;
            if (ActiveScenario == FluidExperimentScenario.VolumeCheck) FrameVolumeVessel();
            if (VolumeSampleAvailable && lastVolumeSnapshot != Gpu.SnapshotRevision)
            {
                VolumeMetrics.Measure(Gpu, VolumeVessel, RequestedVolumeMl);
                lastVolumeSnapshot = Gpu.SnapshotRevision;
            }
            if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchMode(FluidExperimentMode.ACurrent);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchMode(FluidExperimentMode.BReferencePhysics);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchMode(FluidExperimentMode.CReferenceSurface);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SwitchMode(FluidExperimentMode.DImprovedSurface);
            if (Input.GetKeyDown(KeyCode.Alpha5)) SwitchMode(FluidExperimentMode.ECalibratedLiquid);
            if (Input.GetKeyDown(KeyCode.R)) StartScenario(ActiveScenario);
        }
        private void FrameVolumeVessel()
        {
            if (VolumeVessel == null) return;
            Camera camera = world.interactor.inputCamera;
            Rect interior = VolumeVessel.collisionProfile != null
                ? VolumeVessel.collisionProfile.InteriorBounds : VolumeVessel.contentRegions[0];
            Vector2 center = VolumeVessel.LocalToWorld(interior.center);
            Vector3 scale = VolumeVessel.transform.lossyScale;
            float width = Mathf.Max(1, camera.pixelWidth), height = Mathf.Max(1, camera.pixelHeight);
            float panelWidth = showControls ? ActiveControlsRect.xMax + 12 : 0;
            float availableWidth = Mathf.Max(1, width - panelWidth);
            camera.orthographicSize = Mathf.Max(1.5f, interior.height * Mathf.Abs(scale.y) * .8f,
                interior.width * Mathf.Abs(scale.x) * .7f * height / availableWidth);
            // Keep the measured liquid visible beside the controls, including resized Game views.
            float centerOffset = panelWidth / width * camera.orthographicSize * camera.aspect;
            camera.transform.position = new Vector3(center.x - centerOffset, center.y, -20);
        }
        private void OnGUI()
        {
            if (!showControls) return;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(ControlsScale, ControlsScale, 1));
            GUILayout.BeginArea(LogicalControlsRect, GUI.skin.box);
            GUILayout.Label("GPU FLUID EXPERIMENT  |  " + ModeName(ActiveMode));
            GUILayout.BeginHorizontal();
            foreach (FluidExperimentMode mode in Enum.GetValues(typeof(FluidExperimentMode)))
            {
                if (mode == FluidExperimentMode.DImprovedSurface) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                if (GUILayout.Button(ModeName(mode), GUILayout.Height(27)) && Ready) SwitchMode(mode);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Same scene / requested ml / replay. D: C physics. E: calibrated area and new physics.");
            if (ActiveMode == FluidExperimentMode.ECalibratedLiquid)
            {
                GUILayout.BeginHorizontal();
                foreach (FluidExperimentMaterial material in Enum.GetValues(typeof(FluidExperimentMaterial)))
                    if (GUILayout.Button(material.ToString(), GUILayout.Height(23)))
                    {
                        Gpu.improvedMaterial = material;
                        StartScenario(ActiveScenario);
                    }
                GUILayout.EndHorizontal();
                GUILayout.Label("Material: " + Gpu.improvedMaterial + " | volume from each vessel's capacity and interior area");
            }
            GUILayout.BeginHorizontal();
            foreach (FluidExperimentScenario scenario in Enum.GetValues(typeof(FluidExperimentScenario)))
                if (GUILayout.Button(scenario.ToString(), GUILayout.Height(25)) && Ready) StartScenario(scenario);
            GUILayout.EndHorizontal();
            if (ActiveScenario == FluidExperimentScenario.VolumeCheck) DrawVolumeControls();
            GUILayout.Label("Replay: " + ActiveScenario + "  " + ScenarioTime.ToString("F1") + "s");
            GUILayout.Label("1 - 5: compare    R: restart    Manual: LMB pick/place/swap, RMB rotate, C lid");
            GUILayout.Label(Ready ? "GPU: " + SystemInfo.graphicsDeviceName + "  |  particles: " + Gpu.ActiveCount
                + "  |  " + Gpu.SnapshotTotalMl.ToString("F1") + " ml" : "GPU unavailable: " + (Error ?? Gpu?.Error));
            GUILayout.Label("A: baseline | B: reference physics | C: density surface | D: new surface | E: calibrated liquid");
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }
        private void DrawVolumeControls()
        {
            FluidExperimentBody selectedVessel = null;
            GUILayout.BeginHorizontal();
            foreach (FluidExperimentBody body in world.Items)
                if (body.kind == LabItemKind.Glass
                    && GUILayout.Button(body.displayName, GUILayout.Height(23)))
                    selectedVessel = body;
            GUILayout.EndHorizontal();
            if (selectedVessel != null) StartVolumeCheck(selectedVessel, RequestedVolumeMl);
            GUILayout.BeginHorizontal();
            foreach (float ml in new[] { 30f, 60f, 120f })
                if (GUILayout.Button(ml + " ml", GUILayout.Height(23)) && VolumeVessel != null)
                    StartVolumeCheck(VolumeVessel, ml);
            GUILayout.EndHorizontal();
            GUILayout.Label("Requested " + RequestedVolumeMl.ToString("F1") + " ml | Queued "
                + QueuedVolumeMl.ToString("F1") + " ml | Not filled "
                + Mathf.Max(0, RequestedVolumeMl - QueuedVolumeMl).ToString("F1") + " ml");
            if (!VolumeSampleAvailable) { GUILayout.Label("Waiting for liquid measurement..."); return; }
            GUILayout.Label("In glass " + VolumeMetrics.ContainedMl.ToString("F1") + " ml | Outside "
                + VolumeMetrics.OutsideMl.ToString("F1") + " ml | Active " + VolumeMetrics.ActiveMl.ToString("F1") + " ml");
            GUILayout.Label("Particle height (95%) " + VolumeMetrics.ParticleHeight95.ToString("F3")
                + " u | Mean speed " + VolumeMetrics.MeanSpeed.ToString("F3") + " u/s");
            GUILayout.Label("Not filled is an initial fill limit, not spilled liquid.");
        }
        public static string ModeName(FluidExperimentMode mode) => mode switch {
            FluidExperimentMode.ACurrent => "1: A Current",
            FluidExperimentMode.BReferencePhysics => "2: B Physics",
            FluidExperimentMode.CReferenceSurface => "3: C Surface",
            FluidExperimentMode.DImprovedSurface => "4: D New Surface",
            _ => "5: E Calibrated Liquid"
        };
        private void OnDestroy()
        {
            if (Gpu != null && Gpu.settings == runtimeSettings) Gpu.settings = sourceSettings;
            if (runtimeSettings != null) Destroy(runtimeSettings);
        }
    }
}
