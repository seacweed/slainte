using System;
using System.Collections.Generic;
using System.Reflection;
using Slainte.Bartending;
using Slainte.Business;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class BusinessShiftValidator
    {
        private const string BusinessScenePath = "Assets/BusinessScene.unity";
        private const string SettingsPath = "Assets/Resources/Business/BusinessOrderFlowSettings.asset";
        private const string TicketDatabasePath = "Assets/Data/OrderTicket/OrderTicketDatabase.asset";

        [MenuItem("Slainte/품질 검증/시간 기반 영업 검증")]
        public static void ValidateFromMenu()
        {
            RunValidation();
            EditorUtility.DisplayDialog("시간 기반 영업 검증", "모든 검증을 통과했습니다.", "확인");
        }

        public static void RunBatchValidation()
        {
            RunValidation();
        }

        private static void RunValidation()
        {
            ValidateSettingsAndScene();
            ValidatePlannerRules();
            ValidateEpisodeCraftingCompatibility();
            ValidateTechnicalFailureContract();
            Debug.Log("[BusinessShiftValidator] 통과: 180초 설정, 손님 풀, 쿨다운 우선·대체 선택, 필수 액션, 에피소드 실제 제조 결과·구형 분기 호환, BusinessScene 구성");
        }

        private static void ValidateSettingsAndScene()
        {
            BusinessOrderFlowSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(SettingsPath);
            Require(settings != null, "영업 설정 에셋이 없습니다.");
            Require(Mathf.Approximately(settings.shiftDurationSeconds, 180f),
                $"기본 영업시간이 180초가 아닙니다: {settings.shiftDurationSeconds}");
            Require(settings.customerVisitDatabase != null, "영업 설정에 손님 데이터베이스가 연결되지 않았습니다.");
            Require(settings.customerVisitDatabase.visits != null
                    && settings.customerVisitDatabase.visits.Count > 0,
                "손님 데이터베이스가 비어 있습니다.");

            for (int i = 0; i < settings.customerVisitDatabase.visits.Count; i++)
            {
                CustomerVisitData visit = settings.customerVisitDatabase.visits[i];
                if (visit == null)
                    continue;
                Require(visit.cooldownSeconds >= 0f,
                    $"손님 쿨다운이 음수입니다: {visit.visitKey}");
            }

            var scene = EditorSceneManager.OpenScene(BusinessScenePath, OpenSceneMode.Single);
            Require(scene.IsValid(), "BusinessScene을 열지 못했습니다.");
            Require(UnityEngine.Object.FindFirstObjectByType<BusinessFlowBootstrap>() != null,
                "BusinessScene에 BusinessFlowBootstrap이 없습니다.");
            Require(UnityEngine.Object.FindFirstObjectByType<EpisodeRunner>() != null,
                "BusinessScene에 EpisodeRunner가 없습니다.");
            Require(UnityEngine.Object.FindFirstObjectByType<GameModeManager>() != null,
                "BusinessScene에 GameModeManager가 없습니다.");
        }

        private static void ValidatePlannerRules()
        {
            GameObject progressObject = new("BusinessShiftValidator_GameProgress");
            GameProgress progress = progressObject.AddComponent<GameProgress>();
            CustomerVisitDatabase database = ScriptableObject.CreateInstance<CustomerVisitDatabase>();
            CustomerVisitData visit = ScriptableObject.CreateInstance<CustomerVisitData>();
            CustomerVisitData readyVisit = ScriptableObject.CreateInstance<CustomerVisitData>();
            CustomerOrderData order = ScriptableObject.CreateInstance<CustomerOrderData>();
            EpisodeData episode = ScriptableObject.CreateInstance<EpisodeData>();

            try
            {
                progress.LoadFrom(new SaveData());
                progress.SetCurrentDay(5);
                order.key = "validator_order";
                order.requestedRecipeId = "validator_recipe";
                visit.visitKey = "validator_visit";
                visit.weight = 1f;
                visit.cooldownSeconds = 100f;
                visit.members.Add(new CustomerVisitMember { characterKey = "validator_customer" });
                visit.orders.Add(new CustomerVisitOrderOption
                {
                    order = order,
                    weight = 1f,
                    condition = new EpisodeTriggerCondition()
                });
                database.visits.Add(visit);

                List<CustomerVisitData> pool =
                    BusinessSequencePlanner.BuildEligibleVisitPool(database, progress);
                Require(pool.Count == 1, "조건을 만족하는 검증 손님이 풀에 들어오지 않았습니다.");

                Dictionary<string, float> cooldowns = new(StringComparer.OrdinalIgnoreCase)
                {
                    [visit.visitKey] = 100f
                };
                BusinessVisitSelection beforeCooldown = BusinessSequencePlanner.PickWeightedVisit(
                    pool,
                    progress,
                    cooldowns,
                    null,
                    99.999f,
                    new System.Random(1));
                Require(beforeCooldown?.Visit == visit,
                    "모든 손님이 쿨다운 중일 때 대체 손님을 선택하지 못했습니다.");

                readyVisit.visitKey = "validator_ready_visit";
                readyVisit.weight = 1f;
                readyVisit.members.Add(
                    new CustomerVisitMember { characterKey = "validator_ready_customer" });
                readyVisit.orders.Add(new CustomerVisitOrderOption
                {
                    order = order,
                    weight = 1f,
                    condition = new EpisodeTriggerCondition()
                });
                List<CustomerVisitData> mixedPool = new() { visit, readyVisit };
                BusinessVisitSelection readyPreferred = BusinessSequencePlanner.PickWeightedVisit(
                    mixedPool,
                    progress,
                    cooldowns,
                    null,
                    99.999f,
                    new System.Random(1));
                Require(readyPreferred?.Visit == readyVisit,
                    "쿨다운이 끝난 손님보다 쿨다운 중인 손님을 먼저 선택했습니다.");

                BusinessVisitSelection atCooldown = BusinessSequencePlanner.PickWeightedVisit(
                    pool,
                    progress,
                    cooldowns,
                    null,
                    100f,
                    new System.Random(1));
                Require(atCooldown?.Visit == visit, "100초 쿨다운 경계에서 손님이 복귀하지 않았습니다.");

                episode.episodeId = "validator_episode";
                BusinessRequiredActionRule lowerPriority = new()
                {
                    ruleId = "validator_customer_rule",
                    actionType = BusinessRequiredActionType.CustomerVisit,
                    exactDay = 5,
                    priority = 10,
                    timing = BusinessRequiredActionTiming.BeforeFirstCustomer,
                    customerVisit = visit
                };
                BusinessRequiredActionRule higherPriority = new()
                {
                    ruleId = "validator_episode_rule",
                    actionType = BusinessRequiredActionType.EncounterEpisode,
                    exactDay = 5,
                    priority = 100,
                    timing = BusinessRequiredActionTiming.BeforeFirstCustomer,
                    encounterEpisode = episode
                };
                List<BusinessRequiredActionRule> rules = new() { lowerPriority, higherPriority };

                BusinessRequiredActionRule selected = BusinessSequencePlanner.PickNextRequiredAction(
                    rules,
                    progress,
                    BusinessRequiredActionTiming.BeforeFirstCustomer,
                    false,
                    null,
                    null);
                Require(selected == higherPriority, "필수 액션의 높은 priority가 먼저 선택되지 않았습니다.");

                progress.MarkEpisodeCompleted(episode.episodeId);
                selected = BusinessSequencePlanner.PickNextRequiredAction(
                    rules,
                    progress,
                    BusinessRequiredActionTiming.BeforeFirstCustomer,
                    false,
                    null,
                    null);
                Require(selected == lowerPriority, "완료된 인카운터 에피소드를 다시 필수 선택했습니다.");

                HashSet<string> completedTargets = new(StringComparer.OrdinalIgnoreCase)
                {
                    lowerPriority.TargetKey
                };
                selected = BusinessSequencePlanner.PickNextRequiredAction(
                    rules,
                    progress,
                    BusinessRequiredActionTiming.BeforeFirstCustomer,
                    false,
                    null,
                    completedTargets);
                Require(selected == null, "완료한 필수 대상을 건너뛰지 못했습니다.");

                progress.SetCurrentDay(6);
                selected = BusinessSequencePlanner.PickNextRequiredAction(
                    rules,
                    progress,
                    BusinessRequiredActionTiming.BeforeFirstCustomer,
                    false,
                    null,
                    null);
                Require(selected == null, "exactDay가 다른 필수 액션이 선택됐습니다.");
                progress.SetCurrentDay(5);

                EpisodeTriggerCondition appearanceCondition = new();
                appearanceCondition.requiredCustomerAppearances.Add(
                    new CustomerAppearanceCondition { characterId = "validator_customer", count = 1 });
                Require(!ProgressConditionEvaluator.IsMet(appearanceCondition, progress),
                    "등장 횟수 조건이 충족 전인데 true입니다.");
                progress.IncrementCustomerAppearance("validator_customer");
                Require(ProgressConditionEvaluator.IsMet(appearanceCondition, progress),
                    "등장 횟수 조건이 충족 후에도 false입니다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(episode);
                UnityEngine.Object.DestroyImmediate(order);
                UnityEngine.Object.DestroyImmediate(readyVisit);
                UnityEngine.Object.DestroyImmediate(visit);
                UnityEngine.Object.DestroyImmediate(database);
                UnityEngine.Object.DestroyImmediate(progressObject);
            }
        }

        private static void ValidateEpisodeCraftingCompatibility()
        {
            ValidateLegacyCraftingFields();
            ValidateExistingCraftingNodes();
            ValidateCraftingResultMapping();
        }

        private static void ValidateTechnicalFailureContract()
        {
            GameObject controllerObject = new("BusinessOrderSessionValidator");
            BusinessOrderFlowSettings settings =
                ScriptableObject.CreateInstance<BusinessOrderFlowSettings>();
            try
            {
                BusinessOrderSessionController controller =
                    controllerObject.AddComponent<BusinessOrderSessionController>();
                controller.Initialize(null, null, null, null, null, null, settings);

                int callbackCount = 0;
                BusinessOrderSessionResult completed = null;
                bool started = controller.BeginOrder(new OrderSessionRequest
                {
                    sessionId = "validator_missing_recipe",
                    owner = OrderSessionOwner.Episode,
                    requestedRecipeId = "validator_recipe_that_does_not_exist",
                    presentOrder = false,
                    presentFeedback = false,
                    applyProgressRewards = false
                }, result =>
                {
                    callbackCount++;
                    completed = result;
                });

                Require(started, "누락 레시피 주문이 기술 실패 결과를 반환하지 않았습니다.");
                Require(callbackCount == 1, "기술 실패 완료 콜백이 정확히 한 번 호출되지 않았습니다.");
                Require(completed != null && completed.technicalFailure,
                    "누락 레시피가 플레이 결과가 아닌 기술 실패로 분류되지 않았습니다.");
                Require(controller.State == BusinessOrderSessionState.Completed,
                    "기술 실패 후 주문 세션이 Completed 상태로 정리되지 않았습니다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(controllerObject);
            }
        }

        private static void ValidateLegacyCraftingFields()
        {
            EpisodeNode node = new();
            SetPrivateField(node, "nextNodeIdGood", "legacy_good");
            SetPrivateField(node, "nextNodeIdBad", "legacy_bad");
            SetPrivateField(node, "craftingFlagGood", "legacy_good_flag");
            SetPrivateField(node, "craftingFlagBad", "legacy_bad_flag");

            Require(node.GetNextNodeId(CraftingJobResult.Good) == "legacy_good",
                "구형 Good 제조 분기를 읽지 못했습니다.");
            Require(node.GetNextNodeId(CraftingJobResult.MidIce) == "legacy_bad",
                "상세 Mid 분기가 없을 때 구형 Bad 분기로 폴백하지 못했습니다.");
            Require(node.GetCraftingFlag(CraftingJobResult.Good) == "legacy_good_flag",
                "구형 Good 제조 플래그를 읽지 못했습니다.");
            Require(node.GetCraftingFlag(CraftingJobResult.MidGlass) == "legacy_bad_flag",
                "상세 Mid 플래그가 없을 때 구형 Bad 플래그로 폴백하지 못했습니다.");

            EpisodeNode outcomeNode = new();
            outcomeNode.craftingOutcomes.Add(new CraftingOutcome
            {
                result = CraftingJobResult.Bad,
                nextNodeId = "outcome_bad"
            });
            Require(outcomeNode.GetNextNodeId(CraftingJobResult.MidWrongMenu) == "outcome_bad",
                "상세 Mid 분기가 없을 때 신규 Bad outcome으로 폴백하지 못했습니다.");
        }

        private static void ValidateExistingCraftingNodes()
        {
            EpisodeData[] episodes = Resources.LoadAll<EpisodeData>("EpisodeData");
            ItemDefCatalog itemCatalog = ItemDefCatalog.LoadFromResources("Items", null);
            CocktailRecipeCatalog recipeCatalog = CocktailRecipeDataLoader.LoadDefault(itemCatalog);
            OrderTicketDatabase ticketDatabase =
                AssetDatabase.LoadAssetAtPath<OrderTicketDatabase>(TicketDatabasePath);
            Require(ticketDatabase != null, "주문표 데이터베이스를 불러오지 못했습니다.");
            int actualCraftingNodes = 0;
            int manualCraftingNodes = 0;
            for (int i = 0; i < episodes.Length; i++)
            {
                EpisodeData episode = episodes[i];
                if (episode?.nodes == null)
                    continue;

                for (int j = 0; j < episode.nodes.Count; j++)
                {
                    EpisodeNode node = episode.nodes[j];
                    if (node == null || !node.requiresCrafting)
                        continue;

                    if (string.IsNullOrWhiteSpace(node.craftingRecipeId))
                        manualCraftingNodes++;
                    else
                    {
                        actualCraftingNodes++;
                        Require(recipeCatalog.TryGet(node.craftingRecipeId, out CocktailRecipe recipe)
                                && recipe != null
                                && recipe.isOrderable,
                            $"실제 제조 노드의 주문 가능한 레시피가 없습니다: "
                            + $"{episode.episodeId}/{node.nodeId}/{node.craftingRecipeId}");
                        Require(!string.IsNullOrWhiteSpace(node.craftingTicketKey)
                                && ticketDatabase.FindByKey(node.craftingTicketKey) != null,
                            $"실제 제조 노드의 주문표가 없습니다: "
                            + $"{episode.episodeId}/{node.nodeId}/{node.craftingTicketKey}");
                    }

                    Require(!string.IsNullOrWhiteSpace(node.GetNextNodeId(CraftingJobResult.Good)),
                        $"제조 노드의 Good 분기가 없습니다: {episode.episodeId}/{node.nodeId}");
                    Require(!string.IsNullOrWhiteSpace(node.GetNextNodeId(CraftingJobResult.Bad)),
                        $"제조 노드의 Bad 분기가 없습니다: {episode.episodeId}/{node.nodeId}");
                }
            }

            Require(actualCraftingNodes > 0,
                "craftingRecipeId가 있는 실제 제조 노드를 찾지 못했습니다.");
            Require(manualCraftingNodes > 0,
                "기존 수동 판정 호환을 검증할 제조 노드를 찾지 못했습니다.");
        }

        private static void ValidateCraftingResultMapping()
        {
            BusinessOrderSessionResult good = CreateCraftingResult(
                OrderEvaluationGrade.Good,
                null,
                null,
                null);
            Require(EpisodeCraftingResultMapper.Map(good) == CraftingJobResult.Good,
                "Good 제조 결과 매핑에 실패했습니다.");

            CocktailRecipe baseRecipe = new() { id = "requested", glassId = "rock" };
            CocktailRecipe otherRecipe = new() { id = "other" };
            CocktailEvaluationResult requestedFailure = new()
            {
                matchedRecipe = baseRecipe,
                isSuccess = false
            };
            CocktailEvaluationResult detectedOther = new()
            {
                matchedRecipe = otherRecipe,
                isSuccess = true
            };
            BusinessOrderSessionResult wrongMenu = CreateCraftingResult(
                OrderEvaluationGrade.Mid,
                baseRecipe,
                requestedFailure,
                detectedOther);
            Require(EpisodeCraftingResultMapper.Map(wrongMenu) == CraftingJobResult.MidWrongMenu,
                "잘못된 메뉴 제조 결과 매핑에 실패했습니다.");

            CocktailEvaluationResult iceAndGlass = new()
            {
                matchedRecipe = baseRecipe,
                isSuccess = false,
                iceValid = false,
                glassValid = false
            };
            Require(EpisodeCraftingResultMapper.Map(CreateCraftingResult(
                    OrderEvaluationGrade.Mid,
                    baseRecipe,
                    iceAndGlass,
                    null)) == CraftingJobResult.MidIceGlass,
                "얼음·잔 동시 불일치 결과 매핑에 실패했습니다.");

            CocktailRecipe iceVariant = new()
            {
                id = "requested__mid_ice",
                baseRecipeId = "requested",
                glassId = "rock",
                iceRequirement = IceRequirement.Required,
                evaluationGrade = CocktailRecipeEvaluationGrade.Mid
            };
            CocktailEvaluationResult matchedVariant = new()
            {
                matchedRecipe = iceVariant,
                isSuccess = true
            };
            BusinessOrderSessionResult variantResult = CreateCraftingResult(
                OrderEvaluationGrade.Mid,
                baseRecipe,
                matchedVariant,
                matchedVariant);
            Require(EpisodeCraftingResultMapper.Map(variantResult) == CraftingJobResult.MidIce,
                "요청 레시피의 얼음 변형을 잘못된 메뉴로 오인했습니다.");

            CocktailEvaluationResult otherFailure = new()
            {
                matchedRecipe = baseRecipe,
                isSuccess = false
            };
            Require(EpisodeCraftingResultMapper.Map(CreateCraftingResult(
                    OrderEvaluationGrade.Mid,
                    baseRecipe,
                    otherFailure,
                    null)) == CraftingJobResult.Bad,
                "별도 Mid 유형이 없는 제조 실패가 Bad로 폴백하지 않았습니다.");
        }

        private static BusinessOrderSessionResult CreateCraftingResult(
            OrderEvaluationGrade grade,
            CocktailRecipe baseRecipe,
            CocktailEvaluationResult requested,
            CocktailEvaluationResult detected)
        {
            return new BusinessOrderSessionResult
            {
                outcome = OrderSessionOutcome.Served,
                accepted = true,
                grade = grade,
                requestedRecipeId = baseRecipe?.id ?? "requested",
                evaluation = requested == null && detected == null && baseRecipe == null
                    ? null
                    : new CocktailOrderEvaluationResult
                    {
                        order = new GeneratedCocktailOrder
                        {
                            requestedRecipeId = baseRecipe?.id ?? "requested",
                            requestedRecipe = baseRecipe
                        },
                        requestedRecipeResult = requested,
                        detectedRecipeResult = detected
                    }
            };
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, $"런타임 호환 필드가 없습니다: {fieldName}");
            field.SetValue(target, value);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("[BusinessShiftValidator] " + message);
        }
    }
}
