using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Slainte.Business;
using Slainte.Content;
using Slainte.EditorTools;
using UnityEditor;
using UnityEngine;

namespace NarrativeFlow.Editor
{
    public sealed class CompileReport
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public bool HasErrors => Errors.Count > 0;
    }

    // NarrativeGraphSO(비주얼 그래프)를 런타임 EpisodeData(노드 리스트 + nextNodeId 체인)로 컴파일한다.
    // 그래프 블록 하나는 여러 이벤트를 담고, 이벤트 각각이 런타임 EpisodeNode 하나가 되어 실행 순서대로
    // nextNodeId로 이어진다. 블록의 마지막 노드는 블록 출력 포트(선택지/제조 결과/조건 라벨)로 바깥과 연결된다.
    // 노드 ID는 이벤트의 RuntimeNodeId를 그대로 쓰고(CSV에서 온 ID 보존), 없으면 블록 제목으로 새로 만들어
    // 이벤트에 기록한다 — 그래서 그래프 → CSV → 그래프 왕복에서도 ID가 바뀌지 않는다.
    public class EpisodeDataCompiler
    {
        private const string OrderTicketDatabasePath = BusinessAssetPaths.OrderTicketDatabase;

        [MenuItem("Narrative/Compile Selected Graph to EpisodeData")]
        public static void CompileSelected()
        {
            var graph = Selection.activeObject as NarrativeGraphSO;
            if (graph == null)
            {
                Debug.LogError("[EpisodeDataCompiler] Please select a NarrativeGraphSO asset.");
                return;
            }
            new EpisodeDataCompiler().Compile(graph);
        }

        // 컴파일 → EpisodeData 에셋 저장 → 원본 CSV 기록. 오류가 있거나 사용자가 취소하면 false.
        public bool Compile(NarrativeGraphSO graph)
        {
            // 번호 재부여는 구조 편집 때만 돌기 때문에, CSV에서 가져온 뒤 편집 없이(혹은 규칙이 바뀐 뒤)
            // 컴파일하면 저장된 옛 ID가 그대로 나간다. CSV에 쓰는 ID가 항상 현재 규칙을 따르도록 여기서 맞춘다.
            NarrativeNodeIdAssigner.RegenerateIds(graph);

            CompileReport report = new();
            EpisodeData data = Build(graph, report);
            foreach (string warning in report.Warnings)
                Debug.LogWarning($"[EpisodeDataCompiler] {graph.name}: {warning}");
            if (report.HasErrors)
            {
                foreach (string error in report.Errors)
                    Debug.LogError($"[EpisodeDataCompiler] {graph.name}: {error}");
                EditorUtility.DisplayDialog(
                    "컴파일 실패",
                    $"오류 {report.Errors.Count}개로 컴파일하지 않았습니다.\n\n" + string.Join("\n", report.Errors.Take(10)),
                    "확인");
                UnityEngine.Object.DestroyImmediate(data);
                return false;
            }

            string csvPath = NarrativeCsvSync.ResolveSourceCsvPath(graph, data.episodeId);
            if (NarrativeCsvSync.HasExternalChanges(graph, csvPath))
            {
                switch (NarrativeCsvSync.AskConflict(csvPath))
                {
                    case NarrativeCsvSync.ConflictChoice.Cancel:
                        UnityEngine.Object.DestroyImmediate(data);
                        return false;
                    case NarrativeCsvSync.ConflictChoice.ReloadFromCsv:
                        UnityEngine.Object.DestroyImmediate(data);
                        NarrativeCsvSync.ImportCsvToGraph(csvPath);
                        return false;
                }
            }

            // CSV 기록이 에셋 저장보다 먼저다 — SaveEpisodeAsset이 data를 기존 에셋에 병합한 뒤 파기하기 때문.
            NarrativeCsvSync.WriteSourceCsv(graph, csvPath, data);
            string episodeId = data.episodeId;
            int nodeCount = data.nodes.Count;
            string assetPath = SaveEpisodeAsset(data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[EpisodeDataCompiler] Compiled '{episodeId}' → {assetPath} ({nodeCount} nodes), CSV → {csvPath}");
            return true;
        }

        private static string SaveEpisodeAsset(EpisodeData data)
        {
            const string dir = ProjectResourcePaths.AssetRoot + ProjectResourcePaths.NarrativeEpisodes;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = $"{dir}/EpisodeData_{data.episodeId}.asset";
            data.name = $"EpisodeData_{data.episodeId}";

            var existing = AssetDatabase.LoadAssetAtPath<EpisodeData>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(data, existing);
                existing.name = data.name;
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(data);
            }
            else
            {
                AssetDatabase.CreateAsset(data, path);
            }
            return path;
        }

        // 에셋·파일을 건드리지 않는 변환(이벤트 RuntimeNodeId 부여만 그래프에 기록). 왕복 검증에서도 사용.
        public static EpisodeData Build(NarrativeGraphSO graph, CompileReport report)
        {
            return new Builder(graph, report).Run();
        }

        private sealed class Builder
        {
            private readonly NarrativeGraphSO graph;
            private readonly CompileReport report;
            private readonly EpisodeData data;
            private readonly Dictionary<string, NodeDataSO> nodeByGuid;
            private readonly Dictionary<string, List<EdgeData>> edgesBySource;
            private readonly Dictionary<string, List<EpisodeNode>> blockNodes = new();
            private readonly Dictionary<string, string> routerIds = new();
            private readonly HashSet<string> usedIds = new(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> resolving = new();
            private OrderTicketDatabase ticketDatabase;

            public Builder(NarrativeGraphSO graph, CompileReport report)
            {
                this.graph = graph;
                this.report = report;
                data = ScriptableObject.CreateInstance<EpisodeData>();
                nodeByGuid = graph.Nodes.Where(n => n != null && !string.IsNullOrEmpty(n.Guid))
                    .GroupBy(n => n.Guid).ToDictionary(g => g.Key, g => g.First());
                edgesBySource = graph.Edges.GroupBy(e => e.BaseNodeGuid)
                    .ToDictionary(g => g.Key, g => g.OrderBy(e => e.OutputPortIndex).ToList());
            }

            private IEnumerable<EpisodeNodeSO> Blocks => graph.Nodes.OfType<EpisodeNodeSO>();

            public EpisodeData Run()
            {
                CopyMetadata();
                AssignEventIds();

                // 1단계: 블록마다 이벤트를 런타임 노드로 만들고 블록 안에서 순서대로 잇는다.
                foreach (EpisodeNodeSO block in Blocks)
                {
                    List<EpisodeNode> nodes = NarrativeBlockModel.GetExecutionOrder(block)
                        .Where(NarrativeBlockModel.IsRuntimeEvent)
                        .Select(BuildRuntimeNode)
                        .ToList();
                    for (int i = 0; i < nodes.Count - 1; i++)
                        nodes[i].nextNodeId = nodes[i + 1].nodeId;
                    blockNodes[block.Guid] = nodes;
                    data.nodes.AddRange(nodes);
                }

                // 2단계: 각 블록의 마지막 노드를 출력 포트 연결대로 바깥과 잇는다.
                foreach (EpisodeNodeSO block in Blocks)
                    LinkBlockExits(block);

                data.firstNodeId = ResolveStartId();
                if (string.IsNullOrEmpty(data.firstNodeId))
                    report.Errors.Add("시작 노드를 찾지 못했습니다. Graph Settings에서 Start Node를 지정하세요.");

                OrderNodesForReading();
                return data;
            }

            private void CopyMetadata()
            {
                data.episodeId = !string.IsNullOrWhiteSpace(graph.EpisodeId) ? graph.EpisodeId.Trim() : graph.name;
                data.episodeTitle = !string.IsNullOrWhiteSpace(graph.EpisodeTitle) ? graph.EpisodeTitle : data.episodeId;
                data.chapterId = graph.ChapterId;
                data.scheduledDay = graph.ScheduledDay;
                data.scheduledSlot = graph.ScheduledSlot;
                data.slotPriority = graph.SlotPriority;
                // 그래프와 런타임 에셋이 같은 리스트 인스턴스를 공유하지 않도록 직렬화 복제한다.
                data.triggerCondition = JsonUtility.FromJson<EpisodeTriggerCondition>(
                    JsonUtility.ToJson(graph.TriggerCondition ?? new EpisodeTriggerCondition()));
                data.settlementRewards = (graph.SettlementRewards ?? new List<EpisodeSettlementReward>())
                    .Select(r => new EpisodeSettlementReward { requiredFlag = r.requiredFlag, label = r.label, amount = r.amount })
                    .ToList();
            }

            // 기존 ID(CSV 유래 포함)를 먼저 예약한 뒤, ID가 없거나 중복된 이벤트에만 블록 제목 기반 ID를 새로 부여한다.
            private void AssignEventIds()
            {
                foreach (TriggerNodeSO trigger in graph.Nodes.OfType<TriggerNodeSO>())
                    if (!string.IsNullOrWhiteSpace(trigger.RuntimeNodeId)) usedIds.Add(trigger.RuntimeNodeId);
                foreach (EpisodeNodeSO block in Blocks)
                    if (!string.IsNullOrWhiteSpace(block.RouterNodeId)) usedIds.Add(block.RouterNodeId);

                List<(EpisodeNodeSO block, EpisodeEvent ev)> pending = new();
                foreach (EpisodeNodeSO block in Blocks)
                {
                    foreach (EpisodeEvent ev in block.Events.Where(NarrativeBlockModel.IsRuntimeEvent))
                    {
                        if (!string.IsNullOrWhiteSpace(ev.RuntimeNodeId) && usedIds.Add(ev.RuntimeNodeId.Trim()))
                            continue;
                        if (!string.IsNullOrWhiteSpace(ev.RuntimeNodeId))
                            report.Warnings.Add($"노드 ID '{ev.RuntimeNodeId}'가 중복되어 새 ID를 부여합니다(블록 {BlockTitle(block)}).");
                        pending.Add((block, ev));
                    }
                }

                foreach ((EpisodeNodeSO block, EpisodeEvent ev) in pending)
                {
                    List<EpisodeEvent> order = NarrativeBlockModel.GetExecutionOrder(block);
                    int index = Math.Max(0, order.IndexOf(ev));
                    string baseId = SanitizeId(BlockTitle(block));
                    ev.RuntimeNodeId = ReserveId(index == 0 ? baseId : $"{baseId}-{index + 1}");
                    EditorUtility.SetDirty(block);
                }
            }

            private string ReserveId(string desired)
            {
                string id = desired;
                for (int n = 2; !usedIds.Add(id); n++)
                    id = $"{desired}~{n}";
                return id;
            }

            private EpisodeNode BuildRuntimeNode(EpisodeEvent ev)
            {
                // 대사 필드는 선택지·제조 노드에도 쓰인다(선택지 앞 대사, 제조 전 캐릭터 표정 등).
                var n = new EpisodeNode
                {
                    nodeId = ev.RuntimeNodeId.Trim(),
                    speakerKey = ev.SpeakerKey,
                    overrideSpeakerName = ev.OverrideSpeakerName,
                    text = ev.Text,
                    characters = ev.CharacterAppearances.Select(c => new CharacterSlotEntry
                        { characterKey = c.CharacterKey, expressionKey = c.ExpressionKey, slotIndex = c.SlotIndex }).ToList(),
                    bgmCommand = ev.BgmCommand,
                    bgmClipName = ev.BgmClipName,
                    sfxCommand = ev.SfxCommand,
                    sfxClipName = ev.SfxClipName,
                    // 제조 노드가 아니면 의미 없는 값이지만, CSV 임포터의 기본값(false)과 맞춰 왕복 diff를 없앤다.
                    craftingPaymentEnabled = false
                };

                if (ev.Type == EpisodeEventType.Choice)
                {
                    n.choices = ev.Choices.Select(c => new EpisodeChoice
                    {
                        buttonText = c.ButtonText,
                        setFlags = new List<string>(c.SetFlags),
                        clearFlags = new List<string>(c.ClearFlags),
                        varChanges = c.VarChanges.Select(v => new VarChange { varName = v.VarName, delta = v.Delta }).ToList()
                    }).ToList();
                }
                else if (ev.Type == EpisodeEventType.BusinessStart)
                {
                    n.requiresCrafting = true;
                    n.craftingOrderTicket = ev.CraftingOrderTicket != null ? ev.CraftingOrderTicket : ResolveOrderTicket(ev.CraftingTicketKey);
                    n.craftingTicketKey = ev.CraftingTicketKey;
                    n.craftingOrderType = ev.CraftingOrderType;
                    n.craftingOrderTarget = ev.CraftingOrderTarget;
                    n.craftingPaymentEnabled = ev.CraftingPaymentEnabled;
                    n.craftingPaymentCurrency = ev.CraftingPaymentCurrency;
                    n.craftingPaymentMultiplier = BusinessOrderPriceRules.NormalizePaymentMultiplier(ev.CraftingPaymentMultiplier);
                    foreach (CraftingJobResult result in CraftingJobResultPorts.Order)
                    {
                        string flag = ev.GetCraftingFlag(result);
                        List<VarChangeData> vars = ev.GetCraftingVarChanges(result);
                        if (string.IsNullOrEmpty(flag) && vars.Count == 0) continue;
                        n.SetCraftingFlag(result, flag);
                        n.SetCraftingVarChanges(result, vars.Select(v => new VarChange { varName = v.VarName, delta = v.Delta }).ToList());
                    }
                }

                return n;
            }

            private OrderTicketData ResolveOrderTicket(string ticketKey)
            {
                if (string.IsNullOrWhiteSpace(ticketKey))
                    return null;
                ticketDatabase ??= AssetDatabase.LoadAssetAtPath<OrderTicketDatabase>(OrderTicketDatabasePath);
                return ticketDatabase != null ? ticketDatabase.FindByKey(ticketKey) : null;
            }

            private List<EdgeData> EdgesOf(string guid) =>
                edgesBySource.TryGetValue(guid, out var list) ? list : new List<EdgeData>();

            private static bool TryGetEdge(List<EdgeData> edges, int port, out EdgeData edge)
            {
                for (int i = 0; i < edges.Count; i++)
                {
                    if (edges[i].OutputPortIndex == port)
                    {
                        edge = edges[i];
                        return true;
                    }
                }
                edge = default;
                return false;
            }

            // ── 블록 출구 연결 ────────────────────────────────────────────────────────

            private void LinkBlockExits(EpisodeNodeSO block)
            {
                List<EpisodeNode> nodes = blockNodes[block.Guid];
                EpisodeNode last = nodes.Count > 0 ? nodes[nodes.Count - 1] : null;
                EpisodeEvent terminal = NarrativeBlockModel.GetTerminalEvent(block);
                List<EdgeData> edges = EdgesOf(block.Guid);

                if (terminal?.Type == EpisodeEventType.Choice && last != null)
                {
                    foreach (EdgeData edge in edges)
                    {
                        if (edge.OutputPortIndex < last.choices.Count)
                            last.choices[edge.OutputPortIndex].nextNodeId = ResolveTarget(edge.TargetNodeGuid, null);
                    }
                    return;
                }

                if (terminal?.Type == EpisodeEventType.BusinessStart && last != null)
                {
                    CraftingJobResult[] order = CraftingJobResultPorts.Order;
                    foreach (EdgeData edge in edges)
                    {
                        string target = ResolveTarget(edge.TargetNodeGuid, null);
                        if (edge.OutputPortIndex < order.Length && !string.IsNullOrEmpty(target))
                            last.SetNextNodeId(order[edge.OutputPortIndex], target);
                    }
                    last.craftingOutcomes = last.craftingOutcomes
                        .OrderBy(o => Array.IndexOf(order, o.result)).ToList();
                    return;
                }

                if (last != null)
                    ApplyBranchPorts(block, last, edges);
                // 빈 블록은 다른 블록이 가리킬 때 ResolveBlockEntry에서 통과/라우터로 처리한다.
            }

            // 조건 라벨 포트들을 host 노드의 분기 목록으로 옮긴다. Next 포트만 Trigger 노드를 인라인할 수 있다.
            private void ApplyBranchPorts(EpisodeNodeSO block, EpisodeNode host, List<EdgeData> edges)
            {
                bool nextAssigned = false;
                foreach (EdgeData edge in edges)
                {
                    string label = edge.OutputPortIndex < block.OutgoingBranches.Count
                        ? block.OutgoingBranches[edge.OutputPortIndex]
                        : string.Empty;
                    BranchLabel parsed = BranchLabel.Parse(label);
                    switch (parsed.Kind)
                    {
                        case BranchLabelKind.Next:
                            if (nextAssigned)
                            {
                                report.Warnings.Add($"블록 {BlockTitle(block)}에 조건 없는 포트가 여러 개라 첫 번째만 사용합니다.");
                                break;
                            }
                            nextAssigned = true;
                            string next = ResolveTarget(edge.TargetNodeGuid, host);
                            if (next != null) host.nextNodeId = next;
                            break;
                        case BranchLabelKind.Invalid:
                            report.Errors.Add($"블록 {BlockTitle(block)}: {parsed.Error}");
                            break;
                        default:
                            AddBranch(host, parsed, ResolveTarget(edge.TargetNodeGuid, null), $"블록 {BlockTitle(block)}");
                            break;
                    }
                }
            }

            private void AddBranch(EpisodeNode host, BranchLabel parsed, string target, string where)
            {
                if (string.IsNullOrEmpty(target))
                {
                    report.Warnings.Add($"{where}: 조건 포트의 대상이 없어 무시합니다.");
                    return;
                }

                NodeBranch branch = parsed.ToNodeBranch(target);
                if (branch != null)
                    host.branches.Add(branch);
            }

            // 포트가 가리키는 그래프 노드를 런타임 nodeId로 바꾼다. inlineHost가 있으면 Trigger 노드를 그 노드의
            // 분기로 인라인하고(호스트의 nextNodeId는 여기서 직접 설정) null을 돌려준다. 인라인할 수 없는 곳
            // (선택지·제조 포트, 조건 포트)에서는 Trigger 노드를 대사 없는 독립 라우터 노드로 만든다.
            private string ResolveTarget(string guid, EpisodeNode inlineHost)
            {
                if (string.IsNullOrEmpty(guid) || !nodeByGuid.TryGetValue(guid, out NodeDataSO target))
                    return null;

                if (target is EpisodeNodeSO block)
                    return ResolveBlockEntry(block);

                if (target is TriggerNodeSO trigger)
                {
                    if (inlineHost != null)
                    {
                        inlineHost.nextNodeId = InjectTrigger(inlineHost, trigger);
                        return null;
                    }
                    return ResolveRouter(
                        trigger.Guid,
                        trigger.RuntimeNodeId,
                        id => trigger.RuntimeNodeId = id,
                        $"T_{SanitizeId(trigger.name)}",
                        router => router.nextNodeId = InjectTrigger(router, trigger),
                        trigger);
                }

                return null;
            }

            private string ResolveBlockEntry(EpisodeNodeSO block)
            {
                List<EpisodeNode> nodes = blockNodes[block.Guid];
                if (nodes.Count > 0)
                    return nodes[0].nodeId;

                // 이벤트가 없는 블록: 조건 포트가 없으면 연결된 포트를 그대로 통과하고, 있으면 라우터 노드가 된다.
                if (!resolving.Add(block.Guid))
                {
                    report.Errors.Add($"빈 블록끼리 순환 연결되어 있습니다: {BlockTitle(block)}");
                    return null;
                }

                try
                {
                    List<EdgeData> edges = EdgesOf(block.Guid);
                    bool hasConditional = edges.Any(e => e.OutputPortIndex < block.OutgoingBranches.Count
                        && BranchLabel.Parse(block.OutgoingBranches[e.OutputPortIndex]).Kind != BranchLabelKind.Next);
                    if (!hasConditional)
                        return edges.Count > 0 ? ResolveTarget(edges[0].TargetNodeGuid, null) : null;

                    return ResolveRouter(
                        block.Guid,
                        block.RouterNodeId,
                        id => block.RouterNodeId = id,
                        SanitizeId(BlockTitle(block)),
                        router => ApplyBranchPorts(block, router, edges),
                        block);
                }
                finally
                {
                    resolving.Remove(block.Guid);
                }
            }

            // 대사 없는 라우터 노드를 한 번만 만든다(런타임 EpisodeRunner가 즉시 통과).
            private string ResolveRouter(
                string key,
                string storedId,
                Action<string> storeId,
                string fallbackId,
                Action<EpisodeNode> fill,
                UnityEngine.Object owner)
            {
                if (routerIds.TryGetValue(key, out string existing))
                    return existing;

                string id = storedId;
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = ReserveId(fallbackId);
                    storeId(id);
                    EditorUtility.SetDirty(owner);
                }

                // 채우기 전에 등록해 두어야 라우터를 다시 가리키는 순환 연결에서도 같은 노드를 재사용한다.
                routerIds[key] = id;
                EpisodeNode router = new() { nodeId = id };
                data.nodes.Add(router);
                fill(router);
                return id;
            }

            // Trigger 노드의 조건들을 host 분기로 옮기고 Else 포트 대상 nodeId를 돌려준다.
            private string InjectTrigger(EpisodeNode host, TriggerNodeSO trigger)
            {
                List<EdgeData> edges = EdgesOf(trigger.Guid);
                string where = $"Trigger {EpisodeDataCompiler.BlockTitle(trigger)}";
                for (int i = 0; i < trigger.Conditions.Count; i++)
                {
                    if (!TryGetEdge(edges, i, out EdgeData edge))
                        continue;

                    BranchLabel parsed = ParseTriggerCondition(trigger.Conditions[i]);
                    if (parsed.Kind == BranchLabelKind.Invalid || parsed.Kind == BranchLabelKind.Next)
                    {
                        report.Errors.Add($"{where} 조건 {i}: {parsed.Error ?? "조건이 비어 있습니다."}");
                        continue;
                    }

                    AddBranch(host, parsed, ResolveTarget(edge.TargetNodeGuid, null), where);
                }

                return TryGetEdge(edges, trigger.Conditions.Count, out EdgeData elseEdge)
                    ? ResolveTarget(elseEdge.TargetNodeGuid, null)
                    : null;
            }

            private static BranchLabel ParseTriggerCondition(GraphTriggerCondition cond)
            {
                string key = cond.Key?.Trim() ?? string.Empty;
                string op = string.IsNullOrWhiteSpace(cond.Operator) ? "==" : cond.Operator.Trim();
                return cond.Type switch
                {
                    TriggerConditionType.Flag => BranchLabel.Parse($"{key} {op} {cond.Value}"),
                    TriggerConditionType.Variable => BranchLabel.Parse($"{key} {op} {cond.Value}"),
                    TriggerConditionType.Episode => BranchLabel.Parse(key),
                    _ => BranchLabel.Parse(string.Empty)
                };
            }

            private string ResolveStartId()
            {
                if (!string.IsNullOrEmpty(graph.StartNodeGuid) && nodeByGuid.ContainsKey(graph.StartNodeGuid))
                    return ResolveTarget(graph.StartNodeGuid, null);

                HashSet<string> incoming = new(graph.Edges.Select(e => e.TargetNodeGuid));
                EpisodeNodeSO fallback = Blocks.FirstOrDefault(n => !incoming.Contains(n.Guid));
                if (fallback != null)
                    report.Warnings.Add($"Start Node가 지정되지 않아 들어오는 연결이 없는 블록 {BlockTitle(fallback)}에서 시작합니다.");
                return fallback != null ? ResolveBlockEntry(fallback) : null;
            }

            // CSV를 위에서 아래로 읽을 수 있게 대화 흐름(블록) 순서로 정렬하고, 도달할 수 없는 노드는 뒤에 붙인다.
            private void OrderNodesForReading()
            {
                EpisodeGraphTraversal.Flow flow = EpisodeGraphTraversal.BuildFlow(data);
                if (flow.Unreachable.Count > 0)
                    report.Warnings.Add($"시작 노드에서 도달할 수 없는 노드 {flow.Unreachable.Count}개: {string.Join(", ", flow.Unreachable.Take(8).Select(n => n.nodeId))}");
                List<EpisodeNode> ordered = new(flow.Order);
                ordered.AddRange(flow.Unreachable);
                // 중복 ID처럼 흐름 계산에서 빠진 노드도 잃지 않게 원래 순서대로 뒤에 붙인다.
                HashSet<EpisodeNode> placed = new(ordered);
                ordered.AddRange(data.nodes.Where(n => n != null && !placed.Contains(n)));
                data.nodes = ordered;
            }
        }

        public static string BlockTitle(NodeDataSO node)
        {
            CustomNodeField title = node.CustomFields?.Find(f =>
                string.Equals(f.FieldName, "Title", StringComparison.OrdinalIgnoreCase));
            return !string.IsNullOrWhiteSpace(title?.FieldValue) ? title.FieldValue : node.name;
        }

        // CSV 셀과 분기 라벨에서 문제가 되는 문자(쉼표·공백·따옴표·&,|,=)를 밑줄로 바꾼다.
        private static string SanitizeId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "n";
            char[] chars = raw.Trim().Select(c => ",\"' &|=\t\r\n".IndexOf(c) >= 0 ? '_' : c).ToArray();
            return new string(chars);
        }
    }

    // 런타임 노드 사이의 연결 해석(정렬·ID 부여·검증·임포트 공용).
    public static class EpisodeGraphTraversal
    {
        // 다음으로 갈 수 있는 모든 노드 ID(빈 값 포함). 도달 가능성 검사처럼 빠짐없이 봐야 할 때 쓴다.
        public static IEnumerable<string> Successors(EpisodeNode node)
        {
            foreach (EpisodeChoice choice in node.choices) yield return choice.nextNodeId;
            foreach (CraftingOutcome outcome in node.craftingOutcomes) yield return outcome.nextNodeId;
            foreach (NodeBranch branch in node.branches) yield return branch.nextNodeId;
            yield return node.nextNodeId;
        }

        // 노드의 출력 포트 대상(빈 포트 포함, 포트 순서). 선택지 노드는 버튼, 제조 노드는 결과, 그 외는
        // 조건 분기(목록 순서) → Next — 그래프 블록의 포트 순서와 같다.
        public static List<string> Ports(EpisodeNode node)
        {
            if (node.choices.Count > 0)
                return node.choices.Select(c => c.nextNodeId).ToList();
            if (node.requiresCrafting)
                return node.craftingOutcomes.Count > 0
                    ? node.craftingOutcomes.Select(o => o.nextNodeId).ToList()
                    : new List<string> { node.nextNodeId };

            List<string> ports = new();
            ports.AddRange(node.branches.Select(b => b.nextNodeId));
            ports.Add(node.nextNodeId);
            return ports;
        }

        // 앞으로 가는 연결 하나(되돌아오는 연결 제외). Port는 같은 대상으로 가는 포트 중 첫 번째,
        // FromBranch는 출발 노드의 포트가 2개 이상(분기)인지 — 빈 포트도 갈래로 센다.
        public readonly struct ForwardEdge
        {
            public readonly string From;
            public readonly int Port;
            public readonly bool FromBranch;

            public ForwardEdge(string from, int port, bool fromBranch)
            {
                From = from;
                Port = port;
                FromBranch = fromBranch;
            }
        }

        public sealed class Flow
        {
            public readonly List<EpisodeNode> Order = new();
            public readonly List<EpisodeNode> Unreachable = new();
            public readonly Dictionary<string, List<ForwardEdge>> Incoming = new(StringComparer.OrdinalIgnoreCase);
            // 노드별 앞으로 가는 대상(포트 순서, 되돌아오는 연결 제외).
            public readonly Dictionary<string, List<string>> Forward = new(StringComparer.OrdinalIgnoreCase);
        }

        // 대화 흐름(블록) 순서: 분기를 만나면 갈래 1을 끝까지, 다음 갈래를 끝까지 나열하고, 합류 노드는
        // 들어오는 갈래가 모두 나온 뒤에 둔다. 되돌아오는 연결(순환)은 순서 계산에서 뺀다.
        // WHY: 가까운 노드부터(BFS) 나열하면 갈래들이 한 줄씩 섞이고 합류 노드가 분기 중간에 끼어
        // CSV를 위에서 아래로 읽을 수 없게 된다.
        public static Flow BuildFlow(EpisodeData data)
        {
            Flow flow = new();
            Dictionary<string, EpisodeNode> byId = new(StringComparer.OrdinalIgnoreCase);
            foreach (EpisodeNode node in data.nodes)
                if (node != null && !string.IsNullOrWhiteSpace(node.nodeId))
                    byId.TryAdd(node.nodeId, node);

            Dictionary<string, List<(string to, int port)>> targets = new(StringComparer.OrdinalIgnoreCase);
            List<(string to, int port)> TargetsOf(string id)
            {
                if (targets.TryGetValue(id, out var list)) return list;
                list = new List<(string, int)>();
                HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
                List<string> ports = Ports(byId[id]);
                for (int i = 0; i < ports.Count; i++)
                    if (!string.IsNullOrWhiteSpace(ports[i]) && byId.ContainsKey(ports[i]) && seen.Add(ports[i]))
                        list.Add((ports[i], i));
                targets[id] = list;
                return list;
            }

            string start = data.firstNodeId;
            if (string.IsNullOrWhiteSpace(start) || !byId.ContainsKey(start))
            {
                flow.Unreachable.AddRange(byId.Values);
                return flow;
            }

            // 되돌아오는 연결 = 포트 순서 DFS에서 아직 스택 위에 있는 노드로 가는 연결.
            HashSet<(string, string)> backEdges = new();
            HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase) { start };
            HashSet<string> onStack = new(StringComparer.OrdinalIgnoreCase) { start };
            Stack<(string id, int next)> dfs = new();
            dfs.Push((start, 0));
            while (dfs.Count > 0)
            {
                (string id, int next) = dfs.Pop();
                List<(string to, int port)> outs = TargetsOf(id);
                if (next >= outs.Count)
                {
                    onStack.Remove(id);
                    continue;
                }
                dfs.Push((id, next + 1));
                string to = outs[next].to;
                if (onStack.Contains(to))
                    backEdges.Add((id, to));
                else if (visited.Add(to))
                {
                    onStack.Add(to);
                    dfs.Push((to, 0));
                }
            }

            foreach (string id in visited)
            {
                bool branch = Ports(byId[id]).Count >= 2;
                List<string> forward = new();
                flow.Forward[id] = forward;
                foreach ((string to, int port) in TargetsOf(id))
                {
                    if (backEdges.Contains((id, to))) continue;
                    forward.Add(to);
                    if (!flow.Incoming.TryGetValue(to, out List<ForwardEdge> list))
                        flow.Incoming[to] = list = new List<ForwardEdge>();
                    list.Add(new ForwardEdge(id, port, branch));
                }
            }

            // 스택 기반 위상 정렬: 첫 포트 대상이 맨 위에 오게 넣어 갈래 하나를 끝까지 따라간다.
            // 합류 노드는 남은 진입 수가 0이 될 때(모든 갈래가 나온 뒤)에야 스택에 오른다.
            Dictionary<string, int> pending = flow.Incoming.ToDictionary(
                p => p.Key, p => p.Value.Count, StringComparer.OrdinalIgnoreCase);
            Stack<string> ready = new();
            ready.Push(start);
            while (ready.Count > 0)
            {
                string id = ready.Pop();
                flow.Order.Add(byId[id]);
                List<(string to, int port)> outs = TargetsOf(id);
                for (int i = outs.Count - 1; i >= 0; i--)
                {
                    string to = outs[i].to;
                    if (backEdges.Contains((id, to))) continue;
                    if (--pending[to] == 0) ready.Push(to);
                }
            }

            foreach (EpisodeNode node in byId.Values)
                if (!visited.Contains(node.nodeId))
                    flow.Unreachable.Add(node);
            return flow;
        }
    }
}
