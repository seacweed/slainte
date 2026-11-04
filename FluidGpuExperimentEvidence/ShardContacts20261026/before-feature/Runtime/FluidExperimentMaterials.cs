namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum FluidExperimentMaterial { Auto, Water, Spirit, Syrup, Milk }

    /// <summary>Game-scale response presets, not laboratory material measurements.</summary>
    public readonly struct FluidExperimentMaterialPreset
    {
        public readonly float ViscosityRate, SurfaceTension, Wetting, FoamLifetime, OpacityScale;
        public readonly float FlowMultiplier, ResponseMultiplier, DripMultiplier;
        public FluidExperimentMaterialPreset(float viscosity, float tension, float wetting, float foam,
            float opacity, float flow, float response, float drip)
        {
            ViscosityRate = viscosity; SurfaceTension = tension; Wetting = wetting;
            FoamLifetime = foam; OpacityScale = opacity; FlowMultiplier = flow;
            ResponseMultiplier = response; DripMultiplier = drip;
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
                case FluidExperimentMaterial.Spirit: return new FluidExperimentMaterialPreset(5, .6f, .85f, .35f, 1, 1.1f, .85f, .65f);
                case FluidExperimentMaterial.Syrup: return new FluidExperimentMaterialPreset(35, 1.15f, .9f, 2, 1, .25f, 3, 1.8f);
                case FluidExperimentMaterial.Milk: return new FluidExperimentMaterialPreset(16, .8f, .8f, 4, 1.6f, .65f, 1.5f, 1.3f);
                default: return new FluidExperimentMaterialPreset(8, 1, .7f, .65f, 1, 1, 1, 1);
            }
        }
    }
}
