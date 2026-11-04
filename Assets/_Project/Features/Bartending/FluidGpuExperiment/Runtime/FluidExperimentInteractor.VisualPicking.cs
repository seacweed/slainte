using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentInteractor
    {
        private bool PickVisualTarget(Vector2 point)
        {
            world.UpdateVisualSorting();
            Component selected = null;
            long selectedKey = long.MinValue;
            int selectedPriority = -1;
            float selectedDistance = float.MaxValue;
            void Consider(Component candidate, int priority, Vector2 origin)
            {
                long key = world.VisualSortKey(candidate);
                float distance = (origin - point).sqrMagnitude;
                if (selected == null || key > selectedKey || (key == selectedKey && (priority > selectedPriority
                    || (priority == selectedPriority && distance < selectedDistance))))
                { selected = candidate; selectedKey = key; selectedPriority = priority; selectedDistance = distance; }
            }
            foreach (var body in world.Items)
            {
                if (body == null) continue;
                if (body.CanBePicked && body.Contains(point)) Consider(body, 0, body.Position);
                if (body.kind != LabItemKind.Shaker || body.IsHeld) continue;
                foreach (var part in new[] { body.ShakerCap, body.ShakerStrainer })
                    if (part != null && part.ContainsWorldPoint(point)) Consider(part, 2, part.Position);
            }
            foreach (var source in world.GetComponentsInChildren<FluidExperimentGarnishSource>())
                if (source.Contains(point)) Consider(source, 1, source.transform.position);
            var dispenser = world.Dispenser;
            if (dispenser != null && dispenser.IsAvailable)
            {
                Vector2 local = dispenser.Body.WorldToLocal(point);
                if (dispenser.Contains(point) || dispenser.iceButtonLocal.Contains(local) || dispenser.hotButtonLocal.Contains(local))
                    Consider(dispenser.Body, 1, dispenser.Body.Position);
            }
            if (selected == null) return false;
            if (dispenser != null && selected == dispenser.Body) return TryDispenserClick(point, out _);
            if (selected is FluidExperimentShakerPart selectedPart) return PickPart(selectedPart, point);
            if (selected is FluidExperimentGarnishSource selectedSource) return selectedSource.TryDispense(this, point);
            return Pick(selected as FluidExperimentBody, point);
        }
    }
}
