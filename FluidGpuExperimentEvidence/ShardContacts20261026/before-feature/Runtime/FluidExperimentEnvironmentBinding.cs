using System;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    /// <summary>Scene-local environment references; no name lookup or project layer mutation.</summary>
    public sealed class FluidExperimentEnvironmentBinding : MonoBehaviour
    {
        public BoxCollider2D leftBoundary, rightBoundary, topBoundary;
        public BoxCollider2D table;
        public Collider2D[] solidFloors = Array.Empty<Collider2D>();
        [Tooltip("When disabled, liquid drains through the table while solid objects still collide.")]
        public bool tableHoldsLiquid = true;

        public void BindTo(FluidExperimentWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            world.leftBoundary = leftBoundary; world.rightBoundary = rightBoundary; world.topBoundary = topBoundary;
            world.floorColliders = solidFloors;
            world.liquidSurfaces = tableHoldsLiquid && table != null ? new[] { table } : Array.Empty<BoxCollider2D>();
            world.RebuildEnvironment();
        }
    }
}
