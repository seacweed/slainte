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
        // Retain only rendering resources; body, liquid and inventory state still reset per order.
        private const int MaxCachedLiquidLayers = 4;
        private readonly Stack<OwnedLiquidLayer> cachedLiquidLayers = new Stack<OwnedLiquidLayer>(MaxCachedLiquidLayers);
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
                if (layer.renderer != null) layer.renderer.forceRenderingOff = true;
                ownedLiquidLayers.Remove(owner);
                if (cachedLiquidLayers.Count < MaxCachedLiquidLayers) cachedLiquidLayers.Push(layer);
                else DestroyLiquidLayer(layer);
            }
        }
        private OwnedLiquidLayer GetLiquidLayer(uint owner)
        {
            if (!ownedLiquidLayers.TryGetValue(owner, out var layer))
            {
                layer = cachedLiquidLayers.Count > 0 ? cachedLiquidLayers.Pop() : new OwnedLiquidLayer();
                ownedLiquidLayers.Add(owner, layer);
            }
            if (layer.renderer == null)
            {
                var output = new GameObject("FluidExperiment Liquid Owner " + owner) { layer = world.itemLayer, hideFlags = HideFlags.DontSave };
                output.transform.SetParent(transform, false);
                output.AddComponent<MeshFilter>().sharedMesh = surfaceQuad;
                layer.renderer = output.AddComponent<MeshRenderer>();
                layer.renderer.sharedMaterial = surfaceDisplayMaterial;
                layer.renderer.shadowCastingMode = ShadowCastingMode.Off; layer.renderer.receiveShadows = false;
                layer.renderer.forceRenderingOff = true;
            }
            if (layer.texture == null || layer.texture.width != surfaceComposite.width || layer.texture.height != surfaceComposite.height || !layer.texture.IsCreated())
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
            properties.SetFloat("_RimStrength", settings.polishLiquidAppearance ? settings.surfaceRimStrength : 0);
            properties.SetFloat("_LowerEdgeShade", settings.polishLiquidAppearance ? settings.surfaceLowerEdgeShade : 0);
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
        private static void ReleaseLiquidLayerTarget(OwnedLiquidLayer layer)
        {
            if (layer.texture == null) return;
            layer.texture.Release(); Destroy(layer.texture); layer.texture = null;
        }
        private static void DestroyLiquidLayer(OwnedLiquidLayer layer)
        {
            ReleaseLiquidLayerTarget(layer);
            if (layer.renderer != null) { layer.renderer.forceRenderingOff = true; Destroy(layer.renderer.gameObject); }
        }
        private void ReleaseOwnedLiquidTargets()
        {
            foreach (var layer in ownedLiquidLayers.Values) ReleaseLiquidLayerTarget(layer);
            foreach (var layer in cachedLiquidLayers) ReleaseLiquidLayerTarget(layer);
        }
        private void DisposeOwnedLiquidLayers()
        {
            foreach (var layer in ownedLiquidLayers.Values) DestroyLiquidLayer(layer);
            foreach (var layer in cachedLiquidLayers) DestroyLiquidLayer(layer);
            ownedLiquidLayers.Clear(); cachedLiquidLayers.Clear();
            liquidLayerOwners.Clear(); removedLiquidLayers.Clear();
        }
    }
}
