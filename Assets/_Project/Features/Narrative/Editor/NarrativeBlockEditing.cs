using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NarrativeFlow.Editor
{
    // 그래프 에디터에서 블록 안 이벤트를 편집하는 조작(추가·이동·삭제)과 블록 나누기·합치기.
    // 뷰는 이 결과를 다시 그리기만 하고, 데이터 변경과 Undo 기록은 모두 여기서 한다.
    public static class NarrativeBlockEditing
    {
        public static EpisodeEvent CreateEvent(EpisodeEventType type)
        {
            var ev = new EpisodeEvent { Type = type };
            if (type == EpisodeEventType.Choice)
            {
                ev.Choices.Add(new ChoiceOptionData { ButtonText = "Choice 0" });
                ev.Choices.Add(new ChoiceOptionData { ButtonText = "Choice 1" });
            }
            return ev;
        }

        public static EpisodeEvent InsertEvent(EpisodeNodeSO block, int index, EpisodeEventType type)
        {
            Undo.RecordObject(block, $"Add {type}");
            EpisodeEvent ev = CreateEvent(type);
            block.Events.Insert(Mathf.Clamp(index, 0, block.Events.Count), ev);
            EditorUtility.SetDirty(block);
            return ev;
        }

        public static bool MoveEvent(EpisodeNodeSO block, int index, int delta)
        {
            int target = index + delta;
            if (index < 0 || index >= block.Events.Count || target < 0 || target >= block.Events.Count)
                return false;

            Undo.RecordObject(block, "Move Event");
            (block.Events[index], block.Events[target]) = (block.Events[target], block.Events[index]);
            EditorUtility.SetDirty(block);
            return true;
        }

        public static void RemoveEvent(EpisodeNodeSO block, int index)
        {
            if (index < 0 || index >= block.Events.Count)
                return;
            Undo.RecordObject(block, "Remove Event");
            block.Events.RemoveAt(index);
            EditorUtility.SetDirty(block);
        }

        // index 이벤트부터 새 블록으로 떼어 낸다. 블록을 끝내는 이벤트 뒤에서는 나눌 수 없다(그 뒤는 실행되지 않으므로).
        public static bool CanSplit(EpisodeNodeSO block, int index) =>
            block != null
            && index > 0
            && index < block.Events.Count
            && !NarrativeBlockModel.EndsBlock(block.Events[index - 1]);

        // 기존 출력 포트와 연결은 뒤쪽(새) 블록이 그대로 물려받고, 앞쪽 블록은 Next 하나로 새 블록에 이어진다
        // — 실행 결과가 나누기 전과 같아야 하기 때문이다.
        public static EpisodeNodeSO SplitBlock(NarrativeGraphSO graph, EpisodeNodeSO block, int index)
        {
            if (graph == null || !CanSplit(block, index))
                return null;

            EpisodeNodeSO tail = ScriptableObject.CreateInstance<EpisodeNodeSO>();
            tail.Guid = Guid.NewGuid().ToString();
            tail.name = tail.Guid;
            tail.CustomFields = new List<CustomNodeField> { new() { FieldName = "Title", FieldValue = "New Block" } };
            tail.Position = new Rect(block.Position.x + block.Position.width + 80f, block.Position.y, block.Position.width, block.Position.height);

            Undo.RecordObjects(new UnityEngine.Object[] { graph, block }, "Split Block");
            tail.Events = block.Events.Skip(index).ToList();
            tail.OutgoingBranches = new List<string>(block.OutgoingBranches);
            block.Events = block.Events.Take(index).ToList();
            block.OutgoingBranches = new List<string> { NarrativeBlockModel.NextLabel };

            for (int i = 0; i < graph.Edges.Count; i++)
            {
                if (graph.Edges[i].BaseNodeGuid != block.Guid) continue;
                EdgeData edge = graph.Edges[i];
                edge.BaseNodeGuid = tail.Guid;
                graph.Edges[i] = edge;
            }
            graph.Edges.Add(new EdgeData { BaseNodeGuid = block.Guid, TargetNodeGuid = tail.Guid, OutputPortIndex = 0 });

            // 저장된 그래프만 서브에셋으로 붙인다(검증용 메모리 그래프는 에셋이 아니므로).
            if (AssetDatabase.Contains(graph))
                AssetDatabase.AddObjectToAsset(tail, graph);
            Undo.RegisterCreatedObjectUndo(tail, "Split Block");
            graph.Nodes.Add(tail);
            EditorUtility.SetDirty(block);
            EditorUtility.SetDirty(graph);
            return tail;
        }

        // 블록이 조건 없는 Next 하나로만 다음 블록에 이어지고, 다음 블록으로 들어오는 연결이 그것 하나뿐일 때만 합칠 수 있다.
        public static bool TryGetMergeTarget(
            NarrativeGraphSO graph,
            EpisodeNodeSO block,
            out EpisodeNodeSO next,
            out string reason)
        {
            next = null;
            if (graph == null || block == null) { reason = "그래프가 없습니다."; return false; }

            EpisodeEvent terminal = NarrativeBlockModel.GetTerminalEvent(block);
            if (terminal != null && NarrativeBlockModel.EndsBlock(terminal))
            { reason = "선택지·제조로 끝나는 블록은 합칠 수 없습니다."; return false; }

            if (block.OutgoingBranches.Count != 1
                || BranchLabel.Parse(block.OutgoingBranches[0]).Kind != BranchLabelKind.Next)
            { reason = "조건 없는 Next 포트 하나만 있어야 합칠 수 있습니다."; return false; }

            List<EdgeData> outgoing = graph.Edges.Where(e => e.BaseNodeGuid == block.Guid).ToList();
            if (outgoing.Count != 1)
            { reason = "다음 블록이 연결되어 있지 않습니다."; return false; }

            string targetGuid = outgoing[0].TargetNodeGuid;
            next = graph.Nodes.OfType<EpisodeNodeSO>().FirstOrDefault(n => n.Guid == targetGuid);
            if (next == null || next == block)
            { reason = "다음 노드가 블록이 아닙니다(Trigger 등)."; next = null; return false; }

            if (graph.Edges.Count(e => e.TargetNodeGuid == targetGuid) != 1 || graph.StartNodeGuid == targetGuid)
            { reason = "다음 블록을 다른 곳에서도 가리키고 있어 합칠 수 없습니다."; next = null; return false; }

            reason = string.Empty;
            return true;
        }

        public static bool MergeWithNext(NarrativeGraphSO graph, EpisodeNodeSO block)
        {
            if (!TryGetMergeTarget(graph, block, out EpisodeNodeSO next, out _))
                return false;

            Undo.RecordObjects(new UnityEngine.Object[] { graph, block }, "Merge Blocks");
            block.Events.AddRange(next.Events);
            block.OutgoingBranches = new List<string>(next.OutgoingBranches);
            graph.Edges.RemoveAll(e => e.BaseNodeGuid == block.Guid);
            for (int i = 0; i < graph.Edges.Count; i++)
            {
                if (graph.Edges[i].BaseNodeGuid != next.Guid) continue;
                EdgeData edge = graph.Edges[i];
                edge.BaseNodeGuid = block.Guid;
                graph.Edges[i] = edge;
            }
            graph.Nodes.Remove(next);
            Undo.DestroyObjectImmediate(next);
            EditorUtility.SetDirty(block);
            EditorUtility.SetDirty(graph);
            return true;
        }
    }
}
