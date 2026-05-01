using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace NarrativeFlow.Editor
{
    public static class SequenceInspectorUI
    {
        public static void Draw(VisualElement container, SequenceNodeView nodeView, EpisodeNodeSO epNode, SequenceGraphView graph)
        {
            container.Clear();
            container.styleSheets.Add(NarrativeUIHelper.LoadStyle());
            if (nodeView == null) { container.Add(NarrativeUIHelper.CreateLabel("Select an event node.", "info-label").SetMargin(20, 0)); return; }

            nodeView.ClearValidationEvents();
            var ev = nodeView.eventData;

            // Warning Header
            var warningBox = new HelpBox("", HelpBoxMessageType.Error).With(x => { x.style.display = DisplayStyle.None; x.style.marginBottom = 10; });
            container.Add(warningBox);
            System.Action updateHeader = () => { var msg = nodeView.GetWarningMessage(); warningBox.text = msg; warningBox.style.display = string.IsNullOrEmpty(msg) ? DisplayStyle.None : DisplayStyle.Flex; };
            nodeView.OnValidationChanged += updateHeader;
            updateHeader();

            container.Add(NarrativeUIHelper.CreateLabel($"{ev.Type} Properties", "section-header"));

            switch (ev.Type)
            {
                case EpisodeEventType.Dialogue:
                    container.Add(CreateField("Speaker", ev.SpeakerKey, "speaker", nodeView, v => ev.SpeakerKey = v, graph));
                    container.Add(CreateField("Name", ev.OverrideSpeakerName, "none", nodeView, v => ev.OverrideSpeakerName = v, graph));
                    container.Add(CreateField("Text", ev.Text, "text", nodeView, v => ev.Text = v, graph, true));
                    break;
                case EpisodeEventType.Choice:
                    DrawChoices(container, ev, nodeView, graph);
                    break;
                case EpisodeEventType.BusinessStart:
                    container.Add(CreateField("Ticket", ev.CraftingTicketKey, "none", nodeView, v => ev.CraftingTicketKey = v, graph));
                    break;
                case EpisodeEventType.BranchExit:
                    container.Add(NarrativeUIHelper.CreateLabel("Select Output Branch", "field-label"));
                    var row = NarrativeUIHelper.CreateRow();
                    row.Add(new PopupField<string>(epNode.OutgoingBranches, ev.ExitBranchName ?? "").SetFlex(1).With(x => {
                        x.RegisterValueChangedCallback(e => { ev.ExitBranchName = e.newValue; nodeView.SetWarning(false); graph.ValidateAllNodes(); });
                    }));
                    row.Add(NarrativeUIHelper.CreateWarningIcon(nodeView, "exit"));
                    container.Add(row);
                    break;
            }

            container.Add(NarrativeUIHelper.CreateLabel("Tip: Use ports to connect flow.", "info-label").SetMargin(20, 0));
        }

        private static VisualElement CreateField(string label, string val, string errKey, SequenceNodeView view, System.Action<string> setter, SequenceGraphView g, bool multi = false)
        {
            var row = NarrativeUIHelper.CreateRow();
            row.Add(NarrativeUIHelper.CreateLabel(label, "field-label"));
            row.Add(new TextField { value = val, multiline = multi }.SetFlex(1).With(x => {
                x.RegisterValueChangedCallback(e => { setter(e.newValue); g.ValidateAllNodes(); });
            }));
            row.Add(NarrativeUIHelper.CreateWarningIcon(view, errKey));
            return row;
        }

        private static void DrawChoices(VisualElement container, EpisodeEvent ev, SequenceNodeView view, SequenceGraphView graph)
        {
            var sec = new VisualElement();
            container.Add(NarrativeUIHelper.CreateLabel("Choice Options", "field-label"));
            container.Add(sec);
            System.Action refresh = null;
            refresh = () => NarrativeUIHelper.DrawList(sec, ev.Choices, (c, choice, i) => {
                var box = new Box().AddClass("inspector-container").SetMargin(0, 5);
                var head = NarrativeUIHelper.CreateRow("choice-row");
                head.Add(NarrativeUIHelper.CreateLabel($"Choice {i}", "field-label"));
                head.Add(NarrativeUIHelper.CreateButton("X", () => { ev.Choices.RemoveAt(i); refresh(); graph.NotifyInternalNodeStructureChanged(view); graph.NotifyMainGraph(); }));
                box.Add(head);

                var row = NarrativeUIHelper.CreateRow();
                row.Add(new TextField { value = choice.ButtonText }.SetFlex(1).With(x => {
                    x.RegisterValueChangedCallback(e => { choice.ButtonText = e.newValue; graph.ValidateAllNodes(); });
                }));
                row.Add(NarrativeUIHelper.CreateWarningIcon(view, $"choice_{i}"));
                box.Add(row);
                c.Add(box);
            }, () => { ev.Choices.Add(new ChoiceOptionData { ButtonText = "New" }); refresh(); graph.NotifyInternalNodeStructureChanged(view); graph.NotifyMainGraph(); });
            refresh();
        }
    }
}
