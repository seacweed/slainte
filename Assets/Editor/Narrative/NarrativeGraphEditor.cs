using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
    public class NarrativeGraphEditor : EditorWindow
    {
        private NarrativeGraphView _graphView;
        private ObjectField _graphSelector;
        private ScrollView _inspectorView;

        private const string LAST_GRAPH_PREF_KEY = "NarrativeGraph_LastOpenedGuid";

        [MenuItem("Narrative/Open Graph")]
        public static void OpenWindow()
        {
            var window = GetWindow<NarrativeGraphEditor>("Narrative Graph");
            window.titleContent = new GUIContent("Narrative Graph");
            window.Show();
        }

        private void OnEnable()
        {
            ConstructGraphView();
            GenerateToolbar();
            LoadLastOpenedGraph();
        }

        private void OnDisable()
        {
            if (_graphView != null && _graphView.parent != null)
            {
                _graphView.parent.Remove(_graphView);
            }
        }

        private void LoadLastOpenedGraph()
        {
            if (EditorPrefs.HasKey(LAST_GRAPH_PREF_KEY))
            {
                string guid = EditorPrefs.GetString(LAST_GRAPH_PREF_KEY);
                if (!string.IsNullOrEmpty(guid))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path))
                    {
                        var graph = AssetDatabase.LoadAssetAtPath<NarrativeGraphSO>(path);
                        if (graph != null && _graphSelector != null)
                        {
                            _graphSelector.SetValueWithoutNotify(graph);
                            if (_graphView != null)
                            {
                                _graphView.PopulateView(graph);
                            }
                        }
                    }
                }
            }
        }

        private void ConstructGraphView()
        {
            var splitView = new TwoPaneSplitView(0, 250, TwoPaneSplitViewOrientation.Horizontal);
            rootVisualElement.Add(splitView);

            _inspectorView = new ScrollView();
            splitView.Add(_inspectorView);

            _graphView = new NarrativeGraphView(this)
            {
                name = "Narrative Graph"
            };
            splitView.Add(_graphView);
        }

        private void GenerateToolbar()
        {
            var toolbar = new Toolbar();

            _graphSelector = new ObjectField("Graph Asset")
            {
                objectType = typeof(NarrativeGraphSO),
                allowSceneObjects = false
            };
            _graphSelector.RegisterValueChangedCallback(evt =>
            {
                var newGraph = evt.newValue as NarrativeGraphSO;
                _graphView.PopulateView(newGraph);
                _inspectorView.Clear();

                if (newGraph != null)
                {
                    string path = AssetDatabase.GetAssetPath(newGraph);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    EditorPrefs.SetString(LAST_GRAPH_PREF_KEY, guid);
                }
                else
                {
                    EditorPrefs.DeleteKey(LAST_GRAPH_PREF_KEY);
                }
            });

            toolbar.Add(_graphSelector);
            
            var saveButton = new Button(() => { AssetDatabase.SaveAssets(); }) { text = "Save Assets" };
            toolbar.Add(saveButton);

            rootVisualElement.Add(toolbar);
        }

        public void OnNodeSelectionChanged(NarrativeNodeView nodeView)
        {
            _inspectorView.Clear();

            if (nodeView != null)
            {
                string headerText = nodeView.nodeData is EpisodeNodeSO ? "Episode Node" : "Empty Node";
                _inspectorView.Add(new Label(headerText) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10, marginBottom = 10, marginLeft = 5 } });

                // 커스텀 필드(노드 정보 추가) 기능 구현
                _inspectorView.Add(new Label("Fields") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 15, marginBottom = 5, marginLeft = 5 } });
                
                var customFieldsContainer = new VisualElement();
                _inspectorView.Add(customFieldsContainer);

                System.Action refreshCustomFields = null;
                refreshCustomFields = () => {
                    customFieldsContainer.Clear();
                    
                    if (nodeView.nodeData.CustomFields != null)
                    {
                        var warningLabels = new System.Collections.Generic.List<Label>();
                        
                        System.Action updateWarnings = () => {
                            var nameCounts = new System.Collections.Generic.Dictionary<string, int>();
                            foreach (var f in nodeView.nodeData.CustomFields)
                            {
                                if (f.FieldName == null) continue;
                                if (!nameCounts.ContainsKey(f.FieldName)) nameCounts[f.FieldName] = 0;
                                nameCounts[f.FieldName]++;
                            }
                            
                            for (int i = 0; i < warningLabels.Count; i++)
                            {
                                var fname = nodeView.nodeData.CustomFields[i].FieldName;
                                if (fname != null && nameCounts.ContainsKey(fname) && nameCounts[fname] > 1)
                                {
                                    warningLabels[i].style.display = DisplayStyle.Flex;
                                }
                                else
                                {
                                    warningLabels[i].style.display = DisplayStyle.None;
                                }
                            }
                        };

                        for (int i = 0; i < nodeView.nodeData.CustomFields.Count; i++)
                        {
                            int index = i;
                            var fieldData = nodeView.nodeData.CustomFields[index];
                            
                            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2, marginLeft = 5, marginRight = 5 } };
                            
                            var warnLabel = new Label("⚠️") { tooltip = "Duplicate field name!", style = { display = DisplayStyle.None, marginTop = 2, marginRight = 2, fontSize = 12 } };
                            warningLabels.Add(warnLabel);

                            var nameField = new TextField { value = fieldData.FieldName, style = { width = 80, flexGrow = 0, flexShrink = 0 } };
                            nameField.RegisterValueChangedCallback(e => {
                                Undo.RecordObject(nodeView.nodeData, "Change Custom Field Name");
                                nodeView.nodeData.CustomFields[index].FieldName = e.newValue;
                                EditorUtility.SetDirty(nodeView.nodeData);
                                nodeView.RefreshVisuals();
                                updateWarnings();
                            });

                            var valField = new TextField { value = fieldData.FieldValue, style = { width = 120, flexGrow = 0, flexShrink = 0 } };
                            valField.RegisterValueChangedCallback(e => {
                                Undo.RecordObject(nodeView.nodeData, "Change Custom Field Value");
                                nodeView.nodeData.CustomFields[index].FieldValue = e.newValue;
                                EditorUtility.SetDirty(nodeView.nodeData);
                                nodeView.RefreshVisuals();
                            });

                            var delBtn = new Button(() => {
                                Undo.RecordObject(nodeView.nodeData, "Delete Custom Field");
                                nodeView.nodeData.CustomFields.RemoveAt(index);
                                EditorUtility.SetDirty(nodeView.nodeData);
                                nodeView.RefreshVisuals();
                                refreshCustomFields(); // UI 갱신
                            }) { text = "X", style = { width = 20, flexGrow = 0, flexShrink = 0 } };

                            row.Add(warnLabel);
                            row.Add(nameField);
                            row.Add(valField);
                            row.Add(delBtn);
                            customFieldsContainer.Add(row);
                        }
                        
                        updateWarnings();
                    }

                    var addBtn = new Button(() => {
                        Undo.RecordObject(nodeView.nodeData, "Add Custom Field");
                        if (nodeView.nodeData.CustomFields == null) nodeView.nodeData.CustomFields = new System.Collections.Generic.List<CustomNodeField>();
                        nodeView.nodeData.CustomFields.Add(new CustomNodeField { FieldName = "NewField", FieldValue = "" });
                        EditorUtility.SetDirty(nodeView.nodeData);
                        nodeView.RefreshVisuals();
                        refreshCustomFields(); // UI 갱신
                    }) { text = "Add Custom Field", style = { marginTop = 5, marginLeft = 5, marginRight = 5 } };
                    customFieldsContainer.Add(addBtn);
                };
                refreshCustomFields();

                // 템플릿 저장 기능 구현
                _inspectorView.Add(new Label("Save Template") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 20, marginBottom = 5, marginLeft = 5 } });
                var templateNameField = new TextField() { value = "", style = { marginLeft = 5, marginRight = 5 } };
                _inspectorView.Add(templateNameField);
                var saveTemplateBtn = new Button(() => {
                    TemplateManager.SaveTemplate(nodeView.nodeData, templateNameField.value);
                }) { text = "Save as Template", style = { marginLeft = 5, marginRight = 5 } };
                _inspectorView.Add(saveTemplateBtn);
            }
            else
            {
                // 선택된 노드가 없을 때 (빈 공간 클릭 시) 템플릿 매니저 표시
                _inspectorView.Add(new Label("Template Manager") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10, marginBottom = 10, marginLeft = 5 } });
                
                var templates = TemplateManager.GetAllTemplates();
                if (templates.Length == 0)
                {
                    _inspectorView.Add(new Label("No templates saved.") { style = { marginLeft = 5 } });
                }
                else
                {
                    foreach (var t in templates)
                    {
                        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 5, marginLeft = 5, marginRight = 5 } };
                        row.Add(new Label(t.name) { style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } });
                        row.Add(new Button(() => {
                            if (EditorUtility.DisplayDialog("Delete Template", $"Are you sure you want to delete the template '{t.name}'?", "Yes", "No"))
                            {
                                TemplateManager.DeleteTemplate(t);
                                OnNodeSelectionChanged(null); // 삭제 후 리스트 갱신
                            }
                        }) { text = "Delete" });
                        _inspectorView.Add(row);
                    }
                }
            }
        }
    }
}