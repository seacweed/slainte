namespace Slainte.Content
{
    public static class ProjectResourcePaths
    {
        public const string AssetRoot = "Assets/Resources/";

        public const string BartendingRoot = "Bartending";
        public const string BartendingSettings =
            BartendingRoot + "/BusinessBartendingSettings";
        public const string BartendingToolCabinet =
            BartendingRoot + "/ToolCabinet";
        public const string BartendingToolCabinetCatalog =
            BartendingToolCabinet + "/ToolCabinetCatalog";
        public const string BartendingItems = BartendingRoot + "/Items";
        public const string BartendingRecipes = BartendingRoot + "/Recipes";
        public const string BartendingRecipeVariants =
            BartendingRecipes + "/Variants";
        public const string BartendingShop = BartendingRoot + "/Shop";
        public const string BartendingShopCatalog =
            BartendingShop + "/LiquorShopCatalog";

        public const string BusinessRoot = "Business";
        public const string BusinessOrderFlowSettings =
            BusinessRoot + "/BusinessOrderFlowSettings";
        public const string BusinessCustomerVisits =
            BusinessRoot + "/CustomerVisits";
        public const string BusinessCustomerVisitDatabase =
            BusinessCustomerVisits + "/CustomerVisitDatabase";

        public const string NarrativeRoot = "Narrative";
        public const string NarrativeEpisodes = NarrativeRoot + "/Episodes";
        public const string NarrativeChapters = NarrativeRoot + "/Chapters";
        public const string NarrativeGraphs = NarrativeRoot + "/Graphs";

        public const string RestRoot = "Rest";
        public const string RestTv = RestRoot + "/TV";
        public const string RestTvDatabase = RestTv + "/TVBroadcastDatabase";
        public const string RestSprites = RestRoot + "/Sprites";
        public const string RestEpisodeBoardSprites = RestSprites + "/EpisodeBoard";

        public const string CoreRoot = "Core";
        public const string CoreAudio = CoreRoot + "/Audio";
        public const string CoreBgm = CoreAudio + "/BGM";
        public const string CoreSfx = CoreAudio + "/SFX";
    }

    public static class ProjectStreamingAssetPaths
    {
        public const string AssetRoot = "Assets/StreamingAssets/";

        public const string Bartending = "Bartending";
        public const string BartendingIngredients = "ingredients.csv";
        public const string BartendingOrderTemplates = "order_templates.csv";
        public const string BartendingRecipeIngredients = "recipe_ingredients.csv";
        public const string BartendingRecipes = "recipes.csv";

        public const string Narrative = "Narrative";

        public static string ResolveBartendingDirectory(string configuredDirectory)
        {
            if (string.IsNullOrWhiteSpace(configuredDirectory)
                || string.Equals(
                    configuredDirectory,
                    "Data",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return Bartending;
            }

            return configuredDirectory;
        }
    }
}
