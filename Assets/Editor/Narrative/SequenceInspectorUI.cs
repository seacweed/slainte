using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;

namespace NarrativeFlow.Editor
{
    public static class SequenceInspectorUI
    {
        public static void Draw(VisualElement container, SequenceNodeView nodeView, EpisodeNodeSO containerSO, SequenceGraphView graphView)
        {
            container.Clear();
            if (nodeView == null)
            {
                container.Add(new Label("Select an event node to edit its properties.") { style = { unityFontStyleAndWeight = FontStyle.Italic, color = Color.gray, marginTop = 20 } });
                return;
            }

            var ev = nodeView.eventData;

            // Header
            var header = new Label($"{ev.Type} Properties") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 16, marginBottom = 15 } };
            container.Add(header);

            // Dialogue UI
            if (ev.Type == EpisodeEventType.Dialogue)
            {
                var speaker = new TextField("Speaker Key") { value = ev.SpeakerKey };
                speaker.RegisterValueChangedCallback(e => {
                    Undo.RecordObject(containerSO, "Edit Speaker");
                    ev.SpeakerKey = e.newValue;
                    nodeView.UpdateVisuals();
                });
                container.Add(speaker);

                var overrideName = new TextField("Name Override") { value = ev.OverrideSpeakerName };
                overrideName.RegisterValueChangedCallback(e => {
                    Undo.RecordObject(containerSO, "Edit Name Override");
                    ev.OverrideSpeakerName = e.newValue;
                });
                container.Add(overrideName);

                var text = new TextField("Dialogue Text") { value = ev.Text, multiline = true };
                text.style.minHeight = 60;
                text.RegisterValueChangedCallback(e => {
                    Undo.RecordObject(containerSO, "Edit Dialogue Text");
                    ev.Text = e.newValue;
                    nodeView.UpdateVisuals();
                });
                container.Add(text);
            }
            // Choice UI
            else if (ev.Type == EpisodeEventType.Choice)
            {
                container.Add(new Label("Choice Options") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10, marginBottom = 5 } });
                
                var choiceList = new VisualElement();
                container.Add(choiceList);

                System.Action refreshChoices = null;
                refreshChoices = () => {
                    choiceList.Clear();
                    for (int i = 0; i < ev.Choices.Count; i++)
                    {
                        int index = i;
                        var c = ev.Choices[index];
                        var box = new Box { style = { paddingLeft = 5, paddingRight = 5, paddingTop = 5, paddingBottom = 5, marginBottom = 5, backgroundColor = new Color(0.25f, 0.25f, 0.25f) } };
                        
                        var headerRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween } };
                        headerRow.Add(new Label($"Choice {index}") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
                        headerRow.Add(new Button(() => {
                            Undo.RecordObject(containerSO, "Remove Choice");
                            ev.Choices.RemoveAt(index);
                            refreshChoices();
                            graphView.NotifyInternalNodeStructureChanged(nodeView); // Internal Rebuild
                            graphView.NotifyMainGraph(); // Main Rebuild
                        }) { text = "X" });
                        box.Add(headerRow);

                        var btnText = new TextField("Button Text") { value = c.ButtonText };
                        btnText.RegisterValueChangedCallback(e => {
                            Undo.RecordObject(containerSO, "Edit Choice Text");
                            c.ButtonText = e.newValue;
                            nodeView.RebuildPorts(); // Just text change, simple rebuild
                            graphView.NotifyMainGraph();
                        });
                        box.Add(btnText);

                        choiceList.Add(box);
                    }

                    var addBtn = new Button(() => {
                        Undo.RecordObject(containerSO, "Add Choice");
                        ev.Choices.Add(new ChoiceOptionData { ButtonText = "New Choice" });
                        refreshChoices();
                        graphView.NotifyInternalNodeStructureChanged(nodeView); // Internal Rebuild
                        graphView.NotifyMainGraph(); // Main Rebuild
                    }) { text = "+ Add Choice Option" };
                    choiceList.Add(addBtn);
                };
                refreshChoices();
            }
            // Business Start UI
            else if (ev.Type == EpisodeEventType.BusinessStart)
            {
                var ticket = new TextField("Crafting Ticket Key") { value = ev.CraftingTicketKey };
                ticket.RegisterValueChangedCallback(e => {
                    Undo.RecordObject(containerSO, "Edit Ticket Key");
                    ev.CraftingTicketKey = e.newValue;
                });
                container.Add(ticket);
            }
            // Branch Exit UI
            else if (ev.Type == EpisodeEventType.BranchExit)
            {
                container.Add(new Label("Select Output Branch") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } });
                
                var dropdown = new PopupField<string>("Exit Port", containerSO.OutgoingBranches, ev.ExitBranchName ?? (containerSO.OutgoingBranches.Count > 0 ? containerSO.OutgoingBranches[0] : ""));
                dropdown.RegisterValueChangedCallback(e => {
                    Undo.RecordObject(containerSO, "Change Exit Branch");
                    ev.ExitBranchName = e.newValue;
                    nodeView.UpdateVisuals();
                });
                container.Add(dropdown);
            }
            
            // Helpful Tip
            container.Add(new Label("Tip: Use the ports in the graph to connect dialogue flow.") 
            { 
                style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.5f, 0.7f, 1f), fontSize = 10, marginTop = 20, unityFontStyleAndWeight = FontStyle.Italic } 
            });
        }
    }
}
