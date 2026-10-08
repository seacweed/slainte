using System;
using System.Collections.Generic;
using Slainte.Bartending;
using Slainte.Economy;
using UnityEngine;

namespace NarrativeFlow
{
    public enum EpisodeEventType
    {
        Dialogue,
        Choice,
        BusinessStart,
        BusinessEnd, // 레거시(런타임 노드 없음) — 직렬화 호환용으로만 남김
        BranchExit   // 레거시(다음 노드가 없으면 어차피 끝나므로 제거됨) — 직렬화 호환용으로만 남김
    }

    [Serializable]
    public class EpisodeEvent
    {
        public string Guid = System.Guid.NewGuid().ToString();
        // 레거시: 예전 시퀀스 에디터가 쓰던 위치·연결. 이벤트 순서는 이제 EpisodeNodeSO.Events 리스트 순서이며,
        // 그래프를 열 때 NarrativeBlockModel.MigrateLegacyOrder가 이 값을 한 번 읽어 리스트를 정렬한 뒤 비운다.
        [HideInInspector] public Vector2 Position;
        [HideInInspector] public List<string> NextEventGuids = new();

        public EpisodeEventType Type;

        // 이 이벤트가 컴파일될 런타임 EpisodeNode.nodeId. CSV에서 가져온 ID를 그대로 보존하고, 새로 만든
        // 이벤트는 첫 컴파일 때 부여된 ID를 기록해 둔다 — 그래야 그래프↔CSV 왕복 시 노드 ID가 흔들리지 않는다.
        public string RuntimeNodeId;

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

        // SFX Fields (applied on any event, usually Dialogue)
        public SfxCommand SfxCommand;
        public string SfxClipName;

        // Business Fields
        [Tooltip("제조 중 표시할 주문서 에셋입니다. Ticket Key는 이전 데이터 호환용입니다.")]
        public OrderTicketData CraftingOrderTicket;
        public string CraftingTicketKey;
        public CocktailOrderType CraftingOrderType = CocktailOrderType.EpisodeOrder;
        [UnityEngine.Serialization.FormerlySerializedAs("CraftingRecipeId")]
        public string CraftingOrderTarget;
        public bool CraftingPaymentEnabled = true;
        public GameCurrency CraftingPaymentCurrency = GameCurrency.Money;
        [Min(0.01f)] public float CraftingPaymentMultiplier = 1f;
        public List<CraftingOutcomeData> CraftingOutcomes = new();

        // Branch Exit
        public string ExitBranchName;

        private CraftingOutcomeData GetOrAddCraftingOutcome(CraftingJobResult result)
        {
            var outcome = CraftingOutcomes.Find(o => o.Result == result);
            if (outcome == null)
            {
                outcome = new CraftingOutcomeData { Result = result };
                CraftingOutcomes.Add(outcome);
            }
            return outcome;
        }

        public string GetCraftingFlag(CraftingJobResult result) => CraftingOutcomes.Find(o => o.Result == result)?.Flag;
        public void   SetCraftingFlag(CraftingJobResult result, string flag) => GetOrAddCraftingOutcome(result).Flag = flag;

        public List<VarChangeData> GetCraftingVarChanges(CraftingJobResult result) => CraftingOutcomes.Find(o => o.Result == result)?.VarChanges ?? new();
        public void                 SetCraftingVarChanges(CraftingJobResult result, List<VarChangeData> list) => GetOrAddCraftingOutcome(result).VarChanges = list;

        // 편집 UI용: 결과 항목이 없으면 만들어 실제 리스트를 돌려준다(GetCraftingVarChanges는 없을 때 임시 리스트라 수정이 버려짐).
        public List<VarChangeData>  GetCraftingVarChangesForEdit(CraftingJobResult result) => GetOrAddCraftingOutcome(result).VarChanges;
    }

    [Serializable]
    public class CraftingOutcomeData
    {
        public CraftingJobResult Result;
        public string Flag;
        public List<VarChangeData> VarChanges = new();
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
        [HideInInspector] public string TargetNodeId; // 레거시(예전 시퀀스 에디터 내부 연결) — 선택지 경로는 블록 출력 포트로 연결
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
