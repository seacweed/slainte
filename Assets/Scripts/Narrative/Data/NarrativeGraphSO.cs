using System.Collections.Generic;
using UnityEngine;

namespace NarrativeFlow
{
    [CreateAssetMenu(fileName = "New Narrative Graph", menuName = "Narrative/Graph")]
    public class NarrativeGraphSO : ScriptableObject
    {
        public List<NodeDataSO> Nodes = new List<NodeDataSO>();
        public List<EdgeData> Edges = new List<EdgeData>();
    }
}