using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Slainte.Bartending;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public sealed class CustomerDialogueImportReport
    {
        public int profiles;
        public int sourceEntries;
        public int visitOrderEntries;
        public int matchedDialogueEntries;
        public int intentionallyBlankEntries;
        public int createdOrders;
        public int updatedOrders;
        public int createdTickets;
        public int updatedTickets;
        public readonly List<string> warnings = new();
        public readonly List<string> errors = new();

        public string Summary()
        {
            return $"프로필 {profiles}개, 원본 주문 {sourceEntries}개, "
                + $"방문 주문 {visitOrderEntries}개(대사 연결 {matchedDialogueEntries}, "
                + $"의도적 빈칸 {intentionallyBlankEntries}), "
                + $"주문 생성/갱신 {createdOrders}/{updatedOrders}, "
                + $"티켓 생성/갱신 {createdTickets}/{updatedTickets}, "
                + $"경고 {warnings.Count}, 오류 {errors.Count}";
        }
    }

    public static class CustomerDialogueCsvImporter
    {
        public const string SourceCsvAssetPath =
            "Assets/Editor/Data/CustomerDialogue/customer_order_dialogue.csv";

        private const string OrderFolder = CustomerPlanningCsvImporter.OrderFolder;
        private const string VisitFolder = CustomerPlanningCsvImporter.VisitFolder;
        private const string TicketFolder = "Assets/Data/OrderTicket/data/business";
        private const string TicketDatabasePath =
            "Assets/Data/OrderTicket/OrderTicketDatabase.asset";
        private const string ValentinoBerryProfile = "근로자들_발렌티노와베리";
        private const string SilentDialogueToken = "__SILENT__";

        private static readonly HashSet<string> IntentionallyBlankProfiles = new(
            StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> RecipeNameAliases = new(
            StringComparer.OrdinalIgnoreCase)
        {
            { "갓 레이디", "갓레이디" }
        };

        // PDF 색상 원본: 파랑=발렌티노(V), 자홍=베리(B).
        // 키는 주문 슬롯과 CSV 대사 열이며, 문자는 줄(말풍선) 순서를 뜻한다.
        private static readonly Dictionary<string, string> ValentinoBerrySpeakers = new()
        {
            { SpeakerKey(1, DialogueColumn.Order), "VB" },
            { SpeakerKey(1, DialogueColumn.Good), "BV" },
            { SpeakerKey(1, DialogueColumn.MidIce), "VBB" },
            { SpeakerKey(1, DialogueColumn.MidGlass), "VBB" },
            { SpeakerKey(1, DialogueColumn.MidIceGlass), "VBB" },
            { SpeakerKey(1, DialogueColumn.MidWrongMenu), "VBB" },
            { SpeakerKey(1, DialogueColumn.Bad), "VVBB" },

            { SpeakerKey(2, DialogueColumn.Order), "BVB" },
            { SpeakerKey(2, DialogueColumn.Good), "BVBVB" },
            { SpeakerKey(2, DialogueColumn.MidIce), "VBB" },
            { SpeakerKey(2, DialogueColumn.MidGlass), "VBB" },
            { SpeakerKey(2, DialogueColumn.MidIceGlass), "VBB" },
            { SpeakerKey(2, DialogueColumn.MidWrongMenu), "VBB" },
            { SpeakerKey(2, DialogueColumn.Bad), "VB" },

            { SpeakerKey(3, DialogueColumn.Order), "VBV" },
            { SpeakerKey(3, DialogueColumn.Good), "VBVBV" },
            { SpeakerKey(3, DialogueColumn.MidIce), "BVB" },
            { SpeakerKey(3, DialogueColumn.MidGlass), "BVB" },
            { SpeakerKey(3, DialogueColumn.MidIceGlass), "BVB" },
            { SpeakerKey(3, DialogueColumn.MidWrongMenu), "BVB" },
            { SpeakerKey(3, DialogueColumn.Bad), "VB" },

            { SpeakerKey(6, DialogueColumn.Order), "VVBV" },
            { SpeakerKey(6, DialogueColumn.Good), "VBB" },
            { SpeakerKey(6, DialogueColumn.MidIce), "VBB" },
            { SpeakerKey(6, DialogueColumn.MidGlass), "VBB" },
            { SpeakerKey(6, DialogueColumn.MidIceGlass), "VBB" },
            { SpeakerKey(6, DialogueColumn.MidWrongMenu), "VBB" },
            { SpeakerKey(6, DialogueColumn.Bad), "BV" },

            { SpeakerKey(7, DialogueColumn.Order), "BB" },
            { SpeakerKey(7, DialogueColumn.Good), "BV" },
            { SpeakerKey(7, DialogueColumn.Bad), "VB" },
            { SpeakerKey(7, DialogueColumn.MidIce), "BV" },
            { SpeakerKey(7, DialogueColumn.MidGlass), "BV" },
            { SpeakerKey(7, DialogueColumn.MidIceGlass), "BV" },
            { SpeakerKey(7, DialogueColumn.MidWrongMenu), "BV" }
        };

        [MenuItem("Slainte/데이터/주문 대사 CSV 임포트")]
        public static void ImportFromMenu()
        {
            Import(showDialog: true);
        }

        [MenuItem("Slainte/데이터/주문 대사 CSV 임포트 및 게시")]
        public static void ImportAndPublishFromMenu()
        {
            CustomerDialogueImportReport report = Import(showDialog: false);
            if (report.errors.Count == 0)
                CustomerPlanningCsvImporter.PublishValidatedDraftsFromMenu();
            Finish(report, showDialog: true);
        }

        public static void ImportAndPublishFromCommandLine()
        {
            CustomerDialogueImportReport report = Import(showDialog: false);
            if (report.errors.Count > 0)
                throw new InvalidDataException(string.Join("\n", report.errors));

            CustomerPlanningCsvImporter.PublishValidatedDrafts(showDialog: false);
        }

        public static CustomerDialogueImportReport Import(bool showDialog)
        {
            CustomerDialogueImportReport report = new();
            string sourcePath = Path.GetFullPath(SourceCsvAssetPath);
            if (!File.Exists(sourcePath))
            {
                report.errors.Add($"프로젝트 주문 대사 CSV를 찾을 수 없습니다: {SourceCsvAssetPath}");
                Finish(report, showDialog);
                return report;
            }

            Dictionary<string, DialogueProfile> profiles = ParseProfiles(sourcePath, report);
            report.profiles = profiles.Count;
            report.sourceEntries = profiles.Values.Sum(profile => profile.entries.Count);
            if (report.errors.Count > 0)
            {
                Finish(report, showDialog);
                return report;
            }

            EnsureFolder(OrderFolder);
            EnsureFolder(TicketFolder);

            List<CustomerVisitData> visits = LoadAssetsInFolder<CustomerVisitData>(VisitFolder);
            Dictionary<string, CustomerOrderData> orders = LoadAssetsByKey<CustomerOrderData>(
                OrderFolder,
                order => order.key);
            Dictionary<string, OrderTicketData> tickets = LoadAssetsByKey<OrderTicketData>(
                TicketFolder,
                ticket => ticket.key);
            Dictionary<string, CharacterData> characters = LoadAssetsByKey<CharacterData>(
                "Assets",
                character => character.key);
            Dictionary<string, CocktailRecipeDef> recipes = BuildRecipeNameMap(report);
            HashSet<string> usedOrderKeys = new(StringComparer.OrdinalIgnoreCase);
            List<OrderTicketData> generatedTickets = new();

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < visits.Count; i++)
                {
                    UpdateVisit(
                        visits[i],
                        profiles,
                        recipes,
                        characters,
                        orders,
                        tickets,
                        usedOrderKeys,
                        generatedTickets,
                        report);
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

            foreach (CustomerOrderData order in orders.Values)
            {
                if (order != null
                    && !string.IsNullOrWhiteSpace(order.key)
                    && !usedOrderKeys.Contains(order.key))
                {
                    report.warnings.Add(
                        $"현재 방문에서 사용하지 않는 주문 에셋은 삭제하지 않았습니다: {order.key}");
                }
            }

            UpdateTicketDatabase(generatedTickets, report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Finish(report, showDialog);
            return report;
        }

        private static void UpdateVisit(
            CustomerVisitData visit,
            IReadOnlyDictionary<string, DialogueProfile> profiles,
            IReadOnlyDictionary<string, CocktailRecipeDef> recipes,
            IReadOnlyDictionary<string, CharacterData> characters,
            IDictionary<string, CustomerOrderData> orders,
            IDictionary<string, OrderTicketData> tickets,
            ISet<string> usedOrderKeys,
            ICollection<OrderTicketData> generatedTickets,
            CustomerDialogueImportReport report)
        {
            if (visit == null || string.IsNullOrWhiteSpace(visit.visitKey))
                return;

            string profileKey = NormalizeKey(visit.speechStyleKey);
            profiles.TryGetValue(profileKey, out DialogueProfile profile);
            bool intentionallyBlank = IntentionallyBlankProfiles.Contains(profileKey);
            if (profile == null && !intentionallyBlank)
            {
                report.warnings.Add(
                    $"대사 프로필이 없는 방문입니다: {visit.visitKey}/{visit.speechStyleKey}");
            }

            DialogueIdentity identity = ResolveIdentity(visit, characters);
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
            if (visit.plannedOrderNames != null)
            {
                for (int i = 0; i < visit.plannedOrderNames.Count; i++)
                {
                    string plannedName = visit.plannedOrderNames[i];
                    string recipeName = NormalizeOrderName(
                        CocktailOrderType.RecipeOrder,
                        plannedName);
                    if (!recipes.TryGetValue(recipeName, out CocktailRecipeDef recipe)
                        || recipe == null
                        || !recipe.isOrderable)
                    {
                        report.errors.Add(
                            $"{visit.visitKey}: 주문 레시피를 찾을 수 없습니다: {plannedName}");
                        continue;
                    }

                    DialogueEntry entry = profile?.Find(
                        CocktailOrderType.RecipeOrder,
                        plannedName);
                    AddOrder(
                        visit,
                        CocktailOrderType.RecipeOrder,
                        recipe.id,
                        recipe.displayName,
                        null,
                        entry,
                        intentionallyBlank,
                        identity,
                        existingOptions,
                        connected,
                        orders,
                        tickets,
                        usedOrderKeys,
                        generatedTickets,
                        report);
                }
            }

            AddConditionOrder(
                visit,
                CocktailOrderType.TasteOrder,
                visit.preferredTasteKey,
                profile,
                intentionallyBlank,
                identity,
                existingOptions,
                connected,
                orders,
                tickets,
                usedOrderKeys,
                generatedTickets,
                report);
            AddConditionOrder(
                visit,
                CocktailOrderType.MoodOrder,
                visit.preferredAtmosphereKey,
                profile,
                intentionallyBlank,
                identity,
                existingOptions,
                connected,
                orders,
                tickets,
                usedOrderKeys,
                generatedTickets,
                report);

            visit.orders = connected;
            EditorUtility.SetDirty(visit);
        }

        private static void AddConditionOrder(
            CustomerVisitData visit,
            CocktailOrderType orderType,
            string condition,
            DialogueProfile profile,
            bool intentionallyBlank,
            DialogueIdentity identity,
            IReadOnlyDictionary<string, CustomerVisitOrderOption> existingOptions,
            ICollection<CustomerVisitOrderOption> connected,
            IDictionary<string, CustomerOrderData> orders,
            IDictionary<string, OrderTicketData> tickets,
            ISet<string> usedOrderKeys,
            ICollection<OrderTicketData> generatedTickets,
            CustomerDialogueImportReport report)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return;

            DialogueEntry entry = profile?.Find(orderType, condition);
            if (entry == null && !intentionallyBlank)
            {
                report.warnings.Add(
                    $"{visit.visitKey}: 조건 주문 대사를 찾지 못했습니다: {condition}");
            }

            AddOrder(
                visit,
                orderType,
                string.Empty,
                condition.Trim(),
                condition.Trim(),
                entry,
                intentionallyBlank,
                identity,
                existingOptions,
                connected,
                orders,
                tickets,
                usedOrderKeys,
                generatedTickets,
                report);
        }

        private static void AddOrder(
            CustomerVisitData visit,
            CocktailOrderType orderType,
            string recipeId,
            string displayLabel,
            string conditionTag,
            DialogueEntry entry,
            bool intentionallyBlank,
            DialogueIdentity identity,
            IReadOnlyDictionary<string, CustomerVisitOrderOption> existingOptions,
            ICollection<CustomerVisitOrderOption> connected,
            IDictionary<string, CustomerOrderData> orders,
            IDictionary<string, OrderTicketData> tickets,
            ISet<string> usedOrderKeys,
            ICollection<OrderTicketData> generatedTickets,
            CustomerDialogueImportReport report)
        {
            string suffix = orderType switch
            {
                CocktailOrderType.TasteOrder => "taste",
                CocktailOrderType.MoodOrder => "mood",
                _ => recipeId.ToLowerInvariant()
            };
            string orderKey = visit.visitKey + "_" + suffix;
            bool created = !orders.TryGetValue(orderKey, out CustomerOrderData order)
                || order == null;
            if (created)
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
            order.requestedRecipeId = recipeId ?? string.Empty;
            order.orderType = orderType;
            order.characterKey = FirstCharacterKey(visit);
            order.tags ??= new List<string>();
            order.tags.Clear();
            if (!string.IsNullOrWhiteSpace(conditionTag))
                order.tags.Add(conditionTag.Trim());
            order.expressionKeyMid = DefaultExpression(order.expressionKeyMid, "mid");
            order.expressionKeyGood = DefaultExpression(order.expressionKeyGood, "good");
            order.expressionKeyBad = DefaultExpression(order.expressionKeyBad, "bad");

            if (entry != null)
            {
                ApplyDialogue(order, entry, identity, report);
                report.matchedDialogueEntries++;
            }
            else if (intentionallyBlank)
            {
                ApplyIntentionallyBlankDialogue(order);
                report.intentionallyBlankEntries++;
            }

            EditorUtility.SetDirty(order);
            usedOrderKeys.Add(orderKey);
            report.visitOrderEntries++;

            CustomerVisitOrderOption option = existingOptions.TryGetValue(
                orderKey,
                out CustomerVisitOrderOption existingOption)
                    ? existingOption
                    : new CustomerVisitOrderOption
                    {
                        weight = 1f,
                        condition = new EpisodeTriggerCondition()
                    };
            option.order = order;
            option.condition ??= new EpisodeTriggerCondition();
            connected.Add(option);

            OrderTicketData ticket = UpsertTicket(
                orderKey,
                identity.speakerName,
                orderType,
                displayLabel,
                order,
                tickets,
                report);
            generatedTickets.Add(ticket);
        }

        private static void ApplyDialogue(
            CustomerOrderData order,
            DialogueEntry entry,
            DialogueIdentity identity,
            CustomerDialogueImportReport report)
        {
            order.orderDialogueAuthored = true;
            order.authoredFeedback = CustomerOrderFeedbackMask.None;
            order.intentionallySilentFeedback = CustomerOrderFeedbackMask.None;
            order.lines = BuildLines(entry, DialogueColumn.Order, identity, report);
            order.feedbackLinesGood = BuildFeedbackLines(
                order,
                CustomerOrderFeedbackMask.Good,
                entry,
                DialogueColumn.Good,
                identity,
                report);
            order.feedbackLinesMidIce = BuildFeedbackLines(
                order,
                CustomerOrderFeedbackMask.MidIce,
                entry,
                DialogueColumn.MidIce,
                identity,
                report);
            order.feedbackLinesMidGlass = BuildFeedbackLines(
                order,
                CustomerOrderFeedbackMask.MidGlass,
                entry,
                DialogueColumn.MidGlass,
                identity,
                report);
            order.feedbackLinesMidIceGlass = BuildFeedbackLines(
                order,
                CustomerOrderFeedbackMask.MidIceGlass,
                entry,
                DialogueColumn.MidIceGlass,
                identity,
                report);
            order.feedbackLinesMidWrongMenu = BuildFeedbackLines(
                order,
                CustomerOrderFeedbackMask.MidWrongMenu,
                entry,
                DialogueColumn.MidWrongMenu,
                identity,
                report);
            order.feedbackLinesBad = BuildFeedbackLines(
                order,
                CustomerOrderFeedbackMask.Bad,
                entry,
                DialogueColumn.Bad,
                identity,
                report);
            order.feedbackLinesMid ??= new List<DialogueLine>();
            order.feedbackLinesMid.Clear();
        }

        private static void ApplyIntentionallyBlankDialogue(CustomerOrderData order)
        {
            order.orderDialogueAuthored = true;
            order.authoredFeedback = CustomerOrderFeedbackMask.None;
            order.intentionallySilentFeedback = CustomerOrderFeedbackMask.None;
            order.lines = new List<DialogueLine>();
            order.feedbackLinesGood = new List<DialogueLine>();
            order.feedbackLinesMid = new List<DialogueLine>();
            order.feedbackLinesBad = new List<DialogueLine>();
            order.feedbackLinesMidIce = new List<DialogueLine>();
            order.feedbackLinesMidGlass = new List<DialogueLine>();
            order.feedbackLinesMidIceGlass = new List<DialogueLine>();
            order.feedbackLinesMidWrongMenu = new List<DialogueLine>();
        }

        private static List<DialogueLine> BuildFeedbackLines(
            CustomerOrderData order,
            CustomerOrderFeedbackMask mask,
            DialogueEntry entry,
            DialogueColumn column,
            DialogueIdentity identity,
            CustomerDialogueImportReport report)
        {
            string value = entry.Get(column);
            if (IsSilentDialogue(value))
            {
                order.intentionallySilentFeedback |= mask;
                return new List<DialogueLine>();
            }

            List<DialogueLine> lines = BuildLines(entry, column, identity, report);
            if (lines.Count > 0)
                order.authoredFeedback |= mask;
            return lines;
        }

        private static List<DialogueLine> BuildLines(
            DialogueEntry entry,
            DialogueColumn column,
            DialogueIdentity identity,
            CustomerDialogueImportReport report)
        {
            string value = entry.Get(column);
            if (string.IsNullOrWhiteSpace(value) || IsSilentDialogue(value))
                return new List<DialogueLine>();

            string[] sourceLines = value
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
            string speakerSequence = null;
            if (string.Equals(
                    entry.profileKey,
                    ValentinoBerryProfile,
                    StringComparison.OrdinalIgnoreCase))
            {
                ValentinoBerrySpeakers.TryGetValue(
                    SpeakerKey(entry.slot, column),
                    out speakerSequence);
                if (string.IsNullOrEmpty(speakerSequence)
                    || speakerSequence.Length != sourceLines.Length
                    || speakerSequence.Any(speaker => speaker != 'V' && speaker != 'B'))
                {
                    report.errors.Add(
                        $"발렌티노/베리 화자 매핑이 PDF 원본과 맞지 않습니다: "
                        + $"slot={entry.slot}, column={column}, "
                        + $"lines={sourceLines.Length}, mapping={speakerSequence ?? "<없음>"}");
                    speakerSequence = null;
                }
            }

            List<DialogueLine> lines = new(sourceLines.Length);
            for (int i = 0; i < sourceLines.Length; i++)
            {
                if (!string.IsNullOrEmpty(speakerSequence)
                    && i < speakerSequence.Length)
                {
                    bool valentino = speakerSequence[i] == 'V';
                    lines.Add(new DialogueLine
                    {
                        speakerName = valentino ? "발렌티노" : "베리",
                        text = sourceLines[i],
                        nameColor = valentino ? Color.blue : Color.magenta
                    });
                }
                else
                {
                    lines.Add(new DialogueLine
                    {
                        speakerName = identity.speakerName,
                        text = sourceLines[i],
                        nameColor = identity.nameColor
                    });
                }
            }
            return lines;
        }

        private static bool IsSilentDialogue(string value)
        {
            return string.Equals(
                value?.Trim(),
                SilentDialogueToken,
                StringComparison.OrdinalIgnoreCase);
        }

        private static OrderTicketData UpsertTicket(
            string orderKey,
            string customerName,
            CocktailOrderType orderType,
            string displayLabel,
            CustomerOrderData order,
            IDictionary<string, OrderTicketData> tickets,
            CustomerDialogueImportReport report)
        {
            bool created = !tickets.TryGetValue(orderKey, out OrderTicketData ticket)
                || ticket == null;
            if (created)
            {
                ticket = ScriptableObject.CreateInstance<OrderTicketData>();
                ticket.name = "OrderTicketData_" + SafeAssetName(orderKey);
                AssetDatabase.CreateAsset(
                    ticket,
                    $"{TicketFolder}/{ticket.name}.asset");
                tickets[orderKey] = ticket;
                report.createdTickets++;
            }
            else
            {
                report.updatedTickets++;
            }

            ticket.key = orderKey;
            ticket.customerName = customerName ?? string.Empty;
            string fallbackMemo = orderType switch
            {
                CocktailOrderType.TasteOrder => $"맛 조건: {displayLabel}",
                CocktailOrderType.MoodOrder => $"분위기 조건: {displayLabel}",
                _ => $"주문: {displayLabel}"
            };
            ticket.memo = OrderTicketMemoFormatter.Build(order, fallbackMemo);
            ticket.items ??= new List<OrderTicketItem>();
            ticket.items.Clear();
            EditorUtility.SetDirty(ticket);
            return ticket;
        }

        private static void UpdateTicketDatabase(
            IEnumerable<OrderTicketData> generatedTickets,
            CustomerDialogueImportReport report)
        {
            OrderTicketDatabase database =
                AssetDatabase.LoadAssetAtPath<OrderTicketDatabase>(TicketDatabasePath);
            if (database == null)
            {
                report.errors.Add($"주문표 데이터베이스를 찾을 수 없습니다: {TicketDatabasePath}");
                return;
            }

            database.orders ??= new List<OrderTicketData>();
            foreach (OrderTicketData ticket in generatedTickets
                .Where(ticket => ticket != null)
                .GroupBy(ticket => ticket.key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()))
            {
                int index = database.orders.FindIndex(existing => existing != null
                    && string.Equals(
                        existing.key,
                        ticket.key,
                        StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    database.orders.Add(ticket);
                else
                    database.orders[index] = ticket;
            }
            EditorUtility.SetDirty(database);
        }

        private static Dictionary<string, DialogueProfile> ParseProfiles(
            string sourcePath,
            CustomerDialogueImportReport report)
        {
            List<string[]> rows = ParseCsvRows(File.ReadAllText(sourcePath, Encoding.UTF8));
            Dictionary<string, DialogueProfile> profiles = new(
                StringComparer.OrdinalIgnoreCase);
            int index = 0;
            while (index < rows.Count)
            {
                string[] header = rows[index];
                string first = GetCell(header, 0).TrimStart('\uFEFF').Trim();
                if (string.IsNullOrWhiteSpace(first))
                {
                    index++;
                    continue;
                }
                if (first.StartsWith("주문 항목", StringComparison.Ordinal))
                {
                    report.errors.Add($"프로필 헤더 없이 주문 행이 나왔습니다: {first}");
                    index++;
                    continue;
                }

                string profileKey = NormalizeKey(first);
                if (profiles.ContainsKey(profileKey))
                {
                    report.errors.Add($"중복 대사 프로필입니다: {first}");
                    index += 8;
                    continue;
                }

                DialogueProfile profile = new(profileKey);
                profiles[profileKey] = profile;
                index++;
                for (int slot = 1; slot <= 7 && index < rows.Count; slot++, index++)
                {
                    string[] row = rows[index];
                    string marker = GetCell(row, 0).Trim();
                    if (!marker.StartsWith("주문 항목", StringComparison.Ordinal))
                    {
                        report.errors.Add(
                            $"{profileKey}: {slot}번 주문 행 표식이 올바르지 않습니다: {marker}");
                        index--;
                        break;
                    }

                    string orderName = GetCell(row, 1).Trim();
                    if (string.IsNullOrWhiteSpace(orderName))
                        continue;

                    CocktailOrderType orderType = marker.Contains("맛", StringComparison.Ordinal)
                        ? CocktailOrderType.TasteOrder
                        : marker.Contains("분위기", StringComparison.Ordinal)
                            ? CocktailOrderType.MoodOrder
                            : CocktailOrderType.RecipeOrder;
                    DialogueEntry entry = new()
                    {
                        profileKey = profileKey,
                        slot = slot,
                        orderType = orderType,
                        orderName = orderName,
                        cells = new[]
                        {
                            GetCell(row, 2),
                            GetCell(row, 3),
                            GetCell(row, 4),
                            GetCell(row, 5),
                            GetCell(row, 6),
                            GetCell(row, 7),
                            GetCell(row, 8)
                        }
                    };
                    string entryKey = DialogueProfile.EntryKey(orderType, orderName);
                    if (profile.entries.ContainsKey(entryKey))
                    {
                        report.errors.Add(
                            $"{profileKey}: 중복 주문 대사입니다: {orderType}/{orderName}");
                    }
                    else
                    {
                        profile.entries[entryKey] = entry;
                    }
                }
            }
            return profiles;
        }

        private static List<string[]> ParseCsvRows(string text)
        {
            List<string[]> rows = new();
            List<string> fields = new();
            StringBuilder field = new();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (c == ',' && !quoted)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if ((c == '\n' || c == '\r') && !quoted)
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                }
                else if (c == '\r' && quoted)
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    field.Append('\n');
                }
                else
                {
                    field.Append(c);
                }
            }

            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                rows.Add(fields.ToArray());
            }
            return rows;
        }

        private static Dictionary<string, CocktailRecipeDef> BuildRecipeNameMap(
            CustomerDialogueImportReport report)
        {
            Dictionary<string, CocktailRecipeDef> recipes = new(
                StringComparer.OrdinalIgnoreCase);
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

                string key = NormalizeOrderName(
                    CocktailOrderType.RecipeOrder,
                    definition.displayName);
                if (recipes.TryGetValue(key, out CocktailRecipeDef duplicate)
                    && duplicate != definition)
                {
                    report.errors.Add($"중복 레시피 표시 이름입니다: {definition.displayName}");
                }
                else
                {
                    recipes[key] = definition;
                }
            }
            return recipes;
        }

        private static DialogueIdentity ResolveIdentity(
            CustomerVisitData visit,
            IReadOnlyDictionary<string, CharacterData> characters)
        {
            string characterKey = FirstCharacterKey(visit);
            if (!string.IsNullOrWhiteSpace(characterKey)
                && characters.TryGetValue(characterKey, out CharacterData character)
                && character != null)
            {
                return new DialogueIdentity(
                    string.IsNullOrWhiteSpace(character.displayName)
                        ? characterKey
                        : character.displayName,
                    character.nameColor);
            }

            return new DialogueIdentity(
                string.IsNullOrWhiteSpace(characterKey)
                    ? visit.speechStyleKey
                    : characterKey,
                Color.white);
        }

        private static string FirstCharacterKey(CustomerVisitData visit)
        {
            if (visit?.members == null)
                return string.Empty;

            for (int i = 0; i < visit.members.Count; i++)
            {
                string key = visit.members[i]?.characterKey;
                if (!string.IsNullOrWhiteSpace(key))
                    return CustomerPlanningCsvImporter.NormalizeCharacterKey(key);
            }
            return string.Empty;
        }

        private static Dictionary<string, T> LoadAssetsByKey<T>(
            string folder,
            Func<T, string> keySelector)
            where T : UnityEngine.Object
        {
            Dictionary<string, T> assets = new(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder });
            for (int i = 0; i < guids.Length; i++)
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                string key = asset != null ? keySelector(asset) : string.Empty;
                if (!string.IsNullOrWhiteSpace(key))
                    assets[key] = asset;
            }
            return assets;
        }

        private static List<T> LoadAssetsInFolder<T>(string folder)
            where T : UnityEngine.Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .OrderBy(asset => asset.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void EnsureFolder(string path)
        {
            string normalized = path.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        private static string NormalizeKey(string value)
        {
            return (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC);
        }

        private static string NormalizeOrderName(
            CocktailOrderType orderType,
            string value)
        {
            string normalized = NormalizeKey(value);
            if (orderType == CocktailOrderType.RecipeOrder
                && RecipeNameAliases.TryGetValue(normalized, out string alias))
            {
                return alias;
            }
            return normalized;
        }

        private static string GetCell(string[] row, int index)
        {
            return row != null && index >= 0 && index < row.Length
                ? row[index] ?? string.Empty
                : string.Empty;
        }

        private static string DefaultExpression(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string SafeAssetName(string value)
        {
            StringBuilder builder = new();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                builder.Append(invalid.Contains(c) ? '_' : c);
            }
            return builder.ToString();
        }

        private static string SpeakerKey(int slot, DialogueColumn column)
        {
            return slot + ":" + (int)column;
        }

        private static void Finish(CustomerDialogueImportReport report, bool showDialog)
        {
            string summary = report.Summary();
            if (report.errors.Count > 0)
                Debug.LogError("[주문 대사 CSV 임포트] " + summary + "\n" + string.Join("\n", report.errors));
            else
                Debug.Log("[주문 대사 CSV 임포트] " + summary);

            if (report.warnings.Count > 0)
                Debug.LogWarning("[주문 대사 CSV 임포트]\n" + string.Join("\n", report.warnings));

            if (showDialog && !Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    report.errors.Count == 0 ? "주문 대사 임포트 완료" : "주문 대사 임포트 실패",
                    summary,
                    "확인");
            }
        }

        private enum DialogueColumn
        {
            Order,
            Good,
            MidIce,
            MidGlass,
            MidIceGlass,
            MidWrongMenu,
            Bad
        }

        private readonly struct DialogueIdentity
        {
            public DialogueIdentity(string speakerName, Color nameColor)
            {
                this.speakerName = speakerName;
                this.nameColor = nameColor;
            }

            public readonly string speakerName;
            public readonly Color nameColor;
        }

        private sealed class DialogueProfile
        {
            public DialogueProfile(string key)
            {
                this.key = key;
            }

            public readonly string key;
            public readonly Dictionary<string, DialogueEntry> entries = new(
                StringComparer.OrdinalIgnoreCase);

            public DialogueEntry Find(CocktailOrderType orderType, string orderName)
            {
                entries.TryGetValue(EntryKey(orderType, orderName), out DialogueEntry entry);
                return entry;
            }

            public static string EntryKey(CocktailOrderType orderType, string orderName)
            {
                return orderType + "\u001F" + NormalizeOrderName(orderType, orderName);
            }
        }

        private sealed class DialogueEntry
        {
            public string profileKey;
            public int slot;
            public CocktailOrderType orderType;
            public string orderName;
            public string[] cells;

            public string Get(DialogueColumn column)
            {
                int index = (int)column;
                return cells != null && index >= 0 && index < cells.Length
                    ? cells[index]
                    : string.Empty;
            }
        }
    }
}
