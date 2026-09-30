using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    /// <summary>
    /// Read-only measurements of an existing CPU particle snapshot. Heights use world up and are
    /// intended for upright reference vessels; ownership, rather than pick geometry, selects liquid.
    /// No GPU readback or simulation mutation is performed here.
    /// </summary>
    public sealed class FluidExperimentVolumeMetrics
    {
        public float ActiveMl { get; private set; }
        public float ContainedMl { get; private set; }
        public float OutsideMl { get; private set; }
        public int ParticleCount { get; private set; }
        public int ContainedParticleCount { get; private set; }
        public float ParticleHeight95 { get; private set; }
        public float ParticleHeightMax { get; private set; }
        public float MeanSpeed { get; private set; }
        public float InteriorArea { get; private set; }
        /// <summary>
        /// Horizontal area-fill height from requested ml / vessel capacity metadata. This is a
        /// nominal comparison reference, not a calibrated conversion from 2D area to liquid ml.
        /// </summary>
        public float CapacityReferenceHeight { get; private set; }

        private struct HeightSample : IComparable<HeightSample>
        {
            public float height, volume;
            public int CompareTo(HeightSample other) => height.CompareTo(other.height);
        }
        private struct Interval : IComparable<Interval>
        {
            public float min, max;
            public int CompareTo(Interval other) => min.CompareTo(other.min);
        }

        private readonly List<HeightSample> heights = new List<HeightSample>();
        private readonly List<Vector2> geometry = new List<Vector2>();
        private readonly List<int> polygonEnds = new List<int>();
        private readonly List<float> regionX = new List<float>();
        private readonly List<Interval> regionY = new List<Interval>();
        private float bottom, top;

        public void Measure(FluidExperimentGpuLiquid gpu, FluidExperimentBody vessel, float referenceVolumeMl)
        {
            ClearGeometry();
            if (vessel != null)
            {
                Vector2[] interior = vessel.collisionProfile != null ? vessel.collisionProfile.interior : null;
                if (interior != null && interior.Length >= 3)
                {
                    foreach (Vector2 p in interior) geometry.Add(vessel.LocalToWorld(p));
                    if (!FinishPolygon(0)) ClearGeometry();
                }
                if (polygonEnds.Count == 0) AddRegionUnion(vessel);
            }
            MeasureCore(gpu != null ? gpu.Snapshot : null, vessel != null ? vessel.Id : 0,
                gpu != null ? gpu.Radius : 0, vessel != null ? vessel.capacityMl : 0, referenceVolumeMl);
        }

        /// <summary>Pure-data entry point for reference and regression measurements, without a GPU or scene.</summary>
        public void MeasureSnapshot(GpuLiquidParticle[] particles, uint vesselId, float particleRadius,
            Vector2[] worldInterior, float capacityMl, float referenceVolumeMl)
        {
            ClearGeometry();
            if (worldInterior != null && worldInterior.Length >= 3)
            {
                geometry.AddRange(worldInterior);
                if (!FinishPolygon(0)) ClearGeometry();
            }
            MeasureCore(particles, vesselId, particleRadius, capacityMl, referenceVolumeMl);
        }

        private void MeasureCore(GpuLiquidParticle[] particles, uint vesselId, float particleRadius,
            float capacityMl, float referenceVolumeMl)
        {
            ActiveMl = ContainedMl = OutsideMl = ParticleHeight95 = ParticleHeightMax = MeanSpeed = 0;
            ParticleCount = ContainedParticleCount = 0;
            heights.Clear();
            MeasureGeometry(capacityMl, referenceVolumeMl);
            if (particles == null) return;

            double active = 0, contained = 0, outside = 0, heightVolume = 0, speedVolume = 0, speedTotal = 0;
            float radius = Nonnegative(particleRadius);
            foreach (GpuLiquidParticle particle in particles)
            {
                if (particle.Active == 0) continue;
                ParticleCount++;
                float volume = Nonnegative(particle.VolumeMl);
                active += volume;
                // Zero is the unowned/airborne identifier, never a selected vessel.
                if (vesselId == 0 || particle.VesselId != vesselId) { outside += volume; continue; }
                ContainedParticleCount++;
                contained += volume;
                if (volume <= 0) continue;
                if (Finite(particle.Velocity.x) && Finite(particle.Velocity.y))
                {
                    double vx = particle.Velocity.x, vy = particle.Velocity.y;
                    speedTotal += Math.Sqrt(vx * vx + vy * vy) * volume;
                    speedVolume += volume;
                }
                if (polygonEnds.Count == 0 || !Finite(particle.Position.x) || !Finite(particle.Position.y)) continue;
                float height = Nonnegative((double)particle.Position.y + radius - bottom);
                heights.Add(new HeightSample { height = height, volume = volume });
                heightVolume += volume;
                ParticleHeightMax = Mathf.Max(ParticleHeightMax, height);
            }
            ActiveMl = Nonnegative(active); ContainedMl = Nonnegative(contained); OutsideMl = Nonnegative(outside);
            if (speedVolume > 0) MeanSpeed = Nonnegative(speedTotal / speedVolume);
            if (heights.Count == 0) return;
            heights.Sort();
            double accumulated = 0, threshold = heightVolume * .95;
            foreach (HeightSample sample in heights)
            {
                accumulated += sample.volume;
                if (accumulated < threshold) continue;
                ParticleHeight95 = sample.height;
                break;
            }
        }

        private void ClearGeometry()
        {
            geometry.Clear(); polygonEnds.Clear();
            bottom = top = 0;
        }

        private bool FinishPolygon(int start)
        {
            int end = geometry.Count;
            for (int i = start; i < end; i++)
                if (!Finite(geometry[i].x) || !Finite(geometry[i].y)) return false;
            if (PolygonArea(start, end, float.PositiveInfinity) <= 0) return false;
            polygonEnds.Add(end);
            for (int i = start; i < end; i++)
            {
                if (i == 0) bottom = top = geometry[i].y;
                bottom = Mathf.Min(bottom, geometry[i].y);
                top = Mathf.Max(top, geometry[i].y);
            }
            return true;
        }

        // Rectangular fallback regions can overlap. Partition their union locally before transforming
        // to world space, so their overlap is never counted twice in area or reference fill height.
        private void AddRegionUnion(FluidExperimentBody vessel)
        {
            Rect[] regions = vessel.contentRegions;
            if (regions == null) return;
            regionX.Clear();
            foreach (Rect r in regions)
            {
                if (!ValidRegion(r)) continue;
                regionX.Add(r.xMin); regionX.Add(r.xMax);
            }
            regionX.Sort();
            for (int x = 1; x < regionX.Count; x++)
            {
                float left = regionX[x - 1], right = regionX[x];
                if (left >= right) continue;
                regionY.Clear();
                foreach (Rect r in regions)
                    if (ValidRegion(r) && r.xMin < right && r.xMax > left)
                        regionY.Add(new Interval { min = r.yMin, max = r.yMax });
                regionY.Sort();
                for (int y = 0; y < regionY.Count; y++)
                {
                    float low = regionY[y].min, high = regionY[y].max;
                    while (y + 1 < regionY.Count && regionY[y + 1].min <= high)
                        high = Mathf.Max(high, regionY[++y].max);
                    int start = geometry.Count;
                    geometry.Add(vessel.LocalToWorld(new Vector2(left, low)));
                    geometry.Add(vessel.LocalToWorld(new Vector2(right, low)));
                    geometry.Add(vessel.LocalToWorld(new Vector2(right, high)));
                    geometry.Add(vessel.LocalToWorld(new Vector2(left, high)));
                    if (!FinishPolygon(start)) geometry.RemoveRange(start, geometry.Count - start);
                }
            }
        }

        private void MeasureGeometry(float capacityMl, float referenceVolumeMl)
        {
            double area = AreaBelow(float.PositiveInfinity);
            InteriorArea = Nonnegative(area);
            CapacityReferenceHeight = 0;
            if (area <= 0 || !Finite(capacityMl) || capacityMl <= 0 || !Finite(referenceVolumeMl)) return;
            double fraction = Math.Max(0, Math.Min(1, (double)referenceVolumeMl / capacityMl));
            if (fraction <= 0) return;
            if (fraction >= 1) { CapacityReferenceHeight = Nonnegative((double)top - bottom); return; }
            double targetArea = area * fraction;
            double low = bottom, high = top;
            for (int i = 0; i < 32; i++)
            {
                double middle = (low + high) * .5;
                if (AreaBelow(middle) < targetArea) low = middle;
                else high = middle;
            }
            CapacityReferenceHeight = Nonnegative((low + high) * .5 - bottom);
        }

        private double AreaBelow(double level)
        {
            double area = 0;
            int start = 0;
            foreach (int end in polygonEnds) { area += PolygonArea(start, end, level); start = end; }
            return area;
        }

        // Shoelace area of a polygon clipped by y <= level. Emit clipped vertices directly into the
        // area sum; no temporary polygon allocations are needed for the bisection iterations.
        private double PolygonArea(int start, int end, double level)
        {
            if (end - start < 3) return 0;
            Vector2 origin = geometry[start];
            double firstX = 0, firstY = 0, previousX = 0, previousY = 0;
            bool found = false;
            double twiceArea = 0;
            Vector2 a = geometry[end - 1];
            bool aInside = a.y <= level;
            for (int i = start; i < end; i++)
            {
                Vector2 b = geometry[i];
                bool bInside = b.y <= level;
                if (aInside != bInside)
                {
                    double t = (level - a.y) / ((double)b.y - a.y);
                    AddAreaVertex(a.x - (double)origin.x + ((double)b.x - a.x) * t, level - origin.y,
                        ref firstX, ref firstY, ref previousX, ref previousY, ref found, ref twiceArea);
                }
                if (bInside) AddAreaVertex((double)b.x - origin.x, (double)b.y - origin.y,
                    ref firstX, ref firstY, ref previousX, ref previousY, ref found, ref twiceArea);
                a = b; aInside = bInside;
            }
            if (found) twiceArea += previousX * firstY - firstX * previousY;
            return Math.Abs(twiceArea) * .5;
        }

        private static void AddAreaVertex(double x, double y, ref double firstX, ref double firstY,
            ref double previousX, ref double previousY, ref bool found, ref double twiceArea)
        {
            if (found) twiceArea += previousX * y - x * previousY;
            else { firstX = x; firstY = y; found = true; }
            previousX = x; previousY = y;
        }

        private static bool ValidRegion(Rect r) => Finite(r.xMin) && Finite(r.xMax) &&
            Finite(r.yMin) && Finite(r.yMax) && r.width > 0 && r.height > 0;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Nonnegative(float value) => Finite(value) ? Mathf.Max(0, value) : 0;
        private static float Nonnegative(double value) => double.IsNaN(value) || value <= 0 ? 0 :
            (float)Math.Min(float.MaxValue, value);
    }
}
