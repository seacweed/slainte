using UnityEngine;
using UnityEngine.Rendering;

namespace Slainte.Bartending.PhysicsLab
{
    public sealed partial class PhysicsLabGpuLiquid
    {
        private RenderTexture surfaceDensity, surfaceColor, surfaceShape, surfaceComposite;
        private Material surfaceAccumulationMaterial, surfaceCompositeMaterial, surfaceDisplayMaterial;
        private Material streamAccumulationMaterial;
        private MaterialPropertyBlock surfaceParticleProperties, surfaceOutputProperties;
        private CommandBuffer surfaceCommands;
        private Mesh surfaceQuad;
        private MeshRenderer surfaceRenderer;
        private readonly RenderTargetIdentifier[] surfaceMrt = new RenderTargetIdentifier[2];
        public bool SurfaceRenderingReady => surfaceComposite != null && surfaceRenderer != null;
        public Vector2Int SurfaceTextureSize => surfaceComposite != null
            ? new Vector2Int(surfaceComposite.width, surfaceComposite.height) : Vector2Int.zero;
        public string SurfaceRenderingError { get; private set; }

        private bool DrawLiquidSurface(ScriptableRenderContext context, Camera camera)
        {
            if (!EnsureSurfaceResources(camera)) return false;
            // Reuse the existing GPU particle layout and accumulation passes without readback.
            surfaceParticleProperties.SetBuffer("_GpuLiquidParticles", useStreamRendering ? surfaceParticleBuffer : particleBuffer);
            surfaceParticleProperties.SetBuffer("_StreamSegments", streamSegmentBuffer);
            surfaceParticleProperties.SetBuffer("_GpuLiquidColors", particleColorBuffer);
            surfaceParticleProperties.SetFloat("_GpuContainedParticleRadius", Radius * Mathf.Clamp(settings.containedRenderRadius, 1, 2));
            surfaceParticleProperties.SetFloat("_GpuAirborneParticleRadius", Radius * Mathf.Clamp(settings.airborneRenderRadius, 1, 2));
            surfaceParticleProperties.SetFloat("_GpuParticleZ", 0);
            surfaceParticleProperties.SetFloat("_GpuAirborneStretchMultiplier", Mathf.Clamp(settings.airborneStretch, 1, 3));
            surfaceParticleProperties.SetFloat("_GpuAirborneFullStretchSpeed", Mathf.Max(.1f, settings.airborneFullStretchSpeed));

            surfaceCompositeMaterial.SetTexture("_DensityTex", surfaceDensity);
            surfaceCompositeMaterial.SetTexture("_ColorTex", surfaceColor);
            surfaceCompositeMaterial.SetTexture("_ShapeTex", surfaceShape);
            surfaceCompositeMaterial.SetFloat("_Threshold", settings.surfaceThreshold);
            surfaceCompositeMaterial.SetFloat("_MergeStrength", settings.surfaceMergeStrength);
            surfaceCompositeMaterial.SetFloat("_EdgeSoftness", settings.surfaceEdgeSoftness);

            surfaceCommands.Clear();
            surfaceCommands.BeginSample("PhysicsLab Stream Surface");
            if (useStreamRendering) PrepareStreamSurface(surfaceCommands);
            ClearSurfaceTarget(surfaceDensity); ClearSurfaceTarget(surfaceColor);
            ClearSurfaceTarget(surfaceShape); ClearSurfaceTarget(surfaceComposite);
            surfaceCommands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
            if (SystemInfo.supportedRenderTargetCount >= 2)
            {
                surfaceCommands.SetRenderTarget(surfaceMrt, BuiltinRenderTextureType.None);
                AccumulateSurface(0);
            }
            else
            {
                surfaceCommands.SetRenderTarget(surfaceDensity); AccumulateSurface(1);
                surfaceCommands.SetRenderTarget(surfaceColor); AccumulateSurface(2);
            }
            surfaceCommands.SetRenderTarget(surfaceShape); AccumulateSurface(3);
            surfaceCommands.SetRenderTarget(surfaceComposite);
            surfaceCommands.SetViewProjectionMatrices(Matrix4x4.identity,
                GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1, 1, -1, 1, -1, 1), true));
            surfaceCommands.DrawMesh(surfaceQuad, Matrix4x4.identity, surfaceCompositeMaterial, 0, 0);
            surfaceCommands.EndSample("PhysicsLab Stream Surface");
            context.ExecuteCommandBuffer(surfaceCommands);

            surfaceOutputProperties.SetTexture("_SurfaceTex", surfaceComposite);
            surfaceOutputProperties.SetVector("_SurfaceTex_TexelSize", new Vector4(1f / surfaceComposite.width,
                1f / surfaceComposite.height, surfaceComposite.width, surfaceComposite.height));
            surfaceOutputProperties.SetFloat("_HighlightStrength", settings.surfaceHighlightStrength);
            surfaceOutputProperties.SetFloat("_HighlightWidth", settings.surfaceHighlightWidth);
            surfaceRenderer.SetPropertyBlock(surfaceOutputProperties);
            float distance = Mathf.Abs(Vector3.Dot(-camera.transform.position, camera.transform.forward));
            Vector3 center = camera.ViewportToWorldPoint(new Vector3(.5f, .5f, distance));
            Vector3 bottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, distance));
            Vector3 topRight = camera.ViewportToWorldPoint(new Vector3(1, 1, distance));
            Transform output = surfaceRenderer.transform;
            output.SetPositionAndRotation(center, camera.transform.rotation);
            output.localScale = new Vector3(Vector3.Dot(topRight - bottomLeft, camera.transform.right) * .5f,
                Vector3.Dot(topRight - bottomLeft, camera.transform.up) * .5f, 1);
            surfaceRenderer.forceRenderingOff = false;
            return true;
        }

        private void AccumulateSurface(int pass)
        {
            surfaceCommands.DrawProcedural(Matrix4x4.identity, surfaceAccumulationMaterial, pass,
                MeshTopology.Triangles, 6, particleCapacity, surfaceParticleProperties);
            if (useStreamRendering) surfaceCommands.DrawProcedural(Matrix4x4.identity, streamAccumulationMaterial, pass,
                MeshTopology.Triangles, 6, particleCapacity + MaximumStreamHeads, surfaceParticleProperties);
        }

        private void ClearSurfaceTarget(RenderTexture target)
        {
            surfaceCommands.SetRenderTarget(target);
            surfaceCommands.ClearRenderTarget(false, true, Color.clear);
        }

        private bool EnsureSurfaceResources(Camera camera)
        {
            if (settings.surfaceAccumulationShader == null || settings.surfaceCompositeShader == null
                || settings.surfaceDisplayShader == null || settings.streamAccumulationShader == null
                || !settings.streamAccumulationShader.isSupported || !settings.surfaceAccumulationShader.isSupported
                || !settings.surfaceCompositeShader.isSupported || !settings.surfaceDisplayShader.isSupported)
            {
                if (SurfaceRenderingError == null)
                    Debug.LogWarning("[PhysicsLab] Surface shaders unavailable; showing diagnostic particles.", this);
                SurfaceRenderingError = "Surface shaders unavailable";
                return false;
            }
            SurfaceRenderingError = null;
            if (surfaceRenderer == null)
            {
                surfaceAccumulationMaterial = new Material(settings.surfaceAccumulationShader) { hideFlags = HideFlags.DontSave };
                surfaceCompositeMaterial = new Material(settings.surfaceCompositeShader) { hideFlags = HideFlags.DontSave };
                surfaceDisplayMaterial = new Material(settings.surfaceDisplayShader) { hideFlags = HideFlags.DontSave };
                streamAccumulationMaterial = new Material(settings.streamAccumulationShader) { hideFlags = HideFlags.DontSave };
                surfaceParticleProperties = new MaterialPropertyBlock();
                surfaceOutputProperties = new MaterialPropertyBlock();
                surfaceCommands = new CommandBuffer { name = "PhysicsLab Liquid Surface" };
                surfaceQuad = new Mesh { name = "PhysicsLab Surface Quad", hideFlags = HideFlags.DontSave };
                surfaceQuad.vertices = new[] { new Vector3(-1,-1), new Vector3(-1,1), new Vector3(1,1), new Vector3(1,-1) };
                surfaceQuad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                surfaceQuad.triangles = new[] { 0,1,2,0,2,3 };
                surfaceQuad.RecalculateBounds();
                var output = new GameObject("PhysicsLab Liquid Surface") { layer = world.itemLayer, hideFlags = HideFlags.DontSave };
                output.transform.SetParent(transform, false);
                output.AddComponent<MeshFilter>().sharedMesh = surfaceQuad;
                surfaceRenderer = output.AddComponent<MeshRenderer>();
                surfaceRenderer.sharedMaterial = surfaceDisplayMaterial;
                surfaceRenderer.sortingOrder = 0; // Glass/tool backs: -2/-1. Fronts: 4. Ice: 5.
                surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
                surfaceRenderer.receiveShadows = false;
                surfaceRenderer.forceRenderingOff = true;
            }
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
            surfaceShape = CreateSurfaceTarget("Shape", width, height, densityFormat);
            surfaceComposite = CreateSurfaceTarget("Composite", width, height, RenderTextureFormat.ARGBHalf);
            surfaceMrt[0] = surfaceDensity; surfaceMrt[1] = surfaceColor;
            return true;
        }

        private static RenderTexture CreateSurfaceTarget(string label, int width, int height, RenderTextureFormat format)
        {
            var target = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            { name = "PhysicsLab " + label, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            target.Create();
            return target;
        }
        private void ReleaseSurfaceTargets()
        {
            foreach (RenderTexture target in new[] { surfaceDensity, surfaceColor, surfaceShape, surfaceComposite })
                if (target != null) { target.Release(); Destroy(target); }
            surfaceDensity = surfaceColor = surfaceShape = surfaceComposite = null;
        }
        private void DisposeSurfaceRendering()
        {
            ReleaseSurfaceTargets();
            surfaceCommands?.Release(); surfaceCommands = null;
            if (surfaceRenderer != null) { surfaceRenderer.forceRenderingOff = true; Destroy(surfaceRenderer.gameObject); }
            if (surfaceQuad != null) Destroy(surfaceQuad);
            if (surfaceAccumulationMaterial != null) Destroy(surfaceAccumulationMaterial);
            if (surfaceCompositeMaterial != null) Destroy(surfaceCompositeMaterial);
            if (surfaceDisplayMaterial != null) Destroy(surfaceDisplayMaterial);
            if (streamAccumulationMaterial != null) Destroy(streamAccumulationMaterial);
            streamAccumulationMaterial = null;
            surfaceRenderer = null; surfaceQuad = null;
            surfaceAccumulationMaterial = surfaceCompositeMaterial = surfaceDisplayMaterial = null;
            SurfaceRenderingError = null;
        }
    }
}
