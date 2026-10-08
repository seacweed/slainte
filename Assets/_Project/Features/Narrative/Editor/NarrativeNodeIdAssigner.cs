using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace NarrativeFlow.Editor
{
    // 런타임 노드(대사·선택지·제조 이벤트, 라우터) 단위로 nodeId를 그래프 흐름에 맞춰 자동으로 다시 매긴다.
    // 기존 CSV 대부분이 쓰던 "다음 번호" 규칙:
    //   직선      0 → 1 → 2            (시작 번호는 현재 시작 노드가 정수면 그 값, 아니면 1)
    //   분기      20에서 갈라지면(포트가 2개 이상) → 21_1_1, 21_2_1 → 21_1_2 …   (갈래 번호 = 포트 순서)
    //   합류      21_1_k 와 21_2_j 가 만나면 → 22   (오른쪽 두 마디를 떼고 +1)
    //   갈래 속 분기  21_1_3 에서 갈라지면 → 21_1_4_1_1 … → 합류 21_1_5
    //   여러 갈래의 분기가 같은 대상들로 이어지면(예: 갈래마다 있는 제조 노드의 결과 공유)
    //             그 갈래들의 합류 번호에서 갈라진 것으로 본다 → 22_1_1, 22_2_1 … → 합류 23
    //   부분 합류  20의 갈래 일부끼리 먼저 만나고(1·2, 3·4) 나중에 모두 만나면
    //             먼저 만난 곳들을 새 갈래로 본다 → 22_1_1, 22_2_1 … → 모두 만나는 곳 22
    //   미도달    x1, x2 …
    // 흐름은 그래프를 실제로 컴파일한 결과(EpisodeData)에서 읽는다 — Trigger 인라인·빈 블록 통과까지
    // 컴파일러와 똑같이 해석해야 화면의 ID와 CSV의 ID가 어긋나지 않기 때문이다.
    public static class NarrativeNodeIdAssigner
    {
        // 그래프의 모든 런타임 노드 ID를 다시 매기고 블록 제목을 첫 노드 ID로 맞춘다. 바뀐 ID 수를 돌려준다.
        public static int RegenerateIds(NarrativeGraphSO graph)
        {
            if (graph == null) return 0;

            // Build가 ID 없는 이벤트에 임시 ID를 부여하므로, 이후 매핑은 항상 기존 ID → 새 ID로 할 수 있다.
            EpisodeData data = EpisodeDataCompiler.Build(graph, new CompileReport());
            Dictionary<string, string> map = ComputeIds(data);
            UnityEngine.Object.DestroyImmediate(data);

            int changed = 0;
            foreach (EpisodeNodeSO block in graph.Nodes.OfType<EpisodeNodeSO>())
            {
                bool dirty = false;
                foreach (EpisodeEvent ev in block.Events.Where(NarrativeBlockModel.IsRuntimeEvent))
                    dirty |= Remap(ev.RuntimeNodeId, map, id => ev.RuntimeNodeId = id, ref changed);
                if (!string.IsNullOrWhiteSpace(block.RouterNodeId))
                    dirty |= Remap(block.RouterNodeId, map, id => block.RouterNodeId = id, ref changed);
                dirty |= SyncTitle(block);
                if (dirty) EditorUtility.SetDirty(block);
            }

            foreach (TriggerNodeSO trigger in graph.Nodes.OfType<TriggerNodeSO>())
            {
                if (!string.IsNullOrWhiteSpace(trigger.RuntimeNodeId)
                    && Remap(trigger.RuntimeNodeId, map, id => trigger.RuntimeNodeId = id, ref changed))
                    EditorUtility.SetDirty(trigger);
            }

            // 그래프를 열 때마다 불리므로, 바뀐 것이 없으면 에셋을 더럽히지 않는다.
            if (changed > 0) EditorUtility.SetDirty(graph);
            return changed;
        }

        // 블록 제목 = 첫 런타임 노드 ID(없으면 라우터 ID). 카드 제목과 중복 검사에 쓰인다.
        public static bool SyncTitle(EpisodeNodeSO block)
        {
            string firstId = NarrativeBlockModel.GetExecutionOrder(block)
                .FirstOrDefault(NarrativeBlockModel.IsRuntimeEvent)?.RuntimeNodeId;
            string title = !string.IsNullOrWhiteSpace(firstId) ? firstId
                : !string.IsNullOrWhiteSpace(block.RouterNodeId) ? block.RouterNodeId
                : "(빈 블록)";

            block.CustomFields ??= new List<CustomNodeField>();
            CustomNodeField field = block.CustomFields.Find(f =>
                string.Equals(f.FieldName, "Title", StringComparison.OrdinalIgnoreCase));
            if (field == null)
            {
                block.CustomFields.Insert(0, new CustomNodeField { FieldName = "Title", FieldValue = title });
                return true;
            }
            if (field.FieldValue == title) return false;
            field.FieldValue = title;
            return true;
        }

        private static bool Remap(string current, Dictionary<string, string> map, Action<string> set, ref int changed)
        {
            if (string.IsNullOrWhiteSpace(current) || !map.TryGetValue(current.Trim(), out string next) || next == current)
                return false;
            set(next);
            changed++;
            return true;
        }

        // 기존 ID → 새 ID. EpisodeData만 보는 순수 계산이라 검증에서도 쓸 수 있다.
        // 흐름 순서(위상 정렬)대로 돌기 때문에 노드를 볼 때는 앞서 오는 노드의 새 ID가 모두 정해져 있다.
        public static Dictionary<string, string> ComputeIds(EpisodeData data)
        {
            Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
            EpisodeGraphTraversal.Flow flow = EpisodeGraphTraversal.BuildFlow(data);
            HashSet<string> usedLabels = new(StringComparer.OrdinalIgnoreCase);

            void Assign(string id, string label)
            {
                // 규칙상 충돌은 생기지 않지만, 손으로 꼬아 놓은 연결 때문에 겹치면 안전하게 접미사를 붙인다.
                string unique = label;
                for (int n = 2; !usedLabels.Add(unique); n++)
                    unique = $"{label}~{n}";
                result[id] = unique;
            }

            // 합류 번호 → 그 번호로 합류해야 할 분기 노드. 합류 후보가 그 분기의 일부 갈래만 모은 것인지 판정할 때 쓴다.
            Dictionary<string, string> splitByMerge = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> partialGroups = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, HashSet<string>> reachCache = new(StringComparer.OrdinalIgnoreCase);

            HashSet<string> Reach(string id)
            {
                if (reachCache.TryGetValue(id, out HashSet<string> cached)) return cached;
                HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
                Stack<string> stack = new();
                stack.Push(id);
                while (stack.Count > 0)
                {
                    string x = stack.Pop();
                    if (!seen.Add(x)) continue;
                    if (flow.Forward.TryGetValue(x, out List<string> next))
                        foreach (string n in next) stack.Push(n);
                }
                reachCache[id] = seen;
                return seen;
            }

            // 분기 split의 어떤 갈래가 merge를 거치지 않고, merge 뒤에서 다시 만난다면 merge는 일부 갈래만의 합류다.
            // 아예 끝나 버리는 갈래는 따지지 않는다(그 갈래를 뺀 나머지가 모두 만나면 완전한 합류).
            bool IsPartialMerge(string split, string merge)
            {
                if (!flow.Forward.TryGetValue(split, out List<string> branches)) return false;
                HashSet<string> after = Reach(merge);
                foreach (string branch in branches)
                {
                    HashSet<string> reach = Reach(branch);
                    if (!reach.Contains(merge) && reach.Overlaps(after)) return true;
                }
                return false;
            }

            void AssignAndRegister(EpisodeNode node, string label)
            {
                Assign(node.nodeId, label);
                // 포트가 2개 이상인 노드 L의 갈래는 inc(L)_k_j, 모두 만나는 곳은 inc(inc(L)).
                if (EpisodeGraphTraversal.Ports(node).Count >= 2)
                    splitByMerge[IncrementSuffix(IncrementSuffix(result[node.nodeId]))] = node.nodeId;
            }

            foreach (EpisodeNode node in flow.Order)
            {
                if (result.ContainsKey(node.nodeId)) continue;
                if (!flow.Incoming.TryGetValue(node.nodeId, out var edges) || edges.Count == 0)
                {
                    // 진입이 없는 흐름 노드는 시작 노드뿐이다.
                    AssignAndRegister(node, int.TryParse(data.firstNodeId, out int start) && start >= 0
                        ? start.ToString()
                        : "1");
                    continue;
                }

                if (edges.Count == 1)
                {
                    AssignAndRegister(node, Propose(edges[0], result));
                    continue;
                }

                // 모든 진입이 서로 다른 분기 노드의 같은 포트라면, 그 분기 노드들은 결과를 공유하는
                // 한 덩어리의 분기다: 분기 노드들의 합류 번호를 분기 지점으로 삼아 _포트_1을 붙인다.
                bool sharedBranch = edges.All(e => e.FromBranch && e.Port == edges[0].Port);
                if (sharedBranch)
                {
                    AssignAndRegister(node,
                        $"{MergeProposals(edges.Select(e => IncrementSuffix(result[e.From])))}_{edges[0].Port + 1}_1");
                    continue;
                }

                string merged = MergeProposals(edges.Select(e => Propose(e, result)));
                if (splitByMerge.TryGetValue(merged, out string split) && IsPartialMerge(split, node.nodeId))
                {
                    // 일부 갈래만 먼저 만난 곳: 같은 분기의 부분 합류끼리 새 갈래 번호(1, 2 …)를 받고,
                    // 나중에 모두 만나는 곳은 그다음 번호(merged + 1)가 된다.
                    partialGroups.TryGetValue(split, out int group);
                    partialGroups[split] = ++group;
                    splitByMerge[IncrementSuffix(merged)] = split;
                    AssignAndRegister(node, $"{merged}_{group}_1");
                    continue;
                }

                AssignAndRegister(node, merged);
            }

            int orphan = 1;
            foreach (EpisodeNode node in data.nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.nodeId) || result.ContainsKey(node.nodeId)) continue;
                string label;
                do label = $"x{orphan++}"; while (!usedLabels.Add(label));
                result[node.nodeId] = label;
            }

            return result;
        }

        // 연결 하나가 제안하는 ID: 직선이면 +1, 분기면 다음 번호에 _포트_1. ("20" 분기 포트 2 → "21_2_1")
        // 분기의 형제 갈래가 같은 결과를 공유하는 경우(위의 sharedBranch)는 호출 쪽에서 따로 처리한다.
        private static string Propose(EpisodeGraphTraversal.ForwardEdge edge, Dictionary<string, string> assigned)
        {
            string next = IncrementSuffix(assigned[edge.From]);
            return edge.FromBranch ? $"{next}_{edge.Port + 1}_1" : next;
        }

        // 여러 제안이 만나는 합류 ID: 가장 깊은 제안부터 오른쪽 두 마디를 떼고 +1 하기를 모두 같아질 때까지 반복.
        // "21_1_4" + "21_2_3" → "22",  "21_1_4_1_2" + "21_1_4_2_1" → "21_1_5"
        // 깊이가 같아도 서로 다른 최상위 번호(손으로 꼰 연결)면 더 큰 번호를 쓴다 — 합류는 양쪽보다 뒤여야 하므로.
        private static string MergeProposals(IEnumerable<string> proposals)
        {
            List<string> ids = proposals.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            while (ids.Count > 1)
            {
                int depth = ids.Max(Depth);
                if (depth <= 1)
                    return ids.OrderByDescending(id => int.TryParse(id, out int n) ? n : int.MinValue).First();
                ids = ids.Select(id => Depth(id) == depth ? StepUp(id) : id)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            return ids[0];
        }

        // 한 단계 위 번호. 숫자가 아닌 마디("5~2")는 +1이 마디를 늘리므로 그때는 떼기만 한다 —
        // 깊이가 반드시 줄어야 MergeProposals가 끝난다(예전에는 여기서 무한 루프가 났다).
        private static string StepUp(string id)
        {
            string stripped = StripBranch(id);
            string next = IncrementSuffix(stripped);
            return Depth(next) < Depth(id) ? next : stripped;
        }

        private static int Depth(string id) => id.Count(c => c == '_') + 1;

        // 오른쪽 두 마디(갈래 번호·갈래 안 순번)를 뗀다: "21_1_4" → "21", "3_1_2_1_3" → "3_1_2"
        private static string StripBranch(string id)
        {
            int i1 = id.LastIndexOf('_');
            if (i1 <= 0) return id;
            int i2 = id.LastIndexOf('_', i1 - 1);
            return id.Substring(0, i2 < 0 ? i1 : i2);
        }

        // "1"→"2", "3_1_2"→"3_1_3"
        private static string IncrementSuffix(string id)
        {
            int last = id.LastIndexOf('_');
            if (last < 0)
                return int.TryParse(id, out int n) ? (n + 1).ToString() : id + "_1";
            string prefix = id.Substring(0, last);
            string suffix = id.Substring(last + 1);
            return int.TryParse(suffix, out int num) ? $"{prefix}_{num + 1}" : $"{id}_1";
        }
    }
}
