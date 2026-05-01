using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
    public static class NarrativeInspectorUI
    {
        public static void DrawInspector(VisualElement container, NarrativeNodeView nodeView, NarrativeGraphEditor editor)
        {
            container.Clear();
            if (nodeView == null)
            {
                DrawEmptyState(container, editor);
                return;
            }

            var nodeData = nodeView.nodeData;

            // Header
            var header = new Label(nodeData.GetType().Name) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 14, marginBottom = 10 } };
            container.Add(header);

            // Metadata (Custom Fields)
            DrawCustomFields(container, nodeData, nodeView);

            // Specific UI based on node type
            if (nodeData is EpisodeNodeSO epNode)
            {
                DrawEpisodeNodeUI(container, epNode, nodeView);
            }
            else if (nodeData is TriggerNodeSO triggerNode)
            {
                DrawTriggerNodeUI(container, triggerNode, nodeView);
            }

            // Template Section
            DrawTemplateSection(container, nodeData);
        }

        private static void DrawEmptyState(VisualElement container, NarrativeGraphEditor editor)
        {
            container.Add(new Label("Template Manager") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10, marginBottom = 10 } });
            
            var templates = TemplateManager.GetAllTemplates();
            if (templates.Length == 0)
            {
                container.Add(new Label("No templates saved."));
            }
            else
            {
                foreach (var t in templates)
                {
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 5 } };
                    row.Add(new Label(t.name) { style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } });
                    row.Add(new Button(() => {
                        if (EditorUtility.DisplayDialog("Delete Template", $"Are you sure you want to delete the template '{t.name}'?", "Yes", "No"))
                        {
                            TemplateManager.DeleteTemplate(t);
                            editor.OnNodeSelectionChanged(null); 
                        }
                    }) { text = "Delete" });
                    container.Add(row);
                }
            }
        }

        private static void DrawCustomFields(VisualElement container, NodeDataSO nodeData, NarrativeNodeView nodeView)
        {
            var foldout = new Foldout { text = "General Fields", value = true };
            container.Add(foldout);

            System.Action refresh = null;
            refresh = () => {
                foldout.Clear();
                foreach (var field in nodeData.CustomFields)
                {
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
                    var nameField = new TextField { value = field.FieldName, style = { width = 80 } };
                    nameField.RegisterValueChangedCallback(e => {
                        Undo.RecordObject(nodeData, "Change Field Name");
                        field.FieldName = e.newValue;
                        nodeView.RefreshVisuals();
                    });
                    var valField = new TextField { value = field.FieldValue, style = { flexGrow = 1 } };
                    valField.RegisterValueChangedCallback(e => {
                        Undo.RecordObject(nodeData, "Change Field Value");
                        field.FieldValue = e.newValue;
                        nodeView.RefreshVisuals();
                    });
                    row.Add(nameField);
                    row.Add(valField);
                    foldout.Add(row);
                }
                var addBtn = new Button(() => {
                    Undo.RecordObject(nodeData, "Add Field");
                    nodeData.CustomFields.Add(new CustomNodeField { FieldName = "NewField", FieldValue = "" });
                    refresh();
                }) { text = "+" };
                foldout.Add(addBtn);
            };
            refresh();
        }

        private static void DrawEpisodeNodeUI(VisualElement container, EpisodeNodeSO epNode, NarrativeNodeView nodeView)
        {
            var openEditorBtn = new Button(() => {
                var graphView = nodeView.GetFirstAncestorOfType<NarrativeGraphView>();
                EpisodeSequenceEditor.Open(epNode, graphView);
            })
            {
                text = "Open Dedicated Sequence Editor",
                style = { height = 40, backgroundColor = new Color(0.2f, 0.4f, 0.2f), marginBottom = 10, marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold }
            };
            container.Add(openEditorBtn);

            // Manual Branch Management
            var branchSection = new VisualElement { style = { marginTop = 10, paddingLeft = 5, paddingRight = 5, paddingTop = 5, paddingBottom = 5, backgroundColor = new Color(0.2f, 0.2f, 0.2f) } };
            branchSection.Add(new Label("Episode Branches (Output Ports)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 5 } });
            container.Add(branchSection);

            var branchListContainer = new VisualElement();
            branchSection.Add(branchListContainer);

            System.Action refreshBranches = null;
            refreshBranches = () => {
                branchListContainer.Clear();
                for (int i = 0; i < epNode.OutgoingBranches.Count; i++)
                {
                    int index = i;
                    var branchName = epNode.OutgoingBranches[index];
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };

                    var nameField = new TextField { value = branchName, style = { flexGrow = 1 } };
                    nameField.RegisterValueChangedCallback(e => {
                        Undo.RecordObject(epNode, "Rename Branch");
                        epNode.OutgoingBranches[index] = e.newValue;
                        nodeView.RebuildPorts();
                    });

                    var delBtn = new Button(() => {
                        Undo.RecordObject(epNode, "Delete Branch");
                        epNode.OutgoingBranches.RemoveAt(index);
                        refreshBranches();
                        var graphView = nodeView.GetFirstAncestorOfType<NarrativeGraphView>();
                        if (graphView != null) graphView.NotifyNodeStructureChanged(nodeView);
                        else nodeView.RebuildPorts();
                    }) { text = "X" };

                    row.Add(nameField);
                    row.Add(delBtn);
                    branchListContainer.Add(row);
                }

                branchListContainer.Add(new Button(() => {
                    Undo.RecordObject(epNode, "Add Branch");
                    epNode.OutgoingBranches.Add("New Branch");
                    refreshBranches();
                    var graphView = nodeView.GetFirstAncestorOfType<NarrativeGraphView>();
                    if (graphView != null) graphView.NotifyNodeStructureChanged(nodeView);
                    else nodeView.RebuildPorts();
                }) { text = "+ Add Branch Port" });
            };
            refreshBranches();

            container.Add(new Label("Detailed events are managed inside the Dedicated Sequence Editor.") 
            { 
                style = { whiteSpace = WhiteSpace.Normal, color = Color.gray, fontSize = 11, unityFontStyleAndWeight = FontStyle.Italic, marginTop = 10 } 
            });
        }

        private static void DrawTriggerNodeUI(VisualElement container, TriggerNodeSO triggerNode, NarrativeNodeView nodeView)
        {
            var section = new VisualElement { style = { marginTop = 10 } };
            section.Add(new Label("Conditions") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 5 } });
            container.Add(section);

            var listContainer = new VisualElement();
            section.Add(listContainer);

            System.Action refresh = null;
            refresh = () => {
                listContainer.Clear();
                for (int i = 0; i < triggerNode.Conditions.Count; i++)
                {
                    int index = i;
                    var cond = triggerNode.Conditions[index];
                    var box = new Box { style = { paddingTop = 5, paddingBottom = 5, paddingLeft = 5, paddingRight = 5, marginBottom = 5 } };
                    
                    var row1 = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                    var typeField = new EnumField("Type", cond.Type);
                    typeField.RegisterValueChangedCallback(e => cond.Type = (TriggerConditionType)e.newValue);
                    row1.Add(typeField);
                    row1.Add(new Button(() => { 
                        triggerNode.Conditions.RemoveAt(index); 
                        refresh(); 
                        nodeView.RefreshVisuals(); 
                        var graphView = nodeView.GetFirstAncestorOfType<NarrativeGraphView>();
                        if (graphView != null) graphView.NotifyNodeStructureChanged(nodeView);
                        else nodeView.RebuildPorts();
                    }) { text = "X" });
                    box.Add(row1);

                    var keyField = new TextField("Key") { value = cond.Key };
                    keyField.RegisterValueChangedCallback(e => { cond.Key = e.newValue; nodeView.RefreshVisuals(); });
                    box.Add(keyField);

                    var row2 = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                    var opField = new TextField("Op") { value = cond.Operator, style = { width = 50 } };
                    opField.RegisterValueChangedCallback(e => cond.Operator = e.newValue);
                    var valField = new TextField("Val") { value = cond.Value, style = { flexGrow = 1 } };
                    valField.RegisterValueChangedCallback(e => { cond.Value = e.newValue; nodeView.RefreshVisuals(); });
                    row2.Add(opField);
                    row2.Add(valField);
                    box.Add(row2);

                    listContainer.Add(box);
                }
                listContainer.Add(new Button(() => { 
                    Undo.RecordObject(triggerNode, "Add Condition");
                    triggerNode.Conditions.Add(new GraphTriggerCondition()); 
                    refresh(); 
                    nodeView.RefreshVisuals(); 
                    var graphView = nodeView.GetFirstAncestorOfType<NarrativeGraphView>();
                    if (graphView != null) graphView.NotifyNodeStructureChanged(nodeView);
                    else nodeView.RebuildPorts();
                }) { text = "+ Condition" });
            };
            refresh();
        }

        private static void DrawTemplateSection(VisualElement container, NodeDataSO nodeData)
        {
            container.Add(new Label("Save Template") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 20 } });
            var nameField = new TextField { value = "" };
            container.Add(nameField);
            container.Add(new Button(() => TemplateManager.SaveTemplate(nodeData, nameField.value)) { text = "Save as Template" });
        }
    }
}
