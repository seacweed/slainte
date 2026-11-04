using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Slainte.Bartending;
using Slainte.Business;
using Slainte.Economy;
using Slainte.EditorTools;
using UnityEditor;
using UnityEngine;

// 에피소드 CSV(#SECTION 헤더로 구분된 여러 표) ⇄ EpisodeData 변환의 단일 정의.
// CSV 임포터와 그래프 에디터(컴파일 결과 CSV 내보내기)가 모두 이 클래스를 거치므로, 섹션·열 구성이
// 바뀌면 여기 한 곳만 고치면 양방향 포맷이 함께 맞춰진다. Write 결과를 Read하면 같은 데이터가 나와야 한다.
public static class EpisodeCsvCodec
{
    public const string SettlementRewardsSection = "SETTLEMENT_REWARDS";

    // 기획 개편(작전판 제거)으로 더 이상 쓰지 않는 섹션·열. 남아 있으면 무시하고 경고만 남긴다.
    private static readonly string[] ObsoleteSections =
        { "PLAY_TRIGGER", "SELECT_TRIGGER", "SELECT_CHARS", "BOARD", "BOARD_CHARS", "OPENING_CHARS" };
    private static readonly string[] ObsoleteMetaColumns = { "episodeType", "mandatorySlot" };

    private static readonly string[] CompareOperators = { ">=", "<=", "==", ">", "<" };

    // TRIGGER 섹션의 conditionType 값. EpisodeTriggerCondition의 각 필드와 1:1로 대응한다.
    private enum TriggerConditionKind
    {
        MinDay,
        MinMoney,
        RequiredFlag,
        BlockedFlag,
        PrerequisiteEpisode,
        RequiredVar,
        CustomerAppearance
    }

    public sealed class ReadResult
    {
        public EpisodeData Data;
        public readonly HashSet<string> Sections = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Warnings = new();
        public string Error;

        public bool Succeeded => Data != null && string.IsNullOrEmpty(Error);
    }

    // =========================================================================
    // Read
    // =========================================================================

    public static ReadResult Read(string csvText)
    {
        ReadResult result = new();
        Dictionary<string, Section> sections = SplitIntoSections(csvText ?? string.Empty);
        foreach (string name in sections.Keys)
            result.Sections.Add(name);

        foreach (string obsolete in ObsoleteSections)
        {
            if (sections.ContainsKey(obsolete))
                result.Warnings.Add($"더 이상 사용하지 않는 #{obsolete} 섹션을 무시했습니다. CSV에서 지워 주세요.");
        }

        EpisodeData data = ScriptableObject.CreateInstance<EpisodeData>();
        if (!ReadMeta(sections, data, result))
        {
            UnityEngine.Object.DestroyImmediate(data);
            return result;
        }

        data.triggerCondition = ReadTrigger(sections, result);
        data.settlementRewards = ReadSettlementRewards(sections);
        data.nodes = ReadNodes(sections, result);
        result.Data = data;
        return result;
    }

    private static bool ReadMeta(
        Dictionary<string, Section> sections,
        EpisodeData data,
        ReadResult result)
    {
        if (!sections.TryGetValue("META", out Section meta) || meta.Rows.Count == 0)
        {
            string found = sections.Count > 0 ? string.Join(", ", sections.Keys) : "(없음)";
            result.Error = $"#META 섹션이 없거나 비어있습니다. 인식된 섹션: {found}";
            return false;
        }

        string[] row = meta.Rows[0];
        data.episodeId = meta.Get(row, "episodeId", 0);
        data.episodeTitle = meta.Get(row, "episodeTitle", 1);
        data.firstNodeId = meta.Get(row, "firstNodeId", 2);
        // 열 이름으로 찾으므로 예전 META(episodeType,mandatorySlot 열 포함)도 chapterId를 올바르게 읽는다.
        data.chapterId = meta.Get(row, "chapterId", -1);
        data.scheduledDay = ParseNonNegativeInt(meta.Get(row, "day", -1));
        data.scheduledSlot = ParseNonNegativeInt(meta.Get(row, "slot", -1));
        data.slotPriority = int.TryParse(meta.Get(row, "priority", -1), out int priority) ? priority : 0;

        if (string.IsNullOrWhiteSpace(data.episodeId))
        {
            result.Error = "#META의 episodeId가 비어 있습니다.";
            return false;
        }

        foreach (string column in ObsoleteMetaColumns)
        {
            if (meta.HasColumn(column))
                result.Warnings.Add($"#META의 {column} 열은 더 이상 사용하지 않아 무시했습니다.");
        }

        if (data.scheduledDay > 0 && data.scheduledSlot <= 0)
            result.Warnings.Add("#META에 day는 있지만 slot이 없어 영업 일정에 배정되지 않습니다.");
        else if (data.scheduledDay <= 0 && data.scheduledSlot > 0)
            result.Warnings.Add("#META에 slot은 있지만 day가 없어 영업 일정에 배정되지 않습니다.");

        return true;
    }

    // 행 하나 = 조건 하나(conditionType,conditionValue). 여러 행은 AND로 결합된다.
    private static EpisodeTriggerCondition ReadTrigger(
        Dictionary<string, Section> sections,
        ReadResult result)
    {
        EpisodeTriggerCondition condition = new();
        if (!sections.TryGetValue("TRIGGER", out Section trigger))
            return condition;

        foreach (string[] row in trigger.Rows)
        {
            string typeText = trigger.Get(row, "conditionType", 0);
            string value = trigger.Get(row, "conditionValue", 1);
            if (!Enum.TryParse(typeText, true, out TriggerConditionKind kind))
            {
                result.Warnings.Add($"#TRIGGER의 알 수 없는 conditionType을 무시했습니다: {typeText}");
                continue;
            }

            if (!ApplyTriggerRow(condition, kind, value))
                result.Warnings.Add($"#TRIGGER {kind}의 conditionValue 형식이 올바르지 않습니다: {value}");
        }

        return condition;
    }

    private static bool ApplyTriggerRow(
        EpisodeTriggerCondition condition,
        TriggerConditionKind kind,
        string value)
    {
        switch (kind)
        {
            case TriggerConditionKind.MinDay:
                if (!int.TryParse(value, out int day)) return false;
                condition.minDay = day;
                return true;
            case TriggerConditionKind.MinMoney:
                if (!int.TryParse(value, out int money)) return false;
                condition.minMoney = money;
                return true;
            case TriggerConditionKind.RequiredFlag:
                return AddIfPresent(condition.requiredFlags, value);
            case TriggerConditionKind.BlockedFlag:
                return AddIfPresent(condition.blockedFlags, value);
            case TriggerConditionKind.PrerequisiteEpisode:
                return AddIfPresent(condition.prerequisiteEpisodeIds, value);
            case TriggerConditionKind.RequiredVar:
                VarCondition variable = TryParseVarCondition(value);
                if (variable == null) return false;
                condition.requiredVars.Add(variable);
                return true;
            case TriggerConditionKind.CustomerAppearance:
                // RequiredVar와 같은 "이름>=값" 형식을 재사용한다. 등장 횟수는 "이상" 비교만 의미가 있다.
                VarCondition appearance = TryParseVarCondition(value);
                if (appearance == null || appearance.op != CompareOp.GreaterOrEqual) return false;
                condition.requiredCustomerAppearances.Add(new CustomerAppearanceCondition
                {
                    characterId = appearance.varName,
                    count = appearance.threshold
                });
                return true;
            default:
                return false;
        }
    }

    private static List<EpisodeSettlementReward> ReadSettlementRewards(
        Dictionary<string, Section> sections)
    {
        List<EpisodeSettlementReward> rewards = new();
        if (!sections.TryGetValue(SettlementRewardsSection, out Section section))
            return rewards;

        foreach (string[] row in section.Rows)
        {
            rewards.Add(new EpisodeSettlementReward
            {
                requiredFlag = section.Get(row, "requiredFlag", 0),
                amount = int.TryParse(section.Get(row, "amount", 1), out int amount) ? amount : 0,
                label = section.Get(row, "label", 2)
            });
        }

        return rewards;
    }

    private static List<CharacterSlotEntry> ReadSlotEntries(
        Dictionary<string, Section> sections,
        string sectionName,
        int offset)
    {
        List<CharacterSlotEntry> entries = new();
        if (!sections.TryGetValue(sectionName, out Section section))
            return entries;

        foreach (string[] row in section.Rows)
            entries.Add(ToSlotEntry(section, row, offset));
        return entries;
    }

    private static List<EpisodeNode> ReadNodes(
        Dictionary<string, Section> sections,
        ReadResult result)
    {
        List<EpisodeNode> nodes = new();
        if (!sections.TryGetValue("NODES", out Section section))
        {
            result.Warnings.Add("#NODES 섹션이 없습니다.");
            return nodes;
        }

        var chars = GroupByNode(sections, "NODE_CHARS", (s, row) => ToSlotEntry(s, row, 1));
        var choices = GroupByNode(sections, "CHOICES", (s, row) => new EpisodeChoice
        {
            buttonText = s.Get(row, "buttonText", 2),
            nextNodeId = s.Get(row, "nextNodeId", 3),
            setFlags = SplitList(s.Get(row, "setFlags", 4)),
            clearFlags = SplitList(s.Get(row, "clearFlags", 5)),
            varChanges = ParseVarChangeList(s.Get(row, "varChanges", 6))
        });
        Dictionary<string, List<NodeBranch>> branches = ReadBranches(sections, result);
        var craftingBranches = GroupByNode(sections, "NODE_CRAFTING_BRANCHES", (s, row) =>
            Enum.TryParse(s.Get(row, "result", 1), true, out CraftingJobResult craftingResult)
                ? new CraftingOutcome
                {
                    result = craftingResult,
                    nextNodeId = s.Get(row, "nextNodeId", 2),
                    flag = s.Get(row, "flag", 3),
                    varChanges = ParseVarChangeList(s.Get(row, "varChanges", 4))
                }
                : null);

        OrderTicketDatabase ticketDatabase = null;
        HashSet<string> seenNodeIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string[] row in section.Rows)
        {
            string nodeId = section.Get(row, "nodeId", 0);
            // 런타임 FindNode는 첫 번째 노드만 찾으므로 중복 ID의 두 번째 노드는 절대 실행되지 않는다.
            if (!seenNodeIds.Add(nodeId))
                result.Warnings.Add($"#NODES에 nodeId가 중복됩니다(뒤쪽 노드는 실행되지 않음): {nodeId}");
            bool crafting = ParseBoolean(section.Get(row, "requiresCrafting", 5), false);
            string ticketKey = section.Get(row, "craftingTicketKey", 6);
            string orderTarget = section.Get(row, "craftingOrderTarget", -1);
            if (string.IsNullOrWhiteSpace(orderTarget))
                orderTarget = section.Get(row, "craftingRecipeId", -1);

            if (!string.IsNullOrWhiteSpace(ticketKey) && ticketDatabase == null)
                ticketDatabase = AssetDatabase.LoadAssetAtPath<OrderTicketDatabase>(
                    BusinessAssetPaths.OrderTicketDatabase);

            nodes.Add(new EpisodeNode
            {
                nodeId = nodeId,
                speakerKey = section.Get(row, "speakerKey", 1),
                overrideSpeakerName = section.Get(row, "overrideSpeakerName", 2),
                text = section.Get(row, "text", 3),
                nextNodeId = section.Get(row, "nextNodeId", 4),
                requiresCrafting = crafting,
                craftingOrderTicket = !string.IsNullOrWhiteSpace(ticketKey) && ticketDatabase != null
                    ? ticketDatabase.FindByKey(ticketKey)
                    : null,
                craftingTicketKey = ticketKey,
                craftingOrderType = ParseEnum(section.Get(row, "craftingOrderType", -1), CocktailOrderType.EpisodeOrder),
                craftingOrderTarget = orderTarget,
                craftingPaymentEnabled = ParseBoolean(
                    section.Get(row, "craftingPaymentEnabled", -1),
                    crafting && !string.IsNullOrWhiteSpace(orderTarget)),
                craftingPaymentCurrency = ParseCurrency(section.Get(row, "craftingPaymentCurrency", -1)),
                craftingPaymentMultiplier = ParsePositiveFloat(section.Get(row, "craftingPaymentMultiplier", -1), 1f),
                bgmCommand = ParseEnum(section.Get(row, "bgmCommand", 7), BgmCommand.None),
                bgmClipName = section.Get(row, "bgmClipName", 8),
                sfxCommand = ParseEnum(section.Get(row, "sfxCommand", 9), SfxCommand.None),
                sfxClipName = section.Get(row, "sfxClipName", 10),
                craftingOutcomes = Take(craftingBranches, nodeId),
                characters = Take(chars, nodeId),
                choices = Take(choices, nodeId),
                branches = Take(branches, nodeId)
            });
        }

        return nodes;
    }

    // #NODE_BRANCHES: nodeId,conditionType,conditionValue,nextNodeId — 한 줄이 분기 하나, 줄 순서가 판정 순서.
    // conditionType/conditionValue는 #TRIGGER와 같은 이름·값 형식을 쓴다(RequiredVar = "var>=5").
    // 예전 3개 섹션(플래그/변수/에피소드 분기를 따로 쓰던 형식)도 읽되 경고를 남긴다 — 그때 런타임 판정
    // 순서였던 플래그 → 에피소드 → 변수 순으로 이어 붙여 동작이 바뀌지 않게 한다.
    private static Dictionary<string, List<NodeBranch>> ReadBranches(
        Dictionary<string, Section> sections,
        ReadResult result)
    {
        Dictionary<string, List<NodeBranch>> lookup = new();
        void Add(string nodeId, NodeBranch branch)
        {
            if (!lookup.TryGetValue(nodeId, out List<NodeBranch> list))
                lookup.Add(nodeId, list = new List<NodeBranch>());
            list.Add(branch);
        }

        bool legacy = false;
        if (sections.TryGetValue("NODE_BRANCHES", out Section section))
        {
            if (section.HasColumn("conditionType"))
            {
                foreach (string[] row in section.Rows)
                {
                    string nodeId = section.Get(row, "nodeId", 0);
                    string typeText = section.Get(row, "conditionType", 1);
                    string value = section.Get(row, "conditionValue", 2);
                    NodeBranch branch = ParseBranch(typeText, value);
                    if (branch == null)
                    {
                        result.Warnings.Add($"#NODE_BRANCHES의 해석할 수 없는 조건을 무시했습니다: {nodeId} {typeText} {value}");
                        continue;
                    }
                    branch.nextNodeId = section.Get(row, "nextNodeId", 3);
                    Add(nodeId, branch);
                }
            }
            else
            {
                legacy |= section.Rows.Count > 0;
                foreach (string[] row in section.Rows)
                {
                    string nodeId = section.Get(row, "nodeId", 0);
                    List<string> all = SplitList(section.Get(row, "requiredAllFlags", 1));
                    if (all.Count == 0)
                    {
                        if (SplitList(section.Get(row, "requiredAnyFlags", 2)).Count > 0)
                            result.Warnings.Add($"requiredAnyFlags(하나라도 켜짐) 분기는 더 이상 지원하지 않아 무시했습니다: {nodeId}");
                        continue;
                    }
                    Add(nodeId, new NodeBranch
                    {
                        conditionType = NodeBranchConditionType.RequiredFlag,
                        flags = all,
                        nextNodeId = section.Get(row, "nextNodeId", 3)
                    });
                }
            }
        }

        if (sections.TryGetValue("NODE_EPISODE_BRANCHES", out Section episodes))
        {
            legacy |= episodes.Rows.Count > 0;
            foreach (string[] row in episodes.Rows)
                Add(episodes.Get(row, "nodeId", 0), new NodeBranch
                {
                    conditionType = NodeBranchConditionType.PrerequisiteEpisode,
                    episodeId = episodes.Get(row, "requiredCompletedEpisodeId", 1).Trim(),
                    nextNodeId = episodes.Get(row, "nextNodeId", 2)
                });
        }

        if (sections.TryGetValue("NODE_VAR_BRANCHES", out Section vars))
        {
            legacy |= vars.Rows.Count > 0;
            foreach (string[] row in vars.Rows)
                Add(vars.Get(row, "nodeId", 0), new NodeBranch
                {
                    conditionType = NodeBranchConditionType.RequiredVar,
                    varCondition = new VarCondition
                    {
                        varName = vars.Get(row, "varName", 1),
                        op = ParseCompareOp(vars.Get(row, "op", 2)),
                        threshold = int.TryParse(vars.Get(row, "threshold", 3), out int t) ? t : 0
                    },
                    nextNodeId = vars.Get(row, "nextNodeId", 4)
                });
        }

        if (legacy)
            result.Warnings.Add("예전 분기 형식(#NODE_BRANCHES의 requiredAllFlags 열, #NODE_VAR_BRANCHES, #NODE_EPISODE_BRANCHES)을 읽었습니다. "
                + "#NODE_BRANCHES 한 섹션(nodeId,conditionType,conditionValue,nextNodeId)으로 옮겨 주세요.");
        return lookup;
    }

    private static NodeBranch ParseBranch(string typeText, string value)
    {
        if (!Enum.TryParse(typeText?.Trim(), true, out NodeBranchConditionType type)
            || !Enum.IsDefined(typeof(NodeBranchConditionType), type))
            return null;

        NodeBranch branch = new() { conditionType = type };
        switch (type)
        {
            case NodeBranchConditionType.RequiredFlag:
            case NodeBranchConditionType.BlockedFlag:
                branch.flags = SplitList(value);
                return branch.flags.Count > 0 ? branch : null;
            case NodeBranchConditionType.RequiredVar:
                branch.varCondition = TryParseVarCondition(value);
                return branch.varCondition != null ? branch : null;
            case NodeBranchConditionType.PrerequisiteEpisode:
                branch.episodeId = value?.Trim();
                return string.IsNullOrEmpty(branch.episodeId) ? null : branch;
            default:
                return null;
        }
    }

    public static string FormatBranchValue(NodeBranch branch) => branch.conditionType switch
    {
        NodeBranchConditionType.RequiredFlag or NodeBranchConditionType.BlockedFlag =>
            string.Join(", ", branch.flags ?? new List<string>()),
        NodeBranchConditionType.RequiredVar =>
            $"{branch.varCondition.varName}{CompareOpToString(branch.varCondition.op)}{branch.varCondition.threshold.ToString(CultureInfo.InvariantCulture)}",
        NodeBranchConditionType.PrerequisiteEpisode => branch.episodeId,
        _ => string.Empty
    };

    private static Dictionary<string, List<T>> GroupByNode<T>(
        Dictionary<string, Section> sections,
        string sectionName,
        Func<Section, string[], T> build) where T : class
    {
        Dictionary<string, List<T>> lookup = new();
        if (!sections.TryGetValue(sectionName, out Section section))
            return lookup;

        foreach (string[] row in section.Rows)
        {
            T item = build(section, row);
            if (item == null)
                continue;

            string nodeId = section.Get(row, "nodeId", 0);
            if (!lookup.TryGetValue(nodeId, out List<T> list))
            {
                list = new List<T>();
                lookup.Add(nodeId, list);
            }
            list.Add(item);
        }

        return lookup;
    }

    private static List<T> Take<T>(Dictionary<string, List<T>> lookup, string nodeId)
    {
        return lookup.TryGetValue(nodeId, out List<T> list) ? list : new List<T>();
    }

    private static CharacterSlotEntry ToSlotEntry(Section section, string[] row, int offset)
    {
        return new CharacterSlotEntry
        {
            characterKey = section.Get(row, "characterKey", offset),
            expressionKey = section.Get(row, "expressionKey", offset + 1),
            slotIndex = int.TryParse(section.Get(row, "slotIndex", offset + 2), out int slot) ? slot : -1
        };
    }

    // =========================================================================
    // Write
    // =========================================================================

    // 모든 섹션을 (비어 있어도) 헤더와 함께 쓴다 — 섹션이 있으면 CSV가 그 필드의 기준이 된다는
    // 임포트 규칙상, 빠진 섹션 때문에 기존 에셋의 오래된 값이 남는 일을 막기 위해서다.
    public static string Write(EpisodeData data)
    {
        StringBuilder sb = new();

        BeginSection(sb, "META", "episodeId,episodeTitle,firstNodeId,chapterId,day,slot,priority");
        AppendRow(sb, data.episodeId, data.episodeTitle, data.firstNodeId, data.chapterId,
            IntOrEmpty(data.scheduledDay), IntOrEmpty(data.scheduledSlot), IntOrEmpty(data.slotPriority));

        BeginSection(sb, "TRIGGER", "conditionType,conditionValue");
        WriteTrigger(sb, data.triggerCondition);

        BeginSection(sb, SettlementRewardsSection, "requiredFlag,amount,label");
        foreach (EpisodeSettlementReward r in data.settlementRewards ?? new List<EpisodeSettlementReward>())
            AppendRow(sb, r.requiredFlag, r.amount.ToString(CultureInfo.InvariantCulture), r.label);

        List<EpisodeNode> nodes = data.nodes ?? new List<EpisodeNode>();
        // 컬럼 순서는 손으로 쓰던 기존 CSV와 같게 둔다. craftingOrderType은 쓰지 않는다 —
        // 런타임(EpisodeCraftingBridge)이 craftingOrderTarget을 보고 자동 판별하기 때문이다.
        BeginSection(sb, "NODES",
            "nodeId,speakerKey,overrideSpeakerName,text,nextNodeId,requiresCrafting,craftingTicketKey,"
            + "craftingOrderTarget,bgmCommand,bgmClipName,sfxCommand,sfxClipName,"
            + "craftingPaymentEnabled,craftingPaymentCurrency,craftingPaymentMultiplier");
        foreach (EpisodeNode n in nodes)
            AppendNodeRow(sb, n);

        BeginSection(sb, "NODE_CRAFTING_BRANCHES", "nodeId,result,nextNodeId,flag,varChanges");
        foreach (EpisodeNode n in nodes)
            foreach (CraftingOutcome o in n.craftingOutcomes ?? new List<CraftingOutcome>())
                AppendRow(sb, n.nodeId, o.result.ToString(), o.nextNodeId, o.flag, VarChanges(o.varChanges));

        BeginSection(sb, "NODE_CHARS", "nodeId,characterKey,expressionKey,slotIndex");
        foreach (EpisodeNode n in nodes)
            foreach (CharacterSlotEntry c in n.characters ?? new List<CharacterSlotEntry>())
                AppendRow(sb, n.nodeId, c.characterKey, c.expressionKey,
                    c.slotIndex.ToString(CultureInfo.InvariantCulture));

        BeginSection(sb, "CHOICES", "nodeId,choiceIndex,buttonText,nextNodeId,setFlags,clearFlags,varChanges");
        foreach (EpisodeNode n in nodes)
        {
            List<EpisodeChoice> nodeChoices = n.choices ?? new List<EpisodeChoice>();
            for (int i = 0; i < nodeChoices.Count; i++)
            {
                EpisodeChoice c = nodeChoices[i];
                AppendRow(sb, n.nodeId, i.ToString(CultureInfo.InvariantCulture), c.buttonText, c.nextNodeId,
                    JoinList(c.setFlags), JoinList(c.clearFlags), VarChanges(c.varChanges));
            }
        }

        BeginSection(sb, "NODE_BRANCHES", "nodeId,conditionType,conditionValue,nextNodeId");
        foreach (EpisodeNode n in nodes)
            foreach (NodeBranch b in n.branches ?? new List<NodeBranch>())
                AppendRow(sb, n.nodeId, b.conditionType.ToString(), FormatBranchValue(b), b.nextNodeId);

        return sb.ToString();
    }

    // Read가 빈칸에 채우는 기본값과 같은 값은 비워 둔다 — 줄마다 None/Money/1이 반복되면 CSV를 읽기 어렵다.
    // 빈칸 → 기본값 규칙은 ReadNodes와 짝이므로 한쪽을 바꾸면 다른 쪽도 함께 바꿀 것.
    private static void AppendNodeRow(StringBuilder sb, EpisodeNode n)
    {
        bool crafting = n.requiresCrafting;
        bool defaultPayment = crafting && !string.IsNullOrWhiteSpace(n.craftingOrderTarget);
        float multiplier = BusinessOrderPriceRules.NormalizePaymentMultiplier(n.craftingPaymentMultiplier);

        AppendRow(sb, n.nodeId, n.speakerKey, n.overrideSpeakerName, n.text, n.nextNodeId,
            crafting ? Bool(true) : string.Empty,
            crafting ? n.craftingTicketKey : string.Empty,
            crafting ? n.craftingOrderTarget : string.Empty,
            n.bgmCommand != BgmCommand.None ? n.bgmCommand.ToString().ToLowerInvariant() : string.Empty,
            n.bgmClipName,
            n.sfxCommand != SfxCommand.None ? n.sfxCommand.ToString().ToLowerInvariant() : string.Empty,
            n.sfxClipName,
            crafting && n.craftingPaymentEnabled != defaultPayment ? Bool(n.craftingPaymentEnabled) : string.Empty,
            crafting && n.craftingPaymentCurrency != GameCurrency.Money ? n.craftingPaymentCurrency.ToString() : string.Empty,
            crafting && !Mathf.Approximately(multiplier, 1f)
                ? multiplier.ToString("0.###", CultureInfo.InvariantCulture)
                : string.Empty);
    }

    private static void WriteTrigger(StringBuilder sb, EpisodeTriggerCondition condition)
    {
        if (condition == null)
            return;

        if (condition.minDay > 0)
            AppendRow(sb, nameof(TriggerConditionKind.MinDay), condition.minDay.ToString(CultureInfo.InvariantCulture));
        if (condition.minMoney > 0)
            AppendRow(sb, nameof(TriggerConditionKind.MinMoney), condition.minMoney.ToString(CultureInfo.InvariantCulture));
        foreach (string flag in condition.requiredFlags)
            AppendRow(sb, nameof(TriggerConditionKind.RequiredFlag), flag);
        foreach (string flag in condition.blockedFlags)
            AppendRow(sb, nameof(TriggerConditionKind.BlockedFlag), flag);
        foreach (string episodeId in condition.prerequisiteEpisodeIds)
            AppendRow(sb, nameof(TriggerConditionKind.PrerequisiteEpisode), episodeId);
        foreach (VarCondition v in condition.requiredVars)
            AppendRow(sb, nameof(TriggerConditionKind.RequiredVar),
                $"{v.varName}{CompareOpToString(v.op)}{v.threshold.ToString(CultureInfo.InvariantCulture)}");
        foreach (CustomerAppearanceCondition a in condition.requiredCustomerAppearances)
            AppendRow(sb, nameof(TriggerConditionKind.CustomerAppearance),
                $"{a.characterId}>={a.count.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void BeginSection(StringBuilder sb, string name, string header)
    {
        if (sb.Length > 0)
            sb.AppendLine();
        sb.Append('#').AppendLine(name);
        sb.AppendLine(header);
    }

    private static void AppendRow(StringBuilder sb, params string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Quote(fields[i]));
        }
        sb.AppendLine();
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static string IntOrEmpty(int value) =>
        value != 0 ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string Bool(bool value) => value ? "TRUE" : "FALSE";

    private static string JoinList(List<string> values) =>
        values == null ? string.Empty : string.Join("|", values);

    private static string VarChanges(List<VarChange> list)
    {
        if (list == null || list.Count == 0)
            return string.Empty;
        return string.Join("|", list.Select(v =>
            $"{v.varName}{(v.delta >= 0 ? "+" : "")}{v.delta.ToString(CultureInfo.InvariantCulture)}"));
    }

    public static string CompareOpToString(CompareOp op) => op switch
    {
        CompareOp.GreaterOrEqual => ">=",
        CompareOp.Greater => ">",
        CompareOp.Equal => "==",
        CompareOp.Less => "<",
        CompareOp.LessOrEqual => "<=",
        _ => "=="
    };

    // =========================================================================
    // Low-level CSV
    // =========================================================================

    private sealed class Section
    {
        public string[] Header;
        public readonly List<string[]> Rows = new();

        public bool HasColumn(string name) => IndexOf(name) >= 0;

        // 열 이름으로 먼저 찾고, 헤더에 그 열이 없을 때만 위치(fallbackIndex)로 읽는다 —
        // 열 순서가 다른 예전 CSV와 새 CSV를 같은 코드로 읽기 위해서다.
        public string Get(string[] row, string column, int fallbackIndex)
        {
            int index = IndexOf(column);
            return Field(row, index >= 0 ? index : fallbackIndex);
        }

        private int IndexOf(string column)
        {
            if (Header == null)
                return -1;
            for (int i = 0; i < Header.Length; i++)
            {
                if (string.Equals(Header[i]?.Trim(), column, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
    }

    // "#SECTION_NAME" 레코드를 만나면 새 섹션을 시작하고, 그 다음 레코드를 헤더로, 이후 레코드를
    // 데이터 행으로 모은다. 모든 셀이 빈 행(스프레드시트가 열 수를 맞추며 남긴 ",,,,")은 건너뛴다.
    private static Dictionary<string, Section> SplitIntoSections(string csvText)
    {
        Dictionary<string, Section> sections = new(StringComparer.OrdinalIgnoreCase);
        Section current = null;

        foreach (string[] record in ParseRecords(csvText))
        {
            if (IsBlank(record))
                continue;

            string first = record[0].Trim().TrimStart('﻿');
            if (first.StartsWith("#", StringComparison.Ordinal))
            {
                string name = first.Substring(1).Trim();
                if (!sections.TryGetValue(name, out current))
                {
                    current = new Section();
                    sections.Add(name, current);
                }
                continue;
            }

            if (current == null)
                continue;

            if (current.Header == null)
                current.Header = record;
            else
                current.Rows.Add(record);
        }

        return sections;
    }

    // 파일 전체를 한 번에 읽어 레코드로 나눈다. 줄 단위로 나누면 큰따옴표 안의 줄바꿈(여러 줄 대사)이
    // 레코드를 끊어버리므로, 따옴표 상태를 추적하며 따옴표 밖의 줄바꿈에서만 레코드를 자른다.
    private static IEnumerable<string[]> ParseRecords(string text)
    {
        List<string> fields = new();
        StringBuilder field = new();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    yield return fields.ToArray();
                    fields.Clear();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields.ToArray();
        }
    }

    private static bool IsBlank(string[] record)
    {
        for (int i = 0; i < record.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(record[i]))
                return false;
        }
        return true;
    }

    private static string Field(string[] row, int index)
    {
        return index >= 0 && index < row.Length ? row[index].Trim() : string.Empty;
    }

    private static bool AddIfPresent(List<string> target, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        target.Add(value.Trim());
        return true;
    }

    // 리스트 구분자는 '|'가 표준이지만, 예전 문서대로 쉼표로 쓴(따옴표로 감싼) 플래그 목록도 함께 받아준다.
    private static List<string> SplitList(string value)
    {
        List<string> result = new();
        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (string item in value.Split('|', ','))
        {
            string trimmed = item.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                result.Add(trimmed);
        }
        return result;
    }

    // 형식: varName+5 | varName-3
    private static List<VarChange> ParseVarChangeList(string value)
    {
        List<VarChange> result = new();
        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (string item in value.Split('|'))
        {
            string token = item.Trim();
            int plus = token.LastIndexOf('+');
            int minus = token.LastIndexOf('-');
            int splitAt = plus > 0 && plus > minus ? plus : minus > 0 ? minus : -1;
            if (splitAt < 0)
                continue;

            if (!int.TryParse(token.Substring(splitAt + 1).Trim(), out int amount))
                continue;

            result.Add(new VarChange
            {
                varName = token.Substring(0, splitAt).Trim(),
                delta = token[splitAt] == '-' ? -amount : amount
            });
        }

        return result;
    }

    private static VarCondition TryParseVarCondition(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        // 긴 연산자부터 확인해 ">="가 ">"로 잘못 잘리지 않게 한다.
        foreach (string op in CompareOperators)
        {
            int index = token.IndexOf(op, StringComparison.Ordinal);
            if (index <= 0)
                continue;

            if (!int.TryParse(token.Substring(index + op.Length).Trim(), out int threshold))
                continue;

            return new VarCondition
            {
                varName = token.Substring(0, index).Trim(),
                op = ParseCompareOp(op),
                threshold = threshold
            };
        }

        return null;
    }

    public static CompareOp ParseCompareOp(string op)
    {
        return op?.Trim() switch
        {
            ">=" => CompareOp.GreaterOrEqual,
            ">" => CompareOp.Greater,
            "==" => CompareOp.Equal,
            "<" => CompareOp.Less,
            "<=" => CompareOp.LessOrEqual,
            _ => CompareOp.GreaterOrEqual
        };
    }

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum
    {
        return Enum.TryParse(value?.Trim(), true, out T parsed) && Enum.IsDefined(typeof(T), parsed)
            ? parsed
            : fallback;
    }

    private static GameCurrency ParseCurrency(string value) => ParseEnum(value, GameCurrency.Money);

    private static bool ParseBoolean(string value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string normalized = value.Trim();
        if (bool.TryParse(normalized, out bool parsed))
            return parsed;
        if (normalized == "1")
            return true;
        if (normalized == "0")
            return false;
        return fallback;
    }

    private static float ParsePositiveFloat(string value, float fallback)
    {
        return float.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            && result > 0f
                ? result
                : fallback;
    }

    private static int ParseNonNegativeInt(string value)
    {
        return int.TryParse(value, out int result) && result > 0 ? result : 0;
    }
}
