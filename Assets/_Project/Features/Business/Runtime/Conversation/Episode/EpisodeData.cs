using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class EpisodeSettlementReward
{
    public string requiredFlag;
    public string label;
    public int    amount;
}

[Serializable]
public struct CharacterDisplay
{
    public bool isHidden;       // true면 초상화를 비공개(???)로 표시
    public string characterName; // isHidden이 false일 때 사용
}

public enum SelectConditionType
{
    None,                // 조건 없음 — 항상 충족(토글이 항상 인터랙션 가능)
    MinDay,              // 최소 일수
    RequiredFlag,        // 이 플래그가 켜져 있어야 함
    PrerequisiteEpisode, // 이 에피소드가 완료되어야 함
    RequiredVar,         // 수치 변수 조건
    MinMoney             // 최소 소지금
}

// 선택 조건 옵션 하나가 가질 수 있는 조건은 정확히 하나(자물쇠 아이콘 하나 + 설명 한 줄에 대응).
// 여러 조건을 동시에 걸고 싶으면 옵션을 여러 개로 나눠서 표현한다.
[Serializable]
public class SelectSingleCondition
{
    public SelectConditionType type = SelectConditionType.None;
    public int    minDay;
    public string requiredFlag;
    public string prerequisiteEpisodeId;
    public string varName;
    public CompareOp varOp = CompareOp.GreaterOrEqual;
    public int    varThreshold;
    public int    minMoney;
}

// TRIGGER/PLAY_TRIGGER 조건 하나(자물쇠 아이콘 하나 + 설명 한 줄에 대응). 여러 개를 리스트로 두면 AND로 결합된다.
[Serializable]
public class TriggerConditionEntry
{
    public SelectSingleCondition condition = new();
    public string conditionText; // 커스텀 힌트 문구. 비어있으면 condition에서 자동 생성한 문구를 사용
}

// 선택 조건 옵션 하나. 여러 개를 리스트로 두되 동시에 하나만 on 가능(툴팁에서 라디오 버튼처럼 동작).
// flag가 비어있으면 이 옵션엔 토글 UI를 만들지 않는다.
[Serializable]
public class SelectConditionEntry
{
    public SelectSingleCondition condition = new();
    public string flag;
    public string conditionText; // 커스텀 힌트 문구. 비어있으면 condition에서 자동 생성한 문구를 사용

    // 이 옵션의 내용(조건 문구·아이콘)이 플레이어에게 공개되는 조건.
    // 기본값(None)이면 항상 공개 — 기존 데이터와 동일하게 동작
    public SelectSingleCondition revealCondition = new();

    // revealCondition 미충족일 때 conditionText 대신 보여줄 텍스트. 비어있으면 "???"로 표시.
    public string hiddenText;

    // 이 옵션이 선택됐을 때 보여줄 초상화. EpisodeData.characters와 같은 순서/슬롯 수로 채우면 됨.
    // 비어있으면(입력 안 하면) 기본 characters를 그대로 사용.
    public List<CharacterDisplay> characterOverrides = new();
}

[CreateAssetMenu(menuName = "Slainte/Episode Data", fileName = "EpisodeData_")]
public class EpisodeData : ScriptableObject
{
    [Header("Identity")]
    public string episodeId;
    public string episodeTitle;
    public string chapterId;

    [Header("Type")]
    public EpisodeType   episodeType   = EpisodeType.Default;
    public MandatorySlot mandatorySlot = MandatorySlot.None; // episodeType == Mandatory일 때만 사용

    [Header("Trigger")]
    public EpisodeTriggerCondition triggerCondition; // 해금 조건 — 만족하면 작전판에 노출
    public EpisodeTriggerCondition playCondition;     // 플레이 조건 — 만족해야 Play 버튼 활성화 (Default 전용)
    public List<TriggerConditionEntry> triggerConditionEntries = new(); // 해금 조건 항목별 툴팁 표시(순서·커스텀 텍스트 보존)
    public List<TriggerConditionEntry> playConditionEntries = new();    // 플레이 조건 항목별 툴팁 표시

    [Header("Select")]
    public List<SelectConditionEntry> selectConditions = new(); // 선택 조건 목록 — 동시에 하나만 on 가능. Play 버튼 활성화에는 영향 없음

    [Header("Opening")]
    public List<CharacterSlotEntry> openingCharacters = new();
    public string firstNodeId;

    [Header("Nodes")]
    public List<EpisodeNode> nodes = new();

    [Header("Settlement")]
    [Tooltip("에피소드 종료 시 requiredFlag가 서 있으면 정산 화면에 label/amount를 커스텀 보상 줄로 추가합니다.")]
    public List<EpisodeSettlementReward> settlementRewards = new();

    [Header("Board Display")]
    [TextArea(3, 5)] public string episodeDescription;
    public string iconNameBoard;
    public string iconNameArchive;
    public List<CharacterDisplay> characters = new(); // 선택 조건 미선택 시(기본) 보여줄 초상화

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
