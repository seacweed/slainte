using System.Collections.Generic;
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
                    container.Add(NarrativeUIHelper.CreateDivider());
                    DrawCharacterAppearances(container, ev, nodeView, epNode, graph);
                    container.Add(NarrativeUIHelper.CreateDivider());
                    container.Add(DrawBgmSection(ev, epNode, graph));
                    break;
                case EpisodeEventType.Choice:
                    container.Add(CreateField("Speaker", ev.SpeakerKey, "none", nodeView, v => ev.SpeakerKey = v, graph));
                    container.Add(CreateField("Name", ev.OverrideSpeakerName, "none", nodeView, v => ev.OverrideSpeakerName = v, graph));
                    container.Add(CreateField("Text", ev.Text, "none", nodeView, v => ev.Text = v, graph, true));
                    container.Add(NarrativeUIHelper.CreateDivider());
                    DrawChoices(container, ev, nodeView, epNode, graph);
                    break;
                case EpisodeEventType.BusinessStart:
                    container.Add(CreateField("Ticket", ev.CraftingTicketKey, "none", nodeView, v => ev.CraftingTicketKey = v, graph));
                    container.Add(NarrativeUIHelper.CreateDivider());
                    foreach (var result in CraftingJobResultPorts.Order)
                    {
                        string label = CraftingJobResultPorts.Label(result);
                        container.Add(CreateField($"Flag ({label})", ev.GetCraftingFlag(result), "none", nodeView, v => ev.SetCraftingFlag(result, v), graph));
                    }
                    foreach (var result in CraftingJobResultPorts.Order)
                    {
                        string label = CraftingJobResultPorts.Label(result);
                        DrawVarChangeList(container, $"Var Changes ({label})", ev.GetCraftingVarChanges(result), nodeView, epNode, graph);
                    }
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

        private static VisualElement DrawBgmSection(EpisodeEvent ev, EpisodeNodeSO epNode, SequenceGraphView graph)
        {
            var sec = new VisualElement();
            sec.Add(NarrativeUIHelper.CreateLabel("BGM", "field-label"));

            var cmdRow = NarrativeUIHelper.CreateRow();
            cmdRow.Add(NarrativeUIHelper.CreateLabel("Command", "field-label").With(l => l.style.width = 70));
            cmdRow.Add(new EnumField(ev.BgmCommand).SetFlex(1).With(x =>
                x.RegisterValueChangedCallback(e => { ev.BgmCommand = (BgmCommand)e.newValue; EditorUtility.SetDirty(epNode); graph.RefreshMainGraphVisuals(); })));
            sec.Add(cmdRow);

            var clipRow = NarrativeUIHelper.CreateRow();
            clipRow.Add(NarrativeUIHelper.CreateLabel("Clip", "field-label").With(l => l.style.width = 70));
            clipRow.Add(new TextField { value = ev.BgmClipName }.SetFlex(1).With(x =>
                x.RegisterValueChangedCallback(e => { ev.BgmClipName = e.newValue; EditorUtility.SetDirty(epNode); graph.RefreshMainGraphVisuals(); })));
            sec.Add(clipRow);

            return sec;
        }

        private static void DrawCharacterAppearances(VisualElement container, EpisodeEvent ev, SequenceNodeView nodeView, EpisodeNodeSO epNode, SequenceGraphView graph)
        {
            container.Add(NarrativeUIHelper.CreateLabel("Character Appearances", "field-label").SetMargin(4, 0));
            var sec = new VisualElement();
            container.Add(sec);
            System.Action refresh = null;
            refresh = () => NarrativeUIHelper.DrawList(sec, ev.CharacterAppearances, (c, entry, i) => {
                var box = new Box().AddClass("inspector-container").SetMargin(0, 3);

                // Character Key row
                var keyRow = NarrativeUIHelper.CreateRow();
                keyRow.Add(NarrativeUIHelper.CreateLabel("Key", "field-label").With(l => l.style.width = 44));
                keyRow.Add(new TextField { value = entry.CharacterKey }.SetFlex(1).With(x =>
                    x.RegisterValueChangedCallback(e => { entry.CharacterKey = e.newValue; EditorUtility.SetDirty(epNode); graph.RefreshMainGraphVisuals(); })));
                keyRow.Add(NarrativeUIHelper.CreateButton("X", () => { ev.CharacterAppearances.RemoveAt(i); refresh(); EditorUtility.SetDirty(epNode); graph.RefreshMainGraphVisuals(); }));
                box.Add(keyRow);

                // Expression Key row
                var exprRow = NarrativeUIHelper.CreateRow();
                exprRow.Add(NarrativeUIHelper.CreateLabel("Expr", "field-label").With(l => l.style.width = 44));
                exprRow.Add(new TextField { value = entry.ExpressionKey }.SetFlex(1).With(x =>
                    x.RegisterValueChangedCallback(e => { entry.ExpressionKey = e.newValue; EditorUtility.SetDirty(epNode); graph.RefreshMainGraphVisuals(); })));
                box.Add(exprRow);

                // Slot Index row
                var slotRow = NarrativeUIHelper.CreateRow();
                slotRow.Add(NarrativeUIHelper.CreateLabel("Slot", "field-label").With(l => l.style.width = 44));
                slotRow.Add(new IntegerField { value = entry.SlotIndex }.SetFlex(1).With(x =>
                    x.RegisterValueChangedCallback(e => { entry.SlotIndex = e.newValue; EditorUtility.SetDirty(epNode); graph.RefreshMainGraphVisuals(); })));
                box.Add(slotRow);

                c.Add(box);
            }, () => { ev.CharacterAppearances.Add(new CharacterSlotEntryData()); refresh(); EditorUtility.SetDirty(epNode); graph.RefreshMainGraphVisuals(); });
            refresh();
        }

        private static void DrawVarChangeList(VisualElement container, string sectionLabel, List<VarChangeData> list, SequenceNodeView view, EpisodeNodeSO epNode, SequenceGraphView g)
        {
            container.Add(NarrativeUIHelper.CreateLabel(sectionLabel, "field-label").SetMargin(6, 0));
            var sec = new VisualElement();
            container.Add(sec);
            System.Action refresh = null;
            refresh = () => NarrativeUIHelper.DrawList(sec, list, (c, item, i) => {
                var row = NarrativeUIHelper.CreateRow();
                row.Add(new TextField { value = item.VarName }.With(x => { x.style.width = 80; x.RegisterValueChangedCallback(e => { item.VarName = e.newValue; EditorUtility.SetDirty(epNode); }); }));
                row.Add(NarrativeUIHelper.CreateLabel("Δ", "field-label").With(l => l.style.width = 14));
                row.Add(new IntegerField { value = item.Delta }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { item.Delta = e.newValue; EditorUtility.SetDirty(epNode); })));
                row.Add(NarrativeUIHelper.CreateButton("X", () => { list.RemoveAt(i); refresh(); EditorUtility.SetDirty(epNode); }));
                c.Add(row);
            }, () => { list.Add(new VarChangeData()); refresh(); EditorUtility.SetDirty(epNode); });
            refresh();
        }

        private static VisualElement CreateField(string label, string val, string errKey, SequenceNodeView view, System.Action<string> setter, SequenceGraphView g, bool multi = false)
        {
            var row = NarrativeUIHelper.CreateRow();
            row.Add(NarrativeUIHelper.CreateLabel(label, "field-label"));
            row.Add(new TextField { value = val, multiline = multi }.SetFlex(1).With(x => {
                x.RegisterValueChangedCallback(e => { setter(e.newValue); view.UpdateVisuals(); g.ValidateAllNodes(); g.RefreshMainGraphVisuals(); });
            }));
            row.Add(NarrativeUIHelper.CreateWarningIcon(view, errKey));
            return row;
        }

        private static void DrawChoices(VisualElement container, EpisodeEvent ev, SequenceNodeView view, EpisodeNodeSO epNode, SequenceGraphView graph)
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

                var btnRow = NarrativeUIHelper.CreateRow();
                btnRow.Add(new TextField { value = choice.ButtonText }.SetFlex(1).With(x =>
                    x.RegisterValueChangedCallback(e => { choice.ButtonText = e.newValue; graph.ValidateAllNodes(); graph.RefreshMainGraphVisuals(); })));
                btnRow.Add(NarrativeUIHelper.CreateWarningIcon(view, $"choice_{i}"));
                box.Add(btnRow);

                DrawStringList(box, "Set Flags (+)", choice.SetFlags, epNode, graph);
                DrawStringList(box, "Clear Flags (-)", choice.ClearFlags, epNode, graph);
                DrawVarChangeList(box, "Var Changes", choice.VarChanges, view, epNode, graph);

                c.Add(box);
            }, () => { ev.Choices.Add(new ChoiceOptionData { ButtonText = "New" }); refresh(); graph.NotifyInternalNodeStructureChanged(view); graph.NotifyMainGraph(); });
            refresh();
        }

        private static void DrawStringList(VisualElement container, string label, List<string> list, EpisodeNodeSO epNode, SequenceGraphView graph)
        {
            container.Add(NarrativeUIHelper.CreateLabel(label, "field-label").SetMargin(4, 0));
            var sec = new VisualElement();
            container.Add(sec);
            System.Action refresh = null;
            refresh = () => NarrativeUIHelper.DrawList(sec, list, (c, item, i) => {
                var row = NarrativeUIHelper.CreateRow();
                row.Add(new TextField { value = item }.SetFlex(1).With(x =>
                    x.RegisterValueChangedCallback(e => { list[i] = e.newValue; EditorUtility.SetDirty(epNode); graph.RefreshMainGraphVisuals(); })));
                row.Add(NarrativeUIHelper.CreateButton("X", () => { list.RemoveAt(i); refresh(); EditorUtility.SetDirty(epNode); graph.RefreshMainGraphVisuals(); }));
                c.Add(row);
            }, () => { list.Add(""); refresh(); EditorUtility.SetDirty(epNode); });
            refresh();
        }
    }
}
