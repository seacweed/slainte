using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Business
{
    public sealed class BusinessVisitSelection
    {
        public BusinessVisitSelection(
            CustomerVisitData visit,
            CustomerVisitOrderOption orderOption,
            bool usedCooldownFallback = false)
        {
            Visit = visit;
            OrderOption = orderOption;
            UsedCooldownFallback = usedCooldownFallback;
        }

        public CustomerVisitData Visit { get; }
        public CustomerVisitOrderOption OrderOption { get; }
        public bool UsedCooldownFallback { get; }
    }

    public sealed class BusinessSequenceSelection
    {
        private BusinessSequenceSelection(
            CustomerVisitData visit,
            CustomerVisitOrderOption orderOption,
            BusinessRandomEncounterEntry encounter,
            bool usedCooldownFallback)
        {
            Visit = visit;
            OrderOption = orderOption;
            Encounter = encounter;
            UsedCooldownFallback = usedCooldownFallback;
        }

        public CustomerVisitData Visit { get; }
        public CustomerVisitOrderOption OrderOption { get; }
        public BusinessRandomEncounterEntry Encounter { get; }
        public bool UsedCooldownFallback { get; }
        public bool IsEncounter => Encounter != null;

        public static BusinessSequenceSelection ForVisit(
            CustomerVisitData visit,
            CustomerVisitOrderOption orderOption,
            bool usedCooldownFallback)
        {
            return new BusinessSequenceSelection(
                visit,
                orderOption,
                null,
                usedCooldownFallback);
        }

        public static BusinessSequenceSelection ForEncounter(
            BusinessRandomEncounterEntry encounter)
        {
            return new BusinessSequenceSelection(null, null, encounter, false);
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
                    || !ProgressConditionEvaluator.IsMet(visit.condition, progress, visit.maxDay)
                    || !HasEligibleOrder(visit, progress))
                    continue;

                result.Add(visit);
            }

            return result;
        }

        public static BusinessVisitSelection PickWeightedVisit(
            IReadOnlyList<CustomerVisitData> frozenPool,
            GameProgress progress,
            IReadOnlyDictionary<string, float> cooldownUntilByVisit,
            ISet<string> invalidVisitKeys,
            float activeBusinessSeconds,
            System.Random random)
        {
            if (frozenPool == null || frozenPool.Count == 0 || progress == null)
                return null;

            random ??= new System.Random();
            List<VisitCandidate> readyCandidates = new();
            List<VisitCandidate> cooldownFallbackCandidates = new();
            float readyTotalWeight = 0f;
            float cooldownFallbackTotalWeight = 0f;

            for (int i = 0; i < frozenPool.Count; i++)
            {
                CustomerVisitData visit = frozenPool[i];
                if (!IsStructurallyValidVisit(visit)
                    || visit.weight <= 0f
                    || (invalidVisitKeys != null && invalidVisitKeys.Contains(visit.visitKey)))
                    continue;

                List<CustomerVisitOrderOption> orders = BuildEligibleOrders(visit, progress);
                if (orders.Count == 0)
                    continue;

                VisitCandidate candidate = new(visit, orders);
                cooldownFallbackCandidates.Add(candidate);
                cooldownFallbackTotalWeight += visit.weight;

                if (!IsCoolingDown(visit.visitKey, cooldownUntilByVisit, activeBusinessSeconds))
                {
                    readyCandidates.Add(candidate);
                    readyTotalWeight += visit.weight;
                }
            }

            List<VisitCandidate> candidates = readyCandidates.Count > 0
                ? readyCandidates
                : cooldownFallbackCandidates;
            bool usedCooldownFallback = readyCandidates.Count == 0
                && cooldownFallbackCandidates.Count > 0;
            float totalWeight = readyCandidates.Count > 0
                ? readyTotalWeight
                : cooldownFallbackTotalWeight;
            if (candidates.Count == 0 || totalWeight <= 0f)
                return null;

            double roll = random.NextDouble() * totalWeight;
            float cursor = 0f;
            VisitCandidate selected = candidates[candidates.Count - 1];
            for (int i = 0; i < candidates.Count; i++)
            {
                cursor += candidates[i].Visit.weight;
                if (roll <= cursor)
                {
                    selected = candidates[i];
                    break;
                }
            }

            CustomerVisitOrderOption order = PickWeightedOrder(selected.Orders, random);
            return order != null
                ? new BusinessVisitSelection(selected.Visit, order, usedCooldownFallback)
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
            IReadOnlyDictionary<string, float> cooldownUntilByVisit,
            ISet<string> invalidVisitKeys,
            ISet<string> invalidEncounterIds,
            float activeBusinessSeconds,
            System.Random random)
        {
            if (progress == null)
                return null;

            random ??= new System.Random();
            List<SequenceCandidate> readyCandidates = new();
            List<SequenceCandidate> cooldownFallbackCandidates = new();
            float readyTotalWeight = 0f;
            float cooldownFallbackTotalWeight = 0f;

            if (frozenVisitPool != null)
            {
                for (int i = 0; i < frozenVisitPool.Count; i++)
                {
                    CustomerVisitData visit = frozenVisitPool[i];
                    if (!IsStructurallyValidVisit(visit)
                        || visit.weight <= 0f
                        || (invalidVisitKeys != null && invalidVisitKeys.Contains(visit.visitKey)))
                        continue;

                    List<CustomerVisitOrderOption> orders = BuildEligibleOrders(visit, progress);
                    if (orders.Count == 0)
                        continue;

                    SequenceCandidate candidate = new(visit, orders);
                    cooldownFallbackCandidates.Add(candidate);
                    cooldownFallbackTotalWeight += visit.weight;
                    if (!IsCoolingDown(visit.visitKey, cooldownUntilByVisit, activeBusinessSeconds))
                    {
                        readyCandidates.Add(candidate);
                        readyTotalWeight += visit.weight;
                    }
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
                        || (startedEncounterIds != null
                            && startedEncounterIds.Contains(episode.episodeId))
                        || (invalidEncounterIds != null
                            && invalidEncounterIds.Contains(episode.episodeId)))
                        continue;

                    SequenceCandidate candidate = new(entry);
                    readyCandidates.Add(candidate);
                    cooldownFallbackCandidates.Add(candidate);
                    readyTotalWeight += entry.weight;
                    cooldownFallbackTotalWeight += entry.weight;
                }
            }

            List<SequenceCandidate> candidates = readyCandidates.Count > 0
                ? readyCandidates
                : cooldownFallbackCandidates;
            bool usedCooldownFallback = readyCandidates.Count == 0
                && cooldownFallbackCandidates.Count > 0;
            float totalWeight = readyCandidates.Count > 0
                ? readyTotalWeight
                : cooldownFallbackTotalWeight;
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

            CustomerVisitOrderOption order = PickWeightedOrder(selected.Orders, random);
            return order != null
                ? BusinessSequenceSelection.ForVisit(
                    selected.Visit,
                    order,
                    usedCooldownFallback)
                : null;
        }

        public static CustomerVisitOrderOption PickWeightedOrder(
            CustomerVisitData visit,
            GameProgress progress,
            System.Random random)
        {
            if (!IsStructurallyValidVisit(visit) || progress == null)
                return null;

            return PickWeightedOrder(BuildEligibleOrders(visit, progress), random ?? new System.Random());
        }

        public static BusinessRequiredActionRule PickNextRequiredAction(
            IReadOnlyList<BusinessRequiredActionRule> rules,
            GameProgress progress,
            BusinessRequiredActionTiming timing,
            bool includeAllTimings,
            ISet<string> executedRuleIds,
            ISet<string> executedTargetKeys)
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
                    || (!includeAllTimings && rule.timing != timing)
                    || (rule.exactDay > 0 && rule.exactDay != progress.CurrentDay)
                    || !ProgressConditionEvaluator.IsMet(rule.condition, progress)
                    || !IsConfiguredRequiredTarget(rule))
                    continue;

                string targetKey = rule.TargetKey;
                if (executedTargetKeys != null && executedTargetKeys.Contains(targetKey))
                    continue;

                if (rule.actionType == BusinessRequiredActionType.EncounterEpisode
                    && progress.IsEpisodeCompleted(rule.encounterEpisode.episodeId))
                    continue;

                if (selected == null || rule.priority > selected.priority)
                    selected = rule;
            }

            return selected;
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

        private static bool HasEligibleOrder(CustomerVisitData visit, GameProgress progress)
        {
            if (visit?.orders == null)
                return false;

            for (int i = 0; i < visit.orders.Count; i++)
            {
                if (IsEligibleOrder(visit.orders[i], progress))
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
            System.Random random)
        {
            if (orders == null || orders.Count == 0)
                return null;

            float totalWeight = 0f;
            for (int i = 0; i < orders.Count; i++)
                totalWeight += Mathf.Max(0f, orders[i].weight);

            if (totalWeight <= 0f)
                return null;

            double roll = random.NextDouble() * totalWeight;
            float cursor = 0f;
            for (int i = 0; i < orders.Count; i++)
            {
                cursor += Mathf.Max(0f, orders[i].weight);
                if (roll <= cursor)
                    return orders[i];
            }

            return orders[orders.Count - 1];
        }

        private static bool IsCoolingDown(
            string visitKey,
            IReadOnlyDictionary<string, float> cooldownUntilByVisit,
            float activeBusinessSeconds)
        {
            return cooldownUntilByVisit != null
                && cooldownUntilByVisit.TryGetValue(visitKey, out float cooldownUntil)
                && cooldownUntil > activeBusinessSeconds;
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
                List<CustomerVisitOrderOption> orders)
            {
                Visit = visit;
                Orders = orders;
            }

            public CustomerVisitData Visit { get; }
            public List<CustomerVisitOrderOption> Orders { get; }
        }

        private sealed class SequenceCandidate
        {
            public SequenceCandidate(
                CustomerVisitData visit,
                List<CustomerVisitOrderOption> orders)
            {
                Visit = visit;
                Orders = orders;
            }

            public SequenceCandidate(BusinessRandomEncounterEntry encounter)
            {
                Encounter = encounter;
            }

            public CustomerVisitData Visit { get; }
            public List<CustomerVisitOrderOption> Orders { get; }
            public BusinessRandomEncounterEntry Encounter { get; }
            public float Weight => Encounter != null
                ? Encounter.weight
                : Visit != null ? Visit.weight : 0f;
        }
    }
}
