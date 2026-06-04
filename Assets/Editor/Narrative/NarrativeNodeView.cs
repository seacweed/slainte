using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
    public class NarrativeNodeView : Node
    {
        public NodeDataSO nodeData;
        private VisualElement _body;
        private Label _warningIcon;
        private Dictionary<string, string> _fieldErrors = new();
        public event System.Action OnValidationChanged;

        public NarrativeNodeView(NodeDataSO data)
        {
            nodeData = data;
            title = data.name;
            viewDataKey = data.Guid;
            SetPosition(data.Position);
            this.styleSheets.Add(NarrativeUIHelper.LoadStyle());

            capabilities |= Capabilities.Collapsible;

            SetupTitleBar();
            CreateInputPorts();
            CreateOutputPorts();
            
            _body = new VisualElement().AddClass("node-body");
            extensionContainer.Add(_body);

            RefreshVisuals();
            RegisterEvents();

            expanded = true;
            RefreshExpandedState();
        }

        private void SetupTitleBar()
        {
            // Create a fixed spacer for the icon so it doesn't overlap text
            _warningIcon = new Label("⚠️") { 
                style = { 
                    color = new Color(1f, 0.3f, 0.3f),
                    fontSize = 14, 
                    marginRight = 4,
                    marginLeft = 4,
                    display = DisplayStyle.None // Hidden by default
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
            
            // Re-draw visuals with latest error data
            RefreshVisuals();
            OnValidationChanged?.Invoke();
        }

        public string GetWarningMessage() => _warningIcon != null && _warningIcon.style.display == DisplayStyle.Flex ? _warningIcon.tooltip : "";
        public string GetFieldError(string key) => _fieldErrors.TryGetValue(key, out var msg) ? msg : null;

        private void RegisterEvents()
        {
            this.RegisterCallback<MouseDownEvent>(evt => {
                if (evt.clickCount == 2 && nodeData is EpisodeNodeSO epNode)
                {
                    evt.StopImmediatePropagation();
                    EpisodeSequenceEditor.Open(epNode, GetFirstAncestorOfType<NarrativeGraphView>());
                }
            });
            var cb = titleButtonContainer.Q<VisualElement>("collapse-button");
            if (cb != null) cb.style.display = DisplayStyle.Flex;
        }

        public void RefreshVisuals()
        {
            _body.Clear();
            string nodeTitle = nodeData is EpisodeNodeSO ? "Episode" : nodeData is TriggerNodeSO ? "Trigger" : "Node";

            if (nodeData.CustomFields != null)
            {
                for (int i = 0; i < nodeData.CustomFields.Count; i++)
                {
                    var field = nodeData.CustomFields[i];
                    if (field.FieldName.ToLower() == "title") { if (!string.IsNullOrEmpty(field.FieldValue)) nodeTitle = field.FieldValue; continue; }
                    
                    var row = NarrativeUIHelper.CreateRow();
                    row.Add(NarrativeUIHelper.CreateLabel($"{field.FieldName}: ", "field-label"));
                    row.Add(NarrativeUIHelper.CreateLabel(field.FieldValue, "field-value"));
                    
                    // Apply error border only if validation actually failed for this field
                    var err = GetFieldError($"field_{i}");
                    row.MarkError(err, !string.IsNullOrEmpty(err));
                    
                    _body.Add(row);
                }
            }

            title = nodeTitle;
            if (_body.childCount > 0) _body.Insert(0, NarrativeUIHelper.CreateDivider());

            if (nodeData is EpisodeNodeSO ep) DrawEpisode(ep);
            else if (nodeData is TriggerNodeSO tr) DrawTrigger(tr);
            
            RefreshExpandedState();
        }

        private void DrawEpisode(EpisodeNodeSO ep)
        {
            _body.Add(NarrativeUIHelper.CreateLabel($"{ep.Events.Count} Events", "field-label").With(l => l.style.color = new Color(0.4f, 0.7f, 1f)));
            if (ep.Events.Count > 0 && ep.Events[0].Type == EpisodeEventType.Dialogue)
            {
                string txt = ep.Events[0].Text ?? "";
                _body.Add(NarrativeUIHelper.CreateLabel(txt.Length > 40 ? txt.Substring(0, 37) + "..." : txt, "info-label"));
            }
        }

        private void DrawTrigger(TriggerNodeSO tr)
        {
            foreach (var c in tr.Conditions)
                _body.Add(NarrativeUIHelper.CreateLabel($"IF {c.Key} {c.Operator} {c.Value}").With(l => l.style.color = new Color(1f, 0.85f, 0.5f)));
        }

        private void CreateInputPorts() => inputContainer.Add(InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool)).With(p => p.portName = "In"));

        private void CreateOutputPorts()
        {
            outputContainer.Clear();
            if (nodeData is EpisodeNodeSO ep)
            {
                for (int i = 0; i < ep.OutgoingBranches.Count; i++)
                {
                    var row = NarrativeUIHelper.CreateRow("choice-row");
                    row.Add(NarrativeUIHelper.CreateLabel(ep.OutgoingBranches[i], "field-value"));
                    row.Add(InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool)).With(p => { 
                        p.portName = ""; 
                        p.style.width = 20; 
                        p.Query<Label>().ForEach(l => l.style.display = DisplayStyle.None); // Hide default port labels/colons
                    }));
                    
                    var err = GetFieldError($"branch_{i}");
                    row.MarkError(err, !string.IsNullOrEmpty(err));
                    
                    outputContainer.Add(row);
                }
            }
            else if (nodeData is TriggerNodeSO tr)
            {
                for (int i = 0; i <= tr.Conditions.Count; i++)
                {
                    var row = NarrativeUIHelper.CreateRow("choice-row");
                    row.Add(NarrativeUIHelper.CreateLabel(i < tr.Conditions.Count ? $"Case {i}" : "Else", "field-value").With(l => { if (i >= tr.Conditions.Count) l.style.color = Color.gray; }));
                    row.Add(InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool)).With(p => { 
                        p.portName = ""; 
                        p.style.width = 20; 
                        p.Query<Label>().ForEach(l => l.style.display = DisplayStyle.None); // Hide default port labels/colons
                    }));
                    outputContainer.Add(row);
                }
            }
        }

        public override void SetPosition(Rect newPos) { base.SetPosition(newPos); Undo.RecordObject(nodeData, "Move"); nodeData.Position = newPos; EditorUtility.SetDirty(nodeData); }
        public override void OnSelected() { base.OnSelected(); GetFirstAncestorOfType<NarrativeGraphView>()?.window.OnNodeSelectionChanged(this); }
        public void RebuildPorts() => CreateOutputPorts();
    }
}
