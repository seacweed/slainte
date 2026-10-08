using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NarrativeFlow.Editor
{
    // 그래프 자동 배치: 시작 블록으로부터의 깊이를 열로, 같은 열 안에서는 발견 순서(포트 순서)대로 위에서
    // 아래로 쌓는다. CSV에서 그래프를 만들 때(임포터)와 툴바의 "자동 정렬"이 같은 규칙을 쓴다.
    // WHY: 열 간격이 카드 폭과 거의 같으면 열 사이 간선이 한 줄로 겹쳐 어느 포트에서 나온 선인지 구분이 안 된다.
    public static class NarrativeGraphLayout
    {
        public const float CardWidth = 340f;
        public const float ColumnGap = 240f;
        public const float RowGap = 60f;
        private const float ColumnPitch = CardWidth + ColumnGap;

        // keep이 참인 노드는 위치를 건드리지 않는다(임포트 시 기존 그래프에서 위치를 이어받은 블록).
        public static void Arrange(NarrativeGraphSO graph, Func<NodeDataSO, float> heightOf, Func<NodeDataSO, bool> keep = null)
        {
            List<NodeDataSO> nodes = graph.Nodes.Where(n => n != null && !string.IsNullOrEmpty(n.Guid)).ToList();
            Dictionary<string, NodeDataSO> byGuid = nodes.GroupBy(n => n.Guid).ToDictionary(g => g.Key, g => g.First());
            ILookup<string, string> successors = graph.Edges
                .OrderBy(e => e.OutputPortIndex)
                .ToLookup(e => e.BaseNodeGuid, e => e.TargetNodeGuid);

            // BFS 깊이 = 열, BFS 발견 순서 = 열 안의 순번(분기 갈래가 포트 순서대로 위에서 아래로 놓인다).
            Dictionary<string, int> depth = new();
            Dictionary<string, int> discovered = new();
            Queue<string> queue = new();
            if (!string.IsNullOrEmpty(graph.StartNodeGuid) && byGuid.ContainsKey(graph.StartNodeGuid))
            {
                depth[graph.StartNodeGuid] = 0;
                discovered[graph.StartNodeGuid] = 0;
                queue.Enqueue(graph.StartNodeGuid);
            }
            while (queue.Count > 0)
            {
                string guid = queue.Dequeue();
                foreach (string next in successors[guid])
                {
                    if (!byGuid.ContainsKey(next) || depth.ContainsKey(next)) continue;
                    depth[next] = depth[guid] + 1;
                    discovered[next] = discovered.Count;
                    queue.Enqueue(next);
                }
            }

            int unreachableColumn = depth.Count > 0 ? depth.Values.Max() + 1 : 0;
            Dictionary<int, float> nextY = new();
            IEnumerable<NodeDataSO> ordered = nodes
                .OrderBy(n => discovered.TryGetValue(n.Guid, out int order) ? order : int.MaxValue);
            foreach (NodeDataSO node in ordered)
            {
                if (keep != null && keep(node)) continue;

                int column = depth.TryGetValue(node.Guid, out int d) ? d : unreachableColumn;
                nextY.TryGetValue(column, out float y);
                float height = Mathf.Max(80f, heightOf(node));
                node.Position = new Rect(column * ColumnPitch, y, CardWidth, height);
                nextY[column] = y + height + RowGap;
            }
        }

        // 화면에 그려지기 전(임포트) 카드 높이 추정. 접힌 블록은 앞 4줄만 보인다.
        public static float EstimateHeight(NodeDataSO node)
        {
            switch (node)
            {
                case EpisodeNodeSO block:
                    int rows = block.ShowAllEvents ? block.Events.Count : Mathf.Min(4, block.Events.Count);
                    int ports = Mathf.Max(1, block.OutgoingBranches?.Count ?? 1);
                    return 90f + rows * 70f + ports * 24f;
                case TriggerNodeSO trigger:
                    return 110f + (trigger.Conditions?.Count ?? 0) * 26f;
                default:
                    return 160f;
            }
        }
    }
}
