using System;
using System.Collections.Generic;
using UnityEngine;

namespace NarrativeFlow
{
    public enum TriggerConditionType
    {
        Flag,
        Variable,
        Episode // Key = 완료해야 하는 에피소드 ID
    }

    [Serializable]
    public class GraphTriggerCondition
    {
        public TriggerConditionType Type;
        public string Key;
        public string Operator = "==";
        public string Value;
        public string TargetPortName; // For mapping to output ports
    }

    public class TriggerNodeSO : NodeDataSO
    {
        public List<GraphTriggerCondition> Conditions = new();

        // 선택지/제조 포트처럼 조건 분기를 직접 붙일 수 없는 곳에서 이 노드를 가리키면 독립 라우터
        // 런타임 노드로 컴파일되며, 그때 쓸 nodeId를 보존한다(비어 있으면 첫 컴파일 때 부여).
        public string RuntimeNodeId;
    }
}
