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
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
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

            toolbar.Add(new Button(() => _graphView.AutoLayout()) { text = "자동 정렬", tooltip = "시작 블록부터의 깊이를 열로 다시 배치합니다 (Ctrl+Z로 되돌리기)" });

            toolbar.Add(new Button(() =>
            {
                var graph = _graphView.currentGraph;
                if (graph == null) return;
                if (new EpisodeDataCompiler().Compile(graph))
                    ShowNotification(new GUIContent("EpisodeData와 원본 CSV에 저장했습니다."));
                ReloadGraph();
            }) { text = "Compile (EpisodeData + CSV)" });

            toolbar.Add(new Button(() =>
            {
                string picked = EditorUtility.OpenFilePanel("Import Episode CSV to Graph",
                    Slainte.EditorTools.NarrativeAssetPaths.EpisodeSourceRoot, "csv");
                if (string.IsNullOrEmpty(picked)) return;
                var graph = NarrativeCsvSync.ImportCsvToGraph(NarrativeCsvSync.ToProjectRelative(picked));
                if (graph != null) _graphSelector.value = graph;
                ReloadGraph();
            }) { text = "Import CSV..." });

            rootVisualElement.Add(toolbar);
        }

        // 그래프 데이터가 코드에서 바뀐 뒤(컴파일 시 ID 부여, CSV에서 다시 만들기 등) 화면을 다시 그린다.
        public void ReloadGraph()
        {
            var graph = _graphView.currentGraph;
            _graphView.PopulateView(graph);
            OnNodeSelectionChanged(null);
        }

        // 실행 취소는 SO 데이터만 되돌리므로 화면(카드·연결·인스펙터)을 데이터에서 다시 그린다.
        private void OnUndoRedo()
        {
            if (_graphView?.currentGraph != null)
                ReloadGraph();
        }

        public void OnNodeSelectionChanged(NarrativeNodeView nodeView)
        {
            NarrativeInspectorUI.DrawInspector(_inspectorView, nodeView, _graphView, this);
        }
    }
}