#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NarrativeFlow.Runtime;
using Slainte.Content;

namespace NarrativeFlow.Editor
{
    public class NarrativeDataBaker : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            BakeAllGraphs();
        }

        [MenuItem("Narrative/Bake All Graphs to JSON")]
        public static void BakeAllGraphs()
        {
            Debug.Log("[Narrative Baker] Optimizing ScriptableObject data for runtime...");

            string[] guids = AssetDatabase.FindAssets("t:NarrativeGraphSO");
            var baker = new NarrativeDataBaker();
            
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                NarrativeGraphSO graph = AssetDatabase.LoadAssetAtPath<NarrativeGraphSO>(path);
                
                if (graph != null)
                {
                    baker.BakeSingleGraph(graph);
                }
            }
            
            AssetDatabase.Refresh(); 
            Debug.Log("[Narrative Baker] All graphs processing and baking complete.");
        }

        [MenuItem("Assets/Bake This Graph to JSON", true)]
        public static bool ValidateBakeSelectedGraph()
        {
            return Selection.activeObject is NarrativeGraphSO;
        }

        [MenuItem("Assets/Bake This Graph to JSON")]
        public static void BakeSelectedGraph()
        {
            var graph = Selection.activeObject as NarrativeGraphSO;
            if (graph == null) return;

            var baker = new NarrativeDataBaker();
            baker.BakeSingleGraph(graph);

            AssetDatabase.Refresh();
            Debug.Log($"[Narrative Baker] Finished baking specific graph: {graph.name}");
        }

        private void BakeSingleGraph(NarrativeGraphSO graph)
        {
            string json = GenerateFlatJson(graph);
            
            string dirPath = Path.Combine(
                Application.streamingAssetsPath,
                ProjectStreamingAssetPaths.Narrative);
            if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
            
            string outPath = Path.Combine(dirPath, $"{graph.name}_Baked.json");
            File.WriteAllText(outPath, json);
            
            Debug.Log($"[Narrative Baker] Saved JSON to: {outPath}");
        }

        private string GenerateFlatJson(NarrativeGraphSO graph)
        {
            var incomingEdgesCount = new Dictionary<string, int>();
            foreach (var node in graph.Nodes)
            {
                if (node != null) incomingEdgesCount[node.Guid] = 0;
            }

            foreach (var edge in graph.Edges)
            {
                if (incomingEdgesCount.ContainsKey(edge.TargetNodeGuid))
                {
                    incomingEdgesCount[edge.TargetNodeGuid]++;
                }
            }

            var rootNodes = incomingEdgesCount.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key).ToList();
            var reachableGuids = new HashSet<string>();
            var queue = new Queue<string>(rootNodes);

            while (queue.Count > 0)
            {
                string currentGuid = queue.Dequeue();
                if (reachableGuids.Contains(currentGuid)) continue;

                reachableGuids.Add(currentGuid);

                var outgoingEdges = graph.Edges.Where(e => e.BaseNodeGuid == currentGuid);
                foreach (var edge in outgoingEdges)
                {
                    if (!reachableGuids.Contains(edge.TargetNodeGuid)) queue.Enqueue(edge.TargetNodeGuid);
                }
            }

            var validNodes = graph.Nodes.Where(n => n != null && (reachableGuids.Contains(n.Guid) || graph.Nodes.Count <= 1)).ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"Nodes\": [");

            for (int i = 0; i < validNodes.Count; i++)
            {
                var node = validNodes[i];
                if (!reachableGuids.Contains(node.Guid) && graph.Nodes.Count > 1)
                {
                    Debug.LogWarning($"[Narrative Tool] Isolated node excluded from build: {node.Guid} ({node.name})");
                    continue;
                }

                sb.AppendLine("    {");
                sb.AppendLine($"      \"NodeId\": \"{node.Guid}\",");
                sb.AppendLine($"      \"NodeType\": \"{node.GetType().Name}\",");

                if (node.CustomFields != null)
                {
                    foreach (var cf in node.CustomFields)
                    {
                        if (string.IsNullOrWhiteSpace(cf.FieldName)) continue;
                        string key = cf.FieldName.Trim();
                        if (key == "NodeId" || key == "NodeType" || key == "NextNodeIds") continue; // 예약어 보호

                        string value = cf.FieldValue != null ? cf.FieldValue : "";
                        // JSON 문자열 이스케이프 처리
                        value = value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                        sb.AppendLine($"      \"{key}\": \"{value}\",");
                    }
                }

                var outgoingEdges = graph.Edges.Where(e => e.BaseNodeGuid == node.Guid).OrderBy(e => e.OutputPortIndex).ToList();
                sb.Append("      \"NextNodeIds\": [");
                for (int j = 0; j < outgoingEdges.Count; j++)
                {
                    sb.Append($"\"{outgoingEdges[j].TargetNodeGuid}\"");
                    if (j < outgoingEdges.Count - 1) sb.Append(", ");
                }
                sb.AppendLine("]");

                sb.Append("    }");
                if (i < validNodes.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
#endif
