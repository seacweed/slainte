using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        private sealed class OwnedLiquidLayer
        {
            public RenderTexture texture;
            public MeshRenderer renderer;
            public MaterialPropertyBlock properties = new MaterialPropertyBlock();
        }
        private readonly Dictionary<uint, OwnedLiquidLayer> ownedLiquidLayers = new Dictionary<uint, OwnedLiquidLayer>();
        private readonly List<uint> liquidLayerOwners = new List<uint>();
        private readonly List<uint> removedLiquidLayers = new List<uint>();

        private void HideOwnedLiquidLayers()
        {
            foreach (var layer in ownedLiquidLayers.Values) if (layer.renderer != null) layer.renderer.forceRenderingOff = true;
        }
        private void PrepareLiquidLayers()
        {
            liquidLayerOwners.Clear(); liquidLayerOwners.Add(0);
            if (world.ObjectSortingActive)
                foreach (var vessel in world.Items)
                    if (vessel != null && vessel.IsVessel && vessel.isActiveAndEnabled && vessel.Body.simulated)
                        liquidLayerOwners.Add(vessel.Id);
            removedLiquidLayers.Clear();
            foreach (uint owner in ownedLiquidLayers.Keys) if (!liquidLayerOwners.Contains(owner)) removedLiquidLayers.Add(owner);
            foreach (uint owner in removedLiquidLayers)
            {
                var layer = ownedLiquidLayers[owner];
                if (layer.texture != null) { layer.texture.Release(); Destroy(layer.texture); }
                if (layer.renderer != null) { layer.renderer.forceRenderingOff = true; Destroy(layer.renderer.gameObject); }
                ownedLiquidLayers.Remove(owner);
            }
        }
        private OwnedLiquidLayer GetLiquidLayer(uint owner)
        {
            if (!ownedLiquidLayers.TryGetValue(owner, out var layer))
            {
                layer = new OwnedLiquidLayer(); ownedLiquidLayers.Add(owner, layer);
                var output = new GameObject("FluidExperiment Liquid Owner " + owner) { layer = world.itemLayer, hideFlags = HideFlags.DontSave };
                output.transform.SetParent(transform, false);
                output.AddComponent<MeshFilter>().sharedMesh = surfaceQuad;
                layer.renderer = output.AddComponent<MeshRenderer>();
                layer.renderer.sharedMaterial = surfaceDisplayMaterial;
                layer.renderer.shadowCastingMode = ShadowCastingMode.Off; layer.renderer.receiveShadows = false;
                layer.renderer.forceRenderingOff = true;
            }
            if (layer.texture == null || layer.texture.width != surfaceComposite.width || layer.texture.height != surfaceComposite.height)
            {
                if (layer.texture != null) { layer.texture.Release(); Destroy(layer.texture); }
                layer.texture = CreateSurfaceTarget("Owner " + owner, surfaceComposite.width, surfaceComposite.height, RenderTextureFormat.ARGBHalf);
            }
            return layer;
        }
        private void SetLiquidLayerOutput(Camera camera, MeshRenderer renderer, MaterialPropertyBlock properties,
            RenderTexture texture, uint owner)
        {
            properties.SetTexture("_SurfaceTex", texture);
            properties.SetVector("_SurfaceTex_TexelSize", new Vector4(1f / texture.width, 1f / texture.height, texture.width, texture.height));
            properties.SetFloat("_HighlightStrength", settings.surfaceHighlightStrength);
            properties.SetFloat("_HighlightWidth", settings.surfaceHighlightWidth);
            renderer.SetPropertyBlock(properties);
            if (world.ObjectSortingActive && world.TryGetLiquidVisualOrder(owner, out int layer, out int order))
            { renderer.sortingLayerID = layer; renderer.sortingOrder = order; }
            else { renderer.sortingLayerID = 0; renderer.sortingOrder = 0; }
            float distance = Mathf.Abs(Vector3.Dot(-camera.transform.position, camera.transform.forward));
            Vector3 center = camera.ViewportToWorldPoint(new Vector3(.5f, .5f, distance));
            Vector3 bottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, distance));
            Vector3 topRight = camera.ViewportToWorldPoint(new Vector3(1, 1, distance));
            renderer.transform.SetPositionAndRotation(center, camera.transform.rotation);
            renderer.transform.localScale = new Vector3(Vector3.Dot(topRight - bottomLeft, camera.transform.right) * .5f,
                Vector3.Dot(topRight - bottomLeft, camera.transform.up) * .5f, 1);
            renderer.forceRenderingOff = false;
        }
        private void ReleaseOwnedLiquidTargets()
        {
            foreach (var layer in ownedLiquidLayers.Values)
                if (layer.texture != null) { layer.texture.Release(); Destroy(layer.texture); layer.texture = null; }
        }
        private void DisposeOwnedLiquidLayers()
        {
            ReleaseOwnedLiquidTargets();
            foreach (var layer in ownedLiquidLayers.Values)
                if (layer.renderer != null) { layer.renderer.forceRenderingOff = true; Destroy(layer.renderer.gameObject); }
            ownedLiquidLayers.Clear(); liquidLayerOwners.Clear();
        }
    }
}
