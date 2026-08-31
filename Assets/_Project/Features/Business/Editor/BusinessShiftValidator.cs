using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Slainte.Bartending;
using Slainte.Business;
using Slainte.Economy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class BusinessShiftValidator
    {
        private const string BusinessScenePath = ProjectScenePaths.Business;
        private const string SettingsPath = "Assets/Resources/Business/BusinessOrderFlowSettings.asset";
        private const string TicketDatabasePath = "Assets/Data/OrderTicket/OrderTicketDatabase.asset";
        private const string CustomerOrderDatabasePath =
            "Assets/Data/CustomerOrder/CustomerOrderDatabase.asset";
        private const string CharacterDatabasePath =
            "Assets/Data/CharacterData/CharacterDatabase.asset";
        private const string F54CharacterPath =
            "Assets/Data/CharacterData/data/CharacterData_f54.asset";
        private const string F72CharacterPath =
            "Assets/Data/CharacterData/data/CharacterData_f72.asset";

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

        public static void RunCustomerDialogueBatchValidation()
        {
            ValidatePublishedCustomerOrders();
            ValidateF54F72EvaluationSprites();
            ValidateOrderTicketDialogueMemo();
            ValidateConditionOrderEvaluation();
            ValidateCraftingResultMapping();
            Debug.Log(
                "[BusinessShiftValidator] 주문 대사 검증 통과: 연결 주문 168개, "
                + "취향·분위기 주문 26개, 의도적 빈 주문 대사 0개, "
                + "주문 당시 대사 주문표 반영, 상세 결과 매핑 및 누락 대사 dummy fallback");
        }

        private static void RunValidation()
        {
            ValidateSettingsAndScene();
            ValidatePublishedCustomerOrders();
            ValidateF54F72EvaluationSprites();
            ValidatePlannerRules();
            ValidateEpisodeCraftingCompatibility();
            ValidateTechnicalFailureContract();
            Debug.Log("[BusinessShiftValidator] 통과: 180초 설정, 손님·주문 DB 무결성, 최근 손님 2명 제한, Day 5 이후 StrangeCoin_0 3번 슬롯·TheLittles_0 6번 슬롯·다음 날 재시도·완료 제외, 필수 액션, 에피소드 실제 제조 결과·구형 분기 호환, BusinessScene 구성");
        }

        private static void ValidatePublishedCustomerOrders()
        {
            BusinessOrderFlowSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(SettingsPath);
            CustomerOrderDatabase orderDatabase =
                AssetDatabase.LoadAssetAtPath<CustomerOrderDatabase>(CustomerOrderDatabasePath);
            OrderTicketDatabase ticketDatabase =
                AssetDatabase.LoadAssetAtPath<OrderTicketDatabase>(TicketDatabasePath);
            Require(settings?.customerVisitDatabase != null,
                "손님 방문 데이터베이스를 불러오지 못했습니다.");
            Require(orderDatabase != null, "손님 주문 데이터베이스를 불러오지 못했습니다.");
            Require(ticketDatabase != null, "주문표 데이터베이스를 불러오지 못했습니다.");

            HashSet<string> registeredKeys = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < orderDatabase.customers.Count; i++)
            {
                CustomerOrderData registered = orderDatabase.customers[i];
                Require(registered != null && !string.IsNullOrWhiteSpace(registered.key),
                    $"손님 주문 DB의 {i}번 항목이 비어 있거나 키가 없습니다.");
                Require(registeredKeys.Add(registered.key),
                    $"손님 주문 DB에 중복 키가 있습니다: {registered.key}");
            }

            ItemDefCatalog items = ItemDefCatalog.LoadFromResources("Items", null);
            CocktailRecipeCatalog recipes = CocktailRecipeDataLoader.LoadDefault(items);
            int connectedOrderCount = 0;
            int conditionOrderCount = 0;
            int intentionallyBlankOrderCount = 0;
            Dictionary<CraftingJobResult, int> missingFeedbackCounts = new();
            for (int resultIndex = 0; resultIndex < CraftingJobResultPorts.Order.Length; resultIndex++)
                missingFeedbackCounts[CraftingJobResultPorts.Order[resultIndex]] = 0;
            for (int visitIndex = 0;
                 visitIndex < settings.customerVisitDatabase.visits.Count;
                 visitIndex++)
            {
                CustomerVisitData visit = settings.customerVisitDatabase.visits[visitIndex];
                if (visit?.orders == null)
                    continue;

                for (int orderIndex = 0; orderIndex < visit.orders.Count; orderIndex++)
                {
                    CustomerOrderData order = visit.orders[orderIndex]?.order;
                    Require(order != null && !string.IsNullOrWhiteSpace(order.key),
                        $"{visit.visitKey}: 연결 주문이 비어 있거나 키가 없습니다.");
                    Require(orderDatabase.FindByKey(order.key) == order,
                        $"{visit.visitKey}/{order.key}: 방문과 주문 DB의 에셋 참조가 다릅니다.");
                    bool conditionOrder = order.orderType == CocktailOrderType.TasteOrder
                        || order.orderType == CocktailOrderType.MoodOrder;
                    if (conditionOrder)
                    {
                        int validTags = 0;
                        string requestedTag = string.Empty;
                        if (order.tags != null)
                        {
                            for (int tagIndex = 0; tagIndex < order.tags.Count; tagIndex++)
                            {
                                if (string.IsNullOrWhiteSpace(order.tags[tagIndex]))
                                    continue;

                                validTags++;
                                requestedTag = order.tags[tagIndex].Trim();
                            }
                        }
                        Require(validTags == 1,
                            $"{visit.visitKey}/{order.key}: 조건 주문 태그가 정확히 하나가 아닙니다.");
                        requestedTag = CocktailOrderTagRules.Normalize(requestedTag);
                        Require(HasOrderableRecipeForCondition(
                                recipes,
                                order.orderType,
                                requestedTag),
                            $"{visit.visitKey}/{order.key}: 조건 태그에 대응하는 주문 가능한 레시피가 없습니다: "
                            + requestedTag);
                        conditionOrderCount++;
                    }
                    else
                    {
                        Require(recipes.TryGet(order.requestedRecipeId, out CocktailRecipe recipe)
                                && recipe != null
                                && recipe.isOrderable,
                            $"{visit.visitKey}/{order.key}: 주문 가능한 레시피가 없습니다: "
                            + order.requestedRecipeId);
                    }

                    Require(ticketDatabase.FindByKey(order.key) != null,
                        $"{visit.visitKey}/{order.key}: 영업 주문표가 없습니다.");
                    Require((order.intentionallySilentFeedback & order.authoredFeedback) == 0,
                        $"{visit.visitKey}/{order.key}: 실제 대사와 명시적 무대사 결과가 겹칩니다.");
                    for (int resultIndex = 0;
                         resultIndex < CraftingJobResultPorts.Order.Length;
                         resultIndex++)
                    {
                        CraftingJobResult result = CraftingJobResultPorts.Order[resultIndex];
                        bool hasDialogue = order.TryGetAuthoredFeedback(result, out _);
                        bool intentionallySilent = order.IsFeedbackIntentionallySilent(result);
                        if (!hasDialogue && !intentionallySilent)
                        {
                            missingFeedbackCounts[result]++;
                            Require(!string.IsNullOrWhiteSpace(
                                    settings.GetMissingFeedbackDummy(result)),
                                $"{visit.visitKey}/{order.key}: {result} 누락 대사를 대신할 dummy가 없습니다.");
                        }
                    }
                    if (order.orderDialogueAuthored
                        && (order.lines == null || order.lines.Count == 0))
                    {
                        intentionallyBlankOrderCount++;
                    }
                    connectedOrderCount++;
                }
            }

            Require(connectedOrderCount > 0, "검증할 손님 연결 주문이 없습니다.");
            Require(connectedOrderCount == 168,
                $"연결 주문 수가 예상과 다릅니다: {connectedOrderCount}/168");
            Require(conditionOrderCount == 26,
                $"취향·분위기 주문 수가 예상과 다릅니다: {conditionOrderCount}/26");
            Require(intentionallyBlankOrderCount == 0,
                $"의도적으로 비운 주문 대사 수가 예상과 다릅니다: {intentionallyBlankOrderCount}/0");

            List<string> missingFeedbackSummary = new();
            for (int resultIndex = 0;
                 resultIndex < CraftingJobResultPorts.Order.Length;
                 resultIndex++)
            {
                CraftingJobResult result = CraftingJobResultPorts.Order[resultIndex];
                missingFeedbackSummary.Add($"{result}={missingFeedbackCounts[result]}");
            }
            Debug.Log(
                "[BusinessShiftValidator] 누락 결과 대사(dummy 대상): "
                + string.Join(", ", missingFeedbackSummary));
        }

        private static void ValidateF54F72EvaluationSprites()
        {
            CharacterDatabase database =
                AssetDatabase.LoadAssetAtPath<CharacterDatabase>(CharacterDatabasePath);
            Require(database != null, "캐릭터 데이터베이스를 불러오지 못했습니다.");

            ValidateEvaluationSpriteAliases(database, "f54", F54CharacterPath);
            ValidateEvaluationSpriteAliases(database, "f72", F72CharacterPath);

            BusinessOrderFlowSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(SettingsPath);
            Require(settings?.customerVisitDatabase != null,
                "평가 표정을 검증할 손님 방문 데이터베이스를 불러오지 못했습니다.");

            HashSet<string> validatedCharacters = new(StringComparer.OrdinalIgnoreCase);
            for (int visitIndex = 0;
                 visitIndex < settings.customerVisitDatabase.visits.Count;
                 visitIndex++)
            {
                CustomerVisitData visit = settings.customerVisitDatabase.visits[visitIndex];
                if (visit?.members == null)
                    continue;

                for (int memberIndex = 0; memberIndex < visit.members.Count; memberIndex++)
                {
                    CustomerVisitMember member = visit.members[memberIndex];
                    if (member == null
                        || (!string.Equals(member.characterKey, "f54", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(member.characterKey, "f72", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    Require(string.Equals(
                            member.expressionKeyGood,
                            "smile",
                            StringComparison.OrdinalIgnoreCase),
                        $"Good 평가 키가 smile이 아닙니다: {visit.visitKey}/{member.characterKey}");
                    Require(string.Equals(
                            member.expressionKeyBad,
                            "angry",
                            StringComparison.OrdinalIgnoreCase),
                        $"Bad 평가 키가 angry가 아닙니다: {visit.visitKey}/{member.characterKey}");
                    validatedCharacters.Add(member.characterKey);
                }
            }

            Require(validatedCharacters.Contains("f54") && validatedCharacters.Contains("f72"),
                "F54/F72 손님 방문 평가 표정을 검증하지 못했습니다.");
        }

        private static void ValidateEvaluationSpriteAliases(
            CharacterDatabase database,
            string characterKey,
            string assetPath)
        {
            CharacterData character =
                AssetDatabase.LoadAssetAtPath<CharacterData>(assetPath);
            Require(character != null,
                $"평가 표정을 검증할 캐릭터 에셋이 없습니다: {characterKey}");
            Require(database.FindByKey(characterKey) == character,
                $"캐릭터 DB가 평가 표정 에셋을 가리키지 않습니다: {characterKey}");

            Sprite mid = character.GetSprite("mid");
            Sprite smile = character.GetSprite("smile");
            Sprite angry = character.GetSprite("angry");
            Require(mid != null && character.defaultSprite == mid,
                $"기본 표정 폴백이 mid가 아닙니다: {characterKey}");
            Require(smile != null && character.GetSprite("good") == smile,
                $"Good 평가 표정이 smile에 연결되지 않았습니다: {characterKey}");
            Require(angry != null && character.GetSprite("bad") == angry,
                $"Bad 평가 표정이 angry에 연결되지 않았습니다: {characterKey}");
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
            Require(settings.randomEncounters != null, "랜덤 인카운터 목록이 없습니다.");

            for (int i = 0; i < settings.randomEncounters.Count; i++)
            {
                BusinessRandomEncounterEntry entry = settings.randomEncounters[i];
                Require(entry?.episode != null
                        && entry.episode.episodeType == EpisodeType.Encounter
                        && entry.weight > 0f,
                    $"랜덤 인카운터 {i}번이 잘못 설정되었습니다.");
                Require(!string.Equals(
                        entry.episode.episodeId,
                        "StrangeCoin_0",
                        StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(
                            entry.episode.episodeId,
                            "TheLittles_0",
                            StringComparison.OrdinalIgnoreCase),
                    "고정 슬롯 인카운터는 랜덤 인카운터 풀에서 제거되어야 합니다.");
            }

            BusinessRequiredActionRule strangeCoinRule = null;
            BusinessRequiredActionRule theLittlesRule = null;
            Require(settings.requiredActions != null, "필수 영업 액션 목록이 없습니다.");
            for (int i = 0; i < settings.requiredActions.Count; i++)
            {
                BusinessRequiredActionRule rule = settings.requiredActions[i];
                if (rule?.encounterEpisode != null
                    && string.Equals(
                        rule.encounterEpisode.episodeId,
                        "StrangeCoin_0",
                        StringComparison.OrdinalIgnoreCase))
                {
                    strangeCoinRule = rule;
                }
                else if (rule?.encounterEpisode != null
                    && string.Equals(
                        rule.encounterEpisode.episodeId,
                        "TheLittles_0",
                        StringComparison.OrdinalIgnoreCase))
                {
                    theLittlesRule = rule;
                }
            }

            Require(strangeCoinRule != null,
                "StrangeCoin_0의 고정 영업 슬롯 규칙이 없습니다.");
            Require(strangeCoinRule.actionType == BusinessRequiredActionType.EncounterEpisode,
                "StrangeCoin_0 고정 규칙이 인카운터 액션이 아닙니다.");
            Require(strangeCoinRule.timing == BusinessRequiredActionTiming.SequenceSlot
                    && strangeCoinRule.sequenceSlot == 3,
                "StrangeCoin_0은 3번 영업 슬롯으로 설정되어야 합니다.");
            Require(strangeCoinRule.condition != null
                    && strangeCoinRule.condition.minDay == 5,
                "StrangeCoin_0 고정 규칙은 Day 5부터 활성화되어야 합니다.");
            Require(strangeCoinRule.encounterEpisode.episodeType == EpisodeType.Encounter,
                "StrangeCoin_0의 EpisodeType이 Encounter가 아닙니다.");
            Require(strangeCoinRule.encounterEpisode.triggerCondition != null
                    && strangeCoinRule.encounterEpisode.triggerCondition.minDay == 5,
                "StrangeCoin_0 에피소드 자체의 시작 조건도 Day 5여야 합니다.");
            Require(!string.IsNullOrWhiteSpace(strangeCoinRule.encounterEpisode.firstNodeId)
                    && strangeCoinRule.encounterEpisode.FindNode(
                        strangeCoinRule.encounterEpisode.firstNodeId) != null,
                "StrangeCoin_0의 시작 노드를 찾지 못했습니다.");

            Require(theLittlesRule != null,
                "TheLittles_0의 고정 영업 슬롯 규칙이 없습니다.");
            Require(theLittlesRule.actionType == BusinessRequiredActionType.EncounterEpisode,
                "TheLittles_0 고정 규칙이 인카운터 액션이 아닙니다.");
            Require(theLittlesRule.timing == BusinessRequiredActionTiming.SequenceSlot
                    && theLittlesRule.sequenceSlot == 6,
                "TheLittles_0은 6번 영업 슬롯으로 설정되어야 합니다.");
            Require(theLittlesRule.condition != null
                    && theLittlesRule.condition.minDay == 5,
                "TheLittles_0 고정 규칙은 Day 5부터 활성화되어야 합니다.");
            Require(theLittlesRule.encounterEpisode.episodeType == EpisodeType.Encounter,
                "TheLittles_0의 EpisodeType이 Encounter가 아닙니다.");
            Require(theLittlesRule.encounterEpisode.triggerCondition != null
                    && theLittlesRule.encounterEpisode.triggerCondition.minDay == 5,
                "TheLittles_0 에피소드 자체의 시작 조건도 Day 5여야 합니다.");
            Require(!string.IsNullOrWhiteSpace(theLittlesRule.encounterEpisode.firstNodeId)
                    && theLittlesRule.encounterEpisode.FindNode(
                        theLittlesRule.encounterEpisode.firstNodeId) != null,
                "TheLittles_0의 시작 노드를 찾지 못했습니다.");

            for (int i = 0; i < settings.customerVisitDatabase.visits.Count; i++)
            {
                CustomerVisitData visit = settings.customerVisitDatabase.visits[i];
                if (visit == null)
                    continue;
                Require(visit.weight >= 0f,
                    $"손님 등장 가중치가 음수입니다: {visit.visitKey}");
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
            CustomerVisitData tagVisit = ScriptableObject.CreateInstance<CustomerVisitData>();
            CustomerOrderData order = ScriptableObject.CreateInstance<CustomerOrderData>();
            CustomerOrderData tagOrder = ScriptableObject.CreateInstance<CustomerOrderData>();
            EpisodeData episode = ScriptableObject.CreateInstance<EpisodeData>();
            EpisodeData secondEpisode = ScriptableObject.CreateInstance<EpisodeData>();
            EpisodeData sixthEpisode = ScriptableObject.CreateInstance<EpisodeData>();

            try
            {
                progress.LoadFrom(new SaveData());
                progress.SetCurrentDay(5);
                order.key = "validator_order";
                order.requestedRecipeId = "validator_recipe";
                visit.visitKey = "validator_visit";
                visit.reappearanceGroupKey = visit.visitKey;
                visit.weight = 1f;
                visit.initiallyAvailable = true;
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

                HashSet<string> recent = new(StringComparer.OrdinalIgnoreCase)
                {
                    visit.GetReappearanceKey()
                };
                BusinessVisitSelection blockedRecent = BusinessSequencePlanner.PickWeightedVisit(
                    pool,
                    progress,
                    recent,
                    null,
                    new System.Random(1));
                Require(blockedRecent == null,
                    "최근 등장한 손님을 후보 부족 상황에서 다시 선택했습니다.");

                readyVisit.visitKey = "validator_ready_visit";
                readyVisit.reappearanceGroupKey = readyVisit.visitKey;
                readyVisit.weight = 1f;
                readyVisit.initiallyAvailable = true;
                readyVisit.members.Add(
                    new CustomerVisitMember { characterKey = "validator_ready_customer" });
                readyVisit.orders.Add(new CustomerVisitOrderOption
                {
                    order = order,
                    weight = 1f,
                    condition = new EpisodeTriggerCondition()
                });
                List<CustomerVisitData> mixedPool = new() { visit, readyVisit };
                BusinessVisitSelection readySelected = BusinessSequencePlanner.PickWeightedVisit(
                    mixedPool,
                    progress,
                    recent,
                    null,
                    new System.Random(1));
                Require(readySelected?.Visit == readyVisit,
                    "최근 등장 제한에 없는 손님을 선택하지 못했습니다.");

                recent.Clear();
                BusinessVisitSelection afterTwoOthers = BusinessSequencePlanner.PickWeightedVisit(
                    pool,
                    progress,
                    recent,
                    null,
                    new System.Random(1));
                Require(afterTwoOthers?.Visit == visit,
                    "최근 2명 목록에서 빠진 손님이 후보로 복귀하지 않았습니다.");

                tagOrder.key = "validator_taste_order";
                tagOrder.orderType = CocktailOrderType.TasteOrder;
                tagOrder.tags.Add("validator_taste");
                tagVisit.visitKey = "validator_tag_visit";
                tagVisit.reappearanceGroupKey = tagVisit.visitKey;
                tagVisit.weight = 1f;
                tagVisit.initiallyAvailable = true;
                tagVisit.members.Add(
                    new CustomerVisitMember { characterKey = "validator_tag_customer" });
                tagVisit.orders.Add(new CustomerVisitOrderOption
                {
                    order = tagOrder,
                    weight = 1f,
                    condition = new EpisodeTriggerCondition()
                });
                database.visits.Add(tagVisit);

                List<CustomerVisitData> tagPool =
                    BusinessSequencePlanner.BuildEligibleVisitPool(database, progress);
                Require(tagPool.Contains(tagVisit),
                    "단일 태그 맛 주문만 가진 손님이 영업 풀에서 제외됐습니다.");
                CustomerVisitOrderOption selectedTagOrder =
                    BusinessSequencePlanner.PickWeightedOrder(
                        tagVisit,
                        progress,
                        new System.Random(1));
                Require(selectedTagOrder?.order == tagOrder,
                    "유효한 맛 주문을 영업 주문 후보로 선택하지 못했습니다.");

                tagOrder.orderType = CocktailOrderType.MoodOrder;
                Require(BusinessSequencePlanner.HasStructurallyValidOrderTarget(tagOrder),
                    "단일 태그 분위기 주문을 유효한 주문 대상으로 인정하지 않았습니다.");
                CustomerVisitOrderOption selectedMoodOrder =
                    BusinessSequencePlanner.PickWeightedOrder(
                        tagVisit,
                        progress,
                        new System.Random(1));
                Require(selectedMoodOrder?.order == tagOrder,
                    "유효한 분위기 주문을 영업 주문 후보로 선택하지 못했습니다.");
                tagOrder.tags.Add("unexpected_second_tag");
                Require(!BusinessSequencePlanner.HasStructurallyValidOrderTarget(tagOrder),
                    "복수 태그 조건 주문을 유효한 주문 대상으로 인정했습니다.");
                tagOrder.tags.RemoveAt(tagOrder.tags.Count - 1);
                tagOrder.orderType = CocktailOrderType.TasteOrder;

                episode.episodeId = "validator_episode";
                episode.episodeType = EpisodeType.Encounter;
                episode.triggerCondition = new EpisodeTriggerCondition();
                secondEpisode.episodeId = "validator_second_episode";
                secondEpisode.episodeType = EpisodeType.Encounter;
                secondEpisode.triggerCondition = new EpisodeTriggerCondition();
                sixthEpisode.episodeId = "validator_sixth_episode";
                sixthEpisode.episodeType = EpisodeType.Encounter;
                sixthEpisode.triggerCondition = new EpisodeTriggerCondition();
                BusinessRandomEncounterEntry encounterEntry = new()
                {
                    episode = episode,
                    weight = 1f
                };
                BusinessRandomEncounterEntry secondEncounterEntry = new()
                {
                    episode = secondEpisode,
                    weight = 1f
                };
                List<BusinessRandomEncounterEntry> encounterPool =
                    BusinessSequencePlanner.BuildEligibleRandomEncounterPool(
                        new List<BusinessRandomEncounterEntry>
                        {
                            encounterEntry,
                            secondEncounterEntry
                        },
                        progress);
                Require(encounterPool.Count == 2,
                    "조건을 만족한 랜덤 인카운터가 풀에 들어오지 않았습니다.");

                recent.Add(visit.GetReappearanceKey());
                BusinessSequenceSelection encounterBeforeCoolingCustomer =
                    BusinessSequencePlanner.PickWeightedSequence(
                        pool,
                        encounterPool,
                        null,
                        progress,
                        recent,
                        null,
                        null,
                        new System.Random(1));
                Require(encounterBeforeCoolingCustomer?.IsEncounter == true,
                    "실행 가능한 인카운터보다 최근 등장한 손님을 먼저 선택했습니다.");

                HashSet<string> startedEncounterIds = new(StringComparer.OrdinalIgnoreCase)
                {
                    episode.episodeId
                };
                BusinessSequenceSelection differentEncounterSameDay =
                    BusinessSequencePlanner.PickWeightedSequence(
                        pool,
                        encounterPool,
                        startedEncounterIds,
                        progress,
                        recent,
                        null,
                        null,
                        new System.Random(1));
                Require(differentEncounterSameDay?.Encounter?.episode == secondEpisode,
                    "하나의 인카운터 실행이 다른 종류의 당일 등장까지 막았습니다.");

                startedEncounterIds.Add(secondEpisode.episodeId);
                recent.Clear();
                BusinessSequenceSelection noRepeatedEncounter =
                    BusinessSequencePlanner.PickWeightedSequence(
                        pool,
                        encounterPool,
                        startedEncounterIds,
                        progress,
                        recent,
                        null,
                        null,
                        new System.Random(1));
                Require(noRepeatedEncounter?.Visit == visit,
                    "당일에 실행한 인카운터 ID가 다시 선택되었습니다.");

                HashSet<string> reservedTargets = new(StringComparer.OrdinalIgnoreCase)
                {
                    encounterEntry.TargetKey
                };
                List<BusinessRandomEncounterEntry> unreservedPool =
                    BusinessSequencePlanner.BuildEligibleRandomEncounterPool(
                        encounterPool,
                        progress,
                        reservedTargets);
                Require(unreservedPool.Count == 1
                        && unreservedPool[0].episode == secondEpisode,
                    "필수 인카운터를 랜덤 풀에서 예약하지 못했습니다.");

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
                List<BusinessRandomEncounterEntry> afterCompletionPool =
                    BusinessSequencePlanner.BuildEligibleRandomEncounterPool(
                        encounterPool,
                        progress);
                Require(afterCompletionPool.Count == 1
                        && afterCompletionPool[0].episode == secondEpisode,
                    "완료한 인카운터가 이후 영업일의 풀에 다시 들어왔습니다.");
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

                BusinessRequiredActionRule fixedSlot = new()
                {
                    ruleId = "validator_fixed_slot_episode",
                    actionType = BusinessRequiredActionType.EncounterEpisode,
                    condition = new EpisodeTriggerCondition { minDay = 5 },
                    priority = 100,
                    timing = BusinessRequiredActionTiming.SequenceSlot,
                    sequenceSlot = 3,
                    encounterEpisode = secondEpisode
                };
                BusinessRequiredActionRule sixthSlot = new()
                {
                    ruleId = "validator_sixth_slot_episode",
                    actionType = BusinessRequiredActionType.EncounterEpisode,
                    condition = new EpisodeTriggerCondition { minDay = 5 },
                    priority = 100,
                    timing = BusinessRequiredActionTiming.SequenceSlot,
                    sequenceSlot = 6,
                    encounterEpisode = sixthEpisode
                };
                List<BusinessRequiredActionRule> fixedSlotRules = new()
                {
                    fixedSlot,
                    sixthSlot
                };

                progress.SetCurrentDay(4);
                selected = BusinessSequencePlanner.PickNextRequiredAction(
                    fixedSlotRules,
                    progress,
                    BusinessRequiredActionTiming.SequenceSlot,
                    false,
                    null,
                    null,
                    3);
                Require(selected == null, "Day 5 전인데 고정 슬롯 인카운터가 선택됐습니다.");

                progress.SetCurrentDay(5);
                selected = BusinessSequencePlanner.PickNextRequiredAction(
                    fixedSlotRules,
                    progress,
                    BusinessRequiredActionTiming.SequenceSlot,
                    false,
                    null,
                    null,
                    2);
                Require(selected == null, "3번이 아닌 영업 슬롯에서 인카운터가 선택됐습니다.");
                selected = BusinessSequencePlanner.PickNextRequiredAction(
                    fixedSlotRules,
                    progress,
                    BusinessRequiredActionTiming.SequenceSlot,
                    false,
                    null,
                    null,
                    3);
                Require(selected == fixedSlot, "Day 5의 3번 영업 슬롯을 선택하지 못했습니다.");
                selected = BusinessSequencePlanner.PickNextRequiredAction(
                    fixedSlotRules,
                    progress,
                    BusinessRequiredActionTiming.SequenceSlot,
                    false,
                    null,
                    null,
                    6);
                Require(selected == sixthSlot, "Day 5의 6번 영업 슬롯을 선택하지 못했습니다.");
                selected = BusinessSequencePlanner.PickNextRequiredAction(
                    fixedSlotRules,
                    progress,
                    BusinessRequiredActionTiming.AfterTimer,
                    true,
                    null,
                    null,
                    3);
                Require(selected == null, "시간 종료 후 놓친 고정 슬롯을 강제 실행했습니다.");

                progress.SetCurrentDay(6);
                selected = BusinessSequencePlanner.PickNextRequiredAction(
                    fixedSlotRules,
                    progress,
                    BusinessRequiredActionTiming.SequenceSlot,
                    false,
                    null,
                    null,
                    3);
                Require(selected == fixedSlot,
                    "미완료 고정 슬롯 인카운터가 다음 날 같은 슬롯에 재등장하지 않았습니다.");
                progress.MarkEpisodeCompleted(secondEpisode.episodeId);
                selected = BusinessSequencePlanner.PickNextRequiredAction(
                    fixedSlotRules,
                    progress,
                    BusinessRequiredActionTiming.SequenceSlot,
                    false,
                    null,
                    null,
                    3);
                Require(selected == null, "완료한 고정 슬롯 인카운터가 다시 선택됐습니다.");
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
                UnityEngine.Object.DestroyImmediate(sixthEpisode);
                UnityEngine.Object.DestroyImmediate(secondEpisode);
                UnityEngine.Object.DestroyImmediate(episode);
                UnityEngine.Object.DestroyImmediate(tagOrder);
                UnityEngine.Object.DestroyImmediate(order);
                UnityEngine.Object.DestroyImmediate(tagVisit);
                UnityEngine.Object.DestroyImmediate(readyVisit);
                UnityEngine.Object.DestroyImmediate(visit);
                UnityEngine.Object.DestroyImmediate(database);
                UnityEngine.Object.DestroyImmediate(progressObject);
            }
        }

        private static void ValidateEpisodeCraftingCompatibility()
        {
            ValidateLegacyCraftingFields();
            ValidateEpisodeCraftingRequestTargets();
            ValidateStrangeCoinOneChoicePayment();
            ValidateExistingCraftingNodes();
            ValidateOrderTicketDialogueMemo();
            ValidateConditionOrderEvaluation();
            ValidateCraftingResultMapping();
        }

        private static void ValidateOrderTicketDialogueMemo()
        {
            CustomerOrderData order = ScriptableObject.CreateInstance<CustomerOrderData>();
            GameObject managerObject = new("BusinessShiftValidator_OrderTicketManager");

            try
            {
                order.orderDialogueAuthored = true;
                order.lines = new List<DialogueLine>
                {
                    new()
                    {
                        speakerName = "첫 화자",
                        text = "첫 주문 <b>대사</b>"
                    },
                    new()
                    {
                        speakerName = "둘째 화자",
                        text = "둘째 주문 대사"
                    }
                };

                string authoredMemo = OrderTicketMemoFormatter.Build(order, "공통 주문 대사");
                Require(
                    authoredMemo == "첫 주문 <b>대사</b>\n둘째 주문 대사",
                    "주문표가 주문 당시 말풍선 텍스트를 순서대로 보존하지 않습니다.");

                order.lines.Clear();
                Require(
                    OrderTicketMemoFormatter.Build(order, "공통 주문 대사") == string.Empty,
                    "의도적으로 비운 주문 대사가 주문표에서 비워지지 않습니다.");

                order.orderDialogueAuthored = false;
                Require(
                    OrderTicketMemoFormatter.Build(order, "공통 주문 대사") == "공통 주문 대사",
                    "구형 주문의 실제 공통 대사가 주문표에 반영되지 않습니다.");
                Require(
                    OrderTicketMemoFormatter.Build(order, "   ") == string.Empty,
                    "화면에 출력되지 않는 공백 공통 대사가 주문표에 남습니다.");

                OrderTicketManager manager =
                    managerObject.AddComponent<OrderTicketManager>();
                manager.Prepare("customer_order", authoredMemo);
                manager.Prepare("customer_order");

                FieldInfo hasOverrideField = typeof(OrderTicketManager).GetField(
                    "_hasPendingMemoOverride",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo memoOverrideField = typeof(OrderTicketManager).GetField(
                    "_pendingMemoOverride",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Require(hasOverrideField != null && memoOverrideField != null,
                    "주문표 런타임 대사 보존 필드를 찾지 못했습니다.");
                Require(
                    (bool)hasOverrideField.GetValue(manager)
                    && (string)memoOverrideField.GetValue(manager) == authoredMemo,
                    "같은 주문으로 제조를 시작할 때 주문 당시 대사가 유실됩니다.");

                manager.Prepare("episode_order");
                Require(
                    !(bool)hasOverrideField.GetValue(manager),
                    "다른 에피소드 주문표가 이전 손님 대사를 재사용합니다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(order);
            }
        }

        private static void ValidateConditionOrderEvaluation()
        {
            CocktailOrderGenerator generator = new(null, null);
            GeneratedCocktailOrder order = generator.GenerateOrder(
                CocktailOrderType.TasteOrder,
                requestedTags: new[] { "씁쓸함_Bitterness" },
                requestedConditionLabel: "씁쓸함_Bitterness");
            Require(order != null
                    && order.requestedRecipe == null
                    && order.requiredTasteTags.Count == 1
                    && order.requiredTasteTags.Contains("씁쓸함")
                    && order.requestedConditionLabel == "씁쓸함",
                "취향 주문이 기획용 영문 접미사를 제거한 단일 태그로 생성되지 않았습니다.");

            CocktailRecipe matchingRecipe = new() { id = "matching" };
            matchingRecipe.tasteTags.Add("씁쓸함");
            CocktailEvaluationResult matchingResult = new()
            {
                matchedRecipe = matchingRecipe,
                isSuccess = true,
                iceValid = true,
                glassValid = true
            };
            CocktailOrderEvaluator evaluator = new(null);
            CocktailOrderEvaluationResult success = evaluator.Evaluate(
                order,
                null,
                matchingResult);
            Require(success.isSuccess,
                "취향 태그가 맞는 실제 레시피를 조건 주문 성공으로 판정하지 않았습니다.");

            CocktailRecipe mismatchingRecipe = new() { id = "mismatching" };
            mismatchingRecipe.tasteTags.Add("달콤함");
            CocktailEvaluationResult mismatchingResult = new()
            {
                matchedRecipe = mismatchingRecipe,
                isSuccess = true,
                iceValid = true,
                glassValid = true
            };
            CocktailOrderEvaluationResult mismatch = evaluator.Evaluate(
                order,
                null,
                mismatchingResult);
            Require(!mismatch.isSuccess,
                "취향 태그가 다른 실제 레시피를 조건 주문 성공으로 판정했습니다.");

            MethodInfo resolveListedRecipe = typeof(BusinessOrderSessionController).GetMethod(
                "ResolveListedRecipe",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(resolveListedRecipe != null,
                "조건 주문 가격 기준 레시피 선택 함수를 찾지 못했습니다.");
            CocktailRecipe pricedRecipe = (CocktailRecipe)resolveListedRecipe.Invoke(
                null,
                new object[]
                {
                    order,
                    new CocktailOrderEvaluationResult
                    {
                        order = order,
                        requestedRecipeResult = matchingResult,
                        detectedRecipeResult = new CocktailEvaluationResult()
                    }
                });
            Require(pricedRecipe == matchingRecipe,
                "잔·얼음 오류가 있는 조건 주문의 가격 기준 레시피를 보존하지 못했습니다.");

            CocktailRecipe orderedRecipe = new() { id = "ordered" };
            CocktailRecipe actualRecipe = new() { id = "actual" };
            GeneratedCocktailOrder recipeOrder = new()
            {
                orderType = CocktailOrderType.EpisodeOrder,
                requestedRecipe = orderedRecipe,
                requestedRecipeId = orderedRecipe.id
            };
            CocktailRecipe actualPricedRecipe = (CocktailRecipe)resolveListedRecipe.Invoke(
                null,
                new object[]
                {
                    recipeOrder,
                    new CocktailOrderEvaluationResult
                    {
                        order = recipeOrder,
                        requestedRecipeResult = new CocktailEvaluationResult
                        {
                            matchedRecipe = orderedRecipe
                        },
                        detectedRecipeResult = new CocktailEvaluationResult
                        {
                            matchedRecipe = actualRecipe,
                            isSuccess = true
                        }
                    }
                });
            Require(actualPricedRecipe == actualRecipe,
                "다른 칵테일을 제출했을 때 주문 레시피가 아닌 실제 결과 레시피를 가격 기준으로 사용하지 않았습니다.");

            CocktailRecipe servingStylePricedRecipe = (CocktailRecipe)resolveListedRecipe.Invoke(
                null,
                new object[]
                {
                    recipeOrder,
                    new CocktailOrderEvaluationResult
                    {
                        order = recipeOrder,
                        requestedRecipeResult = new CocktailEvaluationResult
                        {
                            matchedRecipe = orderedRecipe,
                            ingredientsValid = true,
                            extrasValid = true,
                            glassValid = false,
                            iceValid = true,
                            shakeIceValid = true,
                            techniqueValid = true
                        },
                        detectedRecipeResult = new CocktailEvaluationResult()
                    }
                });
            Require(servingStylePricedRecipe == orderedRecipe,
                "재료는 맞고 잔·얼음만 틀린 결과 칵테일의 가격을 찾지 못했습니다.");

            GeneratedCocktailOrder moodOrder = generator.GenerateOrder(
                CocktailOrderType.MoodOrder,
                requestedTags: new[] { "고급스러운_Luxurious" },
                requestedConditionLabel: "고급스러운_Luxurious");
            Require(moodOrder != null
                    && moodOrder.requestedRecipe == null
                    && moodOrder.requiredMoodTags.Count == 1
                    && moodOrder.requiredMoodTags.Contains("고급스러운")
                    && moodOrder.requestedConditionLabel == "고급스러운",
                "분위기 주문이 기획용 영문 접미사를 제거한 단일 태그로 생성되지 않았습니다.");

            CocktailRecipe matchingMoodRecipe = new() { id = "matching_mood" };
            matchingMoodRecipe.moodTags.Add("고급스러운");
            CocktailEvaluationResult matchingMoodResult = new()
            {
                matchedRecipe = matchingMoodRecipe,
                isSuccess = true,
                iceValid = true,
                glassValid = true
            };
            CocktailOrderEvaluationResult moodSuccess = evaluator.Evaluate(
                moodOrder,
                null,
                matchingMoodResult);
            Require(moodSuccess.isSuccess,
                "분위기 태그가 맞는 실제 레시피를 조건 주문 성공으로 판정하지 않았습니다.");

            CocktailRecipe mismatchingMoodRecipe = new() { id = "mismatching_mood" };
            mismatchingMoodRecipe.moodTags.Add("포근한");
            CocktailOrderEvaluationResult moodMismatch = evaluator.Evaluate(
                moodOrder,
                null,
                new CocktailEvaluationResult
                {
                    matchedRecipe = mismatchingMoodRecipe,
                    isSuccess = true,
                    iceValid = true,
                    glassValid = true
                });
            Require(!moodMismatch.isSuccess
                    && moodMismatch.outcome == CocktailOrderEvaluationOutcome.MidWrongMenu,
                "분위기 태그가 다른 실제 레시피를 잘못된 메뉴로 판정하지 않았습니다.");
        }

        private static bool HasOrderableRecipeForCondition(
            CocktailRecipeCatalog recipes,
            CocktailOrderType orderType,
            string requestedTag)
        {
            if (recipes == null || string.IsNullOrWhiteSpace(requestedTag))
                return false;

            foreach (CocktailRecipe recipe in recipes.OrderableRecipes)
            {
                if (recipe == null
                    || recipe.evaluationGrade != CocktailRecipeEvaluationGrade.Good
                    || !string.IsNullOrWhiteSpace(recipe.baseRecipeId))
                {
                    continue;
                }

                HashSet<string> tags = orderType == CocktailOrderType.TasteOrder
                    ? recipe.tasteTags
                    : recipe.moodTags;
                if (tags != null && tags.Contains(requestedTag))
                    return true;
            }

            return false;
        }

        private static bool HasPricedOrderableRecipeForCondition(
            CocktailRecipeCatalog recipes,
            CocktailOrderType orderType,
            string requestedTag,
            GameCurrency currency)
        {
            if (recipes == null || string.IsNullOrWhiteSpace(requestedTag))
                return false;

            foreach (CocktailRecipe recipe in recipes.OrderableRecipes)
            {
                if (recipe == null
                    || recipe.evaluationGrade != CocktailRecipeEvaluationGrade.Good
                    || !string.IsNullOrWhiteSpace(recipe.baseRecipeId)
                    || recipe.GetPrice(currency) <= 0)
                {
                    continue;
                }

                HashSet<string> tags = orderType == CocktailOrderType.TasteOrder
                    ? recipe.tasteTags
                    : recipe.moodTags;
                if (tags != null && tags.Contains(requestedTag))
                    return true;
            }

            return false;
        }

        private static void ValidateTechnicalFailureContract()
        {
            GameObject controllerObject = new("BusinessOrderSessionValidator");
            BusinessOrderFlowSettings settings =
                ScriptableObject.CreateInstance<BusinessOrderFlowSettings>();
            try
            {
                CustomerSpawner spawner = controllerObject.AddComponent<CustomerSpawner>();
                BusinessOrderSessionController controller =
                    controllerObject.AddComponent<BusinessOrderSessionController>();
                controller.Initialize(null, spawner, null, null, null, null, settings);

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

                callbackCount = 0;
                completed = null;
                started = controller.BeginOrder(new OrderSessionRequest
                {
                    sessionId = "validator_missing_customer_order",
                    owner = OrderSessionOwner.Business,
                    customerVisitKey = "validator_visit",
                    customerOrderKey = "validator_order_that_does_not_exist",
                    requestedRecipeId = "rec_1006",
                    presentOrder = true,
                    presentFeedback = false,
                    applyProgressRewards = false
                }, result =>
                {
                    callbackCount++;
                    completed = result;
                });

                Require(started, "누락 손님 주문이 기술 실패 결과를 반환하지 않았습니다.");
                Require(callbackCount == 1, "누락 손님 주문의 완료 콜백이 정확히 한 번 호출되지 않았습니다.");
                Require(completed != null && completed.technicalFailure,
                    "누락 손님 주문이 기술 실패로 분류되지 않았습니다.");
                Require(!string.IsNullOrWhiteSpace(completed.failureReason)
                        && completed.failureReason.Contains("validator_visit")
                        && completed.failureReason.Contains("validator_order_that_does_not_exist")
                        && completed.failureReason.Contains("rec_1006"),
                    "누락 손님 주문의 실패 원인에 방문·주문·레시피 정보가 없습니다.");
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

                    if (string.IsNullOrWhiteSpace(node.craftingOrderTarget))
                        manualCraftingNodes++;
                    else
                    {
                        actualCraftingNodes++;
                        Require(node.craftingPaymentEnabled,
                            $"실제 에피소드 제조 노드의 가격 지급이 비활성화되어 있습니다: "
                            + $"{episode.episodeId}/{node.nodeId}");
                        Require(Enum.IsDefined(typeof(GameCurrency), node.craftingPaymentCurrency),
                            $"실제 에피소드 제조 노드의 지급 통화가 유효하지 않습니다: "
                            + $"{episode.episodeId}/{node.nodeId}/{node.craftingPaymentCurrency}");
                        bool tagOrder = node.craftingOrderType == CocktailOrderType.TasteOrder
                            || node.craftingOrderType == CocktailOrderType.MoodOrder;
                        bool supportedOrderType = tagOrder
                            || node.craftingOrderType == CocktailOrderType.EpisodeOrder;
                        Require(supportedOrderType,
                            $"에피소드 제조 노드에 지원하지 않는 주문 유형이 있습니다: "
                            + $"{episode.episodeId}/{node.nodeId}/{node.craftingOrderType}");
                        if (tagOrder)
                        {
                            string requestedTag = CocktailOrderTagRules.Normalize(
                                node.craftingOrderTarget);
                            Require(HasPricedOrderableRecipeForCondition(
                                    recipeCatalog,
                                    node.craftingOrderType,
                                    requestedTag,
                                    node.craftingPaymentCurrency),
                                $"에피소드 조건 주문 태그에 대응하는 가격이 설정된 레시피가 없습니다: "
                                + $"{episode.episodeId}/{node.nodeId}/{requestedTag}/"
                                + $"{node.craftingPaymentCurrency}");
                        }
                        else
                        {
                            bool hasPricedRecipe = recipeCatalog.TryGet(
                                    node.craftingOrderTarget,
                                    out CocktailRecipe recipe)
                                && recipe != null
                                && recipe.isOrderable
                                && recipe.GetPrice(node.craftingPaymentCurrency) > 0;
                            Require(hasPricedRecipe,
                                $"실제 제조 노드의 주문 가능한 레시피 또는 가격이 없습니다: "
                                + $"{episode.episodeId}/{node.nodeId}/{node.craftingOrderTarget}/"
                                + $"{node.craftingPaymentCurrency}");
                        }
                        OrderTicketData registeredTicket =
                            ticketDatabase.FindByKey(node.craftingTicketKey);
                        Require(!string.IsNullOrWhiteSpace(node.craftingTicketKey)
                                && registeredTicket != null,
                            $"실제 제조 노드의 주문표가 없습니다: "
                            + $"{episode.episodeId}/{node.nodeId}/{node.craftingTicketKey}");
                        Require(node.craftingOrderTicket != null
                                && node.craftingOrderTicket == registeredTicket,
                            $"실제 제조 노드에 주문표 에셋이 직접 연결되지 않았습니다: "
                            + $"{episode.episodeId}/{node.nodeId}/{node.craftingTicketKey}");
                    }

                    Require(!string.IsNullOrWhiteSpace(node.GetNextNodeId(CraftingJobResult.Good)),
                        $"제조 노드의 Good 분기가 없습니다: {episode.episodeId}/{node.nodeId}");
                    Require(!string.IsNullOrWhiteSpace(node.GetNextNodeId(CraftingJobResult.Bad)),
                        $"제조 노드의 Bad 분기가 없습니다: {episode.episodeId}/{node.nodeId}");
                }
            }

            Require(actualCraftingNodes > 0,
                "craftingOrderTarget이 있는 실제 제조 노드를 찾지 못했습니다.");
            Require(manualCraftingNodes > 0,
                "기존 수동 판정 호환을 검증할 제조 노드를 찾지 못했습니다.");
        }

        private static void ValidateEpisodeCraftingRequestTargets()
        {
            OrderTicketData recipeTicket = ScriptableObject.CreateInstance<OrderTicketData>();
            recipeTicket.key = "validator_recipe_ticket";
            EpisodeNode recipeNode = new()
            {
                craftingOrderType = CocktailOrderType.EpisodeOrder,
                craftingOrderTarget = "rec_1003",
                craftingOrderTicket = recipeTicket,
                craftingTicketKey = "validator_recipe_ticket",
                craftingPaymentEnabled = true,
                craftingPaymentCurrency = GameCurrency.StrangeCoin,
                craftingPaymentMultiplier = 2f
            };
            Require(EpisodeCraftingBridge.TryBuildRequest(
                    recipeNode,
                    "validator_recipe",
                    out OrderSessionRequest recipeRequest)
                    && recipeRequest.orderType == CocktailOrderType.EpisodeOrder
                    && recipeRequest.requestedRecipeId == "rec_1003"
                    && recipeRequest.ticketData == recipeTicket
                    && recipeRequest.applyPayment
                    && recipeRequest.recordSale
                    && recipeRequest.paymentCurrency == GameCurrency.StrangeCoin
                    && Mathf.Approximately(recipeRequest.paymentMultiplier, 2f)
                    && recipeRequest.requestedTags.Count == 0,
                "에피소드 레시피 target을 지정 레시피 주문으로 변환하지 못했습니다.");
            UnityEngine.Object.DestroyImmediate(recipeTicket);

            EpisodeNode tasteNode = new()
            {
                craftingOrderType = CocktailOrderType.TasteOrder,
                craftingOrderTarget = "씁쓸함_Bitterness"
            };
            Require(EpisodeCraftingBridge.TryBuildRequest(
                    tasteNode,
                    "validator_taste",
                    out OrderSessionRequest tasteRequest)
                    && string.IsNullOrEmpty(tasteRequest.requestedRecipeId)
                    && tasteRequest.requestedTags.Count == 1
                    && tasteRequest.requestedTags[0] == "씁쓸함"
                    && tasteRequest.requestedConditionLabel == "씁쓸함"
                    && Mathf.Approximately(tasteRequest.paymentMultiplier, 1f),
                "에피소드 맛 target을 단일 정규화 태그 주문으로 변환하지 못했습니다.");

            EpisodeNode moodNode = new()
            {
                craftingOrderType = CocktailOrderType.MoodOrder,
                craftingOrderTarget = "고급스러운_Luxurious"
            };
            Require(EpisodeCraftingBridge.TryBuildRequest(
                    moodNode,
                    "validator_mood",
                    out OrderSessionRequest moodRequest)
                    && string.IsNullOrEmpty(moodRequest.requestedRecipeId)
                    && moodRequest.requestedTags.Count == 1
                    && moodRequest.requestedTags[0] == "고급스러운",
                "에피소드 분위기 target을 단일 정규화 태그 주문으로 변환하지 못했습니다.");

            moodNode.craftingOrderType = CocktailOrderType.RecipeModifierOrder;
            Require(!EpisodeCraftingBridge.TryBuildRequest(
                    moodNode,
                    "validator_unsupported",
                    out _),
                "에피소드에서 지원하지 않는 주문 유형을 허용했습니다.");
        }

        private static void ValidateStrangeCoinOneChoicePayment()
        {
            const string episodePath =
                "Assets/Resources/EpisodeData/EpisodeData_StrangeCoin_1.asset";
            const string csvPath =
                "Assets/Data/EpisodeData/에피소드 - EpisodeData_StrangeCoin_1.csv.csv";

            EpisodeData episode = AssetDatabase.LoadAssetAtPath<EpisodeData>(episodePath);
            Require(episode != null, "StrangeCoin_1 에피소드 데이터를 찾지 못했습니다.");

            EpisodeNode moneyPath = episode.FindNode("65_money");
            EpisodeNode coinPath = episode.FindNode("65_coin");
            Require(moneyPath != null && coinPath != null,
                "StrangeCoin_1의 선택지별 결제 제조 노드가 없습니다.");
            Require(episode.FindNode("64_1_3")?.nextNodeId == "65_money",
                "공식 화폐 선택 경로가 Money 제조 노드로 이어지지 않습니다.");
            Require(episode.FindNode("64_2_4")?.nextNodeId == "65_coin",
                "동전 선택 경로가 StrangeCoin 제조 노드로 이어지지 않습니다.");
            Require(episode.FindNode("65") == null,
                "StrangeCoin_1에 통화를 구분하지 않는 기존 65 제조 노드가 남아 있습니다.");

            Require(moneyPath.craftingPaymentEnabled
                    && moneyPath.craftingPaymentCurrency == GameCurrency.Money
                    && Mathf.Approximately(moneyPath.craftingPaymentMultiplier, 2f),
                "StrangeCoin_1 공식 화폐 선택이 Money 2배 결제로 설정되지 않았습니다.");
            Require(coinPath.craftingPaymentEnabled
                    && coinPath.craftingPaymentCurrency == GameCurrency.StrangeCoin
                    && Mathf.Approximately(coinPath.craftingPaymentMultiplier, 2f),
                "StrangeCoin_1 동전 선택이 StrangeCoin 2배 결제로 설정되지 않았습니다.");
            Require(moneyPath.craftingOrderTarget == "rec_1006"
                    && coinPath.craftingOrderTarget == "rec_1006"
                    && moneyPath.craftingTicketKey == "sc1_f72"
                    && coinPath.craftingTicketKey == "sc1_f72",
                "StrangeCoin_1 선택지별 제조 노드의 주문 대상이 서로 다릅니다.");
            Require(moneyPath.GetNextNodeId(CraftingJobResult.Good) == "65_1_1"
                    && coinPath.GetNextNodeId(CraftingJobResult.Good) == "65_1_1"
                    && moneyPath.GetNextNodeId(CraftingJobResult.Bad) == "65_2_1"
                    && coinPath.GetNextNodeId(CraftingJobResult.Bad) == "65_2_1",
                "StrangeCoin_1 선택지별 제조 결과 분기가 기존 대사로 합류하지 않습니다.");

            Require(EpisodeCraftingBridge.TryBuildRequest(
                    moneyPath,
                    "validator_sc1_money",
                    out OrderSessionRequest moneyRequest)
                    && moneyRequest.paymentCurrency == GameCurrency.Money
                    && Mathf.Approximately(moneyRequest.paymentMultiplier, 2f),
                "StrangeCoin_1 공식 화폐 제조 요청을 Money 2배로 만들지 못했습니다.");
            Require(EpisodeCraftingBridge.TryBuildRequest(
                    coinPath,
                    "validator_sc1_coin",
                    out OrderSessionRequest coinRequest)
                    && coinRequest.paymentCurrency == GameCurrency.StrangeCoin
                    && Mathf.Approximately(coinRequest.paymentMultiplier, 2f),
                "StrangeCoin_1 동전 제조 요청을 StrangeCoin 2배로 만들지 못했습니다.");
            Require(BusinessOrderPriceRules.ApplyPaymentMultiplier(189, 2f) == 378
                    && BusinessOrderPriceRules.ApplyPaymentMultiplier(3, 2f) == 6,
                "StrangeCoin_1의 통화별 2배 가격 계산이 올바르지 않습니다.");
            Require(BusinessOrderPriceRules.ApplyPaymentMultiplier(189, 0f) == 189,
                "결제 배율이 없는 기존 주문이 1배 가격으로 호환되지 않습니다.");

            EpisodeNode f54Order = episode.FindNode("8");
            Require(f54Order != null
                    && f54Order.craftingPaymentCurrency == GameCurrency.Money
                    && Mathf.Approximately(f54Order.craftingPaymentMultiplier, 1f),
                "StrangeCoin_1의 F54 주문에 선택지 전용 2배 결제가 적용됐습니다.");

            Require(File.Exists(csvPath), "StrangeCoin_1 원본 CSV를 찾지 못했습니다.");
            string sourceCsv = File.ReadAllText(csvPath);
            Require(sourceCsv.Contains("craftingPaymentMultiplier")
                    && sourceCsv.Contains("65_money,,,,,TRUE,sc1_f72,rec_1006,,,,,TRUE,Money,2")
                    && sourceCsv.Contains("65_coin,,,,,TRUE,sc1_f72,rec_1006,,,,,TRUE,StrangeCoin,2"),
                "StrangeCoin_1의 선택지별 통화/2배 설정이 원본 CSV에 보존되지 않았습니다.");
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
                detectedOther,
                CocktailOrderEvaluationOutcome.MidWrongMenu);
            Require(EpisodeCraftingResultMapper.Map(wrongMenu) == CraftingJobResult.MidWrongMenu,
                "잘못된 메뉴 제조 결과 매핑에 실패했습니다.");

            CocktailEvaluationResult detectedTagMismatch = new()
            {
                matchedRecipe = otherRecipe,
                isSuccess = true,
                iceValid = true,
                glassValid = true
            };
            BusinessOrderSessionResult tagMismatch = new()
            {
                outcome = OrderSessionOutcome.Served,
                accepted = true,
                grade = OrderEvaluationGrade.Mid,
                evaluation = new CocktailOrderEvaluationResult
                {
                    order = new GeneratedCocktailOrder
                    {
                        orderType = CocktailOrderType.TasteOrder,
                        requestedConditionLabel = "쓴맛"
                    },
                    requestedRecipeResult = detectedTagMismatch,
                    detectedRecipeResult = detectedTagMismatch,
                    outcome = CocktailOrderEvaluationOutcome.MidWrongMenu,
                    isSuccess = false
                }
            };
            Require(CraftingResultMapper.Map(tagMismatch) == CraftingJobResult.MidWrongMenu,
                "취향 태그 불일치가 잘못된 메뉴 결과로 매핑되지 않았습니다.");

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
                    null,
                    CocktailOrderEvaluationOutcome.MidIceGlass)) == CraftingJobResult.MidIceGlass,
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
                matchedVariant,
                CocktailOrderEvaluationOutcome.MidIce);
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
            CocktailEvaluationResult detected,
            CocktailOrderEvaluationOutcome evaluationOutcome = CocktailOrderEvaluationOutcome.Bad)
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
                        detectedRecipeResult = detected,
                        outcome = evaluationOutcome
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
