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
        AfterTimer,
        [InspectorName("고정 영업 슬롯")]
        SequenceSlot
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
        [Tooltip("timing이 SequenceSlot일 때 실행할 1부터 시작하는 영업 슬롯입니다.")]
        [Min(0)] public int sequenceSlot;
        public CustomerVisitData customerVisit;
        public EpisodeData encounterEpisode;

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

    [Serializable]
    public sealed class BusinessRandomEncounterEntry
    {
        [Tooltip("EpisodeType이 Encounter인 에피소드만 등록할 수 있습니다.")]
        public EpisodeData episode;
        [Tooltip("일반 손님과 함께 추첨할 때 사용하는 상대 가중치입니다.")]
        [Min(0f)] public float weight = 1f;

        public string TargetKey => episode != null && !string.IsNullOrWhiteSpace(episode.episodeId)
            ? "episode:" + episode.episodeId
            : string.Empty;
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

        [Header("랜덤 인카운터 풀")]
        [Tooltip("조건을 만족한 미완료 Encounter 에피소드만 손님과 함께 가중치 추첨합니다. 각 에피소드 ID는 영업일당 최대 1회입니다.")]
        public List<BusinessRandomEncounterEntry> randomEncounters = new();

        [Header("필수 영업 액션")]
        public List<BusinessRequiredActionRule> requiredActions = new();

        [Header("보상")]
        public int goodMoneyReward = 100;
        public int midMoneyReward = 50;
        public int badMoneyReward;
        [Tooltip("CSV 레시피 가격에 곱하는 Good 지급 비율입니다.")]
        [Min(0f)] public float goodRecipePriceMultiplier = 1f;
        [Tooltip("CSV 레시피 가격에 곱하는 Mid 지급 비율입니다.")]
        [Min(0f)] public float midRecipePriceMultiplier = 0.5f;
        [Tooltip("CSV 레시피 가격에 곱하는 Bad 지급 비율입니다.")]
        [Min(0f)] public float badRecipePriceMultiplier;
        public int goodReputationReward = 2;
        public int midReputationReward;
        public int badReputationReward = -1;
        [Tooltip("Bad 판정 시 판매 수익 지급 직후 추가로 차감하는 실수 페널티 비율입니다(정가 대비). 130%면 정가를 다시 지급받고 130%를 차감해 순수익이 -30%가 됩니다.")]
        [Min(0f)] public float badPenaltyRate = 1.3f;

        [Header("팁")]
        [Range(0f, 1f)] public float satisfiedTipRate = 0.3f;
        [Range(0f, 1f)] public float neutralTipRate = 0f;
        [Range(0f, 1f)] public float dissatisfiedTipRate;

        [Header("기본 반응 대사")]
        public string feedbackSpeakerName = "손님";
        [TextArea(2, 4)] public string goodFeedbackText = "완벽해. 딱 원하던 맛이야.";
        [TextArea(2, 4)] public string midFeedbackText = "비슷하긴 한데, 뭔가 조금 아쉬워.";
        [TextArea(2, 4)] public string badFeedbackText = "이건 내가 주문한 술이 아니야.";

        [Header("누락 결과 임시 대사")]
        public string missingGoodFeedbackText = "goodjob_result_dummy";
        public string missingMidIceFeedbackText = "midjob_ice_result_dummy";
        public string missingMidGlassFeedbackText = "midjob_glass_result_dummy";
        public string missingMidIceGlassFeedbackText = "midjob_ice_glass_result_dummy";
        public string missingMidWrongMenuFeedbackText = "midjob_wrongmenu_result_dummy";
        public string missingBadFeedbackText = "badjob_result_dummy";

        public int GetMoneyReward(OrderEvaluationGrade grade)
        {
            return grade switch
            {
                OrderEvaluationGrade.Good => goodMoneyReward,
                OrderEvaluationGrade.Mid => midMoneyReward,
                _ => badMoneyReward
            };
        }

        public float GetRecipePriceMultiplier(OrderEvaluationGrade grade)
        {
            return grade switch
            {
                OrderEvaluationGrade.Good => goodRecipePriceMultiplier,
                OrderEvaluationGrade.Mid => midRecipePriceMultiplier,
                _ => badRecipePriceMultiplier
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

        public int GetReputationReward(CustomerMood mood)
        {
            return mood switch
            {
                CustomerMood.Satisfied => goodReputationReward,
                CustomerMood.Neutral => midReputationReward,
                CustomerMood.Dissatisfied => badReputationReward,
                _ => 0
            };
        }

        public float GetTipRate(CustomerMood mood)
        {
            return mood switch
            {
                CustomerMood.Satisfied => satisfiedTipRate,
                CustomerMood.Neutral => neutralTipRate,
                CustomerMood.Dissatisfied => dissatisfiedTipRate,
                _ => 0f
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

        public string GetMissingFeedbackDummy(CraftingJobResult result)
        {
            return result switch
            {
                CraftingJobResult.Good => missingGoodFeedbackText,
                CraftingJobResult.MidIce => missingMidIceFeedbackText,
                CraftingJobResult.MidGlass => missingMidGlassFeedbackText,
                CraftingJobResult.MidIceGlass => missingMidIceGlassFeedbackText,
                CraftingJobResult.MidWrongMenu => missingMidWrongMenuFeedbackText,
                _ => missingBadFeedbackText
            };
        }
    }
}
