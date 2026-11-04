namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum FluidExperimentMaterial { Auto, Water, Spirit, Syrup, Milk }

    /// <summary>Game-scale response presets, not laboratory material measurements.</summary>
    public readonly struct FluidExperimentMaterialPreset
    {
        public readonly float ViscosityRate, SurfaceTension;
        public FluidExperimentMaterialPreset(float viscosity, float tension)
        {
            ViscosityRate = viscosity; SurfaceTension = tension;
        }
    }

    public static class FluidExperimentMaterials
    {
        public static FluidExperimentMaterialPreset Resolve(ItemDef ingredient,
            FluidExperimentMaterial selection = FluidExperimentMaterial.Auto)
        {
            if (selection == FluidExperimentMaterial.Auto)
            {
                if (ingredient != null && (ingredient.bottleCategory == BottleCategory.Syrup
                    || ingredient.liquidType == BottleLiquidType.Syrup)) selection = FluidExperimentMaterial.Syrup;
                else if (ingredient != null && (ingredient.abvPercent > 0
                    || ingredient.bottleCategory == BottleCategory.Spirit || ingredient.bottleCategory == BottleCategory.Liqueur))
                    selection = FluidExperimentMaterial.Spirit;
                else selection = FluidExperimentMaterial.Water;
            }
            switch (selection)
            {
                case FluidExperimentMaterial.Spirit: return new FluidExperimentMaterialPreset(5, .6f);
                case FluidExperimentMaterial.Syrup: return new FluidExperimentMaterialPreset(35, 1.15f);
                case FluidExperimentMaterial.Milk: return new FluidExperimentMaterialPreset(16, .8f);
                default: return new FluidExperimentMaterialPreset(8, 1);
            }
        }
    }
}
