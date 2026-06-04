using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using NarrativeFlow.Runtime;

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
                Debug.LogError("Please select a NarrativeGraphSO asset.");
                return;
            }

            var compiler = new EpisodeDataCompiler();
            compiler.Compile(graph);
        }

        public void Compile(NarrativeGraphSO graph)
        {
            string episodeId = graph.name;
            string episodeTitle = string.IsNullOrEmpty(graph.episodeTitle) ? episodeId : graph.episodeTitle;

            EpisodeData runtimeData = ScriptableObject.CreateInstance<EpisodeData>();
            runtimeData.episodeId = episodeId;
            runtimeData.episodeTitle = episodeTitle;
            runtimeData.episodeDescription = graph.episodeDescription;
            runtimeData.iconNameBoard = graph.iconNameBoard;
            runtimeData.iconNameArchive = graph.iconNameArchive;
            runtimeData.characters = new List<EpisodeCharacter>(graph.characters);
            runtimeData.customConditionTexts = new List<string>(graph.customConditionTexts);

            // Copy trigger conditions and opening characters directly
            runtimeData.triggerCondition = graph.triggerCondition;
            runtimeData.openingCharacters = new List<CharacterSlotEntry>(graph.openingCharacters);

            // Maps: Block Guid -> First compiled node ID (used for block-level transitions)
            var blockStartRuntimeId = new Dictionary<string, string>();
            // Maps: Event Guid -> Compiled runtime node ID
            var eventToRuntimeIdMap = new Dictionary<string, string>();

            // First pass: Traverse/Sort sequences in EpisodeNodeSO and assign IDs, and generate Trigger Nodes
            foreach (var gNode in graph.Nodes)
            {
                if (gNode is EpisodeNodeSO epNode)
                {
                    var traversedEvents = GetTraversedEvents(epNode);
                    var runtimeIds = new List<string>();

                    if (traversedEvents.Count == 0)
                    {
                        // Create a dummy node if empty
                        string rid = $"{gNode.Guid}_0";
                        runtimeIds.Add(rid);
                        blockStartRuntimeId[gNode.Guid] = rid;

                        var rNode = new EpisodeNode { nodeId = rid, text = "(Empty Node)" };
                        runtimeData.nodes.Add(rNode);
                    }
                    else
                    {
                        for (int i = 0; i < traversedEvents.Count; i++)
                        {
                            string rid = $"{gNode.Guid}_{i}";
                            runtimeIds.Add(rid);
                            var ev = traversedEvents[i];
                            eventToRuntimeIdMap[ev.Guid] = rid;

                            var rNode = new EpisodeNode { nodeId = rid };
                            runtimeData.nodes.Add(rNode);
                        }
                        blockStartRuntimeId[gNode.Guid] = runtimeIds[0];
                    }
                }
                else if (gNode is TriggerNodeSO triggerNode)
                {
                    // Compile trigger node into an empty routing node
                    string rid = $"TRIGGER_{triggerNode.Guid}";
                    blockStartRuntimeId[gNode.Guid] = rid;

                    var rNode = new EpisodeNode { nodeId = rid };
                    runtimeData.nodes.Add(rNode);
                }
            }

            // Find Start Node in the main graph (node with no incoming edges)
            var incomingCount = new Dictionary<string, int>();
            foreach (var n in graph.Nodes) incomingCount[n.Guid] = 0;
            foreach (var e in graph.Edges) if (incomingCount.ContainsKey(e.TargetNodeGuid)) incomingCount[e.TargetNodeGuid]++;

            var startNodeGuid = incomingCount.OrderBy(kvp => kvp.Value).FirstOrDefault().Key;
            if (!string.IsNullOrEmpty(startNodeGuid) && blockStartRuntimeId.TryGetValue(startNodeGuid, out var startId))
            {
                runtimeData.firstNodeId = startId;
            }

            // Second pass: Populate fields and establish links
            foreach (var gNode in graph.Nodes)
            {
                if (gNode is EpisodeNodeSO epNode)
                {
                    var traversedEvents = GetTraversedEvents(epNode);
                    var edges = graph.Edges.Where(e => e.BaseNodeGuid == gNode.Guid).OrderBy(e => e.OutputPortIndex).ToList();

                    for (int i = 0; i < traversedEvents.Count; i++)
                    {
                        var ev = traversedEvents[i];
                        string rid = eventToRuntimeIdMap[ev.Guid];
                        var rNode = runtimeData.FindNode(rid);

                        // Dialogue fields
                        if (ev.Type == EpisodeEventType.Dialogue)
                        {
                            rNode.speakerKey = ev.SpeakerKey;
                            rNode.overrideSpeakerName = ev.OverrideSpeakerName;
                            rNode.text = ev.Text;
                            rNode.bgmCommand = ev.BgmCommand;
                            rNode.bgmClipName = ev.BgmClipName;

                            // Map character appearances
                            rNode.characters = new List<CharacterSlotEntry>();
                            foreach (var ca in ev.CharacterAppearances)
                            {
                                rNode.characters.Add(new CharacterSlotEntry
                                {
                                    characterKey = ca.CharacterKey,
                                    expressionKey = ca.ExpressionKey,
                                    slotIndex = ca.SlotIndex
                                });
                            }
                        }
                        // BusinessStart fields
                        else if (ev.Type == EpisodeEventType.BusinessStart)
                        {
                            rNode.requiresCrafting = true;
                            rNode.craftingTicketKey = ev.CraftingTicketKey;
                            rNode.craftingFlagGood = ev.CraftingFlagGood;
                            rNode.craftingFlagBad = ev.CraftingFlagBad;

                            rNode.craftingVarChangesGood = new List<VarChange>();
                            foreach (var vc in ev.CraftingVarChangesGood)
                            {
                                rNode.craftingVarChangesGood.Add(new VarChange { varName = vc.VarName, delta = vc.Delta });
                            }

                            rNode.craftingVarChangesBad = new List<VarChange>();
                            foreach (var vc in ev.CraftingVarChangesBad)
                            {
                                rNode.craftingVarChangesBad.Add(new VarChange { varName = vc.VarName, delta = vc.Delta });
                            }

                            // Success (Port 0) and Failure (Port 1) exits
                            if (edges.Count > 0) rNode.nextNodeIdGood = ResolveTargetId(graph, edges[0].TargetNodeGuid, blockStartRuntimeId);
                            if (edges.Count > 1) rNode.nextNodeIdBad = ResolveTargetId(graph, edges[1].TargetNodeGuid, blockStartRuntimeId);
                        }
                        // Choice fields
                        else if (ev.Type == EpisodeEventType.Choice)
                        {
                            rNode.choices = new List<EpisodeChoice>();
                            foreach (var c in ev.Choices)
                            {
                                var rc = new EpisodeChoice
                                {
                                    buttonText = c.ButtonText,
                                    setFlags = new List<string>(c.SetFlags),
                                    clearFlags = new List<string>(c.ClearFlags),
                                    varChanges = new List<VarChange>()
                                };

                                foreach (var vc in c.VarChanges)
                                {
                                    rc.varChanges.Add(new VarChange { varName = vc.VarName, delta = vc.Delta });
                                }

                                if (!string.IsNullOrEmpty(c.TargetNodeId) && eventToRuntimeIdMap.TryGetValue(c.TargetNodeId, out string choiceTargetRid))
                                {
                                    rc.nextNodeId = choiceTargetRid;
                                }
                                rNode.choices.Add(rc);
                            }
                        }
                        // BranchExit fields
                        else if (ev.Type == EpisodeEventType.BranchExit)
                        {
                            int branchIdx = epNode.OutgoingBranches.IndexOf(ev.ExitBranchName);
                            if (branchIdx >= 0)
                            {
                                var branchEdge = edges.Find(e => e.OutputPortIndex == branchIdx);
                                if (branchEdge.BaseNodeGuid != null)
                                {
                                    rNode.nextNodeId = ResolveTargetId(graph, branchEdge.TargetNodeGuid, blockStartRuntimeId);
                                }
                            }
                        }

                        // Set default nextNodeId if not Choice/BusinessStart/BranchExit
                        if (ev.Type != EpisodeEventType.Choice && ev.Type != EpisodeEventType.BusinessStart && ev.Type != EpisodeEventType.BranchExit)
                        {
                            // If has internal connection
                            if (ev.NextEventGuids.Count > 0 && eventToRuntimeIdMap.TryGetValue(ev.NextEventGuids[0], out string nextEventRid))
                            {
                                rNode.nextNodeId = nextEventRid;
                            }
                            else
                            {
                                // Otherwise exit block using default port (Port 0)
                                var defaultEdge = edges.Find(e => e.OutputPortIndex == 0);
                                if (defaultEdge.BaseNodeGuid != null)
                                {
                                    rNode.nextNodeId = ResolveTargetId(graph, defaultEdge.TargetNodeGuid, blockStartRuntimeId);
                                }
                            }
                        }
                    }
                }
                else if (gNode is TriggerNodeSO triggerNode)
                {
                    string rid = $"TRIGGER_{triggerNode.Guid}";
                    var rNode = runtimeData.FindNode(rid);
                    var edges = graph.Edges.Where(e => e.BaseNodeGuid == gNode.Guid).OrderBy(e => e.OutputPortIndex).ToList();

                    // Map trigger conditions
                    rNode.flagBranches = new List<NodeFlagBranch>();
                    rNode.varBranches = new List<NodeVarBranch>();

                    for (int i = 0; i < triggerNode.Conditions.Count; i++)
                    {
                        var cond = triggerNode.Conditions[i];
                        var edge = edges.Find(e => e.OutputPortIndex == i);

                        if (edge.BaseNodeGuid != null)
                        {
                            string targetRid = ResolveTargetId(graph, edge.TargetNodeGuid, blockStartRuntimeId);

                            if (cond.Type == TriggerConditionType.Flag)
                            {
                                rNode.flagBranches.Add(new NodeFlagBranch
                                {
                                    requiredAllFlags = new List<string> { cond.Key },
                                    nextNodeId = targetRid
                                });
                            }
                            else
                            {
                                rNode.varBranches.Add(new NodeVarBranch
                                {
                                    condition = new VarCondition { varName = cond.Key, op = ParseCompareOp(cond.Operator), threshold = int.Parse(cond.Value) },
                                    nextNodeId = targetRid
                                });
                            }
                        }
                    }

                    // Else port (last port)
                    var elseEdge = edges.Find(e => e.OutputPortIndex == triggerNode.Conditions.Count);
                    if (elseEdge.BaseNodeGuid != null)
                    {
                        rNode.nextNodeId = ResolveTargetId(graph, elseEdge.TargetNodeGuid, blockStartRuntimeId);
                    }
                }
            }

            // Save Asset
            string dir = "Assets/Resources/EpisodeData";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = $"{dir}/EpisodeData_{episodeId}.asset";

            // Maintain custom inspector fields when overwriting
            EpisodeData existing = AssetDatabase.LoadAssetAtPath<EpisodeData>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(runtimeData, existing);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(runtimeData, path);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Compiled {episodeId} to {path}");

            // Export to JSON and CSV
            ExportToJson(runtimeData, episodeId);
            ExportToCsv(runtimeData, episodeId);
        }

        private void ExportToJson(EpisodeData data, string id)
        {
            string json = JsonUtility.ToJson(data, true);
            string dir = "Assets/Data/Export";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText($"{dir}/{id}.json", json);
            Debug.Log($"Exported JSON to {dir}/{id}.json");
        }

        private void ExportToCsv(EpisodeData data, string id)
        {
            var sb = new System.Text.StringBuilder();
            
            sb.AppendLine("#META");
            sb.AppendLine("episodeId,episodeTitle,firstNodeId");
            sb.AppendLine($"{data.episodeId},{data.episodeTitle},{data.firstNodeId}");
            sb.AppendLine();

            sb.AppendLine("#NODES");
            sb.AppendLine("nodeId,speakerKey,overrideSpeakerName,text,nextNodeId,requiresCrafting,craftingTicketKey,nextNodeIdGood,nextNodeIdBad");
            foreach (var n in data.nodes)
            {
                string text = n.text?.Replace("\"", "\"\"") ?? "";
                if (text.Contains(",")) text = $"\"{text}\"";
                sb.AppendLine($"{n.nodeId},{n.speakerKey},{n.overrideSpeakerName},{text},{n.nextNodeId},{n.requiresCrafting.ToString().ToLower()},{n.craftingTicketKey},{n.nextNodeIdGood},{n.nextNodeIdBad}");
            }
            sb.AppendLine();

            sb.AppendLine("#CHOICES");
            sb.AppendLine("nodeId,choiceIndex,buttonText,nextNodeId,setFlags,clearFlags,varChanges");
            foreach (var n in data.nodes)
            {
                for (int i = 0; i < n.choices.Count; i++)
                {
                    var c = n.choices[i];
                    string sFlags = string.Join("|", c.setFlags);
                    string cFlags = string.Join("|", c.clearFlags);
                    string vChanges = string.Join("|", c.varChanges.Select(vc => $"{vc.varName}{(vc.delta >= 0 ? "+" : "")}{vc.delta}"));
                    sb.AppendLine($"{n.nodeId},{i},{c.buttonText},{c.nextNodeId},{sFlags},{cFlags},{vChanges}");
                }
            }

            string dir = "Assets/Data/Export";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText($"{dir}/{id}.csv", sb.ToString());
            Debug.Log($"Exported CSV to {dir}/{id}.csv");
        }

        private string ResolveTargetId(NarrativeGraphSO graph, string targetGuid, Dictionary<string, string> blockStartRuntimeId)
        {
            if (blockStartRuntimeId.TryGetValue(targetGuid, out string startId))
            {
                return startId;
            }
            return null;
        }

        private List<EpisodeEvent> GetTraversedEvents(EpisodeNodeSO epNode)
        {
            var traversedList = new List<EpisodeEvent>();
            if (epNode.Events == null || epNode.Events.Count == 0) return traversedList;

            var eventMap = epNode.Events.ToDictionary(e => e.Guid);

            // 1. Identify start node
            EpisodeEvent startEvent = null;
            if (!string.IsNullOrEmpty(epNode.StartEventGuid) && eventMap.TryGetValue(epNode.StartEventGuid, out var sEv))
            {
                startEvent = sEv;
            }
            else
            {
                // Find node with 0 incoming connections
                var incoming = new Dictionary<string, int>();
                foreach (var ev in epNode.Events) incoming[ev.Guid] = 0;
                foreach (var ev in epNode.Events)
                {
                    if (ev.Type == EpisodeEventType.Choice)
                    {
                        foreach (var c in ev.Choices)
                        {
                            if (!string.IsNullOrEmpty(c.TargetNodeId) && incoming.ContainsKey(c.TargetNodeId))
                                incoming[c.TargetNodeId]++;
                        }
                    }
                    else
                    {
                        foreach (var nextId in ev.NextEventGuids)
                        {
                            if (incoming.ContainsKey(nextId)) incoming[nextId]++;
                        }
                    }
                }
                var startCandidate = incoming.OrderBy(kvp => kvp.Value).FirstOrDefault();
                if (!string.IsNullOrEmpty(startCandidate.Key))
                {
                    startEvent = eventMap[startCandidate.Key];
                }
            }

            if (startEvent == null) startEvent = epNode.Events[0];

            // 2. Perform BFS/DFS traversal
            var visited = new HashSet<string>();
            var queue = new Queue<EpisodeEvent>();
            queue.Enqueue(startEvent);
            visited.Add(startEvent.Guid);

            while (queue.Count > 0)
            {
                var ev = queue.Dequeue();
                traversedList.Add(ev);

                if (ev.Type == EpisodeEventType.Choice)
                {
                    foreach (var c in ev.Choices)
                    {
                        if (!string.IsNullOrEmpty(c.TargetNodeId) && eventMap.TryGetValue(c.TargetNodeId, out var targetEv) && !visited.Contains(c.TargetNodeId))
                        {
                            queue.Enqueue(targetEv);
                            visited.Add(c.TargetNodeId);
                        }
                    }
                }
                else
                {
                    foreach (var nextId in ev.NextEventGuids)
                    {
                        if (eventMap.TryGetValue(nextId, out var targetEv) && !visited.Contains(nextId))
                        {
                            queue.Enqueue(targetEv);
                            visited.Add(nextId);
                        }
                    }
                }
            }

            // 3. Append isolated (unvisited) nodes at the end to prevent data loss
            foreach (var ev in epNode.Events)
            {
                if (!visited.Contains(ev.Guid))
                {
                    traversedList.Add(ev);
                    visited.Add(ev.Guid);
                }
            }

            return traversedList;
        }

        private CompareOp ParseCompareOp(string op)
        {
            return op.Trim() switch
            {
                ">=" => CompareOp.GreaterOrEqual,
                ">"  => CompareOp.Greater,
                "==" => CompareOp.Equal,
                "<"  => CompareOp.Less,
                "<=" => CompareOp.LessOrEqual,
                _    => CompareOp.GreaterOrEqual
            };
        }
    }
}
