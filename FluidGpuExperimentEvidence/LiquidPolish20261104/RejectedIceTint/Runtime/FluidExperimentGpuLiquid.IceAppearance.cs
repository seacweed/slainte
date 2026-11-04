using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        private readonly Dictionary<SpriteRenderer, Material> iceOriginalMaterials = new Dictionary<SpriteRenderer, Material>();
        private readonly List<SpriteRenderer> expiredIceRenderers = new List<SpriteRenderer>();
        private MaterialPropertyBlock iceAppearanceProperties;
        private Material iceAppearanceMaterial;

        private void RestoreIceAppearance()
        {
            if (iceOriginalMaterials.Count == 0) return;
            expiredIceRenderers.Clear();
            foreach (var pair in iceOriginalMaterials)
            {
                if (pair.Key == null) { expiredIceRenderers.Add(pair.Key); continue; }
                if (pair.Key.sharedMaterial == iceAppearanceMaterial) pair.Key.sharedMaterial = pair.Value;
                pair.Key.GetPropertyBlock(iceAppearanceProperties);
                iceAppearanceProperties.SetTexture("_IceLiquidTex", Texture2D.blackTexture);
                iceAppearanceProperties.SetFloat("_ImmersionStrength", 0);
                pair.Key.SetPropertyBlock(iceAppearanceProperties);
            }
            foreach (var renderer in expiredIceRenderers) iceOriginalMaterials.Remove(renderer);
        }

        private void UpdateIceAppearance()
        {
            if (!settings.polishLiquidAppearance || !world.ObjectSortingActive || settings.iceImmersionShader == null
                || !settings.iceImmersionShader.isSupported) return;
            if (iceAppearanceMaterial == null)
                iceAppearanceMaterial = new Material(settings.iceImmersionShader) { hideFlags = HideFlags.DontSave };
            if (iceAppearanceProperties == null) iceAppearanceProperties = new MaterialPropertyBlock();
            foreach (var body in world.Items)
            {
                if (body == null || !body.isActiveAndEnabled || body.kind != LabItemKind.Ice || body.IsHeld
                    || !ownedLiquidLayers.TryGetValue(body.ContainingVesselId, out var layer) || layer.texture == null) continue;
                foreach (var renderer in body.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!iceOriginalMaterials.ContainsKey(renderer)) iceOriginalMaterials.Add(renderer, renderer.sharedMaterial);
                    renderer.sharedMaterial = iceAppearanceMaterial;
                    renderer.GetPropertyBlock(iceAppearanceProperties);
                    // Only this ice's owner texture is sampled; overlapping glasses cannot tint it.
                    iceAppearanceProperties.SetTexture("_IceLiquidTex", layer.texture);
                    iceAppearanceProperties.SetFloat("_ImmersionStrength", settings.submergedIceTint);
                    renderer.SetPropertyBlock(iceAppearanceProperties);
                }
            }
        }

        private void DisposeIceAppearance()
        {
            RestoreIceAppearance(); iceOriginalMaterials.Clear();
            if (iceAppearanceMaterial != null) Destroy(iceAppearanceMaterial);
            iceAppearanceMaterial = null;
        }
    }
}
