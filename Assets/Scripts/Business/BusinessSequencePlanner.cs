using System;

namespace Slainte.Business
{
    public static class BusinessSequencePlanner
    {
        public const string OrderEntryType = "order";

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

        private static int CreateSeed(int day)
        {
            unchecked
            {
                return (day * 397) ^ 0x51A17E;
            }
        }
    }
}
