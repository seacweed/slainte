using UnityEngine;
using UnityEngine.InputSystem;

namespace Slainte.Bartending
{
    public static class BartendingPointerAnchor
    {
        public const float DefaultScreenTolerance = 2f;

        private static Object cursorLockOwner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            cursorLockOwner = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public static Vector3 CalculateRootPosition(
            Vector3 currentRootPosition,
            Vector3 currentPivotPosition,
            Vector3 targetPivotPosition)
        {
            Vector3 target = currentRootPosition + targetPivotPosition - currentPivotPosition;
            target.z = 0f;
            return target;
        }

        public static bool TryWarpToWorld(
            Camera fallbackCamera,
            Vector3 worldPosition,
            out Vector2 screenPosition)
        {
            screenPosition = BartendingViewport.GetPointerScreenPosition(
                fallbackCamera,
                worldPosition);
            if (Mouse.current == null
                || !IsFinite(screenPosition.x)
                || !IsFinite(screenPosition.y))
            {
                return false;
            }

            Mouse.current.WarpCursorPosition(screenPosition);
            return true;
        }

        public static bool IsPointerAt(
            Vector2 targetScreenPosition,
            float tolerance = DefaultScreenTolerance)
        {
            return Mouse.current != null
                && Vector2.Distance(
                    Mouse.current.position.ReadValue(),
                    targetScreenPosition) <= Mathf.Max(0f, tolerance);
        }

        public static bool TryLock(Object owner)
        {
            if (owner == null
                || (cursorLockOwner != null && cursorLockOwner != owner))
            {
                return false;
            }

            cursorLockOwner = owner;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            return true;
        }

        public static bool TryGetHorizontalWorldDelta(
            Camera fallbackCamera,
            Bounds worldBounds,
            float sensitivity,
            float screenPadding,
            out float worldDeltaX)
        {
            float rawDeltaX = Mouse.current != null
                ? Mouse.current.delta.ReadValue().x
                : Input.GetAxisRaw("Mouse X");
            float requestedDeltaX = rawDeltaX * Mathf.Max(0f, sensitivity);
            return BartendingViewport.TryConvertClampedHorizontalScreenDelta(
                fallbackCamera,
                worldBounds,
                requestedDeltaX,
                screenPadding,
                out worldDeltaX);
        }

        public static bool UnlockAndWarp(
            Object owner,
            Camera fallbackCamera,
            Vector3 worldPosition,
            out Vector2 screenPosition)
        {
            screenPosition = default;
            if (owner == null
                || (cursorLockOwner != null && cursorLockOwner != owner))
            {
                return false;
            }

            cursorLockOwner = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return TryWarpToWorld(fallbackCamera, worldPosition, out screenPosition);
        }

        public static void Release(Object owner)
        {
            if (owner == null || cursorLockOwner != owner)
                return;

            cursorLockOwner = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
