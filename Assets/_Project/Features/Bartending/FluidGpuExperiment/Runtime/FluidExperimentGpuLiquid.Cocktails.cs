using System;
using System.Linq;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        public int SimulationGeneration => generation;

        /// <summary>A fresh, read-only serving snapshot. Canonical IDs also resolve the 80C water definition.</summary>
        public bool TryBuildCocktailComposition(FluidExperimentBody vessel, ItemDefCatalog catalog,
            out CocktailComposition composition, out string error)
        {
            composition = null; error = string.Empty;
            if (!IsOperational || vessel == null || vessel.World != world || !vessel.IsVessel
                || !vessel.isActiveAndEnabled || !world.Items.Contains(vessel))
            { error = "현재 월드의 유효한 용기를 선택하세요."; return false; }
            if (catalog == null) { error = "재료 카탈로그를 불러오지 못했습니다."; return false; }
            if (pendingSpawnCount != 0)
            { error = "액체 생성이 처리 중입니다. 다음 물리 틱 이후 다시 판정하세요."; return false; }
            ReadbackNow();
            var result = new CocktailComposition();
            foreach (var pair in IngredientsIn(vessel.Id))
            {
                if (!double.IsFinite(pair.Value) || pair.Value < -.00001 || pair.Key == null
                    || !catalog.TryGet(pair.Key.id, out ItemDef canonical))
                { error = "판정 재료 ID를 해석하지 못했습니다: " + (pair.Key != null ? pair.Key.id : "null"); return false; }
                result.Add(canonical, (float)Math.Max(0, pair.Value));
            }
            double volume = 0, attemptedMl = 0, stirredMl = 0, shakenMl = 0, shakenWithIceMl = 0;
            foreach (var particle in snapshotParticles)
            {
                if (particle.Active == 0 || particle.VesselId != vessel.Id) continue;
                if (!float.IsFinite(particle.VolumeMl) || particle.VolumeMl <= 0 || !float.IsFinite(particle.TemperatureC))
                { error = "액체 스냅샷에 유효하지 않은 용량 또는 온도가 있습니다."; return false; }
                volume += particle.VolumeMl;
                result.AddThermalSample(particle.VolumeMl, particle.TemperatureC);
                if ((particle.TechniqueFlags & (uint)CocktailTechnique.Stir) != 0) stirredMl += particle.VolumeMl;
                if ((particle.TechniqueFlags & (uint)CocktailTechnique.Shake) != 0) shakenMl += particle.VolumeMl;
                if ((particle.StateFlags & 1u) != 0) attemptedMl += particle.VolumeMl;
                if ((particle.StateFlags & 2u) != 0) shakenWithIceMl += particle.VolumeMl;
            }
            if (Math.Abs(result.TotalVolumeMl - volume) > Math.Max(.001, volume * .0001))
            { error = "액체 총량과 재료별 합계가 일치하지 않습니다."; return false; }
            // Ingredient totals and covered totals have different accumulation orders. Express
            // coverage on the composition's own total so fully processed contents remain exact,
            // without granting newly added, unprocessed liquid any technique history.
            float Coverage(double covered) => volume > 0 ? (float)(Math.Min(1, covered / volume) * result.TotalVolumeMl) : 0;
            result.RecordParticleTechniqueState(Coverage(attemptedMl), CocktailTechnique.None, true, false);
            result.RecordParticleTechniqueState(Coverage(stirredMl), CocktailTechnique.Stir, false, false);
            result.RecordParticleTechniqueState(Coverage(shakenMl), CocktailTechnique.Shake, false, false);
            result.RecordParticleTechniqueState(Coverage(shakenWithIceMl), CocktailTechnique.None, false, true);
            int iceCount = 0;
            foreach (var body in world.Items)
                if (body != null && body.kind == LabItemKind.Ice && body.ContainingVesselId == vessel.Id
                    && !body.IsHeld && body.Body != null && body.Body.simulated) iceCount++;
            result.SetServingStyle(vessel.servingGlassId, iceCount);
            composition = result;
            return true;
        }

        /// <summary>Marks only current active contents. Does not change position, velocity, volume or mixture.</summary>
        public bool MarkCocktailTechnique(FluidExperimentBody vessel, CocktailTechnique technique,
            bool stirAttempted, bool shakenWithIce)
        {
            if (!IsOperational || vessel == null || vessel.World != world || !vessel.IsVessel || vessel.Id == 0)
                return false;
            simulationShader.SetInt("_TechniqueTargetVessel", (int)vessel.Id);
            simulationShader.SetInt("_TechniqueMask", (int)(technique & (CocktailTechnique.Stir | CocktailTechnique.Shake)));
            simulationShader.SetInt("_TechniqueStateMask", (stirAttempted ? 1 : 0) | (shakenWithIce ? 2 : 0));
            DispatchForCount(techniqueKernel, particleCapacity);
            liquidStateVersion++;
            return true;
        }

        internal bool CocktailMixtureUniform(FluidExperimentBody vessel, float tolerance)
        {
            float total = VolumeIn(vessel.Id);
            if (total <= 0) return false;
            foreach (var pair in ingredientIndices)
            {
                double mean = IngredientVolumeIn(vessel.Id, pair.Key) / total;
                for (int i = 0; i < snapshotParticles.Length; i++)
                {
                    var particle = snapshotParticles[i];
                    if (particle.Active != 0 && particle.VesselId == vessel.Id
                        && Math.Abs(snapshotComposition[i * maximumIngredients + pair.Value] - mean) > tolerance)
                        return false;
                }
            }
            return true;
        }
    }
}
