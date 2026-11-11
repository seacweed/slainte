using UnityEngine;
using UnityEngine.Rendering;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        [Header("Liquid surface")]
        [Tooltip("Support radius of each soft splat. This does not change collision radius or liquid volume.")]
        [Range(1f, 3f)] public float referenceSupportRadius = 1.9f;
        [Range(.05f, .9f)] public float referenceDensityThreshold = .42f;
        [Range(.005f, .15f)] public float referenceEdgeSoftness = .025f;

        private RenderTexture surfaceDensity, surfaceColor, surfaceComposite;
        private Material surfaceAccumulationMaterial, surfaceCompositeMaterial, surfaceDisplayMaterial;
        private MaterialPropertyBlock surfaceParticleProperties, surfaceOutputProperties;
        private CommandBuffer surfaceCommands;
        private Mesh surfaceQuad;
        private MeshRenderer surfaceRenderer;
        private readonly RenderTargetIdentifier[] surfaceMrt = new RenderTargetIdentifier[2];
        public bool SurfaceRenderingReady => surfaceComposite != null && surfaceRenderer != null;
        public Vector2Int SurfaceTextureSize => surfaceComposite != null
            ? new Vector2Int(surfaceComposite.width, surfaceComposite.height) : Vector2Int.zero;
        public string SurfaceRenderingError { get; private set; }
        public long RenderedSurfaceFrames { get; private set; }
        public int LastRenderedSurfaceFrame { get; private set; } = -1;

        private bool DrawLiquidSurface(ScriptableRenderContext context, Camera camera)
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.SurfaceRender);
            if (!EnsureSurfaceResources(camera)) return false;
            surfaceParticleProperties.SetBuffer("_GpuLiquidColors", particleColorBuffer);
            surfaceParticleProperties.SetFloat("_GpuParticleZ", 0);
            // Own the offscreen projection. URP's camera constants can belong to the
            // Editor or another camera when this callback runs before camera setup.
            surfaceParticleProperties.SetMatrix("_SurfaceWorldToClip",
                GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix);
            if (!PrepareImprovedSurface()) return false;
            surfaceCompositeMaterial.SetTexture("_DensityTex", surfaceDensity);
            surfaceCompositeMaterial.SetTexture("_ColorTex", surfaceColor);
            surfaceCompositeMaterial.SetFloat("_Threshold", referenceDensityThreshold);
            surfaceCompositeMaterial.SetFloat("_EdgeSoftness", referenceEdgeSoftness);

            PrepareLiquidLayers();
            foreach (uint owner in liquidLayerOwners)
            {
                var layer = owner == 0 ? null : GetLiquidLayer(owner);
                RenderTexture compositeTarget = layer != null ? layer.texture : surfaceComposite;
                surfaceParticleProperties.SetInt("_SurfaceOwnerFilterEnabled", world.ObjectSortingActive ? 1 : 0);
                surfaceParticleProperties.SetInt("_SurfaceOwnerFilter", (int)owner);
                surfaceCommands.Clear();
                const string sample = "FluidExperiment Improved Surface";
                surfaceCommands.BeginSample(sample);
                ClearSurfaceTarget(surfaceDensity); ClearSurfaceTarget(surfaceColor);
                // The composite overwrites every pixel, including exact zero in empty space.
                if (SystemInfo.supportedRenderTargetCount >= 2)
                {
                    surfaceCommands.SetRenderTarget(surfaceMrt, BuiltinRenderTextureType.None);
                    SetSurfaceRasterArea(surfaceDensity);
                    AccumulateSurface(0);
                }
                else
                {
                    surfaceCommands.SetRenderTarget(surfaceDensity); SetSurfaceRasterArea(surfaceDensity); AccumulateSurface(1);
                    surfaceCommands.SetRenderTarget(surfaceColor); SetSurfaceRasterArea(surfaceColor); AccumulateSurface(2);
                }
                surfaceCommands.SetRenderTarget(compositeTarget);
                SetSurfaceRasterArea(compositeTarget);
                surfaceCommands.DrawMesh(surfaceQuad, Matrix4x4.identity, surfaceCompositeMaterial, 0, 0);
                surfaceCommands.DisableScissorRect();
                surfaceCommands.EndSample(sample);
                context.ExecuteCommandBuffer(surfaceCommands);

                SetLiquidLayerOutput(camera, layer != null ? layer.renderer : surfaceRenderer,
                    layer != null ? layer.properties : surfaceOutputProperties, compositeTarget, owner);
            }
            // Count only successful surface submissions from the actual camera render path.
            if (LastRenderedSurfaceFrame != Time.frameCount)
            {
                LastRenderedSurfaceFrame = Time.frameCount;
                RenderedSurfaceFrames++;
            }
            return true;
        }

        private void AccumulateSurface(int pass)
        {
            surfaceCommands.DrawProcedural(Matrix4x4.identity, surfaceAccumulationMaterial, pass,
                MeshTopology.Triangles, 6, particleCapacity, surfaceParticleProperties);
        }

        private void ClearSurfaceTarget(RenderTexture target)
        {
            surfaceCommands.SetRenderTarget(target);
            SetSurfaceRasterArea(target);
            surfaceCommands.ClearRenderTarget(false, true, Color.clear);
        }

        // Each offscreen target owns its full raster area, including MRT and fallback passes.
        private void SetSurfaceRasterArea(RenderTexture target)
        {
            var area = new Rect(0, 0, target.width, target.height);
            surfaceCommands.SetViewport(area);
            surfaceCommands.EnableScissorRect(area);
        }

        private bool EnsureSurfaceResources(Camera camera)
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.SurfaceResources);
            if (improvedAccumulationShader == null)
                improvedAccumulationShader = Shader.Find("Hidden/Slainte/FluidExperiment/ImprovedAccumulation");
            if (improvedCompositeShader == null)
                improvedCompositeShader = Shader.Find("Hidden/Slainte/FluidExperiment/ImprovedComposite");
            if (improvedAccumulationShader == null || improvedCompositeShader == null || settings.surfaceDisplayShader == null
                || !improvedAccumulationShader.isSupported || !improvedCompositeShader.isSupported || !settings.surfaceDisplayShader.isSupported)
            {
                if (SurfaceRenderingError == null)
                    Debug.LogWarning("[FluidExperiment] Surface shaders unavailable; showing diagnostic particles.", this);
                SurfaceRenderingError = "Surface shaders unavailable";
                return false;
            }
            SurfaceRenderingError = null;
            if (surfaceRenderer == null)
            {
                surfaceDisplayMaterial = new Material(settings.surfaceDisplayShader) { hideFlags = HideFlags.DontSave };
                surfaceParticleProperties = new MaterialPropertyBlock();
                surfaceOutputProperties = new MaterialPropertyBlock();
                surfaceCommands = new CommandBuffer { name = "FluidExperiment Liquid Surface" };
                surfaceQuad = new Mesh { name = "FluidExperiment Surface Quad", hideFlags = HideFlags.DontSave };
                surfaceQuad.vertices = new[] { new Vector3(-1,-1), new Vector3(-1,1), new Vector3(1,1), new Vector3(1,-1) };
                surfaceQuad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                surfaceQuad.triangles = new[] { 0,1,2,0,2,3 };
                surfaceQuad.RecalculateBounds();
                var output = new GameObject("FluidExperiment Liquid Surface") { layer = world.itemLayer, hideFlags = HideFlags.DontSave };
                output.transform.SetParent(transform, false);
                output.AddComponent<MeshFilter>().sharedMesh = surfaceQuad;
                surfaceRenderer = output.AddComponent<MeshRenderer>();
                surfaceRenderer.sharedMaterial = surfaceDisplayMaterial;
                surfaceRenderer.sortingOrder = 0; // Unowned liquid.
                surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
                surfaceRenderer.receiveShadows = false;
                surfaceRenderer.forceRenderingOff = true;
            }
            SetSurfaceMaterial(ref surfaceAccumulationMaterial, improvedAccumulationShader);
            SetSurfaceMaterial(ref surfaceCompositeMaterial, improvedCompositeShader);
            float scale = Mathf.Clamp(settings.surfaceResolutionScale, .25f, 1);
            scale = Mathf.Min(scale, 2048f / Mathf.Max(camera.pixelWidth, camera.pixelHeight, 1));
            int width = Mathf.Max(32, Mathf.RoundToInt(camera.pixelWidth * scale));
            int height = Mathf.Max(32, Mathf.RoundToInt(camera.pixelHeight * scale));
            if (surfaceComposite != null && surfaceComposite.width == width && surfaceComposite.height == height) return true;
            ReleaseSurfaceTargets();
            RenderTextureFormat densityFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf)
                ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGBHalf;
            surfaceDensity = CreateSurfaceTarget("Density", width, height, densityFormat);
            surfaceColor = CreateSurfaceTarget("Color", width, height, RenderTextureFormat.ARGBHalf);
            surfaceComposite = CreateSurfaceTarget("Composite", width, height, RenderTextureFormat.ARGBHalf);
            surfaceMrt[0] = surfaceDensity; surfaceMrt[1] = surfaceColor;
            return true;
        }

        private static void SetSurfaceMaterial(ref Material material, Shader shader)
        {
            if (material != null && material.shader == shader) return;
            if (material != null) Destroy(material);
            material = new Material(shader) { hideFlags = HideFlags.DontSave };
        }

        private static RenderTexture CreateSurfaceTarget(string label, int width, int height, RenderTextureFormat format)
        {
            var target = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            { name = "FluidExperiment " + label, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            target.Create();
            return target;
        }
        private void ReleaseSurfaceTargets()
        {
            ReleaseOwnedLiquidTargets();
            foreach (RenderTexture target in new[] { surfaceDensity, surfaceColor, surfaceComposite })
                if (target != null) { target.Release(); Destroy(target); }
            surfaceDensity = surfaceColor = surfaceComposite = null;
        }
        private void DisposeSurfaceRendering()
        {
            DisposeOwnedLiquidLayers();
            DisposeImprovedSurface();
            ReleaseSurfaceTargets();
            surfaceCommands?.Release(); surfaceCommands = null;
            if (surfaceRenderer != null) { surfaceRenderer.forceRenderingOff = true; Destroy(surfaceRenderer.gameObject); }
            if (surfaceQuad != null) Destroy(surfaceQuad);
            if (surfaceAccumulationMaterial != null) Destroy(surfaceAccumulationMaterial);
            if (surfaceCompositeMaterial != null) Destroy(surfaceCompositeMaterial);
            if (surfaceDisplayMaterial != null) Destroy(surfaceDisplayMaterial);
            surfaceRenderer = null; surfaceQuad = null;
            surfaceAccumulationMaterial = surfaceCompositeMaterial = surfaceDisplayMaterial = null;
            SurfaceRenderingError = null;
        }
    }
}
