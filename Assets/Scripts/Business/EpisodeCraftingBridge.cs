using System;
using Slainte.Bartending;
using UnityEngine;

namespace Slainte.Business
{
    public sealed class EpisodeCraftingBridge : MonoBehaviour
    {
        private BusinessFlowBootstrap businessFlow;
        private bool initialized;
        private bool craftingActive;
        private int sessionSequence;

        public bool IsCraftingActive => craftingActive;

        public void Initialize(BusinessFlowBootstrap flow)
        {
            if (initialized)
                return;

            businessFlow = flow;
            initialized = businessFlow != null;
        }

        public bool TryStart(
            EpisodeNode node,
            Action<CraftingJobResult> onCompleted,
            Action<string> onTechnicalFailure)
        {
            if (!initialized
                || craftingActive
                || node == null
                || string.IsNullOrWhiteSpace(node.craftingRecipeId))
                return false;

            OrderSessionRequest request = new()
            {
                sessionId = $"episode_crafting_{++sessionSequence}",
                owner = OrderSessionOwner.Episode,
                requestedRecipeId = node.craftingRecipeId,
                ticketKey = node.craftingTicketKey,
                orderType = CocktailOrderType.EpisodeOrder,
                paymentCurrency = node.craftingPaymentCurrency,
                presentOrder = false,
                presentFeedback = false,
                applyProgressRewards = false,
                applyPayment = node.craftingPaymentEnabled,
                clearCustomerOnComplete = false
            };

            craftingActive = true;
            bool started = businessFlow.StartEpisodeOrder(request, result =>
            {
                craftingActive = false;
                if (result == null || result.technicalFailure)
                {
                    string reason = result?.failureReason;
                    onTechnicalFailure?.Invoke(string.IsNullOrWhiteSpace(reason)
                        ? "에피소드 제조 세션이 기술적으로 실패했습니다."
                        : reason);
                    return;
                }

                onCompleted?.Invoke(EpisodeCraftingResultMapper.Map(result));
            });

            if (!started)
                craftingActive = false;
            return started;
        }
    }

    public static class CraftingResultMapper
    {
        public static CraftingJobResult Map(BusinessOrderSessionResult result)
        {
            if (result == null
                || result.outcome != OrderSessionOutcome.Served
                || !result.accepted)
                return CraftingJobResult.Bad;

            if (result.grade == OrderEvaluationGrade.Good)
                return CraftingJobResult.Good;

            CocktailOrderEvaluationResult evaluation = result.evaluation;
            return evaluation?.outcome switch
            {
                CocktailOrderEvaluationOutcome.Good => CraftingJobResult.Good,
                CocktailOrderEvaluationOutcome.MidIce => CraftingJobResult.MidIce,
                CocktailOrderEvaluationOutcome.MidGlass => CraftingJobResult.MidGlass,
                CocktailOrderEvaluationOutcome.MidIceGlass => CraftingJobResult.MidIceGlass,
                CocktailOrderEvaluationOutcome.MidWrongMenu => CraftingJobResult.MidWrongMenu,
                _ => CraftingJobResult.Bad
            };
        }
    }

    public static class EpisodeCraftingResultMapper
    {
        public static CraftingJobResult Map(BusinessOrderSessionResult result)
        {
            return CraftingResultMapper.Map(result);
        }
    }
}
