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
            if (ep.Events.Count == 0)
            {
                _body.Add(NarrativeUIHelper.CreateLabel("(No Events)", "info-label").With(l => l.style.color = Color.gray));
                return;
            }

            foreach (var ev in ep.Events)
            {
                var card = new VisualElement();
                card.style.borderTopWidth = card.style.borderBottomWidth = card.style.borderLeftWidth = card.style.borderRightWidth = 1;
                card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
                card.style.borderTopLeftRadius = card.style.borderTopRightRadius = card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 3;
                card.style.marginBottom = 4;
                card.style.paddingTop = card.style.paddingBottom = card.style.paddingLeft = card.style.paddingRight = 4;

                switch (ev.Type)
                {
                    case EpisodeEventType.Dialogue:    AddDialogueInfo(card, ev);     break;
                    case EpisodeEventType.Choice:      AddChoiceInfo(card, ev);       break;
                    case EpisodeEventType.BusinessStart: AddBusinessStartInfo(card, ev); break;
                    case EpisodeEventType.BusinessEnd:
                        card.Add(NarrativeUIHelper.CreateLabel("CRAFTING END", "field-label").With(l => l.style.color = new Color(1f, 0.6f, 0.4f)));
                        break;
                    case EpisodeEventType.BranchExit:
                        var exitRow = NarrativeUIHelper.CreateRow();
                        exitRow.Add(NarrativeUIHelper.CreateLabel("EXIT →", "field-label").With(l => l.style.color = new Color(1f, 0.5f, 0.5f)));
                        if (!string.IsNullOrEmpty(ev.ExitBranchName))
                            exitRow.Add(NarrativeUIHelper.CreateLabel($" {ev.ExitBranchName}", "info-label").With(l => l.style.color = Color.white));
                        card.Add(exitRow);
                        break;
                }

                _body.Add(card);
            }
        }

        private static void AddDialogueInfo(VisualElement card, EpisodeEvent ev)
        {
            var header = NarrativeUIHelper.CreateRow();
            header.Add(NarrativeUIHelper.CreateLabel("DIALOGUE", "field-label").With(l => l.style.color = new Color(0.5f, 0.8f, 1f)));
            if (!string.IsNullOrEmpty(ev.SpeakerKey))
                header.Add(NarrativeUIHelper.CreateLabel($"  {ev.SpeakerKey}", "field-label").With(l => l.style.color = new Color(1f, 0.8f, 0.4f)));
            card.Add(header);

            string txt = ev.Text ?? "";
            if (!string.IsNullOrEmpty(txt))
            {
                var tl = NarrativeUIHelper.CreateLabel(txt.Length > 60 ? txt.Substring(0, 57) + "..." : txt, "info-label");
                tl.style.whiteSpace = WhiteSpace.Normal;
                tl.style.color = new Color(0.9f, 0.9f, 0.9f);
                card.Add(tl);
            }

            foreach (var c in ev.CharacterAppearances)
                card.Add(NarrativeUIHelper.CreateLabel($"[{c.CharacterKey}] {c.ExpressionKey}  s:{c.SlotIndex}", "info-label")
                    .With(l => l.style.color = new Color(0.6f, 1f, 0.6f)));

            if (ev.BgmCommand != BgmCommand.None)
                card.Add(NarrativeUIHelper.CreateLabel($"BGM {ev.BgmCommand} {ev.BgmClipName}", "info-label")
                    .With(l => l.style.color = new Color(1f, 0.7f, 1f)));
        }

        private static void AddChoiceInfo(VisualElement card, EpisodeEvent ev)
        {
            var header = NarrativeUIHelper.CreateRow();
            header.Add(NarrativeUIHelper.CreateLabel("CHOICE", "field-label").With(l => l.style.color = new Color(1f, 0.85f, 0.4f)));
            if (!string.IsNullOrEmpty(ev.SpeakerKey))
                header.Add(NarrativeUIHelper.CreateLabel($"  {ev.SpeakerKey}", "field-label").With(l => l.style.color = new Color(1f, 0.8f, 0.4f)));
            card.Add(header);

            string txt = ev.Text ?? "";
            if (!string.IsNullOrEmpty(txt))
            {
                var tl = NarrativeUIHelper.CreateLabel(txt.Length > 60 ? txt.Substring(0, 57) + "..." : txt, "info-label");
                tl.style.whiteSpace = WhiteSpace.Normal;
                tl.style.color = new Color(0.9f, 0.9f, 0.9f);
                card.Add(tl);
            }

            foreach (var c in ev.Choices)
            {
                card.Add(NarrativeUIHelper.CreateLabel($"> {(string.IsNullOrEmpty(c.ButtonText) ? "(empty)" : c.ButtonText)}", "info-label")
                    .With(l => l.style.color = Color.white));

                var parts = new List<string>();
                foreach (var f in c.SetFlags)   parts.Add($"+{f}");
                foreach (var f in c.ClearFlags) parts.Add($"-{f}");
                foreach (var v in c.VarChanges) parts.Add($"{v.VarName}{(v.Delta >= 0 ? "+" : "")}{v.Delta}");
                if (parts.Count > 0)
                {
                    var summary = NarrativeUIHelper.CreateLabel("  " + string.Join("  ", parts), "info-label");
                    summary.style.color = new Color(0.9f, 0.9f, 0.4f);
                    summary.style.whiteSpace = WhiteSpace.Normal;
                    card.Add(summary);
                }
            }
        }

        private static readonly Color GoodFlagColor = new(0.5f, 1f, 0.5f);
        private static readonly Color MidFlagColor  = new(1f, 0.8f, 0.4f);
        private static readonly Color BadFlagColor  = new(1f, 0.5f, 0.5f);

        private static void AddBusinessStartInfo(VisualElement card, EpisodeEvent ev)
        {
            var header = NarrativeUIHelper.CreateRow();
            header.Add(NarrativeUIHelper.CreateLabel("CRAFTING", "field-label").With(l => l.style.color = new Color(1f, 0.6f, 0.4f)));
            if (!string.IsNullOrEmpty(ev.CraftingTicketKey))
                header.Add(NarrativeUIHelper.CreateLabel($"  {ev.CraftingTicketKey}", "info-label").With(l => l.style.color = new Color(0.9f, 0.9f, 0.9f)));
            card.Add(header);

            foreach (var result in CraftingJobResultPorts.Order)
            {
                string flag = ev.GetCraftingFlag(result);
                if (string.IsNullOrEmpty(flag)) continue;

                Color color = result switch
                {
                    CraftingJobResult.Good => GoodFlagColor,
                    CraftingJobResult.Bad  => BadFlagColor,
                    _                       => MidFlagColor
                };
                card.Add(NarrativeUIHelper.CreateLabel($"[{CraftingJobResultPorts.Label(result)}] {flag}", "info-label").With(l => l.style.color = color));
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
                    row.Add(InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool)).With(p => { p.portName = ""; p.style.width = 20; }));
                    
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
                    row.Add(InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool)).With(p => { p.portName = ""; p.style.width = 20; }));
                    outputContainer.Add(row);
                }
            }
        }

        public override void SetPosition(Rect newPos) { base.SetPosition(newPos); Undo.RecordObject(nodeData, "Move"); nodeData.Position = newPos; EditorUtility.SetDirty(nodeData); }
        public override void OnSelected() { base.OnSelected(); GetFirstAncestorOfType<NarrativeGraphView>()?.window.OnNodeSelectionChanged(this); }
        public void RebuildPorts() => CreateOutputPorts();
    }
}
