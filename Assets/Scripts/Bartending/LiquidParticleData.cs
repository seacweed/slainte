using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    [Serializable]
    public struct LiquidPortion
    {
        public ItemDef sourceItem;
        public float volumeMl;
    }

    [Serializable]
    public sealed class LiquidPayload
    {
        public List<LiquidPortion> portions = new();

        public float TotalVolumeMl
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < portions.Count; i++)
                    total += portions[i].volumeMl;
                return total;
            }
        }

        public void SetSingle(ItemDef sourceItem, float volumeMl)
        {
            portions.Clear();

            if (sourceItem == null || volumeMl <= 0f)
                return;

            portions.Add(new LiquidPortion
            {
                sourceItem = sourceItem,
                volumeMl = volumeMl
            });
        }

        public float GetVolume(ItemDef sourceItem)
        {
            for (int i = 0; i < portions.Count; i++)
            {
                if (portions[i].sourceItem == sourceItem)
                    return portions[i].volumeMl;
            }

            return 0f;
        }

        public void SetVolume(ItemDef sourceItem, float volumeMl)
        {
            if (sourceItem == null)
                return;

            for (int i = 0; i < portions.Count; i++)
            {
                if (portions[i].sourceItem == sourceItem)
                {
                    if (volumeMl <= 0.0001f)
                    {
                        portions.RemoveAt(i);
                        return;
                    }

                    LiquidPortion portion = portions[i];
                    portion.volumeMl = volumeMl;
                    portions[i] = portion;
                    return;
                }
            }

            if (volumeMl > 0.0001f)
            {
                portions.Add(new LiquidPortion
                {
                    sourceItem = sourceItem,
                    volumeMl = volumeMl
                });
            }
        }

        public static void MixPair(LiquidPayload left, LiquidPayload right, float strength)
        {
            if (left == null || right == null)
                return;

            float leftTotal = left.TotalVolumeMl;
            float rightTotal = right.TotalVolumeMl;
            if (leftTotal <= 0f || rightTotal <= 0f)
                return;

            strength = Mathf.Clamp01(strength);

            List<ItemDef> keys = new();
            AddKeys(left, keys);
            AddKeys(right, keys);

            for (int i = 0; i < keys.Count; i++)
            {
                ItemDef item = keys[i];

                float leftRatio = left.GetVolume(item) / leftTotal;
                float rightRatio = right.GetVolume(item) / rightTotal;

                float newLeftRatio = Mathf.Lerp(leftRatio, rightRatio, strength);
                float newRightRatio = Mathf.Lerp(rightRatio, leftRatio, strength);

                left.SetVolume(item, newLeftRatio * leftTotal);
                right.SetVolume(item, newRightRatio * rightTotal);
            }
        }

        private static void AddKeys(LiquidPayload payload, List<ItemDef> keys)
        {
            for (int i = 0; i < payload.portions.Count; i++)
            {
                ItemDef item = payload.portions[i].sourceItem;
                if (item != null && !keys.Contains(item))
                    keys.Add(item);
            }
        }

        public Color EvaluateColor(float minimumAlpha = 0.75f)
        {
            float validTotal = 0f;
            for (int i = 0; i < portions.Count; i++)
            {
                LiquidPortion portion = portions[i];
                if (portion.sourceItem != null && portion.volumeMl > 0f)
                    validTotal += portion.volumeMl;
            }

            if (validTotal <= 0f)
                return Color.clear;

            float r = 0f;
            float g = 0f;
            float b = 0f;
            float a = 0f;

            for (int i = 0; i < portions.Count; i++)
            {
                LiquidPortion portion = portions[i];
                if (portion.sourceItem == null || portion.volumeMl <= 0f)
                    continue;

                float weight = portion.volumeMl / validTotal;
                Color sourceColor = portion.sourceItem.liquidColor;

                r += sourceColor.r * weight;
                g += sourceColor.g * weight;
                b += sourceColor.b * weight;
                a += Mathf.Max(sourceColor.a, minimumAlpha) * weight;
            }

            return new Color(
                Mathf.Clamp01(r),
                Mathf.Clamp01(g),
                Mathf.Clamp01(b),
                Mathf.Clamp01(a));
        }

        public bool HasDifferentComposition(LiquidPayload other, float tolerance = 0.001f)
        {
            if (other == null)
                return TotalVolumeMl > tolerance;

            float myTotal = TotalVolumeMl;
            float otherTotal = other.TotalVolumeMl;

            if (myTotal <= tolerance && otherTotal <= tolerance)
                return false;

            if (myTotal <= tolerance || otherTotal <= tolerance)
                return true;

            List<ItemDef> keys = new();
            AddKeys(this, keys);
            AddKeys(other, keys);

            for (int i = 0; i < keys.Count; i++)
            {
                ItemDef item = keys[i];
                float myRatio = GetVolume(item) / myTotal;
                float otherRatio = other.GetVolume(item) / otherTotal;

                if (Mathf.Abs(myRatio - otherRatio) > tolerance)
                    return true;
            }

            return false;
        }
    }

    public sealed class LiquidParticleData : MonoBehaviour
    {
        public LiquidPayload payload = new();
        public bool hasBeenCollected;

        private SpriteRenderer spriteRenderer;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        public void SetPayload(ItemDef sourceItem, float volumeMl)
        {
            hasBeenCollected = false;
            payload.SetSingle(sourceItem, volumeMl);
            ApplyVisualFromPayload();
        }

        public void MixPayloadWith(LiquidParticleData other, float strength)
        {
            if (other == null)
                return;

            LiquidPayload.MixPair(payload, other.payload, strength);
        }

        public bool HasDifferentComposition(LiquidParticleData other, float tolerance = 0.001f)
        {
            return other != null && payload.HasDifferentComposition(other.payload, tolerance);
        }

        public void ApplyVisualFromPayload()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            if (spriteRenderer == null)
                return;

            spriteRenderer.color = payload.EvaluateColor();
        }
    }
}
