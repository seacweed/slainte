using System.Collections.Generic;
using Slainte.Bartending;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class LiquidMetaballRenderer : MonoBehaviour
{
    private enum DebugView
    {
        Composite = 0,
        Density = 1,
        AccumulatedColor = 2,
        NormalizedColor = 3,
        NormalizedAlpha = 4,
        MaximumCoverage = 5,
        HybridDensity = 6,
        ShapeMask = 7
    }

    private const int MaxInstancesPerDraw = 1023;
    private const int MrtPass = 0;
    private const int DensityFallbackPass = 1;
    private const int ColorFallbackPass = 2;
    private const int MaximumCoveragePass = 3;
    private static readonly int DensityTexId = Shader.PropertyToID("_DensityTex");
    private static readonly int ColorTexId = Shader.PropertyToID("_ColorTex");
    private static readonly int ShapeTexId = Shader.PropertyToID("_ShapeTex");
    private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
    private static readonly int MergeStrengthId = Shader.PropertyToID("_MergeStrength");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int DebugViewId = Shader.PropertyToID("_DebugView");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ParticleColorId = Shader.PropertyToID("_ParticleColor");

    [SerializeField] private Material accumulationMaterial;
    [SerializeField] private Renderer outputRenderer;
    [SerializeField] private Renderer legacyOutputRenderer;
    [SerializeField] private Vector2Int textureSize = new Vector2Int(240, 135);
    [SerializeField, Range(0f, 1f)] private float threshold = 0.3f;
    [SerializeField, Range(0f, 1f)] private float mergeStrength = 0.45f;
    [SerializeField, Range(0f, 0.25f)] private float edgeSoftness = 0.03f;
    [SerializeField, Range(0f, 1f)] private float minimumVisibleAlpha = 0.05f;
    [SerializeField] private bool preferSinglePassMrt = true;
    [SerializeField] private DebugView debugView = DebugView.Composite;

    private Camera captureCamera;
    private RenderTexture originalTarget;
    private RenderTexture densityTexture;
    private RenderTexture colorTexture;
    private RenderTexture shapeTexture;
    private CommandBuffer commandBuffer;
    private MaterialPropertyBlock outputProperties;
    private MaterialPropertyBlock batchProperties;
    private readonly Matrix4x4[] batchMatrices = new Matrix4x4[MaxInstancesPerDraw];
    private readonly Vector4[] batchColors = new Vector4[MaxInstancesPerDraw];
    private readonly RenderTargetIdentifier[] accumulationTargets =
        new RenderTargetIdentifier[2];
    private readonly HashSet<SpriteRenderer> suppressedParticleRenderers = new();
    private Sprite cachedSprite;
    private Mesh cachedSpriteMesh;
    private bool cameraWasEnabled;
    private bool legacyOutputWasEnabled;
    private int targetWidth;
    private int targetHeight;

    private bool UseSinglePassMrt => preferSinglePassMrt
        && SystemInfo.supportedRenderTargetCount >= 2
        && SystemInfo.graphicsShaderLevel >= 35;

    public void Configure(
        Material particleAccumulationMaterial,
        Renderer compositeOutputRenderer,
        Vector2Int accumulationTextureSize,
        float densityThreshold,
        float overlapMergeStrength,
        float compositeEdgeSoftness,
        float fallbackVisibleAlpha)
    {
        accumulationMaterial = particleAccumulationMaterial;
        outputRenderer = compositeOutputRenderer;
        legacyOutputRenderer = null;
        textureSize = accumulationTextureSize;
        threshold = densityThreshold;
        mergeStrength = overlapMergeStrength;
        edgeSoftness = compositeEdgeSoftness;
        minimumVisibleAlpha = fallbackVisibleAlpha;

        ValidateSettings();
        if (isActiveAndEnabled && captureCamera != null)
        {
            EnsureTargets();
            ApplyOutputProperties();
        }
    }

    private void OnEnable()
    {
        captureCamera = GetComponent<Camera>();
        cameraWasEnabled = captureCamera.enabled;
        originalTarget = captureCamera.targetTexture;
        captureCamera.enabled = false;
        captureCamera.clearFlags = CameraClearFlags.SolidColor;
        captureCamera.backgroundColor = Color.clear;

        if (legacyOutputRenderer != null && legacyOutputRenderer != outputRenderer)
        {
            legacyOutputWasEnabled = legacyOutputRenderer.enabled;
            legacyOutputRenderer.enabled = false;
        }

        CreateTargets();
        ApplyOutputProperties();
    }

    private void LateUpdate()
    {
        EnsureTargets();
        if (accumulationMaterial == null || outputRenderer == null
            || densityTexture == null || colorTexture == null
            || shapeTexture == null)
        {
            return;
        }

        commandBuffer ??= new CommandBuffer { name = "Liquid Metaball Accumulation" };
        commandBuffer.Clear();

        Matrix4x4 view = captureCamera.worldToCameraMatrix;
        Matrix4x4 projection = GL.GetGPUProjectionMatrix(captureCamera.projectionMatrix, true);

        commandBuffer.SetRenderTarget(densityTexture);
        commandBuffer.ClearRenderTarget(false, true, Color.clear);
        commandBuffer.SetRenderTarget(colorTexture);
        commandBuffer.ClearRenderTarget(false, true, Color.clear);
        commandBuffer.SetRenderTarget(shapeTexture);
        commandBuffer.ClearRenderTarget(false, true, Color.clear);
        commandBuffer.SetViewProjectionMatrices(view, projection);

        DrawParticles();
        Graphics.ExecuteCommandBuffer(commandBuffer);
    }

    private void DrawParticles()
    {
        HashSet<LiquidParticleData> particles = VesselLiquidTracker.ActiveParticles;
        int batchCount = 0;
        foreach (LiquidParticleData particle in particles)
        {
            if (particle == null || !particle.isActiveAndEnabled)
                continue;

            SpriteRenderer particleRenderer = particle.ParticleRenderer;
            if (particleRenderer == null || !particleRenderer.enabled
                || particleRenderer.sprite == null)
                continue;

            SuppressDirectRendering(particleRenderer);

            if (cachedSprite != particleRenderer.sprite)
            {
                FlushBatch(batchCount);
                batchCount = 0;
                CacheSpriteMesh(particleRenderer.sprite);
            }

            batchMatrices[batchCount] = particleRenderer.localToWorldMatrix;
            Color displayColor = particle.LogicalColor;
            displayColor.a = Mathf.Max(displayColor.a, minimumVisibleAlpha);
            batchColors[batchCount] = ConvertToShaderColor(displayColor);
            batchCount++;

            if (batchCount < MaxInstancesPerDraw)
                continue;

            FlushBatch(batchCount);
            batchCount = 0;
        }

        FlushBatch(batchCount);
    }

    private static Vector4 ConvertToShaderColor(Color color)
    {
        if (QualitySettings.activeColorSpace != ColorSpace.Linear)
            return color;

        float alpha = color.a;
        Color linearColor = color.linear;
        linearColor.a = alpha;
        return linearColor;
    }

    private void SuppressDirectRendering(SpriteRenderer particleRenderer)
    {
        if (particleRenderer.forceRenderingOff)
            return;

        particleRenderer.forceRenderingOff = true;
        suppressedParticleRenderers.Add(particleRenderer);
    }

    private void FlushBatch(int count)
    {
        if (count <= 0 || cachedSpriteMesh == null || cachedSprite == null)
            return;

        batchProperties ??= new MaterialPropertyBlock();
        batchProperties.Clear();
        batchProperties.SetTexture(MainTexId, cachedSprite.texture);
        batchProperties.SetVectorArray(ParticleColorId, batchColors);

        if (UseSinglePassMrt)
        {
            commandBuffer.SetRenderTarget(
                accumulationTargets,
                BuiltinRenderTextureType.None);
            commandBuffer.DrawMeshInstanced(
                cachedSpriteMesh,
                0,
                accumulationMaterial,
                MrtPass,
                batchMatrices,
                count,
                batchProperties);
        }
        else
        {
            commandBuffer.SetRenderTarget(densityTexture);
            commandBuffer.DrawMeshInstanced(
                cachedSpriteMesh,
                0,
                accumulationMaterial,
                DensityFallbackPass,
                batchMatrices,
                count,
                batchProperties);

            commandBuffer.SetRenderTarget(colorTexture);
            commandBuffer.DrawMeshInstanced(
                cachedSpriteMesh,
                0,
                accumulationMaterial,
                ColorFallbackPass,
                batchMatrices,
                count,
                batchProperties);
        }

        commandBuffer.SetRenderTarget(shapeTexture);
        commandBuffer.DrawMeshInstanced(
            cachedSpriteMesh,
            0,
            accumulationMaterial,
            MaximumCoveragePass,
            batchMatrices,
            count,
            batchProperties);
    }

    private void CacheSpriteMesh(Sprite sprite)
    {
        if (cachedSprite == sprite && cachedSpriteMesh != null)
            return;

        ReleaseSpriteMesh();
        cachedSprite = sprite;

        Vector2[] spriteVertices = sprite.vertices;
        Vector2[] spriteUvs = sprite.uv;
        ushort[] spriteTriangles = sprite.triangles;
        Vector3[] vertices = new Vector3[spriteVertices.Length];
        int[] triangles = new int[spriteTriangles.Length];

        for (int i = 0; i < spriteVertices.Length; i++)
            vertices[i] = spriteVertices[i];
        for (int i = 0; i < spriteTriangles.Length; i++)
            triangles[i] = spriteTriangles[i];

        cachedSpriteMesh = new Mesh { name = $"{sprite.name} Metaball Batch" };
        cachedSpriteMesh.vertices = vertices;
        cachedSpriteMesh.uv = spriteUvs;
        cachedSpriteMesh.triangles = triangles;
        cachedSpriteMesh.RecalculateBounds();
        cachedSpriteMesh.UploadMeshData(true);
    }

    private void CreateTargets()
    {
        ReleaseTargets();

        GetRequestedTargetSize(out int width, out int height);

        RenderTextureFormat densityFormat = SystemInfo.SupportsRenderTextureFormat(
            RenderTextureFormat.RHalf)
            ? RenderTextureFormat.RHalf
            : RenderTextureFormat.ARGBHalf;

        densityTexture = CreateTarget("LiquidDensity", width, height, densityFormat);
        shapeTexture = CreateTarget("LiquidMaximumCoverage", width, height, densityFormat);
        colorTexture = CreateTarget("LiquidPremultipliedColor", width, height,
            RenderTextureFormat.ARGBHalf);
        accumulationTargets[0] = new RenderTargetIdentifier(densityTexture);
        accumulationTargets[1] = new RenderTargetIdentifier(colorTexture);
        targetWidth = width;
        targetHeight = height;
    }

    private void EnsureTargets()
    {
        GetRequestedTargetSize(out int width, out int height);
        if (densityTexture != null && colorTexture != null
            && shapeTexture != null
            && targetWidth == width && targetHeight == height)
        {
            return;
        }

        CreateTargets();
        ApplyOutputProperties();
    }

    private void GetRequestedTargetSize(out int width, out int height)
    {
        int baseWidth = originalTarget != null ? originalTarget.width : textureSize.x;
        int baseHeight = originalTarget != null ? originalTarget.height : textureSize.y;
        int maxTextureSize = Mathf.Max(16, SystemInfo.maxTextureSize);
        width = Mathf.Clamp(baseWidth, 16, maxTextureSize);
        height = Mathf.Clamp(baseHeight, 16, maxTextureSize);
    }

    private static RenderTexture CreateTarget(
        string targetName,
        int width,
        int height,
        RenderTextureFormat format)
    {
        RenderTexture target = new RenderTexture(
            width,
            height,
            0,
            format,
            RenderTextureReadWrite.Linear)
        {
            name = targetName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        target.Create();
        return target;
    }

    private void ApplyOutputProperties()
    {
        if (outputRenderer == null)
            return;

        outputProperties ??= new MaterialPropertyBlock();
        outputRenderer.GetPropertyBlock(outputProperties);
        outputProperties.SetTexture(DensityTexId, densityTexture);
        outputProperties.SetTexture(ColorTexId, colorTexture);
        outputProperties.SetTexture(ShapeTexId, shapeTexture);
        outputProperties.SetFloat(ThresholdId, threshold);
        outputProperties.SetFloat(MergeStrengthId, mergeStrength);
        outputProperties.SetFloat(EdgeSoftnessId, edgeSoftness);
        outputProperties.SetFloat(DebugViewId, (float)debugView);
        outputRenderer.SetPropertyBlock(outputProperties);
    }

    private void OnValidate()
    {
        ValidateSettings();

        if (isActiveAndEnabled && captureCamera != null)
        {
            EnsureTargets();
            ApplyOutputProperties();
        }
    }

    private void ValidateSettings()
    {
        textureSize.x = Mathf.Max(16, textureSize.x);
        textureSize.y = Mathf.Max(16, textureSize.y);
        threshold = Mathf.Clamp01(threshold);
        mergeStrength = Mathf.Clamp01(mergeStrength);
        edgeSoftness = Mathf.Clamp(edgeSoftness, 0f, 0.25f);
        minimumVisibleAlpha = Mathf.Clamp01(minimumVisibleAlpha);
    }

    private void OnDisable()
    {
        if (captureCamera != null)
        {
            captureCamera.targetTexture = originalTarget;
            captureCamera.enabled = cameraWasEnabled;
        }

        if (legacyOutputRenderer != null && legacyOutputRenderer != outputRenderer)
            legacyOutputRenderer.enabled = legacyOutputWasEnabled;

        foreach (SpriteRenderer particleRenderer in suppressedParticleRenderers)
        {
            if (particleRenderer != null)
                particleRenderer.forceRenderingOff = false;
        }
        suppressedParticleRenderers.Clear();

        if (commandBuffer != null)
        {
            commandBuffer.Release();
            commandBuffer = null;
        }

        ReleaseTargets();
        ReleaseSpriteMesh();
    }

    private void ReleaseTargets()
    {
        ReleaseTarget(ref densityTexture);
        ReleaseTarget(ref colorTexture);
        ReleaseTarget(ref shapeTexture);
        accumulationTargets[0] = BuiltinRenderTextureType.None;
        accumulationTargets[1] = BuiltinRenderTextureType.None;
        targetWidth = 0;
        targetHeight = 0;
    }

    private static void ReleaseTarget(ref RenderTexture target)
    {
        if (target == null)
            return;

        target.Release();
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
        target = null;
    }

    private void ReleaseSpriteMesh()
    {
        if (cachedSpriteMesh == null)
            return;

        if (Application.isPlaying)
            Destroy(cachedSpriteMesh);
        else
            DestroyImmediate(cachedSpriteMesh);
        cachedSpriteMesh = null;
        cachedSprite = null;
    }
}
