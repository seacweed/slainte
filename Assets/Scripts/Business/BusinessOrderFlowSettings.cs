using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Business
{
    public enum BusinessRequiredActionType
    {
        [InspectorName("필수 손님")]
        CustomerVisit,
        [InspectorName("필수 인카운터")]
        EncounterEpisode
    }

    public enum BusinessRequiredActionTiming
    {
        [InspectorName("첫 손님 이전")]
        BeforeFirstCustomer,
        [InspectorName("주문 사이")]
        BetweenOrders,
        [InspectorName("영업시간 종료 후")]
        AfterTimer
    }

    [Serializable]
    public sealed class BusinessRequiredActionRule
    {
        [Tooltip("같은 영업 중 중복 실행을 막는 고유 키입니다.")]
        public string ruleId;
        public BusinessRequiredActionType actionType;
        [Tooltip("0이면 특정 일차를 요구하지 않습니다.")]
        [Min(0)] public int exactDay;
        public EpisodeTriggerCondition condition = new();
        [Tooltip("여러 필수 규칙이 동시에 활성화되면 큰 값부터 실행합니다.")]
        public int priority;
        public BusinessRequiredActionTiming timing = BusinessRequiredActionTiming.BeforeFirstCustomer;
        public CustomerVisitData customerVisit;
        public EpisodeData encounterEpisode;
        [Tooltip("완료 처리된 에피소드도 다시 인카운터로 실행할 수 있게 합니다.")]
        public bool allowCompletedEpisode;

        public string TargetKey
        {
            get
            {
                if (actionType == BusinessRequiredActionType.CustomerVisit)
                    return customerVisit != null && !string.IsNullOrWhiteSpace(customerVisit.visitKey)
                        ? "customer:" + customerVisit.visitKey
                        : string.Empty;

                return encounterEpisode != null && !string.IsNullOrWhiteSpace(encounterEpisode.episodeId)
                    ? "episode:" + encounterEpisode.episodeId
                    : string.Empty;
            }
        }
    }

    [CreateAssetMenu(menuName = "Slainte/Business/Order Flow Settings", fileName = "BusinessOrderFlowSettings")]
    public sealed class BusinessOrderFlowSettings : ScriptableObject
    {
        [Header("영업 진행")]
        public bool autoStart = true;
        [InspectorName("영업 제한시간(초)")]
        [Min(1f)] public float shiftDurationSeconds = 180f;

        [Header("손님 풀")]
        [InspectorName("손님 방문 데이터베이스")]
        public CustomerVisitDatabase customerVisitDatabase;

        [Header("필수 영업 액션")]
        public List<BusinessRequiredActionRule> requiredActions = new();

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
