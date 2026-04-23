using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace NarrativeFlow.Editor
{
    public static class TemplateManager
    {
        public const string TEMPLATE_DIR = "Assets/Editor/Narrative/Templates";

        public static void SaveTemplate(NodeDataSO sourceNode, string templateName)
        {
            if (!Directory.Exists(TEMPLATE_DIR)) Directory.CreateDirectory(TEMPLATE_DIR);
            
            NodeDataSO clone = Object.Instantiate(sourceNode);
            clone.Guid = ""; // 템플릿 복제 시 GUID를 비워둠
            
            string path = AssetDatabase.GenerateUniqueAssetPath($"{TEMPLATE_DIR}/{templateName}.asset");
            AssetDatabase.CreateAsset(clone, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Narrative Tool] Template saved to {path}");
        }

        public static NodeDataSO[] GetAllTemplates()
        {
            if (!Directory.Exists(TEMPLATE_DIR)) return new NodeDataSO[0];
            string[] guids = AssetDatabase.FindAssets("t:NodeDataSO", new[] { TEMPLATE_DIR });
            var list = new List<NodeDataSO>();
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var asset = AssetDatabase.LoadAssetAtPath<NodeDataSO>(path);
                if (asset != null) list.Add(asset);
            }
            return list.ToArray();
        }

        public static void DeleteTemplate(NodeDataSO template)
        {
            string path = AssetDatabase.GetAssetPath(template);
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}