using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentInteractor
    {
        public FluidExperimentDispenser HeldDispenser { get; private set; }

        private bool TryDispenserClick(Vector2 point, out bool hit)
        {
            hit = false;
            FluidExperimentDispenser dispenser = world != null ? world.Dispenser : null;
            if (dispenser == null || !dispenser.IsAvailable) return false;
            // Button clicks consume the click, including while retracting. They must
            // never also pick up the body or a different item behind the dispenser.
            if (dispenser.TryClickMode(point)) { hit = true; return true; }
            if (!dispenser.Contains(point)) return false;
            hit = true;
            return PickDispenser(dispenser, point);
        }

        public bool PickDispenser(FluidExperimentDispenser dispenser, Vector2 point)
        {
            if (Held != null || HeldPart != null || HeldDispenser != null || world == null
                || dispenser == null || dispenser != world.Dispenser || !dispenser.IsAvailable
                || !dispenser.TryPick(point)) return false;
            // A dispenser owns its collision-aware motion. Never put its body into
            // the ordinary held-body rotation, teleport or throw path.
            HeldDispenser = dispenser;
            Rotating = Returning = false;
            lastPointer = point;
            sampledUserAngle = 0;
            samples.Clear();
            dispenser.SetDispensing(false);
            return true;
        }

        private void UpdateHeldDispenser(Vector2 pointer, bool dispensing)
        {
            FluidExperimentDispenser dispenser = HeldDispenser;
            if (dispenser == null) return;
            if (!dispenser.IsAvailable || dispenser.State != FluidExperimentDispenserState.Held)
            {
                RetractHeldDispenser();
                return;
            }
            // RMB controls emission only. Pointer capture and rotation remain exclusive
            // to ordinary bodies, so translation continues for the entire emission.
            MoveHeld(pointer);
            dispenser.SetDispensing(dispensing);
        }

        private bool RetractHeldDispenser()
        {
            FluidExperimentDispenser dispenser = HeldDispenser;
            if (dispenser == null) return false;
            HeldDispenser = null;
            dispenser.SetDispensing(false);
            dispenser.BeginRetract();
            samples.Clear();
            return true;
        }
    }
}
