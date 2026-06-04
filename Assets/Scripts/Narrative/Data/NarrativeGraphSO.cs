using System.Collections.Generic;
using UnityEngine;

namespace NarrativeFlow
{
    [CreateAssetMenu(fileName = "New Narrative Graph", menuName = "Narrative/Graph")]
    public class NarrativeGraphSO : ScriptableObject
    {
        [Header("Identity & Setup")]
        public string episodeTitle;
        public EpisodeTriggerCondition triggerCondition = new();
        public List<CharacterSlotEntry> openingCharacters = new();

        [Header("Board UI Display (Optional)")]
        [TextArea(3, 5)] public string episodeDescription;
        public string iconNameBoard;
        public string iconNameArchive;
        public List<EpisodeCharacter> characters = new();
        public List<string> customConditionTexts = new();

        [HideInInspector]
        public List<NodeDataSO> Nodes = new List<NodeDataSO>();
        [HideInInspector]
        public List<EdgeData> Edges = new List<EdgeData>();
    }
}