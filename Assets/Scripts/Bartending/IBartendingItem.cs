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
}
