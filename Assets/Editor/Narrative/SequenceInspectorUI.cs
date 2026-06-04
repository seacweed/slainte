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
                    container.Add(CreateField("Speaker", ev.SpeakerKey, "speaker", nodeView, epNode, v => ev.SpeakerKey = v, graph));
                    container.Add(CreateField("Name", ev.OverrideSpeakerName, "none", nodeView, epNode, v => ev.OverrideSpeakerName = v, graph));
                    container.Add(CreateField("Text", ev.Text, "text", nodeView, epNode, v => ev.Text = v, graph, true));
                    
                    // BGM Command & Clip
                    var bgmRow = NarrativeUIHelper.CreateRow();
                    bgmRow.Add(NarrativeUIHelper.CreateLabel("BGM Cmd", "field-label"));
                    bgmRow.Add(new EnumField(ev.BgmCommand).SetFlex(1).With(x => {
                        x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "BGM Cmd"); ev.BgmCommand = (BgmCommand)e.newValue; EditorUtility.SetDirty(epNode); });
                    }));
                    container.Add(bgmRow);
                    
                    container.Add(CreateField("BGM Clip", ev.BgmClipName, "none", nodeView, epNode, v => ev.BgmClipName = v, graph));

                    // Character Appearances (List<CharacterSlotEntryData>)
                    var charSec = new VisualElement().AddClass("inspector-container").SetMargin(5, 5);
                    charSec.Add(NarrativeUIHelper.CreateLabel("Character Appearances", fontSize: 11).With(l => l.style.unityFontStyleAndWeight = FontStyle.Bold));
                    var charListContainer = new VisualElement();
                    charSec.Add(charListContainer);
                    container.Add(charSec);
                    
                    System.Action refreshChars = null;
                    refreshChars = () => NarrativeUIHelper.DrawList(charListContainer, ev.CharacterAppearances, (c, entry, i) => {
                        var box = new Box().AddClass("inspector-container").SetMargin(0, 2);
                        box.Add(NarrativeUIHelper.CreateRow().With(r => {
                            r.Add(NarrativeUIHelper.CreateLabel("Char Key", "field-label").With(l => l.style.width = 60));
                            r.Add(new TextField { value = entry.CharacterKey }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "Char Key"); entry.CharacterKey = e.newValue; EditorUtility.SetDirty(epNode); })));
                            r.Add(NarrativeUIHelper.CreateButton("X", () => { Undo.RecordObject(epNode, "Remove Char"); ev.CharacterAppearances.RemoveAt(i); EditorUtility.SetDirty(epNode); refreshChars(); }));
                        }));
                        box.Add(NarrativeUIHelper.CreateRow().With(r => {
                            r.Add(NarrativeUIHelper.CreateLabel("Expr Key", "field-label").With(l => l.style.width = 60));
                            r.Add(new TextField { value = entry.ExpressionKey }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "Expr Key"); entry.ExpressionKey = e.newValue; EditorUtility.SetDirty(epNode); })));
                        }));
                        box.Add(NarrativeUIHelper.CreateRow().With(r => {
                            r.Add(NarrativeUIHelper.CreateLabel("Slot Index", "field-label").With(l => l.style.width = 60));
                            r.Add(new IntegerField { value = entry.SlotIndex }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "Slot Index"); entry.SlotIndex = e.newValue; EditorUtility.SetDirty(epNode); })));
                        }));
                        c.Add(box);
                    }, () => { Undo.RecordObject(epNode, "Add Char"); ev.CharacterAppearances.Add(new CharacterSlotEntryData()); EditorUtility.SetDirty(epNode); refreshChars(); });
                    refreshChars();
                    break;

                case EpisodeEventType.Choice:
                    DrawChoices(container, ev, nodeView, epNode, graph);
                    break;

                case EpisodeEventType.BusinessStart:
                    container.Add(CreateField("Ticket", ev.CraftingTicketKey, "none", nodeView, epNode, v => ev.CraftingTicketKey = v, graph));
                    container.Add(CreateField("Flag Success", ev.CraftingFlagGood, "none", nodeView, epNode, v => ev.CraftingFlagGood = v, graph));
                    container.Add(CreateField("Flag Failure", ev.CraftingFlagBad, "none", nodeView, epNode, v => ev.CraftingFlagBad = v, graph));

                    // Success affinity changes (List<VarChangeData>)
                    var successVarsSec = new VisualElement().AddClass("inspector-container").SetMargin(5, 5);
                    successVarsSec.Add(NarrativeUIHelper.CreateLabel("Success Affinity Changes", fontSize: 11).With(l => l.style.unityFontStyleAndWeight = FontStyle.Bold));
                    var successVarsList = new VisualElement();
                    successVarsSec.Add(successVarsList);
                    container.Add(successVarsSec);
                    
                    System.Action refreshSuccessVars = null;
                    refreshSuccessVars = () => NarrativeUIHelper.DrawList(successVarsList, ev.CraftingVarChangesGood, (c, vc, i) => {
                        var r = NarrativeUIHelper.CreateRow();
                        r.Add(new TextField { value = vc.VarName }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "VarName"); vc.VarName = e.newValue; EditorUtility.SetDirty(epNode); })));
                        r.Add(new IntegerField { value = vc.Delta }.With(x => { x.style.width = 60; x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "Delta"); vc.Delta = e.newValue; EditorUtility.SetDirty(epNode); }); }));
                        r.Add(NarrativeUIHelper.CreateButton("X", () => { Undo.RecordObject(epNode, "Remove Var"); ev.CraftingVarChangesGood.RemoveAt(i); EditorUtility.SetDirty(epNode); refreshSuccessVars(); }));
                        c.Add(r);
                    }, () => { Undo.RecordObject(epNode, "Add Var"); ev.CraftingVarChangesGood.Add(new VarChangeData()); EditorUtility.SetDirty(epNode); refreshSuccessVars(); });
                    refreshSuccessVars();

                    // Failure affinity changes (List<VarChangeData>)
                    var failureVarsSec = new VisualElement().AddClass("inspector-container").SetMargin(5, 5);
                    failureVarsSec.Add(NarrativeUIHelper.CreateLabel("Failure Affinity Changes", fontSize: 11).With(l => l.style.unityFontStyleAndWeight = FontStyle.Bold));
                    var failureVarsList = new VisualElement();
                    failureVarsSec.Add(failureVarsList);
                    container.Add(failureVarsSec);
                    
                    System.Action refreshFailureVars = null;
                    refreshFailureVars = () => NarrativeUIHelper.DrawList(failureVarsList, ev.CraftingVarChangesBad, (c, vc, i) => {
                        var r = NarrativeUIHelper.CreateRow();
                        r.Add(new TextField { value = vc.VarName }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "VarName"); vc.VarName = e.newValue; EditorUtility.SetDirty(epNode); })));
                        r.Add(new IntegerField { value = vc.Delta }.With(x => { x.style.width = 60; x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "Delta"); vc.Delta = e.newValue; EditorUtility.SetDirty(epNode); }); }));
                        r.Add(NarrativeUIHelper.CreateButton("X", () => { Undo.RecordObject(epNode, "Remove Var"); ev.CraftingVarChangesBad.RemoveAt(i); EditorUtility.SetDirty(epNode); refreshFailureVars(); }));
                        c.Add(r);
                    }, () => { Undo.RecordObject(epNode, "Add Var"); ev.CraftingVarChangesBad.Add(new VarChangeData()); EditorUtility.SetDirty(epNode); refreshFailureVars(); });
                    refreshFailureVars();
                    break;

                case EpisodeEventType.BranchExit:
                    container.Add(NarrativeUIHelper.CreateLabel("Select Output Branch", "field-label"));
                    var row = NarrativeUIHelper.CreateRow();
                    row.Add(new PopupField<string>(epNode.OutgoingBranches, ev.ExitBranchName ?? "").SetFlex(1).With(x => {
                        x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "Exit Branch"); ev.ExitBranchName = e.newValue; EditorUtility.SetDirty(epNode); nodeView.SetWarning(false); graph.ValidateAllNodes(); });
                    }));
                    row.Add(NarrativeUIHelper.CreateWarningIcon(nodeView, "exit"));
                    container.Add(row);
                    break;
            }

            container.Add(NarrativeUIHelper.CreateLabel("Tip: Use ports to connect flow.", "info-label").SetMargin(20, 0));
        }

        private static VisualElement CreateField(string label, string val, string errKey, SequenceNodeView view, EpisodeNodeSO epNode, System.Action<string> setter, SequenceGraphView g, bool multi = false)
        {
            var row = NarrativeUIHelper.CreateRow();
            row.Add(NarrativeUIHelper.CreateLabel(label, "field-label"));
            row.Add(new TextField { value = val, multiline = multi }.SetFlex(1).With(x => {
                x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, $"Edit {label}"); setter(e.newValue); EditorUtility.SetDirty(epNode); g.ValidateAllNodes(); });
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
                head.Add(NarrativeUIHelper.CreateButton("X", () => { Undo.RecordObject(epNode, "Remove Choice"); ev.Choices.RemoveAt(i); EditorUtility.SetDirty(epNode); refresh(); graph.NotifyInternalNodeStructureChanged(view); graph.NotifyMainGraph(); }));
                box.Add(head);

                var row = NarrativeUIHelper.CreateRow();
                row.Add(new TextField { value = choice.ButtonText }.SetFlex(1).With(x => {
                    x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "Edit Choice Text"); choice.ButtonText = e.newValue; EditorUtility.SetDirty(epNode); graph.ValidateAllNodes(); });
                }));
                row.Add(NarrativeUIHelper.CreateWarningIcon(view, $"choice_{i}"));
                box.Add(row);

                // Set Flags List
                var sfSec = new VisualElement().SetMargin(2, 2);
                sfSec.Add(NarrativeUIHelper.CreateLabel("Set Flags", fontSize: 9).With(l => { l.style.color = Color.gray; l.style.unityFontStyleAndWeight = FontStyle.Bold; }));
                var sfList = new VisualElement();
                sfSec.Add(sfList);
                box.Add(sfSec);
                
                System.Action refreshSf = null;
                refreshSf = () => NarrativeUIHelper.DrawList(sfList, choice.SetFlags, (cc, sf, idx) => {
                    var r = NarrativeUIHelper.CreateRow();
                    r.Add(new TextField { value = sf }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "Edit Set Flag"); choice.SetFlags[idx] = e.newValue; EditorUtility.SetDirty(epNode); })));
                    r.Add(NarrativeUIHelper.CreateButton("X", () => { Undo.RecordObject(epNode, "Remove Set Flag"); choice.SetFlags.RemoveAt(idx); EditorUtility.SetDirty(epNode); refreshSf(); }));
                    cc.Add(r);
                }, () => { Undo.RecordObject(epNode, "Add Set Flag"); choice.SetFlags.Add("flag_to_set"); EditorUtility.SetDirty(epNode); refreshSf(); });
                refreshSf();

                // Clear Flags List
                var cfSec = new VisualElement().SetMargin(2, 2);
                cfSec.Add(NarrativeUIHelper.CreateLabel("Clear Flags", fontSize: 9).With(l => { l.style.color = Color.gray; l.style.unityFontStyleAndWeight = FontStyle.Bold; }));
                var cfList = new VisualElement();
                cfSec.Add(cfList);
                box.Add(cfSec);
                
                System.Action refreshCf = null;
                refreshCf = () => NarrativeUIHelper.DrawList(cfList, choice.ClearFlags, (cc, cf, idx) => {
                    var r = NarrativeUIHelper.CreateRow();
                    r.Add(new TextField { value = cf }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "Edit Clear Flag"); choice.ClearFlags[idx] = e.newValue; EditorUtility.SetDirty(epNode); })));
                    r.Add(NarrativeUIHelper.CreateButton("X", () => { Undo.RecordObject(epNode, "Remove Clear Flag"); choice.ClearFlags.RemoveAt(idx); EditorUtility.SetDirty(epNode); refreshCf(); }));
                    cc.Add(r);
                }, () => { Undo.RecordObject(epNode, "Add Clear Flag"); choice.ClearFlags.Add("flag_to_clear"); EditorUtility.SetDirty(epNode); refreshCf(); });
                refreshCf();

                // Var Changes List
                var vcSec = new VisualElement().SetMargin(2, 2);
                vcSec.Add(NarrativeUIHelper.CreateLabel("Var Changes (Name & Delta)", fontSize: 9).With(l => { l.style.color = Color.gray; l.style.unityFontStyleAndWeight = FontStyle.Bold; }));
                var vcList = new VisualElement();
                vcSec.Add(vcList);
                box.Add(vcSec);
                
                System.Action refreshVc = null;
                refreshVc = () => NarrativeUIHelper.DrawList(vcList, choice.VarChanges, (cc, vc, idx) => {
                    var r = NarrativeUIHelper.CreateRow();
                    r.Add(new TextField { value = vc.VarName }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "Edit Var Name"); vc.VarName = e.newValue; EditorUtility.SetDirty(epNode); })));
                    r.Add(new IntegerField { value = vc.Delta }.With(x => { x.style.width = 40; x.RegisterValueChangedCallback(e => { Undo.RecordObject(epNode, "Edit Var Delta"); vc.Delta = e.newValue; EditorUtility.SetDirty(epNode); }); }));
                    r.Add(NarrativeUIHelper.CreateButton("X", () => { Undo.RecordObject(epNode, "Remove Var Change"); choice.VarChanges.RemoveAt(idx); EditorUtility.SetDirty(epNode); refreshVc(); }));
                    cc.Add(r);
                }, () => { Undo.RecordObject(epNode, "Add Var Change"); choice.VarChanges.Add(new VarChangeData()); EditorUtility.SetDirty(epNode); refreshVc(); });
                refreshVc();

                c.Add(box);
            }, () => { Undo.RecordObject(epNode, "Add Choice"); ev.Choices.Add(new ChoiceOptionData { ButtonText = "New" }); EditorUtility.SetDirty(epNode); refresh(); graph.NotifyInternalNodeStructureChanged(view); graph.NotifyMainGraph(); });
            refresh();
        }
    }
}
