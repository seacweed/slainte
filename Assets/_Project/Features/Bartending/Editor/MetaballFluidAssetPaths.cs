namespace Slainte.Bartending.EditorTools
{
    internal static class MetaballFluidAssetPaths
    {
        private const string Root =
            "Assets/_Project/Features/Bartending/Infrastructure/MetaballFluid";
        private const string GraphicsRoot = Root + "/Graphics";
        private const string PhysicsRoot = Root + "/Physics";
        private const string PrefabRoot = Root + "/Prefabs";
        private const string ScriptRoot = Root + "/Scripts";

        public const string AccumulationMaterial =
            GraphicsRoot + "/LiquidMetaballAccumulation.mat";
        public const string AccumulationShader =
            GraphicsRoot + "/LiquidMetaballAccumulation.shader";
        public const string CompositeShader =
            GraphicsRoot + "/LiquidMetaballComposite.shader";
        public const string MetaballMaterial = GraphicsRoot + "/MetaballMat.mat";
        public const string ParticlePhysicsMaterial =
            PhysicsRoot + "/Particle.physicsMaterial2D";

        public const string WaterParticlePrefab = PrefabRoot + "/water_particle.prefab";
        public const string BlueLiquidPrefab = PrefabRoot + "/BlueLiquid.prefab";
        public const string GreenLiquidPrefab = PrefabRoot + "/greenLiquid.prefab";
        public const string RedLiquidPrefab = PrefabRoot + "/redLiquid.prefab";
        public const string BubblePanelPrefab = PrefabRoot + "/BubblePanel.prefab";

        public const string PoolScript = ScriptRoot + "/ObjPooling.cs";
        public const string RendererScript = ScriptRoot + "/LiquidMetaballRenderer.cs";
    }
}
