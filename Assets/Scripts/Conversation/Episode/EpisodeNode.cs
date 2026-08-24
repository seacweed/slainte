using System;
using System.Collections.Generic;
using Slainte.Bartending;
using Slainte.Economy;
using UnityEngine;
using UnityEngine.Serialization;

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
    [Tooltip("제조 중 표시할 주문서 에셋입니다. 기존 데이터는 craftingTicketKey로 폴백합니다.")]
    public OrderTicketData craftingOrderTicket;
    public string craftingTicketKey;
    [Tooltip("EpisodeOrder는 레시피 ID, TasteOrder/MoodOrder는 단일 태그를 대상으로 사용합니다.")]
    public CocktailOrderType craftingOrderType = CocktailOrderType.EpisodeOrder;
    [FormerlySerializedAs("craftingRecipeId")]
    [Tooltip("주문 유형에 따라 레시피 ID 또는 맛/분위기 태그를 입력합니다.")]
    public string craftingOrderTarget;
    [Tooltip("실제 제조 주문 완료 시 선택한 통화로 레시피 가격을 지급합니다. 무료 제공 에피소드만 비활성화합니다.")]
    public bool craftingPaymentEnabled = true;
    public GameCurrency craftingPaymentCurrency = GameCurrency.Money;
    public List<CraftingOutcome> craftingOutcomes = new();

    [HideInInspector, SerializeField] private string nextNodeIdGood;
    [HideInInspector, SerializeField] private string nextNodeIdBad;
    [HideInInspector, SerializeField] private string craftingFlagGood;
    [HideInInspector, SerializeField] private string craftingFlagBad;
    [HideInInspector, SerializeField] private List<VarChange> craftingVarChangesGood = new();
    [HideInInspector, SerializeField] private List<VarChange> craftingVarChangesBad = new();

    [Header("BGM (optional)")]
    public BgmCommand bgmCommand = BgmCommand.None;
    public string bgmClipName;

    [Header("SFX (optional)")]
    public SfxCommand sfxCommand = SfxCommand.None;
    public string sfxClipName;

    public CraftingOutcome GetCraftingOutcome(CraftingJobResult result) =>
        craftingOutcomes?.Find(o => o.result == result);

    private CraftingOutcome GetOrAddCraftingOutcome(CraftingJobResult result)
    {
        craftingOutcomes ??= new List<CraftingOutcome>();
        var outcome = GetCraftingOutcome(result);
        if (outcome == null)
        {
            outcome = new CraftingOutcome { result = result };
            craftingOutcomes.Add(outcome);
        }
        return outcome;
    }

    public string GetNextNodeId(CraftingJobResult result)
    {
        CraftingOutcome outcome = GetResolvedCraftingOutcome(result);
        if (outcome != null)
            return outcome.nextNodeId;

        return result == CraftingJobResult.Good ? nextNodeIdGood : nextNodeIdBad;
    }
    public void   SetNextNodeId(CraftingJobResult result, string nodeId) => GetOrAddCraftingOutcome(result).nextNodeId = nodeId;

    public string GetCraftingFlag(CraftingJobResult result)
    {
        CraftingOutcome outcome = GetResolvedCraftingOutcome(result);
        if (outcome != null)
            return outcome.flag;

        return result == CraftingJobResult.Good ? craftingFlagGood : craftingFlagBad;
    }
    public void   SetCraftingFlag(CraftingJobResult result, string flag) => GetOrAddCraftingOutcome(result).flag = flag;

    public List<VarChange> GetCraftingVarChanges(CraftingJobResult result)
    {
        CraftingOutcome outcome = GetResolvedCraftingOutcome(result);
        if (outcome != null)
            return outcome.varChanges ?? new List<VarChange>();

        List<VarChange> legacy = result == CraftingJobResult.Good
            ? craftingVarChangesGood
            : craftingVarChangesBad;
        return legacy ?? new List<VarChange>();
    }
    public void             SetCraftingVarChanges(CraftingJobResult result, List<VarChange> list) => GetOrAddCraftingOutcome(result).varChanges = list;

    private CraftingOutcome GetResolvedCraftingOutcome(CraftingJobResult result)
    {
        CraftingOutcome exact = GetCraftingOutcome(result);
        if (exact != null)
            return exact;

        return result != CraftingJobResult.Good
            ? GetCraftingOutcome(CraftingJobResult.Bad)
            : null;
    }
}
