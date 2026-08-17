using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.TV
{
    public enum TVBroadcastEffectType
    {
        None,
        DisableDelivery,
        DisableRestShop,
        BoostOrderTagWeight,
        BoostTips,
        BoostCustomerTagWeight
    }

    [Serializable]
    public sealed class TVBroadcastEntry
    {
        public string id;
        public string title;
        [TextArea(2, 5)] public string tickerText;
        [Min(0f)] public float weight = 1f;
        public Sprite presenterSprite;
        public Sprite eventSprite;
        public TVBroadcastEffectType effectType;
        public string targetTag;
        [Min(0f)] public float effectMultiplier = 1f;
        [Tooltip("고도수 주문 효과 전용입니다. 음수이면 자동 도수 판정을 사용하지 않습니다.")]
        public float minimumAbvPercent = -1f;
        [TextArea(1, 3)] public string restrictionReason;
    }

    [CreateAssetMenu(
        menuName = "Slainte/TV/Broadcast Database",
        fileName = "TVBroadcastDatabase")]
    public sealed class TVBroadcastDatabase : ScriptableObject
    {
        public const string ResourcePath = "TV/TVBroadcastDatabase";
        private static TVBroadcastDatabase cachedDefault;

        public List<TVBroadcastEntry> broadcasts = new();

        public static TVBroadcastDatabase LoadDefault()
        {
            cachedDefault ??= Resources.Load<TVBroadcastDatabase>(ResourcePath);
            return cachedDefault;
        }

        public TVBroadcastEntry FindById(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || broadcasts == null)
                return null;

            for (int i = 0; i < broadcasts.Count; i++)
            {
                TVBroadcastEntry entry = broadcasts[i];
                if (entry != null
                    && string.Equals(entry.id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        public TVBroadcastEntry PickWeighted(System.Random random)
        {
            if (broadcasts == null || broadcasts.Count == 0)
                return null;

            random ??= new System.Random();
            float totalWeight = 0f;
            TVBroadcastEntry fallback = null;
            for (int i = 0; i < broadcasts.Count; i++)
            {
                TVBroadcastEntry entry = broadcasts[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || entry.weight <= 0f)
                    continue;

                fallback = entry;
                totalWeight += entry.weight;
            }

            if (fallback == null || totalWeight <= 0f)
                return null;

            double roll = random.NextDouble() * totalWeight;
            float cursor = 0f;
            for (int i = 0; i < broadcasts.Count; i++)
            {
                TVBroadcastEntry entry = broadcasts[i];
                if (entry == null || entry.weight <= 0f)
                    continue;

                cursor += entry.weight;
                if (roll <= cursor)
                    return entry;
            }

            return fallback;
        }
    }
}
