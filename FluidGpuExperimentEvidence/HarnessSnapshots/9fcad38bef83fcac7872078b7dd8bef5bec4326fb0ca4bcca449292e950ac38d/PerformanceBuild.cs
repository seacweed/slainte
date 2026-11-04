using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Evidence-only entry point: runs only in prepare_experiment.py's copied harness project.
// It builds an explicit single-scene Windows player and never edits EditorBuildSettings.scenes.
public static class ExperimentPerformanceBuild
{
    [Serializable] private sealed class Result
    {
        public string status, player, project, error;
        public bool windowsModuleAvailable, frameTimingEnabled = true;
        public string options = "StandaloneWindows64, non-development, explicit isolated comparison scene";
    }
    public static void BuildBenchmark()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        const string scene = "Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity";
        string manifestPath = Path.Combine(project, "source-sha256.json");
        // The preparation manifest lives at the exact project root and records the copied scene and
        // runtime. Its presence is independent of the user's chosen isolated-harness directory name.
        string manifest = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : "";
        if (!manifest.Contains(scene) || !manifest.Contains("FluidExperimentGpuLiquid.cs")
            || !File.Exists(Path.Combine(project, scene)))
            throw new InvalidOperationException("BuildBenchmark requires the prepared isolated evidence project.");
        string output = Environment.GetEnvironmentVariable("PHYSICSLAB_PLAYER_DIR")
            ?? Path.Combine(project, "PlayerBenchmark");
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var result = new Result { project = project, player = Path.Combine(output, "FluidExperimentBenchmark.exe"),
            windowsModuleAvailable = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64) };
        if (!result.windowsModuleAvailable)
        {
            result.status = "UNSUPPORTED_WINDOWS_BUILD_MODULE";
            File.WriteAllText(Path.Combine(output, "player-build-result.json"), JsonUtility.ToJson(result, true));
            Debug.LogWarning(result.status); if (Application.isBatchMode) EditorApplication.Exit(2); return;
        }
        bool oldFrameTiming = PlayerSettings.enableFrameTimingStats;
        try
        {
            PlayerSettings.enableFrameTimingStats = true;
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { scene },
                locationPathName = result.player, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            result.status = report.summary.result == BuildResult.Succeeded ? "BUILT" : "FAILED";
            if (report.summary.result != BuildResult.Succeeded)
                result.error = report.summary.totalErrors + " build errors; inspect Unity build log.";
        }
        catch (Exception exception) { result.status = "FAILED"; result.error = exception.ToString(); }
        finally
        {
            PlayerSettings.enableFrameTimingStats = oldFrameTiming;
            File.WriteAllText(Path.Combine(output, "player-build-result.json"), JsonUtility.ToJson(result, true));
        }
        Debug.Log(JsonUtility.ToJson(result));
        if (Application.isBatchMode) EditorApplication.Exit(result.status == "BUILT" ? 0 : 1);
    }
}
