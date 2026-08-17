using System;
using System.Collections.Generic;
using Slainte.Bartending;
using UnityEngine;

namespace Slainte.TV
{
    public static class TVBroadcastRuntime
    {
        private static CocktailRecipeCatalog cachedRecipeCatalog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeCache()
        {
            cachedRecipeCatalog = null;
        }

        public static TVBroadcastEntry EnsureForecast(
            GameProgress progress,
            TVBroadcastDatabase database,
            System.Random random = null)
        {
            if (progress == null || database == null)
                return null;

            TVBroadcastEntry existing = database.FindById(progress.TVForecastBroadcastId);
            if (existing != null)
                return existing;

            TVBroadcastEntry selected = database.PickWeighted(random);
            if (selected != null)
                progress.SetTVForecast(selected.id);
            return selected;
        }

        public static TVBroadcastEntry ActivateForecastForBusiness(
            GameProgress progress,
            TVBroadcastDatabase database,
            System.Random random = null)
        {
            TVBroadcastEntry forecast = EnsureForecast(progress, database, random);
            if (forecast == null || progress == null)
                return null;

            progress.ActivateTVForecastForBusiness();
            return GetActiveBroadcast(progress, database);
        }

        public static TVBroadcastEntry GetForecast(
            GameProgress progress,
            TVBroadcastDatabase database)
        {
            return progress != null && database != null
                ? database.FindById(progress.TVForecastBroadcastId)
                : null;
        }

        public static TVBroadcastEntry GetActiveBroadcast(
            GameProgress progress,
            TVBroadcastDatabase database)
        {
            if (progress == null
                || database == null
                || progress.TVActiveBusinessDay != progress.CurrentDay)
            {
                return null;
            }

            return database.FindById(progress.TVActiveBroadcastId);
        }

        public static bool IsActiveEffect(
            GameProgress progress,
            TVBroadcastDatabase database,
            TVBroadcastEffectType effectType)
        {
            return GetActiveBroadcast(progress, database)?.effectType == effectType;
        }

        public static float GetTipMultiplier(
            GameProgress progress,
            TVBroadcastDatabase database)
        {
            TVBroadcastEntry active = GetActiveBroadcast(progress, database);
            return active != null && active.effectType == TVBroadcastEffectType.BoostTips
                ? Math.Max(0f, active.effectMultiplier)
                : 1f;
        }

        public static float GetTaggedWeightMultiplier(
            GameProgress progress,
            TVBroadcastDatabase database,
            TVBroadcastEffectType effectType,
            IReadOnlyList<string> tags)
        {
            TVBroadcastEntry active = GetActiveBroadcast(progress, database);
            if (active == null
                || active.effectType != effectType
                || string.IsNullOrWhiteSpace(active.targetTag)
                || !ContainsTag(tags, active.targetTag))
            {
                return 1f;
            }

            return Math.Max(0f, active.effectMultiplier);
        }

        public static float GetOrderWeightMultiplier(
            GameProgress progress,
            TVBroadcastDatabase database,
            CustomerOrderData order)
        {
            TVBroadcastEntry active = GetActiveBroadcast(progress, database);
            return active != null
                && active.effectType == TVBroadcastEffectType.BoostOrderTagWeight
                && MatchesOrder(active, order)
                    ? Math.Max(0f, active.effectMultiplier)
                    : 1f;
        }

        public static bool OrderMatchesActiveBoost(
            GameProgress progress,
            TVBroadcastDatabase database,
            CustomerOrderData order)
        {
            TVBroadcastEntry active = GetActiveBroadcast(progress, database);
            return active != null
                && active.effectType == TVBroadcastEffectType.BoostOrderTagWeight
                && MatchesOrder(active, order);
        }

        public static bool CustomerMatchesActiveBoost(
            GameProgress progress,
            TVBroadcastDatabase database,
            IReadOnlyList<string> tags)
        {
            TVBroadcastEntry active = GetActiveBroadcast(progress, database);
            return active != null
                && active.effectType == TVBroadcastEffectType.BoostCustomerTagWeight
                && !string.IsNullOrWhiteSpace(active.targetTag)
                && ContainsTag(tags, active.targetTag);
        }

        public static bool IsRestShopDisabled(
            GameProgress progress,
            TVBroadcastDatabase database,
            out string reason)
        {
            TVBroadcastEntry active = GetActiveBroadcast(progress, database);
            bool disabled = active != null
                && active.effectType == TVBroadcastEffectType.DisableRestShop;
            reason = disabled ? active.restrictionReason : string.Empty;
            return disabled;
        }

        private static bool ContainsTag(IReadOnlyList<string> tags, string target)
        {
            if (tags == null)
                return false;

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], target, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool MatchesOrder(TVBroadcastEntry active, CustomerOrderData order)
        {
            if (active == null || order == null)
                return false;

            if (!string.IsNullOrWhiteSpace(active.targetTag)
                && ContainsTag(order.tags, active.targetTag))
            {
                return true;
            }

            if (active.minimumAbvPercent < 0f
                || string.IsNullOrWhiteSpace(order.requestedRecipeId))
            {
                return false;
            }

            cachedRecipeCatalog ??= CocktailRecipeDataLoader.LoadDefault(
                ItemDefCatalog.LoadFromResources("Items", null));
            return cachedRecipeCatalog.TryGet(order.requestedRecipeId, out CocktailRecipe recipe)
                && recipe != null
                && recipe.expectedAbvPercent >= active.minimumAbvPercent;
        }
    }
}
