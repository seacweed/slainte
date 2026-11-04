using System.Collections;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    /// <summary>Interactive cohesive-liquid sandbox. The class identity preserves the authored scene.</summary>
    [DefaultExecutionOrder(100), DisallowMultipleComponent]
    public sealed class FluidExperimentComparison : MonoBehaviour
    {
        public FluidExperimentWorld world;
        public bool showControls = true;
        public FluidExperimentBreakageStyle breakageStyle = FluidExperimentBreakageStyle.Natural;
        public FluidExperimentWorld World => world;
        public FluidExperimentGpuLiquid Gpu => world != null ? world.Liquid : null;
        public bool Ready => started && Gpu != null && Gpu.IsOperational;
        public string Error => configurationError ?? (Gpu != null ? Gpu.Error : "Sandbox liquid is missing.");
        public static Rect ControlsRect => new Rect(12, 12, 545, 245);
        private float ControlsScale => Mathf.Min(1, Screen.width / 960f, Screen.height / 640f);
        private Rect ActiveControlsRect
        {
            get
            {
                Rect rect = ControlsRect;
                float scale = ControlsScale;
                return new Rect(rect.x * scale, rect.y * scale, rect.width * scale, rect.height * scale);
            }
        }

        private FluidExperimentLiquidSettings runtimeSettings;
        private FluidExperimentLiquidSettings sourceSettings;
        private Vector3 initialCameraPosition;
        private float initialCameraSize;
        private bool started;
        private string configurationError;

        private void Awake()
        {
            if (world == null) world = GetComponent<FluidExperimentWorld>();
            if (world == null || Gpu == null || world.interactor == null)
            { configurationError = "Sandbox requires World, Liquid and Interactor components."; return; }
            world.showControls = false;
            sourceSettings = Gpu.settings;
            if (sourceSettings != null)
            {
                runtimeSettings = Instantiate(sourceSettings);
                runtimeSettings.name = sourceSettings.name + " (sandbox)";
                runtimeSettings.hideFlags = HideFlags.DontSave;
                Gpu.settings = runtimeSettings;
            }
            SetBreakageStyle(breakageStyle);
            UpdatePointerBlock();
        }

        private IEnumerator Start()
        {
            // The world captures authored body and supply state before the sandbox's first reset.
            yield return null;
            if (configurationError != null || Gpu == null) yield break;
            Camera camera = world.interactor.inputCamera;
            if (camera == null) { configurationError = "Sandbox input camera is missing."; yield break; }
            initialCameraPosition = camera.transform.position;
            initialCameraSize = camera.orthographicSize;
            started = true;
            ResetSandbox();
        }

        public void SetBreakageStyle(FluidExperimentBreakageStyle style)
        {
            breakageStyle = style;
            if (world != null) world.ExplosiveGlassBreakage = style == FluidExperimentBreakageStyle.Explosive;
        }

        public void ResetSandbox()
        {
            if (!Ready) return;
            world.ResetSession();
            world.SetDispenserActive(true);
            world.interactor.enabled = true;
            Camera camera = world.interactor.inputCamera;
            camera.transform.position = initialCameraPosition;
            camera.orthographicSize = initialCameraSize;
            UpdatePointerBlock();
        }

        private void UpdatePointerBlock()
        {
            if (world != null && world.interactor != null)
                world.interactor.pointerBlockRect = showControls ? ActiveControlsRect : Rect.zero;
        }

        private void Update()
        {
            UpdatePointerBlock();
            if (Ready && Input.GetKeyDown(KeyCode.R)) ResetSandbox();
        }

        private void OnGUI()
        {
            if (!showControls) return;
            bool previousEnabled = GUI.enabled;
            Matrix4x4 previousMatrix = GUI.matrix;
            try
            {
                if (world != null && world.Cocktails != null && Event.current != null
                    && world.Cocktails.BlocksPointer(new Vector2(Event.current.mousePosition.x, Screen.height - Event.current.mousePosition.y)))
                    GUI.enabled = false;
                GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(ControlsScale, ControlsScale, 1));
                GUILayout.BeginArea(ControlsRect, GUI.skin.box);
                GUILayout.Label("FLUID SANDBOX  |  Cohesive liquid");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Material", GUILayout.Width(65));
                if (Gpu != null)
                    foreach (FluidExperimentMaterial material in System.Enum.GetValues(typeof(FluidExperimentMaterial)))
                        if (GUILayout.Toggle(Gpu.cohesiveMaterial == material, material.ToString(), GUI.skin.button, GUILayout.Height(23))
                            && Gpu.cohesiveMaterial != material)
                        { Gpu.cohesiveMaterial = material; ResetSandbox(); }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Ice / liquid", GUILayout.Width(85));
                if (Gpu != null)
                {
                    if (GUILayout.Toggle(Gpu.iceLiquidMode == FluidExperimentIceLiquidMode.NonColliding,
                        "No collision", GUI.skin.button, GUILayout.Height(25)))
                        Gpu.iceLiquidMode = FluidExperimentIceLiquidMode.NonColliding;
                    if (GUILayout.Toggle(Gpu.iceLiquidMode == FluidExperimentIceLiquidMode.Colliding,
                        "Collision", GUI.skin.button, GUILayout.Height(25)))
                        Gpu.iceLiquidMode = FluidExperimentIceLiquidMode.Colliding;
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Glass breakage", GUILayout.Width(100));
                if (GUILayout.Toggle(breakageStyle == FluidExperimentBreakageStyle.Natural,
                    "Natural", GUI.skin.button, GUILayout.Height(25))) SetBreakageStyle(FluidExperimentBreakageStyle.Natural);
                if (GUILayout.Toggle(breakageStyle == FluidExperimentBreakageStyle.Explosive,
                    "Explosive", GUI.skin.button, GUILayout.Height(25))) SetBreakageStyle(FluidExperimentBreakageStyle.Explosive);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Reset sandbox [R]", GUILayout.Height(25))) ResetSandbox();
                GUILayout.Label("LMB: pick/place/parts  |  RMB: rotate/dispense/stir  |  C: shaker");
                GUILayout.Label(Ready ? "GPU: " + SystemInfo.graphicsDeviceName + "  |  particles: " + Gpu.ActiveCount
                    + "  |  " + Gpu.SnapshotTotalMl.ToString("F1") + " ml" : "GPU unavailable: " + Error);
                GUILayout.Label("Garnish: click a jar, then place. Placed garnish/ice cannot be picked again.");
                GUILayout.EndArea();
            }
            finally { GUI.matrix = previousMatrix; GUI.enabled = previousEnabled; }
        }

        private void OnDestroy()
        {
            if (Gpu != null && Gpu.settings == runtimeSettings) Gpu.settings = sourceSettings;
            if (runtimeSettings != null) Destroy(runtimeSettings);
        }
    }
}
