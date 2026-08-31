using UnityEngine;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    public sealed class BartendingSelectionCoordinator : MonoBehaviour
    {
        private Object currentOwner;
        private Object lastReleasedOwner;
        private int lastReleasedFrame = -1;

        public bool HasSelection
        {
            get
            {
                ClearDestroyedOwner();
                return currentOwner != null;
            }
        }

        public bool CanAcquire(Object candidate)
        {
            if (candidate == null)
                return false;

            ClearDestroyedOwner();
            if (currentOwner != null)
                return currentOwner == candidate;

            return lastReleasedFrame != Time.frameCount
                || lastReleasedOwner == candidate;
        }

        public bool TryAcquire(Object candidate)
        {
            if (!CanAcquire(candidate))
                return false;

            currentOwner = candidate;
            return true;
        }

        public void Release(Object candidate)
        {
            ClearDestroyedOwner();
            if (candidate == null || currentOwner != candidate)
                return;

            currentOwner = null;
            lastReleasedOwner = candidate;
            lastReleasedFrame = Time.frameCount;
        }

        private void ClearDestroyedOwner()
        {
            if (!ReferenceEquals(currentOwner, null) && currentOwner == null)
            {
                currentOwner = null;
                lastReleasedOwner = null;
                lastReleasedFrame = Time.frameCount;
            }
        }
    }

    public static class BartendingSelection
    {
        public static bool CanAcquire(Component candidate)
        {
            BartendingSelectionCoordinator coordinator = FindCoordinator(candidate);
            return coordinator == null || coordinator.CanAcquire(candidate);
        }

        public static bool TryAcquire(Component candidate)
        {
            BartendingSelectionCoordinator coordinator = FindCoordinator(candidate);
            return coordinator == null || coordinator.TryAcquire(candidate);
        }

        public static void Release(Component candidate)
        {
            FindCoordinator(candidate)?.Release(candidate);
        }

        private static BartendingSelectionCoordinator FindCoordinator(Component candidate)
        {
            return candidate != null
                ? candidate.GetComponentInParent<BartendingSelectionCoordinator>(true)
                : null;
        }
    }
}
