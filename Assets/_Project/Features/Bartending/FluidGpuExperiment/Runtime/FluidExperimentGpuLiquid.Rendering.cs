using UnityEngine;
using UnityEngine.Rendering;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        [Header("Experimental particle surface (C mode only)")]
        public bool useReferenceSurface;
        public Shader referenceAccumulationShader;
        public Shader referenceCompositeShader;
        [Tooltip("Support radius of each soft splat. This does not change collision radius or liquid volume.")]
        [Range(1f, 3f)] public float referenceSupportRadius = 1.9f;
        [Tooltip("Maximum velocity-aligned aspect ratio. Splat area stays constant while its shape stretches.")]
        [Range(1f, 10f)] public float referenceMaximumAspect = 6f;
        [Min(.1f)] public float referenceFullStretchSpeed = 10f;
        [Range(.05f, .9f)] public float referenceDensityThreshold = .42f;
        [Range(.005f, .15f)] public float referenceEdgeSoftness = .025f;
        [Tooltip("One finite density splat between an active lip and its newest nearby particle. Does not add liquid or link particle chains.")]
        public bool referenceNozzleSupport = true;
        [Tooltip("Maximum lip-to-particle distance, in collision radii. A separated drop never stretches this support further.")]
        [Range(1f, 6f)] public float referenceNozzleReach = 4f;
        [Range(.005f, .075f)] public float referenceNozzleMaximumAge = .06f;

        private RenderTexture surfaceDensity, surfaceColor, surfaceShape, surfaceComposite;
        private Material surfaceAccumulationMaterial, surfaceCompositeMaterial, surfaceDisplayMaterial;
        private Material streamAccumulationMaterial;
        private MaterialPropertyBlock surfaceParticleProperties, surfaceOutputProperties;
        private CommandBuffer surfaceCommands;
        private Mesh surfaceQuad;
        private MeshRenderer surfaceRenderer;
        private int referenceHeadCount;
        private readonly RenderTargetIdentifier[] surfaceMrt = new RenderTargetIdentifier[2];
        public bool SurfaceRenderingReady => surfaceComposite != null && surfaceRenderer != null;
        public Vector2Int SurfaceTextureSize => surfaceComposite != null
            ? new Vector2Int(surfaceComposite.width, surfaceComposite.height) : Vector2Int.zero;
        public string SurfaceRenderingError { get; private set; }

        private bool DrawLiquidSurface(ScriptableRenderContext context, Camera camera)
        {
            if (!EnsureSurfaceResources(camera)) return false;
            // Reuse the existing GPU particle layout and accumulation passes without readback.
            // C uses every physical particle, including falling particles inside a glass.
            // It never hides them in favor of a source-linked ribbon.
            surfaceParticleProperties.SetBuffer("_GpuLiquidParticles", !useReferenceSurface && useStreamRendering
                ? surfaceParticleBuffer : particleBuffer);
            surfaceParticleProperties.SetBuffer("_StreamSegments", streamSegmentBuffer);
            surfaceParticleProperties.SetBuffer("_GpuLiquidColors", particleColorBuffer);
            surfaceParticleProperties.SetFloat("_GpuContainedParticleRadius", Radius * Mathf.Clamp(settings.containedRenderRadius, 1, 2));
            surfaceParticleProperties.SetFloat("_GpuAirborneParticleRadius", Radius * Mathf.Clamp(settings.airborneRenderRadius, 1, 2));
            surfaceParticleProperties.SetFloat("_GpuParticleZ", 0);
            surfaceParticleProperties.SetFloat("_GpuAirborneStretchMultiplier", Mathf.Clamp(settings.airborneStretch, 1, 3));
            surfaceParticleProperties.SetFloat("_GpuAirborneFullStretchSpeed", Mathf.Max(.1f, settings.airborneFullStretchSpeed));
            surfaceParticleProperties.SetFloat("_ReferenceSupportRadius", Radius * Mathf.Clamp(referenceSupportRadius, 1f, 3f));
            surfaceParticleProperties.SetFloat("_ReferenceMaximumAspect", Mathf.Clamp(referenceMaximumAspect, 1f, 10f));
            surfaceParticleProperties.SetFloat("_ReferenceFullStretchSpeed", Mathf.Max(.1f, referenceFullStretchSpeed));
            if (useReferenceSurface) PrepareReferenceNozzles();

            surfaceCompositeMaterial.SetTexture("_DensityTex", surfaceDensity);
            surfaceCompositeMaterial.SetTexture("_ColorTex", surfaceColor);
            if (!useReferenceSurface) surfaceCompositeMaterial.SetTexture("_ShapeTex", surfaceShape);
            surfaceCompositeMaterial.SetFloat("_Threshold", useReferenceSurface ? referenceDensityThreshold : settings.surfaceThreshold);
            if (!useReferenceSurface) surfaceCompositeMaterial.SetFloat("_MergeStrength", settings.surfaceMergeStrength);
            surfaceCompositeMaterial.SetFloat("_EdgeSoftness", useReferenceSurface ? referenceEdgeSoftness : settings.surfaceEdgeSoftness);

            surfaceCommands.Clear();
            string sample = useReferenceSurface ? "FluidExperiment Particle Density Surface" : "FluidExperiment Stream Surface";
            surfaceCommands.BeginSample(sample);
            if (!useReferenceSurface && useStreamRendering) PrepareStreamSurface(surfaceCommands);
            ClearSurfaceTarget(surfaceDensity); ClearSurfaceTarget(surfaceColor);
            if (!useReferenceSurface) ClearSurfaceTarget(surfaceShape);
            ClearSurfaceTarget(surfaceComposite);
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
            if (!useReferenceSurface) { surfaceCommands.SetRenderTarget(surfaceShape); AccumulateSurface(3); }
            surfaceCommands.SetRenderTarget(surfaceComposite);
            surfaceCommands.SetViewProjectionMatrices(Matrix4x4.identity,
                GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1, 1, -1, 1, -1, 1), true));
            surfaceCommands.DrawMesh(surfaceQuad, Matrix4x4.identity, surfaceCompositeMaterial, 0, 0);
            surfaceCommands.EndSample(sample);
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
                MeshTopology.Triangles, 6, particleCapacity + (useReferenceSurface ? referenceHeadCount : 0), surfaceParticleProperties);
            if (!useReferenceSurface && useStreamRendering) surfaceCommands.DrawProcedural(Matrix4x4.identity, streamAccumulationMaterial, pass,
                MeshTopology.Triangles, 6, particleCapacity + MaximumStreamHeads, surfaceParticleProperties);
        }

        private void ClearSurfaceTarget(RenderTexture target)
        {
            surfaceCommands.SetRenderTarget(target);
            surfaceCommands.ClearRenderTarget(false, true, Color.clear);
        }

        private bool EnsureSurfaceResources(Camera camera)
        {
            if (useReferenceSurface)
            {
                if (referenceAccumulationShader == null)
                    referenceAccumulationShader = Shader.Find("Hidden/Slainte/FluidExperiment/ReferenceAccumulation");
                if (referenceCompositeShader == null)
                    referenceCompositeShader = Shader.Find("Hidden/Slainte/FluidExperiment/ReferenceComposite");
            }
            Shader accumulation = useReferenceSurface ? referenceAccumulationShader : settings.surfaceAccumulationShader;
            Shader composite = useReferenceSurface ? referenceCompositeShader : settings.surfaceCompositeShader;
            bool ribbons = !useReferenceSurface && useStreamRendering;
            if (accumulation == null || composite == null || settings.surfaceDisplayShader == null
                || !accumulation.isSupported || !composite.isSupported || !settings.surfaceDisplayShader.isSupported
                || (ribbons && (settings.streamAccumulationShader == null || !settings.streamAccumulationShader.isSupported)))
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
                surfaceRenderer.sortingOrder = 0; // Glass/tool backs: -2/-1. Fronts: 4. Ice: 5.
                surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
                surfaceRenderer.receiveShadows = false;
                surfaceRenderer.forceRenderingOff = true;
            }
            // Switching A/B/C changes materials only; the same GPU buffers and render textures are reused.
            SetSurfaceMaterial(ref surfaceAccumulationMaterial, accumulation);
            SetSurfaceMaterial(ref surfaceCompositeMaterial, composite);
            if (ribbons) SetSurfaceMaterial(ref streamAccumulationMaterial, settings.streamAccumulationShader);
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

        private void PrepareReferenceNozzles()
        {
            referenceHeadCount = 0;
            if (referenceNozzleSupport)
                foreach (FluidExperimentBody item in world.Items)
                    if (item != null && referenceHeadCount < MaximumStreamHeads
                        && item.TryGetStreamHead(out GpuLiquidStreamHead head))
                        streamHeads[referenceHeadCount++] = head;
            if (referenceHeadCount > 0) streamHeadBuffer.SetData(streamHeads, 0, 0, referenceHeadCount);
            // Only small CPU-owned nozzle metadata is uploaded. Token validity,
            // recency and genuine mixed color are resolved from GPU sidecars.
            surfaceParticleProperties.SetBuffer("_ReferenceHeads", streamHeadBuffer);
            surfaceParticleProperties.SetBuffer("_ReferenceStreamParticles", streamParticleBuffer);
            surfaceParticleProperties.SetBuffer("_ReferenceTokenLookup", streamLookupBuffer);
            surfaceParticleProperties.SetInt("_ReferenceParticleCapacity", particleCapacity);
            surfaceParticleProperties.SetInt("_ReferenceHeadCount", referenceHeadCount);
            surfaceParticleProperties.SetFloat("_ReferenceSimulationTime", simulationTime);
            surfaceParticleProperties.SetFloat("_ReferenceNozzleMaximumLength", Radius * Mathf.Clamp(referenceNozzleReach, 1f, 6f));
            surfaceParticleProperties.SetFloat("_ReferenceNozzleMaximumAge", Mathf.Clamp(referenceNozzleMaximumAge, .005f, .075f));
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
