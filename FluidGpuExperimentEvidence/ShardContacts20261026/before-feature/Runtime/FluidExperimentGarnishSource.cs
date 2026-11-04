using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    // A fixed, unlimited click supply. Only dispensed pieces participate in world physics.
    [DisallowMultipleComponent]
    public sealed class FluidExperimentGarnishSource : MonoBehaviour
    {
        public string displayName;
        public FluidExperimentBody garnishPrefab;
        public PolygonCollider2D pickCollider;
        public bool Contains(Vector2 point)
        {
            if (!isActiveAndEnabled || pickCollider == null || !pickCollider.enabled) return false;
            Vector2 local = (Vector2)pickCollider.transform.InverseTransformPoint(point) - pickCollider.offset;
            for (int path = 0; path < pickCollider.pathCount; path++)
                if (FluidExperimentCollisionProfile.Contains(pickCollider.GetPath(path), local)) return true;
            return false;
        }
        public bool TryDispense(FluidExperimentInteractor interactor, Vector2 pointer)
        {
            var world = GetComponentInParent<FluidExperimentWorld>();
            if (!isActiveAndEnabled || world == null || interactor == null || interactor.world != world
                || interactor.Held != null || interactor.HeldPart != null || garnishPrefab == null
                || garnishPrefab.kind != LabItemKind.Garnish) return false;
            var piece = Instantiate(garnishPrefab, pointer, Quaternion.identity, world.transform);
            piece.Teleport(pointer, 0);
            if (interactor.Pick(piece, pointer)) return true;
            piece.gameObject.SetActive(false);
            Destroy(piece.gameObject);
            return false;
        }
    }
}
