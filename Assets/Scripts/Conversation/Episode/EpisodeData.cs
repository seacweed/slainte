using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct EpisodeCharacter
{
    public string characterName;
}

[CreateAssetMenu(menuName = "Slainte/Episode Data", fileName = "EpisodeData_")]
public class EpisodeData : ScriptableObject
{
    [Header("Identity")]
    public string episodeId;
    public string episodeTitle;

    [Header("Trigger")]
    public EpisodeTriggerCondition triggerCondition;

    [Header("Opening")]
    public List<CharacterSlotEntry> openingCharacters = new();
    public string firstNodeId;

    [Header("Nodes")]
    public List<EpisodeNode> nodes = new();

    [Header("Board Display")]
    [TextArea(3, 5)] public string episodeDescription;
    public string iconNameBoard;
    public string iconNameArchive;
    public List<EpisodeCharacter> characters = new();
    public List<string> customConditionTexts = new(); // UI에서 표시할 예시: "A에게 돈 10000원 지급"

    public EpisodeNode FindNode(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return null;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node != null && string.Equals(node.nodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                return node;
        }

        return null;
    }
}
