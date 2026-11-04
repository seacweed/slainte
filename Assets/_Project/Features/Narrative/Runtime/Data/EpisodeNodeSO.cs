using System.Collections.Generic;
using UnityEngine;

namespace NarrativeFlow
{
    public class EpisodeNodeSO : NodeDataSO
    {
        [Header("Episode Content")]
        [HideInInspector] public string StartEventGuid; // 레거시 — MigrateLegacyOrder 참고
        public List<EpisodeEvent> Events = new();
        public List<string> OutgoingBranches = new() { "Next" }; // Default branch

        // 이벤트가 하나도 없는데 조건 분기 포트를 가진 블록은 라우터 런타임 노드로 컴파일된다(그때의 nodeId).
        public string RouterNodeId;

        // 에디터 표시 상태: 카드에서 이벤트 목록을 전부 펼쳐 볼지(접으면 앞 몇 줄만).
        public bool ShowAllEvents;

        private void Awake()
        {
            // Default fields for metadata
            if (CustomFields == null || CustomFields.Count == 0)
            {
                CustomFields = new List<CustomNodeField>
                {
                    new CustomNodeField { FieldName = "Title", FieldValue = "New Block" },
                    new CustomNodeField { FieldName = "Description", FieldValue = "" }
                };
            }
        }
    }
}
