namespace Slainte.Editor
{
    internal static class BartendingAssetPaths
    {
        public const string FeatureRoot =
            "Assets/_Project/Features/Bartending";
        public const string ArtRoot = FeatureRoot + "/Art/";
        public const string BottleArtRoot = ArtRoot + "Bottles/";
        public const string GlassArtRoot = ArtRoot + "Glasses/";
        public const string LegacyGlassArtRoot =
            ArtRoot + "Legacy/GlassCollisionTests/";
        public const string ToolCabinetArtRoot = ArtRoot + "ToolCabinet/";
        public const string ToolArtRoot = ToolCabinetArtRoot + "Tools/";
        public const string IceArtRoot = ToolCabinetArtRoot + "Ice/";
        public const string DefaultIceSprite = IceArtRoot + "ice_01.png";
    }
}
