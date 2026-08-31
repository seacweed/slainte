using System.Collections.Generic;
using System.IO;
using UnityEngine;
using NarrativeFlow.Runtime;
using Slainte.Content;

namespace NarrativeFlow
{
    public class NarrativeManager : MonoBehaviour
    {
        public static NarrativeManager Instance { get; private set; }

        private RuntimeNarrativeData _currentGraphData;
        private Dictionary<string, RuntimeNode> _nodeDictionary;

        [Header("Settings")]
        [Tooltip("The exact name of the Graph Asset (without _Baked.json)")]
        public string GraphName = "New Narrative Graph";

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            LoadGraphData(GraphName);
        }

        /// <summary>
        /// StreamingAssets에서 구워진(Baked) JSON 데이터를 불러와 메모리에 딕셔너리로 세팅합니다.
        /// </summary>
        public void LoadGraphData(string graphName)
        {
            string fileName = $"{graphName}_Baked.json";
            string filePath = Path.Combine(
                Application.streamingAssetsPath,
                ProjectStreamingAssetPaths.Narrative,
                fileName);

            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                _currentGraphData = JsonUtility.FromJson<RuntimeNarrativeData>(json);

                // 빠른 탐색(O(1))을 위해 NodeId를 Key로 하는 Dictionary 생성
                _nodeDictionary = new Dictionary<string, RuntimeNode>();
                foreach (var node in _currentGraphData.Nodes)
                {
                    _nodeDictionary[node.NodeId] = node;
                }

                Debug.Log($"[NarrativeManager] Successfully loaded graph: {graphName} ({_nodeDictionary.Count} nodes)");
            }
            else
            {
                Debug.LogError($"[NarrativeManager] Could not find baked data for '{graphName}' at path: {filePath}");
            }
        }

        /// <summary>
        /// 특정 노드 ID를 기반으로 노드 데이터를 가져옵니다.
        /// </summary>
        public RuntimeNode GetNode(string nodeId)
        {
            if (_nodeDictionary != null && _nodeDictionary.TryGetValue(nodeId, out RuntimeNode node))
            {
                return node;
            }
            return null;
        }

        /// <summary>
        /// 그래프 내에서 들어오는 연결선이 없는 '루트(시작) 노드'들을 찾아 반환합니다.
        /// </summary>
        public List<RuntimeNode> GetRootNodes()
        {
            if (_currentGraphData == null) return new List<RuntimeNode>();

            var allTargetIds = new HashSet<string>();
            foreach (var node in _currentGraphData.Nodes)
            {
                foreach (var nextId in node.NextNodeIds)
                {
                    allTargetIds.Add(nextId);
                }
            }

            var rootNodes = new List<RuntimeNode>();
            foreach (var node in _currentGraphData.Nodes)
            {
                if (!allTargetIds.Contains(node.NodeId))
                {
                    rootNodes.Add(node);
                }
            }

            return rootNodes;
        }

        /// <summary>
        /// 예시 로직: 특정 노드의 커스텀 필드 값을 읽어옵니다.
        /// </summary>
        public string GetCustomFieldValue(RuntimeNode node, string fieldKey)
        {
            if (node.CustomFields != null)
            {
                foreach (var field in node.CustomFields)
                {
                    if (field.Key == fieldKey) return field.Value;
                }
            }
            return string.Empty;
        }
    }
}
