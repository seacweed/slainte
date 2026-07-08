using System.Linq;
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
                DrawGraphMetadata(container, gv);
                container.Add(NarrativeUIHelper.CreateDivider());
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

        private static void DrawGraphMetadata(VisualElement container, NarrativeGraphView gv)
        {
            var graph = gv.currentGraph;
            if (graph == null)
            {
                container.Add(NarrativeUIHelper.CreateLabel("No graph loaded.", "info-label").SetMargin(20, 0));
                return;
            }

            container.Add(NarrativeUIHelper.CreateLabel("Graph Settings", "section-header"));

            // Episode ID
            var idRow = NarrativeUIHelper.CreateRow();
            idRow.Add(NarrativeUIHelper.CreateLabel("Episode ID", "field-label").With(l => l.style.width = 90));
            idRow.Add(new TextField { value = graph.EpisodeId }.SetFlex(1).With(x =>
                x.RegisterValueChangedCallback(e => { Undo.RecordObject(graph, "Set EpisodeId"); graph.EpisodeId = e.newValue; EditorUtility.SetDirty(graph); })));
            container.Add(idRow);

            // Episode Title
            var titleRow = NarrativeUIHelper.CreateRow();
            titleRow.Add(NarrativeUIHelper.CreateLabel("Title", "field-label").With(l => l.style.width = 90));
            titleRow.Add(new TextField { value = graph.EpisodeTitle }.SetFlex(1).With(x =>
                x.RegisterValueChangedCallback(e => { Undo.RecordObject(graph, "Set EpisodeTitle"); graph.EpisodeTitle = e.newValue; EditorUtility.SetDirty(graph); })));
            container.Add(titleRow);

            // Start Node dropdown
            container.Add(NarrativeUIHelper.CreateDivider());
            container.Add(NarrativeUIHelper.CreateLabel("Start Node", "field-label"));
            var episodeNodes = graph.Nodes.OfType<EpisodeNodeSO>().ToList();
            if (episodeNodes.Count > 0)
            {
                var labels = episodeNodes.Select(GetNodeTitle).ToList();
                labels.Insert(0, "(None)");

                int startIdx = 0;
                if (!string.IsNullOrEmpty(graph.StartNodeGuid))
                {
                    int found = episodeNodes.FindIndex(n => n.Guid == graph.StartNodeGuid);
                    if (found >= 0) startIdx = found + 1;
                }

                var popup = new PopupField<string>(labels, startIdx);
                popup.RegisterValueChangedCallback(e =>
                {
                    int idx = labels.IndexOf(e.newValue);
                    Undo.RecordObject(graph, "Set StartNode");
                    graph.StartNodeGuid = idx > 0 ? episodeNodes[idx - 1].Guid : "";
                    EditorUtility.SetDirty(graph);
                });
                container.Add(popup);
            }
            else
            {
                container.Add(NarrativeUIHelper.CreateLabel("(Add episode blocks first)", "info-label").With(l => l.style.color = Color.gray));
            }

            // Trigger & Opening Characters note
            container.Add(NarrativeUIHelper.CreateDivider());
            container.Add(NarrativeUIHelper.CreateLabel("Trigger / Opening Chars", "field-label"));
            container.Add(NarrativeUIHelper.CreateLabel("Select the graph asset in the Project panel to edit in the Inspector.", "info-label")
                .With(l => { l.style.color = Color.gray; l.style.whiteSpace = WhiteSpace.Normal; }));
            container.Add(new Button(() => { Selection.activeObject = graph; EditorGUIUtility.PingObject(graph); }) { text = "Ping Graph Asset" }.SetMargin(4, 0));

            // Compile shortcut
            container.Add(NarrativeUIHelper.CreateDivider());
            container.Add(new Button(() => new EpisodeDataCompiler().Compile(graph)) { text = "Compile to EpisodeData" }
                .With(b => { b.style.height = 30; b.style.backgroundColor = new Color(0.15f, 0.35f, 0.15f); }).SetMargin(4, 0));
        }

        private static string GetNodeTitle(EpisodeNodeSO node)
        {
            var titleField = node.CustomFields?.Find(f => f.FieldName?.ToLower() == "title");
            return !string.IsNullOrEmpty(titleField?.FieldValue) ? titleField.FieldValue : node.name;
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
    }
}
