using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentBody
    {
        [Header("Recipe identity")]
        [Tooltip("Stable recipe glass ID, such as rock, highball, hurricane or martini. Metadata only; distinct from the world-assigned instance Id.")]
        public string servingGlassId = string.Empty;
    }
}
