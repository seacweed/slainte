namespace Slainte.EditorTools
{
    internal static class RestAssetPaths
    {
        private const string FeatureRoot = "Assets/_Project/Features/Rest";
        private const string PrefabRoot = FeatureRoot + "/Prefabs";
        private const string SpriteRoot = FeatureRoot + "/Art/Sprites";
        private const string FinalSpriteRoot = SpriteRoot + "/Final";
        private const string DeferredFinalSpriteRoot = FinalSpriteRoot + "/Deferred";
        private const string TVFinalSpriteRoot = SpriteRoot + "/TVFinal";

        public const string TVSystemPrefab = PrefabRoot + "/TVSystem.prefab";
        public const string TVPanelPrefab = PrefabRoot + "/TVPanel.prefab";

        public const string BackgroundColor = FinalSpriteRoot + "/background_color.png";
        public const string BackgroundGray = FinalSpriteRoot + "/background_gray.png";
        public const string CompositeReference = FinalSpriteRoot + "/composite_reference.png";
        public const string TVWorldNormal = FinalSpriteRoot + "/tv_world_normal.png";
        public const string TVWorldOutline = FinalSpriteRoot + "/tv_world_outline.png";
        public const string TVWorldGray = FinalSpriteRoot + "/tv_world_gray.png";
        public const string BoardNormal = FinalSpriteRoot + "/board_normal.png";
        public const string BoardOutline = FinalSpriteRoot + "/board_outline.png";
        public const string BoardGray = FinalSpriteRoot + "/board_gray.png";
        public const string ShopNormal = FinalSpriteRoot + "/shop_normal.png";
        public const string ShopOutline = FinalSpriteRoot + "/shop_outline.png";
        public const string ShopGray = FinalSpriteRoot + "/shop_gray.png";
        public const string StrangeShopNormal = DeferredFinalSpriteRoot + "/shop_strange_normal.png";
        public const string StrangeShopOutline = DeferredFinalSpriteRoot + "/shop_strange_outline.png";
        public const string StrangeShopGray = DeferredFinalSpriteRoot + "/shop_strange_gray.png";

        public const string TVBackground = TVFinalSpriteRoot + "/tv_background.png";
        public const string TVFrame = TVFinalSpriteRoot + "/tv_frame.png";
        public const string TVHeadline = TVFinalSpriteRoot + "/tv_headline.png";
        public const string TVCardBackground = TVFinalSpriteRoot + "/tv_card_background.png";

        public static string TVCard(int cardNumber)
        {
            return $"{TVFinalSpriteRoot}/tv_card_{cardNumber}.png";
        }
    }
}
