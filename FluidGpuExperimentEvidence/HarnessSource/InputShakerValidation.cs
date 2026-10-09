using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

public static class ExperimentInputShakerValidation
{
    private const string Key = "Slainte.FluidGpuExperiment.InputShakerValidation";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR")
        ?? "FluidGpuExperimentEvidence/input-shaker-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }
    public static void Begin()
    {
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Path.Combine(Evidence, "input-shaker-validation.txt"), "STARTED; no result yet.\n");
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    private static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("InputShakerValidationRunner").AddComponent<ExperimentInputShakerValidationRunner>();
    }
    public static void Finish(bool passed, string report)
    {
        File.WriteAllText(Path.Combine(Evidence, "input-shaker-validation.txt"), report);
        File.WriteAllText(Path.Combine(Evidence, "input-shaker-result.txt"), passed ? "PASS\n" : "FAIL\n");
        if (passed) Debug.Log("[ExperimentInputShakerValidation] PASS\n" + report);
        else Debug.LogError("[ExperimentInputShakerValidation] FAIL\n" + report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
    }
}

public sealed class ExperimentInputShakerValidationRunner : MonoBehaviour
{
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private FluidExperimentWorld world;
    private FluidExperimentInteractor hand;
    private SimulationMode2D originalMode;
    private Vector2 originalGravity;
    private bool controlled, finished;
    private float started;
    private IEnumerator Start()
    {
        started = Time.realtimeSinceStartup;
        Application.logMessageReceived += OnLog;
        yield return null;
        var comparison = FindFirstObjectByType<FluidExperimentComparison>();
        while (comparison != null && !comparison.Ready && Time.realtimeSinceStartup - started < 20)
            yield return null;
        try
        {
            Require(comparison != null && comparison.Ready, "Authored comparison scene initializes");
            originalMode = Physics2D.simulationMode;
            originalGravity = Physics2D.gravity;
            controlled = true;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = Vector2.zero;
            comparison.automaticScenario = false;
            comparison.showControls = false;
            comparison.SwitchMode(FluidExperimentMode.DImprovedSurface);
            comparison.StartScenario(FluidExperimentScenario.Manual);
            world = comparison.World;
            hand = world.interactor;
            world.enabled = false;
            hand.enabled = false;
            world.Liquid.automaticReadback = false;
            int index = 0;
            foreach (FluidExperimentBody item in world.Items)
            {
                item.SetHeld(false);
                item.Teleport(new Vector2(-40 - index++ * 4, 20), 0);
                item.Body.bodyType = RigidbodyType2D.Kinematic;
                item.pourMlPerSecond = 0;
            }
            ExperimentRelativePointerChecks.Run(world, Require);
            ValidateReturn(world.Items.First(x => x.kind == LabItemKind.Bottle && x.name == "Bottle_item_1002"));
            ExperimentReturnPourChecks.Run(world, Require);
            ValidateShaker(world.Items.First(x => x.kind == LabItemKind.Shaker));
            Require(errors.Count == 0, "No runtime error, exception or assertion was logged");
            Finish(true, "Input continuity and detachable shaker checks passed in Model D.\n");
        }
        catch (Exception exception) { Finish(false, exception.ToString()); }
    }

    private void ValidateReturn(FluidExperimentBody item)
    {
        // Keep the entire rotating envelope away from the independently tested ceiling clamp.
        item.Teleport(new Vector2(-6, 4), 0);
        Require(hand.Pick(item, item.Position + new Vector2(.2f, .1f)), "Pick body at an off-center grab point");
        hand.BeginRotation();
        hand.RotateBy(855);
        ApplyPose();
        Vector2 startPosition = item.Position;
        Vector2 startPointer = startPosition + new Vector2(2, 1);
        hand.EndRotation(startPointer);
        Near(item.Position, startPosition, "Releasing rotation introduces no initial position jump");
        Sample(1, true);
        Vector2 firstDelta = new Vector2(.6f, -.2f);
        hand.AdvanceUprightReturn(hand.uprightReturnDuration * .25f, startPointer + firstDelta);
        Sample(1.04f);
        Near(item.Position, startPosition + firstDelta, "Mouse translation continues during the first quarter of return");
        Require(hand.Returning && item.Angle > 0 && item.Angle < 135,
            "Multiple turns restore over the shortest arc while still returning");
        hand.EstimateRelease(out Vector2 velocity, out float spin);
        Near(velocity, firstDelta / .04f, "User translation is retained in release samples during return", .002f);
        Require(Mathf.Abs(spin) < .001f, "Automatic upright rotation contributes no sampled spin");
        Vector2 finalDelta = new Vector2(1.5f, .4f);
        hand.AdvanceUprightReturn(hand.uprightReturnDuration, startPointer + finalDelta);
        Sample(1.16f);
        Require(!hand.Returning && Mathf.Abs(item.HeldAngle) < .001f, "Return completes at angle zero");
        Near(item.Position, startPosition + finalDelta, "Final return frame still tracks the cursor");
        hand.MoveHeld(startPointer + finalDelta);
        ApplyPose();
        Near(item.Position, startPosition + finalDelta, "Normal dragging begins without an end-of-return jump");
        hand.MoveHeld(startPointer + finalDelta + Vector2.up);
        ApplyPose();
        Near(item.Position, startPosition + finalDelta + Vector2.up, "No blocked synchronization frames after return");
        Sample(1.18f);
        hand.EstimateRelease(out _, out spin);
        Require(Mathf.Abs(spin) < .001f, "Finishing return cannot introduce delayed release spin");

        hand.BeginRotation(); hand.RotateBy(90); ApplyPose();
        Vector2 stationary = item.Position;
        hand.EndRotation(stationary); Sample(2, true);
        hand.AdvanceUprightReturn(hand.uprightReturnDuration * .5f, stationary); Sample(2.075f);
        hand.EstimateRelease(out velocity, out spin);
        Near(velocity, Vector2.zero, "Stationary cursor return creates no throw impulse");
        Require(Mathf.Abs(spin) < .001f, "Stationary cursor return creates no spin impulse");
        float interruptedAngle = item.HeldAngle;
        hand.BeginRotation(); hand.RotateBy(12); ApplyPose();
        Require(hand.Rotating && !hand.Returning && Mathf.Abs(item.HeldAngle - interruptedAngle - 12) < .001f,
            "New manual rotation interrupts return at the current angle");
        hand.EndRotation(item.Position); Sample(3, true);
        Vector2 nextPointer = item.Position + new Vector2(.3f, .2f);
        hand.AdvanceUprightReturn(hand.uprightReturnDuration * .5f, nextPointer); Sample(3.075f);
        hand.EstimateRelease(out velocity, out spin);
        Require(hand.Drop(nextPointer), "Dropping while moving during return succeeds");
        Near(item.Body.linearVelocity, velocity, "Drop during return preserves real user translation");
        Require(Mathf.Abs(item.Body.angularVelocity) < .001f, "Drop during return excludes automatic spin");
        item.Teleport(new Vector2(-30, 20), 0);
    }

    private void ValidateShaker(FluidExperimentBody shaker)
    {
        shaker.Teleport(new Vector2(0, 5), 0);
        shaker.SetSealed(true);
        Physics2D.SyncTransforms();
        var cap = shaker.ShakerCap;
        var strainer = shaker.ShakerStrainer;
        Require(cap != null && strainer != null, "Existing cap and strainer sprites become separate interactive parts");
        Require(shaker.HasCap && shaker.HasStrainer && shaker.sealedVessel && shaker.BlocksIceMouth,
            "Closed shaker blocks liquid and ice");
        Vector2 capHome = cap.Position;
        Vector2 strainerHome = strainer.Position;
        Require(hand.PickAt(capHome) && hand.HeldPart == cap && hand.Held == null,
            "Cap click selects the cap before the overlapping body and strainer");
        Near(cap.Position, capHome, "Cap detaches without jumping");
        Require(!shaker.HasCap && shaker.HasStrainer && !shaker.sealedVessel && shaker.BlocksIceMouth
            && shaker.capCollider.enabled, "Cap removal opens liquid flow while keeping the ice barrier");
        Require(shaker.ShakerStrainerLiquidHulls.Length > 0 && shaker.ShakerOutletWidth > world.Liquid.Radius * 2,
            "Strainer has liquid guide walls and a usable outlet aperture");
        Vector2[] interior = shaker.LiquidInteriorPath;
        Near((interior[0] + interior[interior.Length - 1]) * .5f, shaker.ShakerOutletLocal,
            "Ownership contour closes across the actual strainer outlet");
        Require(shaker.ContainsLiquid(shaker.LocalToWorld(shaker.ShakerOutletLocal - Vector2.up * .12f)),
            "Strainer neck is included in liquid containment");
        hand.MoveHeld(capHome + Vector2.right * 2);
        Require(hand.Drop(capHome + Vector2.right * 2) && !cap.IsAttached && !cap.IsHeld
            && cap.Body.bodyType == RigidbodyType2D.Dynamic, "Cap can be placed independently in the world");
        Require(hand.PickPart(cap, cap.Position), "Detached cap can be picked up again");
        hand.MoveHeld(capHome);
        Require(hand.Drop(capHome) && shaker.HasCap && shaker.sealedVessel,
            "Cap snaps back onto the attached strainer and seals liquid");

        Require(hand.PickAt(strainerHome) && hand.HeldPart == strainer,
            "Strainer click detaches the strainer independently of the shaker body");
        Require(!shaker.HasStrainer && !shaker.HasCap && !shaker.BlocksIceMouth && !shaker.capCollider.enabled,
            "Removing the strainer opens the bowl to both ice and liquid");
        Require(cap.IsAttached && cap.transform.parent == strainer.transform,
            "Attached cap remains on a detached strainer");
        Vector2 delta = new Vector2(2.5f, 1);
        hand.MoveHeld(strainerHome + delta);
        Physics2D.SyncTransforms();
        Near(cap.Position, capHome + delta, "Cap follows the moved strainer as one assembly");
        Require(hand.Drop(strainerHome + delta) && !strainer.IsAttached && !cap.Body.simulated,
            "Detached combined lid can be placed as one physical body");
        Require(hand.PickAt(cap.Position) && hand.HeldPart == cap,
            "Cap can be removed from the separately placed strainer");
        hand.MoveHeld(capHome);
        hand.Drop(capHome);
        Require(!cap.IsAttached && !shaker.HasCap,
            "Cap cannot attach directly to the shaker while its strainer is elsewhere");
        Require(hand.PickPart(cap, cap.Position), "Pick free cap for attachment to detached strainer");
        Vector2 carrierTarget = strainer.transform.TransformPoint(cap.HomeLocalPosition - strainer.HomeLocalPosition);
        hand.MoveHeld(carrierTarget); hand.Drop(carrierTarget);
        Require(cap.IsAttached && cap.transform.parent == strainer.transform && !shaker.HasCap,
            "Cap attaches to a detached strainer without falsely sealing the shaker");
        Require(hand.PickPart(strainer, strainer.Position), "Pick the combined detached assembly");
        hand.MoveHeld(strainerHome); hand.Drop(strainerHome);
        Require(shaker.HasStrainer && shaker.HasCap && shaker.sealedVessel,
            "Combined strainer and cap reattach and close the shaker together");

        shaker.CycleShakerClosure();
        Require(shaker.ShakerClosure == FluidExperimentShakerClosure.Straining, "C cycle exposes the straining state");
        shaker.CycleShakerClosure();
        Require(shaker.ShakerClosure == FluidExperimentShakerClosure.Open, "C cycle exposes the fully open state");
        shaker.CycleShakerClosure();
        Require(shaker.ShakerClosure == FluidExperimentShakerClosure.Closed, "C cycle restores both parts");
        shaker.SetShakerClosure(FluidExperimentShakerClosure.Open);
        Vector2 detachedPosition = strainer.Position;
        shaker.gameObject.SetActive(false);
        Require(!strainer.gameObject.activeInHierarchy && !cap.gameObject.activeInHierarchy,
            "Disabling the owner also disables its detached combined lid");
        shaker.gameObject.SetActive(true);
        Require(strainer.gameObject.activeInHierarchy && cap.gameObject.activeInHierarchy
            && !shaker.HasStrainer && !shaker.HasCap,
            "Re-enabling the owner restores detached parts without changing assembly state");
        Near(strainer.Position, detachedPosition, "Owner disable and re-enable preserve the detached part pose");
        shaker.SetSealed(true);
        Require(hand.PickPart(cap, cap.Position), "Hold a detached cap before resetting the session");
        world.ResetSession();
        Require(hand.HeldPart == null && hand.Held == null && shaker.HasCap && shaker.HasStrainer,
            "Session reset releases the held part and restores the initial complete assembly");
        Require(world.GetComponentsInChildren<FluidExperimentShakerPart>(true).Length == 2,
            "Repeated open and close operations reuse exactly the two original parts");
    }

    private void ApplyPose() => world.SendMessage("FixedUpdate");
    private void Sample(float time, bool clear = false)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        if (clear) ((IList)typeof(FluidExperimentInteractor).GetField("samples", flags).GetValue(hand)).Clear();
        typeof(FluidExperimentInteractor).GetMethod("Sample", flags).Invoke(hand, new object[] { time });
    }
    private void Near(Vector2 actual, Vector2 expected, string label, float tolerance = .0002f)
        => Require(Vector2.Distance(actual, expected) <= tolerance,
            label + " (actual " + actual.ToString("F5") + ", expected " + expected.ToString("F5") + ")");
    private void Require(bool condition, string label)
    {
        report.Add((condition ? "PASS " : "FAIL ") + label);
        if (!condition) throw new Exception(label);
    }
    private void OnLog(string message, string stack, LogType type)
    {
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            errors.Add(message + "\n" + stack);
    }
    private void Finish(bool passed, string detail)
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        if (controlled) { Physics2D.simulationMode = originalMode; Physics2D.gravity = originalGravity; }
        report.Add(detail);
        ExperimentInputShakerValidation.Finish(passed, string.Join("\n", report) + "\n");
    }
}
