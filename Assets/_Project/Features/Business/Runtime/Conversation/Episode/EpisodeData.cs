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

// 에피소드 하나는 항상 영업 중 특정 (day, slot)에 배정되는 인카운터다.
// 같은 (챕터, day, slot)에 여러 에피소드가 배정되면 slotPriority가 큰 것부터 triggerCondition(등장 조건)을
// 확인해 처음 만족하는 하나만 실행하고, 아무것도 만족하지 않으면 그 슬롯은 랜덤 손님으로 채워진다.
[CreateAssetMenu(menuName = "Slainte/Episode Data", fileName = "EpisodeData_")]
public class EpisodeData : ScriptableObject
{
    public const int UnscheduledDay = 0;

    [Header("Identity")]
    public string episodeId;
    public string episodeTitle;
    public string chapterId;

    [Header("Schedule")]
    [Tooltip("등장하는 날짜(1부터). 0이면 일정에 배정되지 않아 영업에서 자동으로 등장하지 않습니다.")]
    [Min(0)] public int scheduledDay = UnscheduledDay;
    [Tooltip("그 날의 손님 슬롯 번호(1부터).")]
    [Min(0)] public int scheduledSlot;
    [Tooltip("같은 날·슬롯에 후보가 여럿일 때 큰 값부터 등장 조건을 확인합니다.")]
    public int slotPriority;

    [Header("Trigger")]
    public EpisodeTriggerCondition triggerCondition; // 등장 조건 — 슬롯 차례가 왔을 때 판정

    public string firstNodeId;

    [Header("Nodes")]
    public List<EpisodeNode> nodes = new();

    [Header("Settlement")]
    [Tooltip("에피소드 종료 시 requiredFlag가 서 있으면 정산 화면에 label/amount를 커스텀 보상 줄로 추가합니다.")]
    public List<EpisodeSettlementReward> settlementRewards = new();

    public bool IsScheduled => scheduledDay > UnscheduledDay && scheduledSlot > 0;

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
