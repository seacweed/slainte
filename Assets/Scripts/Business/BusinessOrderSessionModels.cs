using System;
using Slainte.Bartending;
using UnityEngine;

namespace Slainte.Business
{
    public enum OrderSessionOwner
    {
        Business,
        Episode
    }

    public enum OrderSessionOutcome
    {
        Served,
        Failed
    }

    public enum BusinessOrderSessionState
    {
        Idle,
        PresentingOrder,
        Crafting,
        Evaluating,
        PresentingFeedback,
        Completed
    }

    public sealed class OrderSessionRequest
    {
        public string sessionId;
        public OrderSessionOwner owner;
        public string customerOrderKey;
        public string customerVisitKey;
        public string requestedRecipeId;
        public string ticketKey;
        public CocktailOrderType orderType = CocktailOrderType.RecipeOrder;
        public bool presentOrder = true;
        public bool presentFeedback = true;
        public bool applyProgressRewards = true;
        public bool clearCustomerOnComplete = true;
    }

    public enum OrderEvaluationGrade
    {
        Bad,
        Mid,
        Good
    }

    public enum CustomerMood
    {
        Unknown,
        Dissatisfied,
        Neutral,
        Satisfied
    }

    public readonly struct BusinessOrderReward
    {
        public BusinessOrderReward(
            CustomerMood mood,
            int baseRevenue,
            int tipAmount,
            int reputationDelta)
        {
            Mood = mood;
            BaseRevenue = baseRevenue;
            TipAmount = tipAmount;
            ReputationDelta = reputationDelta;
        }

        public CustomerMood Mood { get; }
        public int BaseRevenue { get; }
        public int TipAmount { get; }
        public int TotalRevenue => BaseRevenue + TipAmount;
        public int ReputationDelta { get; }
    }

    public static class BusinessOrderRewardCalculator
    {
        public static BusinessOrderReward Calculate(
            OrderEvaluationGrade grade,
            BusinessOrderFlowSettings settings,
            float externalTipMultiplier = 1f)
        {
            CustomerMood mood = ResolveMood(grade);
            int baseRevenue = settings != null ? settings.GetMoneyReward(grade) : 0;
            float tipRate = settings != null ? settings.GetTipRate(mood) : 0f;
            int tipAmount = Mathf.FloorToInt(
                Mathf.Max(0, baseRevenue)
                * Mathf.Clamp01(tipRate)
                * Mathf.Max(0f, externalTipMultiplier));
            int reputationDelta = settings != null ? settings.GetReputationReward(mood) : 0;
            return new BusinessOrderReward(mood, baseRevenue, tipAmount, reputationDelta);
        }

        public static CustomerMood ResolveMood(OrderEvaluationGrade grade)
        {
            return grade switch
            {
                OrderEvaluationGrade.Good => CustomerMood.Satisfied,
                OrderEvaluationGrade.Mid => CustomerMood.Neutral,
                _ => CustomerMood.Dissatisfied
            };
        }
    }

    [Serializable]
    public sealed class BusinessSaleRecord
    {
        public string customerOrderKey;
        public string customerVisitKey;
        public string requestedRecipeId;
        public OrderEvaluationGrade grade;
        public CustomerMood customerMood;
        public int baseRevenue;
        public int tipAmount;
        public int totalRevenue;
        public int reputationDelta;

        public BusinessSaleRecord Clone()
        {
            return (BusinessSaleRecord)MemberwiseClone();
        }
    }

    public static class OrderEvaluationGrader
    {
        public static OrderEvaluationGrade Resolve(
            CocktailOrderEvaluationResult result,
            BusinessOrderFlowSettings settings)
        {
            float score = result?.requestedRecipeResult != null
                ? result.requestedRecipeResult.score
                : result?.detectedRecipeResult != null ? result.detectedRecipeResult.score : 0f;
            float goodThreshold = settings != null ? settings.goodScoreThreshold : 0.8f;
            float midThreshold = settings != null ? settings.midScoreThreshold : 0.45f;

            if (result != null
                && result.isSuccess
                && result.requestedRecipeResult?.matchedRecipe != null
                && result.requestedRecipeResult.matchedRecipe.evaluationGrade
                    == CocktailRecipeEvaluationGrade.Mid)
                return OrderEvaluationGrade.Mid;

            if (result != null && result.isSuccess && score >= goodThreshold)
                return OrderEvaluationGrade.Good;

            return score >= midThreshold ? OrderEvaluationGrade.Mid : OrderEvaluationGrade.Bad;
        }
    }

    public sealed class BusinessOrderSessionResult
    {
        public string sessionId;
        public OrderSessionOwner owner;
        public OrderSessionOutcome outcome;
        public string customerOrderKey;
        public string customerVisitKey;
        public string requestedRecipeId;
        public bool accepted;
        public OrderEvaluationGrade grade;
        public CustomerMood customerMood;
        public int baseRevenue;
        public int tipAmount;
        public int moneyDelta;
        public int reputationDelta;
        public bool technicalFailure;
        public string failureReason;
        public CocktailOrderEvaluationResult evaluation;

        public BusinessSaleRecord ToSaleRecord()
        {
            return new BusinessSaleRecord
            {
                customerOrderKey = customerOrderKey,
                customerVisitKey = customerVisitKey,
                requestedRecipeId = requestedRecipeId,
                grade = grade,
                customerMood = customerMood,
                baseRevenue = baseRevenue,
                tipAmount = tipAmount,
                totalRevenue = moneyDelta,
                reputationDelta = reputationDelta
            };
        }
    }
}
