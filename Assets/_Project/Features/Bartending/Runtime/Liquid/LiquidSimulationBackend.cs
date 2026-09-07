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

    // The legacy pool remains unchanged and is adapted at the integration boundary.
    // In particular, legacy emission still goes through LiquidPool.GetParticle(),
    // which preserves its original zero-velocity spawn behavior.
    public sealed class LegacyLiquidSimulationBackend : ILiquidSimulationBackend
    {
        private readonly LiquidPool pool;

        public LegacyLiquidSimulationBackend(LiquidPool liquidPool)
        {
            pool = liquidPool;
        }

        public LiquidPool Pool => pool;
        public bool IsOperational => pool != null && pool.particlePrefab != null;
        public bool IsGpuBackend => false;
        public float DefaultParticleVolumeMl => pool != null
            ? pool.DefaultParticleVolumeMl
            : 1f;
        public int ActiveParticleCount => pool != null ? pool.ActiveParticleCount : 0;

        public bool TryEmit(
            Vector2 worldPosition,
            Vector2 initialVelocity,
            ItemDef sourceItem,
            float volumeMl)
        {
            if (pool == null)
                return false;

            return pool.GetParticle(worldPosition, sourceItem, volumeMl) != null;
        }

        public void ResetSimulation()
        {
            if (pool == null)
                return;

            LiquidParticleData[] activeParticles = Object.FindObjectsByType<LiquidParticleData>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < activeParticles.Length; i++)
                pool.ReturnParticle(activeParticles[i].gameObject);
        }
    }

    public static class LiquidSimulationRuntime
    {
        private static LiquidPool cachedLegacyPool;
        private static LegacyLiquidSimulationBackend cachedLegacyBackend;

        public static ILiquidSimulationBackend ActiveBackend
        {
            get
            {
                if (GpuLiquidSystem.Instance != null
                    && GpuLiquidSystem.Instance.IsOperational)
                {
                    return GpuLiquidSystem.Instance;
                }

                return GetLegacyBackend(LiquidPool.Instance);
            }
        }

        public static bool IsGpuActive => ActiveBackend?.IsGpuBackend == true;

        public static LegacyLiquidSimulationBackend GetLegacyBackend(LiquidPool pool)
        {
            if (pool == null)
            {
                cachedLegacyPool = null;
                cachedLegacyBackend = null;
                return null;
            }

            if (cachedLegacyPool != pool || cachedLegacyBackend == null)
            {
                cachedLegacyPool = pool;
                cachedLegacyBackend = new LegacyLiquidSimulationBackend(pool);
            }

            return cachedLegacyBackend;
        }

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
