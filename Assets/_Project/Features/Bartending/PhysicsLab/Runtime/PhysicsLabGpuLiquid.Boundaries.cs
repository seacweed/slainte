using System.Runtime.InteropServices;
using UnityEngine;

namespace Slainte.Bartending.PhysicsLab
{
    // One contiguous range per body. IDs are keys, never buffer offsets: unregistering
    // and re-registering bodies must not invalidate ownership lookups.
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuLiquidBoundaryGroup
    {
        public Vector2 Min, Max, SweptMin, SweptMax;
        public uint First, End, ContourFirst, ContourEnd, VesselId;
    }

    public sealed partial class PhysicsLabGpuLiquid
    {
        private GraphicsBuffer boundaryGroupBuffer;
        private GpuLiquidBoundaryGroup[] boundaryGroups;

        private void UploadBoundaryGroup(int groupIndex, uint id, int first, int end, int contourFirst, int contourEnd)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
            Vector2 sweptMin = min, sweptMax = max;
            for (int i = first; i < end; i++)
            {
                GpuLiquidBoundarySegment edge = boundaryUpload[i];
                Vector2 edgeMin = Vector2.Min(edge.A, edge.B), edgeMax = Vector2.Max(edge.A, edge.B);
                min = Vector2.Min(min, edgeMin); max = Vector2.Max(max, edgeMax);
                Vector2 travel = edge.EndPosition - edge.StartPosition;
                if (Mathf.Abs(edge.AngleDelta) < 1e-6f)
                {
                    edgeMin = Vector2.Min(edgeMin, edgeMin - travel);
                    edgeMax = Vector2.Max(edgeMax, edgeMax - travel);
                }
                else
                {
                    // Full rotational envelope, including multiple unwrapped turns.
                    float radius = Mathf.Max(edge.LocalA.magnitude, edge.LocalB.magnitude);
                    edgeMin = Vector2.Min(edge.StartPosition, edge.EndPosition) - Vector2.one * radius;
                    edgeMax = Vector2.Max(edge.StartPosition, edge.EndPosition) + Vector2.one * radius;
                }
                sweptMin = Vector2.Min(sweptMin, edgeMin); sweptMax = Vector2.Max(sweptMax, edgeMax);
            }
            // Conservative padding covers CPU/GPU rounding, not physical wall thickness.
            Vector2 padding = Vector2.one * .0001f;
            boundaryGroups[groupIndex] = new GpuLiquidBoundaryGroup {
                Min = min - padding, Max = max + padding,
                SweptMin = sweptMin - padding, SweptMax = sweptMax + padding,
                First = (uint)first, End = (uint)end, ContourFirst = (uint)contourFirst,
                ContourEnd = (uint)contourEnd, VesselId = id
            };
        }
    }
}
