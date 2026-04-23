using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
    public class NarrativeSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private NarrativeGraphView _graphView;

        public void Init(NarrativeGraphView graphView)
        {
            _graphView = graphView;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var tree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Create Node"), 0),
                
                new SearchTreeGroupEntry(new GUIContent("Base Nodes"), 1),
                new SearchTreeEntry(new GUIContent("Empty Node"))
                {
                    level = 2,
                    userData = typeof(EmptyNodeSO)
                },
                new SearchTreeEntry(new GUIContent("Episode Node"))
                {
                    level = 2,
                    userData = typeof(EpisodeNodeSO)
                }
            };

            var templates = TemplateManager.GetAllTemplates();
            if (templates.Length > 0)
            {
                tree.Add(new SearchTreeGroupEntry(new GUIContent("Templates"), 1));
                foreach (var t in templates)
                {
                    tree.Add(new SearchTreeEntry(new GUIContent(t.name))
                    {
                        level = 2,
                        userData = t
                    });
                }
            }

            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry SearchTreeEntry, SearchWindowContext context)
        {
            var windowMousePosition = context.screenMousePosition - _graphView.window.position.position;
            var graphMousePosition = _graphView.contentViewContainer.WorldToLocal(windowMousePosition);

            if (SearchTreeEntry.userData is Type type)
            {
                _graphView.CreateNode(type, graphMousePosition);
                return true;
            }
            else if (SearchTreeEntry.userData is NodeDataSO template)
            {
                _graphView.CreateNodeFromTemplate(template, graphMousePosition);
                return true;
            }

            return false;
        }
    }
}