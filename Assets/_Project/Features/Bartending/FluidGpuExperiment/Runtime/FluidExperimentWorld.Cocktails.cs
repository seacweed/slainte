namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentWorld
    {
        private FluidExperimentCocktailSession cocktailSession;
        public FluidExperimentCocktailSession Cocktails => cocktailSession != null
            ? cocktailSession : cocktailSession = GetComponent<FluidExperimentCocktailSession>();

        private void ObserveCocktailTechniques(float dt)
        {
            if (Cocktails != null && Cocktails.isActiveAndEnabled) Cocktails.ObserveTechniques(dt);
        }
    }
}
