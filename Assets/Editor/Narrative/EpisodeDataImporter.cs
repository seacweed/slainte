using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NarrativeFlow.Editor
{
    public static class EpisodeDataImporter
    {
        [MenuItem("Narrative/Import EpisodeData to Graph")]
        public static void ImportSelected()
        {
            var source = Selection.activeObject as EpisodeData;
            if (source == null)
            {
                Debug.LogError("[EpisodeDataImporter] Please select an EpisodeData ScriptableObject.");
                return;
            }
            Import(source);
        }

        public static NarrativeGraphSO Import(EpisodeData source)
        {
            const string dir = "Assets/Narrative/Graphs";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = $"{dir}/{source.episodeId}.asset";

            NarrativeGraphSO graph = ScriptableObject.CreateInstance<NarrativeGraphSO>();
            graph.EpisodeId     = source.episodeId;
            graph.EpisodeTitle  = source.episodeTitle;
            graph.ChapterId     = source.chapterId;
            graph.EpisodeType   = source.episodeType;
            graph.MandatorySlot = source.mandatorySlot;
            graph.TriggerCondition  = source.triggerCondition;
            graph.PlayCondition     = source.playCondition;
            graph.SelectConditions  = source.selectConditions;
            graph.OpeningCharacters = (source.openingCharacters ?? new List<CharacterSlotEntry>())
                .Select(c => new CharacterSlotEntry { characterKey = c.characterKey, expressionKey = c.expressionKey, slotIndex = c.slotIndex })
                .ToList();

            AssetDatabase.CreateAsset(graph, path);

            // nodeId -> EpisodeNodeSO (for edge building)
            var nodeViews = new Dictionary<string, EpisodeNodeSO>();

            // BFS from firstNodeId so nodes are laid out roughly in execution order.
            var ordered = BfsOrder(source);

            const float colWidth  = 320f;
            const float rowHeight = 220f;
            const int   maxCols   = 5;
            int col = 0, row = 0;

            foreach (var rNode in ordered)
            {
                var ep = ScriptableObject.CreateInstance<EpisodeNodeSO>();
                ep.Guid     = System.Guid.NewGuid().ToString();
                ep.name     = ep.Guid;
                ep.Position = new Rect(col * colWidth, row * rowHeight, 200, 150);

                ep.CustomFields = new List<CustomNodeField>
                {
                    new CustomNodeField
                    {
                        FieldName  = "Title",
                        FieldValue = !string.IsNullOrEmpty(rNode.text) ? Truncate(rNode.text, 30) : rNode.nodeId
                    }
                };

                ep.Events           = new List<EpisodeEvent> { BuildEvent(rNode) };
                ep.OutgoingBranches = BuildBranches(rNode);

                AssetDatabase.AddObjectToAsset(ep, graph);
                graph.Nodes.Add(ep);
                nodeViews[rNode.nodeId] = ep;

                if (++col >= maxCols) { col = 0; row++; }
            }

            // Set start node.
            if (!string.IsNullOrEmpty(source.firstNodeId) && nodeViews.TryGetValue(source.firstNodeId, out var startEp))
                graph.StartNodeGuid = startEp.Guid;

            // Build edges.
            foreach (var rNode in ordered)
            {
                if (!nodeViews.TryGetValue(rNode.nodeId, out var srcEp)) continue;

                if (rNode.requiresCrafting)
                {
                    var order = CraftingJobResultPorts.Order;
                    for (int i = 0; i < order.Length; i++)
                        AddEdge(graph, srcEp, rNode.GetNextNodeId(order[i]), nodeViews, i);
                }
                else if (rNode.choices.Count > 0)
                {
                    for (int i = 0; i < rNode.choices.Count; i++)
                        AddEdge(graph, srcEp, rNode.choices[i].nextNodeId, nodeViews, i);
                }
                else
                {
                    // Flag/episode/var branches each get a port, nextNodeId gets the last port ("Next").
                    int port = 0;
                    foreach (var fb in rNode.flagBranches)
                        AddEdge(graph, srcEp, fb.nextNodeId, nodeViews, port++);
                    foreach (var eb in rNode.episodeBranches)
                        AddEdge(graph, srcEp, eb.nextNodeId, nodeViews, port++);
                    foreach (var vb in rNode.varBranches)
                        AddEdge(graph, srcEp, vb.nextNodeId, nodeViews, port++);
                    if (!string.IsNullOrEmpty(rNode.nextNodeId))
                        AddEdge(graph, srcEp, rNode.nextNodeId, nodeViews, port);
                }
            }

            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[EpisodeDataImporter] Imported '{source.episodeId}' → {path}  ({graph.Nodes.Count} nodes)");
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = graph;
            return graph;
        }

        // ── BFS traversal ─────────────────────────────────────────────────────────

        private static List<EpisodeNode> BfsOrder(EpisodeData data)
        {
            var result  = new List<EpisodeNode>();
            var visited = new HashSet<string>();
            var queue   = new Queue<string>();

            if (!string.IsNullOrEmpty(data.firstNodeId))
                queue.Enqueue(data.firstNodeId);

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (visited.Contains(id)) continue;
                visited.Add(id);

                var node = data.FindNode(id);
                if (node == null) continue;
                result.Add(node);

                Enqueue(queue, node.nextNodeId);
                foreach (var craftingResult in CraftingJobResultPorts.Order)
                    Enqueue(queue, node.GetNextNodeId(craftingResult));
                node.choices.ForEach(c => Enqueue(queue, c.nextNodeId));
                node.flagBranches.ForEach(b => Enqueue(queue, b.nextNodeId));
                node.episodeBranches.ForEach(b => Enqueue(queue, b.nextNodeId));
                node.varBranches.ForEach(b => Enqueue(queue, b.nextNodeId));
            }

            // Append any orphaned nodes not reachable from start.
            foreach (var node in data.nodes)
                if (!visited.Contains(node.nodeId))
                    result.Add(node);

            return result;
        }

        private static void Enqueue(Queue<string> q, string id)
        {
            if (!string.IsNullOrEmpty(id)) q.Enqueue(id);
        }

        // ── Event / branch builders ───────────────────────────────────────────────

        private static EpisodeEvent BuildEvent(EpisodeNode rNode)
        {
            var ev = new EpisodeEvent
            {
                Guid        = System.Guid.NewGuid().ToString(),
                BgmCommand  = rNode.bgmCommand,
                BgmClipName = rNode.bgmClipName
            };

            if (rNode.requiresCrafting)
            {
                ev.Type              = EpisodeEventType.BusinessStart;
                ev.CraftingTicketKey = rNode.craftingTicketKey;

                foreach (var result in CraftingJobResultPorts.Order)
                {
                    string flag = rNode.GetCraftingFlag(result);
                    var varChanges = rNode.GetCraftingVarChanges(result);
                    if (string.IsNullOrEmpty(flag) && varChanges.Count == 0) continue;

                    ev.SetCraftingFlag(result, flag);
                    ev.SetCraftingVarChanges(result, varChanges
                        .Select(v => new VarChangeData { VarName = v.varName, Delta = v.delta }).ToList());
                }
            }
            else if (rNode.choices.Count > 0)
            {
                ev.Type                = EpisodeEventType.Choice;
                ev.SpeakerKey          = rNode.speakerKey;
                ev.OverrideSpeakerName = rNode.overrideSpeakerName;
                ev.Text                = rNode.text;
                ev.Choices = rNode.choices.Select(c => new ChoiceOptionData
                {
                    ButtonText = c.buttonText,
                    SetFlags   = new List<string>(c.setFlags),
                    ClearFlags = new List<string>(c.clearFlags),
                    VarChanges = c.varChanges.Select(v => new VarChangeData { VarName = v.varName, Delta = v.delta }).ToList()
                }).ToList();
            }
            else
            {
                ev.Type               = EpisodeEventType.Dialogue;
                ev.SpeakerKey         = rNode.speakerKey;
                ev.OverrideSpeakerName = rNode.overrideSpeakerName;
                ev.Text               = rNode.text;
                ev.CharacterAppearances = rNode.characters.Select(c => new CharacterSlotEntryData
                {
                    CharacterKey  = c.characterKey,
                    ExpressionKey = c.expressionKey,
                    SlotIndex     = c.slotIndex
                }).ToList();
            }
            return ev;
        }

        private static List<string> BuildBranches(EpisodeNode rNode)
        {
            if (rNode.requiresCrafting)
                return CraftingJobResultPorts.Order.Select(CraftingJobResultPorts.Label).ToList();

            if (rNode.choices.Count > 0)
                return rNode.choices.Select(c => !string.IsNullOrEmpty(c.buttonText) ? c.buttonText : "Choice").ToList();

            var branches = new List<string>();
            foreach (var fb in rNode.flagBranches)
            {
                string key = fb.requiredAllFlags.Count > 0 ? fb.requiredAllFlags[0] : "flag";
                branches.Add($"{key} == true");
            }
            foreach (var eb in rNode.episodeBranches)
                branches.Add(eb.requiredCompletedEpisodeId); // bare episode id, e.g. "StrangeCoin_0"
            foreach (var vb in rNode.varBranches)
                branches.Add($"{vb.condition.varName} {CompareOpToString(vb.condition.op)} {vb.condition.threshold}");
            branches.Add("Next");
            return branches;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void AddEdge(NarrativeGraphSO graph, EpisodeNodeSO src, string targetNodeId, Dictionary<string, EpisodeNodeSO> nodeViews, int portIndex)
        {
            if (string.IsNullOrEmpty(targetNodeId) || !nodeViews.TryGetValue(targetNodeId, out var dest)) return;
            graph.Edges.Add(new EdgeData { BaseNodeGuid = src.Guid, TargetNodeGuid = dest.Guid, OutputPortIndex = portIndex });
        }

        private static string CompareOpToString(CompareOp op) => op switch
        {
            CompareOp.GreaterOrEqual => ">=",
            CompareOp.Greater        => ">",
            CompareOp.Equal          => "==",
            CompareOp.Less           => "<",
            CompareOp.LessOrEqual    => "<=",
            _                        => "=="
        };

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max - 3) + "...";
    }
}
