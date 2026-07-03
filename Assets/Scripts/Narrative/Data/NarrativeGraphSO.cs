using System.Collections.Generic;
using UnityEngine;

namespace NarrativeFlow
{
    [CreateAssetMenu(fileName = "New Narrative Graph", menuName = "Narrative/Graph")]
    public class NarrativeGraphSO : ScriptableObject
    {
        [Header("Episode Identity")]
        public string EpisodeId;
        public string EpisodeTitle;
        public string StartNodeGuid;

        [Header("Trigger")]
        public EpisodeTriggerCondition TriggerCondition = new();

        [Header("Opening")]
        public List<CharacterSlotEntry> OpeningCharacters = new();

        [Header("Graph Data")]
        public List<NodeDataSO> Nodes = new List<NodeDataSO>();
        public List<EdgeData> Edges = new List<EdgeData>();
    }
}