using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NarrativeFlow.Editor
{
    // EpisodeData(CSV에서 임포트된 런타임 데이터)를 NarrativeGraphSO로 역변환한다 — EpisodeDataCompiler의 반대.
    // 분기·합류가 없는 직선 대사는 블록 하나로 묶고(노드 하나 = 블록 하나면 그래프가 읽을 수 없게 커지므로),
    // 선택지·제조·조건 분기는 블록의 마지막 이벤트와 출력 포트로 표현한다. 각 이벤트에 원래 nodeId를
    // RuntimeNodeId로 보존해 다시 컴파일해도 같은 EpisodeData가 나온다. 이미 그래프가 있으면 블록 첫
    // 이벤트의 nodeId가 같은 블록의 위치를 그대로 재사용해 손으로 정리한 배치를 지킨다.
    public static class EpisodeDataImporter
    {
        public const string GraphFolder = "Assets/_Project/Features/Narrative/Content/Graphs";

        public sealed class BuildResult
        {
            public NarrativeGraphSO Graph;
            public readonly List<string> Warnings = new();
        }

        [MenuItem("Narrative/Import EpisodeData to Graph")]
        public static void ImportSelected()
        {
            var source = Selection.activeObject as EpisodeData;
            if (source == null)
            {
                Debug.LogError("[EpisodeDataImporter] Please select an EpisodeData ScriptableObject.");
                return;
            }
            NarrativeGraphSO graph = Import(source);
            if (graph != null)
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = graph;
            }
        }

        public static string GraphPathFor(string episodeId) => $"{GraphFolder}/{episodeId}.asset";

        // 그래프 에셋으로 저장한다. 기존 그래프가 있으면 에셋을 지우지 않고 내용만 교체한다
        // (에셋 GUID가 유지되어야 에디터 창의 "마지막으로 연 그래프" 등 참조가 끊기지 않는다).
        public static NarrativeGraphSO Import(EpisodeData source)
        {
            if (!Directory.Exists(GraphFolder)) Directory.CreateDirectory(GraphFolder);
            string path = GraphPathFor(source.episodeId);
            NarrativeGraphSO existing = AssetDatabase.LoadAssetAtPath<NarrativeGraphSO>(path);

            BuildResult built = Build(source, existing);
            foreach (string warning in built.Warnings)
                Debug.LogWarning($"[EpisodeDataImporter] {source.episodeId}: {warning}");

            NarrativeGraphSO graph = existing;
            if (graph == null)
            {
                graph = built.Graph;
                AssetDatabase.CreateAsset(graph, path);
            }
            else
            {
                foreach (NodeDataSO old in graph.Nodes.Where(n => n != null).ToList())
                {
                    AssetDatabase.RemoveObjectFromAsset(old);
                    UnityEngine.Object.DestroyImmediate(old, true);
                }
                CopyGraphContent(built.Graph, graph);
                UnityEngine.Object.DestroyImmediate(built.Graph);
            }

            foreach (NodeDataSO node in graph.Nodes)
                AssetDatabase.AddObjectToAsset(node, graph);

            // 그래프의 nodeId는 항상 자동 규칙을 따른다 — CSV에 다른 규칙으로 적힌 번호도 가져오는 즉시 정리한다.
            // (CSV 파일 자체는 여기서 다시 쓰지 않고, 다음 Compile 때 정리된 번호로 저장된다.)
            NarrativeNodeIdAssigner.RegenerateIds(graph);
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
            Debug.Log($"[EpisodeDataImporter] Imported '{source.episodeId}' → {path} ({graph.Nodes.Count} blocks)");
            return graph;
        }

        private static void CopyGraphContent(NarrativeGraphSO from, NarrativeGraphSO to)
        {
            to.EpisodeId = from.EpisodeId;
            to.EpisodeTitle = from.EpisodeTitle;
            to.ChapterId = from.ChapterId;
            to.ScheduledDay = from.ScheduledDay;
            to.ScheduledSlot = from.ScheduledSlot;
            to.SlotPriority = from.SlotPriority;
            to.TriggerCondition = from.TriggerCondition;
            to.SettlementRewards = from.SettlementRewards;
            to.StartNodeGuid = from.StartNodeGuid;
            to.Nodes = from.Nodes;
            to.Edges = from.Edges;
        }

        // 에셋을 만들지 않는 메모리 변환(노드 SO들은 Graph.Nodes에만 들어 있다). 왕복 검증에서도 사용.
        public static BuildResult Build(EpisodeData source, NarrativeGraphSO layoutSource = null)
        {
            BuildResult result = new();
            NarrativeGraphSO graph = ScriptableObject.CreateInstance<NarrativeGraphSO>();
            result.Graph = graph;

            graph.EpisodeId = source.episodeId;
            graph.EpisodeTitle = source.episodeTitle;
            graph.ChapterId = source.chapterId;
            graph.ScheduledDay = source.scheduledDay;
            graph.ScheduledSlot = source.scheduledSlot;
            graph.SlotPriority = source.slotPriority;
            graph.TriggerCondition = JsonUtility.FromJson<EpisodeTriggerCondition>(
                JsonUtility.ToJson(source.triggerCondition ?? new EpisodeTriggerCondition()));
            graph.SettlementRewards = (source.settlementRewards ?? new List<EpisodeSettlementReward>())
                .Select(r => new EpisodeSettlementReward { requiredFlag = r.requiredFlag, label = r.label, amount = r.amount })
                .ToList();

            Dictionary<string, EpisodeNode> byId = new(StringComparer.OrdinalIgnoreCase);
            foreach (EpisodeNode node in source.nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.nodeId)) continue;
                if (!byId.TryAdd(node.nodeId, node))
                    result.Warnings.Add($"nodeId '{node.nodeId}'가 중복되어 뒤쪽 노드를 무시합니다(런타임에서도 실행되지 않음).");
            }

            List<List<EpisodeNode>> chains = BuildChains(source, byId);
            Dictionary<string, EpisodeNodeSO> blockByFirstId = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Rect> savedPositions = CollectPositions(layoutSource);

            foreach (List<EpisodeNode> chain in chains)
            {
                EpisodeNodeSO block = CreateBlock(chain, result);
                graph.Nodes.Add(block);
                blockByFirstId[chain[0].nodeId] = block;
            }

            for (int i = 0; i < chains.Count; i++)
                AddBlockEdges(graph, (EpisodeNodeSO)graph.Nodes[i], chains[i][chains[i].Count - 1], blockByFirstId, result);

            if (!string.IsNullOrEmpty(source.firstNodeId) && blockByFirstId.TryGetValue(source.firstNodeId, out EpisodeNodeSO start))
                graph.StartNodeGuid = start.Guid;
            else
                result.Warnings.Add($"firstNodeId '{source.firstNodeId}'를 찾지 못했습니다.");

            LayoutBlocks(graph, savedPositions);
            return result;
        }

        // 노드들을 블록 단위 체인으로 묶는다. N이 앞 노드 P의 블록에 이어 붙는 조건:
        // P가 분기·선택지·제조 없이 nextNodeId 하나로만 N을 가리키고, N을 가리키는 곳이 P 하나뿐이며, N이 시작 노드가 아닐 것.
        private static List<List<EpisodeNode>> BuildChains(EpisodeData source, Dictionary<string, EpisodeNode> byId)
        {
            Dictionary<string, int> incoming = new(StringComparer.OrdinalIgnoreCase);
            void Count(string id)
            {
                if (!string.IsNullOrEmpty(id))
                    incoming[id] = incoming.TryGetValue(id, out int c) ? c + 1 : 1;
            }
            Count(source.firstNodeId);
            foreach (EpisodeNode node in byId.Values)
                foreach (string next in EpisodeGraphTraversal.Successors(node))
                    Count(next);

            bool Continues(EpisodeNode from, out EpisodeNode next)
            {
                next = null;
                return IsLinear(from)
                    && !string.Equals(from.nodeId, from.nextNodeId, StringComparison.OrdinalIgnoreCase)
                    && byId.TryGetValue(from.nextNodeId, out next)
                    && incoming.TryGetValue(next.nodeId, out int c) && c == 1
                    && !string.Equals(next.nodeId, source.firstNodeId, StringComparison.OrdinalIgnoreCase);
            }

            HashSet<string> continued = new(StringComparer.OrdinalIgnoreCase);
            foreach (EpisodeNode node in byId.Values)
                if (Continues(node, out EpisodeNode next))
                    continued.Add(next.nodeId);

            // 시작 노드부터 실행 순서(BFS)로 블록을 만든다.
            List<List<EpisodeNode>> chains = new();
            HashSet<string> placed = new(StringComparer.OrdinalIgnoreCase);
            foreach (EpisodeNode head in BfsOrder(source, byId))
            {
                if (placed.Contains(head.nodeId) || continued.Contains(head.nodeId))
                    continue;

                List<EpisodeNode> chain = new() { head };
                placed.Add(head.nodeId);
                EpisodeNode current = head;
                while (Continues(current, out EpisodeNode next) && placed.Add(next.nodeId))
                {
                    chain.Add(next);
                    current = next;
                }
                chains.Add(chain);
            }

            // 직선 순환 안에서만 이어지는 노드(모두 continued라 머리가 없는 경우)도 빠짐없이 블록으로 만든다.
            foreach (EpisodeNode node in byId.Values)
            {
                if (placed.Add(node.nodeId))
                    chains.Add(new List<EpisodeNode> { node });
            }

            return chains;
        }

        private static bool IsLinear(EpisodeNode node) =>
            !node.requiresCrafting
            && node.choices.Count == 0
            && node.branches.Count == 0
            && !string.IsNullOrEmpty(node.nextNodeId);

        private static IEnumerable<EpisodeNode> BfsOrder(EpisodeData data, Dictionary<string, EpisodeNode> byId)
        {
            HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
            Queue<string> queue = new();
            if (!string.IsNullOrEmpty(data.firstNodeId)) queue.Enqueue(data.firstNodeId);
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (string.IsNullOrEmpty(id) || !visited.Add(id) || !byId.TryGetValue(id, out EpisodeNode node))
                    continue;
                yield return node;
                foreach (string next in EpisodeGraphTraversal.Successors(node))
                    queue.Enqueue(next);
            }

            foreach (EpisodeNode node in byId.Values)
                if (!visited.Contains(node.nodeId))
                    yield return node;
        }

        private static EpisodeNodeSO CreateBlock(List<EpisodeNode> chain, BuildResult result)
        {
            EpisodeNodeSO block = ScriptableObject.CreateInstance<EpisodeNodeSO>();
            block.Guid = Guid.NewGuid().ToString();
            block.name = block.Guid;
            block.CustomFields = new List<CustomNodeField> { new() { FieldName = "Title", FieldValue = chain[0].nodeId } };
            block.Events = chain.Select(n => BuildEvent(n, result)).ToList();

            block.OutgoingBranches = BuildPortLabels(chain[chain.Count - 1], result);
            return block;
        }

        private static EpisodeEvent BuildEvent(EpisodeNode node, BuildResult result)
        {
            var ev = new EpisodeEvent
            {
                Guid = Guid.NewGuid().ToString(),
                RuntimeNodeId = node.nodeId,
                SpeakerKey = node.speakerKey,
                OverrideSpeakerName = node.overrideSpeakerName,
                Text = node.text,
                CharacterAppearances = node.characters.Select(c => new CharacterSlotEntryData
                    { CharacterKey = c.characterKey, ExpressionKey = c.expressionKey, SlotIndex = c.slotIndex }).ToList(),
                BgmCommand = node.bgmCommand,
                BgmClipName = node.bgmClipName,
                SfxCommand = node.sfxCommand,
                SfxClipName = node.sfxClipName
            };

            if (node.requiresCrafting)
            {
                ev.Type = EpisodeEventType.BusinessStart;
                ev.CraftingOrderTicket = node.craftingOrderTicket;
                ev.CraftingTicketKey = node.craftingTicketKey;
                ev.CraftingOrderType = node.craftingOrderType;
                ev.CraftingOrderTarget = node.craftingOrderTarget;
                ev.CraftingPaymentEnabled = node.craftingPaymentEnabled;
                ev.CraftingPaymentCurrency = node.craftingPaymentCurrency;
                ev.CraftingPaymentMultiplier =
                    Slainte.Business.BusinessOrderPriceRules.NormalizePaymentMultiplier(node.craftingPaymentMultiplier);
                foreach (CraftingJobResult res in CraftingJobResultPorts.Order)
                {
                    string flag = ExactCraftingFlag(node, res);
                    List<VarChange> vars = ExactCraftingVars(node, res);
                    if (string.IsNullOrEmpty(flag) && vars.Count == 0) continue;
                    ev.SetCraftingFlag(res, flag);
                    ev.SetCraftingVarChanges(res, vars.Select(v => new VarChangeData { VarName = v.varName, Delta = v.delta }).ToList());
                }
                if (!string.IsNullOrEmpty(node.nextNodeId))
                    result.Warnings.Add($"제조 노드 '{node.nodeId}'의 nextNodeId는 그래프에서 표현하지 않습니다. 결과별 분기를 사용하세요.");
            }
            else if (node.choices.Count > 0)
            {
                ev.Type = EpisodeEventType.Choice;
                ev.Choices = node.choices.Select(c => new ChoiceOptionData
                {
                    ButtonText = c.buttonText,
                    SetFlags = new List<string>(c.setFlags),
                    ClearFlags = new List<string>(c.clearFlags),
                    VarChanges = c.varChanges.Select(v => new VarChangeData { VarName = v.varName, Delta = v.delta }).ToList()
                }).ToList();
                if (!string.IsNullOrEmpty(node.nextNodeId)
                    || node.branches.Count > 0)
                    result.Warnings.Add($"선택지 노드 '{node.nodeId}'의 nextNodeId/조건 분기는 런타임에서 쓰이지 않아 가져오지 않습니다.");
            }
            else
            {
                ev.Type = EpisodeEventType.Dialogue;
            }

            return ev;
        }

        // 결과별 정확한 항목만 본다(GetCraftingFlag는 Mid가 없으면 Bad로 폴백하므로 그대로 쓰면 항목이 복제된다).
        // 신규 craftingOutcomes가 하나도 없는 예전 데이터만 Good/Bad 레거시 값을 읽는다.
        private static bool UsesLegacy(EpisodeNode node, CraftingJobResult res) =>
            node.craftingOutcomes.Count == 0 && (res == CraftingJobResult.Good || res == CraftingJobResult.Bad);

        private static string ExactCraftingFlag(EpisodeNode node, CraftingJobResult res) =>
            UsesLegacy(node, res) ? node.GetCraftingFlag(res) : node.GetCraftingOutcome(res)?.flag;

        private static List<VarChange> ExactCraftingVars(EpisodeNode node, CraftingJobResult res) =>
            UsesLegacy(node, res)
                ? node.GetCraftingVarChanges(res)
                : node.GetCraftingOutcome(res)?.varChanges ?? new List<VarChange>();

        private static string ExactCraftingNext(EpisodeNode node, CraftingJobResult res) =>
            UsesLegacy(node, res) ? node.GetNextNodeId(res) : node.GetCraftingOutcome(res)?.nextNodeId;

        // 포트 라벨 순서(조건 분기 목록 순서 → Next)는 런타임 판정 순서와 같고, AddBlockEdges의 포트 인덱스와 반드시 일치해야 한다.
        private static List<string> BuildPortLabels(EpisodeNode last, BuildResult result)
        {
            if (last.requiresCrafting)
                return CraftingJobResultPorts.Order.Select(CraftingJobResultPorts.Label).ToList();
            if (last.choices.Count > 0)
                return last.choices.Select((c, i) => string.IsNullOrWhiteSpace(c.buttonText) ? $"Choice {i}" : c.buttonText).ToList();

            List<string> labels = last.branches.Select(BranchLabel.Format).ToList();
            labels.Add(NarrativeBlockModel.NextLabel);
            return labels;
        }

        private static void AddBlockEdges(
            NarrativeGraphSO graph,
            EpisodeNodeSO block,
            EpisodeNode last,
            Dictionary<string, EpisodeNodeSO> blockByFirstId,
            BuildResult result)
        {
            void Add(string targetId, int port)
            {
                if (string.IsNullOrEmpty(targetId)) return;
                if (!blockByFirstId.TryGetValue(targetId, out EpisodeNodeSO target))
                {
                    result.Warnings.Add($"노드 '{last.nodeId}'가 존재하지 않는 노드 '{targetId}'를 가리킵니다.");
                    return;
                }
                graph.Edges.Add(new EdgeData { BaseNodeGuid = block.Guid, TargetNodeGuid = target.Guid, OutputPortIndex = port });
            }

            if (last.requiresCrafting)
            {
                CraftingJobResult[] order = CraftingJobResultPorts.Order;
                for (int i = 0; i < order.Length; i++)
                    Add(ExactCraftingNext(last, order[i]), i);
                return;
            }

            if (last.choices.Count > 0)
            {
                for (int i = 0; i < last.choices.Count; i++)
                    Add(last.choices[i].nextNodeId, i);
                return;
            }

            int port = 0;
            foreach (NodeBranch b in last.branches) Add(b.nextNodeId, port++);
            Add(last.nextNodeId, port);
        }

        private static Dictionary<string, Rect> CollectPositions(NarrativeGraphSO layoutSource)
        {
            Dictionary<string, Rect> positions = new(StringComparer.OrdinalIgnoreCase);
            if (layoutSource == null) return positions;

            foreach (EpisodeNodeSO block in layoutSource.Nodes.OfType<EpisodeNodeSO>())
            {
                string firstId = NarrativeBlockModel.GetExecutionOrder(block)
                    .FirstOrDefault(NarrativeBlockModel.IsRuntimeEvent)?.RuntimeNodeId;
                if (!string.IsNullOrWhiteSpace(firstId))
                    positions[firstId] = block.Position;
            }
            return positions;
        }

        // 저장된 위치가 있는 블록은 그대로 두고, 나머지만 NarrativeGraphLayout 규칙(깊이 = 열)으로 놓는다.
        private static void LayoutBlocks(NarrativeGraphSO graph, Dictionary<string, Rect> savedPositions)
        {
            NarrativeGraphLayout.Arrange(graph, NarrativeGraphLayout.EstimateHeight, node =>
            {
                if (node is not EpisodeNodeSO block) return false;
                string firstId = block.Events.FirstOrDefault()?.RuntimeNodeId;
                if (string.IsNullOrEmpty(firstId) || !savedPositions.TryGetValue(firstId, out Rect saved)) return false;
                block.Position = saved;
                return true;
            });
        }
    }
}
