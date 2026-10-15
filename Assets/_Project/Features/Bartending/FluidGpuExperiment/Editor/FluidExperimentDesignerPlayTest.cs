using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Bartending.FluidGpuExperiment.Editor
{
    /// <summary>Starts one disposable scene via Unity's playModeStartScene without closing user scenes.</summary>
    [InitializeOnLoad]
    public static class FluidExperimentDesignerPlayTest
    {
        [Serializable] public sealed class Request
        {
            public string source, folder, snapshot, previousStartScene;
            public string[] originalProfiles;
            public bool[] cloneProfiles;
            public bool dirty, explosive;
            public float height = 3, fillMl = 40, duration = 4;
            public Vector2 velocity;
            public int repeats = 3;
        }
        const string RequestKey = "FluidDesigner.PlayTest.Request";
        const string ResultKey = "FluidDesigner.PlayTest.Result";
        const string ReadyKey = "FluidDesigner.PlayTest.Restore";
        const string TargetName = "DesignerTestItem";
        static FluidExperimentRuntimeRig rig;
        static FluidExperimentBody target;
        static float started, nextRead;
        static int run, errorCount;
        static float peakShardSpeed;
        static bool initialized;
        static string lines = "";
        public static string Status { get; private set; } = "Ready";
        public static string Results => SessionState.GetString(ResultKey, "No play test has run yet.");
        public static bool Active => !string.IsNullOrEmpty(SessionState.GetString(RequestKey, ""))
            && !SessionState.GetBool(ReadyKey, false);
        public static event Action Changed;
        static FluidExperimentDesignerPlayTest()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Tick;
        }
        public static Request PendingRestore()
        {
            if (!SessionState.GetBool(ReadyKey, false)) return null;
            string json = SessionState.GetString(RequestKey, "");
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Request>(json);
        }
        public static void FinishRestore()
        {
            var request = PendingRestore();
            SessionState.EraseString(RequestKey); SessionState.EraseBool(ReadyKey);
            if (request != null && !string.IsNullOrEmpty(request.folder)
                && request.folder.StartsWith(FluidExperimentBuilder.Root + "/DesignerTemp/", StringComparison.Ordinal))
                AssetDatabase.DeleteAsset(request.folder);
        }
        public static void Start(FluidExperimentDesignerSession session, Request request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before starting a designer test.");
            if (Active) throw new InvalidOperationException("A designer play test is already active.");
            session.Validate();
            if (!float.IsFinite(request.height) || request.height < 0 || !float.IsFinite(request.fillMl) || request.fillMl < 0
                || !float.IsFinite(request.velocity.x) || !float.IsFinite(request.velocity.y)
                || !float.IsFinite(request.duration) || request.duration < .25f || request.repeats < 1 || request.repeats > 100)
                throw new ArgumentException("Use finite nonnegative height/fill, duration >= 0.25, and 1–100 repeats.");
            request.source = session.SourcePath; request.dirty = session.Dirty;
            request.originalProfiles = session.Profiles.Select(p => AssetDatabase.GetAssetPath(p.original)).ToArray();
            request.cloneProfiles = session.Profiles.Select(p => p.cloneForPrefab).ToArray();
            request.folder = FluidExperimentBuilder.Root + "/DesignerTemp/Run_" + Guid.NewGuid().ToString("N");
            request.previousStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
            var previousActive = SceneManager.GetActiveScene();
            Scene testScene = default;
            try
            {
                request.snapshot = session.SaveSnapshot(request.folder);
                // Opening a copied saved template preserves even an unnamed, dirty user scene.
                string scenePath = request.folder + "/PlayTest.unity";
                testScene = FluidExperimentAssemblyBuilder.CreateWorkingScene(scenePath);
                var testPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(request.snapshot);
                var sandbox = FluidExperimentAssemblyBuilder.CreateSandbox(testScene, testPrefab);
                var item = sandbox.GetComponentsInChildren<FluidExperimentBody>(true)
                    .FirstOrDefault(b => b.name.Contains("TestItem") || b.name == testPrefab.name);
                if (item == null) item = sandbox.SpawnObject(testPrefab, Vector2.up * 3).GetComponent<FluidExperimentBody>();
                item.name = TargetName;
                // The runtime rig deliberately owns simulation mode; set its exposed enum without a comparison component.
                var serialized = new SerializedObject(sandbox);
                var style = serialized.FindProperty("breakageStyle");
                if (style != null) { style.enumValueIndex = request.explosive ? 1 : 0; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                if (!EditorSceneManager.SaveScene(testScene, scenePath)) throw new InvalidOperationException("Could not save the isolated play-test scene.");
                EditorSceneManager.CloseScene(testScene, true); testScene = default;
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                SessionState.SetString(RequestKey, JsonUtility.ToJson(request));
                SessionState.SetBool(ReadyKey, false); SessionState.SetString(ResultKey, "Starting isolated play test…");
                Status = "Starting isolated play test…"; Changed?.Invoke();
                EditorApplication.isPlaying = true;
            }
            catch
            {
                if (testScene.IsValid() && testScene.isLoaded) EditorSceneManager.CloseScene(testScene, true);
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(request.previousStartScene);
                if (!string.IsNullOrEmpty(request.folder)) AssetDatabase.DeleteAsset(request.folder);
                throw;
            }
        }
        public static void Stop()
        {
            if (Active && EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
        static void OnPlayMode(PlayModeStateChange state)
        {
            if (string.IsNullOrEmpty(SessionState.GetString(RequestKey, ""))) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                initialized = false; run = errorCount = 0; lines = ""; peakShardSpeed = 0;
                started = Time.realtimeSinceStartup;
                Application.logMessageReceived += Log;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Application.logMessageReceived -= Log;
                if (initialized && rig != null) RecordRun("Stopped");
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                var request = JsonUtility.FromJson<Request>(SessionState.GetString(RequestKey, ""));
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(request.previousStartScene)
                    ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(request.previousStartScene);
                SessionState.SetBool(ReadyKey, true);
                Status = "Test stopped; original editor scenes restored.";
                Changed?.Invoke();
                FluidExperimentDesignerWindow.RestoreAfterPlayTest();
            }
        }
        static void Log(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            errorCount++; lines += "ERROR: " + message + "\n";
            SessionState.SetString(ResultKey, lines); Changed?.Invoke();
        }
        static void Tick()
        {
            if (!EditorApplication.isPlaying || !Active) return;
            var request = JsonUtility.FromJson<Request>(SessionState.GetString(RequestKey, ""));
            try
            {
                if (!initialized)
                {
                    rig = UnityEngine.Object.FindFirstObjectByType<FluidExperimentRuntimeRig>();
                    target = rig != null ? rig.GetComponentsInChildren<FluidExperimentBody>(true).FirstOrDefault(b => b.name == TargetName) : null;
                    if (rig == null || target == null || rig.Liquid == null || !rig.Liquid.IsOperational)
                    {
                        if (Time.realtimeSinceStartup - started > 20) throw new InvalidOperationException("Play-test rig did not initialize within 20 seconds.");
                        return;
                    }
                    initialized = true; BeginRun(request); return;
                }
                if (Time.realtimeSinceStartup >= nextRead)
                {
                    nextRead = Time.realtimeSinceStartup + .2f;
                    if (rig.World.GlassShards.Count > 0)
                        peakShardSpeed = Mathf.Max(peakShardSpeed, rig.World.GlassShards.Max(s => s.Body.linearVelocity.magnitude));
                    Status = $"Run {run}/{request.repeats} · {Time.realtimeSinceStartup - started:F1}s · shards {rig.World.GlassShards.Count}"
                        + $" · liquid boundaries {rig.Liquid.LastShardBoundaryCount} (skipped {rig.Liquid.SkippedShardBoundaryCount}) · errors {errorCount}";
                    Changed?.Invoke();
                }
                if (Time.realtimeSinceStartup - started < request.duration) return;
                RecordRun("Completed");
                if (run < request.repeats) BeginRun(request);
                else { initialized = false; EditorApplication.isPlaying = false; }
            }
            catch (Exception ex)
            {
                lines += "TEST FAILED: " + ex.Message + "\n"; SessionState.SetString(ResultKey, lines);
                initialized = false; EditorApplication.isPlaying = false;
            }
        }
        static void BeginRun(Request request)
        {
            run++; peakShardSpeed = 0;
            rig.ResetRig();
            rig.PrepareDrop(target, request.height, request.velocity, request.fillMl);
            started = Time.realtimeSinceStartup; nextRead = started;
        }
        static void RecordRun(string state)
        {
            var breakable = target != null ? target.GetComponent<FluidExperimentBreakableGlass>() : null;
            if (rig == null || rig.Liquid == null || !rig.Liquid.IsOperational)
            {
                lines += $"{state} run {run}: liquid simulation is unavailable; no metrics were published.\n";
                SessionState.SetString(ResultKey, lines); Changed?.Invoke(); return;
            }
            rig.Liquid.ReadbackNow();
            var ledger = rig.Liquid.Ledger;
            if (ledger == null)
            {
                lines += $"{state} run {run}: stopped before the first liquid metrics snapshot.\n";
                SessionState.SetString(ResultKey, lines); Changed?.Invoke(); return;
            }
            var totals = ledger.Total;
            lines += $"{state} run {run}: broken={breakable != null && breakable.IsBroken}, shards={rig.World.GlassShards.Count}, "
                + $"impact={breakable?.LastImpactSpeed ?? 0:F2}, impulse={breakable?.LastImpactImpulse ?? 0:F2}, "
                + $"peak shard speed={peakShardSpeed:F2}, active={totals.ActiveMl:F2} ml, "
                + $"conservation error={totals.ConservationErrorMl:F4} ml, shard boundaries={rig.Liquid.LastShardBoundaryCount}, "
                + $"skipped={rig.Liquid.SkippedShardBoundaryCount}, errors={errorCount}\n";
            SessionState.SetString(ResultKey, lines); Changed?.Invoke();
        }
    }
}
