using System;
using UnityEngine.Profiling;

namespace Slainte.Bartending.FluidGpuExperiment
{
    /// <summary>Opt-in timing hook. Benchmark workloads and exports live in the development client.</summary>
    public static class FluidExperimentPerformance
    {
        internal static readonly CustomSampler PhysicsSampler = CustomSampler.Create("FluidExperiment.Physics.Step");
        public static bool IsRecording { get; set; }
        public static event Action<long> PhysicsRecorded;

        internal static void RecordPhysics(long stopwatchTicks)
        {
            if (IsRecording) PhysicsRecorded?.Invoke(stopwatchTicks);
        }
    }
}
