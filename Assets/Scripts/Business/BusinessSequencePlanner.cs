using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Business
{
    public static class BusinessSequencePlanner
    {
        public const string OrderEntryType = "order";

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
                    || !ProgressConditionEvaluator.IsMet(visit.condition, progress, visit.maxDay))
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
