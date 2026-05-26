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
            // Get title from first EpisodeNodeSO's custom fields if exists
            string episodeTitle = episodeId;
            var firstNode = graph.Nodes.OfType<EpisodeNodeSO>().FirstOrDefault();
            if (firstNode != null)
            {
                var titleField = firstNode.CustomFields.Find(f => f.FieldName != null && f.FieldName.ToLower() == "title");
                if (titleField != null && !string.IsNullOrEmpty(titleField.FieldValue))
                {
                    episodeTitle = titleField.FieldValue;
                }
            }

            EpisodeData runtimeData = ScriptableObject.CreateInstance<EpisodeData>();
            runtimeData.episodeId = episodeId;
            runtimeData.episodeTitle = episodeTitle;

            // Mapping: Graph Node Guid -> List of Runtime Node IDs (since one graph node can be multiple runtime nodes)
            var nodeMapping = new Dictionary<string, List<string>>();
            
            // First pass: Generate all runtime nodes and assign IDs
            foreach (var gNode in graph.Nodes)
            {
                if (gNode is EpisodeNodeSO epNode)
                {
                    var runtimeIds = new List<string>();
                    if (epNode.Events.Count == 0)
                    {
                        // Create a dummy node if empty
                        string rid = $"{gNode.Guid}_0";
                        runtimeIds.Add(rid);
                        runtimeData.nodes.Add(new EpisodeNode { nodeId = rid, text = "(Empty Node)" });
                    }
                    else
                    {
                        for (int i = 0; i < epNode.Events.Count; i++)
                        {
                            string rid = $"{gNode.Guid}_{i}";
                            runtimeIds.Add(rid);
                            var ev = epNode.Events[i];
                            var rNode = new EpisodeNode { nodeId = rid };
                            
                            if (ev.Type == EpisodeEventType.Dialogue)
                            {
                                rNode.speakerKey = ev.SpeakerKey;
                                rNode.overrideSpeakerName = ev.OverrideSpeakerName;
                                rNode.text = ev.Text;
                            }
                            else if (ev.Type == EpisodeEventType.BusinessStart)
                            {
                                rNode.requiresCrafting = true;
                                rNode.craftingTicketKey = ev.CraftingTicketKey;
                            }
                            else if (ev.Type == EpisodeEventType.Choice)
                            {
                                foreach (var c in ev.Choices)
                                {
                                    rNode.choices.Add(new EpisodeChoice
                                    {
                                        buttonText = c.ButtonText,
                                        setFlags = new List<string>(c.SetFlags),
                                        clearFlags = new List<string>(c.ClearFlags),
                                        // TargetNodeId will be resolved in second pass
                                    });
                                }
                            }
                            // BusinessEnd is a logical point, usually it's the LAST event in a block
                            // that has two output ports. In runtime, it's properties on the BusinessStart node.
                            // Wait, if BusinessEnd is a separate event, we need to merge it back to the Start node
                            // OR the Start node handles the jump. 
                            // Current EpisodeNode structure: requiresCrafting=true node has nextNodeIdGood/Bad.
                            
                            runtimeData.nodes.Add(rNode);
                        }
                    }
                    nodeMapping[gNode.Guid] = runtimeIds;
                }
                else if (gNode is TriggerNodeSO triggerNode)
                {
                    // Trigger nodes are logical routers, they don't produce Dialogue nodes
                    // But they need an ID to be referenced? 
                    // Actually, if Node A -> Trigger T -> Node B, 
                    // Node A's nextNodeId (or branches) should point directly to Node B.
                    // We'll handle this in second pass resolution.
                }
            }

            // Find Start Node (no incoming edges)
            var incomingCount = new Dictionary<string, int>();
            foreach (var n in graph.Nodes) incomingCount[n.Guid] = 0;
            foreach (var e in graph.Edges) if (incomingCount.ContainsKey(e.TargetNodeGuid)) incomingCount[e.TargetNodeGuid]++;
            
            var startNodeGuid = incomingCount.OrderBy(kvp => kvp.Value).FirstOrDefault().Key;
            if (nodeMapping.TryGetValue(startNodeGuid, out var startIds))
            {
                runtimeData.firstNodeId = startIds[0];
            }

            // Second pass: Link nodes
            foreach (var gNode in graph.Nodes)
            {
                if (gNode is EpisodeNodeSO epNode)
                {
                    var rIds = nodeMapping[gNode.Guid];
                    for (int i = 0; i < epNode.Events.Count; i++)
                    {
                        var ev = epNode.Events[i];
                        var rNode = runtimeData.FindNode(rIds[i]);

                        // Internal link within block
                        if (i < epNode.Events.Count - 1)
                        {
                            rNode.nextNodeId = rIds[i+1];
                            
                            // Special case: if this is a choice event, the choices override nextNodeId
                            // But in our graph, choice output ports are handled at the block level.
                            // This is a bit tricky. If Choice is NOT the last event, where do the choices go?
                            // In this simple compiler, we assume Choice/BusinessEnd are LAST in the block if they lead to other nodes.
                        }
                        else
                        {
                            // Last event in block - link to next graph nodes
                            var edges = graph.Edges.Where(e => e.BaseNodeGuid == gNode.Guid).OrderBy(e => e.OutputPortIndex).ToList();
                            
                            if (ev.Type == EpisodeEventType.Choice)
                            {
                                for (int j = 0; j < ev.Choices.Count && j < edges.Count; j++)
                                {
                                    rNode.choices[j].nextNodeId = ResolveTargetId(graph, edges[j].TargetNodeGuid, nodeMapping);
                                }
                            }
                            else if (ev.Type == EpisodeEventType.BusinessEnd || rNode.requiresCrafting)
                            {
                                // BusinessEnd logic: Success = port 0, Fail = port 1
                                if (edges.Count > 0) rNode.nextNodeIdGood = ResolveTargetId(graph, edges[0].TargetNodeGuid, nodeMapping);
                                if (edges.Count > 1) rNode.nextNodeIdBad = ResolveTargetId(graph, edges[1].TargetNodeGuid, nodeMapping);
                            }
                            else
                            {
                                // Handle manual branches with potential conditions
                                for (int j = 0; j < edges.Count; j++)
                                {
                                    var edge = edges[j];
                                    if (j < epNode.OutgoingBranches.Count)
                                    {
                                        string branchDef = epNode.OutgoingBranches[j];
                                        string targetRId = ResolveTargetId(graph, edge.TargetNodeGuid, nodeMapping);
                                        
                                        if (TryParseCondition(branchDef, out var cond))
                                        {
                                            if (cond.Type == TriggerConditionType.Flag)
                                            {
                                                rNode.flagBranches.Add(new NodeFlagBranch
                                                {
                                                    requiredAllFlags = new List<string> { cond.Key },
                                                    nextNodeId = targetRId
                                                });
                                            }
                                            else
                                            {
                                                rNode.varBranches.Add(new NodeVarBranch { 
                                                    condition = new VarCondition { varName = cond.Key, threshold = int.Parse(cond.Value) }, 
                                                    nextNodeId = targetRId 
                                                });
                                            }
                                        }
                                        else if (j == 0 || branchDef.ToLower() == "next" || branchDef.ToLower() == "default")
                                        {
                                            // Fallback next node
                                            rNode.nextNodeId = targetRId;
                                        }
                                    }
                                }
                                
                                // Also handle cases where a TriggerNode might be directly connected
                                if (edges.Count > 0 && rNode.nextNodeId == null && rNode.flagBranches.Count == 0 && rNode.varBranches.Count == 0)
                                {
                                    var targetNode = graph.Nodes.Find(n => n.Guid == edges[0].TargetNodeGuid);
                                    if (targetNode is TriggerNodeSO triggerNode)
                                    {
                                        InjectTriggerLogic(rNode, triggerNode, graph, nodeMapping);
                                    }
                                    else
                                    {
                                        rNode.nextNodeId = ResolveTargetId(graph, edges[0].TargetNodeGuid, nodeMapping);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Save Asset
            string dir = "Assets/Resources/EpisodeData";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = $"{dir}/EpisodeData_{episodeId}.asset";
            AssetDatabase.CreateAsset(runtimeData, path);
            AssetDatabase.SaveAssets();
            
            Debug.Log($"Compiled {episodeId} to {path}");

            // Also Export to JSON and CSV if desired
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
            // Simple CSV export logic following the project's CSV guide
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
                    sb.AppendLine($"{n.nodeId},{i},{c.buttonText},{c.nextNodeId},{sFlags},{cFlags},");
                }
            }

            string dir = "Assets/Data/Export";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText($"{dir}/{id}.csv", sb.ToString());
            Debug.Log($"Exported CSV to {dir}/{id}.csv");
        }

        private void InjectTriggerLogic(EpisodeNode rNode, TriggerNodeSO triggerNode, NarrativeGraphSO graph, Dictionary<string, List<string>> nodeMapping)
        {
            // Inject trigger logic into this runtime node
            foreach (var cond in triggerNode.Conditions)
            {
                var targetEdge = graph.Edges.Find(e => e.BaseNodeGuid == triggerNode.Guid && e.OutputPortIndex == triggerNode.Conditions.IndexOf(cond));
                if (!string.IsNullOrEmpty(targetEdge.BaseNodeGuid))
                {
                    string targetRId = ResolveTargetId(graph, targetEdge.TargetNodeGuid, nodeMapping);
                    if (cond.Type == TriggerConditionType.Flag)
                    {
                        rNode.flagBranches.Add(new NodeFlagBranch
                        {
                            requiredAllFlags = new List<string> { cond.Key },
                            nextNodeId = targetRId
                        });
                    }
                    else
                    {
                        rNode.varBranches.Add(new NodeVarBranch { 
                            condition = new VarCondition { varName = cond.Key, threshold = int.Parse(cond.Value) }, 
                            nextNodeId = targetRId 
                        });
                    }
                }
            }
            // Handle Else port (last port of trigger)
            var elseEdge = graph.Edges.Find(e => e.BaseNodeGuid == triggerNode.Guid && e.OutputPortIndex == triggerNode.Conditions.Count);
            if (!string.IsNullOrEmpty(elseEdge.BaseNodeGuid))
            {
                rNode.nextNodeId = ResolveTargetId(graph, elseEdge.TargetNodeGuid, nodeMapping);
            }
        }

        private bool TryParseCondition(string input, out GraphTriggerCondition cond)
        {
            cond = new GraphTriggerCondition();
            
            // Expected formats:
            // "Money > 100" (Variable)
            // "FlagName == true" (Flag)
            // "FlagName == false" (Flag)
            
            string[] parts = input.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;

            string key = parts[0];
            string op = parts[1];
            string val = parts[2];

            if (val.ToLower() == "true" || val.ToLower() == "false")
            {
                cond.Type = TriggerConditionType.Flag;
                cond.Key = key;
                // Note: current NodeFlagBranch system usually checks for 'Presence' of flag
                // We assume if someone puts "Flag == false", the system handles it or we use a convention
                return true;
            }
            else if (int.TryParse(val, out _))
            {
                cond.Type = TriggerConditionType.Variable;
                cond.Key = key;
                cond.Operator = op;
                cond.Value = val;
                return true;
            }

            return false;
        }

        private string ResolveTargetId(NarrativeGraphSO graph, string targetGuid, Dictionary<string, List<string>> nodeMapping)
        {
            var targetNode = graph.Nodes.Find(n => n.Guid == targetGuid);
            if (targetNode is EpisodeNodeSO)
            {
                return nodeMapping[targetGuid][0];
            }
            else if (targetNode is TriggerNodeSO)
            {
                // Recursive resolution through trigger
                // For now, this is a placeholder. 
                // A true trigger would return a branch structure, but EpisodeNode already has branches.
                // Simplified: Just point to the first node of the first branch for now, 
                // but real implementation should inject branches into the PREVIOUS node.
                return $"TRIGGER_{targetGuid}"; 
            }
            return null;
        }
    }
}
