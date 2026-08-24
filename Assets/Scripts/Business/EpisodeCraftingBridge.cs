using System;
using System.Collections.Generic;
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
            if (!initialized || craftingActive)
                return false;

            int nextSessionSequence = sessionSequence + 1;
            if (!TryBuildRequest(
                    node,
                    $"episode_crafting_{nextSessionSequence}",
                    out OrderSessionRequest request))
                return false;

            sessionSequence = nextSessionSequence;

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

        public void AbortForEpisodeRecovery()
        {
            if (!craftingActive)
                return;

            businessFlow?.TryAbortEpisodeOrderForRecovery();
            craftingActive = false;
        }

        public static bool TryBuildRequest(
            EpisodeNode node,
            string sessionId,
            out OrderSessionRequest request)
        {
            request = null;
            if (node == null || string.IsNullOrWhiteSpace(node.craftingOrderTarget))
                return false;

            CocktailOrderType orderType = node.craftingOrderType;
            bool tagOrder = orderType == CocktailOrderType.TasteOrder
                || orderType == CocktailOrderType.MoodOrder;
            if (!tagOrder && orderType != CocktailOrderType.EpisodeOrder)
                return false;

            string target = node.craftingOrderTarget.Trim();
            string normalizedTag = tagOrder
                ? CocktailOrderTagRules.Normalize(target)
                : string.Empty;
            if (tagOrder && string.IsNullOrWhiteSpace(normalizedTag))
                return false;

            request = new OrderSessionRequest
            {
                sessionId = sessionId,
                owner = OrderSessionOwner.Episode,
                requestedRecipeId = tagOrder ? string.Empty : target,
                requestedConditionLabel = tagOrder ? normalizedTag : string.Empty,
                requestedTags = tagOrder
                    ? new List<string> { normalizedTag }
                    : new List<string>(),
                ticketData = node.craftingOrderTicket,
                ticketKey = node.craftingTicketKey,
                orderType = orderType,
                paymentCurrency = node.craftingPaymentCurrency,
                presentOrder = false,
                presentFeedback = false,
                applyProgressRewards = false,
                applyPayment = node.craftingPaymentEnabled,
                recordSale = node.craftingPaymentEnabled,
                clearCustomerOnComplete = false
            };
            return true;
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
