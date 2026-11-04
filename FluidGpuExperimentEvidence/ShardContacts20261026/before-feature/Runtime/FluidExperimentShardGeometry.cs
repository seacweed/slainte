using System;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    /// <summary>Generated geometry only. Rebuilding artwork never replaces designer tuning.</summary>
    [CreateAssetMenu(menuName = "Slainte/Fluid Experiment/Shard Geometry")]
    public sealed class FluidExperimentShardGeometry : ScriptableObject
    {
        public FluidExperimentBreakageProfile.Shard[] definitions = Array.Empty<FluidExperimentBreakageProfile.Shard>();
        public string sourceDescription;
    }
}
