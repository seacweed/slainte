using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Slainte.Bartending;
using Slainte.Content;
using Slainte.Economy;
using Slainte.TV;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public sealed class CustomerCsvImportReport
    {
        public int sourceRows;
        public int createdCharacters;
        public int updatedCharacters;
        public int createdVisits;
        public int updatedVisits;
        public int createdOrders;
        public int updatedOrders;
        public readonly List<string> warnings = new();
        public readonly List<string> errors = new();

        public string Summary()
        {
            return $"원본 {sourceRows}행, 캐릭터 생성/갱신 {createdCharacters}/{updatedCharacters}, "
                + $"방문 생성/갱신 {createdVisits}/{updatedVisits}, "
                + $"주문 생성/갱신 {createdOrders}/{updatedOrders}, "
                + $"경고 {warnings.Count}, 오류 {errors.Count}";
        }
    }

    public static class CustomerPlanningCsvImporter
    {
        public const string RootFolder = BusinessAssetPaths.CustomerImportRoot;
        public const string CharacterFolder = RootFolder + "/DraftCharacters";
        public const string VisitFolder = RootFolder + "/DraftVisits";
        public const string OrderFolder = RootFolder + "/DraftOrders";
        public const string AttributeTagPrefix = "customer_attribute:";
        public const string NightPatrolAttributeTag = AttributeTagPrefix + "야간순찰";

        private const string CharacterDatabasePath = BusinessAssetPaths.CharacterDatabase;
        private const string VisitDatabasePath =
            ProjectResourcePaths.AssetRoot
            + ProjectResourcePaths.BusinessCustomerVisitDatabase
            + ".asset";
        private const string OrderDatabasePath = BusinessAssetPaths.CustomerOrderDatabase;

        private static readonly Dictionary<string, string> RecipeNameAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "갓 레이디", "갓레이디" }
            };

        private static readonly Dictionary<string, string> CharacterKeyAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "cartha", "kartha" },
                { "eliud", "eliot" }
            };

        public static string DefaultCsvPath
        {
            get
            {
                string downloads = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");
                string updated = Path.Combine(
                    downloads,
                    "Data_slainte.csv - 손님 (1).csv");
                return File.Exists(updated)
                    ? updated
                    : Path.Combine(downloads, "Data_slainte.csv - 손님.csv");
            }
        }

        [MenuItem("Slainte/데이터/손님 CSV 드래프트 임포트")]
        public static void ImportDefaultFromMenu()
        {
            Import(DefaultCsvPath, showDialog: true);
        }

        [MenuItem("Slainte/데이터/손님 CSV 선택하여 드래프트 임포트")]
        public static void ImportSelectedFromMenu()
        {
            string path = EditorUtility.OpenFilePanel("손님 CSV 선택", string.Empty, "csv");
            if (!string.IsNullOrWhiteSpace(path))
                Import(path, showDialog: true);
        }

        public static void ImportDefaultFromCommandLine()
        {
            CustomerCsvImportReport report = Import(DefaultCsvPath, showDialog: false);
            if (report.errors.Count > 0)
                throw new InvalidDataException(string.Join("\n", report.errors));
        }

        public static void ImportAndPublishDefaultFromCommandLine()
        {
            ImportDefaultFromCommandLine();
            CustomerDialogueImportReport dialogueReport =
                CustomerDialogueCsvImporter.Import(showDialog: false);
            if (dialogueReport.errors.Count > 0)
                throw new InvalidDataException(string.Join("\n", dialogueReport.errors));
            PublishValidatedDrafts(showDialog: false);
        }

        public static CustomerCsvImportReport Import(string csvPath, bool showDialog)
        {
            CustomerCsvImportReport report = new();
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
            {
                report.errors.Add($"손님 CSV를 찾을 수 없습니다: {csvPath}");
                Finish(report, showDialog);
                return report;
            }

            List<SourceCustomerRow> rows = ParseAndValidate(csvPath, report);
            report.sourceRows = rows.Count;
            if (report.errors.Count > 0)
            {
                Finish(report, showDialog);
                return report;
            }

            EnsureFolder(CharacterFolder);
            EnsureFolder(VisitFolder);
            EnsureFolder(OrderFolder);

            Dictionary<string, CharacterData> characters = LoadAssetsByKey<CharacterData>(
                asset => asset.key);
            Dictionary<string, CustomerVisitData> visits = LoadAssetsByKey<CustomerVisitData>(
                asset => !string.IsNullOrWhiteSpace(asset.sourceCustomerId)
                    ? asset.sourceCustomerId
                    : asset.visitKey);
            Dictionary<string, CustomerOrderData> orders = LoadAssetsByKey<CustomerOrderData>(
                asset => asset.key);
            Dictionary<string, CocktailRecipeDef> recipes = BuildRecipeNameMap(report);
            if (report.errors.Count > 0)
            {
                Finish(report, showDialog);
                return report;
            }
            HashSet<string> incomingIds = new(
                rows.Select(row => row.sourceId),
                StringComparer.OrdinalIgnoreCase);

            foreach (CustomerVisitData existing in visits.Values)
            {
                if (existing != null
                    && !string.IsNullOrWhiteSpace(existing.sourceCustomerId)
                    && AssetDatabase.GetAssetPath(existing).StartsWith(
                        VisitFolder,
                        StringComparison.OrdinalIgnoreCase)
                    && !incomingIds.Contains(existing.sourceCustomerId))
                {
                    report.warnings.Add(
                        $"CSV에서 사라진 드래프트 방문은 삭제하지 않았습니다: {existing.visitKey}");
                }
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    SourceCustomerRow row = rows[i];
                    CharacterData character = UpsertCharacter(row, characters, report);
                    CustomerVisitData visit = UpsertVisit(row, character, visits, report);
                    UpsertOrders(row, visit, orders, recipes, report);
                }
            }
            catch (Exception exception)
            {
                report.errors.Add(exception.ToString());
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Finish(report, showDialog);
            return report;
        }

        [MenuItem("Slainte/데이터/검증된 손님 드래프트 게시")]
        public static void PublishValidatedDraftsFromMenu()
        {
            PublishValidatedDrafts(showDialog: true);
        }

        public static void PublishValidatedDrafts(bool showDialog)
        {
            List<string> errors = ValidateDraftsForPublish(out List<CustomerVisitData> visits);
            if (errors.Count > 0)
            {
                string message = string.Join("\n", errors.Take(20));
                Debug.LogError("[손님 드래프트 게시] 중단\n" + string.Join("\n", errors));
                if (Application.isBatchMode || !showDialog)
                    throw new InvalidDataException(message);
                EditorUtility.DisplayDialog("손님 드래프트 게시 실패", message, "확인");
                return;
            }

            CharacterDatabase characterDatabase =
                AssetDatabase.LoadAssetAtPath<CharacterDatabase>(CharacterDatabasePath);
            CustomerVisitDatabase visitDatabase =
                AssetDatabase.LoadAssetAtPath<CustomerVisitDatabase>(VisitDatabasePath);
            CustomerOrderDatabase orderDatabase =
                AssetDatabase.LoadAssetAtPath<CustomerOrderDatabase>(OrderDatabasePath);
            if (characterDatabase == null || visitDatabase == null || orderDatabase == null)
            {
                if (Application.isBatchMode || !showDialog)
                    throw new InvalidDataException(
                        "캐릭터, 손님 방문 또는 손님 주문 데이터베이스를 찾을 수 없습니다.");
                EditorUtility.DisplayDialog(
                    "손님 드래프트 게시 실패",
                    "캐릭터, 손님 방문 또는 손님 주문 데이터베이스를 찾을 수 없습니다.",
                    "확인");
                return;
            }

            int addedCharacters = 0;
            int addedVisits = 0;
            int addedOrders = 0;
            int updatedOrders = 0;
            orderDatabase.customers ??= new List<CustomerOrderData>();
            for (int i = 0; i < visits.Count; i++)
            {
                CustomerVisitData visit = visits[i];
                for (int memberIndex = 0; memberIndex < visit.members.Count; memberIndex++)
                {
                    CharacterData character = FindCharacterByKey(
                        visit.members[memberIndex].characterKey);
                    if (character != null && characterDatabase.FindByKey(character.key) == null)
                    {
                        characterDatabase.characters.Add(character);
                        addedCharacters++;
                    }
                }

                if (visitDatabase.FindByKey(visit.visitKey) == null)
                {
                    visitDatabase.visits.Add(visit);
                    addedVisits++;
                }
            }

            List<CustomerOrderData> publishedOrders = CollectPublishedOrders(visits);
            for (int i = 0; i < publishedOrders.Count; i++)
            {
                CustomerOrderData order = publishedOrders[i];
                int existingIndex = FindOrderIndex(orderDatabase, order.key);
                if (existingIndex < 0)
                {
                    orderDatabase.customers.Add(order);
                    addedOrders++;
                }
                else if (orderDatabase.customers[existingIndex] != order)
                {
                    orderDatabase.customers[existingIndex] = order;
                    updatedOrders++;
                }
            }

            for (int i = 0; i < publishedOrders.Count; i++)
            {
                CustomerOrderData order = publishedOrders[i];
                if (orderDatabase.FindByKey(order.key) != order)
                {
                    throw new InvalidDataException(
                        $"게시한 주문을 손님 주문 DB에서 동일한 에셋으로 조회할 수 없습니다: {order.key}");
                }
            }

            EditorUtility.SetDirty(characterDatabase);
            EditorUtility.SetDirty(visitDatabase);
            EditorUtility.SetDirty(orderDatabase);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[손님 드래프트 게시] 캐릭터 {addedCharacters}개, 방문 {addedVisits}개, "
                + $"주문 추가/갱신 {addedOrders}/{updatedOrders}개를 등록했습니다.");
            if (showDialog && !Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "손님 드래프트 게시 완료",
                    $"캐릭터 {addedCharacters}개, 방문 {addedVisits}개, "
                    + $"주문 추가/갱신 {addedOrders}/{updatedOrders}개를 등록했습니다.",
                    "확인");
            }
        }

        public static List<string> ValidateDraftsForPublish(
            out List<CustomerVisitData> visits)
        {
            visits = LoadAssetsInFolder<CustomerVisitData>(VisitFolder);
            List<string> errors = new();
            if (visits.Count == 0)
            {
                errors.Add("게시할 손님 드래프트가 없습니다.");
                return errors;
            }

            ItemDefCatalog items = ItemDefCatalog.LoadFromResources(
                ProjectResourcePaths.BartendingItems,
                null);
            CocktailRecipeCatalog recipes = CocktailRecipeDataLoader.LoadDefault(items);
            bool hasNightPatrol = false;
            Dictionary<string, CustomerOrderData> ordersByKey =
                new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < visits.Count; i++)
            {
                CustomerVisitData visit = visits[i];
                if (visit == null || string.IsNullOrWhiteSpace(visit.visitKey))
                {
                    errors.Add("방문 키가 없는 드래프트가 있습니다.");
                    continue;
                }

                NormalizeVisitCharacterKeys(visit);

                hasNightPatrol |= visit.tags != null && visit.tags.Any(
                    tag => string.Equals(
                        tag,
                        NightPatrolAttributeTag,
                        StringComparison.OrdinalIgnoreCase));
                if (visit.members == null || visit.members.Count == 0)
                {
                    errors.Add($"{visit.visitKey}: 구성원이 없습니다.");
                    continue;
                }

                for (int memberIndex = 0; memberIndex < visit.members.Count; memberIndex++)
                {
                    string characterKey = visit.members[memberIndex]?.characterKey;
                    CharacterData character = FindCharacterByKey(characterKey);
                    if (character == null)
                    {
                        errors.Add($"{visit.visitKey}: 캐릭터 {characterKey}를 찾을 수 없습니다.");
                    }
                    else if (!HasPresentationSprite(character))
                    {
                        Debug.LogWarning(
                            $"[손님 드래프트 게시] {visit.visitKey}: "
                            + $"캐릭터 {characterKey} 이미지가 없습니다.");
                    }
                }

                int plannedCount = visit.plannedOrderNames?.Count ?? 0;
                int conditionOrderCount =
                    (string.IsNullOrWhiteSpace(visit.preferredTasteKey) ? 0 : 1)
                    + (string.IsNullOrWhiteSpace(visit.preferredAtmosphereKey) ? 0 : 1);
                int expectedCount = plannedCount + conditionOrderCount;
                int connectedCount = visit.orders?.Count ?? 0;
                if (connectedCount == 0)
                {
                    errors.Add(
                        $"{visit.visitKey}: 기획 주문 {expectedCount}개 중 {connectedCount}개만 연결됐습니다.");
                    continue;
                }
                if (expectedCount == 0 || connectedCount != expectedCount)
                {
                    errors.Add(
                        $"{visit.visitKey}: 기획 주문 {expectedCount}개 중 "
                        + $"{connectedCount}개만 연결됐습니다.");
                }

                for (int orderIndex = 0; orderIndex < visit.orders.Count; orderIndex++)
                {
                    CustomerOrderData order = visit.orders[orderIndex]?.order;
                    if (order == null || string.IsNullOrWhiteSpace(order.key))
                    {
                        errors.Add($"{visit.visitKey}: 키가 없는 주문이 연결됐습니다.");
                        continue;
                    }

                    if (ordersByKey.TryGetValue(order.key, out CustomerOrderData duplicate)
                        && duplicate != order)
                    {
                        errors.Add(
                            $"{visit.visitKey}: 서로 다른 주문 에셋이 같은 키를 사용합니다: {order.key}");
                    }
                    else
                    {
                        ordersByKey[order.key] = order;
                    }

                    bool tagOrder = order.orderType == CocktailOrderType.TasteOrder
                        || order.orderType == CocktailOrderType.MoodOrder;
                    if (tagOrder)
                    {
                        int validTagCount = order.tags?.Count(
                            tag => !string.IsNullOrWhiteSpace(tag)) ?? 0;
                        if (validTagCount != 1)
                        {
                            errors.Add(
                                $"{visit.visitKey}: 조건 주문에는 정확히 하나의 태그가 필요합니다.");
                        }
                    }
                    else if (!recipes.TryGet(order.requestedRecipeId, out CocktailRecipe recipe)
                        || recipe == null
                        || !recipe.isOrderable)
                    {
                        errors.Add($"{visit.visitKey}: 주문 레시피 연결이 유효하지 않습니다.");
                    }
                    else if (!order.orderDialogueAuthored
                        && (order.lines == null || order.lines.Count == 0))
                    {
                        Debug.LogWarning(
                            $"[손님 드래프트 게시] {visit.visitKey}/{order.key}: "
                            + "주문 대사가 없어 기본 주문 표시를 사용합니다.");
                    }
                }
            }

            if (!hasNightPatrol)
                errors.Add($"TV 대상 태그가 손님 드래프트에 없습니다: {NightPatrolAttributeTag}");
            return errors;
        }

        private static List<CustomerOrderData> CollectPublishedOrders(
            IReadOnlyList<CustomerVisitData> visits)
        {
            Dictionary<string, CustomerOrderData> byKey =
                new(StringComparer.OrdinalIgnoreCase);
            if (visits != null)
            {
                for (int visitIndex = 0; visitIndex < visits.Count; visitIndex++)
                {
                    CustomerVisitData visit = visits[visitIndex];
                    if (visit?.orders == null)
                        continue;

                    for (int orderIndex = 0; orderIndex < visit.orders.Count; orderIndex++)
                    {
                        CustomerOrderData order = visit.orders[orderIndex]?.order;
                        if (order != null && !string.IsNullOrWhiteSpace(order.key))
                            byKey[order.key] = order;
                    }
                }
            }

            return byKey.Values
                .OrderBy(order => order.key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int FindOrderIndex(CustomerOrderDatabase database, string key)
        {
            if (database?.customers == null || string.IsNullOrWhiteSpace(key))
                return -1;

            for (int i = 0; i < database.customers.Count; i++)
            {
                CustomerOrderData existing = database.customers[i];
                if (existing != null
                    && string.Equals(existing.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static List<SourceCustomerRow> ParseAndValidate(
            string path,
            CustomerCsvImportReport report)
        {
            string text = File.ReadAllText(path).TrimStart('\uFEFF');
            List<CsvRow> csvRows = CsvTable.Parse(text);
            List<SourceCustomerRow> rows = new();
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> displayNameByCharacter =
                new(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < csvRows.Count; i++)
            {
                CsvRow csv = csvRows[i];
                string sourceId = csv.Get("ID").Trim();
                string displayName = csv.Get("Name").Trim();
                string englishName = csv.Get("Name_eng").Trim();
                string weightText = csv.Get("등장 확률").Trim();
                string attribute = csv.Get("손님속성").Trim();
                string speechStyle = csv.Get("말투속성").Trim();
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    bool looksLikeNote = !string.IsNullOrWhiteSpace(displayName)
                        && string.IsNullOrWhiteSpace(englishName)
                        && string.IsNullOrWhiteSpace(weightText)
                        && string.IsNullOrWhiteSpace(attribute)
                        && string.IsNullOrWhiteSpace(speechStyle);
                    if (looksLikeNote)
                        report.warnings.Add($"{i + 2}행 설명을 건너뛰었습니다: {displayName}");
                    else
                        report.errors.Add($"{i + 2}행: 손님 ID가 비어 있습니다.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(displayName)
                    || string.IsNullOrWhiteSpace(englishName)
                    || string.IsNullOrWhiteSpace(weightText)
                    || string.IsNullOrWhiteSpace(attribute)
                    || string.IsNullOrWhiteSpace(speechStyle))
                {
                    report.errors.Add($"{i + 2}행: 필수 값이 비어 있습니다.");
                    continue;
                }

                if (!float.TryParse(
                        weightText,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float weight)
                    || weight < 0f)
                {
                    report.errors.Add($"{i + 2}행: 등장 확률은 0 이상의 숫자여야 합니다: {weightText}");
                    continue;
                }

                if (!ids.Add(sourceId))
                {
                    report.errors.Add($"중복 손님 ID: {sourceId}");
                    continue;
                }

                string characterKey = CreateStableKey(englishName, sourceId);
                if (displayNameByCharacter.TryGetValue(characterKey, out string existingName)
                    && !string.Equals(existingName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    report.errors.Add(
                        $"같은 캐릭터 키에 서로 다른 이름이 연결됩니다: {characterKey} ({existingName}/{displayName})");
                    continue;
                }

                displayNameByCharacter[characterKey] = displayName;
                SourceCustomerRow row = new()
                {
                    sourceId = sourceId,
                    visitKey = sourceId.ToLowerInvariant(),
                    characterKey = characterKey,
                    displayName = displayName,
                    englishName = englishName,
                    weight = weight,
                    attribute = attribute,
                    speechStyle = speechStyle,
                    preferredTaste = csv.Get("주문 항목 6: 맛").Trim(),
                    preferredAtmosphere = csv.Get("주문 항목 7: 분위기").Trim()
                };
                for (int orderIndex = 1; orderIndex <= 5; orderIndex++)
                {
                    string orderName = csv.Get($"주문 항목 {orderIndex}").Trim();
                    if (!string.IsNullOrWhiteSpace(orderName)
                        && !row.orderNames.Contains(orderName, StringComparer.OrdinalIgnoreCase))
                    {
                        row.orderNames.Add(orderName);
                    }
                }

                if (row.orderNames.Count == 0)
                    report.warnings.Add($"{sourceId}: 기획 주문이 없습니다.");
                rows.Add(row);
            }

            return rows;
        }

        private static CharacterData UpsertCharacter(
            SourceCustomerRow row,
            Dictionary<string, CharacterData> characters,
            CustomerCsvImportReport report)
        {
            bool isNew = !characters.TryGetValue(row.characterKey, out CharacterData character)
                || character == null;
            if (isNew)
            {
                character = ScriptableObject.CreateInstance<CharacterData>();
                character.name = "CharacterData_" + row.characterKey;
                AssetDatabase.CreateAsset(
                    character,
                    $"{CharacterFolder}/{character.name}.asset");
                characters[row.characterKey] = character;
                report.createdCharacters++;
            }
            else
            {
                report.updatedCharacters++;
            }

            character.key = row.characterKey;
            character.displayName = row.displayName;
            character.englishName = row.englishName;
            EditorUtility.SetDirty(character);
            return character;
        }

        private static CustomerVisitData UpsertVisit(
            SourceCustomerRow row,
            CharacterData character,
            Dictionary<string, CustomerVisitData> visits,
            CustomerCsvImportReport report)
        {
            bool isNew = !visits.TryGetValue(row.sourceId, out CustomerVisitData visit)
                || visit == null;
            if (isNew)
            {
                visit = ScriptableObject.CreateInstance<CustomerVisitData>();
                visit.name = "CustomerVisit_" + row.visitKey;
                AssetDatabase.CreateAsset(
                    visit,
                    $"{VisitFolder}/{visit.name}.asset");
                visits[row.sourceId] = visit;
                report.createdVisits++;
            }
            else
            {
                report.updatedVisits++;
            }

            visit.sourceCustomerId = row.sourceId;
            visit.visitKey = row.visitKey;
            visit.customerAttributeKey = row.attribute;
            visit.speechStyleKey = row.speechStyle;
            visit.preferredTasteKey = row.preferredTaste;
            visit.preferredAtmosphereKey = row.preferredAtmosphere;
            visit.weight = row.weight;
            visit.reappearanceGroupKey = row.characterKey;
            visit.overridePaymentCurrency = RequiresStrangeCoinPayment(row.attribute);
            visit.paymentCurrency = visit.overridePaymentCurrency
                ? GameCurrency.StrangeCoin
                : GameCurrency.Money;
            ConfigureAvailability(row, visit);
            if (visit.initiallyAvailable && !HasPresentationSprite(character))
            {
                visit.initiallyAvailable = false;
                report.warnings.Add(
                    $"{visit.visitKey}: 시작 손님이지만 이미지가 없어 임시로 비활성화했습니다.");
            }
            visit.tags ??= new List<string>();
            visit.tags.RemoveAll(tag => tag != null && tag.StartsWith(
                AttributeTagPrefix,
                StringComparison.OrdinalIgnoreCase));
            visit.tags.Add(AttributeTagPrefix + row.attribute);
            visit.plannedOrderNames ??= new List<string>();
            visit.plannedOrderNames.Clear();
            visit.plannedOrderNames.AddRange(row.orderNames);

            visit.members ??= new List<CustomerVisitMember>();
            CustomerVisitMember member = visit.members.Count > 0
                ? visit.members[0]
                : new CustomerVisitMember();
            member.characterKey = character.key;
            visit.members.Clear();
            visit.members.Add(member);
            EditorUtility.SetDirty(visit);
            return visit;
        }

        private static void ConfigureAvailability(
            SourceCustomerRow row,
            CustomerVisitData visit)
        {
            visit.initiallyAvailable = string.Equals(
                    row.attribute,
                    "근로자들",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    row.attribute,
                    "F54",
                    StringComparison.OrdinalIgnoreCase);
            visit.availabilityTransitions ??= new List<CustomerAvailabilityTransition>();
            visit.availabilityTransitions.Clear();

            if (string.Equals(row.attribute, "아무개들", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.attribute, "F72", StringComparison.OrdinalIgnoreCase))
            {
                AddAvailabilityTransition(
                    visit,
                    enabled: true,
                    prerequisiteEpisodeId: "StrangeCoin_0");
                AddAvailabilityTransition(
                    visit,
                    enabled: false,
                    requiredFlag: "sc2_1_false");
                return;
            }

            if (string.Equals(row.attribute, "셀리", StringComparison.OrdinalIgnoreCase))
            {
                AddAvailabilityTransition(
                    visit,
                    enabled: true,
                    requiredFlag: "sc2_1_true");
                return;
            }

            if (string.Equals(row.attribute, "E12", StringComparison.OrdinalIgnoreCase))
            {
                AddAvailabilityTransition(
                    visit,
                    enabled: true,
                    prerequisiteEpisodeId: "StrangeCoin_3");
                return;
            }

            if (string.Equals(
                    row.attribute,
                    "카사_아이들_셀리",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddAvailabilityTransition(
                    visit,
                    enabled: true,
                    requiredFlag: "tl3_2_1_true");
                return;
            }

            if (string.Equals(
                    row.attribute,
                    "카사_아이들_F54",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddAvailabilityTransition(
                    visit,
                    enabled: true,
                    requiredFlag: "tl3_2_2_true");
            }
        }

        private static void AddAvailabilityTransition(
            CustomerVisitData visit,
            bool enabled,
            string prerequisiteEpisodeId = null,
            string requiredFlag = null)
        {
            EpisodeTriggerCondition condition = new();
            if (!string.IsNullOrWhiteSpace(prerequisiteEpisodeId))
                condition.prerequisiteEpisodeIds.Add(prerequisiteEpisodeId);
            if (!string.IsNullOrWhiteSpace(requiredFlag))
                condition.requiredFlags.Add(requiredFlag);

            visit.availabilityTransitions.Add(new CustomerAvailabilityTransition
            {
                enabled = enabled,
                condition = condition
            });
        }

        private static void UpsertOrders(
            SourceCustomerRow row,
            CustomerVisitData visit,
            Dictionary<string, CustomerOrderData> orders,
            Dictionary<string, CocktailRecipeDef> recipes,
            CustomerCsvImportReport report)
        {
            Dictionary<string, CustomerVisitOrderOption> existingOptions = new(
                StringComparer.OrdinalIgnoreCase);
            if (visit.orders != null)
            {
                for (int i = 0; i < visit.orders.Count; i++)
                {
                    CustomerVisitOrderOption option = visit.orders[i];
                    if (option?.order != null && !string.IsNullOrWhiteSpace(option.order.key))
                        existingOptions[option.order.key] = option;
                }
            }

            List<CustomerVisitOrderOption> connected = new();
            for (int i = 0; i < row.orderNames.Count; i++)
            {
                string sourceOrderName = row.orderNames[i];
                string lookupName = RecipeNameAliases.TryGetValue(
                    sourceOrderName,
                    out string alias)
                        ? alias
                        : sourceOrderName;
                if (!recipes.TryGetValue(lookupName, out CocktailRecipeDef recipe)
                    || recipe == null
                    || !recipe.isOrderable)
                {
                    report.warnings.Add(
                        $"{row.sourceId}: 레시피가 없어 주문명을 원문으로만 보존합니다: {sourceOrderName}");
                    continue;
                }

                string orderKey = row.visitKey + "_" + recipe.id.ToLowerInvariant();
                bool isNew = !orders.TryGetValue(orderKey, out CustomerOrderData order)
                    || order == null;
                if (isNew)
                {
                    order = ScriptableObject.CreateInstance<CustomerOrderData>();
                    order.name = "CustomerOrder_" + SafeAssetName(orderKey);
                    AssetDatabase.CreateAsset(
                        order,
                        $"{OrderFolder}/{order.name}.asset");
                    orders[orderKey] = order;
                    report.createdOrders++;
                }
                else
                {
                    report.updatedOrders++;
                }

                order.key = orderKey;
                order.requestedRecipeId = recipe.id;
                order.orderType = CocktailOrderType.RecipeOrder;
                order.characterKey = row.characterKey;
                order.paymentCurrency = RequiresStrangeCoinPayment(row.attribute)
                    ? GameCurrency.StrangeCoin
                    : GameCurrency.Money;
                if (string.IsNullOrWhiteSpace(order.expressionKeyMid))
                    order.expressionKeyMid = "mid";
                if (string.IsNullOrWhiteSpace(order.expressionKeyGood))
                    order.expressionKeyGood = "good";
                if (string.IsNullOrWhiteSpace(order.expressionKeyBad))
                    order.expressionKeyBad = "bad";
                EditorUtility.SetDirty(order);

                if (!existingOptions.TryGetValue(orderKey, out CustomerVisitOrderOption option))
                {
                    option = new CustomerVisitOrderOption
                    {
                        order = order,
                        weight = 1f,
                        condition = new EpisodeTriggerCondition()
                    };
                }
                else
                {
                    option.order = order;
                    option.condition ??= new EpisodeTriggerCondition();
                }

                connected.Add(option);
            }

            visit.orders = connected;
            EditorUtility.SetDirty(visit);
        }

        private static bool RequiresStrangeCoinPayment(string attribute)
        {
            return string.Equals(attribute, "아무개들", StringComparison.OrdinalIgnoreCase)
                || string.Equals(attribute, "F72", StringComparison.OrdinalIgnoreCase)
                || string.Equals(attribute, "셀리", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, CocktailRecipeDef> BuildRecipeNameMap(
            CustomerCsvImportReport report)
        {
            Dictionary<string, CocktailRecipeDef> recipes =
                new(StringComparer.OrdinalIgnoreCase);
            CocktailRecipeDef[] definitions = Resources.LoadAll<CocktailRecipeDef>(
                ProjectResourcePaths.BartendingRecipes);
            for (int i = 0; i < definitions.Length; i++)
            {
                CocktailRecipeDef definition = definitions[i];
                if (definition == null
                    || string.IsNullOrWhiteSpace(definition.displayName))
                {
                    continue;
                }

                string name = definition.displayName.Trim();
                if (recipes.TryGetValue(name, out CocktailRecipeDef existing)
                    && existing != definition)
                {
                    report.errors.Add($"중복 레시피 표시명: {name}");
                    continue;
                }

                recipes[name] = definition;
            }

            return recipes;
        }

        private static Dictionary<string, T> LoadAssetsByKey<T>(Func<T, string> getKey)
            where T : UnityEngine.Object
        {
            Dictionary<string, T> result = new(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            for (int i = 0; i < guids.Length; i++)
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[i]));
                string key = asset != null ? getKey(asset) : string.Empty;
                if (!string.IsNullOrWhiteSpace(key) && !result.ContainsKey(key))
                    result[key] = asset;
            }

            return result;
        }

        private static List<T> LoadAssetsInFolder<T>(string folder)
            where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder });
            List<T> result = new();
            for (int i = 0; i < guids.Length; i++)
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (asset != null)
                    result.Add(asset);
            }

            return result;
        }

        private static CharacterData FindCharacterByKey(string key)
        {
            string canonicalKey = NormalizeCharacterKey(key);
            if (string.IsNullOrWhiteSpace(canonicalKey))
                return null;
            Dictionary<string, CharacterData> characters = LoadAssetsByKey<CharacterData>(
                asset => asset.key);
            return characters.TryGetValue(canonicalKey, out CharacterData character)
                ? character
                : null;
        }

        private static void NormalizeVisitCharacterKeys(CustomerVisitData visit)
        {
            if (visit == null)
                return;

            bool changed = false;
            if (visit.members != null)
            {
                for (int i = 0; i < visit.members.Count; i++)
                {
                    CustomerVisitMember member = visit.members[i];
                    if (member == null)
                        continue;

                    string canonicalKey = NormalizeCharacterKey(member.characterKey);
                    if (!string.Equals(
                            canonicalKey,
                            member.characterKey,
                            StringComparison.Ordinal))
                    {
                        member.characterKey = canonicalKey;
                        changed = true;
                    }
                }
            }

            string canonicalGroupKey = NormalizeCharacterKey(visit.reappearanceGroupKey);
            if (!string.Equals(
                    canonicalGroupKey,
                    visit.reappearanceGroupKey,
                    StringComparison.Ordinal))
            {
                visit.reappearanceGroupKey = canonicalGroupKey;
                changed = true;
            }

            if (changed)
                EditorUtility.SetDirty(visit);
        }

        private static bool HasPresentationSprite(CharacterData character)
        {
            if (character == null)
                return false;
            if (character.defaultSprite != null || character.defaultOverlaySprite != null)
                return true;
            return character.expressions != null && character.expressions.Any(
                expression => expression != null
                    && (expression.sprite != null || expression.overlaySprite != null));
        }

        public static string NormalizeCharacterKey(string key)
        {
            string normalized = key?.Trim() ?? string.Empty;
            return CharacterKeyAliases.TryGetValue(normalized, out string canonicalKey)
                ? canonicalKey
                : normalized;
        }

        private static string CreateStableKey(string englishName, string fallback)
        {
            string source = string.IsNullOrWhiteSpace(englishName) ? fallback : englishName;
            StringBuilder builder = new();
            bool separatorPending = false;
            for (int i = 0; i < source.Length; i++)
            {
                char value = source[i];
                if (char.IsLetterOrDigit(value))
                {
                    if (separatorPending && builder.Length > 0)
                        builder.Append('_');
                    builder.Append(char.ToLowerInvariant(value));
                    separatorPending = false;
                }
                else
                {
                    separatorPending = builder.Length > 0;
                }
            }

            string stableKey = builder.Length > 0
                ? builder.ToString()
                : fallback.ToLowerInvariant();
            return NormalizeCharacterKey(stableKey);
        }

        private static string SafeAssetName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new(value.Length);
            for (int i = 0; i < value.Length; i++)
                builder.Append(invalid.Contains(value[i]) ? '_' : value[i]);
            return builder.ToString();
        }

        private static void EnsureFolder(string fullPath)
        {
            string[] parts = fullPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void Finish(CustomerCsvImportReport report, bool showDialog)
        {
            for (int i = 0; i < report.warnings.Count; i++)
                Debug.LogWarning("[손님 CSV 임포트] " + report.warnings[i]);
            for (int i = 0; i < report.errors.Count; i++)
                Debug.LogError("[손님 CSV 임포트] " + report.errors[i]);
            Debug.Log("[손님 CSV 임포트] " + report.Summary());

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    report.errors.Count == 0 ? "손님 CSV 임포트 완료" : "손님 CSV 임포트 실패",
                    report.Summary(),
                    "확인");
            }
        }

        private sealed class SourceCustomerRow
        {
            public string sourceId;
            public string visitKey;
            public string characterKey;
            public string displayName;
            public string englishName;
            public float weight;
            public string attribute;
            public string speechStyle;
            public string preferredTaste;
            public string preferredAtmosphere;
            public readonly List<string> orderNames = new();
        }
    }
}
