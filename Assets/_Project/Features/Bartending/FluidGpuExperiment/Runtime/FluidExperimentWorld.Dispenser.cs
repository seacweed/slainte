using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentWorld
    {
        public FluidExperimentDispenser dispenserPrefab;
        public FluidExperimentDispenser Dispenser { get; private set; }

        private void InitializeDispenser()
        {
            if (dispenserPrefab == null || Dispenser != null) return;
            // Prefab docking coordinates belong to this world's authored frame. Resolve
            // them once per instance so independent runtime rigs do not share a world-space dock.
            Vector2 requestedDock = transform.TransformPoint(dispenserPrefab.dockPosition);
            Vector2 requestedAnchor = transform.TransformPoint(dispenserPrefab.hoseAnchor);
            Dispenser = Instantiate(dispenserPrefab, requestedDock, Quaternion.identity, transform);
            Dispenser.name = "Dispenser";
            // Retraction checks an inflated shape; leave more clearance than its .0125
            // per-side padding so a fitted dock is also reachable by that same sweep.
            Vector2 fittedDock = ConstrainHeldPosition(Dispenser.Body, requestedDock, 0, .04f);
            Dispenser.dockPosition = fittedDock;
            Vector2 anchor = requestedAnchor + fittedDock - requestedDock;
            const float hoseMargin = .04f;
            if (leftBoundary != null && leftBoundary.isActiveAndEnabled)
                anchor.x = Mathf.Max(anchor.x, leftBoundary.bounds.max.x + hoseMargin);
            if (rightBoundary != null && rightBoundary.isActiveAndEnabled)
                anchor.x = Mathf.Min(anchor.x, rightBoundary.bounds.min.x - hoseMargin);
            if (topBoundary != null && topBoundary.isActiveAndEnabled)
                anchor.y = Mathf.Min(anchor.y, topBoundary.bounds.min.y - hoseMargin);
            Dispenser.hoseAnchor = anchor;
            Dispenser.ResetDispenser();
        }
        public void SetDispenserActive(bool value)
        {
            if (Dispenser == null) return;
            bool active = value && liquid != null && liquid.CohesivePhysicsActive;
            if (!active && interactor != null && interactor.HeldDispenser == Dispenser)
                interactor.ReleaseWithVelocity(Vector2.zero);
            if (Dispenser.gameObject.activeSelf != active) Dispenser.gameObject.SetActive(active);
            if (active) Dispenser.ResetDispenser();
        }
    }
}
