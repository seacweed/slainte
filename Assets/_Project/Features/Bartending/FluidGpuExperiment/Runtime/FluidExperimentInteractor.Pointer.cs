using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentInteractor
    {
        public Vector2 LogicalPointerScreen { get; private set; }
        public bool PointerCaptured { get; private set; }
        public bool PointerHandoffPending { get; private set; }
        public Vector2 DisplayedPointerScreen => PointerCaptured && Held != null && inputCamera != null
            ? (Vector2)inputCamera.WorldToScreenPoint(Held.transform.TransformPoint(Held.rotationPivotLocal))
            : LogicalPointerScreen;
        private Mouse capturedMouse;
        private uint pointerInputUpdate;
        private bool pointerInputRead, nativeCursorOwned, rebasePointerOnResume, requirePointerRelease;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private Vector2 pointerWarpTarget;
        private float pointerHandoffStarted;
        private Vector2 previousHandoffAbsolute;
        private int stationaryHandoffUpdates;
        private Texture2D pointerTexture;
        private readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
        private PointerEventData pointerEvent;
        private EventSystem pointerEventSystem;

        private bool ReadPointerInput(Mouse mouse, out Vector2 screen, out Vector2 delta)
        {
            bool fresh = !pointerInputRead || pointerInputUpdate != InputState.updateCount;
            pointerInputRead = true;
            pointerInputUpdate = InputState.updateCount;
            delta = fresh ? mouse.delta.ReadValue() : Vector2.zero;
            Vector2 absolute = mouse.position.ReadValue();
            if (rebasePointerOnResume)
            {
                RebaseFollowPointer(absolute);
                rebasePointerOnResume = false;
            }
            if (PointerHandoffPending && fresh)
            {
                // Windows relative RAWINPUT is independent of the asynchronous position warp.
                // Continue the drag even if that update still contains the old locked position.
                LogicalPointerScreen += delta;
                LogicalPointerScreen = new Vector2(Mathf.Clamp(LogicalPointerScreen.x, 0, Screen.width),
                    Mathf.Clamp(LogicalPointerScreen.y, 0, Screen.height));
                // A broad pixel tolerance would acknowledge a stale position during slow
                // movement and replay that same movement when absolute input catches up.
                bool arrived = (absolute - LogicalPointerScreen).sqrMagnitude < .0001f;
                stationaryHandoffUpdates = delta.sqrMagnitude < .0001f
                    && (absolute - previousHandoffAbsolute).sqrMagnitude < .0001f
                    ? stationaryHandoffUpdates + 1 : 0;
                previousHandoffAbsolute = absolute;
                // OS acceleration/scaling can differ from RAWINPUT. Reconcile such offsets
                // only after motion settles, never while late absolute events could count it twice.
                bool settled = Time.unscaledTime - pointerHandoffStarted >= .25f && stationaryHandoffUpdates >= 2;
                if (arrived || settled)
                {
                    // Apply real motion before changing coordinate systems. The correction is
                    // a grab/return-baseline change, never body motion or sampled throw velocity.
                    MoveHeld(inputCamera.ScreenToWorldPoint(LogicalPointerScreen));
                    RebaseFollowPointer(absolute);
                    CompletePointerHandoff();
                }
            }
            if (!PointerCaptured && !PointerHandoffPending) LogicalPointerScreen = absolute;
            screen = LogicalPointerScreen;
            return fresh;
        }

        private void RebaseFollowPointer(Vector2 nextScreen)
        {
            Vector2 previous = inputCamera.ScreenToWorldPoint(LogicalPointerScreen);
            Vector2 next = inputCamera.ScreenToWorldPoint(nextScreen);
            if (Returning) returnStartPointer += next - previous;
            else if (Held != null && !Rotating)
            {
                Held.ApplyHeldPose();
                grabLocal = Held.WorldToLocal(next);
            }
            else if (HeldPart != null) HeldPart.RebasePointer(next);
            LogicalPointerScreen = nextScreen;
            lastPointer = next;
        }

        private bool CanCapturePointer() => !requirePointerRelease && (Application.isBatchMode
            || (Application.isFocused && Cursor.lockState == CursorLockMode.None));

        private void CapturePointer(Mouse mouse)
        {
            CompletePointerHandoff();
            capturedMouse = mouse;
            PointerCaptured = true;
            LogicalPointerScreen = inputCamera.WorldToScreenPoint(RotationPointerWorld);
            if (Application.isBatchMode) return;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            nativeCursorOwned = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void BeginPointerHandoff(Vector2 worldPointer)
        {
            if (!PointerCaptured) return; // Direct simulation APIs do not manipulate the OS cursor.
            PointerCaptured = false;
            PointerHandoffPending = true;
            LogicalPointerScreen = pointerWarpTarget = inputCamera.WorldToScreenPoint(worldPointer);
            pointerHandoffStarted = Time.unscaledTime;
            stationaryHandoffUpdates = 0;
            previousHandoffAbsolute = capturedMouse != null ? capturedMouse.position.ReadValue() : pointerWarpTarget;
            if (!nativeCursorOwned) return;
            Cursor.lockState = previousCursorLock;
            // Keep displaying the freely moving software pointer until position catches up.
            // This is the only warp in a normal capture session, after manual rotation has ended.
            if (Application.isFocused && capturedMouse != null)
                capturedMouse.WarpCursorPosition(pointerWarpTarget);
        }

        private bool PointerCaptureLost(Mouse mouse)
        {
            if (!PointerCaptured && !PointerHandoffPending) return false;
            return mouse != capturedMouse
                || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (nativeCursorOwned && (!Application.isFocused
                    || (PointerCaptured && Cursor.lockState != CursorLockMode.Locked)));
        }

        private void CancelPointerCapture()
        {
            requirePointerRelease |= PointerCaptured || PointerHandoffPending || Rotating;
            bool rotating = Rotating;
            Vector2 pivot = rotating ? RotationPointerWorld : lastPointer;
            ReleasePointer(); // Focus loss/Escape never warps into another application.
            if (rotating) EndRotation(pivot);
            if (inputCamera != null) LogicalPointerScreen = inputCamera.WorldToScreenPoint(pivot);
            rebasePointerOnResume = true;
        }

        private void CompletePointerHandoff()
        {
            PointerHandoffPending = false;
            if (!nativeCursorOwned) return;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            nativeCursorOwned = false;
        }

        private void ReleasePointer()
        {
            PointerCaptured = false;
            CompletePointerHandoff();
            capturedMouse = null;
        }

        private bool IsPointerBlocked(Vector2 screen)
        {
            if (pointerBlockRect.Contains(new Vector2(screen.x, Screen.height - screen.y))) return true;
            EventSystem system = EventSystem.current;
            if (system == null) return false;
            if (pointerEvent == null || pointerEventSystem != system)
            {
                pointerEventSystem = system;
                pointerEvent = new PointerEventData(system);
            }
            pointerEvent.Reset();
            pointerEvent.position = screen;
            pointerHits.Clear();
            system.RaycastAll(pointerEvent, pointerHits);
            return pointerHits.Count > 0;
        }

        private void OnGUI()
        {
            if ((!PointerCaptured && !PointerHandoffPending) || inputCamera == null
                || (!Application.isBatchMode && !Application.isFocused)) return;
            if (pointerTexture == null) CreatePointerTexture();
            Vector2 screen = DisplayedPointerScreen;
            int depth = GUI.depth;
            Color color = GUI.color;
            GUI.depth = -10000;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(screen.x, Screen.height - screen.y, pointerTexture.width, pointerTexture.height), pointerTexture);
            GUI.color = color;
            GUI.depth = depth;
        }

        private void CreatePointerTexture()
        {
            // Software arrow hotspot is its top-left tip, exactly at the rendered pivot.
            string[] rows = { "#", "##", "#.#", "#..#", "#...#", "#....#", "#.....#", "#......#",
                "#.......#", "#........#", "#.........#", "#..........#", "#......#####", "#...#..#",
                "#..##..#", "#.#  #..#", "##   #..#", "#     #..#", "      #..#", "       ##" };
            pointerTexture = new Texture2D(12, rows.Length, TextureFormat.RGBA32, false)
                { name = "Fluid rotation pointer", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            Color32[] pixels = new Color32[12 * rows.Length];
            for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                pixels[(rows.Length - 1 - y) * 12 + x] = rows[y][x] == '#'
                    ? new Color32(0, 0, 0, 255) : rows[y][x] == '.' ? new Color32(255, 255, 255, 255) : new Color32();
            pointerTexture.SetPixels32(pixels);
            pointerTexture.Apply(false, true);
        }

        private void OnDestroy()
        {
            ReleasePointer();
            if (pointerTexture != null) Destroy(pointerTexture);
        }
    }
}
