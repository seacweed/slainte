using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Slainte.Bartending;
using Slainte.Bartending.FluidGpuExperiment;
using Slainte.Content;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Slainte.EditorTools
{
    // 참조 점검과 실제 그래픽 장치의 게임 씬 검증을 구분한다. Play 검증은 진행 저장을 억제한다.
    [InitializeOnLoad]
    public static class GpuCraftingIntegrationValidator
    {
        private const string ActiveKey = "Slainte.GpuCrafting.Validation.Active";
        private const string BatchKey = "Slainte.GpuCrafting.Validation.Batch";
        private const string ResultKey = "Slainte.GpuCrafting.Validation.Result";
        private const string SetupKey = "Slainte.GpuCrafting.Validation.SceneSetup";
        private const string ManualKey = "Slainte.GpuCrafting.Validation.Manual";
        private const string BusinessScene = "Assets/_Project/Scenes/Production/BusinessScene.unity";
        private static double startedAt;
        [Serializable]
        private sealed class SetupSnapshot { public SceneSetup[] scenes; }
        private static bool finishing;

        static GpuCraftingIntegrationValidator()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
            startedAt = EditorApplication.timeSinceStartup;
        }

        [MenuItem("Slainte/Bartending/Validate GPU Crafting References")]
        public static void ValidateReferences()
        {
            var settings = Resources.Load<BusinessBartendingSettings>(ProjectResourcePaths.BartendingSettings);
            Require(settings != null && settings.gpuCrafting != null && settings.gpuCrafting.runtimePrefab != null,
                "Production GPU settings/runtime reference missing.");
            var rig = settings.gpuCrafting.runtimePrefab;
            Require(rig.world != null && rig.liquid != null && rig.interactor != null && rig.catalog != null,
                "GPU runtime modules/catalog missing.");
            Require(rig.liquidSettings != null && rig.liquidSettings.gpuLiquidComputeShader != null
                && rig.liquidSettings.surfaceDisplayShader != null && rig.liquid.drawShader != null
                && rig.liquid.improvedSurfaceCompute != null && rig.liquid.improvedAccumulationShader != null
                && rig.liquid.improvedCompositeShader != null, "Direct shader references required for player builds.");
            int bottles = 0;
            foreach (var item in Resources.LoadAll<ItemDef>(ProjectResourcePaths.BartendingItems).Where(i => i.type == ItemType.Bottle))
            {
                Require(rig.catalog.entries.Any(e => e?.ingredient != null && e.ingredient.id == item.id
                    && e.prefab != null && e.prefab.TryGetComponent(out FluidExperimentBody body)
                    && body.kind == LabItemKind.Bottle), "GPU bottle mapping missing: " + item.id);
                bottles++;
            }
            var cabinet = Resources.Load<ToolCabinetCatalog>(ProjectResourcePaths.BartendingToolCabinetCatalog);
            Require(cabinet != null, "Tool cabinet catalog missing.");
            foreach (var glass in cabinet.glasses.Where(g => g != null))
                Require(rig.catalog.entries.Any(e => e?.prefab != null && e.prefab.TryGetComponent(out FluidExperimentBody body)
                    && body.kind == LabItemKind.Glass && body.servingGlassId == glass.glassId), "GPU glass mapping missing: " + glass.StableId);
            foreach (var garnish in settings.gpuCrafting.garnishes)
                Require(!string.IsNullOrEmpty(garnish.inventoryId) && garnish.prefab != null
                    && garnish.prefab.kind == LabItemKind.Garnish, "GPU garnish mapping invalid.");
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(BusinessScene) != null, "Production BusinessScene missing.");
            Debug.Log($"[GpuCraftingIntegration] REFERENCES PASS: {bottles} bottles, {cabinet.glasses.Length} glasses, GPU/shader/garnish references.");
        }

        public static void RunBatchReferences()
        {
            ValidateReferences();
            BusinessIntegrationRulesValidator.RunBatchValidation();
            PlanningCsvAssetImporter.ValidateImportedAssets();
        }

        [MenuItem("Slainte/Bartending/Validate GPU Crafting Play Mode %#F8")]
        public static void RunFromMenu() => Begin(false);
        public static void RunPlayModeBatch()
        {
            Slainte.Editor.WorldCapacityLabelValidator.ValidateAll();
            Begin(true);
        }

        [MenuItem("Slainte/Bartending/Inspect GPU Crafting In Production %#F9")]
        public static void RunManualInspection() => Begin(false, true);

        [MenuItem("Slainte/Bartending/Capture GPU Liquid Frame %#F10")]
        public static void CaptureLiquidFrame()
        {
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<BusinessBartendingBootstrap>();
            var session = bootstrap != null ? bootstrap.ActiveSession : null;
            if (!EditorApplication.isPlaying || session == null || !session.Ready)
            {
                Debug.LogWarning("[GpuCrafting] Enter crafting in Play Mode before capturing liquid.");
                return;
            }

            var liquid = session.Rig.Liquid;
            var camera = session.Rig.inputCamera;
            var surfaceField = typeof(FluidExperimentGpuLiquid).GetField("surfaceComposite", BindingFlags.NonPublic | BindingFlags.Instance);
            var surface = surfaceField?.GetValue(liquid) as RenderTexture;
            if (surface == null || camera.targetTexture == null || !surface.IsCreated() || !camera.targetTexture.IsCreated())
            {
                Debug.LogWarning("[GpuCrafting] There is no completed liquid frame to capture.");
                return;
            }

            var folder = Path.GetFullPath("Library/GpuLiquidDiagnostics/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            SaveLiquidTexture(surface, Path.Combine(folder, "free-liquid.png"));
            SaveLiquidTexture(camera.targetTexture, Path.Combine(folder, "bartending-camera.png"));
            foreach (string fieldName in new[] { "surfaceDensity", "surfaceColor" })
            {
                var texture = typeof(FluidExperimentGpuLiquid).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(liquid) as RenderTexture;
                if (texture != null && texture.IsCreated()) SaveLiquidTexture(texture, Path.Combine(folder, fieldName + ".png"));
            }
            var ellipses = liquid.ReadImprovedSurfaceParticles().Where(p => p.Active != 0).Select(p =>
                FormattableString.Invariant($"{p.VesselId},{p.StartPosition.x:R},{p.StartPosition.y:R},{p.EndPosition.x:R},{p.EndPosition.y:R},{p.Radii.x:R},{p.Radii.y:R},{p.CoverageWeight:R},{p.OpacityScale:R},{p.BirthFraction:R}"));
            File.WriteAllLines(Path.Combine(folder, "ellipses.csv"),
                new[] { "owner,start_x,start_y,end_x,end_y,radius_x,radius_y,coverage,opacity,birth_fraction" }.Concat(ellipses));
            liquid.ReadbackNow();
            var particles = liquid.Snapshot.Where(p => p.Active != 0).Select(p =>
            {
                Vector3 current = camera.WorldToViewportPoint(p.Position);
                Vector3 previous = camera.WorldToViewportPoint(p.PreviousPosition);
                return FormattableString.Invariant($"{p.VesselId},{current.x:R},{current.y:R},{previous.x:R},{previous.y:R},{p.VolumeMl:R}");
            });
            File.WriteAllLines(Path.Combine(folder, "particles.csv"),
                new[] { "owner,x_uv,y_uv,previous_x_uv,previous_y_uv,ml" }.Concat(particles));
            var worktop = session.Rig.environment.table.bounds.max.y;
            var owners = typeof(FluidExperimentGpuLiquid).GetField("liquidLayerOwners", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(liquid) as IList;
            object densityOwner = owners != null && owners.Count > 0 ? owners[owners.Count - 1] : null;
            File.WriteAllText(Path.Combine(folder, "frame.txt"),
                FormattableString.Invariant($"frame={Time.frameCount}, surfaceFrame={liquid.LastRenderedSurfaceFrame}, graphics={SystemInfo.graphicsDeviceType}, screen={Screen.width}x{Screen.height}, camera={camera.pixelWidth}x{camera.pixelHeight}, worktopY={camera.WorldToViewportPoint(new Vector3(0, worktop)).y:R}, densityOwner={densityOwner}, interpolation={liquid.ImprovedSurfaceInterpolation:R}"));
            SaveLiquidGeometry(session, Path.Combine(folder, "geometry.txt"));
            Debug.Log("[GpuCrafting] Liquid frame saved to " + folder);
        }

        private static void SaveLiquidGeometry(GpuCraftingSession session, string path)
        {
            var camera = session.Rig.inputCamera;
            var lines = new List<string>();
            void AddBounds(string name, Vector2 min, Vector2 max)
            {
                var lo = camera.WorldToViewportPoint(min);
                var hi = camera.WorldToViewportPoint(max);
                lines.Add(FormattableString.Invariant($"{name} uv=({lo.x:F4},{lo.y:F4})–({hi.x:F4},{hi.y:F4})"));
            }

            foreach (var hull in session.Rig.World.EnvironmentHulls)
            {
                var collider = hull.Collider;
                if (collider != null && collider.isActiveAndEnabled)
                    AddBounds("Environment " + collider.name, collider.bounds.min, collider.bounds.max);
            }
            foreach (var body in session.Rig.World.Items.Where(b => b != null && b.isActiveAndEnabled))
                AddBounds($"Body {body.Id} {body.name}", body.SolidBounds.min, body.SolidBounds.max);

            // These are the hull bounds used by the last liquid surface pass, including invisible geometry.
            var field = typeof(FluidExperimentGpuLiquid).GetField("improvedSurfaceHulls", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(session.Rig.Liquid) is IEnumerable hulls)
                foreach (var hull in hulls)
                {
                    var type = hull.GetType();
                    var bounds = (Vector4)type.GetField("Bounds").GetValue(hull);
                    AddBounds($"GPU hull owner={type.GetField("VesselId").GetValue(hull)} flags={type.GetField("Held").GetValue(hull)}",
                        new Vector2(bounds.x, bounds.y), new Vector2(bounds.z, bounds.w));
                }

            var output = session.Viewport.GetComponent<RawImage>();
            var corners = new Vector3[4];
            var canvas = output.canvas.rootCanvas;
            foreach (var graphic in canvas.GetComponentsInChildren<Graphic>())
            {
                if (!graphic.isActiveAndEnabled || graphic.depth <= output.depth || graphic.canvasRenderer.cull) continue;
                graphic.rectTransform.GetWorldCorners(corners);
                var canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var lo = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[0]);
                var hi = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[2]);
                lines.Add(FormattableString.Invariant($"Canvas above output {graphic.name} depth={graphic.depth} screen=({lo.x:F1},{lo.y:F1})–({hi.x:F1},{hi.y:F1})"));
            }
            File.WriteAllLines(path, lines);
        }

        private static void SaveLiquidTexture(RenderTexture target, string path)
        {
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        private static void Begin(bool batch, bool manual = false)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before running the integration check.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "Save modified scenes before running the integration check.");
            ValidateReferences();
            SessionState.SetString(SetupKey, JsonUtility.ToJson(new SetupSnapshot { scenes = EditorSceneManager.GetSceneManagerSetup() }));
            var scene = EditorSceneManager.OpenScene(BusinessScene, OpenSceneMode.Single);
            var host = new GameObject("[DEV] GPU Crafting Integration Probe");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<GpuCraftingIntegrationProbe>().manualInspection = manual;
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetBool(ManualKey, manual);
            SessionState.SetString(ResultKey, string.Empty);
            startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.isPlaying = true;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying || finishing) return;
            var probe = UnityEngine.Object.FindFirstObjectByType<GpuCraftingIntegrationProbe>();
            if (probe != null && probe.Done)
            {
                Finish(probe.Passed, probe.Passed ? string.Join("\n", probe.Checks) : probe.Failure);
                return;
            }
            if (EditorApplication.timeSinceStartup - startedAt > (SessionState.GetBool(ManualKey, false) ? 600 : 120))
                Finish(false, "GPU crafting integration timed out.");
        }

        private static void Finish(bool passed, string detail)
        {
            finishing = true;
            string text = (passed ? "PASS" : "FAIL") + "\n" + detail;
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/GpuCrafting-playmode-result.txt", text);
            SessionState.SetString(ResultKey, text);
            EditorApplication.isPlaying = false;
        }

        private static void HandlePlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { startedAt = EditorApplication.timeSinceStartup; return; }
            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(ActiveKey, false);
            bool batch = SessionState.GetBool(BatchKey, false);
            string result = SessionState.GetString(ResultKey, string.Empty);
            finishing = false;
            if (batch) EditorApplication.Exit(result.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1);
            else
            {
                string snapshot = SessionState.GetString(SetupKey, string.Empty);
                if (!string.IsNullOrEmpty(snapshot))
                    EditorSceneManager.RestoreSceneManagerSetup(JsonUtility.FromJson<SetupSnapshot>(snapshot).scenes);
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
