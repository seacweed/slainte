using System;
using System.Collections.Generic;
using UnityEngine;

namespace NarrativeFlow
{
    public enum EpisodeEventType
    {
        Dialogue,
        Choice,
        BusinessStart,
        BusinessEnd,
        BranchExit
    }

    [Serializable]
    public class EpisodeEvent
    {
        public string Guid = System.Guid.NewGuid().ToString();
        public Vector2 Position;
        public List<string> NextEventGuids = new();

        public EpisodeEventType Type;

        // Dialogue Fields
        public string SpeakerKey;
        public string OverrideSpeakerName;
        [TextArea(2, 5)]
        public string Text;
        public List<CharacterSlotEntryData> CharacterAppearances = new();

        // Choice Fields
        public List<ChoiceOptionData> Choices = new();

        // BGM Fields (applied on any event, usually Dialogue)
        public BgmCommand BgmCommand;
        public string BgmClipName;

        // Business Fields
        public string CraftingTicketKey;
        public string CraftingFlagGood;
        public string CraftingFlagBad;
        public List<VarChangeData> CraftingVarChangesGood = new();
        public List<VarChangeData> CraftingVarChangesBad  = new();

        // Branch Exit
        public string ExitBranchName;
    }

    [Serializable]
    public class CharacterSlotEntryData
    {
        public string CharacterKey;
        public string ExpressionKey;
        public int SlotIndex = -1;
    }

    [Serializable]
    public class ChoiceOptionData
    {
        public string ButtonText;
        public string TargetNodeId; // Graph output port will handle this
        public List<string> SetFlags = new();
        public List<string> ClearFlags = new();
        public List<VarChangeData> VarChanges = new();
    }

    [Serializable]
    public class VarChangeData
    {
        public string VarName;
        public int Delta;
    }
}
