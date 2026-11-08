#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Slainte.Bartending.FluidGpuExperiment;
using Slainte.Business;
using Slainte.Content;
using UnityEngine;
using UnityEngine.Rendering;

namespace Slainte.Bartending
{
    // 에디터 검증 전용. 실제 영업 씬을 사용하되 저장은 억제하고 시작 전 진행 상태로 복원한다.
    [DefaultExecutionOrder(-30000)]
    public sealed class GpuCraftingIntegrationProbe : MonoBehaviour
    {
        public bool Done { get; private set; }
        public bool manualInspection;
        public bool Passed { get; private set; }
        public string Failure { get; private set; }
        public readonly List<string> Checks = new List<string>();
        private BusinessFlowBootstrap flow;
        private BusinessBartendingBootstrap bootstrap;
        private PlaytestProgressIsolation isolation;
        private BusinessOrderFlowSettings testSettings;
        private bool measuringEntry;
        private readonly List<float> entryFrameTimes = new List<float>();
        private readonly List<float> craftingEntryFrameTimes = new List<float>();
        private readonly List<string> entryFrameDetails = new List<string>();
        private GameModeManager observedMode;
        private bool injectRenderClip;

        private void ApplyForeignRenderState(ScriptableRenderContext context, Camera camera)
        {
            if (!injectRenderClip || camera.name != "BartendingCamera") return;
            // 다른 카메라/에디터 패스가 남긴 작은 scissor 상태를 재현한다. 액체 합성은 이 상태에 의존하면 안 된다.
            using (var commands = new CommandBuffer())
            {
                commands.EnableScissorRect(new Rect(camera.pixelWidth * .35f, camera.pixelHeight * .35f,
                    camera.pixelWidth * .3f, camera.pixelHeight * .3f));
                context.ExecuteCommandBuffer(commands);
            }
        }

        private void Update()
        {
            if (measuringEntry)
            {
                float ms = Time.unscaledDeltaTime * 1000;
                entryFrameTimes.Add(ms);
                if (observedMode != null && observedMode.CurrentMode == GameMode.CraftingMode) craftingEntryFrameTimes.Add(ms);
                entryFrameDetails.Add($"{Time.frameCount},{ms:0.###},{observedMode?.CurrentMode},{flow?.OrderSessionController?.State}");
            }
        }

        private void Awake()
        {
            RenderPipelineManager.beginCameraRendering += ApplyForeignRenderState;
            observedMode = FindFirstObjectByType<GameModeManager>();
            isolation = PlaytestProgressIsolation.Attach(gameObject);
            flow = FindFirstObjectByType<BusinessFlowBootstrap>();
            if (flow == null) flow = gameObject.AddComponent<BusinessFlowBootstrap>();
            testSettings = Instantiate(BusinessOrderFlowSettings.LoadDefault());
            testSettings.autoStart = false;
            Require(flow.TryOverrideSettingsBeforeInitialization(testSettings), "Flow settings must be injected before initialization.");
        }

        private IEnumerator Start()
        {
            var tests = RunChecks();
            while (true)
            {
                bool more;
                try { more = tests.MoveNext(); }
                catch (Exception error)
                {
                    Failure = error + "\nCompleted checks:\n" + string.Join("\n", Checks);
                    Done = true;
                    Debug.LogException(error, this);
                    yield break;
                }
                if (!more) break;
                yield return tests.Current;
            }
            Passed = true;
            Done = true;
            isolation.RestoreNow();
            Debug.Log("[GpuCraftingIntegration] PASS: " + string.Join("; ", Checks));
        }

        private IEnumerator RunChecks()
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < deadline && (!flow.IsRuntimeReady || !isolation.IsReady)) yield return null;
            Require(flow.IsRuntimeReady && isolation.IsReady, "Scene services did not initialize.");
            // 실제 사용자는 시작 페이드가 끝난 뒤 조작한다. 전환용 BlackScreen은 올바른 입력 차단 UI다.
            deadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < deadline && SceneTransitionManager.Instance != null
                && SceneTransitionManager.Instance.fadeCanvasGroup != null
                && SceneTransitionManager.Instance.fadeCanvasGroup.blocksRaycasts) yield return null;
            Require(SceneTransitionManager.Instance == null || SceneTransitionManager.Instance.fadeCanvasGroup == null
                || !SceneTransitionManager.Instance.fadeCanvasGroup.blocksRaycasts, "The initial scene fade did not complete.");
            bootstrap = FindFirstObjectByType<BusinessBartendingBootstrap>();
            Require(bootstrap != null, "Canvas manufacturing bootstrap missing.");
            var items = ItemDefCatalog.LoadFromResources();
            var recipes = CocktailRecipeDataLoader.LoadDefault(items);
            var visitDatabase = CustomerVisitDatabase.LoadDefault();
            var authoredOrder = visitDatabase.visits.Where(v => v != null)
                .SelectMany(v => v.orders.Where(o => o?.order != null).Select(o => new { visit = v, order = o.order }))
                .FirstOrDefault(candidate => candidate.order.orderType == CocktailOrderType.RecipeOrder
                    && recipes.TryGet(candidate.order.requestedRecipeId, out var definition)
                    && definition.isOrderable && (definition.requiredTechnique & (CocktailTechnique.Stir | CocktailTechnique.Shake)) == 0);
            Require(authoredOrder != null, "An authored customer/build recipe order is needed for the integration fixture.");
            recipes.TryGet(authoredOrder.order.requestedRecipeId, out var recipe);
            var dialogue = FindFirstObjectByType<DialogueController>();

            BusinessOrderSessionResult result = null;
            var request = new OrderSessionRequest
            {
                sessionId = "gpu-integration-business", owner = OrderSessionOwner.Business,
                customerOrderKey = authoredOrder.order.key, customerVisitKey = authoredOrder.visit.visitKey,
                requestedRecipeId = recipe.id, presentOrder = true, presentFeedback = true,
                paymentCurrency = authoredOrder.order.paymentCurrency,
                applyProgressRewards = true, recordSale = true, clearCustomerOnComplete = true
            };
            int moneyBefore = GameProgress.Instance.CurrentMoney;
            int salesBefore = GameProgress.Instance.DayDrinkSalesCount;
            measuringEntry = true;
            Require(flow.OrderSessionController.BeginOrder(request, value => result = value), "Business order was rejected.");
            deadline = Time.realtimeSinceStartup + (manualInspection ? 600 : 30);
            while (Time.realtimeSinceStartup < deadline && !bootstrap.IsSessionReady)
            {
                if (!manualInspection && flow.OrderSessionController.State == BusinessOrderSessionState.PresentingOrder && dialogue.IsOpen)
                    dialogue.Advance();
                yield return null;
            }
            Require(bootstrap.IsSessionReady, "GPU session did not become ready.");
            Checks.Add("authored customer arrival/order dialogue enters GPU crafting");
            var session = bootstrap.ActiveSession;
            var firstRig = session.Rig;
            var firstTarget = session.Rig.inputCamera.targetTexture;
            if (manualInspection)
            {
                deadline = Time.realtimeSinceStartup + 600;
                while (Time.realtimeSinceStartup < deadline && result == null) yield return null;
                Require(result != null && result.outcome == OrderSessionOutcome.Served && !result.technicalFailure,
                    "Native UI inspection did not complete a submitted order.");
                Checks.Add("native UI order/selection/serving/customer feedback completed: " + result.grade);
                yield break;
            }
            for (int i = 0; i < 12; i++) yield return null;
            measuringEntry = false;
            entryFrameTimes.Sort();
            string entryTiming = $"entry frames: max={entryFrameTimes.Last():0.##} ms, p95={entryFrameTimes[(int)((entryFrameTimes.Count - 1) * .95f)]:0.##} ms";
            if (craftingEntryFrameTimes.Count > 0) entryTiming += $"; crafting-mode max={craftingEntryFrameTimes.Max():0.##} ms";
            System.IO.Directory.CreateDirectory("PlaytestEvidence/GpuCraftingIntegration20261104/TransitionRecheck");
            System.IO.File.WriteAllText("PlaytestEvidence/GpuCraftingIntegration20261104/TransitionRecheck/entry-timing.txt", entryTiming);
            System.IO.File.WriteAllLines("PlaytestEvidence/GpuCraftingIntegration20261104/TransitionRecheck/entry-frames.csv", entryFrameDetails);
            Checks.Add(entryTiming);
            session.Rig.Interactor.InputSuspended = true; // 재현 중 실제 마우스가 객체를 건드리지 않게 한다.
            session.Rig.Interactor.enabled = false; // 카메라 전환 완료가 InputSuspended를 풀어도 고정 입력에 OS 포인터가 섞이지 않게 한다.
            Require(session.GetComponentsInChildren<LiquidPool>().Length == 0
                && session.GetComponentsInChildren<GpuLiquidSystem>().Length == 0, "Legacy manufacturing was instantiated.");
            Checks.Add("GPU-only runtime in the authored Canvas");

            Require(BartendingViewport.TryGetInputScreenRect(out var screenRect), "Viewport rectangle missing.");
            var gameRect = Slainte.Shared.Display.GameAspect.GameScreenRect;
            Require(Vector2.Distance(screenRect.min, gameRect.min) < 1
                && Vector2.Distance(screenRect.max, gameRect.max) < 1, "Manufacturing does not fill the complete game area.");
            var front = FindFirstObjectByType<FrontCameraRig>().FrontWorld;
            Vector3 originalScale = front.localScale;
            Vector2 originalPosition = front.anchoredPosition;
            front.localScale *= 1.2f;
            front.anchoredPosition += new Vector2(80, 30);
            Canvas.ForceUpdateCanvases();
            bool staysFullScreen = BartendingViewport.TryGetInputScreenRect(out var zoomedRect)
                && Vector2.Distance(zoomedRect.min, gameRect.min) < 1
                && Vector2.Distance(zoomedRect.max, gameRect.max) < 1;
            front.localScale = originalScale;
            front.anchoredPosition = originalPosition;
            Canvas.ForceUpdateCanvases();
            Require(staysFullScreen, "Backdrop zoom or pan resized the manufacturing area.");
            Checks.Add("manufacturing fills the complete game area independently of backdrop zoom/pan");
            Require(session.Viewport.TryGetWorldRect(out var worldRect) && worldRect.width > 0 && worldRect.height > 0,
                "Full viewport geometry must include its maximum edges.");
            var screen = screenRect.center;
            Require(!session.Rig.Interactor.IsPointerBlocked(screen), "Passive scene graphics block manufacturing input. "
                + session.Rig.Interactor.DescribePointerBlockers(screen));
            Require(BartendingViewport.TryGetPointerWorldPosition(null, screen, out var world), "Canvas pointer mapping failed.");
            Require(Vector2.Distance(screen, BartendingViewport.GetPointerScreenPosition(null, world)) < 1, "Viewport round trip drifted.");
            Checks.Add("Canvas/world pointer round trip");
            var coverage = CheckLiquidRenderCoverage(session);
            while (coverage.MoveNext()) yield return coverage.Current;

            var shopCatalog = Resources.Load<LiquorShopCatalog>(ProjectResourcePaths.BartendingShopCatalog);
            var stock = shopCatalog != null ? shopCatalog.bottles.FirstOrDefault(d => d != null && d.item != null) : null;
            Require(stock != null, "Bottle inventory definition missing.");
            var shelfAndFalls = CheckShelfSizesAndFalls(session, shopCatalog);
            while (shelfAndFalls.MoveNext()) yield return shelfAndFalls.Current;
            var sharedReturnArea = CheckSharedReturnArea(session, stock);
            while (sharedReturnArea.MoveNext()) yield return sharedReturnArea.Current;
            GameProgress.Instance.SetBottleAmount(stock.InventoryId, stock.unitVolume * 2);
            float totalBefore = session.Supply.GetTotalAmount(stock);
            Require(bootstrap.TryTakeFromShelf(stock, screen, out var reason), reason);
            var bottle = session.Rig.Interactor.Held;
            Require(bottle != null && bottle.kind == LabItemKind.Bottle, "Shelf did not supply a GPU bottle.");
            Require(Mathf.Abs(session.Supply.GetTotalAmount(stock) - totalBefore) < .001f, "Taking a bottle changed total stock.");
            session.Rig.Interactor.BeginRotation();
            session.Rig.Interactor.RotateBy(110);
            bottle.ApplyHeldPose();
            float remainingBefore = bottle.remainingMl;
            // 방출은 각도 이력 캡처를 포함한 실제 월드 틱으로 재현한다.
            for (int i = 0; i < 25; i++) session.Rig.World.TickLiquid(.02f);
            float used = remainingBefore - bottle.remainingMl;
            Require(used > 0, "Bottle emission did not consume stock.");
            Require(Mathf.Abs(session.Supply.GetTotalAmount(stock) - (totalBefore - used)) < .01f, "Stock was not updated during emission.");
            session.Supply.SynchronizeConsumption();
            Require(Mathf.Abs(session.Supply.GetTotalAmount(stock) - (totalBefore - used)) < .01f, "GPU emission and total stock differ.");
            Require(bootstrap.TryReturnHeldBottleToShelf(out reason), reason);
            Require(Mathf.Abs(session.Supply.GetShelfAmount(stock) - (totalBefore - used)) < .01f, "Bottle return changed stock.");
            session.Supply.AddDeliveredBottle(stock);
            Require(Mathf.Abs(session.Supply.GetTotalAmount(stock) - Mathf.Min(stock.MaxAmount, totalBefore - used + stock.unitVolume)) < .01f, "Delivery stock differs.");
            Checks.Add("bottle supply/emission/return/delivery stock");

            var glassId = "glass_" + recipe.glassId;
            Require(bootstrap.TryPickUpCabinetItem(glassId, screen, out reason), reason);
            var glass = session.Rig.Interactor.Held;
            Require(glass != null && glass.kind == LabItemKind.Glass, "Cabinet did not supply a GPU glass.");
            var interaction = CheckBottleGlassInteraction(session, glass, stock);
            while (interaction.MoveNext()) yield return interaction.Current;
            var lowerSweep = CheckHeldGlassAtLowerEdge(session, glass);
            while (lowerSweep.MoveNext()) yield return lowerSweep.Current;
            var ingredient = recipe.ingredients[0].item;
            Require(session.Rig.Liquid.Fill(glass, ingredient, 5) > 0, "Storage fixture fill failed.");
            session.Rig.World.TickLiquid(.02f);
            session.Rig.Liquid.ReadbackNow();
            uint originalId = glass.Id;
            double originalMl = session.Rig.Liquid.IngredientsIn(glass.Id).Values.Sum();
            Require(session.Supply.TryReturnTool(out reason), reason);
            Require(glass.IsStored && !glass.gameObject.activeSelf, "Tool was not stored.");
            Require(bootstrap.TryPickUpCabinetItem(glassId, screen, out reason), reason);
            Require(session.Rig.Interactor.Held == glass && glass.Id == originalId, "Restoring the glass changed its identity.");
            Require(BartendingViewport.TryGetPointerWorldPosition(null, screen, out var restoredPoint)
                && Vector2.Distance(glass.Position, restoredPoint) < .01f,
                $"Restored glass missed its pointer: body={glass.Position}, expected={restoredPoint}.");
            session.Rig.Liquid.ReadbackNow();
            Require(Math.Abs(session.Rig.Liquid.IngredientsIn(glass.Id).Values.Sum() - originalMl) < .01, "Storage lost contents.");
            Checks.Add("filled glass storage/restoration preserves identity and ml");

            session.Rig.Liquid.ResetSimulation();
            Vector2 servingScreen = PlaceInServingArea(session, glass);
            foreach (var part in recipe.ingredients)
                Require(Math.Abs(session.Rig.Liquid.Fill(glass, part.item, part.targetMl) - part.targetMl) < .01, "Recipe fixture did not fit the glass.");
            Require(!session.TrySubmit(glass, out _), "Unprocessed GPU spawns were submitted.");
            session.Rig.World.TickLiquid(.02f);
            AddRequiredIce(session, glass, recipe);
            var renderedBefore = session.Rig.Liquid.RenderedSurfaceFrames;
            for (int i = 0; i < 8; i++) yield return null;
            Require(session.Rig.Liquid.RenderedSurfaceFrames > renderedBefore
                && string.IsNullOrEmpty(session.Rig.Liquid.SurfaceRenderingError), "GPU liquid was not rendered through the game camera.");
            Checks.Add("GPU surface rendered through the production RenderTexture camera");
            System.IO.Directory.CreateDirectory("PlaytestEvidence/GpuCraftingIntegration20261104/ProductionRecheck");
            ScreenCapture.CaptureScreenshot("PlaytestEvidence/GpuCraftingIntegration20261104/ProductionRecheck/production-good-cocktail.png");
            yield return null;
            Require(session.Rig.Interactor.ConsumePrimaryClick(servingScreen), "The serving click was not consumed. "
                + $"held={session.Rig.Interactor.Held}, position={glass.Position}, target={glass.TargetPosition}, screen={servingScreen}; "
                + session.Rig.Interactor.DescribePointerBlockers(servingScreen));
            deadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < deadline && result == null)
            {
                if (dialogue.IsOpen) dialogue.Advance();
                yield return null;
            }
            Require(result != null && result.outcome == OrderSessionOutcome.Served && !result.technicalFailure, "GPU result did not reach the business order.");
            Require(result.grade == OrderEvaluationGrade.Good, "The exact GPU recipe was not graded Good.");
            Require(GameProgress.Instance.CurrentMoney == moneyBefore + result.moneyDelta
                && GameProgress.Instance.DayDrinkSalesCount == salesBefore + 1, "Business payment/sale was not applied exactly once.");
            Require(!session.TrySubmit(glass, out _), "Duplicate submission was accepted.");
            Checks.Add("serving-area input/evaluation/customer feedback/payment/duplicate guard");
            yield return null;
            Require(bootstrap.ActiveSession == null && BartendingViewport.Active == null, "Manufacturing teardown leaked its viewport.");
            Require(bootstrap.IsGpuPrepared && firstRig.Ready && firstTarget.IsCreated(), "Standby released prepared GPU resources.");

            result = null;
            var node = new EpisodeNode { craftingOrderTarget = recipe.id, craftingPaymentEnabled = false };
            Require(EpisodeCraftingBridge.TryBuildRequest(node, "gpu-integration-episode", out var episodeRequest), "Episode request conversion failed.");
            Require(flow.StartEpisodeOrder(episodeRequest, value => result = value), "Episode manufacturing did not start.");
            deadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < deadline && !bootstrap.IsSessionReady) yield return null;
            Require(bootstrap.IsSessionReady, "The second GPU session did not initialize.");
            session = bootstrap.ActiveSession;
            Require(session.Rig == firstRig && session.Rig.inputCamera.targetTexture == firstTarget,
                "Repeated crafting reallocated its GPU rig or viewport target.");
            Checks.Add("prepared GPU rig/RenderTexture reused across business and episode sessions");
            session.Rig.Interactor.InputSuspended = true;
            session.Rig.Interactor.enabled = false;
            Require(bootstrap.TryPickUpCabinetItem(glassId, screen, out reason), reason);
            glass = session.Rig.Interactor.Held;
            foreach (var part in recipe.ingredients) session.Rig.Liquid.Fill(glass, part.item, part.targetMl);
            session.Rig.World.TickLiquid(.02f);
            AddRequiredIce(session, glass, recipe);
            int episodeMoney = GameProgress.Instance.CurrentMoney;
            Require(session.TrySubmit(glass, out reason), reason);
            Require(result != null && EpisodeCraftingResultMapper.Map(result) == CraftingJobResult.Good, "Episode result mapping failed.");
            Require(GameProgress.Instance.CurrentMoney == episodeMoney, "An unpaid episode granted business payment.");
            yield return null;
            Require(bootstrap.ActiveSession == null, "Episode teardown left a GPU session.");
            Checks.Add("episode request/result mapping and repeated session teardown");
        }

        // 호출 횟수만으로 화면 잘림을 놓치지 않도록 실제 카메라 출력의 아홉 위치에서 알파를 읽는다.
        // Exercise the production supply entrypoints so the Canvas measurements reach
        // the same instances players pick up. Stirring is reserved for the user's manual check.
        private IEnumerator CheckShelfSizesAndFalls(GpuCraftingSession session, LiquorShopCatalog catalog)
        {
            var ingredients = FindFirstObjectByType<IngredientSelectionUI>();
            var tools = FindFirstObjectByType<ToolSelectionUI>();
            BartendingViewport.TryGetInputScreenRect(out var screen);
            int bottles = 0, cabinetItems = 0;
            var details = new List<string>();
            void CheckHeight(FluidExperimentBody body, Vector2 expected, string id)
            {
                body.ApplyHeldPose();
                Physics2D.SyncTransforms();
                var renderer = BartendingNativeSpriteSizer.FindReferenceRenderer(body.gameObject);
                float height = renderer.bounds.size.y;
                Require(Mathf.Abs(height - expected.y) < .005f, $"Shelf size mismatch for {id}: {height} vs {expected.y}.");
                details.Add($"{id}: shelf={expected}, deployedHeight={height:0.####}, scale={body.transform.localScale}");
            }
            foreach (var definition in catalog.bottles)
            {
                if (definition == null || definition is GarnishDef
                    || !ingredients.TryGetShelfWorldSize(definition.InventoryId, out var expected)) continue;
                GameProgress.Instance.SetBottleAmount(definition.InventoryId, definition.unitVolume * 2);
                Require(bootstrap.TryTakeFromShelf(definition, screen.center, out var reason), reason);
                CheckHeight(session.Rig.Interactor.Held, expected, definition.InventoryId);
                Require(bootstrap.TryReturnHeldBottleToShelf(out reason), reason);
                bottles++;
            }
            var cabinet = Resources.Load<ToolCabinetCatalog>(ProjectResourcePaths.BartendingToolCabinetCatalog);
            var ids = cabinet.glasses.Where(d => d != null).Select(d => d.StableId)
                .Concat(cabinet.tools.Where(d => d != null).Select(d => d.StableId));
            foreach (string id in ids)
            {
                if (!tools.TryGetShelfWorldSize(id, out var expected)) continue;
                Require(bootstrap.TryPickUpCabinetItem(id, screen.center, out var reason), reason);
                var body = session.Rig.Interactor.Held;
                CheckHeight(body, expected, id);
                if (body.kind == LabItemKind.Jigger || body.kind == LabItemKind.Shaker)
                {
                    var labels = FindFirstObjectByType<BartendingInteractionOverlay>();
                    var labelIngredient = catalog.bottles.First(d => d != null && d.item != null).item;
                    session.Rig.Liquid.ResetSimulation();
                    float expectedTotal = 0;
                    for (int sample = 0; sample < 2; sample++)
                    {
                        const float amount = .5f;
                        float filled = session.Rig.Liquid.Fill(body, labelIngredient, amount);
                        Require(Mathf.Abs(filled - amount) < .001f, $"Volume label fixture did not fit {id}: {filled} ml.");
                        expectedTotal += amount;
                        session.Rig.World.TickLiquid(.02f);
                        session.Rig.Liquid.ReadbackNow();
                        yield return new WaitForSecondsRealtime(.15f);
                        string expectedMl = expectedTotal.ToString("0.#") + " ml";
                        Require(labels.TryGetGpuVesselContentsText(body, out var text) && text.EndsWith(expectedMl),
                            $"GPU volume label differs from contents: {id}, {text}.");
                    }
                    session.Rig.Liquid.ResetSimulation();
                    session.Rig.World.TickLiquid(.02f);
                    session.Rig.Liquid.ReadbackNow();
                    yield return new WaitForSecondsRealtime(.15f);
                    Require(labels.TryGetGpuVesselContentsText(body, out var empty) && empty.EndsWith("0 ml"), "Empty GPU vessel label was stale.");
                    Checks.Add(id + " live contents label updates 0.5 -> 1 -> 0 ml");
                }
                Vector3 scale = body.transform.localScale;
                Require(session.Supply.TryReturnTool(out reason), reason);
                if (body.kind == LabItemKind.Jigger || body.kind == LabItemKind.Shaker)
                {
                    yield return new WaitForSecondsRealtime(.15f);
                    Require(!FindFirstObjectByType<BartendingInteractionOverlay>().TryGetGpuVesselContentsText(body, out _),
                        "A stored GPU tool retained its volume label.");
                }
                Require(bootstrap.TryPickUpCabinetItem(id, screen.center, out reason), reason);
                Require(session.Rig.Interactor.Held == body && body.transform.localScale == scale, "Cabinet restore changed scale or identity.");
                Require(session.Supply.TryReturnTool(out reason), reason);
                cabinetItems++;
            }
            Require(bottles > 0 && cabinetItems == 7, $"Shelf coverage incomplete: {bottles} bottles, {cabinetItems} cabinet items.");
            session.Rig.Liquid.ResetSimulation();
            float floor = session.Rig.environment.table.bounds.max.y;
            var ingredient = catalog.bottles.First(d => d != null && d.item != null).item;
            var point = new Vector2(0, floor + .7f);
            for (int i = 0; i < 4; i++)
                Require(session.Rig.Liquid.TryEmit(point + Vector2.right * i * .08f, Vector2.zero,
                    ingredient, session.Rig.Liquid.ParticleVolumeMl, 0), "Spill fixture emission failed.");
            var ice = session.Rig.Spawn(session.Rig.World.Dispenser.icePrefab, point + Vector2.right * 2);
            ice.MarkPlaced();
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            session.Rig.Liquid.ReadbackNow();
            Require(session.Rig.Liquid.Snapshot.Any(p => p.Active != 0 && p.Position.y < floor - .1f),
                "Spilled liquid did not fall through the table.");
            Require(ice != null && ice.Position.y < floor - .1f, "Loose ice did not fall through the table.");
            for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();
            yield return null;
            session.Rig.Liquid.ReadbackNow();
            Require(session.Rig.Liquid.SnapshotTotalMl < .001f, "Offscreen spill did not drain.");
            Require(ice == null, "Offscreen loose ice was not retired.");
            const string folder = "PlaytestEvidence/GpuCraftingIntegration20261104/ShelfAndFalls20261108/";
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllLines(folder + "shelf-sizes.txt", details);
            Checks.Add($"shelf-sized {bottles} bottles and {cabinetItems} tools/glasses; restore scale stable; liquid/ice cross table and retire below bounds");
        }

        private IEnumerator CheckSharedReturnArea(GpuCraftingSession session, LiquorBottleDef stock)
        {
            var settings = Resources.Load<BusinessBartendingSettings>(ProjectResourcePaths.BartendingSettings);
            Rect area = Slainte.Shared.Display.GameAspect.NormalizedToScreenRect(settings.shelfReturnAreaNormalized);
            var ingredients = FindFirstObjectByType<IngredientSelectionUI>();
            var tools = FindFirstObjectByType<ToolSelectionUI>();
            Require(ingredients.ContainsReturnPoint(area.center) && tools.ContainsReturnPoint(area.center),
                "Bottle and tool returns do not share the configured rectangle.");
            foreach (var point in new[] { new Vector2(area.center.x, area.yMin - 5),
                new Vector2(area.center.x, area.yMax + 5), new Vector2(area.xMin - 5, area.center.y) })
                Require(!ingredients.ContainsReturnPoint(point) && !tools.ContainsReturnPoint(point),
                    "Return accepted outside the shared rectangle.");
            BartendingViewport.TryGetInputScreenRect(out var screen);
            Require(bootstrap.TryTakeFromShelf(stock, screen.center, out var reason), reason);
            Require(!BottleReturnZones.TryReturnHeldBottle(new Vector2(area.center.x, area.yMax + 5))
                && session.Rig.Interactor.Held != null, "Bottle returned above the rectangle.");
            Require(BottleReturnZones.TryReturnHeldBottle(area.center) && session.Rig.Interactor.Held == null,
                "Bottle did not return inside the rectangle.");
            yield return null;
            Require(bootstrap.TryPickUpCabinetItem("tool_jigger", screen.center, out reason), reason);
            Require(!ToolReturnZones.TryReturnHeldItem(new Vector2(area.center.x, area.yMin - 5),
                () => session.Supply.TryReturnTool(out _)) && session.Rig.Interactor.Held != null,
                "Tool returned below the rectangle.");
            Require(ToolReturnZones.TryReturnHeldItem(area.center, () => session.Supply.TryReturnTool(out _))
                && session.Rig.Interactor.Held == null, "Tool did not return inside the rectangle.");
            Checks.Add("one shared shelf return rectangle; bottles/tools return inside and remain held outside");
        }

        private IEnumerator CheckLiquidRenderCoverage(GpuCraftingSession session)
        {
            session.Viewport.TryGetWorldRect(out var rect);
            var liquid = session.Rig.Liquid;
            liquid.ResetSimulation();
            var ingredient = session.Rig.catalog.entries.First(e => e?.ingredient != null).ingredient;
            var points = new List<Vector2>();
            foreach (float y in new[] { .22f, .57f, .84f })
            foreach (float x in new[] { .17f, .53f, .81f })
            {
                Vector2 p = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, x), Mathf.Lerp(rect.yMin, rect.yMax, y));
                points.Add(p);
            }
            var camera = session.Rig.inputCamera;
            var originalTarget = camera.targetTexture;
            var image = session.Viewport.GetComponent<UnityEngine.UI.RawImage>();
            var originalImage = image.texture;
            var largerTarget = new RenderTexture(2560, 1440, 24, RenderTextureFormat.ARGB32);
            largerTarget.Create();
            camera.targetTexture = largerTarget;
            image.texture = largerTarget;
            injectRenderClip = true;
            var target = camera.targetTexture;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            string folder = "PlaytestEvidence/GpuCraftingIntegration20261104/TransitionRecheck/";
            var details = new List<string>();
            int visible = 0;
            foreach (Vector2 p in points)
            {
                // 한 위치씩 그려 반전된 대칭 격자가 우연히 통과하는 것도 막는다.
                liquid.ResetSimulation();
                for (int i = 0; i < 4; i++)
                    Require(liquid.TryEmit(p + new Vector2((i % 2) * .04f, (i / 2) * .04f), Vector2.zero, ingredient, liquid.ParticleVolumeMl, 0),
                        "Render-coverage particle emission failed.");
                session.Rig.World.SimulationSuspended = false;
                session.Rig.World.TickLiquid(.02f);
                session.Rig.World.SimulationSuspended = true;
                for (int i = 0; i < 8; i++) yield return null;
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                RenderTexture.active = previous;
                var uv = camera.WorldToViewportPoint(p);
                int cx = Mathf.RoundToInt(uv.x * target.width), cy = Mathf.RoundToInt(uv.y * target.height);
                int radius = Mathf.CeilToInt(target.height * .018f);
                int count = 0;
                for (int y = Mathf.Max(0, cy - radius); y < Mathf.Min(target.height, cy + radius); y++)
                for (int x = Mathf.Max(0, cx - radius); x < Mathf.Min(target.width, cx + radius); x++)
                {
                    var color = pixels.GetPixel(x, y);
                    if (color.a > .05f && color.r > color.b * 1.2f) count++;
                }
                if (count > 0) visible++;
                details.Add($"uv=({uv.x:0.##},{uv.y:0.##}), opaquePixels={count}");
                if (count == 0 || details.Count == points.Count)
                    System.IO.File.WriteAllBytes(folder + $"liquid-coverage-{details.Count}.png", pixels.EncodeToPNG());
            }
            var renderPrefab = session.Supply.FindBody(LabItemKind.Glass, "rock");
            var renderGlass = session.Rig.Spawn(renderPrefab, Vector2.zero,
                scale: session.Supply.GetShelfScale(renderPrefab, "glass_rock"));
            renderGlass.SetHeld(true);
            foreach (Vector2 p in points)
            {
                liquid.ResetSimulation();
                renderGlass.Teleport(p, 0);
                renderGlass.transform.SetPositionAndRotation(p, Quaternion.identity);
                Physics2D.SyncTransforms();
                Require(liquid.Fill(renderGlass, ingredient, 40) > 0, "Owned render-coverage fill failed.");
                session.Rig.World.SimulationSuspended = false;
                session.Rig.World.TickLiquid(.02f);
                session.Rig.World.SimulationSuspended = true;
                for (int i = 0; i < 8; i++) yield return null;
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                RenderTexture.active = previous;
                var uv = camera.WorldToViewportPoint(p);
                int cx = Mathf.RoundToInt(uv.x * target.width), cy = Mathf.RoundToInt(uv.y * target.height);
                int radius = Mathf.CeilToInt(target.height * .12f), count = 0;
                for (int y = Mathf.Max(0, cy - radius); y < Mathf.Min(target.height, cy + radius); y++)
                for (int x = Mathf.Max(0, cx - radius); x < Mathf.Min(target.width, cx + radius); x++)
                {
                    var color = pixels.GetPixel(x, y);
                    if (color.a > .05f && color.r > color.b * 1.2f) count++;
                }
                if (count > 0) visible++;
                details.Add($"owned uv=({uv.x:0.##},{uv.y:0.##}), liquidPixels={count}");
                if (count == 0) System.IO.File.WriteAllBytes(folder + $"owned-coverage-{details.Count}.png", pixels.EncodeToPNG());
            }
            renderGlass.gameObject.SetActive(false);
            Destroy(renderGlass.gameObject);
            Destroy(pixels);
            injectRenderClip = false;
            camera.targetTexture = originalTarget;
            image.texture = originalImage;
            largerTarget.Release();
            Destroy(largerTarget);
            session.Rig.World.SimulationSuspended = false;
            liquid.ResetSimulation();
            System.IO.File.WriteAllLines(folder + "liquid-coverage.txt", details);
            Require(visible == points.Count * 2, $"Liquid render coverage clipped: {visible}/{points.Count * 2}. " + string.Join("; ", details));
            Checks.Add("actual free/owned liquid alpha present separately at all 9 QHD viewport positions");
        }

        // 조성 직접 주입과 구분한다. 실제 병 emitter와 native 물리/GPU 틱만으로 잔에 들어오는 양을 검사한다.
        private IEnumerator CheckBottleGlassInteraction(GpuCraftingSession session, FluidExperimentBody glass, LiquorBottleDef stock)
        {
            session.Rig.Liquid.ResetSimulation();
            var input = session.Rig.Interactor;
            float floor = session.Rig.environment.table.bounds.max.y;
            float bottomOffset = glass.Position.y - glass.SolidBounds.min.y;
            glass.SetHeldPose(new Vector2(0, floor + bottomOffset + .02f), 0);
            glass.ApplyHeldPose();
            input.ReleaseWithVelocity(Vector2.zero);
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            Require(Mathf.Abs(glass.SolidBounds.min.y - floor) < .15f, "The released glass did not rest on the Canvas-mapped table.");
            float stockBefore = session.Supply.GetTotalAmount(stock);
            Require(session.Supply.TryTake(stock, glass.Position + new Vector2(2, 4), true, out var reason), reason);
            var bottle = input.Held;
            bottle.SetHeldPose(bottle.Position, 130);
            bottle.ApplyHeldPose();
            bottle.SynchronizeHistory();
            // 주둥이를 잔 입구 바로 위에 둔다. 이동 이력으로 인한 순간 분출은 고정 입력에서 제외한다.
            var nozzle = bottle.NozzleAt(1);
            var desiredNozzle = new Vector2(glass.SolidBounds.center.x + .15f, glass.SolidBounds.max.y + .15f);
            bottle.SetHeldPose(bottle.Position + desiredNozzle - nozzle, 130);
            bottle.ApplyHeldPose();
            bottle.SynchronizeHistory();
            Physics2D.SyncTransforms();
            foreach (var a in bottle.solidColliders)
            foreach (var b in glass.solidColliders)
                if (a != null && b != null && a.enabled && b.enabled)
                    Require(Physics2D.GetIgnoreCollision(a, b), "Held bottle must retain the original GPU overlap policy.");
            float remainingBefore = bottle.remainingMl;
            for (int i = 0; i < 50; i++)
            {
                yield return new WaitForFixedUpdate();
                if (i == 35)
                {
                    System.IO.Directory.CreateDirectory("PlaytestEvidence/GpuCraftingIntegration20261104/ProductionRecheck");
                    ScreenCapture.CaptureScreenshot("PlaytestEvidence/GpuCraftingIntegration20261104/ProductionRecheck/production-physical-pour.png");
                    yield return null;
                }
            }
            float emitted = remainingBefore - bottle.remainingMl;
            bottle.SetHeldPose(bottle.Position, 0);
            bottle.ApplyHeldPose();
            bottle.SynchronizeHistory();
            for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();
            session.Rig.Liquid.ReadbackNow();
            double received = session.Rig.Liquid.IngredientsIn(glass.Id).Values.Sum();
            Require(emitted > 0 && received > 0, $"Bottle-to-glass transfer failed: emitted={emitted}, received={received}.");
            Require(Math.Abs(session.Supply.GetTotalAmount(stock) - (stockBefore - emitted)) < .01,
                "Physical pouring and stock consumption differ.");
            Require(session.Rig.Liquid.TryBuildCocktailComposition(glass, session.Rig.World.Cocktails.ItemCatalog, out var composition, out reason), reason);
            Require(composition != null && Math.Abs(composition.TotalVolumeMl - received) < .01
                && Math.Abs(composition.GetVolume(bottle.ingredient) - received) < .01,
                "Physical poured amount/ingredient identity were unavailable to submission.");
            Checks.Add($"physical bottle-to-glass pour without Fill: emitted {emitted:0.###} ml, received {received:0.###} ml");
            input.ReleaseWithVelocity(Vector2.zero);
            foreach (var a in bottle.solidColliders)
            foreach (var b in glass.solidColliders)
                if (a != null && b != null && a.enabled && b.enabled)
                    Require(!Physics2D.GetIgnoreCollision(a, b), "Released bottle/glass solid contacts were not restored.");
            Require(input.Pick(bottle, bottle.Position), "The poured bottle could not be picked again.");
            bottle.SetHeldPose(glass.SolidBounds.center, 0);
            bottle.ApplyHeldPose();
            Physics2D.SyncTransforms();
            Require(input.Drop(glass.SolidBounds.center) && input.Held == glass,
                "Dropping a held bottle over the glass did not preserve the original item-swap interaction.");
            input.ReleaseWithVelocity(Vector2.zero);
            Require(input.Pick(bottle, bottle.Position), "The swapped bottle could not be picked again.");
            Require(session.Supply.TryReturnIngredient(out reason), reason);
            Require(input.Pick(glass, glass.Position), "The physically filled glass could not be picked up.");
            Checks.Add("bottle/glass held overlap policy, release contacts, overlapping item swap and filled-glass pickup");
        }

        // 한 번 부은 잔을 실제 집기 입력과 물리 틱으로 움직인다. 고정 위치 재충전으로 바닥 겹침을 숨기지 않는다.
        private IEnumerator CheckHeldGlassAtLowerEdge(GpuCraftingSession session, FluidExperimentBody glass)
        {
            var liquid = session.Rig.Liquid;
            var input = session.Rig.Interactor;
            var table = session.Rig.environment.table.bounds;
            var camera = session.Rig.inputCamera;
            var target = camera.targetTexture;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            Require(session.Viewport.TryGetWorldRect(out var viewport), "Lower-edge viewport mapping failed.");
            // 잔 위쪽을 잡고 화면 하단까지 움직이면 손이 뷰포트 안에 있어도 잔 바닥은 밖으로 나갈 수 있다.
            Vector2 grabPoint = new Vector2(glass.Position.x, glass.SolidBounds.max.y - .15f);
            input.ReleaseWithVelocity(Vector2.zero);
            Require(input.Pick(glass, grabPoint), "Lower-edge upper grab failed.");
            float grabOffset = grabPoint.y - glass.Position.y;
            float bottomOffset = glass.Position.y - glass.SolidBounds.min.y;
            liquid.ReadbackNow();
            double initialMl = liquid.IngredientsIn(glass.Id).Values.Sum();
            var rows = new List<string>();
            var counts = new List<int>();
            bool clearOfTable = true;
            bool retainedVolume = true;
            try
            {
                foreach (float clearance in new[] { .30f, .08f, -.10f, -.32f, -.55f, -.8f, -1.05f, -1.30f, -1.55f, -2f, float.NegativeInfinity, .30f })
                {
                    float targetY = Mathf.Max(viewport.yMin + .02f, table.max.y + bottomOffset + clearance + grabOffset);
                    while (Mathf.Abs(targetY - input.LastWorldPointer.y) > .001f)
                    {
                        float nextY = Mathf.MoveTowards(input.LastWorldPointer.y, targetY, .02f);
                        input.MoveHeld(new Vector2(input.LastWorldPointer.x, nextY));
                        yield return new WaitForFixedUpdate();
                    }
                    for (int i = 0; i < 12; i++) yield return new WaitForFixedUpdate();
                    yield return null;
                    yield return null;
                    liquid.ReadbackNow();
                    double ml = liquid.IngredientsIn(glass.Id).Values.Sum();
                    retainedVolume &= Math.Abs(ml - initialMl) < .01;
                    var bounds = glass.SolidBounds;
                    float actualClearance = bounds.min.y - table.max.y;
                    clearOfTable &= actualClearance >= -.005f;
                    var min = camera.WorldToViewportPoint(bounds.min);
                    var max = camera.WorldToViewportPoint(bounds.max);
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    pixels.Apply();
                    RenderTexture.active = previous;
                    int visible = 0;
                    for (int y = Mathf.Max(0, Mathf.FloorToInt(min.y * target.height)); y < Mathf.Min(target.height, Mathf.CeilToInt(max.y * target.height)); y++)
                    for (int x = Mathf.Max(0, Mathf.FloorToInt(min.x * target.width)); x < Mathf.Min(target.width, Mathf.CeilToInt(max.x * target.width)); x++)
                    {
                        var pixel = pixels.GetPixel(x, y);
                        if (pixel.a > .05f && pixel.r > pixel.b * 1.2f) visible++;
                    }
                    counts.Add(visible);
                    rows.Add($"requested={clearance:0.00}, actual={actualClearance:0.000}, screenY={camera.WorldToViewportPoint(input.LastWorldPointer).y:0.000}, volume={ml:0.###} ml, liquidPixels={visible}");
                }
            }
            finally { Destroy(pixels); }
            string folder = "PlaytestEvidence/GpuCraftingIntegration20261104/LowerEdgeRecheck";
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllLines(folder + "/held-glass-sweep.txt", rows);
            Require(initialMl > 0 && retainedVolume &&
                counts.All(n => n > 0 && n >= counts[0] / 2) && clearOfTable,
                "Held glass lower-edge regression: " + string.Join(" | ", rows));
            Checks.Add("poured glass retains volume and rendering while dragged to the worktop limit and raised");
        }

        private static Vector2 PlaceInServingArea(GpuCraftingSession session, FluidExperimentBody glass)
        {
            var target = FindFirstObjectByType<BartendingInteractionOverlay>();
            Require(target != null && target.TryGetServeTargetScreenRect(out _), "The actual customer's serving area is unavailable.");
            target.TryGetServeTargetScreenRect(out var rect);
            Require(BartendingViewport.TryGetPointerWorldPosition(null, rect.center, out var position), "Serving-area coordinates are outside the viewport.");
            session.Rig.Interactor.MoveHeld(position);
            glass.ApplyHeldPose();
            Physics2D.SyncTransforms();
            return rect.center;
        }

        private static void AddRequiredIce(GpuCraftingSession session, FluidExperimentBody glass, CocktailRecipe recipe)
        {
            if (IceRequirementRules.ResolvePresenceRule(recipe.iceRequirement, recipe.requiredIceCount) != IceRequirement.Required) return;
            // 고정 판정 입력을 만드는 구간이다. 집힌 잔의 입구 통과 규칙을 우회해 물리 통과를 주장하지 않는다.
            session.Rig.Interactor.ReleaseWithVelocity(Vector2.zero);
            var prefab = session.Supply.FindBody(LabItemKind.Ice);
            for (int i = 0; i < Mathf.Max(1, recipe.requiredIceCount); i++)
                session.Rig.Spawn(prefab, glass.LocalToWorld(glass.contentRegions[0].center) + Vector2.up * (.1f * i));
            Physics2D.SyncTransforms();
            session.Rig.World.RefreshIceContainment();
            Require(session.Rig.Interactor.Pick(glass, glass.Position), "Fixture glass could not be picked up again.");
        }

        private static void Require(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException(reason); }

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= ApplyForeignRenderState;
            if (testSettings != null) Destroy(testSettings);
        }
    }
}
#endif
