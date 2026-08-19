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
        private static readonly List<ItemDef> SharedItemBuffer = new List<ItemDef>(8);

        public List<LiquidPortion> portions = new();
        public float temperatureC = 20f;
        public CocktailTechnique techniques = CocktailTechnique.None;

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
            temperatureC = sourceItem != null ? sourceItem.servingTemperatureC : 20f;
            techniques = CocktailTechnique.None;

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

            float equilibriumTemperature =
                (left.temperatureC * leftTotal + right.temperatureC * rightTotal)
                / (leftTotal + rightTotal);
            left.temperatureC = Mathf.Lerp(left.temperatureC, equilibriumTemperature, strength);
            right.temperatureC = Mathf.Lerp(right.temperatureC, equilibriumTemperature, strength);

            CocktailTechnique combinedTechniques = left.techniques | right.techniques;
            left.techniques = combinedTechniques;
            right.techniques = combinedTechniques;

            List<ItemDef> keys = GetSharedItemBuffer();
            AddKeys(left, keys);
            AddKeys(right, keys);

            for (int i = 0; i < keys.Count; i++)
            {
                ItemDef item = keys[i];

                float leftRatio = left.GetVolume(item) / leftTotal;
                float rightRatio = right.GetVolume(item) / rightTotal;
                float equilibriumRatio =
                    (left.GetVolume(item) + right.GetVolume(item))
                    / (leftTotal + rightTotal);

                float newLeftRatio = Mathf.Lerp(leftRatio, equilibriumRatio, strength);
                float newRightRatio = Mathf.Lerp(rightRatio, equilibriumRatio, strength);

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

        public Color EvaluateColor(float minimumAlpha = 0f)
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

            if (Mathf.Abs(temperatureC - other.temperatureC) > 0.1f
                || techniques != other.techniques)
                return true;

            float myTotal = TotalVolumeMl;
            float otherTotal = other.TotalVolumeMl;

            if (myTotal <= tolerance && otherTotal <= tolerance)
                return false;

            if (myTotal <= tolerance || otherTotal <= tolerance)
                return true;

            List<ItemDef> keys = GetSharedItemBuffer();
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

        private static List<ItemDef> GetSharedItemBuffer()
        {
            SharedItemBuffer.Clear();
            return SharedItemBuffer;
        }

        public void CoolTowards(float ambientTemperatureC, float degreesPerSecond, float deltaTime)
        {
            temperatureC = Mathf.MoveTowards(
                temperatureC,
                ambientTemperatureC,
                Mathf.Max(0f, degreesPerSecond) * Mathf.Max(0f, deltaTime));
        }
    }

    public sealed class LiquidParticleData : MonoBehaviour
    {
        [Header("Volume")]
        [Tooltip("Volume in milliliters represented by one newly spawned liquid particle.")]
        [SerializeField, Min(0.01f)] private float defaultVolumeMl = 1f;

        public LiquidPayload payload = new();
        public bool hasBeenCollected;

        [Header("Thermal")]
        [SerializeField] private float ambientTemperatureC = 20f;
        [SerializeField, Min(0f)] private float coolingDegreesPerSecond = 0.35f;

        private SpriteRenderer spriteRenderer;
        private Collider2D particleCollider;
        private Color logicalColor = Color.clear;

        public float DefaultVolumeMl => Mathf.Max(0.01f, defaultVolumeMl);
        public VesselLiquidTracker VesselOwner { get; private set; }
        internal Color LogicalColor => logicalColor;
        internal SpriteRenderer ParticleRenderer
        {
            get
            {
                if (spriteRenderer == null)
                    spriteRenderer = GetComponent<SpriteRenderer>();
                return spriteRenderer;
            }
        }
        internal Collider2D ParticleCollider
        {
            get
            {
                if (particleCollider == null)
                    particleCollider = GetComponent<Collider2D>();
                return particleCollider;
            }
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            particleCollider = GetComponent<Collider2D>();
            ApplyVisualFromPayload();
        }

        private void OnEnable()
        {
            VesselLiquidTracker.RegisterParticle(this);
        }

        private void OnDisable()
        {
            ClearVesselOwner();
            VesselLiquidTracker.UnregisterParticle(this);
        }

        private void Update()
        {
            payload?.CoolTowards(ambientTemperatureC, coolingDegreesPerSecond, Time.deltaTime);
        }

        public void SetPayload(ItemDef sourceItem, float volumeMl)
        {
            ClearVesselOwner();
            hasBeenCollected = false;
            payload ??= new LiquidPayload();
            payload.SetSingle(sourceItem, volumeMl);
            ApplyVisualFromPayload();
        }

        public void MixPayloadWith(LiquidParticleData other, float strength)
        {
            if (other == null)
                return;

            LiquidPayload.MixPair(payload, other.payload, strength);
        }

        public void RecordTechnique(CocktailTechnique technique)
        {
            if (payload != null)
                payload.techniques |= technique;
        }

        public bool HasDifferentComposition(LiquidParticleData other, float tolerance = 0.001f)
        {
            return other != null && payload.HasDifferentComposition(other.payload, tolerance);
        }

        public bool CanInteractWith(LiquidParticleData other)
        {
            return other != null
                && (VesselOwner == null
                    || other.VesselOwner == null
                    || VesselOwner == other.VesselOwner);
        }

        internal bool TryAssignVesselOwner(VesselLiquidTracker owner)
        {
            if (owner == null)
                return false;

            if (VesselOwner != null)
                return VesselOwner == owner;

            VesselOwner = owner;
            owner.RegisterOwnedParticle(this);
            VesselLiquidTracker.RefreshParticleIsolation(this);
            return true;
        }

        internal void ReleaseVesselOwner(VesselLiquidTracker owner)
        {
            if (VesselOwner != owner)
                return;

            ClearVesselOwner();
        }

        internal void ClearVesselOwner()
        {
            VesselLiquidTracker previousOwner = VesselOwner;
            if (previousOwner == null)
                return;

            VesselOwner = null;
            previousOwner.UnregisterOwnedParticle(this);
            VesselLiquidTracker.RefreshParticleIsolation(this);
        }

        public void ApplyVisualFromPayload()
        {
            logicalColor = payload != null
                ? payload.EvaluateColor()
                : Color.clear;

            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            if (spriteRenderer == null)
                return;

            spriteRenderer.color = logicalColor;
        }
    }
}
