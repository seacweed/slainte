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
        public const string SpriteRoot = ArtRoot + "Sprites/";
        public const string BottleSpriteRoot = SpriteRoot + "Bottles/";
        public const string CocktailSpriteRoot = SpriteRoot + "Cocktails/";
        public const string EquipmentSpriteRoot = SpriteRoot + "Equipment/";
        public const string BeakerSpriteRoot = EquipmentSpriteRoot + "Beaker/";
        public const string CobblerShakerSpriteRoot =
            EquipmentSpriteRoot + "CobblerShaker/";
        public const string RockGlassSpriteRoot =
            EquipmentSpriteRoot + "RockGlass/";
        public const string BartendingUiSpriteRoot = SpriteRoot + "UI/";
        public const string LiquorShelfSpriteRoot =
            BartendingUiSpriteRoot + "LiquorShelf/";
        public const string RecipeBookSpriteRoot =
            BartendingUiSpriteRoot + "RecipeBook/";

        public const string PrefabRoot = FeatureRoot + "/Prefabs/";
        public const string EquipmentPrefabRoot = PrefabRoot + "Equipment/";
        public const string InteractionPrefabRoot = PrefabRoot + "Interaction/";
        public const string LiquorShelfPrefabRoot = PrefabRoot + "UI/LiquorShelf/";
        public const string RecipeBookPrefabRoot = PrefabRoot + "UI/RecipeBook/";

        public const string BeakerPrefab = EquipmentPrefabRoot + "Beaker.prefab";
        public const string BottlePrefab = EquipmentPrefabRoot + "Bottle.prefab";
        public const string CobblerShakerPrefab =
            EquipmentPrefabRoot + "CobblerShaker.prefab";
        public const string GlassPrefab = EquipmentPrefabRoot + "Glass.prefab";
        public const string IceCubePrefab = EquipmentPrefabRoot + "IceCube.prefab";
        public const string OrangeJuiceBottlePrefab =
            EquipmentPrefabRoot + "OrangeJuiceBottle.prefab";
        public const string TestSlotPrefab =
            InteractionPrefabRoot + "TestSlot.prefab";
    }
}
