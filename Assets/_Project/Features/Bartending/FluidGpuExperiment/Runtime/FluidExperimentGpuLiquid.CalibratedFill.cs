using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        private readonly FluidExperimentVolumeMetrics calibrationGeometry = new FluidExperimentVolumeMetrics();
        private readonly List<Vector2> calibratedFillSites = new List<Vector2>();
        private readonly List<Vector2> calibratedOccupiedSites = new List<Vector2>();
        private readonly List<Vector2> calibratedHeightSamples = new List<Vector2>();

        // A vessel's capacity fixes its effective depth: its entire interior area represents capacityMl.
        // This is a 2D convention; it never changes the authoritative ml carried by each particle.
        public float AreaPerMl(uint vesselId)
        {
            if (world != null && vesselId != 0)
                foreach (FluidExperimentBody body in world.Items)
                    if (body.Id == vesselId && body.IsVessel && float.IsFinite(body.capacityMl) && body.capacityMl > 0)
                        return AreaPerMl(body);
            return ReferenceFinite(settings.improvedAreaPerMl, .00678f, .0001f, 1);
        }

        private float AreaPerMl(FluidExperimentBody body)
        {
            if (body.IsVessel && float.IsFinite(body.capacityMl) && body.capacityMl > 0)
            {
                calibrationGeometry.Measure(null, body, 0);
                if (calibrationGeometry.InteriorArea > 0) return calibrationGeometry.InteriorArea / body.capacityMl;
            }
            return ReferenceFinite(settings.improvedAreaPerMl, .00678f, .0001f, 1);
        }

        public float ImprovedKernelRadius(uint vesselId) => ImprovedKernelRadiusForArea(AreaPerMl(vesselId));

        private float ImprovedKernelRadiusForArea(float areaPerMl) => Mathf.Clamp(
            Mathf.Sqrt(Mathf.Max(.001f, ParticleVolumeMl) * areaPerMl) * settings.improvedKernelRatio,
            Radius * 2.1f, SolverSmoothingRadius);

        private float FillCalibrated(FluidExperimentBody vessel, ItemDef ingredient, float requested)
        {
            if (ingredient == null || !float.IsFinite(requested) || requested <= 0
                || !float.IsFinite(ParticleVolumeMl) || ParticleVolumeMl <= 0) return 0;
            ReadbackNow();
            float existing = VolumeIn(vessel.Id);
            calibratedOccupiedSites.Clear();
            foreach (GpuLiquidParticle particle in snapshotParticles)
                if (particle.Active != 0 && particle.VesselId == vessel.Id) calibratedOccupiedSites.Add(particle.Position);
            for (int i = 0; i < pendingSpawnCount; i++)
                if (spawnCommands[i].VesselId == vessel.Id)
                {
                    existing += spawnCommands[i].VolumeMl;
                    calibratedOccupiedSites.Add(spawnCommands[i].Position);
                }
            float remaining = Mathf.Min(requested, Mathf.Max(0, vessel.capacityMl - existing));
            if (remaining <= 0) return 0;
            Rect bounds;
            if (vessel.collisionProfile != null && vessel.collisionProfile.interior.Length >= 3)
                bounds = vessel.collisionProfile.InteriorBounds;
            else
            {
                bounds = vessel.contentRegions[0];
                foreach (Rect region in vessel.contentRegions)
                    bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, region.xMin), Mathf.Min(bounds.yMin, region.yMin),
                        Mathf.Max(bounds.xMax, region.xMax), Mathf.Max(bounds.yMax, region.yMax));
            }
            Vector3 scale = vessel.transform.lossyScale;
            float sx = Mathf.Max(.0001f, Mathf.Abs(scale.x)), sy = Mathf.Max(.0001f, Mathf.Abs(scale.y));
            float spacing = Mathf.Sqrt(ParticleVolumeMl * AreaPerMl(vessel.Id) / .8660254f);
            float existingSurface = float.NegativeInfinity;
            calibratedHeightSamples.Clear();
            float surfaceVolume = 0;
            foreach (GpuLiquidParticle particle in snapshotParticles)
                if (particle.Active != 0 && particle.VesselId == vessel.Id)
                {
                    calibratedHeightSamples.Add(new Vector2(vessel.WorldToLocal(particle.Position).y, particle.VolumeMl));
                    surfaceVolume += particle.VolumeMl;
                }
            calibratedHeightSamples.Sort((a, b) => a.x.CompareTo(b.x));
            float accumulated = 0;
            foreach (Vector2 sample in calibratedHeightSamples)
            {
                accumulated += sample.y;
                existingSurface = sample.x;
                if (accumulated >= surfaceVolume * .95f) break;
            }
            int needed = Mathf.Min(particleCapacity, Mathf.CeilToInt(remaining / ParticleVolumeMl));
            // A finite disk loses a strip of usable space at the walls. At high fill fractions,
            // compress only the initial lattice enough to fit the authored ml capacity. The same
            // density solver then relaxes it; neither particle ml nor the capacity scale changes.
            float minimumSpacing = Radius * 2.001f;
            spacing = Mathf.Max(spacing, minimumSpacing);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                FindCalibratedFillSites(vessel, bounds, sx, sy, spacing, needed, existingSurface);
                if (calibratedFillSites.Count >= needed || spacing <= minimumSpacing) break;
                spacing = Mathf.Max(minimumSpacing, spacing * .995f);
            }
            float emitted = 0;
            foreach (Vector2 position in calibratedFillSites)
            {
                float ml = Mathf.Min(ParticleVolumeMl, remaining - emitted);
                if (ml <= 0 || !TryEmit(position, Vector2.zero, ingredient, ml, vessel.Id)) break;
                emitted += ml;
            }
            return emitted;
        }

        private void FindCalibratedFillSites(FluidExperimentBody vessel, Rect bounds, float sx, float sy,
            float spacing, int needed, float existingSurface)
        {
            calibratedFillSites.Clear();
            float dx = spacing / sx, dy = spacing * .8660254f / sy;
            float clearance = Mathf.Max(Radius * 2, spacing * .8f);
            // Topping up starts at the existing P95 surface, not at a single stray crest.
            // Filling gaps deep inside settled liquid would compress the bulk and launch it out.
            float firstY = Mathf.Max(bounds.yMin + Radius / sy + .0005f, existingSurface + clearance / sy);
            int row = 0;
            for (float y = firstY; y < bounds.yMax - Radius / sy && calibratedFillSites.Count < needed; y += dy, row++)
            for (float x = bounds.xMin + Radius / sx + (row % 2) * dx * .5f; x < bounds.xMax - Radius / sx && calibratedFillSites.Count < needed; x += dx)
            {
                Vector2 local = new Vector2(x, y);
                if (!vessel.ContainsLiquidDisk(local, Radius)) continue;
                Vector2 position = vessel.LocalToWorld(local);
                if (CalibratedFillOccupied(position, clearance)) continue;
                calibratedFillSites.Add(position);
            }
        }

        private bool CalibratedFillOccupied(Vector2 position, float clearance)
        {
            float square = clearance * clearance;
            foreach (Vector2 occupied in calibratedOccupiedSites)
                if ((occupied - position).sqrMagnitude < square) return true;
            return false;
        }
    }
}
