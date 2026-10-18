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
using UnityEngine.InputSystem.LowLevel;
using Slainte.Bartending.FluidGpuExperiment;

public static class ExperimentDispenserValidation
{
    const string Key = "FluidExperiment.DispenserValidation";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("DispenserValidation").AddComponent<DispenserValidationRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
}

// This fixture runs only in the disposable hidden GPU harness. Virtual input is
// queued into Unity; it never drives the user's desktop cursor or opens a window.
public sealed class DispenserValidationRunner : MonoBehaviour
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const float Dt = .02f;
    readonly List<string> report = new List<string>(), errors = new List<string>();
    FluidExperimentComparison comparison;
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentInteractor hand;
    FluidExperimentDispenser dispenser;
    float floorY;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    object Field(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
    void SetField(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
    object Invoke(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, Members).Invoke(target, arguments);
    void Check(bool okay, string text)
    {
        report.Add((okay ? "PASS " : "FAIL ") + text);
        if (!okay) throw new Exception(text);
    }
    bool Near(float a, float b, float epsilon = .001f) => Mathf.Abs(a - b) <= epsilon;
    bool Near(Vector2 a, Vector2 b, float epsilon = .002f) => Vector2.Distance(a, b) <= epsilon;
    bool Finite(Vector2 vector) => float.IsFinite(vector.x) && float.IsFinite(vector.y);
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) errors.Add(message); }

    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        float deadline = Time.realtimeSinceStartup + 30;
        while (comparison != null && !comparison.Ready && Time.realtimeSinceStartup < deadline) yield return null;
        var previousSimulation = Physics2D.simulationMode;
        var previousGravity = Physics2D.gravity;
        bool success = false;
        try
        {
            Check(Application.isBatchMode && comparison != null && comparison.Ready,
                "Hidden Unity GPU harness initializes without desktop input");
            comparison.automaticScenario = false;
            comparison.showControls = false;
            comparison.StartScenario(FluidExperimentScenario.Manual);
            world = comparison.World; gpu = comparison.Gpu; hand = world.interactor;
            dispenser = world.Dispenser;
            Check(comparison.ActiveMode == FluidExperimentMode.FCoherentLiquid && gpu.CohesivePhysicsActive,
                "Authored default remains F coherent liquid");
            Check(dispenser != null, "F Manual instantiates the authored dispenser prefab");
            world.seedLiquids = false;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = new Vector2(0, -9.81f);
            floorY = world.floorColliders.Where(c => c != null).Max(c => c.bounds.max.y);
            Reset();
            AuthoredContract();
            VirtualInput();
            ModeAndInventory();
            HotEmissionAndFailure();
            IceEmissionAndFailure();
            BlockedEmissionAndBoundaryBudget();
            ShardBoundaryBudget();
            ReceivingGlassIntegration();
            CollisionAndRetraction();
            FullIceInventory();
            FullHotInventory();
            ResetAndModeScope();
            Check(errors.Count == 0, "No Unity errors, assertions or exceptions");
            success = true;
        }
        catch (Exception exception) { report.Add(exception.ToString()); }
        finally
        {
            Physics2D.simulationMode = previousSimulation;
            Physics2D.gravity = previousGravity;
            Application.logMessageReceived -= Log;
            report.AddRange(errors);
            File.WriteAllText(Path.Combine(Evidence, "dispenser-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(Evidence, "dispenser-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }

    void DisableAutomaticSimulation()
    {
        world.enabled = false; hand.enabled = false;
        gpu.automaticReadback = false;
    }
    void Reset()
    {
        world.ResetSession();
        DisableAutomaticSimulation();
        foreach (var item in world.Items.ToArray())
        {
            item.Body.interpolation = RigidbodyInterpolation2D.None;
            if (item == dispenser.Body) continue;
            item.SetHeld(false);
            item.Teleport(new Vector2(-60 - item.Id * 3, 20), 0);
            item.Body.simulated = false;
            // Disabled/interpolated rigidbodies can retain their old rendered Transform.
            // Keep hidden fixture bodies out of both the native scene and captures.
            item.transform.SetPositionAndRotation(item.Position, Quaternion.Euler(0, 0, item.Angle));
            item.pourMlPerSecond = 0;
        }
        gpu.ResetSimulation();
        Physics2D.SyncTransforms();
    }
    void Step(float dt = Dt)
    {
        dispenser.StepMotion(dt);
        Physics2D.SyncTransforms();
        Physics2D.Simulate(dt);
        world.TickLiquid(dt);
        Invoke(dispenser, "LateUpdate");
    }
    void Move(Vector2 destination, int frames = 80)
    {
        dispenser.MoveToPointer(destination);
        for (int i = 0; i < frames; i++) Step();
    }
    Vector2 WorkPoint => new Vector2(0, floorY + 4);
    void Select(string mode)
    {
        Check(dispenser.State.ToString() == "Docked", "Mode fixture starts docked");
        Vector2 point = mode == "Ice" ? dispenser.IceButtonWorld : dispenser.HotButtonWorld;
        Check(dispenser.TryClickMode(point) && dispenser.Mode.ToString() == mode, "Select " + mode + " at the authored button position");
    }
    void PickToWork()
    {
        Check(hand.PickDispenser(dispenser, dispenser.Body.Position), "Pick dispenser through its interactor API");
        Move(WorkPoint);
        Check(Near(dispenser.Body.Position, WorkPoint, .06f), "Held dispenser reaches the unobstructed work position");
    }
    void Retract()
    {
        hand.ReleaseWithVelocity(Vector2.zero);
        for (int i = 0; i < 500 && dispenser.State.ToString() != "Docked"; i++) Step();
        Check(dispenser.State.ToString() == "Docked" && Near(dispenser.Body.Position, dispenser.DockWorld, .025f),
            "Released dispenser completes collision-aware retraction to its dock");
    }
    FluidExperimentBody[] Ice() => world.Items.Where(item => item != null && item.kind == LabItemKind.Ice).ToArray();
    void ClearIce()
    {
        foreach (var ice in Ice()) { ice.gameObject.SetActive(false); Destroy(ice.gameObject); }
    }

    void AuthoredContract()
    {
        Check(dispenser.IsAvailable && dispenser.State.ToString() == "Docked" && dispenser.Mode.ToString() == "None",
            "F Manual starts available, docked and off");
        Check(Near(dispenser.RemainingMl, 5000) && Near(dispenser.capacityMl, 5000), "The authored shared capacity is 5000 ml");
        Check(Near(dispenser.hotFlowMlPerSecond, 40) && Near(dispenser.iceInterval, .3f),
            "The authored rates are 40 ml per second and one ice per 0.3 seconds");
        Check(dispenser.hotWater != null && dispenser.icePrefab != null && dispenser.icePrefab.kind == LabItemKind.Ice,
            "Dispenser has authored hot water and existing native ice references");
        Check(dispenser.hotWater.servingTemperatureC > 40, "Hot water carries an explicit hot serving temperature");
        Check(dispenser.noneSprite != null && dispenser.iceSprite != null && dispenser.hotSprite != null
            && new[] { dispenser.noneSprite, dispenser.iceSprite, dispenser.hotSprite }.Distinct().Count() == 3,
            "Three distinct imported sprites represent off, Ice and Hot");
        Check(dispenser.bodyRenderer != null && dispenser.bodyRenderer.sprite == dispenser.noneSprite,
            "Docked off mode displays the configured off sprite");
        Check(dispenser.Contains(dispenser.Body.Position) && dispenser.Body.solidColliders.Any(c => c != null && !c.isTrigger),
            "Dispenser body is pickable and has solid collision geometry");
        Check(dispenser.Hose != null && dispenser.Hose.positionCount >= 3
            && dispenser.Hose.GetComponents<Collider2D>().Length == 0,
            "Hose is a multipoint curve with no native collider");
        Check((hand.inputCamera.cullingMask & (1 << dispenser.Hose.gameObject.layer)) != 0,
            "Runtime-created hose is on a layer visible to the authored gameplay camera");
        Check(!dispenser.Body.IsVessel && !dispenser.Body.IsLooseSolid, "Dispenser has no internal liquid vessel or loose-solid behavior");
        var hurricane = world.Items.First(item => item.GetComponent<FluidExperimentBreakableGlass>() != null)
            .GetComponent<FluidExperimentBreakableGlass>();
        Check(Near(hurricane.profile.minimumImpactSpeed, 6) && Near(hurricane.profile.minimumImpulsePerMass, 4),
            "Dispenser integration preserves the remote hurricane impact thresholds of 6 and 4");
        dispenser.SetDispensing(true);
        for (int i = 0; i < 20; i++) Step();
        Check(!dispenser.IsDispensing && Near(dispenser.RemainingMl, 5000) && Ice().Length == 0 && Near(gpu.EmittedMl, 0),
            "Docked/off tool cannot dispense even when asked");
        Capture("dispenser-docked.png", dispenser.DockWorld, 2.2f);
    }

    void VirtualInput()
    {
        Reset();
        var previousMouse = Mouse.current;
        var previousKeyboard = Keyboard.current;
        var previousUpdate = InputSystem.settings.updateMode;
        var previousBackground = InputSystem.settings.backgroundBehavior;
        var previousEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
        bool previousRun = Application.runInBackground;
        var previousLock = Cursor.lockState; bool previousVisible = Cursor.visible;
        Rect previousBlock = hand.pointerBlockRect;
        Mouse mouse = null; Keyboard keyboard = null;
        var update = typeof(FluidExperimentInteractor).GetMethod("Update", Members);
        var inputUpdate = typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(InputUpdateType) }, null);
        void Frame(Vector2 point, bool left = false, bool right = false, Key? key = null)
        {
            Vector2 screen = hand.inputCamera.WorldToScreenPoint(point);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }
                .WithButton(MouseButton.Left, left).WithButton(MouseButton.Right, right));
            InputSystem.QueueStateEvent(keyboard, key.HasValue ? new KeyboardState(key.Value) : new KeyboardState());
            inputUpdate.Invoke(null, new object[] { InputUpdateType.Manual });
            mouse.MakeCurrent(); keyboard.MakeCurrent(); update.Invoke(hand, null);
        }
        void Click(Vector2 point) { Frame(point); Frame(point, true); }
        try
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true; hand.pointerBlockRect = default;
            mouse = InputSystem.AddDevice<Mouse>("DispenserVirtualMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("DispenserVirtualKeyboard");
            mouse.MakeCurrent(); keyboard.MakeCurrent();
            Click(dispenser.IceButtonWorld);
            Check(dispenser.Mode.ToString() == "Ice" && hand.HeldDispenser == null && dispenser.State.ToString() == "Docked",
                "Actual Ice button click selects mode without picking the overlapping body");
            update.Invoke(hand, null); Frame(dispenser.IceButtonWorld, true);
            Check(dispenser.Mode.ToString() == "Ice", "Repeated Update or holding LMB does not toggle twice");
            Click(dispenser.IceButtonWorld);
            Check(dispenser.Mode.ToString() == "None", "Clicking the active Ice button toggles off");
            Click(dispenser.HotButtonWorld);
            Check(dispenser.Mode.ToString() == "Hot" && dispenser.bodyRenderer.sprite == dispenser.hotSprite,
                "Actual Hot button click selects the hot sprite");
            Click(dispenser.Body.Position);
            Check(hand.HeldDispenser == dispenser && hand.Held == null && hand.HeldPart == null
                && dispenser.State.ToString() == "Held", "Actual body click acquires the dedicated dispenser hand state");
            dispenser.TryClickMode(dispenser.IceButtonWorld);
            Check(dispenser.Mode.ToString() == "Hot", "Held dispenser ignores mode changes while consuming button hits");
            Frame(WorkPoint, false, true);
            for (int i = 0; i < 100; i++) { Frame(WorkPoint, false, true); Step(); }
            float firstVolume = gpu.EmittedMl;
            Check(dispenser.IsDispensing && firstVolume > 0 && Near(dispenser.Body.Position, WorkPoint, .06f),
                "Actual RMB hold dispenses while translating to the cursor");
            Vector2 moved = WorkPoint + new Vector2(2, .5f);
            for (int i = 0; i < 40; i++) { Frame(moved, false, true); Step(); }
            Check(Near(dispenser.Body.Position, moved, .06f) && gpu.EmittedMl > firstVolume
                && !hand.Rotating && !hand.Returning && !hand.PointerCaptured && Near(dispenser.Body.Angle, 0),
                "RMB movement continues emission without rotation, upright return or cursor capture");
            Check(Near(dispenser.Hose.GetPosition(0), dispenser.hoseAnchor)
                && Near(dispenser.Hose.GetPosition(dispenser.Hose.positionCount - 1),
                    dispenser.transform.TransformPoint(dispenser.hoseAttachmentLocal)),
                "Moving hose retains its fixed overhead anchor and follows the actual tool attachment");
            Vector2 blockedScreen = hand.inputCamera.WorldToScreenPoint(moved);
            hand.pointerBlockRect = new Rect(blockedScreen.x - 10, Screen.height - blockedScreen.y - 10, 20, 20);
            Frame(moved, false, true); float blockedVolume = dispenser.RemainingMl;
            for (int i = 0; i < 10; i++) Step();
            Check(!dispenser.IsDispensing && Near(dispenser.RemainingMl, blockedVolume),
                "RMB over a blocked control area suppresses emission while the tool remains held");
            hand.pointerBlockRect = default;
            Frame(moved, false, false); float stopped = dispenser.RemainingMl;
            for (int i = 0; i < 15; i++) Step();
            Check(!dispenser.IsDispensing && Near(dispenser.RemainingMl, stopped), "RMB release stops immediately without trailing inventory drain");
            Frame(moved, false, true); Step();
            Frame(moved, false, true, Key.Escape); Step();
            Check(hand.HeldDispenser == null && !dispenser.IsDispensing && dispenser.State.ToString() != "Held",
                "Actual Escape cancels emission and releases the dispenser for safe retraction");
            Frame(moved);
            for (int i = 0; i < 500 && dispenser.State.ToString() != "Docked"; i++) Step();
            Check(dispenser.State.ToString() == "Docked", "Cancelled input returns the tool to its dock");
            Click(dispenser.Body.Position); Frame(WorkPoint, false, true); Step();
            Invoke(hand, "CancelPointerCapture"); float cancelledVolume = dispenser.RemainingMl;
            for (int i = 0; i < 20; i++) Step();
            Check(hand.HeldDispenser == null && !dispenser.IsDispensing && Near(dispenser.RemainingMl, cancelledVolume),
                "Shared focus-loss cancellation stops stock consumption and clears the held tool");
            Check(Cursor.lockState == previousLock && Cursor.visible == previousVisible,
                "Virtual input never modifies the native cursor lock or visibility");
        }
        finally
        {
            hand.ReleaseWithVelocity(Vector2.zero); hand.pointerBlockRect = previousBlock;
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            InputSystem.settings.updateMode = previousUpdate;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditor;
            Application.runInBackground = previousRun;
        }
    }

    void ModeAndInventory()
    {
        Reset(); Select("Hot"); PickToWork();
        dispenser.SetDispensing(true);
        for (int i = 0; i < 50; i++) Step();
        dispenser.SetDispensing(false);
        Check(Near(gpu.EmittedMl, 40, .51f) && Near(dispenser.RemainingMl + gpu.EmittedMl, 5000),
            "One second of Hot follows 40 ml/s and subtracts exactly the accepted GPU volume");
        float afterHot = dispenser.RemainingMl;
        Retract(); Select("Ice"); PickToWork();
        dispenser.SetDispensing(true);
        for (int i = 0; i < 16; i++) Step();
        dispenser.SetDispensing(false);
        Check(Ice().Length == 1 && Near(dispenser.RemainingMl, afterHot - 10),
            "Switching from Hot to Ice spends the same shared inventory");
        Retract();
        Check(dispenser.Mode.ToString() == "Ice" && Near(dispenser.RemainingMl, afterHot - 10),
            "Retraction preserves mode and remaining inventory");
        Check(dispenser.TryClickMode(dispenser.IceButtonWorld) && dispenser.Mode.ToString() == "None",
            "Active mode can be toggled off after docking");
        PickToWork(); dispenser.SetDispensing(true);
        float before = dispenser.RemainingMl;
        for (int i = 0; i < 30; i++) Step();
        Check(Near(dispenser.RemainingMl, before), "Held None mode never consumes inventory");
    }

    void HotEmissionAndFailure()
    {
        Reset(); Select("Hot"); PickToWork();
        dispenser.SetDispensing(true); dispenser.Tick(.1f);
        int pending = (int)Field(gpu, "pendingSpawnCount");
        var commands = (Array)Field(gpu, "spawnCommands");
        Check(pending > 0 && Near(gpu.EmittedMl, 4, .51f), "Hot emission queues actual GPU spawn commands");
        for (int i = 0; i < pending; i++)
        {
            object command = commands.GetValue(i);
            Vector2 velocity = (Vector2)Field(command, "Velocity");
            Check(Near(velocity.x, 0) && velocity.y < 0 && (uint)Field(command, "VesselId") == 0,
                "Hot particle " + i + " begins straight down without inherited dispenser movement or vessel ownership");
            Check(Near((float)Field(command, "TemperatureC"), dispenser.hotWater.servingTemperatureC),
                "Hot particle " + i + " carries the configured water temperature");
        }
        dispenser.SetDispensing(false); gpu.Step(Dt); gpu.ReadbackNow();
        Check(gpu.ActiveCount > 0 && gpu.Snapshot.Where(p => p.Active != 0).All(p => Finite(p.Position) && Finite(p.Velocity)),
            "Queued hot water becomes finite actual GPU particles");
        Check(gpu.ReadStreamParticles().Any(stream => stream.Token != 0 && stream.StreamId != 0),
            "Hot water uses the existing connected stream representation");
        Capture("dispenser-hot.png", dispenser.Body.Position - Vector2.up, 2.6f);
        Reset(); Select("Hot"); PickToWork();
        SetField(gpu, "availableSlots", 0);
        dispenser.SetDispensing(true);
        for (int i = 0; i < 100; i++) dispenser.Tick(Dt);
        Check(Near(dispenser.RemainingMl, 5000) && Near(gpu.EmittedMl, 0) && (int)Field(gpu, "pendingSpawnCount") == 0,
            "Exhausted GPU allocation rejects hot emission without inventory loss");
        SetField(gpu, "availableSlots", (int)Field(gpu, "particleCapacity"));
        dispenser.Tick(Dt);
        Check(gpu.EmittedMl > 0 && gpu.EmittedMl <= dispenser.hotFlowMlPerSecond * Dt + gpu.ParticleVolumeMl,
            "Restoring GPU slots emits only current flow, with no accumulated rejected burst");
        Check(Near(dispenser.RemainingMl + gpu.EmittedMl, 5000), "Recovery from allocation failure preserves exact stock accounting");
        foreach (float remaining in new[] { 0f, 1f, 9f, 10f, 11f })
        {
            Reset(); Select("Hot"); PickToWork(); SetField(dispenser, "remainingMl", remaining);
            dispenser.SetDispensing(true);
            for (int i = 0; i < 100; i++) Step();
            Check(Near(dispenser.RemainingMl, 0) && Near(gpu.EmittedMl, remaining),
                "Hot drains exactly " + remaining + " ml with no negative inventory");
        }
    }

    void IceEmissionAndFailure()
    {
        foreach (float remaining in new[] { 0f, 1f, 9f, 10f, 11f })
        {
            Reset(); Select("Ice"); PickToWork(); SetField(dispenser, "remainingMl", remaining);
            dispenser.SetDispensing(true);
            for (int i = 0; i < 16; i++) Step();
            int expected = remaining > 0 ? 1 : 0;
            Check(Ice().Length == expected && Near(dispenser.RemainingMl, Mathf.Max(0, remaining - 10)),
                "Ice first interval handles " + remaining + " ml with the documented final-piece rule");
            foreach (var ice in Ice())
                Check(!ice.CanBePicked && !hand.Pick(ice, ice.Position) && ice.Body.bodyType == RigidbodyType2D.Dynamic,
                    "Dispensed ice is a native dynamic body already marked placed");
            ClearIce();
            for (int i = 0; i < 20; i++) Step();
            Check(Ice().Length == (remaining > 10 ? 1 : 0) && Near(dispenser.RemainingMl, 0),
                "Ice subsequent interval exhausts " + remaining + " ml without extra pieces");
        }
        Reset(); Select("Ice"); PickToWork();
        var prefab = dispenser.icePrefab;
        try
        {
            dispenser.icePrefab = null; dispenser.SetDispensing(true);
            for (int i = 0; i < 100; i++) Step();
            Check(Ice().Length == 0 && Near(dispenser.RemainingMl, 5000), "Unavailable ice prefab consumes no shared stock");
            dispenser.icePrefab = prefab;
            for (int i = 0; i < 16; i++) Step();
            Check(Ice().Length == 1 && Near(dispenser.RemainingMl, 4990),
                "Recovered ice generation resumes one interval without an accumulated burst");
        }
        finally { dispenser.icePrefab = prefab; }
        dispenser.SetDispensing(false);
        Capture("dispenser-ice.png", dispenser.Body.Position - Vector2.up, 2.6f);
    }

    void CollisionAndRetraction()
    {
        Reset(); PickToWork();
        Move(new Vector2(0, floorY - 3), 150);
        Check(dispenser.Body.SolidBounds.min.y >= floorY - .035f,
            "Held dispenser cannot pass through the authored tabletop toward a cursor below it");
        Move(WorkPoint);
        if (world.leftBoundary != null)
        {
            Move(new Vector2(world.leftBoundary.bounds.min.x - 4, WorkPoint.y), 160);
            Check(dispenser.Body.SolidBounds.min.x >= world.leftBoundary.bounds.max.x - .04f,
                "Held dispenser cannot cross the authored left wall");
            Move(WorkPoint);
        }
        var obstacleObject = new GameObject("DispenserValidationObstacle");
        obstacleObject.layer = world.itemLayer;
        var obstacle = obstacleObject.AddComponent<BoxCollider2D>();
        obstacle.size = new Vector2(.8f, 2.2f);
        try
        {
            obstacleObject.transform.position = WorkPoint + new Vector2(2, 0);
            Physics2D.SyncTransforms();
            Move(WorkPoint + new Vector2(4, 0), 100);
            Check(dispenser.Body.solidColliders.Where(c => c != null && c.enabled).All(c => !c.Distance(obstacle).isOverlapped
                || c.Distance(obstacle).distance >= -.035f), "Continuous held movement stops or slides at solid obstacle geometry");
            Move(WorkPoint);
            obstacleObject.transform.position = (dispenser.Body.Position + dispenser.DockWorld) * .5f;
            Physics2D.SyncTransforms();
            hand.ReleaseWithVelocity(Vector2.zero);
            float maximumPenetration = 0;
            bool sawRetraction = dispenser.State.ToString() == "Retracting";
            var beforeMode = dispenser.Mode;
            dispenser.TryClickMode(dispenser.IceButtonWorld);
            Check(dispenser.Mode == beforeMode && !hand.PickDispenser(dispenser, dispenser.Body.Position),
                "Retracting tool rejects mode changes and re-pickup");
            for (int i = 0; i < 600 && dispenser.State.ToString() != "Docked"; i++)
            {
                Step();
                foreach (var collider in dispenser.Body.solidColliders.Where(c => c != null && c.enabled))
                    maximumPenetration = Mathf.Max(maximumPenetration, -collider.Distance(obstacle).distance);
            }
            Check(sawRetraction && dispenser.State.ToString() == "Docked"
                && Near(dispenser.Body.Position, dispenser.DockWorld, .03f) && maximumPenetration < .04f,
                "Retraction routes around a real blocking collider and docks without tunneling");
            Check(!dispenser.IsDispensing && dispenser.Hose.positionCount >= 3,
                "Completed retraction stops output and retains the retracted hose curve");
        }
        finally { obstacleObject.SetActive(false); Destroy(obstacleObject); }
        Capture("dispenser-returned.png", dispenser.DockWorld, 2.2f);
    }

    void BlockedEmissionAndBoundaryBudget()
    {
        foreach (string mode in new[] { "Ice", "Hot" })
        {
            Reset(); Select(mode); PickToWork();
            var blockerObject = new GameObject("DispenserBlockedNozzle");
            blockerObject.layer = world.itemLayer;
            var blocker = blockerObject.AddComponent<BoxCollider2D>();
            blocker.size = new Vector2(.8f, .5f);
            blockerObject.transform.position = dispenser.MouthWorld + Vector2.down * .28f;
            try
            {
                Physics2D.SyncTransforms(); dispenser.SetDispensing(true);
                for (int i = 0; i < 100; i++) dispenser.Tick(Dt);
                Check(Near(dispenser.RemainingMl, 5000) && Ice().Length == 0 && Near(gpu.EmittedMl, 0),
                    mode + " physically blocked nozzle consumes no inventory and creates no overlapping output");
                blockerObject.SetActive(false); Physics2D.SyncTransforms();
                dispenser.Tick(mode == "Ice" ? dispenser.iceInterval : Dt);
                Check(mode == "Ice" ? Ice().Length == 1 : gpu.EmittedMl > 0 && gpu.EmittedMl <= 1,
                    mode + " resumes a single normal emission after clearing the nozzle");
            }
            finally { blockerObject.SetActive(false); Destroy(blockerObject); }
        }
        Reset(); Select("Ice"); PickToWork();
        var ballast = new List<FluidExperimentBody>();
        try
        {
            while (gpu.CanAdmitDispenserIce(dispenser.icePrefab) && ballast.Count < 400)
            {
                var ice = Instantiate(dispenser.icePrefab, new Vector2(-50 - ballast.Count, 25), Quaternion.identity, world.transform);
                ice.Body.simulated = false; ballast.Add(ice);
            }
            Check(ballast.Count > 0 && ballast.Count < 400 && !gpu.CanAdmitDispenserIce(dispenser.icePrefab),
                "Actual registered ice geometry reaches the conservative GPU boundary admission limit");
            int before = Ice().Length;
            dispenser.SetDispensing(true);
            for (int i = 0; i < 20; i++) dispenser.Tick(dispenser.iceInterval);
            Check(Ice().Length == before && Near(dispenser.RemainingMl, 5000),
                "Full solid-boundary budget blocks ice before instantiation and preserves stock");
            foreach (var ice in ballast) { ice.gameObject.SetActive(false); Destroy(ice.gameObject); }
            ballast.Clear();
            dispenser.Tick(dispenser.iceInterval);
            Check(Ice().Length == 1 && Near(dispenser.RemainingMl, 4990),
                "Removing geometry restores one regular ice birth without a backlog");
        }
        finally
        {
            foreach (var ice in ballast) if (ice != null) { ice.gameObject.SetActive(false); Destroy(ice.gameObject); }
        }
    }

    void ShardBoundaryBudget()
    {
        Reset(); Select("Ice"); PickToWork();
        bool oldCollision = world.shardLiquidCollision;
        int oldBudget = world.maximumShardLiquidSegments;
        var ballast = new List<FluidExperimentBody>();
        try
        {
            world.shardLiquidCollision = true; world.maximumShardLiquidSegments = 512;
            var source = world.Items.First(item => item.GetComponent<FluidExperimentBreakableGlass>() != null);
            source.Body.simulated = true;
            source.Body.bodyType = RigidbodyType2D.Dynamic;
            source.Teleport(new Vector2(-30, 20), 0);
            source.transform.SetPositionAndRotation(source.Position, Quaternion.identity);
            source.SynchronizeHistory(); Physics2D.SyncTransforms();
            var fracture = source.GetComponent<FluidExperimentBreakableGlass>();
            Invoke(fracture, "QueueImpact", 10f, source.Body.mass * 10f, source.Position);
            Invoke(fracture, "CompleteBreak");
            Check(fracture.IsBroken && world.GlassShards.Count == 16
                && world.GlassShards.Sum(shard => shard.GetComponent<FluidExperimentShardLiquidProxy>().Segments.Count) > 0,
                "Ice admission fixture includes the remote real hurricane thin-wall shard proxies");
            while (gpu.CanAdmitDispenserIce(dispenser.icePrefab) && ballast.Count < 400)
            {
                var ice = Instantiate(dispenser.icePrefab, new Vector2(-50 - ballast.Count, 25), Quaternion.identity, world.transform);
                ice.Body.simulated = false; ballast.Add(ice);
            }
            Check(ballast.Count > 0 && ballast.Count < 400 && !gpu.CanAdmitDispenserIce(dispenser.icePrefab),
                "Real shard edges and registered ice together reach the conservative boundary admission limit");
            world.shardLiquidCollision = false;
            Check(gpu.CanAdmitDispenserIce(dispenser.icePrefab), "Disabling shard-liquid contacts frees their actual reserved boundary space");
            world.shardLiquidCollision = true;
            world.maximumShardLiquidSegments = 0;
            Check(gpu.CanAdmitDispenserIce(dispenser.icePrefab), "A zero shard upload budget excludes unsubmitted proxies from ice admission cost");
            world.maximumShardLiquidSegments = 512;
            int before = Ice().Length;
            dispenser.SetDispensing(true); dispenser.Tick(dispenser.iceInterval);
            Check(Ice().Length == before && Near(dispenser.RemainingMl, 5000),
                "Restoring shard contact reservations blocks ice creation without consuming inventory");
            gpu.Step(.001f);
            Check(gpu.LastShardBoundaryCount > 0 && gpu.SkippedShardBoundaryCount == 0,
                "Boundary admission leaves sufficient real GPU space for every selected hurricane shard proxy");
        }
        finally
        {
            foreach (var ice in ballast) if (ice != null) { ice.gameObject.SetActive(false); Destroy(ice.gameObject); }
            world.shardLiquidCollision = oldCollision; world.maximumShardLiquidSegments = oldBudget;
            Reset();
        }
    }

    void ReceivingGlassIntegration()
    {
        Reset(); Select("Hot"); PickToWork(); Move(WorkPoint + Vector2.up);
        var receiver = world.Items.First(item => item.kind == LabItemKind.Glass && item.name.Contains("highball"));
        Vector2 intendedReceiverPosition = dispenser.MouthWorld + Vector2.down * .6f - receiver.mouthLocal;
        // Preserve explicit evidence for the original fixture ordering before repairing it:
        // activating a disabled native body may restore its rendered Transform position.
        receiver.Teleport(intendedReceiverPosition, 0);
        Vector2 beforeActivationBody = receiver.Position;
        Vector2 beforeActivationTransform = receiver.transform.position;
        receiver.Body.simulated = true; receiver.Body.bodyType = RigidbodyType2D.Kinematic;
        Vector2 afterActivationBody = receiver.Position;
        Vector2 afterActivationTransform = receiver.transform.position;
        report.Add($"INFO receiver activation intended={intendedReceiverPosition:R} bodyBefore={beforeActivationBody:R} transformBefore={beforeActivationTransform:R} bodyAfter={afterActivationBody:R} transformAfter={afterActivationTransform:R}");
        // Activate first; establish the actual native pose last, as for other native fixtures.
        receiver.Teleport(intendedReceiverPosition, 0);
        receiver.transform.SetPositionAndRotation(intendedReceiverPosition, Quaternion.identity);
        receiver.SynchronizeHistory();
        Physics2D.SyncTransforms();
        Check(Near(receiver.Position, intendedReceiverPosition)
            && Near(receiver.transform.position, intendedReceiverPosition), "Receiving fixture activates before setting the final native and rendered pose");
        ReceiverDiagnostics(receiver, "receiver-before-emission");
        Capture("dispenser-receiver-before.png", receiver.Position + Vector2.up, 3.3f);
        dispenser.SetDispensing(true);
        for (int i = 0; i < 25; i++)
        {
            Step();
            if ((i + 1) % 5 == 0) ReceiverDiagnostics(receiver, "receiver-emitting-" + (i + 1));
        }
        dispenser.SetDispensing(false);
        for (int i = 0; i < 120; i++) Step();
        gpu.ReadbackNow();
        ReceiverDiagnostics(receiver, "receiver-after-settling");
        Capture("dispenser-fill-glass.png", receiver.Position + Vector2.up, 3.3f);
        Check(Near(gpu.EmittedMl, 20, .51f) && gpu.VolumeIn(receiver.Id) >= 18,
            "Actual emitted hot stream enters the existing glass ownership/collision path and retains at least 90 percent of a 20 ml dose");
        // Reconfigure only the supply state; keep the filled receiver and use real ice
        // contact/containment after its release from the nozzle.
        Retract(); Select("Ice");
        Check(hand.PickDispenser(dispenser, dispenser.Body.Position), "Pick Ice supply above the already filled receiver");
        Move(WorkPoint + Vector2.up);
        dispenser.SetDispensing(true);
        for (int i = 0; i < 16; i++) Step();
        dispenser.SetDispensing(false);
        for (int i = 0; i < 160; i++) Step();
        world.RefreshIceContainment();
        Check(Ice().Length == 1 && Ice()[0].ContainingVesselId == receiver.Id && !Ice()[0].CanBePicked,
            "Dispensed native ice enters the receiving glass and remains permanently placed");
        Capture("dispenser-ice-in-glass.png", receiver.Position + Vector2.up, 3.3f);
    }

    void ReceiverDiagnostics(FluidExperimentBody receiver, string stage)
    {
        gpu.ReadbackNow();
        var active = gpu.Snapshot.Where(particle => particle.Active != 0).ToArray();
        float unowned = active.Where(particle => particle.VesselId == 0).Sum(particle => particle.VolumeMl);
        float otherOwned = active.Where(particle => particle.VesselId != 0 && particle.VesselId != receiver.Id).Sum(particle => particle.VolumeMl);
        float geometricallyInside = active.Where(particle => receiver.ContainsLiquid(particle.Position)).Sum(particle => particle.VolumeMl);
        float maxSpeed = active.Length == 0 ? 0 : active.Max(particle => particle.Velocity.magnitude);
        string metrics = FormattableString.Invariant($"INFO {stage} emitted={gpu.EmittedMl:R} stock={dispenser.RemainingMl:R} owned={gpu.VolumeIn(receiver.Id):R} snapshot={gpu.SnapshotTotalMl:R} unowned={unowned:R} otherOwned={otherOwned:R} insideGeometry={geometricallyInside:R} missing={gpu.EmittedMl - gpu.SnapshotTotalMl:R} active={active.Length} maxSpeed={maxSpeed:R} blocked={dispenser.BlockedReason}");
        report.Add(metrics);
        report.Add($"INFO {stage} receiverId={receiver.Id} body={receiver.Position:R} transform={receiver.transform.position:R} scale={receiver.transform.lossyScale:R} mouth={receiver.LocalToWorld(receiver.mouthLocal):R} bounds={receiver.SolidBounds} simulated={receiver.Body.simulated} type={receiver.Body.bodyType} dispenserBody={dispenser.Body.Position:R} dispenserTransform={dispenser.transform.position:R} nozzle={dispenser.MouthWorld:R}");
        var rows = new List<string> { "index,x,y,previousX,previousY,velocityX,velocityY,volumeMl,temperatureC,vesselId,insideReceiver,active" };
        for (int i = 0; i < gpu.Snapshot.Length; i++)
        {
            var p = gpu.Snapshot[i];
            if (p.Active == 0) continue;
            rows.Add(FormattableString.Invariant($"{i},{p.Position.x:R},{p.Position.y:R},{p.PreviousPosition.x:R},{p.PreviousPosition.y:R},{p.Velocity.x:R},{p.Velocity.y:R},{p.VolumeMl:R},{p.TemperatureC:R},{p.VesselId},{receiver.ContainsLiquid(p.Position)},{p.Active}"));
        }
        File.WriteAllLines(Path.Combine(Evidence, stage + ".csv"), rows);
    }

    void FullIceInventory()
    {
        Reset(); Select("Ice"); PickToWork(); dispenser.SetDispensing(true);
        int produced = 0;
        float started = Time.realtimeSinceStartup;
        for (int cycle = 0; cycle < 500; cycle++)
        {
            // Deterministic supply throughput; actual ice is registered and GPU geometry
            // is uploaded before offscreen cleanup. This is not a 500-body frame-rate test.
            dispenser.Tick(dispenser.iceInterval + .0001f);
            var pieces = Ice();
            Check(pieces.Length == 1 && !pieces[0].CanBePicked, "Full stock cycle " + cycle + " creates exactly one placed ice body");
            produced += pieces.Length;
            gpu.Step(Dt);
            foreach (var ice in pieces) ice.Teleport(gpu.settings.gpuLiquidWorldMin - Vector2.one * 10, 0);
            world.TickLiquid(.00001f);
            Check(Ice().Length == 0, "Full stock cycle " + cycle + " releases offscreen ice from world registration");
        }
        Check(produced == 500 && Near(dispenser.RemainingMl, 0), "All 5000 ml produce exactly 500 normal ice pieces");
        dispenser.Tick(5);
        Check(Ice().Length == 0 && Near(dispenser.RemainingMl, 0), "Exhausted ice supply stays empty across a long subsequent tick");
        report.Add("INFO Sequential 500-piece throughput wall seconds=" + (Time.realtimeSinceStartup - started).ToString("R"));
    }

    void FullHotInventory()
    {
        Reset(); Select("Hot"); PickToWork(); dispenser.SetDispensing(true);
        float totalAccepted = 0;
        int batches = 0;
        // Keep GPU particle count bounded by flushing each accepted batch outside the
        // simulation region. This verifies 5000 ml stock and GPU-slot reuse, not cooling.
        var particleBuffer = (GraphicsBuffer)Field(gpu, "particleBuffer");
        while (dispenser.RemainingMl > .0001f && batches++ < 1000)
        {
            float before = gpu.EmittedMl;
            dispenser.Tick(.25f); gpu.Step(Dt); gpu.ReadbackNow();
            totalAccepted += gpu.EmittedMl - before;
            var particles = (GpuLiquidParticle[])gpu.Snapshot.Clone();
            Check(particles.Any(p => p.Active != 0) && particles.All(p => p.Active == 0 || (Finite(p.Position) && Finite(p.Velocity))),
                "Full hot stock batch " + batches + " materializes finite actual GPU particles");
            foreach (int index in Enumerable.Range(0, particles.Length).Where(i => particles[i].Active != 0))
            {
                particles[index].Position = particles[index].PreviousPosition = gpu.settings.gpuLiquidWorldMin - Vector2.one * 10;
            }
            particleBuffer.SetData(particles);
            gpu.Step(Dt); gpu.ReadbackNow();
        }
        Check(batches < 1000 && Near(dispenser.RemainingMl, 0) && Near(totalAccepted, 5000, .01f)
            && Near(gpu.EmittedMl, 5000, .01f), "Full 5000 ml hot stock is accepted exactly once while GPU allocation is repeatedly reused");
        dispenser.Tick(1);
        Check(Near(gpu.EmittedMl, 5000, .01f), "Empty hot supply cannot emit beyond its initial capacity");
        report.Add("INFO Full hot stock GPU recycling batches=" + batches);
    }

    void ResetAndModeScope()
    {
        Reset(); Select("Hot"); PickToWork(); dispenser.SetDispensing(true); Step();
        world.ResetSession(); DisableAutomaticSimulation();
        Check(dispenser.IsAvailable && dispenser.State.ToString() == "Docked" && dispenser.Mode.ToString() == "None"
            && Near(dispenser.RemainingMl, 5000) && !dispenser.IsDispensing && hand.HeldDispenser == null,
            "R-equivalent world reset restores off mode, stock, dock and hand state");
        Check(Ice().Length == 0, "Reset removes generated ice");
        foreach (FluidExperimentMode mode in Enum.GetValues(typeof(FluidExperimentMode)))
        {
            comparison.SwitchMode(mode); comparison.StartScenario(FluidExperimentScenario.Manual);
            DisableAutomaticSimulation(); dispenser = world.Dispenser;
            bool expected = mode == FluidExperimentMode.FCoherentLiquid || mode == FluidExperimentMode.GExplosiveBreakage;
            Check(dispenser != null && dispenser.IsAvailable == expected, mode + " enforces F/G Manual availability");
            if (!expected)
                Check(!hand.PickDispenser(dispenser, dispenser.Body.Position), mode + " cannot pick the hidden cohesive dispenser");
            else Check(dispenser.Mode.ToString() == "None" && Near(dispenser.RemainingMl, 5000), mode + " restores a fresh off supply");
        }
        Check(world.ExplosiveGlassBreakage && gpu.CohesivePhysicsActive, "G keeps explosive breakage and the cohesive liquid solver enabled");
        Reset(); Select("Hot"); PickToWork(); dispenser.SetDispensing(true);
        for (int i = 0; i < 25; i++) Step();
        dispenser.SetDispensing(false); gpu.ReadbackNow();
        Check(gpu.EmittedMl > 0 && Near(dispenser.RemainingMl + gpu.EmittedMl, 5000)
            && gpu.Snapshot.Any(particle => particle.Active != 0), "G accepts actual hot particles and conserves the shared supply");
        Retract(); Select("Ice"); PickToWork(); dispenser.SetDispensing(true);
        Step(dispenser.iceInterval); dispenser.SetDispensing(false);
        Check(Ice().Length == 1 && !Ice()[0].CanBePicked && Near(dispenser.RemainingMl + gpu.EmittedMl + 10, 5000),
            "G emits one native placed ice cube with the same stock and one-time-pickup rules");
        Capture("dispenser-g-mode.png", dispenser.Body.Position - Vector2.up, 2.6f);
        comparison.StartScenario(FluidExperimentScenario.Rest); DisableAutomaticSimulation();
        Check(!world.Dispenser.IsAvailable && hand.HeldDispenser == null, "Automatic comparison scenarios exclude the dispenser");
        comparison.StartScenario(FluidExperimentScenario.Manual); DisableAutomaticSimulation();
        Check(world.Dispenser.IsAvailable && world.Dispenser.Mode.ToString() == "None"
            && Near(world.Dispenser.RemainingMl, 5000), "Manual re-entry restores the reusable supply");
        comparison.SwitchMode(FluidExperimentMode.FCoherentLiquid); DisableAutomaticSimulation();
        Check(world.Dispenser.IsAvailable && !world.ExplosiveGlassBreakage && gpu.CohesivePhysicsActive,
            "Returning from G to F preserves natural breakage and the available manual supply");
    }

    void Capture(string name, Vector2 center, float halfHeight)
    {
        var camera = hand.inputCamera;
        var oldTarget = camera.targetTexture; var oldPosition = camera.transform.position;
        float oldSize = camera.orthographicSize; var oldActive = RenderTexture.active;
        var target = new RenderTexture(960, 720, 24);
        var pixels = new Texture2D(960, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.transform.position = new Vector3(center.x, center.y, oldPosition.z);
            camera.orthographicSize = halfHeight; Physics2D.SyncTransforms(); camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Evidence, name), pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; camera.transform.position = oldPosition; camera.orthographicSize = oldSize;
            RenderTexture.active = oldActive; target.Release(); Destroy(target); Destroy(pixels);
        }
    }
}
