namespace Slainte.Bartending.EditorTools
{
    internal static class MetaballFluidAssetPaths
    {
        private const string Root =
            "Assets/_Project/Features/Bartending/Infrastructure/MetaballFluid";
        private const string GraphicsRoot = Root + "/Graphics";
        private const string PhysicsRoot = Root + "/Physics";
        private const string PrefabRoot = Root + "/Prefabs";
        private const string RuntimeLiquidRoot =
            "Assets/_Project/Features/Bartending/Runtime/Liquid";
        private const string ParticleRuntimeRoot = RuntimeLiquidRoot + "/Particles";
        private const string RenderingRuntimeRoot = RuntimeLiquidRoot + "/Rendering";

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
        public const string PoolScript = ParticleRuntimeRoot + "/LiquidPool.cs";
        public const string ReactionScript = ParticleRuntimeRoot + "/LiquidReaction.cs";
        public const string RecyclerScript =
            ParticleRuntimeRoot + "/LiquidParticleRecycler.cs";
        public const string RendererScript =
            RenderingRuntimeRoot + "/LiquidMetaballRenderer.cs";
        public const string FullScreenQuadScript =
            RenderingRuntimeRoot + "/FullScreenQuad.cs";
        public const string DraggableBarScript =
            "Assets/_Project/Features/Bartending/Runtime/Interaction/DraggableBar.cs";
    }
}
