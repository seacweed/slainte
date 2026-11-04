using Slainte.Content;
using UnityEngine;

namespace Slainte.Business
{
    [CreateAssetMenu(menuName = "Slainte/Business/Order Flow Settings", fileName = "BusinessOrderFlowSettings")]
    public sealed class BusinessOrderFlowSettings : ScriptableObject
    {
        public const string ResourcePath = ProjectResourcePaths.BusinessOrderFlowSettings;

        [Header("영업 진행")]
        public bool autoStart = true;
        [InspectorName("하루 손님 수")]
        [Tooltip("하루 영업의 손님 슬롯 수입니다. 각 슬롯은 배정된 에피소드 또는 랜덤 손님으로 채워집니다.")]
        [Min(1)] public int customersPerDay = 5;

        [Header("손님 풀")]
        [InspectorName("손님 방문 데이터베이스")]
        public CustomerVisitDatabase customerVisitDatabase;

        [Header("기능 해금")]
        [Tooltip("이 에피소드를 완료하면 배송 버튼이 생성됩니다. 비어 있거나 아직 존재하지 않는 ID는 잠금 상태로 처리합니다.")]
        public string deliveryUnlockEpisodeId;
        [Tooltip("이 에피소드를 완료하면 TV를 클릭할 수 있습니다. 비어 있거나 아직 존재하지 않는 ID는 잠금 상태로 처리합니다.")]
        public string tvUnlockEpisodeId;

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
        [Tooltip("거물(big_fish) Good 판정 시 정가에 더하는 보너스 비율입니다. 200%면 최종 수익은 정가의 300%입니다.")]
        [Min(0f)] public float bigFishGoodBonusRate = 2f;
        [Tooltip("거물(big_fish) Mid/Bad 판정 시 정가 수익에서 차감하는 페널티 비율입니다. 300%면 최종 수익은 정가의 -200%입니다.")]
        [Min(0f)] public float bigFishFailurePenaltyRate = 3f;

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

        public static BusinessOrderFlowSettings LoadDefault()
        {
            return Resources.Load<BusinessOrderFlowSettings>(ResourcePath);
        }

        public bool IsDeliveryUnlocked(GameProgress progress)
        {
            return IsEpisodeFeatureUnlocked(progress, deliveryUnlockEpisodeId);
        }

        public bool IsTVUnlocked(GameProgress progress)
        {
            return IsEpisodeFeatureUnlocked(progress, tvUnlockEpisodeId);
        }

        private static bool IsEpisodeFeatureUnlocked(
            GameProgress progress,
            string episodeId)
        {
            return progress != null
                && !string.IsNullOrWhiteSpace(episodeId)
                && progress.IsEpisodeCompleted(episodeId.Trim());
        }

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
