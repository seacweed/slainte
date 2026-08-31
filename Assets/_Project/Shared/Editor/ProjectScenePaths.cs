namespace Slainte.EditorTools
{
    public static class ProjectScenePaths
    {
        private const string ProductionRoot = "Assets/_Project/Scenes/Production";
        private const string DevelopmentRoot = "Assets/_Project/Scenes/Development";

        public const string MainMenu = ProductionRoot + "/MainMenuScene.unity";
        public const string Core = ProductionRoot + "/CoreScene.unity";
        public const string Business = ProductionRoot + "/BusinessScene.unity";
        public const string Rest = ProductionRoot + "/RestScene.unity";

        public const string BartendingSandbox =
            DevelopmentRoot + "/BartendingSandbox.unity";
        public const string BusinessCustomerPoolStress =
            DevelopmentRoot + "/BusinessCustomerPoolStress.unity";
        public const string BusinessFlowIntegrationPlaytest =
            DevelopmentRoot + "/BusinessFlowIntegrationPlaytest.unity";
    }
}
