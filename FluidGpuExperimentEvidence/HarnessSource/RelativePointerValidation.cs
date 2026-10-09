using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Slainte.Bartending.FluidGpuExperiment;

// Exercise the real input-state -> Interactor.Update path with virtual devices.
// This fixture must only run in the hidden batchmode harness. It never asks an
// operating-system pointer to move and never creates a visible test window.
public static class ExperimentRelativePointerChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(FluidExperimentWorld world, Action<bool, string> check)
    {
        check(Application.isBatchMode, "Relative input fixture requires hidden batchmode with native cursor operations disabled");
        var hand = world.interactor;
        var bottle = world.Items.First(x => x.kind == LabItemKind.Bottle && x.name == "Bottle_item_1003");
        var previousMouse = Mouse.current;
        var previousKeyboard = Keyboard.current;
        var previousMode = InputSystem.settings.updateMode;
        var previousBackground = InputSystem.settings.backgroundBehavior;
        var previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        bool previousRunInBackground = Application.runInBackground;
        CursorLockMode previousLock = Cursor.lockState;
        bool previousVisibility = Cursor.visible;
        Rect previousBlock = hand.pointerBlockRect;
        float previousReturnDuration = hand.uprightReturnDuration;
        Mouse mouse = null;
        Keyboard keyboard = null;
        try
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            // A hidden Editor has no focused Game view. Route only this isolated
            // harness's queued state into player buffers, without focusing a window.
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>("RelativePointerValidationMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("RelativePointerValidationKeyboard");
            mouse.MakeCurrent(); keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            var input = new VirtualInput(hand, mouse, keyboard);
            hand.pointerBlockRect = default;
            // Input frames are deliberately advanced synchronously. Give the separate
            // return animation enough time even when the launching Editor frame was slow.
            hand.uprightReturnDuration = Mathf.Max(1, Time.unscaledDeltaTime * 16);
            input.Frame(Vector2.zero, Vector2.zero, false);

            float slow = RunRotation(hand, bottle, input, Enumerable.Repeat(1f, 20).ToArray(), check, "twenty one-pixel frames");
            float fast = RunRotation(hand, bottle, input, new[] { 20f }, check, "one twenty-pixel frame");
            float fractional = RunRotation(hand, bottle, input, Enumerable.Repeat(.5f, 40).ToArray(), check, "forty half-pixel frames");
            Near(slow, 20 * .1f * hand.rotationSensitivity, .001f, check, "Slow input accumulates all physical mouse travel");
            Near(fast, slow, .001f, check, "Rotation is independent of event distribution across input frames");
            Near(fractional, slow, .001f, check, "Subpixel relative input remains continuous without a dead zone");
            float reversal = RunRotation(hand, bottle, input, new[] { 1f, 3f, -2f, -.5f, -1.5f }, check, "small direction reversals");
            Near(reversal, 0, .001f, check, "Opposite relative movement cancels exactly without threshold bias");

            Begin(hand, bottle, input, check);
            float before = bottle.TargetAngle;
            input.Queue(new Vector2(50, 80), Vector2.up, true);
            input.Queue(new Vector2(900, 20), Vector2.up, true);
            input.Queue(new Vector2(200, 700), Vector2.up, true);
            input.Flush();
            Near(bottle.TargetAngle - before, 3 * .1f * hand.rotationSensitivity, .001f, check,
                "Multiple queued physical events accumulate once in an input update despite unrelated absolute positions");
            before = bottle.TargetAngle;
            input.Frame(new Vector2(2000, -400), Vector2.zero, true);
            Near(bottle.TargetAngle, before, .001f, check, "Absolute cursor position changes alone cannot rotate a locked item");
            input.Frame(new Vector2(2000, -400), Vector2.up, true);
            before = bottle.TargetAngle;
            input.RepeatInteractorUpdate();
            Near(bottle.TargetAngle, before, .001f, check, "Repeated Update in the same input update does not apply mouse delta twice");

            foreach (Vector2 edge in new[] { new Vector2(100, 2), new Vector2(-100, 2), new Vector2(0, 100) })
            {
                Begin(hand, bottle, input, check, edge);
                input.Frame(new Vector2(40, 50), Vector2.up * 358, true);
                float constrainedAngle = bottle.TargetAngle;
                Vector2 constrainedPosition = bottle.TargetPosition;
                Vector2 constrainedPivot = hand.inputCamera.WorldToScreenPoint(hand.RotationPointerWorld);
                input.Frame(new Vector2(1700, 900), Vector2.zero, true);
                Near(bottle.TargetAngle, constrainedAngle, .001f, check, "Boundary-constrained pivot cannot become rotation input: " + edge);
                Near(bottle.TargetPosition, constrainedPosition, .002f, check, "Zero relative motion retains the constrained body pose: " + edge);
                Near(hand.LogicalPointerScreen, constrainedPivot, .002f, check, "Logical cursor follows the actual constrained pivot: " + edge);
            }

            // Transition/UI/reset cases are kept on the same actual Update path.
            ValidateTransitions(world, bottle, input, keyboard, check);
            check(Cursor.lockState == previousLock && Cursor.visible == previousVisibility,
                "Batchmode input tests leave native cursor lock and visibility untouched");
        }
        finally
        {
            hand.pointerBlockRect = previousBlock;
            hand.uprightReturnDuration = previousReturnDuration;
            hand.ReleaseWithVelocity(Vector2.zero);
            int index = 0;
            foreach (var item in world.Items)
            {
                item.SetHeld(false);
                item.Teleport(new Vector2(-40 - index++ * 4, 20), 0);
                item.Body.bodyType = RigidbodyType2D.Kinematic;
                item.pourMlPerSecond = 0;
            }
            world.Liquid.ResetSimulation();
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            InputSystem.settings.updateMode = previousMode;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            Application.runInBackground = previousRunInBackground;
        }
    }

    private static float RunRotation(FluidExperimentInteractor hand, FluidExperimentBody bottle, VirtualInput input,
        float[] motion, Action<bool, string> check, string label)
    {
        Begin(hand, bottle, input, check);
        float start = bottle.TargetAngle;
        foreach (float delta in motion)
        {
            float before = bottle.TargetAngle;
            input.Frame(new Vector2(1200 + delta * 30, 50 - delta * 12), new Vector2(0, delta), true);
            Near(bottle.TargetAngle - before, delta * .1f * hand.rotationSensitivity, .001f, check,
                label + ": individual motion remains proportional without snapping");
        }
        float result = bottle.TargetAngle - start;
        hand.ReleaseWithVelocity(Vector2.zero);
        input.Frame(Vector2.zero, Vector2.zero, false);
        return result;
    }

    private static void Begin(FluidExperimentInteractor hand, FluidExperimentBody bottle, VirtualInput input,
        Action<bool, string> check, Vector2? position = null)
    {
        hand.ReleaseWithVelocity(Vector2.zero);
        input.Frame(Vector2.zero, Vector2.zero, false);
        bottle.Teleport(position ?? new Vector2(0, 2), 0);
        check(hand.Pick(bottle, bottle.Position), "Pick fixture bottle for queued relative input");
        Vector2 screen = hand.inputCamera.WorldToScreenPoint(bottle.Position);
        float entryAngle = bottle.TargetAngle;
        input.Frame(screen, Vector2.up * 17, true);
        check(hand.Rotating && !hand.Returning && hand.PointerCaptured,
            "Queued right-button press enters manual rotation and logical capture; " + input.Diagnostic(screen));
        Near(bottle.TargetAngle, entryAngle, .001f, check, "Capture entry ignores only the snapshot containing pre-button movement");
        input.Frame(screen, Vector2.zero, true);
        // The disabled world does not run interpolation/render frames between these
        // synchronous inputs. Initialize its displayed pose to the authored physics
        // pose before testing hit locations; subsequent input remains unmodified.
        bottle.transform.SetPositionAndRotation(bottle.Position, Quaternion.Euler(0, 0, bottle.Angle));
        Physics2D.SyncTransforms();
    }

    private static void ValidateTransitions(FluidExperimentWorld world, FluidExperimentBody bottle, VirtualInput input,
        Keyboard keyboard, Action<bool, string> check)
    {
        var hand = world.interactor;
        var camera = hand.inputCamera;
        Begin(hand, bottle, input, check);
        input.Frame(new Vector2(40, 50), new Vector2(0, 80), true);
        float expectedReleaseAngle = bottle.TargetAngle + 2 * .1f * hand.rotationSensitivity;
        Vector2 beforeRelease = hand.RotationPointerWorld
            - bottle.PointAt(bottle.rotationPivotLocal, Vector2.zero, expectedReleaseAngle);
        Vector2 pivot = camera.WorldToScreenPoint(hand.RotationPointerWorld);
        input.Frame(new Vector2(40, 50), Vector2.up * 2, false);
        float capturedReleaseAngle = (float)typeof(FluidExperimentInteractor).GetField("returnStartAngle", Private).GetValue(hand);
        Near(capturedReleaseAngle, expectedReleaseAngle, .001f, check, "Right-button release consumes its final physical rotation delta exactly once");
        check(!hand.PointerCaptured && hand.PointerHandoffPending && hand.Returning,
            "Queued right-button release immediately unlocks logical capture and starts return pointer handoff");
        Near(hand.LogicalPointerScreen, pivot, .002f, check, "Return pointer starts at the last displayed pivot, not the locked absolute pointer");
        Near(bottle.Position, beforeRelease, .002f, check, "Beginning return cannot move the body from a center-to-pivot pointer change");

        Vector2 beforeMove = bottle.Position;
        Vector2 move = new Vector2(4, 3);
        input.Frame(pivot, move, false);
        check(hand.PointerHandoffPending,
            "A stale warp-target absolute coordinate cannot acknowledge handoff after real relative movement");
        Near(hand.LogicalPointerScreen, pivot + move, .002f, check, "Pending cursor handoff accepts real mouse movement immediately");
        Near(bottle.Position - beforeMove, WorldDelta(camera, pivot, move), .002f, check,
            "Mouse movement translates the held object during pointer handoff and upright return");

        Vector2 acknowledged = pivot + move;
        beforeMove = bottle.Position;
        input.Frame(acknowledged, Vector2.zero, false);
        check(!hand.PointerHandoffPending, "Matching native-coordinate input completes handoff without waiting extra frames");
        Near(bottle.Position, beforeMove, .002f, check,
            "Delayed absolute catch-up with zero new delta cannot apply the previous physical movement a second time");
        Vector2 ordinaryMove = new Vector2(-3, 2);
        beforeMove = bottle.Position;
        input.Frame(acknowledged + ordinaryMove, ordinaryMove, false);
        Near(bottle.Position - beforeMove, WorldDelta(camera, acknowledged, ordinaryMove), .002f, check,
            "Absolute pointer tracking resumes continuously on the next frame");

        Begin(hand, bottle, input, check);
        input.Frame(new Vector2(40, 50), Vector2.up * 40, true);
        pivot = camera.WorldToScreenPoint(hand.RotationPointerWorld);
        input.Frame(new Vector2(40, 50), Vector2.zero, false);
        beforeMove = bottle.Position;
        input.Frame(pivot, Vector2.right, false);
        check(hand.PointerHandoffPending,
            "A delayed warp-target position cannot acknowledge a one-pixel real movement");
        Near(bottle.Position - beforeMove, WorldDelta(camera, pivot, Vector2.right), .002f, check,
            "One-pixel physical handoff movement is applied without a dead zone");
        beforeMove = bottle.Position;
        input.Frame(pivot + Vector2.right, Vector2.zero, false);
        check(!hand.PointerHandoffPending, "Exact one-pixel absolute catch-up completes the handoff");
        Near(bottle.Position, beforeMove, .002f, check,
            "One-pixel absolute catch-up does not duplicate physical movement");

        Begin(hand, bottle, input, check);
        pivot = camera.WorldToScreenPoint(bottle.transform.TransformPoint(bottle.rotationPivotLocal));
        hand.pointerBlockRect = new Rect(pivot.x - 12, Screen.height - pivot.y - 12, 24, 24);
        input.Frame(new Vector2(40, 50), Vector2.zero, true, true);
        check(hand.Held == bottle && hand.PointerCaptured,
            "Rotation-time click checks the logical pivot against the screen overlay block");
        hand.pointerBlockRect = default;
        input.Frame(new Vector2(40, 50), Vector2.zero, true);

        EventSystem previousEventSystem = EventSystem.current;
        var ui = new GameObject("RelativePointerValidationUI");
        try
        {
            var events = ui.AddComponent<EventSystem>();
            EventSystem.current = events;
            var raycaster = ui.AddComponent<ExperimentRelativePointerRaycaster>();
            raycaster.hitRect = new Rect(pivot - Vector2.one * 12, Vector2.one * 24);
            input.Frame(new Vector2(40, 50), Vector2.zero, true, true);
            check(hand.Held == bottle && hand.PointerCaptured,
                "UI raycast at the software cursor prevents a rotation-time click from dropping the body");
            Near(raycaster.lastPointer, pivot, .002f, check, "EventSystem raycast receives the logical pivot instead of the locked native position");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ui);
            if (previousEventSystem != null) EventSystem.current = previousEventSystem;
        }
        input.Frame(new Vector2(40, 50), Vector2.zero, true);
        input.Frame(new Vector2(40, 50), Vector2.zero, true, true);
        check(hand.Held == null && !hand.PointerCaptured,
            "Unblocked left click while rotating drops the held body and releases pointer ownership");

        Begin(hand, bottle, input, check);
        var target = world.Items.First(x => x != bottle && x.kind == LabItemKind.Bottle);
        target.Teleport(bottle.Position, 0);
        target.transform.SetPositionAndRotation(target.Position, Quaternion.identity);
        Physics2D.SyncTransforms();
        Vector2 swapPoint = hand.RotationPointerWorld;
        // Awake/registration order may select different bottle silhouettes. Equal
        // root positions do not guarantee that one bottle's pivot lies in the other's
        // pick area. Explicitly place the target's pick center under the test click.
        target.Teleport(target.Position + swapPoint - (Vector2)target.pickCollider.bounds.center, 0);
        target.transform.SetPositionAndRotation(target.Position, Quaternion.identity);
        Physics2D.SyncTransforms();
        var swapCandidate = world.FindSwapTarget(bottle, swapPoint);
        check(swapCandidate == target, "Swap fixture has an overlapping solid and a target pick area at the rotation pivot");
        string swapDiagnostic = $"point={swapPoint:F6}, bottle={bottle.name}/{bottle.Position:F6}, target={target.name}/{target.Position:F6}, contains={target.Contains(swapPoint)}, candidate={swapCandidate?.name}";
        input.Frame(new Vector2(40, 50), Vector2.zero, true, true);
        check(hand.Held == target && !hand.PointerCaptured,
            "Rotation-time left click swaps using the software pointer's world position; " + swapDiagnostic + $", held={hand.Held?.name}, captured={hand.PointerCaptured}");
        hand.ReleaseWithVelocity(Vector2.zero);
        target.Teleport(new Vector2(-35, 20), 0);

        Begin(hand, bottle, input, check);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
        input.Frame(new Vector2(40, 50), Vector2.zero, true);
        check(!hand.PointerCaptured && !hand.PointerHandoffPending,
            "Queued Escape clears logical capture and any pending pointer handoff");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        input.Frame(new Vector2(40, 50), Vector2.zero, false);

        Begin(hand, bottle, input, check);
        typeof(FluidExperimentInteractor).GetMethod("OnApplicationFocus", Private).Invoke(hand, new object[] { false });
        check(!hand.PointerCaptured && !hand.PointerHandoffPending,
            "Focus loss cancels capture and handoff without a warp into another application");
        typeof(FluidExperimentInteractor).GetMethod("OnApplicationFocus", Private).Invoke(hand, new object[] { true });
        input.Frame(new Vector2(40, 50), Vector2.up * 30, true);
        check(!hand.PointerCaptured, "Returning focus while RMB remains held does not recapture the pointer");

        Begin(hand, bottle, input, check);
        typeof(FluidExperimentInteractor).GetMethod("OnDisable", Private).Invoke(hand, null);
        check(hand.Held == null && !hand.PointerCaptured && !hand.PointerHandoffPending,
            "Interactor disable releases the item and all pointer state");

        Begin(hand, bottle, input, check);
        bottle.gameObject.SetActive(false);
        check(hand.Held == null && !hand.PointerCaptured && !hand.PointerHandoffPending,
            "Disabling the held owner clears rotation pointer ownership");
        bottle.gameObject.SetActive(true);

        Begin(hand, bottle, input, check);
        world.ResetSession();
        check(hand.Held == null && !hand.PointerCaptured && !hand.PointerHandoffPending,
            "Session reset leaves no captured or transitioning cursor");
    }

    private static Vector2 WorldDelta(Camera camera, Vector2 origin, Vector2 delta)
        => (Vector2)camera.ScreenToWorldPoint(origin + delta) - (Vector2)camera.ScreenToWorldPoint(origin);

    private static void Near(float actual, float expected, float tolerance, Action<bool, string> check, string label)
        => check(Mathf.Abs(actual - expected) <= tolerance, label + $" (actual={actual:F6}, expected={expected:F6})");

    private static void Near(Vector2 actual, Vector2 expected, float tolerance, Action<bool, string> check, string label)
        => check(Vector2.Distance(actual, expected) <= tolerance, label + $" (actual={actual:F6}, expected={expected:F6})");

    private sealed class VirtualInput
    {
        private readonly FluidExperimentInteractor hand;
        private readonly Mouse mouse;
        private readonly Keyboard keyboard;
        private readonly MethodInfo update = typeof(FluidExperimentInteractor).GetMethod("Update", Private);
        private readonly MethodInfo inputUpdate = typeof(InputSystem).GetMethod("Update",
            BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(InputUpdateType) }, null);
        public VirtualInput(FluidExperimentInteractor hand, Mouse mouse, Keyboard keyboard)
        { this.hand = hand; this.mouse = mouse; this.keyboard = keyboard; }
        public void Queue(Vector2 position, Vector2 delta, bool right, bool left = false)
        {
            MouseState state = new MouseState { position = position, delta = delta }
                .WithButton(MouseButton.Right, right).WithButton(MouseButton.Left, left);
            InputSystem.QueueStateEvent(mouse, state);
        }
        public void Flush()
        {
            // Parameterless Update chooses Editor buffers whenever the Game view
            // lacks focus. Explicit Manual updates exercise real player button edges.
            inputUpdate.Invoke(null, new object[] { InputUpdateType.Manual });
            mouse.MakeCurrent(); keyboard.MakeCurrent();
            update.Invoke(hand, null);
        }
        public string Diagnostic(Vector2 screen)
        {
            object lastRead = typeof(FluidExperimentInteractor).GetField("pointerInputRead", Private).GetValue(hand);
            object lastUpdate = typeof(FluidExperimentInteractor).GetField("pointerInputUpdate", Private).GetValue(hand);
            object blocked = typeof(FluidExperimentInteractor).GetMethod("IsPointerBlocked", Private)
                .Invoke(hand, new object[] { screen });
            return $"current={Mouse.current == mouse}, enabled={mouse.enabled}, pressed={mouse.rightButton.isPressed}, "
                + $"edge={mouse.rightButton.wasPressedThisFrame}, update={InputState.updateCount}/{InputState.currentUpdateType}, "
                + $"last={lastUpdate}, read={lastRead}, blocked={blocked}, rotating={hand.Rotating}, captured={hand.PointerCaptured}";
        }
        public void RepeatInteractorUpdate() => update.Invoke(hand, null);
        public void Frame(Vector2 position, Vector2 delta, bool right, bool left = false)
        {
            Queue(position, delta, right, left);
            Flush();
        }
    }
}

public sealed class ExperimentRelativePointerRaycaster : BaseRaycaster
{
    public Rect hitRect;
    public Vector2 lastPointer;
    public override Camera eventCamera => null;
    public override void Raycast(PointerEventData eventData, List<RaycastResult> results)
    {
        lastPointer = eventData.position;
        if (hitRect.Contains(eventData.position))
            results.Add(new RaycastResult { gameObject = gameObject, module = this, distance = 0,
                screenPosition = eventData.position, index = results.Count });
    }
}
