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
        private VisualElement _customDataContainer;

        public NarrativeNodeView(NodeDataSO data)
        {
            nodeData = data;
            title = data.name;
            viewDataKey = data.Guid;

            style.left = data.Position.x;
            style.top = data.Position.y;

            // 1. Enable collapsing
            capabilities |= Capabilities.Collapsible;

            // 2. Build ports (These are in the 'top' container, so they stay visible)
            CreateInputPorts();
            CreateOutputPorts();

            // 3. Setup information container
            SetupVisuals();

            // 4. Initial content refresh
            RefreshVisuals();

            // 5. Double-click to open internal editor
            this.RegisterCallback<MouseDownEvent>(evt => {
                if (evt.clickCount == 2 && nodeData is EpisodeNodeSO epNode)
                {
                    evt.StopImmediatePropagation();
                    var graphView = GetFirstAncestorOfType<NarrativeGraphView>();
                    if (graphView != null)
                    {
                        graphView.ClearSelection();
                        EpisodeSequenceEditor.Open(epNode, graphView);
                    }
                    else
                    {
                        EpisodeSequenceEditor.Open(epNode, null);
                    }
                }
            });

            // 6. Force initial state
            expanded = true;
            RefreshExpandedState();
        }

        private void SetupVisuals()
        {
            // The extensionContainer is the standard GraphView area that hides when collapsed.
            _customDataContainer = new VisualElement();
            _customDataContainer.style.paddingLeft = 8;
            _customDataContainer.style.paddingRight = 8;
            _customDataContainer.style.paddingTop = 8;
            _customDataContainer.style.paddingBottom = 8;
            _customDataContainer.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.95f);
            
            // Add our data container to the extension container
            extensionContainer.Add(_customDataContainer);
        }

        public void RefreshVisuals()
        {
            _customDataContainer.Clear();
            
            string nodeTitle = "Node";
            if (nodeData is EpisodeNodeSO) nodeTitle = "Episode Block";
            else if (nodeData is TriggerNodeSO) nodeTitle = "Trigger Branch";
            else if (nodeData is EmptyNodeSO) nodeTitle = "Empty Node";

            if (nodeData.CustomFields != null)
            {
                var titleField = nodeData.CustomFields.Find(f => f.FieldName != null && f.FieldName.ToLower() == "title");
                if (titleField != null && !string.IsNullOrEmpty(titleField.FieldValue))
                {
                    nodeTitle = titleField.FieldValue;
                }

                // Show General Fields
                foreach (var field in nodeData.CustomFields)
                {
                    if (field.FieldName.ToLower() == "title") continue;

                    var fieldRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
                    fieldRow.Add(new Label($"{field.FieldName}: ") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11, color = new Color(0.85f, 0.85f, 0.85f) } });
                    fieldRow.Add(new Label(field.FieldValue) { style = { fontSize = 11, color = Color.white, flexGrow = 1, whiteSpace = WhiteSpace.Normal } });
                    _customDataContainer.Add(fieldRow);
                }
            }

            title = nodeTitle;

            // Divider
            if (_customDataContainer.childCount > 0)
            {
                var divider = new VisualElement { style = { height = 1, backgroundColor = new Color(0.4f, 0.4f, 0.4f), marginTop = 5, marginBottom = 8 } };
                _customDataContainer.Insert(0, divider);
            }

            // Episode Specific Info
            if (nodeData is EpisodeNodeSO epNode)
            {
                var eventCount = epNode.Events?.Count ?? 0;
                var countLabel = new Label($"{eventCount} Events");
                countLabel.style.color = new Color(0.4f, 0.7f, 1f);
                countLabel.style.fontSize = 11;
                countLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                countLabel.style.marginBottom = 4;
                _customDataContainer.Add(countLabel);
                
                if (eventCount > 0)
                {
                    var firstEvent = epNode.Events[0];
                    if (firstEvent.Type == EpisodeEventType.Dialogue)
                    {
                        string previewText = firstEvent.Text ?? "";
                        if (previewText.Length > 40) previewText = previewText.Substring(0, 37) + "...";
                        var previewLabel = new Label(previewText);
                        previewLabel.style.whiteSpace = WhiteSpace.Normal;
                        previewLabel.style.fontSize = 11;
                        previewLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
                        previewLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                        _customDataContainer.Add(previewLabel);
                    }
                }
            }

            // Trigger Specific Info
            if (nodeData is TriggerNodeSO triggerNode)
            {
                foreach (var cond in triggerNode.Conditions)
                {
                    var condLabel = new Label($"IF {cond.Key} {cond.Operator} {cond.Value}");
                    condLabel.style.fontSize = 11;
                    condLabel.style.color = new Color(1f, 0.85f, 0.5f);
                    _customDataContainer.Add(condLabel);
                }
            }
            
            // Re-apply state in case child count changed
            RefreshExpandedState();
        }

        private void CreateInputPorts()
        {
            var inputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            inputPort.portName = "Input";
            inputContainer.Add(inputPort);
        }

        private void CreateOutputPorts()
        {
            outputContainer.Clear();

            if (nodeData is EpisodeNodeSO epNode)
            {
                if (epNode.OutgoingBranches != null)
                {
                    for (int i = 0; i < epNode.OutgoingBranches.Count; i++)
                    {
                        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 2, marginLeft = 5 } };
                        row.Add(new Label(epNode.OutgoingBranches[i]) { style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft, fontSize = 11, marginRight = 5 } });
                        
                        var p = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
                        p.portName = ""; 
                        p.style.width = 20;
                        row.Add(p);
                        
                        outputContainer.Add(row);
                    }
                }
            }
            else if (nodeData is TriggerNodeSO triggerNode)
            {
                for (int i = 0; i < triggerNode.Conditions.Count; i++)
                {
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 2, marginLeft = 5 } };
                    row.Add(new Label($"Case {i}") { style = { flexGrow = 1, fontSize = 11 } });
                    
                    var p = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
                    p.portName = "";
                    row.Add(p);
                    outputContainer.Add(row);
                }
                
                var rowElse = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 2, marginLeft = 5 } };
                rowElse.Add(new Label("Else") { style = { flexGrow = 1, fontSize = 11, color = Color.gray } });
                var pElse = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
                pElse.portName = "";
                rowElse.Add(pElse);
                outputContainer.Add(rowElse);
            }
            else
            {
                var outputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                outputPort.portName = "Output";
                outputContainer.Add(outputPort);
            }
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            Undo.RecordObject(nodeData, "Move Node Position");
            nodeData.Position = newPos;
            EditorUtility.SetDirty(nodeData);
        }

        public override void OnSelected()
        {
            base.OnSelected();
            if (GetFirstAncestorOfType<NarrativeGraphView>() is NarrativeGraphView view)
            {
                view.window.OnNodeSelectionChanged(this);
            }
        }

        public void RebuildPorts()
        {
            CreateOutputPorts();
        }
    }
}
