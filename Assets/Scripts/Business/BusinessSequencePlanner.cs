using System;
using System.Collections.Generic;
using Slainte.TV;
using UnityEngine;

namespace Slainte.Business
{
    public sealed class BusinessVisitSelection
    {
        public BusinessVisitSelection(
            CustomerVisitData visit,
            CustomerVisitOrderOption orderOption)
        {
            Visit = visit;
            OrderOption = orderOption;
        }

        public CustomerVisitData Visit { get; }
        public CustomerVisitOrderOption OrderOption { get; }
    }

    public sealed class BusinessSequenceSelection
    {
        private BusinessSequenceSelection(
            CustomerVisitData visit,
            CustomerVisitOrderOption orderOption,
            BusinessRandomEncounterEntry encounter)
        {
            Visit = visit;
            OrderOption = orderOption;
            Encounter = encounter;
        }

        public CustomerVisitData Visit { get; }
        public CustomerVisitOrderOption OrderOption { get; }
        public BusinessRandomEncounterEntry Encounter { get; }
        public bool IsEncounter => Encounter != null;

        public static BusinessSequenceSelection ForVisit(
            CustomerVisitData visit,
            CustomerVisitOrderOption orderOption)
        {
            return new BusinessSequenceSelection(
                visit,
                orderOption,
                null);
        }

        public static BusinessSequenceSelection ForEncounter(
            BusinessRandomEncounterEntry encounter)
        {
            return new BusinessSequenceSelection(null, null, encounter);
        }
    }

    public static class BusinessSequencePlanner
    {
        public static List<CustomerVisitData> BuildEligibleVisitPool(
            CustomerVisitDatabase database,
            GameProgress progress)
        {
            List<CustomerVisitData> result = new();
            if (database?.visits == null || progress == null)
                return result;

            for (int i = 0; i < database.visits.Count; i++)
            {
                CustomerVisitData visit = database.visits[i];
                if (!IsStructurallyValidVisit(visit)
                    || visit.weight <= 0f
                    || !HasStructurallyValidOrder(visit))
                    continue;

                result.Add(visit);
            }

            return result;
        }

        public static BusinessVisitSelection PickWeightedVisit(
            IReadOnlyList<CustomerVisitData> frozenPool,
            GameProgress progress,
            ISet<string> recentVisitKeys,
            ISet<string> invalidVisitKeys,
            System.Random random)
        {
            if (frozenPool == null || frozenPool.Count == 0 || progress == null)
                return null;

            random ??= new System.Random();
            List<VisitCandidate> candidates = new();
            float totalWeight = 0f;

            for (int i = 0; i < frozenPool.Count; i++)
            {
                CustomerVisitData visit = frozenPool[i];
                if (!IsStructurallyValidVisit(visit)
                    || visit.weight <= 0f
                    || !IsVisitAvailableForSelection(visit, frozenPool, progress)
                    || IsRecentlySeen(visit, recentVisitKeys)
                    || (invalidVisitKeys != null && invalidVisitKeys.Contains(visit.visitKey)))
                    continue;

                List<CustomerVisitOrderOption> orders = BuildEligibleOrders(visit, progress);
                if (orders.Count == 0)
                    continue;

                float visitWeight = GetVisitWeight(visit, orders, progress);
                VisitCandidate candidate = new(visit, orders, visitWeight);
                candidates.Add(candidate);
                totalWeight += visitWeight;
            }

            if (candidates.Count == 0 || totalWeight <= 0f)
                return null;

            double roll = random.NextDouble() * totalWeight;
            float cursor = 0f;
            VisitCandidate selected = candidates[candidates.Count - 1];
            for (int i = 0; i < candidates.Count; i++)
            {
                cursor += candidates[i].Weight;
                if (roll <= cursor)
                {
                    selected = candidates[i];
                    break;
                }
            }

            CustomerVisitOrderOption order = PickWeightedOrder(
                selected.Orders,
                random,
                progress,
                applyTVModifiers: true);
            return order != null
                ? new BusinessVisitSelection(selected.Visit, order)
                : null;
        }

        public static List<BusinessRandomEncounterEntry> BuildEligibleRandomEncounterPool(
            IReadOnlyList<BusinessRandomEncounterEntry> configuredPool,
            GameProgress progress,
            ISet<string> reservedTargetKeys = null)
        {
            List<BusinessRandomEncounterEntry> result = new();
            if (configuredPool == null || progress == null)
                return result;

            HashSet<string> episodeIds = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < configuredPool.Count; i++)
            {
                BusinessRandomEncounterEntry entry = configuredPool[i];
                EpisodeData episode = entry?.episode;
                if (!IsStructurallyValidEncounter(episode)
                    || entry.weight <= 0f
                    || progress.IsEpisodeCompleted(episode.episodeId)
                    || !ProgressConditionEvaluator.IsMet(episode.triggerCondition, progress)
                    || (reservedTargetKeys != null && reservedTargetKeys.Contains(entry.TargetKey))
                    || !episodeIds.Add(episode.episodeId))
                    continue;

                result.Add(entry);
            }

            return result;
        }

        public static BusinessSequenceSelection PickWeightedSequence(
            IReadOnlyList<CustomerVisitData> frozenVisitPool,
            IReadOnlyList<BusinessRandomEncounterEntry> frozenEncounterPool,
            ISet<string> startedEncounterIds,
            GameProgress progress,
            ISet<string> recentVisitKeys,
            ISet<string> invalidVisitKeys,
            ISet<string> invalidEncounterIds,
            System.Random random)
        {
            if (progress == null)
                return null;

            random ??= new System.Random();
            List<SequenceCandidate> candidates = new();
            float totalWeight = 0f;

            if (frozenVisitPool != null)
            {
                for (int i = 0; i < frozenVisitPool.Count; i++)
                {
                    CustomerVisitData visit = frozenVisitPool[i];
                    if (!IsStructurallyValidVisit(visit)
                        || visit.weight <= 0f
                        || !IsVisitAvailableForSelection(
                            visit,
                            frozenVisitPool,
                            progress)
                        || IsRecentlySeen(visit, recentVisitKeys)
                        || (invalidVisitKeys != null && invalidVisitKeys.Contains(visit.visitKey)))
                        continue;

                    List<CustomerVisitOrderOption> orders = BuildEligibleOrders(visit, progress);
                    if (orders.Count == 0)
                        continue;

                    float visitWeight = GetVisitWeight(visit, orders, progress);
                    SequenceCandidate candidate = new(visit, orders, visitWeight);
                    candidates.Add(candidate);
                    totalWeight += visitWeight;
                }
            }

            if (frozenEncounterPool != null)
            {
                for (int i = 0; i < frozenEncounterPool.Count; i++)
                {
                    BusinessRandomEncounterEntry entry = frozenEncounterPool[i];
                    EpisodeData episode = entry?.episode;
                    if (!IsStructurallyValidEncounter(episode)
                        || entry.weight <= 0f
                        || progress.IsEpisodeCompleted(episode.episodeId)
                        || !ProgressConditionEvaluator.IsMet(episode.triggerCondition, progress)
                        || (startedEncounterIds != null
                            && startedEncounterIds.Contains(episode.episodeId))
                        || (invalidEncounterIds != null
                            && invalidEncounterIds.Contains(episode.episodeId)))
                        continue;

                    SequenceCandidate candidate = new(entry);
                    candidates.Add(candidate);
                    totalWeight += entry.weight;
                }
            }

            if (candidates.Count == 0 || totalWeight <= 0f)
                return null;

            double roll = random.NextDouble() * totalWeight;
            float cursor = 0f;
            SequenceCandidate selected = candidates[candidates.Count - 1];
            for (int i = 0; i < candidates.Count; i++)
            {
                cursor += candidates[i].Weight;
                if (roll <= cursor)
                {
                    selected = candidates[i];
                    break;
                }
            }

            if (selected.Encounter != null)
                return BusinessSequenceSelection.ForEncounter(selected.Encounter);

            CustomerVisitOrderOption order = PickWeightedOrder(
                selected.Orders,
                random,
                progress,
                applyTVModifiers: true);
            return order != null
                ? BusinessSequenceSelection.ForVisit(
                    selected.Visit,
                    order)
                : null;
        }

        public static CustomerVisitOrderOption PickWeightedOrder(
            CustomerVisitData visit,
            GameProgress progress,
            System.Random random)
        {
            if (!IsStructurallyValidVisit(visit) || progress == null)
                return null;

            return PickWeightedOrder(
                BuildEligibleOrders(visit, progress),
                random ?? new System.Random(),
                progress,
                applyTVModifiers: false);
        }

        public static BusinessRequiredActionRule PickNextRequiredAction(
            IReadOnlyList<BusinessRequiredActionRule> rules,
            GameProgress progress,
            BusinessRequiredActionTiming timing,
            bool includeAllTimings,
            ISet<string> executedRuleIds,
            ISet<string> executedTargetKeys,
            int sequenceSlot = 0)
        {
            if (rules == null || progress == null)
                return null;

            BusinessRequiredActionRule selected = null;
            for (int i = 0; i < rules.Count; i++)
            {
                BusinessRequiredActionRule rule = rules[i];
                if (rule == null
                    || (executedRuleIds != null
                        && !string.IsNullOrWhiteSpace(rule.ruleId)
                        && executedRuleIds.Contains(rule.ruleId))
                    || !MatchesRequiredTiming(
                        rule,
                        timing,
                        includeAllTimings,
                        sequenceSlot)
                    || (rule.exactDay > 0 && rule.exactDay != progress.CurrentDay)
                    || !ProgressConditionEvaluator.IsMet(rule.condition, progress)
                    || !IsConfiguredRequiredTarget(rule))
                    continue;

                string targetKey = rule.TargetKey;
                if (executedTargetKeys != null && executedTargetKeys.Contains(targetKey))
                    continue;

                if (rule.actionType == BusinessRequiredActionType.EncounterEpisode
                    && (progress.IsEpisodeCompleted(rule.encounterEpisode.episodeId)
                        || !ProgressConditionEvaluator.IsMet(
                            rule.encounterEpisode.triggerCondition,
                            progress)))
                {
                    continue;
                }

                if (selected == null || rule.priority > selected.priority)
                    selected = rule;
            }

            return selected;
        }

        private static bool MatchesRequiredTiming(
            BusinessRequiredActionRule rule,
            BusinessRequiredActionTiming timing,
            bool includeAllTimings,
            int sequenceSlot)
        {
            if (rule.timing == BusinessRequiredActionTiming.SequenceSlot)
            {
                return timing == BusinessRequiredActionTiming.SequenceSlot
                    && rule.sequenceSlot > 0
                    && rule.sequenceSlot == sequenceSlot;
            }

            if (timing == BusinessRequiredActionTiming.SequenceSlot)
                return false;

            return includeAllTimings || rule.timing == timing;
        }

        private static bool IsStructurallyValidVisit(CustomerVisitData visit)
        {
            if (visit == null
                || string.IsNullOrWhiteSpace(visit.visitKey)
                || visit.members == null
                || visit.members.Count == 0
                || visit.orders == null
                || visit.orders.Count == 0)
                return false;

            for (int i = 0; i < visit.members.Count; i++)
            {
                CustomerVisitMember member = visit.members[i];
                if (member != null && !string.IsNullOrWhiteSpace(member.characterKey))
                    return true;
            }

            return false;
        }

        private static bool HasStructurallyValidOrder(CustomerVisitData visit)
        {
            if (visit?.orders == null)
                return false;

            for (int i = 0; i < visit.orders.Count; i++)
            {
                CustomerVisitOrderOption option = visit.orders[i];
                CustomerOrderData order = option?.order;
                if (option != null
                    && option.weight > 0f
                    && order != null
                    && !string.IsNullOrWhiteSpace(order.key)
                    && !string.IsNullOrWhiteSpace(order.requestedRecipeId))
                    return true;
            }

            return false;
        }

        private static List<CustomerVisitOrderOption> BuildEligibleOrders(
            CustomerVisitData visit,
            GameProgress progress)
        {
            List<CustomerVisitOrderOption> result = new();
            if (visit?.orders == null)
                return result;

            for (int i = 0; i < visit.orders.Count; i++)
            {
                CustomerVisitOrderOption option = visit.orders[i];
                if (IsEligibleOrder(option, progress))
                    result.Add(option);
            }

            return result;
        }

        private static bool IsEligibleOrder(
            CustomerVisitOrderOption option,
            GameProgress progress)
        {
            CustomerOrderData order = option?.order;
            return option != null
                && option.weight > 0f
                && order != null
                && !string.IsNullOrWhiteSpace(order.key)
                && !string.IsNullOrWhiteSpace(order.requestedRecipeId)
                && ProgressConditionEvaluator.IsMet(option.condition, progress);
        }

        private static CustomerVisitOrderOption PickWeightedOrder(
            IReadOnlyList<CustomerVisitOrderOption> orders,
            System.Random random,
            GameProgress progress,
            bool applyTVModifiers)
        {
            if (orders == null || orders.Count == 0)
                return null;

            float totalWeight = 0f;
            for (int i = 0; i < orders.Count; i++)
                totalWeight += GetOrderWeight(orders[i], progress, applyTVModifiers);

            if (totalWeight <= 0f)
                return null;

            double roll = random.NextDouble() * totalWeight;
            float cursor = 0f;
            for (int i = 0; i < orders.Count; i++)
            {
                cursor += GetOrderWeight(orders[i], progress, applyTVModifiers);
                if (roll <= cursor)
                    return orders[i];
            }

            return orders[orders.Count - 1];
        }

        public static bool HasEligibleTargetForActiveTVEffect(
            IReadOnlyList<CustomerVisitData> visits,
            GameProgress progress)
        {
            TVBroadcastDatabase database = TVBroadcastDatabase.LoadDefault();
            TVBroadcastEntry active = TVBroadcastRuntime.GetActiveBroadcast(progress, database);
            if (active == null
                || (active.effectType != TVBroadcastEffectType.BoostCustomerTagWeight
                    && active.effectType != TVBroadcastEffectType.BoostOrderTagWeight))
            {
                return true;
            }

            if (visits == null)
                return false;

            for (int i = 0; i < visits.Count; i++)
            {
                CustomerVisitData visit = visits[i];
                if (!IsVisitAvailableForSelection(visit, visits, progress))
                    continue;

                if (active.effectType == TVBroadcastEffectType.BoostCustomerTagWeight)
                {
                    if (TVBroadcastRuntime.CustomerMatchesActiveBoost(
                            progress,
                            database,
                            visit))
                    {
                        return true;
                    }

                    continue;
                }

                List<CustomerVisitOrderOption> orders = BuildEligibleOrders(visit, progress);
                for (int orderIndex = 0; orderIndex < orders.Count; orderIndex++)
                {
                    if (TVBroadcastRuntime.OrderMatchesActiveBoost(
                            progress,
                            database,
                            orders[orderIndex]?.order))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static float GetVisitWeight(
            CustomerVisitData visit,
            IReadOnlyList<CustomerVisitOrderOption> eligibleOrders,
            GameProgress progress)
        {
            float multiplier = TVBroadcastRuntime.GetCustomerWeightMultiplier(
                progress,
                TVBroadcastDatabase.LoadDefault(),
                visit);
            float baseOrderWeight = 0f;
            float boostedOrderWeight = 0f;
            if (eligibleOrders != null)
            {
                for (int i = 0; i < eligibleOrders.Count; i++)
                {
                    CustomerVisitOrderOption option = eligibleOrders[i];
                    baseOrderWeight += Mathf.Max(0f, option?.weight ?? 0f);
                    boostedOrderWeight += GetOrderWeight(
                        option,
                        progress,
                        applyTVModifiers: true);
                }
            }

            if (baseOrderWeight > 0f)
                multiplier *= boostedOrderWeight / baseOrderWeight;

            return Mathf.Max(0f, visit != null ? visit.weight * multiplier : 0f);
        }

        private static float GetOrderWeight(
            CustomerVisitOrderOption option,
            GameProgress progress,
            bool applyTVModifiers)
        {
            float multiplier = applyTVModifiers
                ? TVBroadcastRuntime.GetOrderWeightMultiplier(
                    progress,
                    TVBroadcastDatabase.LoadDefault(),
                    option?.order)
                : 1f;
            return Mathf.Max(0f, option != null ? option.weight * multiplier : 0f);
        }

        public static bool IsVisitAvailable(
            CustomerVisitData visit,
            GameProgress progress)
        {
            return EvaluateVisitAvailability(
                visit,
                progress,
                visit != null && visit.initiallyAvailable);
        }

        private static bool IsVisitAvailableForSelection(
            CustomerVisitData visit,
            IReadOnlyList<CustomerVisitData> visits,
            GameProgress progress)
        {
            TVBroadcastDatabase database = TVBroadcastDatabase.LoadDefault();
            if (!TVBroadcastRuntime.IsCustomerAllowedByActivePool(
                    progress,
                    database,
                    visit))
            {
                return false;
            }

            TVBroadcastEntry active = TVBroadcastRuntime.GetActiveBroadcast(
                progress,
                database);
            if (active == null
                || !active.exclusiveCustomerPool
                || active.effectType != TVBroadcastEffectType.BoostCustomerTagWeight
                || !TVBroadcastRuntime.CustomerMatchesActiveBoost(
                    progress,
                    database,
                    visit))
            {
                return IsVisitAvailable(visit, progress);
            }

            CustomerVisitData canonical = FindCanonicalAvailabilityVisit(
                visit,
                visits,
                progress,
                database);
            return canonical != null
                ? IsVisitAvailable(canonical, progress)
                : EvaluateVisitAvailability(visit, progress, initialAvailability: true);
        }

        private static CustomerVisitData FindCanonicalAvailabilityVisit(
            CustomerVisitData specialVisit,
            IReadOnlyList<CustomerVisitData> visits,
            GameProgress progress,
            TVBroadcastDatabase database)
        {
            if (specialVisit == null || visits == null)
                return null;

            string reappearanceKey = specialVisit.GetReappearanceKey();
            if (string.IsNullOrWhiteSpace(reappearanceKey))
                return null;

            for (int i = 0; i < visits.Count; i++)
            {
                CustomerVisitData candidate = visits[i];
                if (candidate == null
                    || candidate == specialVisit
                    || !string.Equals(
                        candidate.GetReappearanceKey(),
                        reappearanceKey,
                        StringComparison.OrdinalIgnoreCase)
                    || TVBroadcastRuntime.CustomerMatchesActiveBoost(
                        progress,
                        database,
                        candidate))
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static bool EvaluateVisitAvailability(
            CustomerVisitData visit,
            GameProgress progress,
            bool initialAvailability)
        {
            if (visit == null
                || progress == null
                || !ProgressConditionEvaluator.IsMet(
                    visit.condition,
                    progress,
                    visit.maxDay))
            {
                return false;
            }

            bool available = initialAvailability;
            if (visit.availabilityTransitions == null)
                return available;

            for (int i = 0; i < visit.availabilityTransitions.Count; i++)
            {
                CustomerAvailabilityTransition transition =
                    visit.availabilityTransitions[i];
                if (transition != null
                    && ProgressConditionEvaluator.IsMet(transition.condition, progress))
                {
                    available = transition.enabled;
                }
            }

            return available;
        }

        private static bool IsRecentlySeen(
            CustomerVisitData visit,
            ISet<string> recentVisitKeys)
        {
            if (visit == null || recentVisitKeys == null)
                return false;

            string key = visit.GetReappearanceKey();
            return !string.IsNullOrWhiteSpace(key) && recentVisitKeys.Contains(key);
        }

        private static bool IsConfiguredRequiredTarget(BusinessRequiredActionRule rule)
        {
            if (rule.actionType == BusinessRequiredActionType.CustomerVisit)
                return IsStructurallyValidVisit(rule.customerVisit);

            return IsStructurallyValidEncounter(rule.encounterEpisode);
        }

        private static bool IsStructurallyValidEncounter(EpisodeData episode)
        {
            return episode != null
                && episode.episodeType == EpisodeType.Encounter
                && !string.IsNullOrWhiteSpace(episode.episodeId);
        }

        private sealed class VisitCandidate
        {
            public VisitCandidate(
                CustomerVisitData visit,
                List<CustomerVisitOrderOption> orders,
                float weight)
            {
                Visit = visit;
                Orders = orders;
                Weight = weight;
            }

            public CustomerVisitData Visit { get; }
            public List<CustomerVisitOrderOption> Orders { get; }
            public float Weight { get; }
        }

        private sealed class SequenceCandidate
        {
            public SequenceCandidate(
                CustomerVisitData visit,
                List<CustomerVisitOrderOption> orders,
                float weight)
            {
                Visit = visit;
                Orders = orders;
                Weight = weight;
            }

            public SequenceCandidate(BusinessRandomEncounterEntry encounter)
            {
                Encounter = encounter;
                Weight = encounter != null ? encounter.weight : 0f;
            }

            public CustomerVisitData Visit { get; }
            public List<CustomerVisitOrderOption> Orders { get; }
            public BusinessRandomEncounterEntry Encounter { get; }
            public float Weight { get; }
        }
    }
}
