using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        private readonly FluidExperimentVolumeMetrics calibrationGeometry = new FluidExperimentVolumeMetrics();

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

    }
}
