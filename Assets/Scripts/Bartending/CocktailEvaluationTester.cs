using UnityEngine;

namespace Slainte.Bartending
{
    public sealed class CocktailEvaluationTester : MonoBehaviour
    {
        [Header("판정 대상")]
        [SerializeField] private GlassController targetGlass;
        [SerializeField] private VesselLiquidTracker targetTracker;
        [SerializeField] private bool autoFindTarget = true;

        [Header("레시피·주문 데이터")]
        [SerializeField] private string dataFolder = "Data";
        [SerializeField] private string orderTemplatesFileName = "order_templates.csv";
        [SerializeField] private string itemResourcesPath = "Items";
        [SerializeField] private ItemDef[] additionalItems;

        [Header("주문")]
        [SerializeField] private bool generateOrderOnReload = true;
        [SerializeField] private string fixedRecipeOrderId = "";
        [SerializeField] private KeyCode nextOrderKey = KeyCode.N;

        [Header("입력")]
        [SerializeField] private KeyCode submitKey = KeyCode.Return;
        [SerializeField] private bool drawResultOnGui = true;

        private CocktailEvaluator evaluator;
        private CocktailOrderEvaluator orderEvaluator;
        private CocktailOrderGenerator orderGenerator;
        private CocktailRecipeCatalog recipeCatalog;
        private CocktailOrderTemplateCatalog orderTemplateCatalog;
        private GeneratedCocktailOrder currentOrder;
        private string lastResultText = "Enter 키를 눌러 대상 잔을 판정하세요.";

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
            recipeCatalog = CocktailRecipeDataLoader.LoadDefault(itemCatalog);
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

            lastResultText = $"레시피 {recipeCatalog.Count}개와 주문 문장 {orderTemplateCatalog.Count}개를 불러왔습니다. {nextOrderKey} 키로 주문을 생성하세요.";
            Debug.Log($"[CocktailEvaluationTester] {lastResultText}");
        }

        public void GenerateNewOrder()
        {
            if (orderGenerator == null)
            {
                ItemDefCatalog itemCatalog = ItemDefCatalog.LoadFromResources(itemResourcesPath, additionalItems);
                recipeCatalog = CocktailRecipeDataLoader.LoadDefault(itemCatalog);
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
                lastResultText = $"레시피 {recipeCatalog.Count}개와 주문 문장 {orderTemplateCatalog.Count}개를 불러왔지만 주문을 생성하지 못했습니다.";
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
                lastResultText = "판정할 잔의 액체 추적기를 찾을 수 없습니다.";
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
                return $"진행 중인 주문이 없습니다. {nextOrderKey} 키로 새 주문을 생성하세요.";

            return "현재 주문\n"
                + currentOrder.line
                + "\n요청 레시피: "
                + currentOrder.RequestedRecipeName
                + $"\n제출: {submitKey} / 새 주문: {nextOrderKey}";
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
