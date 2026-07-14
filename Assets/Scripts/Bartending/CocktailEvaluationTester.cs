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
        [SerializeField] private string orderTemplatesFileName = "order_templates.csv";
        [SerializeField] private string itemResourcesPath = "Items";
        [SerializeField] private ItemDef[] additionalItems;

        [Header("Order")]
        [SerializeField] private bool generateOrderOnReload = true;
        [SerializeField] private string fixedRecipeOrderId = "";
        [SerializeField] private KeyCode nextOrderKey = KeyCode.N;

        [Header("Input")]
        [SerializeField] private KeyCode submitKey = KeyCode.Return;
        [SerializeField] private bool drawResultOnGui = true;

        private CocktailEvaluator evaluator;
        private CocktailOrderEvaluator orderEvaluator;
        private CocktailOrderGenerator orderGenerator;
        private CocktailRecipeCatalog recipeCatalog;
        private CocktailOrderTemplateCatalog orderTemplateCatalog;
        private GeneratedCocktailOrder currentOrder;
        private string lastResultText = "Press Enter to evaluate the target glass.";

        private void Start()
        {
            Reload();
        }

        private void Update()
        {
            if (Input.GetKeyDown(submitKey))
                Submit();

            if (Input.GetKeyDown(nextOrderKey))
                GenerateNewOrder();
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
            orderTemplateCatalog = CocktailOrderCsvLoader.LoadTemplatesFromStreamingAssets(
                dataFolder,
                orderTemplatesFileName);
            orderGenerator = new CocktailOrderGenerator(recipeCatalog, orderTemplateCatalog);
            orderEvaluator = new CocktailOrderEvaluator(evaluator);

            if (generateOrderOnReload)
            {
                GenerateNewOrder();
                return;
            }

            lastResultText = $"Loaded {recipeCatalog.Count} recipe(s), {orderTemplateCatalog.Count} order template(s). Press {nextOrderKey} for an order.";
            Debug.Log($"[CocktailEvaluationTester] {lastResultText}");
        }

        public void GenerateNewOrder()
        {
            if (orderGenerator == null)
            {
                ItemDefCatalog itemCatalog = ItemDefCatalog.LoadFromResources(itemResourcesPath, additionalItems);
                recipeCatalog = CocktailRecipeCsvLoader.LoadFromStreamingAssets(
                    itemCatalog,
                    dataFolder,
                    recipesFileName,
                    recipeIngredientsFileName);
                evaluator = new CocktailEvaluator(recipeCatalog);
                orderTemplateCatalog = CocktailOrderCsvLoader.LoadTemplatesFromStreamingAssets(
                    dataFolder,
                    orderTemplatesFileName);
                orderGenerator = new CocktailOrderGenerator(recipeCatalog, orderTemplateCatalog);
                orderEvaluator = new CocktailOrderEvaluator(evaluator);
            }

            currentOrder = orderGenerator.GenerateRecipeOrder(fixedRecipeOrderId);
            if (currentOrder == null)
            {
                lastResultText = $"Loaded {recipeCatalog.Count} recipe(s), {orderTemplateCatalog.Count} order template(s). No order generated.";
                Debug.LogWarning($"[CocktailEvaluationTester] {lastResultText}");
                return;
            }

            lastResultText = BuildCurrentOrderText();
            Debug.Log($"[CocktailEvaluationTester]\n{lastResultText}");
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

            if (currentOrder == null)
                GenerateNewOrder();

            CocktailComposition composition = tracker.BuildComposition();
            CocktailEvaluationResult detectedRecipeResult = evaluator.Evaluate(composition);
            if (currentOrder != null && orderEvaluator != null)
            {
                CocktailOrderEvaluationResult orderResult = orderEvaluator.Evaluate(
                    currentOrder,
                    composition,
                    detectedRecipeResult);
                lastResultText = orderResult.ToDebugString();
            }
            else
            {
                lastResultText = detectedRecipeResult.ToDebugString();
            }

            Debug.Log($"[CocktailEvaluationTester]\n{lastResultText}");
        }

        private string BuildCurrentOrderText()
        {
            if (currentOrder == null)
                return $"No active order. Press {nextOrderKey} for a new order.";

            return "Current Order\n"
                + currentOrder.line
                + "\nRequested: "
                + currentOrder.RequestedRecipeName
                + $"\nPress {submitKey} to submit. Press {nextOrderKey} for a new order.";
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

            const float width = 440f;
            GUI.Box(new Rect(12f, 12f, width, 260f), lastResultText);
        }
    }
}
