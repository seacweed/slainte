using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    public interface IBartendingItem
    {
        GameObject GameObject { get; }
        bool IsPickedUp { get; }
        void SnapToSlot(Transform slotTransform, SlotController slot);
        void OnPickedUp();
        void OnDropped();
    }

    public interface IPointerAnchoredPickup
    {
        void OnPickedUpAt(Vector3 pointerWorld);
    }

    public interface IBartendingCabinetVisualProvider
    {
        Sprite GetCabinetVisualSprite(int layerIndex, Sprite fallback);
    }

    public interface IBartendingViewTransitionParticipant
    {
        void SuspendForViewTransition();
        void UpdateForViewTransition(Vector3 pointerWorld);
        void ResumeAfterViewTransition();
    }

    /// <summary>
    /// Defines interaction order and a tiny visual Z tie-break between items.
    /// It deliberately does not change renderer sorting orders, liquid presentation,
    /// or any 2D physics property.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BartendingItemOrder : MonoBehaviour
    {
        private const float DisplayDepthStep = 0.0005f;
        private static readonly HashSet<BartendingItemOrder> activeItems = new();
        private static int nextRank;

        private Collider2D pointerCollider;
        private Func<Vector2, bool> pointerContains;
        private VesselLiquidTracker liquidTracker;
        private bool initialized;
        private float originalZ;

        public int Rank { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            activeItems.Clear();
            nextRank = 0;
        }

        public static BartendingItemOrder Attach(
            GameObject item,
            Collider2D clickCollider,
            VesselLiquidTracker tracker = null,
            Func<Vector2, bool> contains = null)
        {
            if (item == null)
                return null;

            BartendingItemOrder order = item.GetComponent<BartendingItemOrder>();
            if (order == null)
                order = item.AddComponent<BartendingItemOrder>();

            order.Initialize(clickCollider, tracker, contains);
            return order;
        }

        public void BringToFront()
        {
            if (!initialized)
                return;

            Rank = ++nextRank;
            liquidTracker?.SetInteractionPriority(Rank);
            ApplyDisplayDepth();
        }

        public bool IsFrontmostAt(Vector2 worldPoint)
        {
            if (!Contains(worldPoint))
                return false;

            foreach (BartendingItemOrder other in activeItems)
            {
                if (other == null || other == this || !other.Contains(worldPoint))
                    continue;

                if (other.Rank > Rank
                    || (other.Rank == Rank && other.GetInstanceID() > GetInstanceID()))
                {
                    return false;
                }
            }

            return true;
        }

        private void Initialize(
            Collider2D clickCollider,
            VesselLiquidTracker tracker,
            Func<Vector2, bool> contains)
        {
            pointerCollider = clickCollider;
            pointerContains = contains;
            liquidTracker = tracker;
            if (!initialized)
            {
                originalZ = transform.position.z;
                initialized = true;
                activeItems.Add(this);
                BringToFront();
                return;
            }

            liquidTracker?.SetInteractionPriority(Rank);
            ApplyDisplayDepth();
        }

        private void OnEnable()
        {
            if (initialized)
            {
                activeItems.Add(this);
                ApplyDisplayDepth();
            }
        }

        private void OnDisable()
        {
            activeItems.Remove(this);
        }

        private void LateUpdate()
        {
            if (initialized)
                ApplyDisplayDepth();
        }

        private void ApplyDisplayDepth()
        {
            Vector3 position = transform.position;
            position.z = originalZ - Mathf.Min(Rank, 10000) * DisplayDepthStep;
            transform.position = position;
        }

        private bool Contains(Vector2 worldPoint)
        {
            if (!isActiveAndEnabled)
                return false;
            if (pointerContains != null)
                return pointerContains(worldPoint);
            return pointerCollider != null
                && pointerCollider.enabled
                && pointerCollider.gameObject.activeInHierarchy
                && pointerCollider.OverlapPoint(worldPoint);
        }
    }
}
