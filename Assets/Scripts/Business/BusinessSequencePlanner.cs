using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Business
{
    public static class BusinessSequencePlanner
    {
        public const string OrderEntryType = "order";

        public static BusinessDaySnapshot Create(
            int day,
            BusinessOrderFlowSettings settings,
            GameProgress progress)
        {
            if (settings == null || settings.sequenceMode == BusinessSequenceMode.Fixed)
                return CreateFixed(day, settings);

            CustomerVisitDatabase database = settings.customerVisitDatabase != null
                ? settings.customerVisitDatabase
                : CustomerVisitDatabase.LoadDefault();
            return CreateFromPool(day, settings, database, progress);
        }

        public static BusinessDaySnapshot CreateFixed(
            int day,
            BusinessOrderFlowSettings settings)
        {
            BusinessDaySnapshot snapshot = new BusinessDaySnapshot
            {
                day = day,
                seed = CreateSeed(day),
                currentIndex = 0,
                isCompleted = false
            };

            if (settings?.fixedOrders == null)
                return snapshot;

            for (int i = 0; i < settings.fixedOrders.Count; i++)
            {
                FixedBusinessOrder order = settings.fixedOrders[i];
                if (order == null || string.IsNullOrWhiteSpace(order.customerOrderKey))
                    continue;

                snapshot.entries.Add(new BusinessSequenceEntrySnapshot
                {
                    entryType = OrderEntryType,
                    entryId = order.customerOrderKey.Trim(),
                    contentId = order.requestedRecipeId?.Trim() ?? string.Empty
                });
            }

            snapshot.isCompleted = snapshot.entries.Count == 0;
            return snapshot;
        }

        public static BusinessDaySnapshot CreateFromPool(
            int day,
            BusinessOrderFlowSettings settings,
            CustomerVisitDatabase database,
            GameProgress progress)
        {
            BusinessDaySnapshot snapshot = new BusinessDaySnapshot
            {
                day = day,
                seed = CreateSeed(day),
                currentIndex = 0,
                isCompleted = false
            };

            if (settings == null || database == null || database.visits == null || progress == null)
            {
                snapshot.isCompleted = true;
                return snapshot;
            }

            List<VisitCandidate> candidates = BuildCandidates(day, database, progress);
            int minimum = Mathf.Max(0, settings.minVisitsPerDay);
            int maximum = Mathf.Max(minimum, settings.maxVisitsPerDay);
            System.Random random = new System.Random(snapshot.seed);
            int targetCount = maximum > minimum
                ? random.Next(minimum, maximum + 1)
                : minimum;

            while (snapshot.entries.Count < targetCount && candidates.Count > 0)
            {
                VisitCandidate visit = PickWeightedVisit(candidates, random);
                if (visit == null)
                    break;

                CustomerVisitOrderOption orderOption = PickWeightedOrder(visit.orders, random);
                CustomerOrderData order = orderOption?.order;
                if (order != null)
                {
                    snapshot.entries.Add(new BusinessSequenceEntrySnapshot
                    {
                        entryType = OrderEntryType,
                        entryId = order.key.Trim(),
                        contentId = order.requestedRecipeId.Trim(),
                        visitKey = visit.visit.visitKey.Trim(),
                        orderType = (int)order.orderType
                    });
                }

                if (!visit.visit.allowDuplicateInDay)
                    candidates.Remove(visit);
            }

            snapshot.isCompleted = snapshot.entries.Count == 0;
            return snapshot;
        }

        private static List<VisitCandidate> BuildCandidates(
            int day,
            CustomerVisitDatabase database,
            GameProgress progress)
        {
            List<VisitCandidate> result = new List<VisitCandidate>();
            for (int i = 0; i < database.visits.Count; i++)
            {
                CustomerVisitData visit = database.visits[i];
                if (visit == null
                    || string.IsNullOrWhiteSpace(visit.visitKey)
                    || visit.weight <= 0f
                    || visit.members == null
                    || visit.members.Count == 0
                    || !ProgressConditionEvaluator.IsMet(visit.condition, progress, visit.maxDay)
                    || IsOnCooldown(visit, day, progress))
                    continue;

                List<CustomerVisitOrderOption> orders = new List<CustomerVisitOrderOption>();
                if (visit.orders != null)
                {
                    for (int orderIndex = 0; orderIndex < visit.orders.Count; orderIndex++)
                    {
                        CustomerVisitOrderOption option = visit.orders[orderIndex];
                        CustomerOrderData order = option?.order;
                        if (option == null
                            || option.weight <= 0f
                            || order == null
                            || string.IsNullOrWhiteSpace(order.key)
                            || string.IsNullOrWhiteSpace(order.requestedRecipeId)
                            || !ProgressConditionEvaluator.IsMet(option.condition, progress))
                            continue;

                        orders.Add(option);
                    }
                }

                if (orders.Count > 0)
                    result.Add(new VisitCandidate(visit, orders));
            }

            return result;
        }

        private static bool IsOnCooldown(CustomerVisitData visit, int day, GameProgress progress)
        {
            if (visit.cooldownDays <= 0)
                return false;

            int lastVisitedDay = progress.GetLastCustomerVisitDay(visit.visitKey);
            return lastVisitedDay >= 0 && day - lastVisitedDay <= visit.cooldownDays;
        }

        private static VisitCandidate PickWeightedVisit(
            List<VisitCandidate> candidates,
            System.Random random)
        {
            float totalWeight = 0f;
            for (int i = 0; i < candidates.Count; i++)
                totalWeight += Mathf.Max(0f, candidates[i].visit.weight);

            if (totalWeight <= 0f)
                return null;

            double roll = random.NextDouble() * totalWeight;
            float cursor = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                cursor += Mathf.Max(0f, candidates[i].visit.weight);
                if (roll <= cursor)
                    return candidates[i];
            }

            return candidates[candidates.Count - 1];
        }

        private static CustomerVisitOrderOption PickWeightedOrder(
            List<CustomerVisitOrderOption> orders,
            System.Random random)
        {
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

        private static int CreateSeed(int day)
        {
            unchecked
            {
                return (day * 397) ^ 0x51A17E;
            }
        }

        private sealed class VisitCandidate
        {
            public VisitCandidate(CustomerVisitData visit, List<CustomerVisitOrderOption> orders)
            {
                this.visit = visit;
                this.orders = orders;
            }

            public readonly CustomerVisitData visit;
            public readonly List<CustomerVisitOrderOption> orders;
        }
    }
}
