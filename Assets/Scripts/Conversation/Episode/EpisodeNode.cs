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

    [Header("Episode Branches")]
    public List<NodeEpisodeBranch> episodeBranches = new();

    [Header("Presentation (optional)")]
    public List<CharacterSlotEntry> characters = new();

    [Header("Crafting (optional)")]
    public bool   requiresCrafting = false;
    public string craftingTicketKey;
    public List<CraftingOutcome> craftingOutcomes = new();

    [Header("BGM (optional)")]
    public BgmCommand bgmCommand = BgmCommand.None;
    public string bgmClipName;

    public CraftingOutcome GetCraftingOutcome(CraftingJobResult result) =>
        craftingOutcomes.Find(o => o.result == result);

    private CraftingOutcome GetOrAddCraftingOutcome(CraftingJobResult result)
    {
        var outcome = GetCraftingOutcome(result);
        if (outcome == null)
        {
            outcome = new CraftingOutcome { result = result };
            craftingOutcomes.Add(outcome);
        }
        return outcome;
    }

    public string GetNextNodeId(CraftingJobResult result) => GetCraftingOutcome(result)?.nextNodeId;
    public void   SetNextNodeId(CraftingJobResult result, string nodeId) => GetOrAddCraftingOutcome(result).nextNodeId = nodeId;

    public string GetCraftingFlag(CraftingJobResult result) => GetCraftingOutcome(result)?.flag;
    public void   SetCraftingFlag(CraftingJobResult result, string flag) => GetOrAddCraftingOutcome(result).flag = flag;

    public List<VarChange> GetCraftingVarChanges(CraftingJobResult result) => GetCraftingOutcome(result)?.varChanges ?? new();
    public void             SetCraftingVarChanges(CraftingJobResult result, List<VarChange> list) => GetOrAddCraftingOutcome(result).varChanges = list;
}
