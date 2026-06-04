using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
    public static class NarrativeInspectorUI
    {
        public static void DrawInspector(VisualElement container, NarrativeNodeView nodeView, NarrativeGraphView gv, NarrativeGraphEditor editor)
        {
            container.Clear();
            container.styleSheets.Add(NarrativeUIHelper.LoadStyle());
            if (nodeView == null)
            {
                DrawGlobalSettings(container, gv?.currentGraph, editor);
                DrawTemplates(container, editor);
                return;
            }

            nodeView.ClearValidationEvents();
            var data = nodeView.nodeData;

            // 1. Warning Header
            var warningBox = new HelpBox("", HelpBoxMessageType.Error).With(x => { x.style.display = DisplayStyle.None; x.style.marginBottom = 10; });
            container.Add(warningBox);
            System.Action updateHeader = () => { var msg = nodeView.GetWarningMessage(); warningBox.text = msg; warningBox.style.display = string.IsNullOrEmpty(msg) ? DisplayStyle.None : DisplayStyle.Flex; };
            nodeView.OnValidationChanged += updateHeader;
            updateHeader();

            container.Add(NarrativeUIHelper.CreateLabel(data.GetType().Name, "section-header"));

            // 2. Custom Fields
            var foldout = new Foldout { text = "General Fields", value = true };
            container.Add(foldout);
            System.Action refreshFields = null;
            refreshFields = () => NarrativeUIHelper.DrawList(foldout, data.CustomFields, (c, f, i) => {
                var row = NarrativeUIHelper.CreateRow();
                // Name
                row.Add(new TextField { value = f.FieldName }.With(x => {
                    x.style.width = 100;
                    x.RegisterValueChangedCallback(e => { f.FieldName = e.newValue; nodeView.RefreshVisuals(); gv.ValidateAllNodes(); });
                }));
                row.Add(NarrativeUIHelper.CreateWarningIcon(nodeView, $"field_{i}"));
                // Value
                row.Add(new TextField { value = f.FieldValue }.SetFlex(1).With(x => {
                    x.RegisterValueChangedCallback(e => { f.FieldValue = e.newValue; nodeView.RefreshVisuals(); gv.ValidateAllNodes(); });
                }));
                if (f.FieldName.ToLower() == "title") row.Add(NarrativeUIHelper.CreateWarningIcon(nodeView, "title"));

                row.Add(NarrativeUIHelper.CreateButton("X", () => { data.CustomFields.RemoveAt(i); refreshFields(); nodeView.RefreshVisuals(); gv.ValidateAllNodes(); }));
                c.Add(row);
            }, () => { data.CustomFields.Add(new CustomNodeField { FieldName = "New", FieldValue = "" }); refreshFields(); nodeView.RefreshVisuals(); gv.ValidateAllNodes(); });
            refreshFields();

            if (data is EpisodeNodeSO ep) DrawEpisode(container, ep, nodeView, gv);
            else if (data is TriggerNodeSO tr) DrawTrigger(container, tr, nodeView, gv);

            container.Add(NarrativeUIHelper.CreateDivider());
            container.Add(NarrativeUIHelper.CreateLabel("Save Template", "field-label").SetMargin(10, 0));
            var tName = new TextField(); container.Add(tName);
            container.Add(NarrativeUIHelper.CreateButton("Save", () => TemplateManager.SaveTemplate(data, tName.value)));
        }

        private static void DrawEpisode(VisualElement container, EpisodeNodeSO ep, NarrativeNodeView view, NarrativeGraphView gv)
        {
            container.Add(NarrativeUIHelper.CreateButton("Open Sequence Editor", () => EpisodeSequenceEditor.Open(ep, gv)).With(b => { b.style.height = 36; b.style.backgroundColor = new Color(0.2f, 0.4f, 0.2f); }).SetMargin(10, 10));
            
            var sec = new VisualElement().AddClass("inspector-container");
            sec.Add(NarrativeUIHelper.CreateLabel("Outcome Branches", "field-label"));
            container.Add(sec);

            System.Action refresh = null;
            refresh = () => NarrativeUIHelper.DrawList(sec, ep.OutgoingBranches, (c, b, i) => {
                var row = NarrativeUIHelper.CreateRow();
                row.Add(new TextField { value = b }.SetFlex(1).With(x => {
                    x.RegisterValueChangedCallback(e => { ep.OutgoingBranches[i] = e.newValue; view.RebuildPorts(); gv.ValidateAllNodes(); });
                }));
                row.Add(NarrativeUIHelper.CreateWarningIcon(view, $"branch_{i}"));
                row.Add(NarrativeUIHelper.CreateButton("X", () => { ep.OutgoingBranches.RemoveAt(i); refresh(); gv.NotifyNodeStructureChanged(view); gv.ValidateAllNodes(); }));
                c.Add(row);
            }, () => { ep.OutgoingBranches.Add("New"); refresh(); gv.NotifyNodeStructureChanged(view); gv.ValidateAllNodes(); });
            refresh();
        }

        private static void DrawTrigger(VisualElement container, TriggerNodeSO tr, NarrativeNodeView view, NarrativeGraphView gv)
        {
            var list = new VisualElement(); container.Add(list);
            System.Action refresh = null;
            refresh = () => NarrativeUIHelper.DrawList(list, tr.Conditions, (c, cond, i) => {
                var box = new Box().AddClass("inspector-container").SetMargin(0, 5);
                box.Add(NarrativeUIHelper.CreateRow().With(r => {
                    r.Add(new EnumField(cond.Type).SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { cond.Type = (TriggerConditionType)e.newValue; gv.ValidateAllNodes(); })));
                    r.Add(NarrativeUIHelper.CreateButton("X", () => { tr.Conditions.RemoveAt(i); refresh(); view.RefreshVisuals(); gv.NotifyNodeStructureChanged(view); gv.ValidateAllNodes(); }));
                }));
                box.Add(NarrativeUIHelper.CreateRow().With(r => {
                    r.Add(NarrativeUIHelper.CreateLabel("Key", "field-label").With(l => l.style.width = 40));
                    r.Add(new TextField { value = cond.Key }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { cond.Key = e.newValue; view.RefreshVisuals(); gv.ValidateAllNodes(); })));
                    r.Add(NarrativeUIHelper.CreateWarningIcon(view, $"cond_key_{i}"));
                }));
                box.Add(NarrativeUIHelper.CreateRow().With(r => {
                    r.Add(new TextField { value = cond.Operator }.With(x => { x.style.width = 40; x.RegisterValueChangedCallback(e => { cond.Operator = e.newValue; gv.ValidateAllNodes(); }); }));
                    r.Add(new TextField { value = cond.Value }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { cond.Value = e.newValue; view.RefreshVisuals(); gv.ValidateAllNodes(); })));
                    r.Add(NarrativeUIHelper.CreateWarningIcon(view, $"cond_val_{i}"));
                }));
                c.Add(box);
            }, () => { tr.Conditions.Add(new GraphTriggerCondition()); refresh(); gv.ValidateAllNodes(); });
            refresh();
        }

        private static void DrawTemplates(VisualElement container, NarrativeGraphEditor editor)
        {
            container.Add(NarrativeUIHelper.CreateLabel("Templates", "section-header"));
            foreach (var t in TemplateManager.GetAllTemplates())
            {
                var row = NarrativeUIHelper.CreateRow().SetMargin(0, 2);
                row.Add(new Label(t.name).SetFlex(1));
                row.Add(NarrativeUIHelper.CreateButton("Delete", () => { if (EditorUtility.DisplayDialog("Del", $"Delete {t.name}?", "Yes")) { TemplateManager.DeleteTemplate(t); editor.OnNodeSelectionChanged(null); } }));
                container.Add(row);
            }
        }

        private static void DrawGlobalSettings(VisualElement container, NarrativeGraphSO graph, NarrativeGraphEditor editor)
        {
            if (graph == null) return;

            var titleLabel = NarrativeUIHelper.CreateLabel("Episode Global Settings", "section-header");
            container.Add(titleLabel);

            // 1. Basic Metadata
            container.Add(NarrativeUIHelper.CreateLabel("Episode Title", "field-label"));
            container.Add(new TextField { value = graph.episodeTitle }.With(x => {
                x.RegisterValueChangedCallback(e => { graph.episodeTitle = e.newValue; EditorUtility.SetDirty(graph); });
            }));

            container.Add(NarrativeUIHelper.CreateLabel("Board Description", "field-label"));
            container.Add(new TextField { value = graph.episodeDescription, multiline = true }.With(x => {
                x.RegisterValueChangedCallback(e => { graph.episodeDescription = e.newValue; EditorUtility.SetDirty(graph); });
            }));

            var boardRow = NarrativeUIHelper.CreateRow();
            boardRow.Add(NarrativeUIHelper.CreateLabel("Icon (Board)", "field-label").SetFlex(1));
            boardRow.Add(new TextField { value = graph.iconNameBoard }.SetFlex(1).With(x => {
                x.RegisterValueChangedCallback(e => { graph.iconNameBoard = e.newValue; EditorUtility.SetDirty(graph); });
            }));
            container.Add(boardRow);

            var archiveRow = NarrativeUIHelper.CreateRow();
            archiveRow.Add(NarrativeUIHelper.CreateLabel("Icon (Archive)", "field-label").SetFlex(1));
            archiveRow.Add(new TextField { value = graph.iconNameArchive }.SetFlex(1).With(x => {
                x.RegisterValueChangedCallback(e => { graph.iconNameArchive = e.newValue; EditorUtility.SetDirty(graph); });
            }));
            container.Add(archiveRow);

            // 2. Trigger Condition Foldout
            var triggerFoldout = new Foldout { text = "Trigger Conditions", value = false };
            container.Add(triggerFoldout);

            triggerFoldout.Add(NarrativeUIHelper.CreateLabel("Min Day", "field-label"));
            triggerFoldout.Add(new IntegerField { value = graph.triggerCondition.minDay }.With(x => {
                x.RegisterValueChangedCallback(e => { graph.triggerCondition.minDay = e.newValue; EditorUtility.SetDirty(graph); });
            }));

            // Lists under trigger condition
            // requiredFlags (List<string>)
            var reqFlagsSec = new VisualElement().AddClass("inspector-container").SetMargin(5, 5);
            triggerFoldout.Add(reqFlagsSec);
            System.Action refreshReqFlags = null;
            refreshReqFlags = () => NarrativeUIHelper.DrawList(reqFlagsSec, graph.triggerCondition.requiredFlags, (c, f, i) => {
                var r = NarrativeUIHelper.CreateRow();
                r.Add(new TextField { value = f }.SetFlex(1).With(x => {
                    x.RegisterValueChangedCallback(e => { graph.triggerCondition.requiredFlags[i] = e.newValue; EditorUtility.SetDirty(graph); });
                }));
                r.Add(NarrativeUIHelper.CreateButton("X", () => { graph.triggerCondition.requiredFlags.RemoveAt(i); refreshReqFlags(); EditorUtility.SetDirty(graph); }));
                c.Add(r);
            }, () => { graph.triggerCondition.requiredFlags.Add("new_flag"); refreshReqFlags(); EditorUtility.SetDirty(graph); });
            reqFlagsSec.Add(new Label("Required Flags"));
            refreshReqFlags();

            // blockedFlags (List<string>)
            var blkFlagsSec = new VisualElement().AddClass("inspector-container").SetMargin(5, 5);
            triggerFoldout.Add(blkFlagsSec);
            System.Action refreshBlkFlags = null;
            refreshBlkFlags = () => NarrativeUIHelper.DrawList(blkFlagsSec, graph.triggerCondition.blockedFlags, (c, f, i) => {
                var r = NarrativeUIHelper.CreateRow();
                r.Add(new TextField { value = f }.SetFlex(1).With(x => {
                    x.RegisterValueChangedCallback(e => { graph.triggerCondition.blockedFlags[i] = e.newValue; EditorUtility.SetDirty(graph); });
                }));
                r.Add(NarrativeUIHelper.CreateButton("X", () => { graph.triggerCondition.blockedFlags.RemoveAt(i); refreshBlkFlags(); EditorUtility.SetDirty(graph); }));
                c.Add(r);
            }, () => { graph.triggerCondition.blockedFlags.Add("new_flag"); refreshBlkFlags(); EditorUtility.SetDirty(graph); });
            blkFlagsSec.Add(new Label("Blocked Flags"));
            refreshBlkFlags();

            // prerequisiteEpisodeIds (List<string>)
            var prereqSec = new VisualElement().AddClass("inspector-container").SetMargin(5, 5);
            triggerFoldout.Add(prereqSec);
            System.Action refreshPrereq = null;
            refreshPrereq = () => NarrativeUIHelper.DrawList(prereqSec, graph.triggerCondition.prerequisiteEpisodeIds, (c, f, i) => {
                var r = NarrativeUIHelper.CreateRow();
                r.Add(new TextField { value = f }.SetFlex(1).With(x => {
                    x.RegisterValueChangedCallback(e => { graph.triggerCondition.prerequisiteEpisodeIds[i] = e.newValue; EditorUtility.SetDirty(graph); });
                }));
                r.Add(NarrativeUIHelper.CreateButton("X", () => { graph.triggerCondition.prerequisiteEpisodeIds.RemoveAt(i); refreshPrereq(); EditorUtility.SetDirty(graph); }));
                c.Add(r);
            }, () => { graph.triggerCondition.prerequisiteEpisodeIds.Add("prev_episode"); refreshPrereq(); EditorUtility.SetDirty(graph); });
            prereqSec.Add(new Label("Prerequisite Episodes"));
            refreshPrereq();

            // requiredVars (List<VarCondition>)
            var reqVarsSec = new VisualElement().AddClass("inspector-container").SetMargin(5, 5);
            triggerFoldout.Add(reqVarsSec);
            System.Action refreshReqVars = null;
            refreshReqVars = () => NarrativeUIHelper.DrawList(reqVarsSec, graph.triggerCondition.requiredVars, (c, cond, i) => {
                var box = new Box().AddClass("inspector-container").SetMargin(0, 2);
                box.Add(NarrativeUIHelper.CreateRow().With(r => {
                    r.Add(NarrativeUIHelper.CreateLabel("Var", "field-label").With(l => l.style.width = 40));
                    r.Add(new TextField { value = cond.varName }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { cond.varName = e.newValue; EditorUtility.SetDirty(graph); })));
                    r.Add(NarrativeUIHelper.CreateButton("X", () => { graph.triggerCondition.requiredVars.RemoveAt(i); refreshReqVars(); EditorUtility.SetDirty(graph); }));
                }));
                box.Add(NarrativeUIHelper.CreateRow().With(r => {
                    r.Add(new EnumField(cond.op).With(x => { x.style.width = 100; x.RegisterValueChangedCallback(e => { cond.op = (CompareOp)e.newValue; EditorUtility.SetDirty(graph); }); }));
                    r.Add(new IntegerField { value = cond.threshold }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { cond.threshold = e.newValue; EditorUtility.SetDirty(graph); })));
                }));
                c.Add(box);
            }, () => { graph.triggerCondition.requiredVars.Add(new VarCondition()); refreshReqVars(); EditorUtility.SetDirty(graph); });
            reqVarsSec.Add(new Label("Required Affinity/Vars"));
            refreshReqVars();


            // 3. Opening Characters Foldout
            var openCharsFoldout = new Foldout { text = "Opening Characters", value = false };
            container.Add(openCharsFoldout);

            var openCharsSec = new VisualElement().AddClass("inspector-container");
            openCharsFoldout.Add(openCharsSec);
            System.Action refreshOpenChars = null;
            refreshOpenChars = () => NarrativeUIHelper.DrawList(openCharsSec, graph.openingCharacters, (c, entry, i) => {
                var box = new Box().AddClass("inspector-container").SetMargin(0, 2);
                box.Add(NarrativeUIHelper.CreateRow().With(r => {
                    r.Add(NarrativeUIHelper.CreateLabel("Char Key", "field-label"));
                    r.Add(new TextField { value = entry.characterKey }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { entry.characterKey = e.newValue; EditorUtility.SetDirty(graph); })));
                    r.Add(NarrativeUIHelper.CreateButton("X", () => { graph.openingCharacters.RemoveAt(i); refreshOpenChars(); EditorUtility.SetDirty(graph); }));
                }));
                box.Add(NarrativeUIHelper.CreateRow().With(r => {
                    r.Add(NarrativeUIHelper.CreateLabel("Expr Key", "field-label"));
                    r.Add(new TextField { value = entry.expressionKey }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { entry.expressionKey = e.newValue; EditorUtility.SetDirty(graph); })));
                }));
                box.Add(NarrativeUIHelper.CreateRow().With(r => {
                    r.Add(NarrativeUIHelper.CreateLabel("Slot Index", "field-label"));
                    r.Add(new IntegerField { value = entry.slotIndex }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { entry.slotIndex = e.newValue; EditorUtility.SetDirty(graph); })));
                }));
                c.Add(box);
            }, () => { graph.openingCharacters.Add(new CharacterSlotEntry()); refreshOpenChars(); EditorUtility.SetDirty(graph); });
            refreshOpenChars();


            // 4. Board Display Lists
            var boardListsFoldout = new Foldout { text = "Board Display Detail", value = false };
            container.Add(boardListsFoldout);

            // characters (List<EpisodeCharacter>)
            var boardCharsSec = new VisualElement().AddClass("inspector-container").SetMargin(5, 5);
            boardListsFoldout.Add(boardCharsSec);
            System.Action refreshBoardChars = null;
            refreshBoardChars = () => NarrativeUIHelper.DrawList(boardCharsSec, graph.characters, (c, ec, i) => {
                var r = NarrativeUIHelper.CreateRow();
                r.Add(new TextField { value = ec.characterName }.SetFlex(1).With(x => {
                    x.RegisterValueChangedCallback(e => { ec.characterName = e.newValue; EditorUtility.SetDirty(graph); });
                }));
                r.Add(NarrativeUIHelper.CreateButton("X", () => { graph.characters.RemoveAt(i); refreshBoardChars(); EditorUtility.SetDirty(graph); }));
                c.Add(r);
            }, () => { graph.characters.Add(new EpisodeCharacter()); refreshBoardChars(); EditorUtility.SetDirty(graph); });
            boardCharsSec.Add(new Label("Characters List"));
            refreshBoardChars();

            // customConditionTexts (List<string>)
            var customCondSec = new VisualElement().AddClass("inspector-container").SetMargin(5, 5);
            boardListsFoldout.Add(customCondSec);
            System.Action refreshCustomCond = null;
            refreshCustomCond = () => NarrativeUIHelper.DrawList(customCondSec, graph.customConditionTexts, (c, text, i) => {
                var r = NarrativeUIHelper.CreateRow();
                r.Add(new TextField { value = text }.SetFlex(1).With(x => {
                    x.RegisterValueChangedCallback(e => { graph.customConditionTexts[i] = e.newValue; EditorUtility.SetDirty(graph); });
                }));
                r.Add(NarrativeUIHelper.CreateButton("X", () => { graph.customConditionTexts.RemoveAt(i); refreshCustomCond(); EditorUtility.SetDirty(graph); }));
                c.Add(r);
            }, () => { graph.customConditionTexts.Add("Condition text"); refreshCustomCond(); EditorUtility.SetDirty(graph); });
            customCondSec.Add(new Label("Custom Condition Texts (UI Guide)"));
            refreshCustomCond();

            container.Add(NarrativeUIHelper.CreateDivider());
        }
    }
}
