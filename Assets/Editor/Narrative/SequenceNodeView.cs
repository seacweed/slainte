using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;

namespace NarrativeFlow.Editor
{
    public class SequenceNodeView : Node
    {
        public EpisodeEvent eventData;
        private EpisodeNodeSO _container;
        private SequenceGraphView _graph;
        private Label _summary;
        private Label _warningIcon;
        private Dictionary<string, string> _fieldErrors = new();
        public event System.Action OnValidationChanged;

        public SequenceNodeView(EpisodeEvent data, EpisodeNodeSO container, SequenceGraphView graph)
        {
            eventData = data;
            _container = container;
            _graph = graph;
            
            title = data.Type.ToString();
            viewDataKey = data.Guid;
            SetPosition(new Rect(data.Position, Vector2.zero));

            inputContainer.Add(InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool)).With(p => p.portName = "In"));
            
            capabilities &= ~Capabilities.Collapsible;

            SetupWarningIcon();

            _summary = NarrativeUIHelper.CreateLabel("").SetColor(Color.gray);
            NarrativeUIHelper.AddPadding(_summary, 5);
            extensionContainer.Add(_summary);

            RebuildPorts();
            UpdateVisuals();
            RefreshExpandedState();
        }

        private void SetupWarningIcon()
        {
            _warningIcon = new Label("⚠️") { 
                style = { 
                    color = new Color(1f, 0.3f, 0.3f), 
                    fontSize = 14, 
                    marginRight = 4,
                    marginLeft = 4,
                    display = DisplayStyle.None
                } 
            };
            titleContainer.Insert(0, _warningIcon);
        }

        public void ClearValidationEvents() => OnValidationChanged = null;

        public void SetWarning(bool show, string message = "", Dictionary<string, string> fieldErrors = null)
        {
            if (_warningIcon == null) return;
            
            _warningIcon.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show && !string.IsNullOrEmpty(message)) _warningIcon.tooltip = message;
            
            _fieldErrors = fieldErrors ?? new Dictionary<string, string>();
            
            // Re-draw node content to update red borders on ports
            UpdateVisuals();
            RebuildPorts(); 
            
            OnValidationChanged?.Invoke();
        }

        public string GetWarningMessage() => _warningIcon != null && _warningIcon.style.display == DisplayStyle.Flex ? _warningIcon.tooltip : "";
        public string GetFieldError(string key) => _fieldErrors.TryGetValue(key, out var msg) ? msg : null;

        public void RebuildPorts()
        {
            outputContainer.Clear();
            if (eventData.Type == EpisodeEventType.Choice)
            {
                for (int i = 0; i < eventData.Choices.Count; i++)
                {
                    var c = eventData.Choices[i];
                    var row = NarrativeUIHelper.CreateRow(justify: Justify.SpaceBetween);
                    row.Add(NarrativeUIHelper.CreateLabel(c.ButtonText, fontSize: 10).With(l => l.style.marginRight = 5));
                    row.Add(InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool)).With(p => { p.portName = ""; p.style.width = 16; }));
                    
                    var err = GetFieldError($"choice_{i}");
                    row.MarkError(err, !string.IsNullOrEmpty(err));
                    
                    outputContainer.Add(row);
                }
                outputContainer.Add(NarrativeUIHelper.CreateButton("+ Choice", () => {
                    Undo.RecordObject(_container, "Add Choice");
                    eventData.Choices.Add(new ChoiceOptionData { ButtonText = "New" });
                    _graph.NotifyInternalNodeStructureChanged(this);
                    _graph.NotifyMainGraph();
                }).With(b => { b.style.fontSize = 9; b.style.height = 16; b.style.marginTop = 5; }));
            }
            else
            {
                outputContainer.Add(InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool)).With(p => p.portName = "Out"));
            }
        }

        public void UpdateVisuals()
        {
            _summary.text = eventData.Type switch {
                EpisodeEventType.Dialogue => $"<b>{eventData.SpeakerKey}</b>\n{(eventData.Text?.Length > 40 ? eventData.Text.Substring(0, 37) + "..." : eventData.Text)}",
                EpisodeEventType.Choice => $"{eventData.Choices.Count} Options",
                EpisodeEventType.BusinessStart =>
                    $"{eventData.CraftingOrderType}: {eventData.CraftingOrderTarget}\nTicket: "
                    + $"{(eventData.CraftingOrderTicket != null ? eventData.CraftingOrderTicket.key : eventData.CraftingTicketKey)}",
                EpisodeEventType.BranchExit => $"EXIT -> <b>{eventData.ExitBranchName}</b>",
                _ => "Point"
            };
            if (eventData.Type == EpisodeEventType.BranchExit) _summary.style.color = new Color(1f, 0.5f, 0.5f);
        }

        public override void OnSelected() { base.OnSelected(); _graph.window?.OnSelectionChanged(this); }
        public override void OnUnselected() { base.OnUnselected(); _graph.window?.OnSelectionChanged(null); }
        public override void SetPosition(Rect newPos) { base.SetPosition(newPos); Undo.RecordObject(_container, "Move"); eventData.Position = newPos.position; }
    }
}
