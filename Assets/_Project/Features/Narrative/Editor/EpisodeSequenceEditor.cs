using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;

namespace NarrativeFlow.Editor
{
    public class EpisodeSequenceEditor : EditorWindow
    {
        private EpisodeNodeSO _targetNode;
        private SequenceGraphView _graphView;
        public NarrativeGraphView mainGraphView;
        private VisualElement _inspectorView;

        public static void Open(EpisodeNodeSO node, NarrativeGraphView mainView)
        {
            var window = GetWindow<EpisodeSequenceEditor>("Sequence Editor");
            window._targetNode = node;
            window.mainGraphView = mainView;
            window.titleContent = new GUIContent($"Edit Sequence: {node.name}");
            window.minSize = new Vector2(900, 700);
            window.Show();
            window.RefreshUI();
        }

        private void OnEnable()
        {
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            GenerateToolbar();
            ConstructView();
        }

        private void ConstructView()
        {
            // Create a container for the split view that takes up all space below toolbar
            var container = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Row } };
            rootVisualElement.Add(container);

            var splitView = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1;
            container.Add(splitView);

            // Left side: Inspector
            _inspectorView = new ScrollView();
            _inspectorView.style.minWidth = 250;
            _inspectorView.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            _inspectorView.style.paddingLeft = 10;
            _inspectorView.style.paddingRight = 10;
            _inspectorView.style.paddingTop = 10;
            splitView.Add(_inspectorView);

            // Right side: Graph
            _graphView = new SequenceGraphView(this);
            splitView.Add(_graphView);
        }

        private void GenerateToolbar()
        {
            var toolbar = new Toolbar();
            foreach (EpisodeEventType type in System.Enum.GetValues(typeof(EpisodeEventType)))
            {
                toolbar.Add(new Button(() => _graphView.CreateEvent(type, Vector2.zero)) { text = $"+ {type}" });
            }
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(new Button(() => RefreshUI()) { text = "Refresh Graph" });
            rootVisualElement.Add(toolbar);
        }

        public void OnSelectionChanged(SequenceNodeView nodeView)
        {
            SequenceInspectorUI.Draw(_inspectorView, nodeView, _targetNode, _graphView);
        }

        public void RefreshUI()
        {
            if (_graphView != null)
            {
                _graphView.Populate(_targetNode);
                _inspectorView?.Clear();
            }
        }
    }
}
