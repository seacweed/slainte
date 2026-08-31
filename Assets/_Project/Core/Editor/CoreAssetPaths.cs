namespace Slainte.EditorTools
{
    internal static class CoreAssetPaths
    {
        public const string CoreRoot = "Assets/_Project/Core";
        public const string ContentRoot = CoreRoot + "/Content/";
        public const string CutsceneContentRoot = ContentRoot + "Cutscenes/";
        public const string CutsceneSpriteRoot =
            CoreRoot + "/Art/Sprites/Cutscenes/";
        public const string SettlementSpriteRoot =
            CoreRoot + "/Art/Sprites/Settlement/";
        public const string SettlementLinePrefab =
            CoreRoot + "/Prefabs/Settlement/SettlementLine.prefab";

        public const string ProjectSettingsRoot = "Assets/_Project/Settings/";
        public const string DefaultVolumeProfile =
            ProjectSettingsRoot + "DefaultVolumeProfile.asset";
        public const string InputActions =
            ProjectSettingsRoot + "InputSystem_Actions.inputactions";
        public const string UniversalRenderPipelineGlobalSettings =
            ProjectSettingsRoot + "UniversalRenderPipelineGlobalSettings.asset";
    }
}
