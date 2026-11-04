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

        [Header("Schedule")]
        [Min(0)] public int ScheduledDay;  // EpisodeData.scheduledDay — 0이면 일정 미배정
        [Min(0)] public int ScheduledSlot; // EpisodeData.scheduledSlot — 1부터
        public int SlotPriority;           // 같은 슬롯 후보 간 우선순위(큰 값 먼저)

        [Header("Trigger")]
        public EpisodeTriggerCondition TriggerCondition = new(); // 등장 조건 — 슬롯 차례에 판정

        [Header("Settlement")]
        public List<EpisodeSettlementReward> SettlementRewards = new();

        [Header("CSV Sync")]
        public string SourceCsvPath;     // 동기화 대상 원본 CSV(프로젝트 상대 경로)
        public string LastSyncedCsvHash; // 마지막으로 읽거나 쓴 시점의 원본 CSV 해시 — 외부 수정 감지용

        [Header("Graph Data")]
        public List<NodeDataSO> Nodes = new List<NodeDataSO>();
        public List<EdgeData> Edges = new List<EdgeData>();
    }
}