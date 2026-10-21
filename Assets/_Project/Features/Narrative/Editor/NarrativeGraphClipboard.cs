using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NarrativeFlow.Editor
{
    // 그래프 에디터의 복사/붙여넣기 데이터 형식. 노드 묶음(노드 + 그 노드들끼리의 연결)과 블록 안 이벤트 줄 두 종류를
    // OS 클립보드(GraphView가 쓰는 systemCopyBuffer)에 머리말 + JSON으로 담는다. 노드·이벤트는 EditorJsonUtility로
    // 직렬화해 주문서 같은 에셋 참조가 그래프·에디터 세션을 넘어도 유지되고, 꺼낼 때는 Guid와 런타임 ID를 새로 발급해
    // 원본과 충돌하지 않게 한다(템플릿에서 노드를 만들 때도 같은 규칙을 쓴다).
    public static class NarrativeGraphClipboard
    {
        private const string NodesHeader = "SlainteNarrativeNodes:";
        private const string EventsHeader = "SlainteNarrativeEvents:";

        [Serializable]
        private class NodeEntry
        {
            public string TypeName;
            public string Json;
        }

        [Serializable]
        private class NodePayload
        {
            public List<NodeEntry> Nodes = new();
            public List<EdgeData> Edges = new();
        }

        public static string Buffer
        {
            get => EditorGUIUtility.systemCopyBuffer;
            set => EditorGUIUtility.systemCopyBuffer = value;
        }

        public static bool HasNodes(string data) => data != null && data.StartsWith(NodesHeader, StringComparison.Ordinal);
        public static bool HasEvents(string data) => data != null && data.StartsWith(EventsHeader, StringComparison.Ordinal);

        // 묶음 안의 노드끼리 이어진 연결만 함께 담는다 — 바깥 노드로 가는 연결은 붙여넣을 곳에 대상이 없다.
        public static string SerializeNodes(IReadOnlyCollection<NodeDataSO> nodes, IEnumerable<EdgeData> graphEdges)
        {
            if (nodes == null || nodes.Count == 0) return null;
            var guids = new HashSet<string>(nodes.Select(n => n.Guid));
            var payload = new NodePayload();
            foreach (NodeDataSO node in nodes)
                payload.Nodes.Add(new NodeEntry { TypeName = node.GetType().AssemblyQualifiedName, Json = EditorJsonUtility.ToJson(node) });
            if (graphEdges != null)
                payload.Edges.AddRange(graphEdges.Where(e => guids.Contains(e.BaseNodeGuid) && guids.Contains(e.TargetNodeGuid)));
            return NodesHeader + JsonUtility.ToJson(payload);
        }

        // 새 인스턴스(아직 에셋에 붙지 않음)와 새 Guid로 바꾼 연결을 돌려준다. 위치는 원본 그대로이며 호출 측이 옮긴다.
        public static bool TryDeserializeNodes(string data, out List<NodeDataSO> nodes, out List<EdgeData> edges)
        {
            nodes = new List<NodeDataSO>();
            edges = new List<EdgeData>();
            if (!HasNodes(data)) return false;

            NodePayload payload;
            try { payload = JsonUtility.FromJson<NodePayload>(data.Substring(NodesHeader.Length)); }
            catch (ArgumentException) { return false; }
            if (payload?.Nodes == null) return false;

            var guidMap = new Dictionary<string, string>();
            foreach (NodeEntry entry in payload.Nodes)
            {
                Type type = Type.GetType(entry.TypeName);
                if (type == null || type.IsAbstract || !typeof(NodeDataSO).IsAssignableFrom(type)) continue;

                var node = (NodeDataSO)ScriptableObject.CreateInstance(type);
                EditorJsonUtility.FromJsonOverwrite(entry.Json, node);
                string oldGuid = node.Guid;
                ResetIdentity(node);
                if (!string.IsNullOrEmpty(oldGuid)) guidMap[oldGuid] = node.Guid;
                nodes.Add(node);
            }

            foreach (EdgeData edge in payload.Edges ?? new List<EdgeData>())
            {
                if (!guidMap.TryGetValue(edge.BaseNodeGuid, out string from) || !guidMap.TryGetValue(edge.TargetNodeGuid, out string to))
                    continue;
                edges.Add(new EdgeData { BaseNodeGuid = from, TargetNodeGuid = to, OutputPortIndex = edge.OutputPortIndex });
            }
            return nodes.Count > 0;
        }

        // 이벤트 목록은 임시 블록에 담아 직렬화한다 — EditorJsonUtility가 에셋 참조를 보존하는 건 UnityEngine.Object 단위이기 때문.
        public static string SerializeEvents(IEnumerable<EpisodeEvent> events)
        {
            var holder = ScriptableObject.CreateInstance<EpisodeNodeSO>();
            try
            {
                holder.Events = events.ToList();
                return holder.Events.Count > 0 ? EventsHeader + EditorJsonUtility.ToJson(holder) : null;
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }

        public static List<EpisodeEvent> DeserializeEvents(string data)
        {
            if (!HasEvents(data)) return new List<EpisodeEvent>();
            var holder = ScriptableObject.CreateInstance<EpisodeNodeSO>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(data.Substring(EventsHeader.Length), holder);
                List<EpisodeEvent> events = holder.Events ?? new List<EpisodeEvent>();
                foreach (EpisodeEvent ev in events) ResetIdentity(ev);
                return events;
            }
            catch (ArgumentException) { return new List<EpisodeEvent>(); }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }

        // 복제본이 원본의 Guid·런타임 노드 ID를 그대로 가지면 연결과 CSV nodeId가 원본과 겹친다.
        // 런타임 ID는 비워 두면 NarrativeNodeIdAssigner가 흐름에 맞는 새 번호를 매긴다.
        public static void ResetIdentity(NodeDataSO node)
        {
            node.Guid = Guid.NewGuid().ToString();
            node.name = node.Guid;
            if (node is EpisodeNodeSO block)
            {
                block.RouterNodeId = null;
                block.StartEventGuid = null;
                foreach (EpisodeEvent ev in block.Events) ResetIdentity(ev);
            }
            else if (node is TriggerNodeSO trigger)
            {
                trigger.RuntimeNodeId = null;
            }
        }

        public static void ResetIdentity(EpisodeEvent ev)
        {
            ev.Guid = Guid.NewGuid().ToString();
            ev.RuntimeNodeId = null;
            ev.NextEventGuids?.Clear();
        }
    }
}
