using System.Collections.Generic;
using System.IO;
using Slainte.Bartending;
using Slainte.Business;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Slainte.Editor
{
    public static class BusinessFlowSceneSetup
    {
        private enum SmokePhase
        {
            WaitingForOrder,
            WaitingForCrafting,
            WaitingForFeedback,
            WaitingForCompletion
        }

        private enum SmokeScenario
        {
            SubmitBad,
            ServeWithInventory,
            SequentialGrades,
            EpisodeResourceUse
        }

        private const string ScenePath = "Assets/BusinessScene.unity";
        private const string SettingsFolder = "Assets/Resources/Business";
        private const string SettingsPath = SettingsFolder + "/BusinessOrderFlowSettings.asset";
        private const string BartendingSettingsPath = "Assets/Resources/Bartending/BusinessBartendingSettings.asset";
        private const string VodkaItemPath = "Assets/Resources/Items/breeze_vodka.asset";
        private const string LemonItemPath = "Assets/Resources/Items/lemon_juice.asset";
        private const string LemonShelfPath = "Assets/Data/LiquorBottle/lemonJuice.asset";
        private const string VerticalOrderKey = "vertical_slice_vodka_lemon";
        private const string CustomerOrderPath =
            "Assets/Data/CustomerOrder/data/CustomerOrder_vertical_slice_vodka_lemon.asset";
        private const string OrderTicketPath =
            "Assets/Data/OrderTicket/data/OrderTicketData_vertical_slice_vodka_lemon.asset";
        private const string CustomerDatabasePath = "Assets/Data/CustomerOrder/CustomerOrderDatabase.asset";
        private const string TicketDatabasePath = "Assets/Data/OrderTicket/OrderTicketDatabase.asset";
        private static SmokePhase smokePhase;
        private static SmokeScenario smokeScenario;
        private static double smokeStartedAt;
        private static int smokeExitCode;
        private static bool smokeBaselinesCaptured;
        private static int smokeBaselineMoney;
        private static int smokeBaselineReputation;
        private static int smokeBaselineDay;
        private static int smokeBaselineBusinessIndex;
        private static int smokeBaselineBusinessCount;
        private static bool smokeBaselineBusinessCompleted;
        private static readonly List<BusinessOrderSessionResult> smokeOrderResults = new();
        private static BusinessOrderSessionController smokeObservedSession;
        private static float smokeExpectedVodkaCapacity;
        private static float smokeExpectedVodkaInventoryAmount;
        private static bool episodeOrderRequested;
        private static bool episodeOrderCompleted;
        private static BusinessOrderSessionResult episodeOrderResult;
        private static bool smokeOriginalAutoStart;
        private static bool smokeAutoStartOverridden;
        private static bool previousEnterPlayModeOptionsEnabled;
        private static EnterPlayModeOptions previousEnterPlayModeOptions;
        private static string smokeAutosavePath;
        private static string smokeAutosaveBackupPath;
        private static bool smokeAutosaveExisted;

        [MenuItem("Slainte/Business/Apply Business Flow Setup")]
        public static void Apply()
        {
            BusinessOrderFlowSettings flowSettings = EnsureFlowSettings();
            EnsureVerticalSliceOrderData();
            LiquorBottleDef lemonShelfDefinition = EnsureLemonShelfData();
            ConfigureBartendingSettings();
            ConfigureScene(flowSettings, lemonShelfDefinition);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BusinessFlowSceneSetup] Business flow setup applied.");
        }

        public static void ApplyFromCommandLine()
        {
            Apply();
        }

        public static void RunSmokeTestFromCommandLine()
        {
            StartSmokeTest(SmokeScenario.SubmitBad);
        }

        public static void RunServeGestureInventoryQaFromCommandLine()
        {
            StartSmokeTest(SmokeScenario.ServeWithInventory);
        }

        public static void RunSequentialGradeQaFromCommandLine()
        {
            StartSmokeTest(SmokeScenario.SequentialGrades);
        }

        public static void RunEpisodeResourceQaFromCommandLine()
        {
            StartSmokeTest(SmokeScenario.EpisodeResourceUse);
        }

        public static void RunDataQaFromCommandLine()
        {
            try
            {
                ValidateBusinessData();
                Debug.Log("[BusinessFlowDataQa] Business data and evaluation QA passed.");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[BusinessFlowDataQa] QA failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        private static void StartSmokeTest(SmokeScenario scenario)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            smokeScenario = scenario;
            smokePhase = SmokePhase.WaitingForOrder;
            smokeStartedAt = EditorApplication.timeSinceStartup;
            smokeExitCode = 1;
            smokeBaselinesCaptured = false;
            episodeOrderRequested = false;
            episodeOrderCompleted = false;
            episodeOrderResult = null;
            smokeOrderResults.Clear();
            smokeObservedSession = null;
            smokeExpectedVodkaCapacity = 0f;
            smokeExpectedVodkaInventoryAmount = 0f;
            smokeAutoStartOverridden = false;
            OverrideAutoStartForSmoke();
            PrepareSmokeAutosaveBackup();
            previousEnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            previousEnterPlayModeOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged -= HandleSmokePlayModeState;
            EditorApplication.playModeStateChanged += HandleSmokePlayModeState;
            EditorApplication.update -= TickSmokeTest;
            EditorApplication.update += TickSmokeTest;
            EditorApplication.EnterPlaymode();
        }

        private static void ValidateBusinessData()
        {
            BusinessOrderFlowSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(SettingsPath);
            AssertQa(settings != null, "Business order flow settings asset is missing.");
            AssertQa(settings.fixedOrders != null && settings.fixedOrders.Exists(order =>
                    order != null
                    && order.customerOrderKey == VerticalOrderKey
                    && order.requestedRecipeId == "vodka_lemon"),
                "The fixed Vodka Lemon order is not configured.");
            AssertQa(settings.midScoreThreshold < settings.goodScoreThreshold,
                "Evaluation thresholds are not ordered mid < good.");

            CustomerOrderDatabase customerDatabase =
                AssetDatabase.LoadAssetAtPath<CustomerOrderDatabase>(CustomerDatabasePath);
            CustomerOrderData customerOrder = customerDatabase != null
                ? customerDatabase.FindByKey(VerticalOrderKey)
                : null;
            AssertQa(customerOrder != null, "Customer order is not reachable from its database.");
            AssertQa(customerOrder.lines != null && customerOrder.lines.Count > 0,
                "Customer order dialogue is empty.");
            AssertQa(customerOrder.feedbackLinesGood != null && customerOrder.feedbackLinesGood.Count > 0,
                "Good feedback dialogue is empty.");
            AssertQa(customerOrder.feedbackLinesMid != null && customerOrder.feedbackLinesMid.Count > 0,
                "Mid feedback dialogue is empty.");
            AssertQa(customerOrder.feedbackLinesBad != null && customerOrder.feedbackLinesBad.Count > 0,
                "Bad feedback dialogue is empty.");

            OrderTicketDatabase ticketDatabase =
                AssetDatabase.LoadAssetAtPath<OrderTicketDatabase>(TicketDatabasePath);
            OrderTicketData ticket = ticketDatabase != null
                ? ticketDatabase.FindByKey(VerticalOrderKey)
                : null;
            AssertQa(ticket != null, "Order ticket is not reachable from its database.");
            AssertQa(ticket.items != null && ticket.items.Count == 2,
                "Vodka Lemon ticket must contain exactly two ingredients.");
            AssertQa(ticket.items[0].qty == 50 && ticket.items[1].qty == 30,
                "Vodka Lemon ticket quantities must be 50 ml and 30 ml.");

            BusinessBartendingSettings bartendingSettings =
                AssetDatabase.LoadAssetAtPath<BusinessBartendingSettings>(BartendingSettingsPath);
            AssertQa(bartendingSettings != null, "Business bartending settings asset is missing.");
            AssertQa(bartendingSettings.initialBottleItems == null
                    || bartendingSettings.initialBottleItems.Length == 0,
                "Business bartending must not auto-spawn shelf bottles.");

            LiquorBottleDef lemonShelfDefinition =
                AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(LemonShelfPath);
            AssertQa(lemonShelfDefinition != null && lemonShelfDefinition.id == "lemon_juice",
                "Lemon Juice is missing from the liquor shelf data.");

            BusinessDaySnapshot firstPlan = BusinessSequencePlanner.CreateFixed(3, settings);
            BusinessDaySnapshot secondPlan = BusinessSequencePlanner.CreateFixed(3, settings);
            AssertQa(firstPlan.HasEntries && !firstPlan.isCompleted,
                "The fixed business sequence did not create an order.");
            AssertQa(firstPlan.seed == secondPlan.seed,
                "The fixed business sequence seed is not deterministic.");
            BusinessDaySnapshot clonedPlan = firstPlan.Clone();
            string originalEntryId = firstPlan.entries[0].entryId;
            clonedPlan.entries[0].entryId = "qa_mutated_clone";
            AssertQa(firstPlan.entries[0].entryId == originalEntryId,
                "Business day snapshot clone shares mutable entry instances.");

            ItemDefCatalog itemCatalog = ItemDefCatalog.LoadFromResources("Items", null);
            AssertQa(itemCatalog.TryGet("breeze_vodka", out ItemDef vodka),
                "Breeze Vodka ItemDef is not loadable from Resources.");
            AssertQa(itemCatalog.TryGet("lemon_juice", out ItemDef lemon),
                "Lemon Juice ItemDef is not loadable from Resources.");
            CocktailRecipeCatalog recipeCatalog = CocktailRecipeCsvLoader.LoadFromStreamingAssets(
                itemCatalog,
                "Data",
                "recipes.csv",
                "recipe_ingredients.csv");
            AssertQa(recipeCatalog.TryGet("vodka_lemon", out CocktailRecipe recipe),
                "Vodka Lemon recipe could not be loaded from CSV.");
            AssertQa(recipe.ingredients.Count == 2,
                "Vodka Lemon recipe must resolve exactly two ingredients.");

            GeneratedCocktailOrder generatedOrder =
                new CocktailOrderGenerator(recipeCatalog, null).GenerateRecipeOrder("vodka_lemon");
            AssertQa(generatedOrder != null, "Vodka Lemon generated order could not be created.");
            CocktailOrderEvaluator evaluator = new CocktailOrderEvaluator(new CocktailEvaluator(recipeCatalog));

            CocktailComposition exact = new CocktailComposition();
            exact.Add(vodka, 50f);
            exact.Add(lemon, 30f);
            CocktailOrderEvaluationResult exactResult = evaluator.Evaluate(generatedOrder, exact);
            AssertQa(OrderEvaluationGrader.Resolve(exactResult, settings) == OrderEvaluationGrade.Good,
                "Exact 50/30 Vodka Lemon was not graded Good.");

            CocktailComposition imbalanced = new CocktailComposition();
            imbalanced.Add(vodka, 50f);
            imbalanced.Add(lemon, 50f);
            CocktailOrderEvaluationResult midResult = evaluator.Evaluate(generatedOrder, imbalanced);
            AssertQa(OrderEvaluationGrader.Resolve(midResult, settings) == OrderEvaluationGrade.Mid,
                "Imbalanced 50/50 Vodka Lemon was not graded Mid.");

            CocktailOrderEvaluationResult emptyResult = evaluator.Evaluate(
                generatedOrder,
                new CocktailComposition());
            AssertQa(OrderEvaluationGrader.Resolve(emptyResult, settings) == OrderEvaluationGrade.Bad,
                "Empty glass was not graded Bad.");
        }

        private static void AssertQa(bool condition, string message)
        {
            if (!condition)
                throw new System.InvalidOperationException(message);
        }

        private static BusinessOrderFlowSettings EnsureFlowSettings()
        {
            EnsureFolder(SettingsFolder);
            BusinessOrderFlowSettings settings = AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<BusinessOrderFlowSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            if (settings.fixedOrders == null)
                settings.fixedOrders = new List<FixedBusinessOrder>();
            settings.fixedOrders.RemoveAll(order => order != null
                && order.customerOrderKey == "yukari"
                && order.requestedRecipeId == "vodka_lemon");
            bool hasVerticalOrder = settings.fixedOrders.Exists(order => order != null
                && order.customerOrderKey == VerticalOrderKey);
            if (!hasVerticalOrder)
            {
                settings.fixedOrders.Add(new FixedBusinessOrder
                {
                    customerOrderKey = VerticalOrderKey,
                    requestedRecipeId = "vodka_lemon"
                });
            }

            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static LiquorBottleDef EnsureLemonShelfData()
        {
            ItemDef lemonItem = AssetDatabase.LoadAssetAtPath<ItemDef>(LemonItemPath);
            if (lemonItem == null)
                throw new System.InvalidOperationException("Lemon Juice ItemDef is missing.");

            LiquorBottleDef shelfDefinition =
                AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(LemonShelfPath);
            if (shelfDefinition == null)
            {
                shelfDefinition = ScriptableObject.CreateInstance<LiquorBottleDef>();
                AssetDatabase.CreateAsset(shelfDefinition, LemonShelfPath);
            }

            shelfDefinition.id = lemonItem.id;
            shelfDefinition.displayName = lemonItem.displayName;
            shelfDefinition.sprite = lemonItem.icon;
            shelfDefinition.unlockFlagKey = string.Empty;
            shelfDefinition.subCategory = "과일 주스";
            shelfDefinition.bottleCount = 6;
            shelfDefinition.unitVolume = lemonItem.capacityMl;
            EditorUtility.SetDirty(shelfDefinition);
            return shelfDefinition;
        }

        private static void EnsureVerticalSliceOrderData()
        {
            CustomerOrderData customerOrder = AssetDatabase.LoadAssetAtPath<CustomerOrderData>(CustomerOrderPath);
            if (customerOrder == null)
            {
                customerOrder = ScriptableObject.CreateInstance<CustomerOrderData>();
                AssetDatabase.CreateAsset(customerOrder, CustomerOrderPath);
            }

            customerOrder.key = VerticalOrderKey;
            customerOrder.characterKey = "yukari";
            customerOrder.expressionKeyMid = "mid";
            customerOrder.expressionKeyGood = "mid";
            customerOrder.expressionKeyBad = "mid";
            customerOrder.lines = new List<DialogueLine>
            {
                CreateLine("유카리", "좋은 저녁이야, 바텐더."),
                CreateLine("유카리", "오늘은 보드카 레몬으로 부탁해.")
            };
            customerOrder.feedbackLinesGood = new List<DialogueLine>
            {
                CreateLine("유카리", "완벽해. 딱 내가 원하던 맛이야.")
            };
            customerOrder.feedbackLinesMid = new List<DialogueLine>
            {
                CreateLine("유카리", "나쁘진 않은데, 균형이 조금 아쉽네.")
            };
            customerOrder.feedbackLinesBad = new List<DialogueLine>
            {
                CreateLine("유카리", "이건 내가 주문한 술이 아닌 것 같아.")
            };
            EditorUtility.SetDirty(customerOrder);

            OrderTicketData ticket = AssetDatabase.LoadAssetAtPath<OrderTicketData>(OrderTicketPath);
            if (ticket == null)
            {
                ticket = ScriptableObject.CreateInstance<OrderTicketData>();
                AssetDatabase.CreateAsset(ticket, OrderTicketPath);
            }

            ticket.key = VerticalOrderKey;
            ticket.customerName = "유카리";
            ticket.memo = "보드카 레몬: 보드카 50 ml, 레몬 주스 30 ml.";
            ticket.items = new List<OrderTicketItem>
            {
                new OrderTicketItem { name = "브리즈 보드카", qty = 50, price = 0 },
                new OrderTicketItem { name = "레몬 주스", qty = 30, price = 0 }
            };
            EditorUtility.SetDirty(ticket);

            CustomerOrderDatabase customerDatabase =
                AssetDatabase.LoadAssetAtPath<CustomerOrderDatabase>(CustomerDatabasePath);
            if (customerDatabase != null && !customerDatabase.customers.Contains(customerOrder))
            {
                customerDatabase.customers.Add(customerOrder);
                EditorUtility.SetDirty(customerDatabase);
            }

            OrderTicketDatabase ticketDatabase =
                AssetDatabase.LoadAssetAtPath<OrderTicketDatabase>(TicketDatabasePath);
            if (ticketDatabase != null && !ticketDatabase.orders.Contains(ticket))
            {
                ticketDatabase.orders.Add(ticket);
                EditorUtility.SetDirty(ticketDatabase);
            }
        }

        private static DialogueLine CreateLine(string speaker, string text)
        {
            return new DialogueLine
            {
                speakerName = speaker,
                text = text,
                nameColor = new Color(1f, 0.84f, 0.58f, 1f)
            };
        }

        private static void HandleSmokePlayModeState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                smokeStartedAt = EditorApplication.timeSinceStartup;
                return;
            }

            if (state != PlayModeStateChange.EnteredEditMode)
                return;

            EditorSettings.enterPlayModeOptionsEnabled = previousEnterPlayModeOptionsEnabled;
            EditorSettings.enterPlayModeOptions = previousEnterPlayModeOptions;
            RestoreAutoStartAfterSmoke();
            if (!RestoreSmokeAutosave())
                smokeExitCode = 1;
            AssetDatabase.SaveAssets();
            EditorApplication.playModeStateChanged -= HandleSmokePlayModeState;
            EditorApplication.update -= TickSmokeTest;
            EditorApplication.Exit(smokeExitCode);
        }

        private static void TickSmokeTest()
        {
            if (!EditorApplication.isPlaying)
                return;

            BusinessOrderSessionController session =
                Object.FindFirstObjectByType<BusinessOrderSessionController>();
            double timeoutSeconds = smokeScenario == SmokeScenario.SequentialGrades ? 45d : 20d;
            if (EditorApplication.timeSinceStartup - smokeStartedAt > timeoutSeconds)
            {
                string sessionState = session != null ? session.State.ToString() : "missing";
                FinishSmokeTest(false,
                    $"Business flow smoke test timed out. Scenario={smokeScenario}, "
                    + $"Phase={smokePhase}, SessionState={sessionState}.");
                return;
            }

            DialogueController dialogue = Object.FindFirstObjectByType<DialogueController>();
            if (session == null || dialogue == null)
                return;

            if (smokeObservedSession != session)
            {
                if (smokeObservedSession != null)
                    smokeObservedSession.OrderCompleted -= HandleSmokeOrderCompleted;
                smokeObservedSession = session;
                smokeObservedSession.OrderCompleted += HandleSmokeOrderCompleted;
            }

            if (!smokeBaselinesCaptured)
            {
                GameProgress progress = GameProgress.Instance;
                if (progress == null)
                    return;

                smokeBaselineMoney = progress.Money;
                smokeBaselineReputation = progress.Reputation;
                smokeBaselineDay = progress.CurrentDay;
                BusinessDaySnapshot baselineSnapshot = progress.GetBusinessDaySnapshot();
                smokeBaselineBusinessIndex = baselineSnapshot.currentIndex;
                smokeBaselineBusinessCount = baselineSnapshot.entries?.Count ?? 0;
                smokeBaselineBusinessCompleted = baselineSnapshot.isCompleted;
                smokeBaselinesCaptured = true;
            }

            switch (smokePhase)
            {
                case SmokePhase.WaitingForOrder:
                    if (smokeScenario == SmokeScenario.EpisodeResourceUse)
                    {
                        TryStartEpisodeResourceOrder();
                        break;
                    }

                    if (session.State == BusinessOrderSessionState.Idle)
                    {
                        BusinessFlowBootstrap bootstrap =
                            Object.FindFirstObjectByType<BusinessFlowBootstrap>();
                        if (bootstrap != null && bootstrap.IsRuntimeReady)
                            bootstrap.StartBusinessSequence();
                    }
                    else if (session.State == BusinessOrderSessionState.PresentingOrder)
                    {
                        AdvanceDialogue(dialogue);
                    }
                    else if (session.State == BusinessOrderSessionState.Crafting)
                    {
                        smokePhase = SmokePhase.WaitingForCrafting;
                    }
                    break;

                case SmokePhase.WaitingForCrafting:
                    if (session.State == BusinessOrderSessionState.Crafting)
                    {
                        BusinessBartendingBootstrap bartending =
                            Object.FindFirstObjectByType<BusinessBartendingBootstrap>();
                        if (bartending != null && bartending.CurrentTargetTracker != null)
                        {
                            if (SessionButtonExists("수락Button")
                                || SessionButtonExists("거절Button")
                                || SessionButtonExists("주문 포기Button")
                                || SessionButtonExists("제출Button")
                                || SessionButtonExists("버리기Button"))
                            {
                                FinishSmokeTest(false,
                                    "An order action button was exposed during gesture-only crafting.");
                                break;
                            }

                            if (smokeScenario == SmokeScenario.EpisodeResourceUse)
                            {
                                if (!PrepareEpisodeResourceUse(bartending, out string failure))
                                {
                                    FinishSmokeTest(false, failure);
                                    break;
                                }

                                if (!DragGlassForward(bartending, out string serveFailure))
                                {
                                    FinishSmokeTest(false, serveFailure);
                                    break;
                                }

                                smokePhase = SmokePhase.WaitingForFeedback;
                            }
                            else if (smokeScenario == SmokeScenario.SequentialGrades)
                            {
                                OrderEvaluationGrade expectedGrade = GetSequentialExpectedGrade(
                                    smokeOrderResults.Count);
                                if (!PopulateSampleGlass(
                                        bartending.CurrentTargetTracker,
                                        expectedGrade,
                                        out string populationFailure))
                                {
                                    FinishSmokeTest(false, populationFailure);
                                    break;
                                }

                                if (!DragGlassForward(bartending, out string gradeServeFailure))
                                {
                                    FinishSmokeTest(false, gradeServeFailure);
                                    break;
                                }

                                smokePhase = SmokePhase.WaitingForFeedback;
                            }
                            else if (smokeScenario == SmokeScenario.SubmitBad)
                            {
                                if (!DragGlassForward(bartending, out string submissionFailure))
                                {
                                    FinishSmokeTest(false, submissionFailure);
                                    break;
                                }

                                smokePhase = SmokePhase.WaitingForFeedback;
                            }
                            else
                            {
                                BottleController vodkaBottle = FindBottle("breeze_vodka");
                                if (vodkaBottle == null)
                                {
                                    if (bartending.SessionBottleCount != 0)
                                    {
                                        FinishSmokeTest(false,
                                            "A bottle auto-spawned before the liquor shelf was used.");
                                        break;
                                    }

                                    bool vodkaSelected =
                                        ClickShelfBottle("breeze_vodka", out string vodkaFailure);
                                    if (!vodkaSelected)
                                    {
                                        FinishSmokeTest(false,
                                            "Liquor shelf click failed: " + vodkaFailure);
                                        break;
                                    }

                                    vodkaBottle = FindBottle("breeze_vodka");
                                }

                                if (vodkaBottle == null || bartending.SessionBottleCount != 1)
                                {
                                    FinishSmokeTest(false,
                                        "Liquor shelf selection did not create the vodka bottle.");
                                    break;
                                }

                                LiquorBottleSlotUI vodkaShelfSlot = FindShelfBottleSlot("breeze_vodka");
                                float defaultInventoryAmount =
                                    vodkaShelfSlot != null && vodkaShelfSlot.Definition != null
                                        ? vodkaShelfSlot.Definition.MaxAmount
                                        : vodkaBottle.CurrentCapacity;
                                float inventoryAmount = GameProgress.Instance.GetBottleAmount(
                                    "breeze_vodka",
                                    defaultInventoryAmount);
                                smokeExpectedVodkaCapacity = Mathf.Max(0f, vodkaBottle.CurrentCapacity - 10f);
                                smokeExpectedVodkaInventoryAmount = Mathf.Max(0f, inventoryAmount - 10f);
                                vodkaBottle.SetCurrentCapacity(smokeExpectedVodkaCapacity, true);
                                if (!DragGlassForward(bartending, out string serveFailure))
                                {
                                    FinishSmokeTest(false, serveFailure);
                                    break;
                                }

                                smokePhase = SmokePhase.WaitingForFeedback;
                            }
                        }
                    }
                    break;

                case SmokePhase.WaitingForFeedback:
                    if (session.State == BusinessOrderSessionState.PresentingFeedback)
                    {
                        AdvanceDialogue(dialogue);
                    }
                    else if (smokeScenario == SmokeScenario.SequentialGrades
                        && (session.State == BusinessOrderSessionState.PresentingOrder
                            || session.State == BusinessOrderSessionState.Crafting))
                    {
                        smokePhase = SmokePhase.WaitingForOrder;
                    }
                    else if (session.State == BusinessOrderSessionState.Completed)
                    {
                        smokePhase = smokeScenario == SmokeScenario.SequentialGrades
                            && smokeOrderResults.Count < 3
                                ? SmokePhase.WaitingForOrder
                                : SmokePhase.WaitingForCompletion;
                    }
                    break;

                case SmokePhase.WaitingForCompletion:
                    if (smokeScenario == SmokeScenario.EpisodeResourceUse)
                    {
                        if (!episodeOrderCompleted)
                            break;

                        if (TryValidateEpisodeResourceScenario(session, out string episodeFailure))
                            FinishSmokeTest(true, "EpisodeResourceUse QA passed.");
                        else
                            FinishSmokeTest(false, episodeFailure);
                        break;
                    }

                    BusinessDaySnapshot snapshot = GameProgress.Instance?.GetBusinessDaySnapshot();
                    if (snapshot != null && snapshot.isCompleted)
                    {
                        if (TryValidateCompletedScenario(session, snapshot, out string failure))
                            FinishSmokeTest(true, smokeScenario + " QA passed.");
                        else
                            FinishSmokeTest(false, failure);
                    }
                    break;
            }
        }

        private static void TryStartEpisodeResourceOrder()
        {
            if (episodeOrderRequested)
            {
                smokePhase = SmokePhase.WaitingForCrafting;
                return;
            }

            BusinessFlowBootstrap bootstrap = Object.FindFirstObjectByType<BusinessFlowBootstrap>();
            if (bootstrap == null || !bootstrap.IsRuntimeReady)
                return;

            var request = new OrderSessionRequest
            {
                sessionId = "qa_episode_resource_use",
                owner = OrderSessionOwner.Episode,
                requestedRecipeId = "vodka_lemon",
                ticketKey = VerticalOrderKey,
                presentOrder = false,
                applyProgressRewards = false,
                clearCustomerOnComplete = false
            };

            episodeOrderRequested = bootstrap.StartEpisodeOrder(request, result =>
            {
                episodeOrderResult = result;
                episodeOrderCompleted = true;
                GameModeManager modeManager = Object.FindFirstObjectByType<GameModeManager>();
                modeManager?.RequestModeChange(GameMode.EpisodeMode);
                DataManager.Instance?.Save();
            });
        }

        private static bool PrepareEpisodeResourceUse(
            BusinessBartendingBootstrap bartending,
            out string failure)
        {
            failure = string.Empty;
            BottleController vodkaBottle = FindBottle("breeze_vodka");
            if (vodkaBottle == null)
            {
                if (bartending.SessionBottleCount != 0)
                {
                    failure = "A bottle auto-spawned before the episode order used the liquor shelf.";
                    return false;
                }

                if (!ClickShelfBottle("breeze_vodka", out string shelfFailure))
                {
                    failure = "Episode order could not select vodka from the liquor shelf: " + shelfFailure;
                    return false;
                }

                vodkaBottle = FindBottle("breeze_vodka");
            }

            if (vodkaBottle == null)
            {
                failure = "Episode order did not create the selected vodka bottle.";
                return false;
            }

            LiquorBottleSlotUI vodkaShelfSlot = FindShelfBottleSlot("breeze_vodka");
            float defaultInventoryAmount =
                vodkaShelfSlot != null && vodkaShelfSlot.Definition != null
                    ? vodkaShelfSlot.Definition.MaxAmount
                    : vodkaBottle.CurrentCapacity;
            float inventoryAmount = GameProgress.Instance.GetBottleAmount(
                "breeze_vodka",
                defaultInventoryAmount);
            smokeExpectedVodkaCapacity = Mathf.Max(0f, vodkaBottle.CurrentCapacity - 10f);
            smokeExpectedVodkaInventoryAmount = Mathf.Max(0f, inventoryAmount - 10f);
            vodkaBottle.SetCurrentCapacity(smokeExpectedVodkaCapacity, true);
            return true;
        }

        private static bool TryValidateEpisodeResourceScenario(
            BusinessOrderSessionController session,
            out string failure)
        {
            failure = string.Empty;
            GameProgress progress = GameProgress.Instance;
            if (progress == null)
            {
                failure = "GameProgress became unavailable during episode resource validation.";
                return false;
            }

            if (episodeOrderResult == null
                || episodeOrderResult.owner != OrderSessionOwner.Episode
                || episodeOrderResult.outcome != OrderSessionOutcome.Served)
            {
                failure = "Episode order did not return the expected episode-owned served result.";
                return false;
            }

            if (session.State != BusinessOrderSessionState.Completed)
            {
                failure = "Episode order session did not reach Completed state.";
                return false;
            }

            if (!Mathf.Approximately(
                    progress.GetBottleAmount("breeze_vodka", -1f),
                    smokeExpectedVodkaInventoryAmount))
            {
                failure = "Episode order did not retain the consumed bottle inventory.";
                return false;
            }

            if (progress.Money != smokeBaselineMoney || progress.Reputation != smokeBaselineReputation)
            {
                failure = "Episode order incorrectly applied business money or reputation rewards.";
                return false;
            }

            BusinessDaySnapshot snapshot = progress.GetBusinessDaySnapshot();
            int entryCount = snapshot.entries?.Count ?? 0;
            if (progress.CurrentDay != smokeBaselineDay
                || snapshot.currentIndex != smokeBaselineBusinessIndex
                || entryCount != smokeBaselineBusinessCount
                || snapshot.isCompleted != smokeBaselineBusinessCompleted)
            {
                failure = "Episode order incorrectly changed the day or business sequence snapshot.";
                return false;
            }

            GameModeManager modeManager = Object.FindFirstObjectByType<GameModeManager>();
            if (modeManager != null && modeManager.CurrentMode != GameMode.EpisodeMode)
            {
                failure = "Episode order did not return control to EpisodeMode.";
                return false;
            }

            return true;
        }

        private static BottleController FindBottle(string itemId)
        {
            BottleController[] bottles = Object.FindObjectsByType<BottleController>(FindObjectsSortMode.None);
            for (int i = 0; i < bottles.Length; i++)
            {
                ItemDef item = bottles[i] != null ? bottles[i].BottleData : null;
                if (item != null && string.Equals(item.id, itemId, System.StringComparison.OrdinalIgnoreCase))
                    return bottles[i];
            }

            return null;
        }

        private static bool DragGlassForward(
            BusinessBartendingBootstrap bartending,
            out string failure)
        {
            VesselLiquidTracker tracker = bartending != null ? bartending.CurrentTargetTracker : null;
            GlassController glass = tracker != null ? tracker.GetComponent<GlassController>() : null;
            if (glass == null)
            {
                failure = "The serving glass could not be found. ";
                return false;
            }

            Vector2 forwardPosition = new Vector2(Screen.width * 0.5f, Screen.height + 1f);
            if (glass.TryRequestServeAtScreenPosition(forwardPosition, false))
            {
                failure = "The glass was served without an active drag gesture. ";
                return false;
            }

            if (!glass.TryRequestServeAtScreenPosition(forwardPosition, true))
            {
                failure = "Dragging the glass forward did not request serving. ";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static OrderEvaluationGrade GetSequentialExpectedGrade(int orderIndex)
        {
            return orderIndex switch
            {
                0 => OrderEvaluationGrade.Good,
                1 => OrderEvaluationGrade.Mid,
                _ => OrderEvaluationGrade.Bad
            };
        }

        private static bool PopulateSampleGlass(
            VesselLiquidTracker tracker,
            OrderEvaluationGrade expectedGrade,
            out string failure)
        {
            failure = string.Empty;
            if (tracker == null)
            {
                failure = "The serving glass tracker is missing. ";
                return false;
            }

            ItemDefCatalog itemCatalog = ItemDefCatalog.LoadFromResources("Items", null);
            if (!itemCatalog.TryGet("breeze_vodka", out ItemDef vodka)
                || !itemCatalog.TryGet("lemon_juice", out ItemDef lemon))
            {
                failure = "The sample Vodka Lemon ingredients could not be loaded. ";
                return false;
            }

            if (expectedGrade == OrderEvaluationGrade.Good)
            {
                if (!CreateQaLiquidParticle(tracker, vodka, 50f, -0.02f, out failure)
                    || !CreateQaLiquidParticle(tracker, lemon, 30f, 0.02f, out failure))
                {
                    return false;
                }
            }
            else if (expectedGrade == OrderEvaluationGrade.Mid)
            {
                if (!CreateQaLiquidParticle(tracker, vodka, 50f, -0.02f, out failure)
                    || !CreateQaLiquidParticle(tracker, lemon, 50f, 0.02f, out failure))
                {
                    return false;
                }
            }

            Physics2D.SyncTransforms();
            CocktailComposition composition = tracker.BuildComposition();
            CocktailRecipeCatalog recipeCatalog = CocktailRecipeCsvLoader.LoadFromStreamingAssets(
                itemCatalog,
                "Data",
                "recipes.csv",
                "recipe_ingredients.csv");
            GeneratedCocktailOrder order =
                new CocktailOrderGenerator(recipeCatalog, null).GenerateRecipeOrder("vodka_lemon");
            CocktailOrderEvaluationResult evaluation =
                new CocktailOrderEvaluator(new CocktailEvaluator(recipeCatalog)).Evaluate(order, composition);
            BusinessOrderFlowSettings flowSettings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(SettingsPath);
            OrderEvaluationGrade actualGrade = OrderEvaluationGrader.Resolve(evaluation, flowSettings);
            if (actualGrade != expectedGrade)
            {
                failure = $"Sample glass grade mismatch: expected {expectedGrade}, actual {actualGrade}. ";
                return false;
            }

            return true;
        }

        private static bool CreateQaLiquidParticle(
            VesselLiquidTracker tracker,
            ItemDef item,
            float volumeMl,
            float horizontalOffset,
            out string failure)
        {
            Collider2D[] vesselColliders = tracker.GetComponentsInChildren<Collider2D>();
            Collider2D trigger = System.Array.Find(
                vesselColliders,
                collider => collider != null && collider.isTrigger);
            if (trigger == null)
            {
                failure = "The serving glass has no liquid tracking trigger. ";
                return false;
            }

            GameObject particleObject = new GameObject("QA_Liquid_" + item.id);
            particleObject.transform.SetParent(tracker.transform.parent, true);
            particleObject.transform.position = trigger.bounds.center + new Vector3(horizontalOffset, 0f, 0f);
            particleObject.layer = tracker.gameObject.layer;

            Rigidbody2D body = particleObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            CircleCollider2D particleCollider = particleObject.AddComponent<CircleCollider2D>();
            particleCollider.radius = 0.01f;
            LiquidParticleData particle = particleObject.AddComponent<LiquidParticleData>();
            particle.SetPayload(item, volumeMl);

            failure = string.Empty;
            return true;
        }

        private static bool SessionButtonExists(string objectName)
        {
            Button[] buttons =
                Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].gameObject.name == objectName)
                    return true;
            }

            return false;
        }

        private static LiquorBottleSlotUI FindShelfBottleSlot(string itemId)
        {
            LiquorBottleSlotUI[] slots =
                Object.FindObjectsByType<LiquorBottleSlotUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < slots.Length; i++)
            {
                LiquorBottleDef definition = slots[i] != null ? slots[i].Definition : null;
                if (definition != null
                    && string.Equals(definition.id, itemId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return slots[i];
                }
            }

            return null;
        }

        private static bool ClickShelfBottle(string itemId, out string failure)
        {
            LiquorBottleSlotUI shelfSlot = FindShelfBottleSlot(itemId);
            if (shelfSlot == null)
            {
                failure = itemId + " shelf slot is missing. ";
                return false;
            }

            BusinessBartendingBootstrap bartending =
                Object.FindFirstObjectByType<BusinessBartendingBootstrap>();
            if (bartending == null)
            {
                failure = "Bartending session is missing. ";
                return false;
            }

            if (!bartending.TryPlaceBottleFromShelf(shelfSlot.Definition, out string placementFailure))
            {
                failure = itemId + " could not be selected from the liquor shelf: "
                    + placementFailure + " ";
                return false;
            }

            if (FindBottle(itemId) == null)
            {
                failure = itemId + " did not create a table bottle. ";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool TryValidateCompletedScenario(
            BusinessOrderSessionController session,
            BusinessDaySnapshot snapshot,
            out string failure)
        {
            failure = string.Empty;
            GameProgress progress = GameProgress.Instance;
            BusinessOrderFlowSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(SettingsPath);
            if (progress == null || settings == null)
            {
                failure = "GameProgress or flow settings became unavailable during completion validation.";
                return false;
            }

            if (session.State != BusinessOrderSessionState.Completed)
            {
                failure = "Order session did not reach Completed state.";
                return false;
            }

            if (snapshot.entries == null || snapshot.currentIndex != snapshot.entries.Count)
            {
                failure = "Business sequence index did not advance to the end.";
                return false;
            }

            if (progress.CurrentDay != snapshot.day + 1)
            {
                failure = $"Day did not advance after business completion: business day {snapshot.day}, "
                    + $"current day {progress.CurrentDay}.";
                return false;
            }

            if (GameManager.Instance.CurrentState != GameState.Rest)
            {
                failure = $"Game state did not advance to Rest after business completion: "
                    + $"{GameManager.Instance.CurrentState}.";
                return false;
            }

            int expectedResultCount = smokeScenario == SmokeScenario.SequentialGrades ? 3 : 1;
            if (smokeOrderResults.Count != expectedResultCount)
            {
                failure = $"Completed order result count mismatch: expected {expectedResultCount}, "
                    + $"actual {smokeOrderResults.Count}.";
                return false;
            }

            for (int i = 0; i < smokeOrderResults.Count; i++)
            {
                BusinessOrderSessionResult result = smokeOrderResults[i];
                OrderEvaluationGrade expectedGrade = smokeScenario == SmokeScenario.SequentialGrades
                    ? GetSequentialExpectedGrade(i)
                    : OrderEvaluationGrade.Bad;
                if (result == null
                    || result.owner != OrderSessionOwner.Business
                    || result.outcome != OrderSessionOutcome.Served
                    || !result.accepted
                    || result.grade != expectedGrade)
                {
                    failure = $"Order result {i + 1} did not match the expected served "
                        + $"{expectedGrade} result.";
                    return false;
                }

                int expectedResultMoney = settings.GetMoneyReward(expectedGrade);
                int expectedResultReputation = settings.GetReputationReward(expectedGrade);
                if (result.moneyDelta != expectedResultMoney
                    || result.reputationDelta != expectedResultReputation)
                {
                    failure = $"Order result {i + 1} reward mismatch: expected "
                        + $"{expectedResultMoney}/{expectedResultReputation}, actual "
                        + $"{result.moneyDelta}/{result.reputationDelta}.";
                    return false;
                }
            }

            int expectedMoney = smokeBaselineMoney;
            int expectedReputation = smokeBaselineReputation;
            if (smokeScenario == SmokeScenario.SequentialGrades)
            {
                for (int i = 0; i < 3; i++)
                {
                    OrderEvaluationGrade grade = GetSequentialExpectedGrade(i);
                    expectedMoney = Mathf.Max(0, expectedMoney + settings.GetMoneyReward(grade));
                    expectedReputation += settings.GetReputationReward(grade);
                }
            }
            else if (smokeScenario == SmokeScenario.SubmitBad
                || smokeScenario == SmokeScenario.ServeWithInventory)
            {
                expectedMoney = Mathf.Max(0, expectedMoney + settings.badMoneyReward);
                expectedReputation += settings.badReputationReward;
            }

            if (progress.Money != expectedMoney || progress.Reputation != expectedReputation)
            {
                failure = $"Reward mismatch: expected money/reputation {expectedMoney}/{expectedReputation}, "
                    + $"actual {progress.Money}/{progress.Reputation}.";
                return false;
            }

            if (smokeScenario == SmokeScenario.ServeWithInventory
                && !Mathf.Approximately(
                    progress.GetBottleAmount("breeze_vodka", -1f),
                    smokeExpectedVodkaInventoryAmount))
            {
                failure = "Forward glass serving did not retain the consumed bottle amount.";
                return false;
            }

            BusinessBartendingBootstrap bartending =
                Object.FindFirstObjectByType<BusinessBartendingBootstrap>();
            if (bartending != null && bartending.CurrentTargetTracker != null)
            {
                failure = "Bartending session was not cleaned up after order completion.";
                return false;
            }

            GameModeManager modeManager = Object.FindFirstObjectByType<GameModeManager>();
            if (modeManager != null && modeManager.CurrentMode != GameMode.OrderMode)
            {
                failure = "Game mode did not return to OrderMode after order completion.";
                return false;
            }

            CustomerSpawner customerSpawner = Object.FindFirstObjectByType<CustomerSpawner>();
            if (customerSpawner != null && customerSpawner.CurrentOrderData != null)
            {
                failure = "Customer order data was not cleared after completion.";
                return false;
            }

            return true;
        }

        private static void AdvanceDialogue(DialogueController dialogue)
        {
            if (!dialogue.SkipTypingIfNeeded())
                dialogue.Advance();
        }

        private static void HandleSmokeOrderCompleted(BusinessOrderSessionResult result)
        {
            if (result != null && result.owner == OrderSessionOwner.Business)
                smokeOrderResults.Add(result);
        }

        private static void FinishSmokeTest(bool success, string message)
        {
            smokeExitCode = success ? 0 : 1;
            if (success)
                Debug.Log("[BusinessFlowSmokeTest] " + message);
            else
                Debug.LogError("[BusinessFlowSmokeTest] " + message);

            if (smokeObservedSession != null)
                smokeObservedSession.OrderCompleted -= HandleSmokeOrderCompleted;
            smokeObservedSession = null;
            EditorApplication.update -= TickSmokeTest;
            EditorApplication.ExitPlaymode();
        }

        private static void OverrideAutoStartForSmoke()
        {
            BusinessOrderFlowSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(SettingsPath);
            if (settings == null)
                return;

            smokeOriginalAutoStart = settings.autoStart;
            settings.autoStart = false;
            smokeAutoStartOverridden = true;
        }

        private static void RestoreAutoStartAfterSmoke()
        {
            if (!smokeAutoStartOverridden)
                return;

            BusinessOrderFlowSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(SettingsPath);
            if (settings != null)
                settings.autoStart = smokeOriginalAutoStart;

            smokeAutoStartOverridden = false;
        }

        private static void PrepareSmokeAutosaveBackup()
        {
            smokeAutosavePath = Path.Combine(Application.persistentDataPath, "autosave.json");
            smokeAutosaveBackupPath = smokeAutosavePath + ".business-flow-smoke.bak";

            if (File.Exists(smokeAutosaveBackupPath))
            {
                File.Copy(smokeAutosaveBackupPath, smokeAutosavePath, true);
                File.Delete(smokeAutosaveBackupPath);
            }

            smokeAutosaveExisted = File.Exists(smokeAutosavePath);
            if (smokeAutosaveExisted)
                File.Copy(smokeAutosavePath, smokeAutosaveBackupPath, true);

            string autosaveDirectory = Path.GetDirectoryName(smokeAutosavePath);
            if (!string.IsNullOrWhiteSpace(autosaveDirectory))
                Directory.CreateDirectory(autosaveDirectory);

            SaveData smokeSave = new SaveData { dayCount = 1 };
            if (smokeScenario == SmokeScenario.SequentialGrades)
            {
                smokeSave.businessDay = new BusinessDaySnapshot
                {
                    day = 1,
                    seed = 0x51A17E,
                    currentIndex = 0,
                    isCompleted = false
                };
                for (int i = 0; i < 3; i++)
                {
                    smokeSave.businessDay.entries.Add(new BusinessSequenceEntrySnapshot
                    {
                        entryType = BusinessSequencePlanner.OrderEntryType,
                        entryId = VerticalOrderKey,
                        contentId = "vodka_lemon"
                    });
                }
            }

            File.WriteAllText(
                smokeAutosavePath,
                JsonUtility.ToJson(smokeSave, true));
        }

        private static bool RestoreSmokeAutosave()
        {
            try
            {
                if (smokeAutosaveExisted)
                {
                    if (!File.Exists(smokeAutosaveBackupPath))
                        throw new FileNotFoundException("Smoke-test autosave backup is missing.", smokeAutosaveBackupPath);

                    File.Copy(smokeAutosaveBackupPath, smokeAutosavePath, true);
                }
                else if (File.Exists(smokeAutosavePath))
                {
                    File.Delete(smokeAutosavePath);
                }

                if (File.Exists(smokeAutosaveBackupPath))
                    File.Delete(smokeAutosaveBackupPath);
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[BusinessFlowSmokeTest] Failed to restore autosave: " + exception);
                return false;
            }
        }

        private static void ConfigureBartendingSettings()
        {
            BusinessBartendingSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessBartendingSettings>(BartendingSettingsPath);
            if (settings == null)
            {
                Debug.LogError("Business bartending settings are missing.");
                return;
            }

            settings.initialBottleItems = System.Array.Empty<ItemDef>();
            EditorUtility.SetDirty(settings);
        }

        private static void ConfigureScene(
            BusinessOrderFlowSettings settings,
            LiquorBottleDef lemonShelfDefinition)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BusinessFlowBootstrap bootstrap = FindInScene<BusinessFlowBootstrap>(scene);
            if (bootstrap == null)
            {
                GameObject host = new GameObject("BusinessFlow");
                SceneManager.MoveGameObjectToScene(host, scene);
                bootstrap = host.AddComponent<BusinessFlowBootstrap>();
            }

            SerializedObject serializedBootstrap = new SerializedObject(bootstrap);
            serializedBootstrap.FindProperty("settings").objectReferenceValue = settings;
            serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrap);
            ConfigureLemonShelfSlot(scene, lemonShelfDefinition);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureLemonShelfSlot(Scene scene, LiquorBottleDef lemonShelfDefinition)
        {
            if (lemonShelfDefinition == null)
                return;

            List<LiquorBottleSlotUI> shelfSlots = new List<LiquorBottleSlotUI>();
            foreach (GameObject root in scene.GetRootGameObjects())
                shelfSlots.AddRange(root.GetComponentsInChildren<LiquorBottleSlotUI>(true));

            if (shelfSlots.Exists(slot => slot != null
                    && slot.Definition != null
                    && string.Equals(
                        slot.Definition.id,
                        lemonShelfDefinition.id,
                        System.StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            LiquorBottleSlotUI anchor = shelfSlots.Find(slot => slot != null
                && slot.Definition != null
                && slot.Definition.id == "tropical_juice");
            LiquorBottleSlotUI target = null;
            if (anchor != null && anchor.transform.parent != null)
            {
                LiquorBottleSlotUI[] siblings =
                    anchor.transform.parent.GetComponentsInChildren<LiquorBottleSlotUI>(true);
                target = System.Array.Find(siblings, slot => slot != null && slot.Definition == null);
            }

            target ??= shelfSlots.Find(slot => slot != null && slot.Definition == null);
            if (target == null)
                throw new System.InvalidOperationException("No empty liquor shelf slot is available for Lemon Juice.");

            SerializedObject serializedSlot = new SerializedObject(target);
            serializedSlot.FindProperty("def").objectReferenceValue = lemonShelfDefinition;
            serializedSlot.ApplyModifiedPropertiesWithoutUndo();

            Image image = target.GetComponent<Image>();
            if (image == null)
                image = target.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            image.preserveAspect = true;
            EditorUtility.SetDirty(image);
            EditorUtility.SetDirty(target);
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }

            return null;
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
