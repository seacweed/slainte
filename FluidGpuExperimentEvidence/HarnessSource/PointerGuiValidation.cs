using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Slainte.Bartending.FluidGpuExperiment;

// Interactive isolated Editor check: real Mouse.position/OS warps, not a batchmode mock.
public static class ExperimentPointerGuiValidation
{
    private const string Key = "FluidExperiment.PointerGui";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    [InitializeOnLoadMethod]
    private static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("PointerGuiProbe").AddComponent<ExperimentPointerGuiRunner>();
    };
    public static void Begin()
    {
        if (Application.isBatchMode) throw new Exception("Native pointer check requires an interactive Editor");
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    public static void Finish(bool passed, string text)
    {
        File.WriteAllText(Path.Combine(Evidence, "pointer-gui-result.txt"), (passed ? "PASS\n" : "FAIL\n") + text);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
    }
}

public sealed class ExperimentPointerGuiRunner : MonoBehaviour
{
    private readonly List<string> report = new List<string>();
    private string status = "Click Start in this Game view to validate the native rotation pointer.";
    private bool startRequested;
    private IEnumerator Start()
    {
        var comparison = FindFirstObjectByType<FluidExperimentComparison>();
        float timeout = Time.realtimeSinceStartup + 180;
        while ((!startRequested || !Application.isFocused || comparison == null || !comparison.Ready) && Time.realtimeSinceStartup < timeout)
            yield return null;
        if (!startRequested || !Application.isFocused || comparison == null || !comparison.Ready)
        { ExperimentPointerGuiValidation.Finish(false, "No focused operational Game view within 180 seconds"); yield break; }
        comparison.automaticScenario = false; comparison.showControls = false;
        comparison.StartScenario(FluidExperimentScenario.Manual);
        var world = comparison.World; var hand = world.interactor;
        var bottle = world.Items.First(x => x.kind == LabItemKind.Bottle);
        bottle.Teleport(new Vector2(0, 1), 0);
        hand.Pick(bottle, bottle.Position); hand.BeginRotation();
        status = "Native pointer anchored at bottle pivot. Do not move the mouse during this short probe.";
        yield return new WaitForSecondsRealtime(1);
        bool passed = true;
        var camera = hand.inputCamera;
        var deltaMethod = hand.GetType().GetMethod("RotationPointerDelta", BindingFlags.Instance | BindingFlags.NonPublic);
        // End-of-frame warps reach the input update on the next frame. RotateBy also
        // exercises the projection of a constrained pivot at changing body angles.
        for (int frame = 0; frame < 90; frame++)
        {
            yield return null;
            Vector2 target = camera.WorldToScreenPoint(hand.RotationPointerWorld);
            Vector2 actual = Mouse.current.position.ReadValue();
            float error = Vector2.Distance(actual, target);
            float delta = (float)deltaMethod.Invoke(hand, new object[] { actual });
            if (!Application.isFocused || error > 2 || Mathf.Abs(delta) > .0001f) passed = false;
            report.Add($"frame={frame} target={target:F3} actual={actual:F3} error={error:F3} spuriousRotation={delta:F3}");
            hand.RotateBy(frame < 45 ? 2 : -2);
        }
        hand.EndRotation(camera.ScreenToWorldPoint(Mouse.current.position.ReadValue()));
        bool anchoring = (bool)hand.GetType().GetField("rotationPointerAnchored", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hand);
        passed &= !anchoring && hand.Returning;
        report.Add("Return releases cursor anchoring immediately: " + (!anchoring && hand.Returning));
        yield return new WaitForSecondsRealtime(.25f);
        passed &= !hand.Returning;
        hand.ReleaseWithVelocity(Vector2.zero);
        report.Add("Return completes and object releases: " + (!hand.Returning && hand.Held == null));
        ExperimentPointerGuiValidation.Finish(passed, string.Join("\n", report));
    }
    private void OnGUI()
    {
        GUI.Box(new Rect(10, Screen.height - 100, 780, 90), status);
        if (!startRequested && GUI.Button(new Rect(25, Screen.height - 60, 180, 40), "Start native pointer test"))
            startRequested = true;
    }
}
