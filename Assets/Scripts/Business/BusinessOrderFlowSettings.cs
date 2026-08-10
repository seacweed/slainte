using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Business
{
    public enum BusinessSequenceMode
    {
        [InspectorName("고정 주문")]
        Fixed,
        [InspectorName("손님 풀")]
        CustomerPool
    }

    [Serializable]
    public sealed class FixedBusinessOrder
    {
        public string customerOrderKey;
        public string requestedRecipeId;
    }

    [CreateAssetMenu(menuName = "Slainte/Business/Order Flow Settings", fileName = "BusinessOrderFlowSettings")]
    public sealed class BusinessOrderFlowSettings : ScriptableObject
    {
        [Header("영업 순서")]
        public bool autoStart = true;
        [InspectorName("진행 방식")]
        public BusinessSequenceMode sequenceMode = BusinessSequenceMode.Fixed;
        [InspectorName("고정 주문 목록")]
        public List<FixedBusinessOrder> fixedOrders = new();

        [Header("손님 풀")]
        [InspectorName("손님 방문 데이터베이스")]
        public CustomerVisitDatabase customerVisitDatabase;
        [InspectorName("하루 최소 방문 수")]
        [Min(0)] public int minVisitsPerDay = 15;
        [InspectorName("하루 최대 방문 수")]
        [Min(0)] public int maxVisitsPerDay = 15;

        [Header("판정")]
        [Range(0f, 1f)] public float midScoreThreshold = 0.45f;
        [Range(0f, 1f)] public float goodScoreThreshold = 0.8f;

        [Header("보상")]
        public int goodMoneyReward = 100;
        public int midMoneyReward = 50;
        public int badMoneyReward;
        public int goodReputationReward = 2;
        public int midReputationReward;
        public int badReputationReward = -1;

        [Header("기본 반응 대사")]
        public string feedbackSpeakerName = "손님";
        [TextArea(2, 4)] public string goodFeedbackText = "완벽해. 딱 원하던 맛이야.";
        [TextArea(2, 4)] public string midFeedbackText = "비슷하긴 한데, 뭔가 조금 아쉬워.";
        [TextArea(2, 4)] public string badFeedbackText = "이건 내가 주문한 술이 아니야.";

        public int GetMoneyReward(OrderEvaluationGrade grade)
        {
            return grade switch
            {
                OrderEvaluationGrade.Good => goodMoneyReward,
                OrderEvaluationGrade.Mid => midMoneyReward,
                _ => badMoneyReward
            };
        }

        public int GetReputationReward(OrderEvaluationGrade grade)
        {
            return grade switch
            {
                OrderEvaluationGrade.Good => goodReputationReward,
                OrderEvaluationGrade.Mid => midReputationReward,
                _ => badReputationReward
            };
        }

        public string GetFallbackFeedback(OrderEvaluationGrade grade)
        {
            return grade switch
            {
                OrderEvaluationGrade.Good => goodFeedbackText,
                OrderEvaluationGrade.Mid => midFeedbackText,
                _ => badFeedbackText
            };
        }
    }
}
