using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Slainte.Bartending;
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
        public const string RootFolder = "Assets/Data/CustomerImport";
        public const string CharacterFolder = RootFolder + "/DraftCharacters";
        public const string VisitFolder = RootFolder + "/DraftVisits";
        public const string OrderFolder = RootFolder + "/DraftOrders";
        public const string AttributeTagPrefix = "customer_attribute:";
        public const string NightPatrolAttributeTag = AttributeTagPrefix + "야간순찰";

        private const string CharacterDatabasePath =
            "Assets/Data/CharacterData/CharacterDatabase.asset";
        private const string VisitDatabasePath =
            "Assets/Resources/CustomerVisit/CustomerVisitDatabase.asset";

        private static readonly Dictionary<string, string> RecipeNameAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "갓 레이디", "갓레이디" }
            };

        public static string DefaultCsvPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "Data_slainte.csv - 손님.csv");

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
            List<string> errors = ValidateDraftsForPublish(out List<CustomerVisitData> visits);
            if (errors.Count > 0)
            {
                string message = string.Join("\n", errors.Take(20));
                Debug.LogError("[손님 드래프트 게시] 중단\n" + string.Join("\n", errors));
                EditorUtility.DisplayDialog("손님 드래프트 게시 실패", message, "확인");
                return;
            }

            CharacterDatabase characterDatabase =
                AssetDatabase.LoadAssetAtPath<CharacterDatabase>(CharacterDatabasePath);
            CustomerVisitDatabase visitDatabase =
                AssetDatabase.LoadAssetAtPath<CustomerVisitDatabase>(VisitDatabasePath);
            if (characterDatabase == null || visitDatabase == null)
            {
                EditorUtility.DisplayDialog(
                    "손님 드래프트 게시 실패",
                    "캐릭터 또는 손님 방문 데이터베이스를 찾을 수 없습니다.",
                    "확인");
                return;
            }

            int addedCharacters = 0;
            int addedVisits = 0;
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

            EditorUtility.SetDirty(characterDatabase);
            EditorUtility.SetDirty(visitDatabase);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[손님 드래프트 게시] 캐릭터 {addedCharacters}개, 방문 {addedVisits}개를 등록했습니다.");
            EditorUtility.DisplayDialog(
                "손님 드래프트 게시 완료",
                $"캐릭터 {addedCharacters}개, 방문 {addedVisits}개를 등록했습니다.",
                "확인");
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

            ItemDefCatalog items = ItemDefCatalog.LoadFromResources("Items", null);
            CocktailRecipeCatalog recipes = CocktailRecipeDataLoader.LoadDefault(items);
            bool hasNightPatrol = false;
            for (int i = 0; i < visits.Count; i++)
            {
                CustomerVisitData visit = visits[i];
                if (visit == null || string.IsNullOrWhiteSpace(visit.visitKey))
                {
                    errors.Add("방문 키가 없는 드래프트가 있습니다.");
                    continue;
                }

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
                        errors.Add($"{visit.visitKey}: 캐릭터 {characterKey} 이미지가 없습니다.");
                    }
                }

                int plannedCount = visit.plannedOrderNames?.Count ?? 0;
                int connectedCount = visit.orders?.Count ?? 0;
                if (plannedCount == 0 || connectedCount != plannedCount)
                {
                    errors.Add(
                        $"{visit.visitKey}: 기획 주문 {plannedCount}개 중 {connectedCount}개만 연결됐습니다.");
                    continue;
                }

                for (int orderIndex = 0; orderIndex < visit.orders.Count; orderIndex++)
                {
                    CustomerOrderData order = visit.orders[orderIndex]?.order;
                    if (order == null
                        || !recipes.TryGet(order.requestedRecipeId, out CocktailRecipe recipe)
                        || recipe == null
                        || !recipe.isOrderable)
                    {
                        errors.Add($"{visit.visitKey}: 주문 레시피 연결이 유효하지 않습니다.");
                    }
                    else if (order.lines == null || order.lines.Count == 0)
                    {
                        errors.Add($"{visit.visitKey}/{order.key}: 주문 대사가 없습니다.");
                    }
                }
            }

            if (!hasNightPatrol)
                errors.Add($"TV 대상 태그가 손님 드래프트에 없습니다: {NightPatrolAttributeTag}");
            return errors;
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
                string attribute = csv.Get("손님속성").Trim();
                string speechStyle = csv.Get("말투속성").Trim();
                if (string.IsNullOrWhiteSpace(sourceId)
                    || string.IsNullOrWhiteSpace(displayName)
                    || string.IsNullOrWhiteSpace(englishName)
                    || string.IsNullOrWhiteSpace(attribute)
                    || string.IsNullOrWhiteSpace(speechStyle))
                {
                    report.errors.Add($"{i + 2}행: 필수 값이 비어 있습니다.");
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
                    attribute = attribute,
                    speechStyle = speechStyle
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
                visit.weight = 1f;
                visit.cooldownSeconds = 100f;
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
            visit.cooldownGroupKey = row.characterKey;
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

        private static Dictionary<string, CocktailRecipeDef> BuildRecipeNameMap(
            CustomerCsvImportReport report)
        {
            Dictionary<string, CocktailRecipeDef> recipes =
                new(StringComparer.OrdinalIgnoreCase);
            CocktailRecipeDef[] definitions = Resources.LoadAll<CocktailRecipeDef>("Recipes");
            for (int i = 0; i < definitions.Length; i++)
            {
                CocktailRecipeDef definition = definitions[i];
                if (definition == null
                    || !string.IsNullOrWhiteSpace(definition.baseRecipeId)
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
            if (string.IsNullOrWhiteSpace(key))
                return null;
            Dictionary<string, CharacterData> characters = LoadAssetsByKey<CharacterData>(
                asset => asset.key);
            return characters.TryGetValue(key, out CharacterData character)
                ? character
                : null;
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

            return builder.Length > 0 ? builder.ToString() : fallback.ToLowerInvariant();
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
            public string attribute;
            public string speechStyle;
            public readonly List<string> orderNames = new();
        }
    }
}
