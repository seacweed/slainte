using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Business
{
    [Serializable]
    public sealed class FixedBusinessOrder
    {
        public string customerOrderKey;
        public string requestedRecipeId;
    }

    [CreateAssetMenu(menuName = "Slainte/Business/Order Flow Settings", fileName = "BusinessOrderFlowSettings")]
    public sealed class BusinessOrderFlowSettings : ScriptableObject
    {
        [Header("Sequence")]
        public bool autoStart = true;
        public List<FixedBusinessOrder> fixedOrders = new();

        [Header("Evaluation")]
        [Range(0f, 1f)] public float midScoreThreshold = 0.45f;
        [Range(0f, 1f)] public float goodScoreThreshold = 0.8f;

        [Header("Rewards")]
        public int goodMoneyReward = 100;
        public int midMoneyReward = 50;
        public int badMoneyReward;
        public int goodReputationReward = 2;
        public int midReputationReward;
        public int badReputationReward = -1;
        public int abandonReputationReward = -2;

        [Header("Fallback Feedback")]
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
