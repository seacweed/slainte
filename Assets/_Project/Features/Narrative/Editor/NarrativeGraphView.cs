using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
    // 내러티브 그래프 에디터의 GraphView 구현체. NarrativeGraphSO(영속 데이터: Nodes/Edges)와
    // 화면에 그려진 NarrativeNodeView/Edge 사이의 동기화를 담당한다 — 노드/엣지를 그래프에서
    // 추가·삭제할 때마다(OnGraphViewChanged) 영속 데이터에도 같은 변경을 반영한다. 에피소드 대화는
    // 되돌아가는 흐름(다시 묻기 등)이 정상이므로 순환 연결은 막지 않는다.
    public class NarrativeGraphView : GraphView
    {
        public NarrativeGraphEditor window;
        private NarrativeSearchWindow _searchWindow;
        public NarrativeGraphSO currentGraph;

        public NarrativeGraphView(NarrativeGraphEditor editorWindow)
        {
            window = editorWindow;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            var contentDragger = new LeftClickPanDragger();
            this.AddManipulator(contentDragger);

            this.AddManipulator(new SelectionDragger());
            
            var rectangleSelector = new RectangleSelector();
            rectangleSelector.activators.Clear();
            rectangleSelector.activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse, modifiers = EventModifiers.Control });
            this.AddManipulator(rectangleSelector);

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            AddSearchWindow();

            graphViewChanged += OnGraphViewChanged;
        }

        // 현재 그래프 전체를 자동 배치한다. 화면에 그려진 카드의 실제 높이를 써서 같은 열의 카드가 겹치지 않게 한다.
        public void AutoLayout()
        {
            if (currentGraph == null) return;
            Dictionary<NodeDataSO, float> measured = nodes.OfType<NarrativeNodeView>()
                .Where(v => v.nodeData != null)
                .GroupBy(v => v.nodeData)
                .ToDictionary(g => g.Key, g => g.First().layout.height);

            Undo.RecordObjects(currentGraph.Nodes.Where(n => n != null).Cast<UnityEngine.Object>().ToArray(), "Auto Layout");
            NarrativeGraphLayout.Arrange(currentGraph, node =>
                measured.TryGetValue(node, out float h) && !float.IsNaN(h) && h > 0f ? h : NarrativeGraphLayout.EstimateHeight(node));
            foreach (NodeDataSO node in currentGraph.Nodes.Where(n => n != null))
                EditorUtility.SetDirty(node);
            window?.ReloadGraph();
        }

        private void AddSearchWindow()
        {
            _searchWindow = ScriptableObject.CreateInstance<NarrativeSearchWindow>();
            _searchWindow.Init(this);
            nodeCreationRequest = context => SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), _searchWindow);
        }

        public void PopulateView(NarrativeGraphSO graph)
        {
            currentGraph = graph;
            graphElements.ToList().ForEach(RemoveElement);

            if (currentGraph != null)
            {
                // 예전 그래프는 선택지/제조 블록의 포트 라벨이 내용과 어긋나 있을 수 있어 열 때 한 번 맞춘다.
                foreach (var block in currentGraph.Nodes.OfType<EpisodeNodeSO>())
                {
                    // 예전 시퀀스 에디터의 연결선 순서를 리스트 순서로 한 번 옮긴다.
                    bool migrated = NarrativeBlockModel.MigrateLegacyOrder(block);
                    if (NarrativeBlockModel.SyncDerivedPorts(block) || migrated) EditorUtility.SetDirty(block);
                }
                // 번호가 규칙과 어긋난 채 저장된 그래프(예전 규칙, 손으로 고친 CSV)도 열자마자 맞춘다 —
                // 편집할 때만 맞추면 처음 건드리는 순간 번호가 통째로 바뀌어 보인다.
                NarrativeNodeIdAssigner.RegenerateIds(currentGraph);

                var nodeDictionary = new Dictionary<string, NarrativeNodeView>();
                foreach (var node in currentGraph.Nodes)
                {
                    if (node != null)
                    {
                        var nodeView = new NarrativeNodeView(node);
                        AddElement(nodeView);
                        nodeDictionary.Add(node.Guid, nodeView);
                    }
                }

                foreach (var edgeData in currentGraph.Edges)
                {
                    if (nodeDictionary.TryGetValue(edgeData.BaseNodeGuid, out var baseNode) &&
                        nodeDictionary.TryGetValue(edgeData.TargetNodeGuid, out var targetNode))
                    {
                        var outputPorts = baseNode.outputContainer.Query<Port>().ToList();
                        if (edgeData.OutputPortIndex < outputPorts.Count)
                            AddElement(outputPorts[edgeData.OutputPortIndex].ConnectTo(targetNode.inputContainer.Q<Port>()));
                    }
                }
                
                ValidateAllNodes(); // Validation after load
            }
        }

        public void CreateNode(Type type, Vector2 position)
        {
            if (currentGraph == null) return;
            var nodeData = ScriptableObject.CreateInstance(type) as NodeDataSO;
            nodeData.Guid = Guid.NewGuid().ToString();
            nodeData.name = nodeData.Guid;
            nodeData.Position = new Rect(position, new Vector2(150, 200));

            Undo.RegisterCreatedObjectUndo(nodeData, "Create Node");
            AssetDatabase.AddObjectToAsset(nodeData, currentGraph);
            Undo.RecordObject(currentGraph, "Add To Graph");
            currentGraph.Nodes.Add(nodeData);
            AssetDatabase.SaveAssets();

            AddElement(new NarrativeNodeView(nodeData));
            NarrativeNodeIdAssigner.RegenerateIds(currentGraph);
            ValidateAllNodes();
        }

        public void CreateNodeFromTemplate(NodeDataSO template, Vector2 position)
        {
            if (currentGraph == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select or create a Narrative Graph Asset first.", "OK");
                return;
            }

            var nodeData = UnityEngine.Object.Instantiate(template);
            nodeData.Guid = Guid.NewGuid().ToString();
            nodeData.name = nodeData.Guid;
            nodeData.Position = new Rect(position, new Vector2(150, 200));

            Undo.RegisterCreatedObjectUndo(nodeData, "Create Narrative Node from Template");
            AssetDatabase.AddObjectToAsset(nodeData, currentGraph);
            
            Undo.RecordObject(currentGraph, "Add Node To Graph");
            currentGraph.Nodes.Add(nodeData);

            AssetDatabase.SaveAssets();

            var nodeView = new NarrativeNodeView(nodeData);
            AddElement(nodeView);
            NarrativeNodeIdAssigner.RegenerateIds(currentGraph);
            ValidateAllNodes();
        }

        // 그래프 전체 검증. 컴파일러와 같은 규칙(NarrativeBlockModel/BranchLabel)으로 판정해, 노드에 경고가
        // 없으면 컴파일도 통과하도록 맞춘다.
        public void ValidateAllNodes()
        {
            var nodeViews = graphElements.OfType<NarrativeNodeView>().ToList();
            var titleCounts = nodeViews.GroupBy(v => v.title).ToDictionary(g => g.Key, g => g.Count());
            var knownEpisodeIds = new HashSet<string>(
                Resources.LoadAll<EpisodeData>(Slainte.Content.ProjectResourcePaths.NarrativeEpisodes)
                    .Where(e => e != null).Select(e => e.episodeId),
                StringComparer.OrdinalIgnoreCase);
            var runtimeIdCounts = (currentGraph != null ? currentGraph.Nodes.OfType<EpisodeNodeSO>() : Enumerable.Empty<EpisodeNodeSO>())
                .SelectMany(b => b.Events)
                .Where(e => NarrativeBlockModel.IsRuntimeEvent(e) && !string.IsNullOrWhiteSpace(e.RuntimeNodeId))
                .GroupBy(e => e.RuntimeNodeId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var connectedPorts = new HashSet<(string, int)>(
                (currentGraph != null ? currentGraph.Edges : new List<EdgeData>()).Select(e => (e.BaseNodeGuid, e.OutputPortIndex)));

            foreach (var v in nodeViews)
            {
                var errors = new List<string>();
                var fieldErrors = new Dictionary<string, string>();

                if (!string.IsNullOrEmpty(v.title) && titleCounts[v.title] > 1)
                {
                    errors.Add("Duplicate Node Title");
                    fieldErrors["title"] = "Title is already used by another node.";
                }

                if (v.nodeData.CustomFields != null)
                {
                    var names = v.nodeData.CustomFields.Select(f => f.FieldName.ToLower()).ToList();
                    for (int i = 0; i < names.Count; i++)
                    {
                        if (names.Count(n => n == names[i]) > 1)
                        {
                            fieldErrors[$"field_{i}"] = "Duplicate Field Name";
                            if (!errors.Contains("Duplicate Field Names")) errors.Add("Duplicate Field Names");
                        }
                    }
                }

                if (v.nodeData is EpisodeNodeSO ep)
                    ValidateBlock(ep, errors, fieldErrors, knownEpisodeIds, runtimeIdCounts, connectedPorts);
                else if (v.nodeData is TriggerNodeSO tr)
                    ValidateTrigger(tr, errors, fieldErrors, knownEpisodeIds);

                v.SetWarning(errors.Count > 0, string.Join("\n• ", errors), fieldErrors);
            }
        }

        private static void ValidateBlock(
            EpisodeNodeSO ep,
            List<string> errors,
            Dictionary<string, string> fieldErrors,
            HashSet<string> knownEpisodeIds,
            Dictionary<string, int> runtimeIdCounts,
            HashSet<(string, int)> connectedPorts)
        {
            var unreachable = new List<EpisodeEvent>();
            var ordered = NarrativeBlockModel.GetExecutionOrder(ep, unreachable);
            if (unreachable.Count > 0)
                errors.Add($"실행되지 않는 이벤트 {unreachable.Count}개 (시퀀스 연결이 끊겼거나 선택지/제조 뒤에 있음)");

            foreach (var ev in ordered.Where(NarrativeBlockModel.IsRuntimeEvent))
            {
                if (!string.IsNullOrWhiteSpace(ev.RuntimeNodeId)
                    && runtimeIdCounts.TryGetValue(ev.RuntimeNodeId.Trim(), out int count) && count > 1)
                    errors.Add($"노드 ID 중복: {ev.RuntimeNodeId}");
            }

            var kind = NarrativeBlockModel.GetTerminalKind(ep);
            var labels = NarrativeBlockModel.GetPortLabels(ep);
            if (kind != BlockTerminalKind.Branches)
            {
                for (int i = 0; i < labels.Count; i++)
                {
                    if (connectedPorts.Contains((ep.Guid, i))) continue;
                    fieldErrors[$"branch_{i}"] = "연결 안 됨 — 이 결과에서 에피소드가 끝납니다";
                    if (kind == BlockTerminalKind.Choice)
                        errors.Add($"선택지 '{labels[i]}'가 연결되지 않았습니다");
                }
                return;
            }

            if (ep.Events.Any(e => !NarrativeBlockModel.IsRuntimeEvent(e)))
                errors.Add("더 이상 쓰지 않는 이벤트(분기 탈출 등)가 있습니다 — 실행에서 무시되니 삭제하세요");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ep.OutgoingBranches.Count; i++)
            {
                string label = ep.OutgoingBranches[i] ?? "";
                if (!seen.Add(label))
                {
                    fieldErrors[$"branch_{i}"] = "Duplicate Branch Name";
                    if (!errors.Contains("Duplicate Branch Names")) errors.Add("Duplicate Branch Names");
                    continue;
                }

                var parsed = BranchLabel.Parse(label);
                if (parsed.Kind == BranchLabelKind.Invalid)
                {
                    fieldErrors[$"branch_{i}"] = parsed.Error;
                    errors.Add(parsed.Error);
                }
                else if (parsed.Kind == BranchLabelKind.Episode && !knownEpisodeIds.Contains(parsed.Name))
                {
                    fieldErrors[$"branch_{i}"] = $"'{parsed.Name}'는 알려진 에피소드 ID가 아닙니다(에피소드 완료 조건으로 해석됨)";
                    errors.Add($"알 수 없는 에피소드 분기: {parsed.Name}");
                }
            }
        }

        private static void ValidateTrigger(
            TriggerNodeSO tr,
            List<string> errors,
            Dictionary<string, string> fieldErrors,
            HashSet<string> knownEpisodeIds)
        {
            for (int i = 0; i < tr.Conditions.Count; i++)
            {
                var c = tr.Conditions[i];
                if (string.IsNullOrWhiteSpace(c.Key))
                {
                    fieldErrors[$"cond_key_{i}"] = "Required";
                    errors.Add($"Condition {i} Key missing");
                    continue;
                }

                if (c.Type == TriggerConditionType.Episode)
                {
                    if (!knownEpisodeIds.Contains(c.Key.Trim()))
                    {
                        fieldErrors[$"cond_key_{i}"] = "알려진 에피소드 ID가 아닙니다";
                        errors.Add($"Condition {i}: 알 수 없는 에피소드 {c.Key}");
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(c.Value))
                {
                    fieldErrors[$"cond_val_{i}"] = "Required";
                    errors.Add($"Condition {i} Value missing");
                    continue;
                }

                string op = string.IsNullOrWhiteSpace(c.Operator) ? "==" : c.Operator.Trim();
                var parsed = BranchLabel.Parse($"{c.Key.Trim()} {op} {c.Value.Trim()}");
                bool isFlag = parsed.IsFlag;
                if (parsed.Kind == BranchLabelKind.Invalid || (c.Type == TriggerConditionType.Flag) != isFlag)
                {
                    string message = parsed.Error ?? (c.Type == TriggerConditionType.Flag
                        ? "Flag 조건은 '== true' / '== false'만 쓸 수 있습니다"
                        : "Variable 조건 값은 정수여야 합니다");
                    fieldErrors[$"cond_val_{i}"] = message;
                    errors.Add($"Condition {i}: {message}");
                }
            }
        }

        // 카드의 index 이벤트부터 새 블록으로 떼어 낸다. 블록 수와 연결이 바뀌므로 그래프 전체를 다시 그린다.
        public void SplitBlock(NarrativeNodeView view, int index)
        {
            if (currentGraph == null || !(view?.nodeData is EpisodeNodeSO block)) return;
            if (NarrativeBlockEditing.SplitBlock(currentGraph, block, index) == null) return;
            NarrativeNodeIdAssigner.RegenerateIds(currentGraph);
            window.ReloadGraph();
        }

        public void MergeWithNext(NarrativeNodeView view)
        {
            if (currentGraph == null || !(view?.nodeData is EpisodeNodeSO block)) return;
            if (!NarrativeBlockEditing.MergeWithNext(currentGraph, block)) return;
            NarrativeNodeIdAssigner.RegenerateIds(currentGraph);
            window.ReloadGraph();
        }

        // 이벤트 구성이 바뀐 블록의 포트를 다시 맞추고(선택지/제조 블록은 포트가 내용에서 결정됨),
        // 흐름이 바뀌었으므로 모든 런타임 노드 ID를 다시 매긴다. ValidateAllNodes가 모든 카드를 다시 그린다.
        public void RefreshBlock(NarrativeNodeView view)
        {
            if (view == null) return;
            if (view.nodeData is EpisodeNodeSO ep && NarrativeBlockModel.SyncDerivedPorts(ep))
                EditorUtility.SetDirty(ep);
            NotifyNodeStructureChanged(view);
            NarrativeNodeIdAssigner.RegenerateIds(currentGraph);
            view.RefreshVisuals();
            ValidateAllNodes();
        }

        // 노드의 포트 구성이 바뀌었을 때(분기 추가/삭제 등) 호출된다. 화면상의 연결선을 일단 모두
        // 지우고 포트를 다시 그린 뒤, 영속 데이터(currentGraph.Edges)에 남아있는 연결 정보를 새
        // 포트 인덱스에 맞춰 복원한다 — 복원할 포트가 더 이상 없는 엣지(삭제된 분기)는 데이터에서도 함께 제거한다.
        public void NotifyNodeStructureChanged(NarrativeNodeView nodeView)
        {
            if (currentGraph == null) return;

            // 1. Identify and remove visual edges connected to this node
            var outputPorts = nodeView.outputContainer.Query<Port>().ToList();
            var inputPorts = nodeView.inputContainer.Query<Port>().ToList();
            var visualEdgesToRemove = outputPorts.SelectMany(p => p.connections)
                .Concat(inputPorts.SelectMany(p => p.connections))
                .Distinct().ToList();

            foreach (var edge in visualEdgesToRemove) RemoveElement(edge);

            // 2. Rebuild the visual ports
            nodeView.RebuildPorts();

            // 3. Re-link visual edges using persistent data
            var allNodeViews = graphElements.OfType<NarrativeNodeView>().ToDictionary(v => v.nodeData.Guid);
            var edgesToRemoveData = new List<EdgeData>();

            foreach (var edgeData in currentGraph.Edges)
            {
                // Only process edges related to this node for visual recovery
                if (edgeData.BaseNodeGuid == nodeView.nodeData.Guid || edgeData.TargetNodeGuid == nodeView.nodeData.Guid)
                {
                    if (allNodeViews.TryGetValue(edgeData.BaseNodeGuid, out var srcView) &&
                        allNodeViews.TryGetValue(edgeData.TargetNodeGuid, out var destView))
                    {
                        var srcPorts = srcView.outputContainer.Query<Port>().ToList();
                        var destPorts = destView.inputContainer.Query<Port>().ToList();

                        if (edgeData.OutputPortIndex < srcPorts.Count && destPorts.Count > 0)
                        {
                            AddElement(srcPorts[edgeData.OutputPortIndex].ConnectTo(destPorts[0]));
                        }
                        else
                        {
                            // Port index no longer exists (e.g. branch was deleted)
                            edgesToRemoveData.Add(edgeData);
                        }
                    }
                }
            }

            // 4. Cleanup data for truly invalid edges
            if (edgesToRemoveData.Count > 0)
            {
                Undo.RecordObject(currentGraph, "Cleanup Invalid Edges");
                foreach (var ed in edgesToRemoveData) currentGraph.Edges.Remove(ed);
                EditorUtility.SetDirty(currentGraph);
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (change.elementsToRemove != null)
            {
                foreach (var element in change.elementsToRemove)
                {
                    if (element is NarrativeNodeView nodeView)
                    {
                        if (currentGraph != null)
                        {
                            Undo.RecordObject(currentGraph, "Remove Node From Graph");
                            currentGraph.Nodes.Remove(nodeView.nodeData);
                            
                            currentGraph.Edges.RemoveAll(e => e.BaseNodeGuid == nodeView.nodeData.Guid || e.TargetNodeGuid == nodeView.nodeData.Guid);

                            Undo.DestroyObjectImmediate(nodeView.nodeData);
                            AssetDatabase.SaveAssets();
                        }
                        
                        if (window != null)
                        {
                            window.OnNodeSelectionChanged(null);
                        }
                    }
                    else if (element is Edge edge)
                    {
                        if (currentGraph != null && edge.output.node is NarrativeNodeView outNode && edge.input.node is NarrativeNodeView inNode)
                        {
                            Undo.RecordObject(currentGraph, "Remove Edge");
                            // 같은 두 노드 사이에 포트가 다른 연결이 여럿일 수 있으므로 포트 인덱스까지 맞춰 지운다.
                            int portIndex = outNode.outputContainer.Query<Port>().ToList().IndexOf(edge.output);
                            int index = currentGraph.Edges.FindIndex(e => e.BaseNodeGuid == outNode.nodeData.Guid
                                && e.TargetNodeGuid == inNode.nodeData.Guid
                                && e.OutputPortIndex == portIndex);
                            if (index >= 0) currentGraph.Edges.RemoveAt(index);
                            EditorUtility.SetDirty(currentGraph);
                        }
                    }
                }
            }

            if (change.edgesToCreate != null)
            {
                var edgesToRemove = new List<Edge>();
                foreach (var edge in change.edgesToCreate)
                {
                    if (currentGraph != null && edge.output.node is NarrativeNodeView outNode && edge.input.node is NarrativeNodeView inNode)
                    {
                        Undo.RecordObject(currentGraph, "Add Edge");
                        
                        // IMPORTANT: Get the correct index from the Query list, NOT just the container child index
                        var allOutputPorts = outNode.outputContainer.Query<Port>().ToList();
                        int outputIndex = allOutputPorts.IndexOf(edge.output as Port);
                        
                        currentGraph.Edges.Add(new EdgeData
                        {
                            BaseNodeGuid = outNode.nodeData.Guid,
                            TargetNodeGuid = inNode.nodeData.Guid,
                            OutputPortIndex = outputIndex >= 0 ? outputIndex : 0
                        });
                        EditorUtility.SetDirty(currentGraph);
                    }
                }
                
                foreach (var edge in edgesToRemove)
                {
                    change.edgesToCreate.Remove(edge);
                }
            }

            bool structureChanged =
                (change.elementsToRemove?.Any(e => e is NarrativeNodeView || e is Edge) ?? false) ||
                (change.edgesToCreate?.Count > 0);
            if (structureChanged && currentGraph != null)
            {
                NarrativeNodeIdAssigner.RegenerateIds(currentGraph);
                ValidateAllNodes();
            }

            return change;
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatiblePorts = new List<Port>();
            ports.ForEach(port =>
            {
                if (startPort != port && startPort.node != port.node && startPort.direction != port.direction)
                {
                    compatiblePorts.Add(port);
                }
            });
            return compatiblePorts;
        }
    }
}