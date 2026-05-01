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
            NarrativeInspectorUI.DrawInspector(_inspectorView, nodeView, this);
        }
    }
}