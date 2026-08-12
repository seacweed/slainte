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
                presentOrder = false,
                presentFeedback = false,
                applyProgressRewards = false,
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

    public static class EpisodeCraftingResultMapper
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
            CocktailEvaluationResult requested = evaluation?.requestedRecipeResult;
            CocktailEvaluationResult detected = evaluation?.detectedRecipeResult;
            CocktailRecipe baseRecipe = evaluation?.order?.requestedRecipe;
            CocktailRecipe matchedRecipe = requested?.matchedRecipe;

            if (IsWrongMenu(result.requestedRecipeId, requested, detected))
                return CraftingJobResult.MidWrongMenu;

            bool isRequestedVariant = IsRequestedRecipeFamily(
                matchedRecipe,
                result.requestedRecipeId);
            bool iceInvalid = requested != null && !requested.iceValid;
            bool glassInvalid = requested != null && !requested.glassValid;
            if (isRequestedVariant && baseRecipe != null && matchedRecipe != null)
            {
                iceInvalid |= matchedRecipe.iceRequirement != baseRecipe.iceRequirement;
                glassInvalid |= !string.Equals(
                    matchedRecipe.glassId,
                    baseRecipe.glassId,
                    StringComparison.OrdinalIgnoreCase);
            }
            if (iceInvalid && glassInvalid)
                return CraftingJobResult.MidIceGlass;
            if (iceInvalid)
                return CraftingJobResult.MidIce;
            if (glassInvalid)
                return CraftingJobResult.MidGlass;

            return CraftingJobResult.Bad;
        }

        private static bool IsWrongMenu(
            string requestedRecipeId,
            CocktailEvaluationResult requested,
            CocktailEvaluationResult detected)
        {
            return requested != null
                && !requested.isSuccess
                && detected != null
                && detected.isSuccess
                && detected.matchedRecipe != null
                && !IsRequestedRecipeFamily(detected.matchedRecipe, requestedRecipeId);
        }

        private static bool IsRequestedRecipeFamily(
            CocktailRecipe recipe,
            string requestedRecipeId)
        {
            return recipe != null
                && (string.Equals(
                        recipe.id,
                        requestedRecipeId,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        recipe.baseRecipeId,
                        requestedRecipeId,
                        StringComparison.OrdinalIgnoreCase));
        }
    }
}
