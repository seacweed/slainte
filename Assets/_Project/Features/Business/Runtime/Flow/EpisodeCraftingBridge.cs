using System;
using System.Collections.Generic;
using Slainte.Bartending;
using Slainte.Content;
using UnityEngine;

namespace Slainte.Business
{
    // 에피소드 그래프의 제조 노드(craftingOrderTarget)를 영업과 동일한
    // BusinessOrderSessionController 판정 세션으로 연결하는 다리 역할. EpisodeRunner가
    // 제조 노드를 만나면 이 클래스를 거쳐 BusinessFlowBootstrap.StartEpisodeOrder()를 호출하고,
    // 결과는 CraftingJobResult(Good/Mid.../Bad)로 다시 변환해 에피소드 분기에 넘긴다.
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
                // 손님 주문 제시/결과 대사는 에피소드 자체 연출(그래프의 대화 노드)이 대신하므로
                // 세션에는 끄고, 진행 보상도 기본은 지급하지 않는다(영업 판매량·재등장 제한에서
                // 제외). 다만 노드가 craftingPaymentEnabled를 켰다면 대금 지급·판매 기록만 예외적으로 활성화한다.
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
                cachedTagPalette = Resources.Load<TasteMoodTagPaletteDef>(
                    ProjectResourcePaths.BartendingRecipes + "/TasteMoodPalette");

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
