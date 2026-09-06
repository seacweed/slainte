using UnityEngine;

namespace Slainte.Bartending
{
    public enum LiquidSimulationBackendMode
    {
        LegacyRigidbody2D = 0,
        GpuPbfXpbd = 1,
        Automatic = 2
    }

    public interface ILiquidSimulationBackend
    {
        bool IsOperational { get; }
        bool IsGpuBackend { get; }
        float DefaultParticleVolumeMl { get; }
        int ActiveParticleCount { get; }

        bool TryEmit(
            Vector2 worldPosition,
            Vector2 initialVelocity,
            ItemDef sourceItem,
            float volumeMl);

        void ResetSimulation();
    }

    public static class LiquidSimulationRuntime
    {
        public static ILiquidSimulationBackend ActiveBackend
        {
            get
            {
                if (GpuLiquidSystem.Instance != null
                    && GpuLiquidSystem.Instance.IsOperational)
                {
                    return GpuLiquidSystem.Instance;
                }

                return LiquidPool.Instance;
            }
        }

        public static bool IsGpuActive => ActiveBackend?.IsGpuBackend == true;

        public static bool TryEmit(
            Vector2 worldPosition,
            Vector2 initialVelocity,
            ItemDef sourceItem,
            float volumeMl)
        {
            ILiquidSimulationBackend backend = ActiveBackend;
            return backend != null
                && backend.IsOperational
                && backend.TryEmit(
                    worldPosition,
                    initialVelocity,
                    sourceItem,
                    volumeMl);
        }
    }
}
