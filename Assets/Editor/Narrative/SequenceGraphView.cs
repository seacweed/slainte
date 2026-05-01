using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;

namespace NarrativeFlow.Editor
{
    public class SequenceGraphView : GraphView
    {
        private EpisodeNodeSO _targetNode;
        public EpisodeSequenceEditor window;

        public SequenceGraphView(EpisodeSequenceEditor window)
        {
            this.window = window;
            
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            graphViewChanged += OnGraphViewChanged;
        }

        public void Populate(EpisodeNodeSO node)
        {
            _targetNode = node;
            
            var elements = graphElements.ToList();
            foreach (var elem in elements) RemoveElement(elem);

            if (_targetNode == null) return;

            var nodeViews = new Dictionary<string, SequenceNodeView>();

            foreach (var ev in _targetNode.Events)
            {
                var nodeView = new SequenceNodeView(ev, _targetNode, this);
                AddElement(nodeView);
                nodeViews[ev.Guid] = nodeView;
            }

            foreach (var ev in _targetNode.Events)
            {
                if (nodeViews.TryGetValue(ev.Guid, out var sourceView))
                {
                    if (ev.Type == EpisodeEventType.Choice)
                    {
                        var choicePorts = sourceView.outputContainer.Query<Port>().ToList();
                        for (int i = 0; i < ev.Choices.Count; i++)
                        {
                            if (i < choicePorts.Count && !string.IsNullOrEmpty(ev.Choices[i].TargetNodeId))
                            {
                                if (nodeViews.TryGetValue(ev.Choices[i].TargetNodeId, out var targetView))
                                {
                                    var edge = choicePorts[i].ConnectTo(targetView.inputContainer.Q<Port>());
                                    AddElement(edge);
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (var nextGuid in ev.NextEventGuids)
                        {
                            if (nodeViews.TryGetValue(nextGuid, out var targetView))
                            {
                                var outPort = sourceView.outputContainer.Q<Port>();
                                var inPort = targetView.inputContainer.Q<Port>();
                                var edge = outPort.ConnectTo(inPort);
                                AddElement(edge);
                            }
                        }
                    }
                }
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (change.edgesToCreate != null)
            {
                foreach (var edge in change.edgesToCreate)
                {
                    var source = edge.output.node as SequenceNodeView;
                    var target = edge.input.node as SequenceNodeView;
                    if (source != null && target != null)
                    {
                        Undo.RecordObject(_targetNode, "Connect Events");
                        
                        if (source.eventData.Type == EpisodeEventType.Choice)
                        {
                            // Map specific choice port to TargetNodeId
                            var allOutputPorts = source.outputContainer.Query<Port>().ToList();
                            int portIndex = allOutputPorts.IndexOf(edge.output as Port);
                            if (portIndex >= 0 && portIndex < source.eventData.Choices.Count)
                            {
                                source.eventData.Choices[portIndex].TargetNodeId = target.eventData.Guid;
                            }
                        }
                        else
                        {
                            // Default flow
                            if (!source.eventData.NextEventGuids.Contains(target.eventData.Guid))
                            {
                                source.eventData.NextEventGuids.Add(target.eventData.Guid);
                            }
                        }
                    }
                }
            }

            if (change.elementsToRemove != null)
            {
                foreach (var elem in change.elementsToRemove)
                {
                    if (elem is SequenceNodeView nodeView)
                    {
                        Undo.RecordObject(_targetNode, "Remove Event");
                        _targetNode.Events.Remove(nodeView.eventData);
                        foreach (var other in _targetNode.Events)
                        {
                            other.NextEventGuids.Remove(nodeView.eventData.Guid);
                            foreach (var c in other.Choices) if (c.TargetNodeId == nodeView.eventData.Guid) c.TargetNodeId = null;
                        }
                    }
                    else if (elem is Edge edge)
                    {
                        var source = edge.output.node as SequenceNodeView;
                        var target = edge.input.node as SequenceNodeView;
                        if (source != null && target != null)
                        {
                            Undo.RecordObject(_targetNode, "Disconnect Events");
                            if (source.eventData.Type == EpisodeEventType.Choice)
                            {
                                var allOutputPorts = source.outputContainer.Query<Port>().ToList();
                                int portIndex = allOutputPorts.IndexOf(edge.output as Port);
                                if (portIndex >= 0 && portIndex < source.eventData.Choices.Count)
                                {
                                    source.eventData.Choices[portIndex].TargetNodeId = null;
                                }
                            }
                            else
                            {
                                source.eventData.NextEventGuids.Remove(target.eventData.Guid);
                            }
                        }
                    }
                }
            }

            return change;
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return ports.ToList().Where(p => p.direction != startPort.direction && p.node != startPort.node).ToList();
        }

        public void CreateEvent(EpisodeEventType type, Vector2 position)
        {
            if (_targetNode == null) return;
            
            Undo.RecordObject(_targetNode, "Create Inner Event");
            var newEv = new EpisodeEvent { Type = type, Position = position };
            _targetNode.Events.Add(newEv);
            
            var nodeView = new SequenceNodeView(newEv, _targetNode, this);
            AddElement(nodeView);

            NotifyMainGraph();
        }

        public void NotifyMainGraph()
        {
            if (window != null && window.mainGraphView != null)
            {
                var mainNodeView = window.mainGraphView.GetNodeByGuid(_targetNode.Guid) as NarrativeNodeView;
                if (mainNodeView != null)
                {
                    window.mainGraphView.NotifyNodeStructureChanged(mainNodeView);
                }
            }
        }


        // New Helper for internal port rebuilding
        public void NotifyInternalNodeStructureChanged(SequenceNodeView nodeView)
        {
            // Similar logic to main graph: clear invalid edges when ports change
            var ports = nodeView.outputContainer.Query<Port>().ToList();
            var visualEdgesToRemove = new List<Edge>();

            foreach (var port in ports)
            {
                var connections = port.connections.ToList();
                foreach (var edge in connections)
                {
                    visualEdgesToRemove.Add(edge);
                    
                    int portIndex = ports.IndexOf(port);
                    if (nodeView.eventData.Type == EpisodeEventType.Choice)
                    {
                        if (portIndex >= 0 && portIndex < nodeView.eventData.Choices.Count)
                            nodeView.eventData.Choices[portIndex].TargetNodeId = null;
                    }
                    else
                    {
                        // For non-choice nodes, we only have one logical flow usually
                        nodeView.eventData.NextEventGuids.Clear();
                    }
                }
            }

            foreach (var edge in visualEdgesToRemove) RemoveElement(edge);
            
            nodeView.RebuildPorts();
        }
    }

    public class SequenceNodeView : Node
    {
        public EpisodeEvent eventData;
        private EpisodeNodeSO _container;
        private SequenceGraphView _graph;
        private Label _summaryLabel;

        public SequenceNodeView(EpisodeEvent data, EpisodeNodeSO container, SequenceGraphView graph)
        {
            eventData = data;
            _container = container;
            _graph = graph;
            
            title = data.Type.ToString();
            viewDataKey = data.Guid;
            style.left = data.Position.x;
            style.top = data.Position.y;

            var outPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            outPort.portName = "Out";
            outputContainer.Add(outPort);

            // Disable Collapsible for internal sequence nodes as requested
            capabilities &= ~Capabilities.Collapsible;

            _summaryLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, fontSize = 11, color = Color.gray, maxWidth = 150, paddingLeft = 5, paddingRight = 5, paddingTop = 5, paddingBottom = 5 } };

            extensionContainer.Add(_summaryLabel);
            
            RebuildPorts();
            UpdateVisuals();
            RefreshExpandedState();
        }

        public void RebuildPorts()
        {
            outputContainer.Clear();

            if (eventData.Type == EpisodeEventType.Choice)
            {
                for (int i = 0; i < eventData.Choices.Count; i++)
                {
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 2 } };
                    row.Add(new Label(eventData.Choices[i].ButtonText) { style = { flexGrow = 1, fontSize = 10, marginRight = 5 } });
                    
                    var p = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
                    p.portName = "";
                    p.style.width = 16;
                    row.Add(p);
                    outputContainer.Add(row);
                }

                // Restore "+ Choice" button on the node itself for convenience
                var addBtn = new Button(() => {
                    Undo.RecordObject(_container, "Add Choice");
                    eventData.Choices.Add(new ChoiceOptionData { ButtonText = "New Choice" });
                    _graph.NotifyInternalNodeStructureChanged(this);
                    _graph.NotifyMainGraph();
                }) { text = "+ Choice", style = { fontSize = 9, height = 16, marginTop = 5 } };
                outputContainer.Add(addBtn);
            }
            else
            {
                var outPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                outPort.portName = "Out";
                outputContainer.Add(outPort);
            }
        }

        public void UpdateVisuals()
        {
            if (eventData.Type == EpisodeEventType.Dialogue)
            {
                string preview = string.IsNullOrEmpty(eventData.Text) ? "(Empty Dialogue)" : eventData.Text;
                if (preview.Length > 40) preview = preview.Substring(0, 37) + "...";
                _summaryLabel.text = $"<b>{eventData.SpeakerKey}</b>\n{preview}";
            }
            else if (eventData.Type == EpisodeEventType.Choice)
            {
                _summaryLabel.text = $"{eventData.Choices.Count} Choice Options";
            }
            else if (eventData.Type == EpisodeEventType.BusinessStart)
            {
                _summaryLabel.text = $"Ticket: {eventData.CraftingTicketKey}";
            }
            else if (eventData.Type == EpisodeEventType.BranchExit)
            {
                _summaryLabel.text = $"EXIT -> <b>{eventData.ExitBranchName ?? "NOT SET"}</b>";
                _summaryLabel.style.color = new Color(1f, 0.5f, 0.5f);
            }
            else
            {
                _summaryLabel.text = "Sequence Point";
            }
        }

        public override void OnSelected()
        {
            base.OnSelected();
            if (_graph.window != null)
            {
                _graph.window.OnSelectionChanged(this);
            }
        }

        public override void OnUnselected()
        {
            base.OnUnselected();
            if (_graph.window != null)
            {
                _graph.window.OnSelectionChanged(null);
            }
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            Undo.RecordObject(_container, "Move Event");
            eventData.Position = newPos.position;
        }
    }
}
