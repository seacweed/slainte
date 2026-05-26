using System;

namespace NarrativeFlow
{
    [Serializable]
    public struct EdgeData
    {
        public string BaseNodeGuid;
        public string TargetNodeGuid;
        public int OutputPortIndex;
    }
}