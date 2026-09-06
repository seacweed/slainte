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
    // 추가·삭제할 때마다(OnGraphViewChanged) 영속 데이터에도 같은 변경을 반영하고, 순환 참조가
    // 생기는 연결은 IsCircular()로 걸러 거부한다.
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

        public void ValidateAllNodes()
        {
            var nodeViews = graphElements.OfType<NarrativeNodeView>().ToList();
            var titleCounts = nodeViews.GroupBy(v => v.title).ToDictionary(g => g.Key, g => g.Count());

            foreach (var v in nodeViews)
            {
                var errors = new List<string>();
                var fieldErrors = new Dictionary<string, string>();

                // 1. Global Title check
                if (!string.IsNullOrEmpty(v.title) && titleCounts[v.title] > 1) 
                {
                    errors.Add("Duplicate Node Title");
                    fieldErrors["title"] = "Title is already used by another node.";
                }

                // 2. Local Field check
                if (v.nodeData.CustomFields != null)
                {
                    var names = v.nodeData.CustomFields.Select(f => f.FieldName.ToLower()).ToList();
                    for (int i = 0; i < v.nodeData.CustomFields.Count; i++)
                    {
                        var name = v.nodeData.CustomFields[i].FieldName.ToLower();
                        if (names.Count(n => n == name) > 1)
                        {
                            fieldErrors[$"field_{i}"] = "Duplicate Field Name";
                            if (!errors.Contains("Duplicate Field Names")) errors.Add("Duplicate Field Names");
                        }
                    }
                }

                // 3. Local Branch check
                if (v.nodeData is EpisodeNodeSO ep && ep.OutgoingBranches != null)
                {
                    var branches = ep.OutgoingBranches.Select(b => b.ToLower()).ToList();
                    for (int i = 0; i < ep.OutgoingBranches.Count; i++)
                    {
                        var b = ep.OutgoingBranches[i].ToLower();
                        if (branches.Count(n => n == b) > 1)
                        {
                            fieldErrors[$"branch_{i}"] = "Duplicate Branch Name";
                            if (!errors.Contains("Duplicate Branch Names")) errors.Add("Duplicate Branch Names");
                        }
                    }
                }

                // 4. Trigger Condition check
                if (v.nodeData is TriggerNodeSO tr && tr.Conditions != null)
                {
                    for (int i = 0; i < tr.Conditions.Count; i++)
                    {
                        var c = tr.Conditions[i];
                        if (string.IsNullOrEmpty(c.Key)) { fieldErrors[$"cond_key_{i}"] = "Required"; errors.Add($"Condition {i} Key missing"); }
                        if (string.IsNullOrEmpty(c.Value)) { fieldErrors[$"cond_val_{i}"] = "Required"; errors.Add($"Condition {i} Value missing"); }
                    }
                }

                v.SetWarning(errors.Count > 0, string.Join("\n• ", errors), fieldErrors);
            }
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
                            var edgeData = currentGraph.Edges.FirstOrDefault(e => e.BaseNodeGuid == outNode.nodeData.Guid && e.TargetNodeGuid == inNode.nodeData.Guid);
                            currentGraph.Edges.Remove(edgeData);
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
                    if (IsCircular(edge.output.node, edge.input.node))
                    {
                        window.ShowNotification(new GUIContent("Circular dependency detected!"));
                        edgesToRemove.Add(edge);
                        continue;
                    }

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

        // startNode → targetNode로의 새 연결이 순환을 만드는지 확인한다. 순환 여부는
        // "targetNode에서 출발해 startNode에 도달할 수 있는가"와 동치이므로 역방향으로 탐색한다.
        private bool IsCircular(Node startNode, Node targetNode)
        {
            bool IsReachable(Node from, Node to)
            {
                var visited = new HashSet<string>();
                bool Search(Node cur)
                {
                    if (cur == to) return true;
                    if (visited.Contains(cur.viewDataKey)) return false;
                    visited.Add(cur.viewDataKey);
                    
                    var edges = cur.outputContainer.Query<Edge>().ToList();
                    foreach (var edge in edges)
                    {
                        if (edge.input?.node is Node next && Search(next)) return true;
                    }
                    return false;
                }
                return Search(from);
            }

            return IsReachable(targetNode, startNode);
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