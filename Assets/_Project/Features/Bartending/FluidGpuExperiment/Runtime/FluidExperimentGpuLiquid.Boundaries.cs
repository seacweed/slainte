using System.Runtime.InteropServices;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    // One contiguous range per body. IDs are keys, never buffer offsets: unregistering
    // and re-registering bodies must not invalidate ownership lookups.
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuLiquidBoundaryGroup
    {
        public Vector2 Min, Max, SweptMin, SweptMax;
        public uint First, End, ContourFirst, ContourEnd, VesselId;
    }

    public sealed partial class FluidExperimentGpuLiquid
    {
        private GraphicsBuffer boundaryGroupBuffer;
        private GpuLiquidBoundaryGroup[] boundaryGroups;

        private void UploadBoundaryGroup(int groupIndex, uint id, int first, int end, int contourFirst, int contourEnd)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
            Vector2 sweptMin = min, sweptMax = max;
            // Each contiguous group belongs to one rigid body and shares its pose.
            float startSine = Mathf.Sin(boundaryUpload[first].StartAngle);
            float startCosine = Mathf.Cos(boundaryUpload[first].StartAngle);
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
                else if (Mathf.Abs(edge.AngleDelta) <= 1f)
                {
                    // A rotating endpoint differs from its linear chord by at most
                    // max|p''(t)| / 8 = radius * angleDelta^2 / 8. Translation is linear.
                    // This conservative arc envelope avoids treating a small rotation
                    // as a full turn, while retaining every point of the swept segment.
                    Vector2 startA = edge.StartPosition + new Vector2(
                        startCosine * edge.LocalA.x - startSine * edge.LocalA.y,
                        startSine * edge.LocalA.x + startCosine * edge.LocalA.y);
                    Vector2 startB = edge.StartPosition + new Vector2(
                        startCosine * edge.LocalB.x - startSine * edge.LocalB.y,
                        startSine * edge.LocalB.x + startCosine * edge.LocalB.y);
                    float radius = Mathf.Max(edge.LocalA.magnitude, edge.LocalB.magnitude);
                    Vector2 arcPadding = Vector2.one * (radius * edge.AngleDelta * edge.AngleDelta * .125f);
                    edgeMin = Vector2.Min(edgeMin, Vector2.Min(startA, startB)) - arcPadding;
                    edgeMax = Vector2.Max(edgeMax, Vector2.Max(startA, startB)) + arcPadding;
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
