using System;
using System.Collections.Generic;
using UnityEngine;

namespace NarrativeFlow
{
    public enum TriggerConditionType
    {
        Flag,
        Variable
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
    }
}
