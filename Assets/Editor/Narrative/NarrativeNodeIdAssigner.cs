using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace NarrativeFlow.Editor
{
    public static class NarrativeNodeIdAssigner
    {
        public static void RegenerateIds(NarrativeGraphSO graph)
        {
            if (graph == null) return;

            var episodeNodeSet = new HashSet<string>(
                graph.Nodes.OfType<EpisodeNodeSO>().Select(n => n.Guid));
            var nodeByGuid = graph.Nodes.OfType<EpisodeNodeSO>().ToDictionary(n => n.Guid);

            // Build adjacency (EpisodeNodeSO → EpisodeNodeSO only)
            var outgoing      = nodeByGuid.Keys.ToDictionary(g => g, _ => new Dictionary<int, string>());
            var incomingCount = nodeByGuid.Keys.ToDictionary(g => g, _ => 0);

            foreach (var edge in graph.Edges)
            {
                if (!episodeNodeSet.Contains(edge.BaseNodeGuid) || !episodeNodeSet.Contains(edge.TargetNodeGuid))
                    continue;
                outgoing[edge.BaseNodeGuid][edge.OutputPortIndex] = edge.TargetNodeGuid;
                incomingCount[edge.TargetNodeGuid]++;
            }

            // BFS: (guid, pathId)
            var queue = new Queue<(string guid, string pathId)>();
            var assigned = new HashSet<string>();
            // merge node guid → list of arriving pathIds
            var mergeArrivals = new Dictionary<string, List<string>>();

            if (!string.IsNullOrEmpty(graph.StartNodeGuid) && episodeNodeSet.Contains(graph.StartNodeGuid))
                queue.Enqueue((graph.StartNodeGuid, "1"));

            while (queue.Count > 0)
            {
                var (guid, pathId) = queue.Dequeue();

                // Merge point: wait until all incoming branches arrive
                if (incomingCount.TryGetValue(guid, out int inc) && inc > 1)
                {
                    if (!mergeArrivals.ContainsKey(guid))
                        mergeArrivals[guid] = new List<string>();
                    mergeArrivals[guid].Add(pathId);

                    if (mergeArrivals[guid].Count < inc)
                        continue;

                    // All branches arrived. Strip the last 2 underscore-segments from
                    // any arriving pathId to recover the split-root ID, then increment.
                    // e.g. "11_1_2" and "11_2_4" → strip "_1_2" / "_2_4" → "11" → "12"
                    pathId = ResolveMergeId(mergeArrivals[guid][0]);
                }

                if (assigned.Contains(guid)) continue;
                assigned.Add(guid);

                if (!nodeByGuid.TryGetValue(guid, out var node)) continue;
                SetTitle(node, pathId);

                if (!outgoing.TryGetValue(guid, out var ports) || ports.Count == 0) continue;

                var connected = ports.OrderBy(kv => kv.Key).ToList();
                if (connected.Count == 1)
                {
                    queue.Enqueue((connected[0].Value, IncrementSuffix(pathId)));
                }
                else
                {
                    foreach (var kv in connected)
                    {
                        int branchIdx = kv.Key + 1; // 1-based
                        queue.Enqueue((kv.Value, $"{pathId}_{branchIdx}_1"));
                    }
                }
            }

            // Orphaned (unreachable from start)
            int orphanIdx = 1;
            foreach (var node in graph.Nodes.OfType<EpisodeNodeSO>())
                if (!assigned.Contains(node.Guid))
                    SetTitle(node, $"x{orphanIdx++}");

            EditorUtility.SetDirty(graph);
        }

        // Strip the 2 rightmost underscore-segments and increment what remains.
        // "11_1_2" → "11" → "12"
        // "3_1_2_1_3" → "3_1_2" → "3_1_3"
        private static string ResolveMergeId(string pathId)
        {
            int i1 = pathId.LastIndexOf('_');
            if (i1 <= 0) return IncrementSuffix(pathId);
            int i2 = pathId.LastIndexOf('_', i1 - 1);
            if (i2 < 0) return IncrementSuffix(pathId.Substring(0, i1));
            return IncrementSuffix(pathId.Substring(0, i2));
        }

        // "1"→"2", "11"→"12", "3_1_2"→"3_1_3"
        private static string IncrementSuffix(string id)
        {
            int last = id.LastIndexOf('_');
            if (last < 0)
                return int.TryParse(id, out int n) ? (n + 1).ToString() : id + "_1";
            string prefix = id.Substring(0, last);
            string suffix = id.Substring(last + 1);
            return int.TryParse(suffix, out int num) ? $"{prefix}_{num + 1}" : $"{id}_1";
        }

        private static void SetTitle(EpisodeNodeSO node, string title)
        {
            Undo.RecordObject(node, "Auto ID");
            if (node.CustomFields == null) node.CustomFields = new List<CustomNodeField>();
            var existing = node.CustomFields.Find(f =>
                string.Equals(f.FieldName, "Title", System.StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                existing.FieldValue = title;
            else
                node.CustomFields.Insert(0, new CustomNodeField { FieldName = "Title", FieldValue = title });
            EditorUtility.SetDirty(node);
        }
    }
}
