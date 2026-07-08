using UnityEngine;

namespace Slainte.Bartending
{
    public sealed class CocktailEvaluationTester : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private GlassController targetGlass;
        [SerializeField] private VesselLiquidTracker targetTracker;
        [SerializeField] private bool autoFindTarget = true;

        [Header("CSV")]
        [SerializeField] private string dataFolder = "Data";
        [SerializeField] private string recipesFileName = "recipes.csv";
        [SerializeField] private string recipeIngredientsFileName = "recipe_ingredients.csv";
        [SerializeField] private string itemResourcesPath = "Items";
        [SerializeField] private ItemDef[] additionalItems;

        [Header("Input")]
        [SerializeField] private KeyCode submitKey = KeyCode.Return;
        [SerializeField] private bool drawResultOnGui = true;

        private CocktailEvaluator evaluator;
        private CocktailRecipeCatalog recipeCatalog;
        private string lastResultText = "Press Enter to evaluate the target glass.";

        private void Start()
        {
            Reload();
        }

        private void Update()
        {
            if (Input.GetKeyDown(submitKey))
                Submit();
        }

        public void Reload()
        {
            ItemDefCatalog itemCatalog = ItemDefCatalog.LoadFromResources(itemResourcesPath, additionalItems);
            recipeCatalog = CocktailRecipeCsvLoader.LoadFromStreamingAssets(
                itemCatalog,
                dataFolder,
                recipesFileName,
                recipeIngredientsFileName);
            evaluator = new CocktailEvaluator(recipeCatalog);
            lastResultText = $"Loaded {recipeCatalog.Count} recipe(s). Press {submitKey} to evaluate.";
            Debug.Log($"[CocktailEvaluationTester] {lastResultText}");
        }

        public void Submit()
        {
            VesselLiquidTracker tracker = ResolveTargetTracker();
            if (tracker == null)
            {
                lastResultText = "No VesselLiquidTracker target found.";
                Debug.LogWarning($"[CocktailEvaluationTester] {lastResultText}");
                return;
            }

            if (evaluator == null)
                Reload();

            CocktailComposition composition = tracker.BuildComposition();
            CocktailEvaluationResult result = evaluator.Evaluate(composition);
            lastResultText = result.ToDebugString();
            Debug.Log($"[CocktailEvaluationTester]\n{lastResultText}");
        }

        private VesselLiquidTracker ResolveTargetTracker()
        {
            if (targetTracker != null)
                return targetTracker;

            if (targetGlass != null && targetGlass.LiquidTracker != null)
                return targetGlass.LiquidTracker;

            if (!autoFindTarget)
                return null;

            targetGlass = FindFirstObjectByType<GlassController>();
            if (targetGlass != null && targetGlass.LiquidTracker != null)
                return targetGlass.LiquidTracker;

            targetTracker = FindFirstObjectByType<VesselLiquidTracker>();
            return targetTracker;
        }

        private void OnGUI()
        {
            if (!drawResultOnGui)
                return;

            const float width = 360f;
            GUI.Box(new Rect(12f, 12f, width, 180f), lastResultText);
        }
    }
}
