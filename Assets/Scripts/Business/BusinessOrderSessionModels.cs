using System;
using System.Collections.Generic;
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
        public string requestedConditionLabel;
        public List<string> requestedTags = new();
        public OrderTicketData ticketData;
        public string ticketKey;
        public CocktailOrderType orderType = CocktailOrderType.RecipeOrder;
        public GameCurrency paymentCurrency = GameCurrency.Money;
        public float paymentMultiplier = 1f;
        public BusinessCustomerRewardProfile rewardProfile =
            BusinessCustomerRewardProfile.Standard;
        public bool presentOrder = true;
        public bool presentFeedback = true;
        public bool applyProgressRewards = true;
        public bool applyPayment;
        public bool recordSale;
        public bool applyReputation;
        public bool clearCustomerOnComplete = true;
    }

    public static class BusinessOrderPriceRules
    {
        public static float NormalizePaymentMultiplier(float multiplier)
        {
            return !float.IsNaN(multiplier)
                && !float.IsInfinity(multiplier)
                && multiplier > 0f
                    ? multiplier
                    : 1f;
        }

        public static int ApplyPaymentMultiplier(int listedPrice, float multiplier)
        {
            if (listedPrice <= 0)
                return listedPrice;

            return Mathf.Max(0, Mathf.RoundToInt(
                listedPrice * NormalizePaymentMultiplier(multiplier)));
        }
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

    public enum BusinessCustomerRewardProfile
    {
        Standard,
        BigFish
    }

    public static class BusinessCustomerRules
    {
        private const string BigFishCharacterKey = "big_fish";

        public static GameCurrency ResolvePaymentCurrency(
            CustomerVisitData visit,
            GameCurrency fallback)
        {
            if (visit == null)
                return fallback;
            if (visit.overridePaymentCurrency)
                return visit.paymentCurrency;

            string attribute = visit.customerAttributeKey?.Trim();
            return string.Equals(attribute, "아무개들", StringComparison.OrdinalIgnoreCase)
                || string.Equals(attribute, "F72", StringComparison.OrdinalIgnoreCase)
                || string.Equals(attribute, "셀리", StringComparison.OrdinalIgnoreCase)
                    ? GameCurrency.StrangeCoin
                    : fallback;
        }

        public static BusinessCustomerRewardProfile ResolveRewardProfile(
            CustomerVisitData visit)
        {
            if (visit?.members != null)
            {
                for (int i = 0; i < visit.members.Count; i++)
                {
                    if (string.Equals(
                            visit.members[i]?.characterKey,
                            BigFishCharacterKey,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return BusinessCustomerRewardProfile.BigFish;
                    }
                }
            }

            return BusinessCustomerRewardProfile.Standard;
        }
    }

    public readonly struct BusinessOrderReward
    {
        public BusinessOrderReward(
            CustomerMood mood,
            int baseRevenue,
            int tipAmount,
            int penaltyAmount,
            int reputationDelta)
        {
            Mood = mood;
            BaseRevenue = baseRevenue;
            TipAmount = tipAmount;
            PenaltyAmount = penaltyAmount;
            ReputationDelta = reputationDelta;
        }

        public CustomerMood Mood { get; }
        public int BaseRevenue { get; }
        public int TipAmount { get; }
        public int PenaltyAmount { get; }
        public int TotalRevenue => BaseRevenue + TipAmount - PenaltyAmount;
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
            float externalTipMultiplier = 1f,
            int currentMoney = 0,
            BusinessCustomerRewardProfile rewardProfile =
                BusinessCustomerRewardProfile.Standard)
        {
            CustomerMood mood = ResolveMood(grade);
            int baseRevenue;
            int penaltyAmount = 0;
            int tipAmount;
            if (listedRecipePrice >= 0)
            {
                int price = Mathf.Max(0, listedRecipePrice);
                baseRevenue = price;
                if (rewardProfile == BusinessCustomerRewardProfile.BigFish)
                {
                    if (grade == OrderEvaluationGrade.Good)
                    {
                        float bonusRate = settings != null
                            ? Mathf.Max(0f, settings.bigFishGoodBonusRate)
                            : 2f;
                        tipAmount = Mathf.RoundToInt(
                            price * bonusRate * Mathf.Max(0f, externalTipMultiplier));
                    }
                    else
                    {
                        float penaltyRate = settings != null
                            ? Mathf.Max(0f, settings.bigFishFailurePenaltyRate)
                            : 3f;
                        tipAmount = 0;
                        penaltyAmount = Mathf.RoundToInt(price * penaltyRate);
                    }
                }
                else
                {
                    if (grade == OrderEvaluationGrade.Bad)
                    {
                        float penaltyRate = settings != null
                            ? Mathf.Max(0f, settings.badPenaltyRate)
                            : 1.3f;
                        penaltyAmount = Mathf.RoundToInt(price * penaltyRate);
                    }

                    float tipRate = settings != null ? settings.GetTipRate(mood) : 0f;
                    tipAmount = Mathf.RoundToInt(
                        price
                        * Mathf.Clamp01(tipRate)
                        * Mathf.Max(0f, externalTipMultiplier));
                }
            }
            else
            {
                baseRevenue = settings != null ? settings.GetMoneyReward(grade) : 0;
                float tipRate = settings != null ? settings.GetTipRate(mood) : 0f;
                tipAmount = Mathf.RoundToInt(
                    Mathf.Max(0, baseRevenue)
                    * Mathf.Clamp01(tipRate)
                    * Mathf.Max(0f, externalTipMultiplier));
            }

            int reputationDelta = settings != null ? settings.GetReputationReward(mood) : 0;
            return new BusinessOrderReward(mood, baseRevenue, tipAmount, penaltyAmount, reputationDelta);
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
        public int penaltyAmount;
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
            if (result == null)
                return OrderEvaluationGrade.Bad;

            return result.outcome switch
            {
                CocktailOrderEvaluationOutcome.Good => OrderEvaluationGrade.Good,
                CocktailOrderEvaluationOutcome.MidIce => OrderEvaluationGrade.Mid,
                CocktailOrderEvaluationOutcome.MidGlass => OrderEvaluationGrade.Mid,
                CocktailOrderEvaluationOutcome.MidIceGlass => OrderEvaluationGrade.Mid,
                CocktailOrderEvaluationOutcome.MidWrongMenu => OrderEvaluationGrade.Mid,
                _ => OrderEvaluationGrade.Bad
            };
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
        public int penaltyAmount;
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
                penaltyAmount = penaltyAmount,
                totalRevenue = PaymentAmount,
                reputationDelta = reputationDelta
            };
        }
    }
}
