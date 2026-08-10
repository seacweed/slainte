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
            Insert(0, new GridBackground().With(g => g.StretchToParentSize()));
            graphViewChanged += OnGraphViewChanged;
        }

        public void Populate(EpisodeNodeSO node)
        {
            _targetNode = node;
            graphElements.ToList().ForEach(RemoveElement);
            if (_targetNode == null) return;

            var views = _targetNode.Events.Select(ev => new SequenceNodeView(ev, _targetNode, this)).ToDictionary(v => v.eventData.Guid);
            views.Values.ToList().ForEach(AddElement);

            foreach (var view in views.Values) LinkNodeEdges(view, views);
            
            ValidateAllNodes();
        }

        public void ValidateAllNodes()
        {
            var nodeViews = graphElements.OfType<SequenceNodeView>().ToList();
            foreach (var v in nodeViews)
            {
                var errors = new List<string>();
                var fieldErrors = new Dictionary<string, string>();
                var ev = v.eventData;

                if (ev.Type == EpisodeEventType.Dialogue)
                {
                    if (string.IsNullOrEmpty(ev.Text)) { errors.Add("Empty Text"); fieldErrors["text"] = "Required"; }
                    if (string.IsNullOrEmpty(ev.SpeakerKey)) { errors.Add("Empty Speaker"); fieldErrors["speaker"] = "Required"; }
                }
                else if (ev.Type == EpisodeEventType.Choice)
                {
                    if (ev.Choices == null || ev.Choices.Count == 0) errors.Add("No Choices");
                    else
                    {
                        var texts = ev.Choices.Select(c => c.ButtonText.ToLower()).ToList();
                        for (int i = 0; i < ev.Choices.Count; i++)
                        {
                            if (texts.Count(t => t == ev.Choices[i].ButtonText.ToLower()) > 1)
                            {
                                fieldErrors[$"choice_{i}"] = "Duplicate Choice";
                                if (!errors.Contains("Duplicate Choices")) errors.Add("Duplicate Choices");
                            }
                        }
                    }
                }
                else if (ev.Type == EpisodeEventType.BusinessStart)
                {
                    if (string.IsNullOrWhiteSpace(ev.CraftingTicketKey))
                    {
                        errors.Add("Crafting Ticket Missing");
                        fieldErrors["ticket"] = "Required";
                    }
                    if (string.IsNullOrWhiteSpace(ev.CraftingRecipeId))
                    {
                        errors.Add("Crafting Recipe Missing");
                        fieldErrors["recipe"] = "Required";
                    }
                }
                else if (ev.Type == EpisodeEventType.BranchExit)
                {
                    if (string.IsNullOrEmpty(ev.ExitBranchName)) { errors.Add("Branch Not Selected"); fieldErrors["exit"] = "Required"; }
                }

                v.SetWarning(errors.Count > 0, string.Join("\n• ", errors), fieldErrors);
            }
        }

        public void LinkNodeEdges(SequenceNodeView src, Dictionary<string, SequenceNodeView> allViews)
        {
            if (src.eventData.Type == EpisodeEventType.Choice)
            {
                var ports = src.outputContainer.Query<Port>().ToList();
                for (int i = 0; i < src.eventData.Choices.Count; i++)
                {
                    var targetId = src.eventData.Choices[i].TargetNodeId;
                    if (i < ports.Count && !string.IsNullOrEmpty(targetId) && allViews.TryGetValue(targetId, out var dest))
                        AddElement(ports[i].ConnectTo(dest.inputContainer.Q<Port>()));
                }
            }
            else
            {
                src.eventData.NextEventGuids.ForEach(nextId => {
                    if (allViews.TryGetValue(nextId, out var dest))
                        AddElement(src.outputContainer.Q<Port>().ConnectTo(dest.inputContainer.Q<Port>()));
                });
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (change.edgesToCreate != null)
            {
                foreach (var edge in change.edgesToCreate)
                {
                    if (edge.output.node is SequenceNodeView src && edge.input.node is SequenceNodeView dest)
                    {
                        Undo.RecordObject(_targetNode, "Connect");
                        if (src.eventData.Type == EpisodeEventType.Choice)
                        {
                            int idx = src.outputContainer.Query<Port>().ToList().IndexOf(edge.output as Port);
                            if (idx >= 0 && idx < src.eventData.Choices.Count) src.eventData.Choices[idx].TargetNodeId = dest.eventData.Guid;
                        }
                        else if (!src.eventData.NextEventGuids.Contains(dest.eventData.Guid)) src.eventData.NextEventGuids.Add(dest.eventData.Guid);
                    }
                }
            }
            if (change.elementsToRemove != null)
            {
                foreach (var elem in change.elementsToRemove)
                {
                    if (elem is SequenceNodeView v)
                    {
                        Undo.RecordObject(_targetNode, "Remove");
                        _targetNode.Events.Remove(v.eventData);
                        _targetNode.Events.ForEach(o => { o.NextEventGuids.Remove(v.eventData.Guid); o.Choices.ForEach(c => { if (c.TargetNodeId == v.eventData.Guid) c.TargetNodeId = null; }); });
                    }
                    else if (elem is Edge e && e.output.node is SequenceNodeView s && e.input.node is SequenceNodeView d)
                    {
                        Undo.RecordObject(_targetNode, "Disconnect");
                        if (s.eventData.Type == EpisodeEventType.Choice)
                        {
                            int idx = s.outputContainer.Query<Port>().ToList().IndexOf(e.output as Port);
                            if (idx >= 0 && idx < s.eventData.Choices.Count) s.eventData.Choices[idx].TargetNodeId = null;
                        }
                        else s.eventData.NextEventGuids.Remove(d.eventData.Guid);
                    }
                }
            }
            ValidateAllNodes();
            return change;
        }

        public override List<Port> GetCompatiblePorts(Port p, NodeAdapter a) => ports.ToList().Where(x => x.direction != p.direction && x.node != p.node).ToList();
        
        public void CreateEvent(EpisodeEventType type, Vector2 pos)
        {
            if (_targetNode == null) return;
            Undo.RecordObject(_targetNode, "Create Event");
            var ev = new EpisodeEvent { Type = type, Position = pos };
            _targetNode.Events.Add(ev);
            AddElement(new SequenceNodeView(ev, _targetNode, this));
            ValidateAllNodes();
            NotifyMainGraph();
        }

        public void NotifyMainGraph()
        {
            if (window?.mainGraphView != null && window.mainGraphView.GetNodeByGuid(_targetNode.Guid) is NarrativeNodeView v)
                window.mainGraphView.NotifyNodeStructureChanged(v);
        }

        public void RefreshMainGraphVisuals()
        {
            if (_targetNode == null || window?.mainGraphView == null) return;
            if (window.mainGraphView.GetNodeByGuid(_targetNode.Guid) is NarrativeNodeView v)
                v.RefreshVisuals();
        }

        public void NotifyInternalNodeStructureChanged(SequenceNodeView v)
        {
            var ports = v.outputContainer.Query<Port>().ToList();
            ports.SelectMany(p => p.connections.ToList()).ToList().ForEach(RemoveElement);
            v.RebuildPorts();
            var allViews = graphElements.OfType<SequenceNodeView>().ToDictionary(nv => nv.eventData.Guid);
            LinkNodeEdges(v, allViews);
            ValidateAllNodes();
        }
    }
}
