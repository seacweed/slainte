using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
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
            
            var elements = graphElements.ToList();
            foreach (var elem in elements)
            {
                RemoveElement(elem);
            }

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
                        var outputPort = baseNode.outputContainer[edgeData.OutputPortIndex] as Port;
                        var inputPort = targetNode.inputContainer[0] as Port;

                        var edge = outputPort.ConnectTo(inputPort);
                        AddElement(edge);
                    }
                }
            }
        }

        public void CreateNode(Type type, Vector2 position)
        {
            if (currentGraph == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select or create a Narrative Graph Asset first.", "OK");
                return;
            }

            var nodeData = ScriptableObject.CreateInstance(type) as NodeDataSO;
            nodeData.Guid = Guid.NewGuid().ToString();
            nodeData.name = nodeData.Guid;
            nodeData.Position = new Rect(position, new Vector2(150, 200));

            Undo.RegisterCreatedObjectUndo(nodeData, "Create Narrative Node");
            AssetDatabase.AddObjectToAsset(nodeData, currentGraph);
            
            Undo.RecordObject(currentGraph, "Add Node To Graph");
            currentGraph.Nodes.Add(nodeData);

            AssetDatabase.SaveAssets();

            var nodeView = new NarrativeNodeView(nodeData);
            AddElement(nodeView);
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
                        int outputIndex = outNode.outputContainer.IndexOf(edge.output);
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

            return change;
        }

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