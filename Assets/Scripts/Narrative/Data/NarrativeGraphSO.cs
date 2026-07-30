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
        public string ChapterId;

        [Header("Type")]
        public EpisodeType   EpisodeType   = EpisodeType.Default;
        public MandatorySlot MandatorySlot = MandatorySlot.None;

        [Header("Trigger")]
        public EpisodeTriggerCondition TriggerCondition = new(); // 해금 조건
        public EpisodeTriggerCondition PlayCondition = new();    // 플레이 조건

        [Header("Opening")]
        public List<CharacterSlotEntry> OpeningCharacters = new();

        [Header("Graph Data")]
        public List<NodeDataSO> Nodes = new List<NodeDataSO>();
        public List<EdgeData> Edges = new List<EdgeData>();
    }
}