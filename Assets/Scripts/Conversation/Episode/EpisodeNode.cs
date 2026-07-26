using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class EpisodeNode
{
    [Header("Identity")]
    public string nodeId;

    [Header("Dialogue")]
    public string speakerKey;
    public string overrideSpeakerName;
    [TextArea(2, 6)]
    public string text;

    [Header("Flow")]
    public string nextNodeId;

    [Header("Choices")]
    public List<EpisodeChoice> choices = new();

    [Header("Flag Branches")]
    public List<NodeFlagBranch> flagBranches = new();

    [Header("Var Branches")]
    public List<NodeVarBranch> varBranches = new();

    [Header("Presentation (optional)")]
    public List<CharacterSlotEntry> characters = new();

    [Header("Crafting (optional)")]
    public bool   requiresCrafting = false;
    public string craftingTicketKey;
    public string craftingRecipeId;
    public string nextNodeIdGood;
    public string nextNodeIdBad;
    public string craftingFlagGood;
    public string craftingFlagBad;
    public List<VarChange> craftingVarChangesGood = new();
    public List<VarChange> craftingVarChangesBad  = new();

    [Header("BGM (optional)")]
    public BgmCommand bgmCommand = BgmCommand.None;
    public string bgmClipName;
}
