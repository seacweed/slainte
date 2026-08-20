using System;
using Slainte.Bartending;
using Slainte.Economy;
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
        public GameCurrency paymentCurrency = GameCurrency.Money;
        public bool presentOrder = true;
        public bool presentFeedback = true;
        public bool applyProgressRewards = true;
        public bool applyPayment;
        public bool applyReputation;
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
            return Calculate(grade, -1, settings, externalTipMultiplier);
        }

        public static BusinessOrderReward Calculate(
            OrderEvaluationGrade grade,
            int listedRecipePrice,
            BusinessOrderFlowSettings settings,
            float externalTipMultiplier = 1f)
        {
            CustomerMood mood = ResolveMood(grade);
            int baseRevenue = listedRecipePrice >= 0
                ? Mathf.FloorToInt(
                    Mathf.Max(0, listedRecipePrice)
                    * (settings != null
                        ? settings.GetRecipePriceMultiplier(grade)
                        : grade == OrderEvaluationGrade.Good ? 1f
                        : grade == OrderEvaluationGrade.Mid ? 0.5f
                        : 0f))
                : settings != null ? settings.GetMoneyReward(grade) : 0;
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
        public GameCurrency paymentCurrency = GameCurrency.Money;
        public int listedPrice;
        public OrderEvaluationGrade grade;
        public CustomerMood customerMood;
        public int baseRevenue;
        public int tipAmount;
        public int totalRevenue;
        public int reputationDelta;
        public bool paymentApplied;

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
        public GameCurrency paymentCurrency = GameCurrency.Money;
        public int listedPrice;
        public bool accepted;
        public OrderEvaluationGrade grade;
        public CustomerMood customerMood;
        public int baseRevenue;
        public int tipAmount;
        public int moneyDelta;
        public int strangeCoinDelta;
        public int totalPayment;
        public int reputationDelta;
        public bool technicalFailure;
        public string failureReason;
        public CocktailOrderEvaluationResult evaluation;

        public int PaymentAmount => totalPayment != 0
            ? totalPayment
            : moneyDelta + strangeCoinDelta;

        public BusinessSaleRecord ToSaleRecord()
        {
            return new BusinessSaleRecord
            {
                customerOrderKey = customerOrderKey,
                customerVisitKey = customerVisitKey,
                requestedRecipeId = requestedRecipeId,
                paymentCurrency = paymentCurrency,
                listedPrice = listedPrice,
                grade = grade,
                customerMood = customerMood,
                baseRevenue = baseRevenue,
                tipAmount = tipAmount,
                totalRevenue = PaymentAmount,
                reputationDelta = reputationDelta
            };
        }
    }
}
