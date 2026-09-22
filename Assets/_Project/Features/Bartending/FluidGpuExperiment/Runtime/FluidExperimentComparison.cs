using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum FluidExperimentMode { ACurrent, BReferencePhysics, CReferenceSurface }
    public enum FluidExperimentScenario { Manual, Rest, Pour, Tilt, Stir, SealedShake }

    /// <summary>One world at a time, reset to the same authored state for every comparison.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class FluidExperimentComparison : MonoBehaviour
    {
        public FluidExperimentWorld world;
        public FluidExperimentMode initialMode = FluidExperimentMode.CReferenceSurface;
        public bool automaticScenario = true;
        public bool showControls = true;
        public FluidExperimentWorld World => world;
        public FluidExperimentGpuLiquid Gpu => world != null ? world.Liquid : null;
        public FluidExperimentMode ActiveMode { get; private set; }
        public FluidExperimentScenario ActiveScenario { get; private set; }
        public bool Ready { get; private set; }
        public float ScenarioTime { get; private set; }
        public string Error { get; private set; }
        public static Rect ControlsRect => new Rect(12, 12, 545, 225);

        private FluidExperimentLiquidSettings runtimeSettings;
        private FluidExperimentLiquidSettings sourceSettings;
        private readonly Dictionary<FluidExperimentBody, float> flowRates = new Dictionary<FluidExperimentBody, float>();
        private Vector3 initialCameraPosition;
        private float initialCameraSize;
        private FluidExperimentBody bottle, glass, shaker, spoon;

        private void Awake()
        {
            if (world == null) world = GetComponent<FluidExperimentWorld>();
            if (world == null || Gpu == null) { Error = "Comparison world is missing."; return; }
            world.showControls = false;
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
            Gpu.solver = mode == FluidExperimentMode.ACurrent
                ? FluidExperimentSolver.BaselinePbf : FluidExperimentSolver.ReferenceSph;
            Gpu.useReferenceSurface = mode == FluidExperimentMode.CReferenceSurface;
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
        }
        public void StartScenario(FluidExperimentScenario scenario)
        {
            if (!Ready) return;
            ResetComparison(); ActiveScenario = scenario;
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
                glass.Teleport(new Vector2(0, -.4f), 0);
                Gpu.Fill(glass, bottle.ingredient, 60);
                if (scenario == FluidExperimentScenario.Pour)
                {
                    bottle.Teleport(new Vector2(0, 3.5f), 180); bottle.ResetSupply(700, 0);
                }
                if (scenario == FluidExperimentScenario.Stir)
                {
                    // Held vessels intentionally ignore unrelated tool boundaries.
                    // Keep both participants unheld and kinematic for this replay.
                    glass.SetHeld(false); glass.Body.bodyType = RigidbodyType2D.Kinematic;
                    spoon.Teleport(new Vector2(0, .7f), 0);
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
                    spoon.Body.position = new Vector2(.28f * Mathf.Sin(t * 3), .7f + .12f * Mathf.Sin(t * 6));
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
            if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchMode(FluidExperimentMode.ACurrent);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchMode(FluidExperimentMode.BReferencePhysics);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchMode(FluidExperimentMode.CReferenceSurface);
            if (Input.GetKeyDown(KeyCode.R)) StartScenario(ActiveScenario);
        }
        private void OnGUI()
        {
            if (!showControls) return;
            GUILayout.BeginArea(ControlsRect, GUI.skin.box);
            GUILayout.Label("GPU FLUID EXPERIMENT  |  " + ModeName(ActiveMode));
            GUILayout.BeginHorizontal();
            foreach (FluidExperimentMode mode in Enum.GetValues(typeof(FluidExperimentMode)))
                if (GUILayout.Button(ModeName(mode), GUILayout.Height(27)) && Ready) SwitchMode(mode);
            GUILayout.EndHorizontal();
            GUILayout.Label("Same scene / particle volume / replay. Switching modes restarts the selected replay.");
            GUILayout.BeginHorizontal();
            foreach (FluidExperimentScenario scenario in Enum.GetValues(typeof(FluidExperimentScenario)))
                if (GUILayout.Button(scenario.ToString(), GUILayout.Height(25)) && Ready) StartScenario(scenario);
            GUILayout.EndHorizontal();
            GUILayout.Label("Replay: " + ActiveScenario + "  " + ScenarioTime.ToString("F1") + "s");
            GUILayout.Label("1 / 2 / 3: compare    R: restart    Manual: LMB pick/place, RMB rotate, C lid");
            GUILayout.Label(Ready ? "GPU: " + SystemInfo.graphicsDeviceName + "  |  particles: " + Gpu.ActiveCount
                + "  |  " + Gpu.SnapshotTotalMl.ToString("F1") + " ml" : "GPU unavailable: " + (Error ?? Gpu?.Error));
            GUILayout.Label("A = existing PBF copy    B = reference GPU physics    C = reference physics + density surface");
            GUILayout.EndArea();
        }
        public static string ModeName(FluidExperimentMode mode) => mode == FluidExperimentMode.ACurrent ? "1: A Current"
            : mode == FluidExperimentMode.BReferencePhysics ? "2: B Physics" : "3: C Surface";
        private void OnDestroy()
        {
            if (Gpu != null && Gpu.settings == runtimeSettings) Gpu.settings = sourceSettings;
            if (runtimeSettings != null) Destroy(runtimeSettings);
        }
    }
}
