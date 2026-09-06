using System;
using System.Collections.Generic;
using Slainte.Bartending;
using Slainte.Content;
using UnityEngine;

namespace Slainte.TV
{
    // TV 방송은 예보(Forecast) → 활성(Active) 2단계로 하루 지연을 두고 진행된다: EnsureForecast가
    // 오늘 미리 다음 영업일 방송을 뽑아 Rest 화면에 예고로 보여주고, ActivateForecastForBusiness가
    // 그 예보를 실제 영업 시작 시점에 "오늘의 활성 방송"으로 확정한다. 이 파일의 나머지 메서드는
    // 모두 GetActiveBroadcast() 결과를 바탕으로 손님/주문 가중치·팁·상점 등에 효과를 적용한다.
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
            // 활성 방송은 그 방송이 activate된 영업일 하루만 유효하다. 날짜가 지나면(다음 영업일)
            // 자동으로 만료되어 별도 정리 없이도 어제 효과가 이어지지 않는다.
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

        public static float GetCustomerWeightMultiplier(
            GameProgress progress,
            TVBroadcastDatabase database,
            CustomerVisitData visit)
        {
            TVBroadcastEntry active = GetActiveBroadcast(progress, database);
            return active != null
                && active.effectType == TVBroadcastEffectType.BoostCustomerTagWeight
                && MatchesCustomer(active, visit)
                    ? Math.Max(0f, active.effectMultiplier)
                    : 1f;
        }

        public static bool IsCustomerAllowedByActivePool(
            GameProgress progress,
            TVBroadcastDatabase database,
            CustomerVisitData visit)
        {
            TVBroadcastEntry active = GetActiveBroadcast(progress, database);
            return active == null
                || !active.exclusiveCustomerPool
                || MatchesCustomer(active, visit);
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

        public static bool CustomerMatchesActiveBoost(
            GameProgress progress,
            TVBroadcastDatabase database,
            CustomerVisitData visit)
        {
            TVBroadcastEntry active = GetActiveBroadcast(progress, database);
            return active != null
                && active.effectType == TVBroadcastEffectType.BoostCustomerTagWeight
                && MatchesCustomer(active, visit);
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

        private static bool MatchesCustomer(
            TVBroadcastEntry active,
            CustomerVisitData visit)
        {
            if (active == null || visit == null || string.IsNullOrWhiteSpace(active.targetTag))
                return false;

            if (ContainsTag(visit.tags, active.targetTag))
                return true;

            // targetTag가 "customer_attribute:" 접두어면 방문 태그가 아니라 손님 속성 키
            // (customerAttributeKey)와의 접두 일치로 판정한다 — 개별 방문 태그를 일일이 붙이지
            // 않고도 "이 속성을 가진 손님 전체"를 타겟팅할 수 있게 하는 유일한 예외 문법이다.
            const string attributePrefix = "customer_attribute:";
            if (!active.targetTag.StartsWith(
                    attributePrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string targetAttribute = active.targetTag.Substring(attributePrefix.Length).Trim();
            string visitAttribute = visit.customerAttributeKey?.Trim();
            return !string.IsNullOrWhiteSpace(targetAttribute)
                && !string.IsNullOrWhiteSpace(visitAttribute)
                && visitAttribute.StartsWith(
                    targetAttribute,
                    StringComparison.OrdinalIgnoreCase);
        }

        // 태그 일치 또는 최소 도수(minimumAbvPercent) 조건 중 하나로만 매칭한다 — 태그가 있으면
        // 태그로 우선 판정하고, 없거나 태그가 안 맞으면 요청 레시피의 실제 도수를 조회해 도수
        // 기준 방송("독한 술이 땡기는 날" 같은 효과)을 판정한다.
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
                ItemDefCatalog.LoadFromResources(ProjectResourcePaths.BartendingItems, null));
            return cachedRecipeCatalog.TryGet(order.requestedRecipeId, out CocktailRecipe recipe)
                && recipe != null
                && recipe.expectedAbvPercent >= active.minimumAbvPercent;
        }
    }
}
