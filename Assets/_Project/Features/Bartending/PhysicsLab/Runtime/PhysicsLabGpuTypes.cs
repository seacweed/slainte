using System.Runtime.InteropServices;
using UnityEngine;

namespace Slainte.Bartending.PhysicsLab
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuLiquidParticle
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
        public Vector2 LocalA;
        public Vector2 LocalB;
        public Vector2 StartPosition;
        public Vector2 EndPosition;
        public float StartAngle;
        public float AngleDelta;
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

}
