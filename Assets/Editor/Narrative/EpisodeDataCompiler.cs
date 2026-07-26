using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NarrativeFlow.Editor
{
    public class EpisodeDataCompiler
    {
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

        public void Compile(NarrativeGraphSO graph)
        {
            string episodeId    = !string.IsNullOrEmpty(graph.EpisodeId)    ? graph.EpisodeId    : graph.name;
            string episodeTitle = !string.IsNullOrEmpty(graph.EpisodeTitle) ? graph.EpisodeTitle : episodeId;

            EpisodeData data = ScriptableObject.CreateInstance<EpisodeData>();
            data.episodeId    = episodeId;
            data.episodeTitle = episodeTitle;
            data.triggerCondition  = graph.TriggerCondition ?? new EpisodeTriggerCondition();
            data.openingCharacters = (graph.OpeningCharacters ?? new List<CharacterSlotEntry>())
                .Select(c => new CharacterSlotEntry { characterKey = c.characterKey, expressionKey = c.expressionKey, slotIndex = c.slotIndex })
                .ToList();

            // First pass: build runtime EpisodeNodes from each EpisodeNodeSO's event list.
            // nodeMapping: blockGuid -> ordered list of runtime nodeIds (one per event)
            var nodeMapping = new Dictionary<string, List<string>>();
            foreach (var gNode in graph.Nodes.OfType<EpisodeNodeSO>())
            {
                var ids = new List<string>();
                if (gNode.Events.Count == 0)
                {
                    string rid = $"{gNode.Guid}_0";
                    ids.Add(rid);
                    data.nodes.Add(new EpisodeNode { nodeId = rid });
                }
                else
                {
                    for (int i = 0; i < gNode.Events.Count; i++)
                    {
                        string rid = $"{gNode.Guid}_{i}";
                        ids.Add(rid);
                        data.nodes.Add(BuildRuntimeNode(rid, gNode.Events[i]));
                    }
                }
                nodeMapping[gNode.Guid] = ids;
            }

            // Resolve start node: prefer explicit StartNodeGuid, fallback to node with no incoming edges.
            if (!string.IsNullOrEmpty(graph.StartNodeGuid) && nodeMapping.TryGetValue(graph.StartNodeGuid, out var startIds))
            {
                data.firstNodeId = startIds[0];
            }
            else
            {
                var incoming = new HashSet<string>(graph.Edges.Select(e => e.TargetNodeGuid));
                string fallbackGuid = graph.Nodes.OfType<EpisodeNodeSO>()
                    .Where(n => !incoming.Contains(n.Guid))
                    .Select(n => n.Guid)
                    .FirstOrDefault();
                if (fallbackGuid != null && nodeMapping.TryGetValue(fallbackGuid, out var fb))
                    data.firstNodeId = fb[0];
            }

            // Second pass: link runtime nodes.
            foreach (var gNode in graph.Nodes.OfType<EpisodeNodeSO>())
            {
                var rIds      = nodeMapping[gNode.Guid];
                var blockEdges = graph.Edges
                    .Where(e => e.BaseNodeGuid == gNode.Guid)
                    .OrderBy(e => e.OutputPortIndex)
                    .ToList();

                for (int i = 0; i < gNode.Events.Count; i++)
                {
                    var ev    = gNode.Events[i];
                    var rNode = data.FindNode(rIds[i]);
                    bool isLast = i == gNode.Events.Count - 1;

                    if (!isLast)
                    {
                        rNode.nextNodeId = rIds[i + 1];
                    }
                    else
                    {
                        LinkLastEvent(rNode, ev, gNode, blockEdges, graph, nodeMapping);
                    }
                }
            }

            // Save (overwrite existing asset if present).
            const string dir = "Assets/Resources/EpisodeData";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = $"{dir}/EpisodeData_{episodeId}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<EpisodeData>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(data, existing);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(data, path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[EpisodeDataCompiler] Compiled '{episodeId}' → {path}  ({data.nodes.Count} nodes)");

            ExportToCsv(data, episodeId);
        }

        // ── Node builder ──────────────────────────────────────────────────────────

        private static EpisodeNode BuildRuntimeNode(string nodeId, EpisodeEvent ev)
        {
            var n = new EpisodeNode
            {
                nodeId      = nodeId,
                bgmCommand  = ev.BgmCommand,
                bgmClipName = ev.BgmClipName
            };

            switch (ev.Type)
            {
                case EpisodeEventType.Dialogue:
                    n.speakerKey          = ev.SpeakerKey;
                    n.overrideSpeakerName = ev.OverrideSpeakerName;
                    n.text                = ev.Text;
                    n.characters          = ev.CharacterAppearances
                        .Select(c => new CharacterSlotEntry { characterKey = c.CharacterKey, expressionKey = c.ExpressionKey, slotIndex = c.SlotIndex })
                        .ToList();
                    break;

                case EpisodeEventType.BusinessStart:
                    n.requiresCrafting       = true;
                    n.craftingTicketKey      = ev.CraftingTicketKey;
                    n.craftingRecipeId       = ev.CraftingRecipeId;
                    n.craftingFlagGood       = ev.CraftingFlagGood;
                    n.craftingFlagBad        = ev.CraftingFlagBad;
                    n.craftingVarChangesGood = ev.CraftingVarChangesGood
                        .Select(v => new VarChange { varName = v.VarName, delta = v.Delta }).ToList();
                    n.craftingVarChangesBad  = ev.CraftingVarChangesBad
                        .Select(v => new VarChange { varName = v.VarName, delta = v.Delta }).ToList();
                    break;

                case EpisodeEventType.Choice:
                    n.choices = ev.Choices
                        .Select(c => new EpisodeChoice
                        {
                            buttonText = c.ButtonText,
                            setFlags   = new List<string>(c.SetFlags),
                            clearFlags = new List<string>(c.ClearFlags),
                            varChanges = c.VarChanges
                                .Select(v => new VarChange { varName = v.VarName, delta = v.Delta })
                                .ToList()
                        }).ToList();
                    break;
            }
            return n;
        }

        // ── Link last event → outer graph edges ───────────────────────────────────

        private void LinkLastEvent(
            EpisodeNode rNode,
            EpisodeEvent ev,
            EpisodeNodeSO gNode,
            List<EdgeData> blockEdges,
            NarrativeGraphSO graph,
            Dictionary<string, List<string>> nodeMapping)
        {
            if (ev.Type == EpisodeEventType.Choice)
            {
                for (int j = 0; j < ev.Choices.Count; j++)
                {
                    if (j >= rNode.choices.Count) break;
                    var edge = blockEdges.FirstOrDefault(e => e.OutputPortIndex == j);
                    if (!string.IsNullOrEmpty(edge.BaseNodeGuid))
                        rNode.choices[j].nextNodeId = ResolveNodeId(graph, edge.TargetNodeGuid, nodeMapping, rNode);
                }
                return;
            }

            if (ev.Type == EpisodeEventType.BusinessStart || rNode.requiresCrafting)
            {
                var good = blockEdges.FirstOrDefault(e => e.OutputPortIndex == 0);
                var bad  = blockEdges.FirstOrDefault(e => e.OutputPortIndex == 1);
                if (!string.IsNullOrEmpty(good.BaseNodeGuid))
                    rNode.nextNodeIdGood = ResolveNodeId(graph, good.TargetNodeGuid, nodeMapping, rNode);
                if (!string.IsNullOrEmpty(bad.BaseNodeGuid))
                    rNode.nextNodeIdBad  = ResolveNodeId(graph, bad.TargetNodeGuid,  nodeMapping, rNode);
                return;
            }

            // Regular branches: OutgoingBranches[j] labels encode conditions or "Next"/"Default".
            for (int j = 0; j < blockEdges.Count; j++)
            {
                string label    = j < gNode.OutgoingBranches.Count ? gNode.OutgoingBranches[j] : "";
                string targetId = ResolveNodeId(graph, blockEdges[j].TargetNodeGuid, nodeMapping, rNode);
                if (targetId == null) continue; // TriggerNode handled inline

                if (TryParseCondition(label, out var cond))
                {
                    if (cond.Type == TriggerConditionType.Flag)
                    {
                        rNode.flagBranches.Add(new NodeFlagBranch
                            { requiredAllFlags = new List<string> { cond.Key }, nextNodeId = targetId });
                    }
                    else if (TryParseCompareOp(cond.Operator, out var op) && int.TryParse(cond.Value, out int thr))
                    {
                        rNode.varBranches.Add(new NodeVarBranch
                            { condition = new VarCondition { varName = cond.Key, op = op, threshold = thr }, nextNodeId = targetId });
                    }
                }
                else if (rNode.nextNodeId == null)
                {
                    rNode.nextNodeId = targetId;
                }
            }
        }

        // ── TriggerNode resolution ────────────────────────────────────────────────

        private string ResolveNodeId(
            NarrativeGraphSO graph,
            string targetGuid,
            Dictionary<string, List<string>> nodeMapping,
            EpisodeNode currentNode)
        {
            if (string.IsNullOrEmpty(targetGuid)) return null;

            var target = graph.Nodes.Find(n => n.Guid == targetGuid);
            if (target is EpisodeNodeSO && nodeMapping.TryGetValue(targetGuid, out var ids))
                return ids[0];

            if (target is TriggerNodeSO trigger)
            {
                InjectTriggerLogic(currentNode, trigger, graph, nodeMapping);
                return null; // branches injected inline; no direct "next"
            }
            return null;
        }

        private void InjectTriggerLogic(
            EpisodeNode rNode,
            TriggerNodeSO triggerNode,
            NarrativeGraphSO graph,
            Dictionary<string, List<string>> nodeMapping)
        {
            var triggerEdges = graph.Edges
                .Where(e => e.BaseNodeGuid == triggerNode.Guid)
                .OrderBy(e => e.OutputPortIndex)
                .ToList();

            for (int i = 0; i < triggerNode.Conditions.Count; i++)
            {
                var cond = triggerNode.Conditions[i];
                var edge = triggerEdges.FirstOrDefault(e => e.OutputPortIndex == i);
                if (string.IsNullOrEmpty(edge.BaseNodeGuid)) continue;

                string targetId = ResolveNodeId(graph, edge.TargetNodeGuid, nodeMapping, rNode);
                if (targetId == null) continue;

                if (cond.Type == TriggerConditionType.Flag)
                {
                    rNode.flagBranches.Add(new NodeFlagBranch
                        { requiredAllFlags = new List<string> { cond.Key }, nextNodeId = targetId });
                }
                else if (TryParseCompareOp(cond.Operator, out var op) && int.TryParse(cond.Value, out int thr))
                {
                    rNode.varBranches.Add(new NodeVarBranch
                        { condition = new VarCondition { varName = cond.Key, op = op, threshold = thr }, nextNodeId = targetId });
                }
            }

            // Else port (index == Conditions.Count)
            var elseEdge = triggerEdges.FirstOrDefault(e => e.OutputPortIndex == triggerNode.Conditions.Count);
            if (!string.IsNullOrEmpty(elseEdge.BaseNodeGuid))
            {
                string elseId = ResolveNodeId(graph, elseEdge.TargetNodeGuid, nodeMapping, rNode);
                if (elseId != null) rNode.nextNodeId = elseId;
            }
        }

        // ── Condition parsing ─────────────────────────────────────────────────────

        private static bool TryParseCondition(string input, out GraphTriggerCondition cond)
        {
            cond = new GraphTriggerCondition();
            if (string.IsNullOrWhiteSpace(input)) return false;

            string[] parts = input.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;

            cond.Key      = parts[0];
            cond.Operator = parts[1];
            cond.Value    = parts[2];

            string val = cond.Value.ToLower();
            if (val == "true" || val == "false") { cond.Type = TriggerConditionType.Flag;     return true; }
            if (int.TryParse(cond.Value, out _)) { cond.Type = TriggerConditionType.Variable; return true; }
            return false;
        }

        private static bool TryParseCompareOp(string op, out CompareOp result)
        {
            switch (op)
            {
                case ">=": result = CompareOp.GreaterOrEqual; return true;
                case ">":  result = CompareOp.Greater;        return true;
                case "==": result = CompareOp.Equal;          return true;
                case "<":  result = CompareOp.Less;           return true;
                case "<=": result = CompareOp.LessOrEqual;    return true;
                default:   result = CompareOp.Equal;          return false;
            }
        }

        // ── CSV export ────────────────────────────────────────────────────────────
        // Column order matches EpisodeCsvImporter.ParseNodes exactly.

        private static void ExportToCsv(EpisodeData data, string id)
        {
            var sb = new StringBuilder();

            sb.AppendLine("#META");
            sb.AppendLine("episodeId,episodeTitle,firstNodeId");
            sb.AppendLine($"{data.episodeId},{data.episodeTitle},{data.firstNodeId}");
            sb.AppendLine();

            if (data.triggerCondition != null)
            {
                var tc = data.triggerCondition;
                sb.AppendLine("#TRIGGER");
                sb.AppendLine("minDay,requiredFlags,blockedFlags,prerequisiteEpisodeIds,requiredVars");
                string reqVars = string.Join("|", tc.requiredVars.Select(v => $"{v.varName}{CompareOpToString(v.op)}{v.threshold}"));
                sb.AppendLine($"{tc.minDay},{string.Join("|", tc.requiredFlags)},{string.Join("|", tc.blockedFlags)},{string.Join("|", tc.prerequisiteEpisodeIds)},{reqVars}");
                sb.AppendLine();
            }

            if (data.openingCharacters != null && data.openingCharacters.Count > 0)
            {
                sb.AppendLine("#OPENING_CHARS");
                sb.AppendLine("characterKey,expressionKey,slotIndex");
                foreach (var c in data.openingCharacters)
                    sb.AppendLine($"{c.characterKey},{c.expressionKey},{c.slotIndex}");
                sb.AppendLine();
            }

            sb.AppendLine("#NODES");
            sb.AppendLine("nodeId,speakerKey,overrideSpeakerName,text,nextNodeId,requiresCrafting,craftingTicketKey,nextNodeIdGood,nextNodeIdBad,bgmCommand,bgmClipName,craftingFlagGood,craftingFlagBad,craftingVarChangesGood,craftingVarChangesBad,craftingRecipeId");
            foreach (var n in data.nodes)
            {
                string varGood = VarChangesToCsv(n.craftingVarChangesGood);
                string varBad  = VarChangesToCsv(n.craftingVarChangesBad);
                sb.AppendLine($"{n.nodeId},{n.speakerKey},{n.overrideSpeakerName},{Csv(n.text)},{n.nextNodeId},{n.requiresCrafting.ToString().ToLower()},{n.craftingTicketKey},{n.nextNodeIdGood},{n.nextNodeIdBad},{n.bgmCommand},{n.bgmClipName},{n.craftingFlagGood},{n.craftingFlagBad},{varGood},{varBad},{n.craftingRecipeId}");
            }
            sb.AppendLine();

            bool hasChars = data.nodes.Any(n => n.characters.Count > 0);
            if (hasChars)
            {
                sb.AppendLine("#NODE_CHARS");
                sb.AppendLine("nodeId,characterKey,expressionKey,slotIndex");
                foreach (var n in data.nodes)
                    foreach (var c in n.characters)
                        sb.AppendLine($"{n.nodeId},{c.characterKey},{c.expressionKey},{c.slotIndex}");
                sb.AppendLine();
            }

            bool hasChoices = data.nodes.Any(n => n.choices.Count > 0);
            if (hasChoices)
            {
                sb.AppendLine("#CHOICES");
                sb.AppendLine("nodeId,choiceIndex,buttonText,nextNodeId,setFlags,clearFlags,varChanges");
                foreach (var n in data.nodes)
                    for (int i = 0; i < n.choices.Count; i++)
                    {
                        var c    = n.choices[i];
                        string vars = VarChangesToCsv(c.varChanges);
                        sb.AppendLine($"{n.nodeId},{i},{Csv(c.buttonText)},{c.nextNodeId},{string.Join("|", c.setFlags)},{string.Join("|", c.clearFlags)},{vars}");
                    }
                sb.AppendLine();
            }

            bool hasFlagBranches = data.nodes.Any(n => n.flagBranches.Count > 0);
            if (hasFlagBranches)
            {
                sb.AppendLine("#NODE_BRANCHES");
                sb.AppendLine("nodeId,requiredAllFlags,requiredAnyFlags,nextNodeId");
                foreach (var n in data.nodes)
                    foreach (var b in n.flagBranches)
                        sb.AppendLine($"{n.nodeId},{string.Join("|", b.requiredAllFlags)},{string.Join("|", b.requiredAnyFlags)},{b.nextNodeId}");
                sb.AppendLine();
            }

            bool hasVarBranches = data.nodes.Any(n => n.varBranches.Count > 0);
            if (hasVarBranches)
            {
                sb.AppendLine("#NODE_VAR_BRANCHES");
                sb.AppendLine("nodeId,varName,op,threshold,nextNodeId");
                foreach (var n in data.nodes)
                    foreach (var b in n.varBranches)
                        sb.AppendLine($"{n.nodeId},{b.condition.varName},{CompareOpToString(b.condition.op)},{b.condition.threshold},{b.nextNodeId}");
            }

            const string dir = "Assets/Data/Export";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText($"{dir}/{id}.csv", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[EpisodeDataCompiler] Exported CSV → Assets/Data/Export/{id}.csv");
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\"", "\"\"");
            return (s.Contains(',') || s.Contains('\n') || s.Contains('"')) ? $"\"{s}\"" : s;
        }

        private static string VarChangesToCsv(List<VarChange> list)
        {
            if (list == null || list.Count == 0) return "";
            return string.Join("|", list.Select(v => $"{v.varName}{(v.delta >= 0 ? "+" : "")}{v.delta}"));
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
    }
}
