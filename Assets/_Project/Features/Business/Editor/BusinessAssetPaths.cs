namespace Slainte.EditorTools
{
    internal static class BusinessAssetPaths
    {
        public const string FeatureRoot = "Assets/_Project/Features/Business";
        public const string ArtRoot = FeatureRoot + "/Art/";
        public const string SpriteRoot = ArtRoot + "Sprites/";
        public const string EnvironmentSpriteRoot = SpriteRoot + "Environment/";
        public const string CharacterSpriteRoot = SpriteRoot + "Characters/";
        public const string CustomerSpriteRoot = SpriteRoot + "Customers/";
        public const string OrderTicketSpriteRoot = SpriteRoot + "UI/OrderTicket/";
        public const string ConversationSpriteRoot = SpriteRoot + "UI/Conversation/";
        public const string AudioRoot = FeatureRoot + "/Audio/";
        public const string TypewriterSfx = AudioRoot + "SFX/Untitled.wav";

        public const string PrefabRoot = FeatureRoot + "/Prefabs/";
        public const string AffinityNotificationPrefab =
            PrefabRoot + "UI/Notifications/AffinityNotificationUI.prefab";
        public const string CharacterPrefab =
            PrefabRoot + "Presentation/CharacterPrefab.prefab";
        public const string ChoiceButtonPrefab =
            PrefabRoot + "Conversation/ChoiceButton.prefab";
        public const string ItemRowPrefab =
            PrefabRoot + "OrderTicket/ItemRow.prefab";
    }
}
