using System.Runtime.InteropServices;
using UnityEngine;

namespace Slainte.Bartending
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuLiquidParticle
    {
        public Vector2 Position;
        public Vector2 PreviousPosition;
        public Vector2 Velocity;
        public float VolumeMl;
        public float TemperatureC;
        public uint VesselId;
        public uint TechniqueFlags;
        public uint StateFlags;
        public uint Active;
        public uint Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuLiquidSpawnCommand
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float VolumeMl;
        public float TemperatureC;
        public uint SourceIngredient;
        public uint VesselId;
        public uint StateFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuLiquidIngredientVisual
    {
        public Vector4 Color;
        public uint InheritMixedColor;
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuLiquidBoundarySegment
    {
        public Vector2 A;
        public Vector2 B;
        public Vector2 VelocityA;
        public Vector2 VelocityB;
        public uint VesselId;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuLiquidVesselTrigger
    {
        public Vector2 Center;
        public Vector2 AxisX;
        public Vector2 AxisY;
        public Vector2 HalfExtents;
        public uint VesselId;
        public int Priority;
        public uint Active;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuLiquidAgitator
    {
        public Vector2 A;
        public Vector2 B;
        public Vector2 Velocity;
        public float Radius;
        public float Strength;
        public uint VesselId;
        public uint Active;
    }

    public sealed class GpuLiquidVesselSnapshot
    {
        internal readonly float[] ComponentVolumesMl;

        private readonly float[] previousComponentVolumesMl;
        private int previousParticleCount = -1;
        private float previousTotalVolumeMl = -1f;

        internal GpuLiquidVesselSnapshot(int maximumIngredients)
        {
            ComponentVolumesMl = new float[maximumIngredients];
            previousComponentVolumesMl = new float[maximumIngredients];
        }

        public int ParticleCount { get; internal set; }
        public float TotalVolumeMl { get; internal set; }
        public float AverageTemperatureC { get; internal set; } = 20f;
        public float MeanCompositionDeviation { get; internal set; } = 1f;
        public float MaximumCompositionDeviation { get; internal set; } = 1f;
        public int OutOfToleranceParticleCount { get; internal set; }
        public float StirAttemptedVolumeMl { get; internal set; }
        public float StirredVolumeMl { get; internal set; }
        public float ShakenVolumeMl { get; internal set; }
        public float ShakenWithIceVolumeMl { get; internal set; }
        public int Version { get; private set; }
        public bool HasSurfaceSample { get; internal set; }
        public Vector2 SurfaceWorldPosition { get; internal set; }
        public float SurfaceTemperatureC { get; internal set; } = 20f;
        public Color SurfaceColor { get; internal set; } = Color.clear;

        internal float WeightedTemperature;
        internal float WeightedDeviation;
        internal float HighestParticleY;
        internal float SurfaceVolumeMl;
        internal float SurfaceWeightedTemperature;
        internal Vector2 SurfaceWeightedPosition;
        internal Color SurfaceWeightedColor;

        internal void BeginAccumulation()
        {
            ParticleCount = 0;
            TotalVolumeMl = 0f;
            AverageTemperatureC = 20f;
            MeanCompositionDeviation = 1f;
            MaximumCompositionDeviation = 0f;
            OutOfToleranceParticleCount = 0;
            StirAttemptedVolumeMl = 0f;
            StirredVolumeMl = 0f;
            ShakenVolumeMl = 0f;
            ShakenWithIceVolumeMl = 0f;
            HasSurfaceSample = false;
            SurfaceWorldPosition = Vector2.zero;
            SurfaceTemperatureC = 20f;
            SurfaceColor = Color.clear;
            WeightedTemperature = 0f;
            WeightedDeviation = 0f;
            HighestParticleY = float.NegativeInfinity;
            SurfaceVolumeMl = 0f;
            SurfaceWeightedTemperature = 0f;
            SurfaceWeightedPosition = Vector2.zero;
            SurfaceWeightedColor = Color.clear;
            System.Array.Clear(ComponentVolumesMl, 0, ComponentVolumesMl.Length);
        }

        internal void EndAccumulation(int ingredientCount)
        {
            AverageTemperatureC = TotalVolumeMl > 0.0001f
                ? WeightedTemperature / TotalVolumeMl
                : 20f;
            MeanCompositionDeviation = TotalVolumeMl > 0.0001f
                ? Mathf.Clamp01(WeightedDeviation / TotalVolumeMl)
                : 1f;
            if (ParticleCount <= 0)
                MaximumCompositionDeviation = 1f;

            if (SurfaceVolumeMl > 0.0001f)
            {
                HasSurfaceSample = true;
                SurfaceWorldPosition = SurfaceWeightedPosition / SurfaceVolumeMl;
                SurfaceTemperatureC = SurfaceWeightedTemperature / SurfaceVolumeMl;
                SurfaceColor = SurfaceWeightedColor / SurfaceVolumeMl;
            }

            bool changed = previousParticleCount != ParticleCount
                || !Mathf.Approximately(previousTotalVolumeMl, TotalVolumeMl);
            for (int i = 0; i < ingredientCount && !changed; i++)
            {
                changed = !Mathf.Approximately(
                    previousComponentVolumesMl[i],
                    ComponentVolumesMl[i]);
            }

            if (changed)
                Version++;

            previousParticleCount = ParticleCount;
            previousTotalVolumeMl = TotalVolumeMl;
            System.Array.Copy(
                ComponentVolumesMl,
                previousComponentVolumesMl,
                previousComponentVolumesMl.Length);
        }
    }
}
