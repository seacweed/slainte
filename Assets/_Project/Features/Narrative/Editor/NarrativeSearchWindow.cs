using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
    // Space 키로 여는 노드 생성 검색창. 노드는 템플릿에서만 만들므로 템플릿을 한 단계 목록으로 바로 보여 준다.
    public class NarrativeSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private NarrativeGraphView _graphView;

        public void Init(NarrativeGraphView graphView)
        {
            _graphView = graphView;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var tree = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent("노드 만들기"), 0) };

            var templates = TemplateManager.GetAllTemplates();
            foreach (var t in templates)
                tree.Add(new SearchTreeEntry(new GUIContent(t.name)) { level = 1, userData = t });
            if (templates.Length == 0)
                tree.Add(new SearchTreeEntry(new GUIContent("(템플릿 없음 — 노드 선택 후 Save Template으로 저장)")) { level = 1 });

            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            if (!(searchTreeEntry.userData is NodeDataSO template)) return false;

            var windowMousePosition = context.screenMousePosition - _graphView.window.position.position;
            var graphMousePosition = _graphView.contentViewContainer.WorldToLocal(windowMousePosition);
            _graphView.CreateNodeFromTemplate(template, graphMousePosition);
            return true;
        }
    }
}
