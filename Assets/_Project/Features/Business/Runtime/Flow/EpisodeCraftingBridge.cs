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

        private static TasteMoodTagPaletteDef cachedTagPalette;

        public static bool TryBuildRequest(
            EpisodeNode node,
            string sessionId,
            out OrderSessionRequest request)
        {
            request = null;
            if (node == null || string.IsNullOrWhiteSpace(node.craftingOrderTarget))
                return false;

            string target = node.craftingOrderTarget.Trim();
            string normalizedTag = CocktailOrderTagRules.Normalize(target);
            CocktailOrderType orderType = ResolveOrderType(normalizedTag);
            bool tagOrder = orderType == CocktailOrderType.TasteOrder
                || orderType == CocktailOrderType.MoodOrder;

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
                paymentMultiplier = BusinessOrderPriceRules.NormalizePaymentMultiplier(
                    node.craftingPaymentMultiplier),
                presentOrder = false,
                presentFeedback = false,
                applyProgressRewards = false,
                applyPayment = node.craftingPaymentEnabled,
                recordSale = node.craftingPaymentEnabled,
                clearCustomerOnComplete = false
            };
            return true;
        }

        // craftingOrderTarget 값 자체로 판별한다: 맛/분위기 태그 팔레트에 등록된 태그면 TasteOrder/MoodOrder,
        // 아니면(레시피 ID로 간주) EpisodeOrder. CSV에 별도 craftingOrderType 컬럼을 요구하지 않기 위함.
        private static CocktailOrderType ResolveOrderType(string normalizedTag)
        {
            if (string.IsNullOrWhiteSpace(normalizedTag))
                return CocktailOrderType.EpisodeOrder;

            if (cachedTagPalette == null)
                cachedTagPalette = Resources.Load<TasteMoodTagPaletteDef>("Recipes/TasteMoodPalette");

            if (cachedTagPalette == null)
                return CocktailOrderType.EpisodeOrder;

            if (cachedTagPalette.TryGetTasteColor(normalizedTag, out _, out _))
                return CocktailOrderType.TasteOrder;
            if (cachedTagPalette.TryGetMoodColor(normalizedTag, out _, out _))
                return CocktailOrderType.MoodOrder;

            return CocktailOrderType.EpisodeOrder;
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
