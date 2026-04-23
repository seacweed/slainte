using System;
using System.Collections.Generic;

namespace NarrativeFlow.Runtime
{
    [Serializable]
    public class RuntimeNarrativeData
    {
        public List<RuntimeNode> Nodes = new List<RuntimeNode>(); 
    }

    [Serializable]
    public class RuntimeNode
    {
        public string NodeId;
        public string NodeType;
        public string Title;
        public string ContentText;  
        public string SpeakerId;
        public string Condition;
        public string EventMethod;
        public string EventParams;
        public List<string> NextNodeIds = new List<string>();
        public List<CustomNodeFieldData> CustomFields = new List<CustomNodeFieldData>();
    }

    [Serializable]
    public class CustomNodeFieldData
    {
        public string Key;
        public string Value;
    }
}