using System.Collections.Generic;
using Slainte.Bartending;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class LiquidMetaballRenderer : MonoBehaviour
{
    private const int MaxInstancesPerDraw = 1023;
    private static readonly int DensityTexId = Shader.PropertyToID("_DensityTex");
    private static readonly int ColorTexId = Shader.PropertyToID("_ColorTex");
    private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ParticleColorId = Shader.PropertyToID("_ParticleColor");

    [SerializeField] private Material accumulationMaterial;
    [SerializeField] private Renderer outputRenderer;
    [SerializeField] private Vector2Int textureSize = new Vector2Int(240, 135);
    [SerializeField, Range(0f, 1f)] private float threshold = 0.3f;
    [SerializeField, Range(0f, 1f)] private float minimumVisibleAlpha = 0.05f;

    private Camera captureCamera;
    private RenderTexture originalTarget;
    private RenderTexture densityTexture;
    private RenderTexture colorTexture;
    private CommandBuffer commandBuffer;
    private MaterialPropertyBlock outputProperties;
    private MaterialPropertyBlock batchProperties;
    private readonly Matrix4x4[] batchMatrices = new Matrix4x4[MaxInstancesPerDraw];
    private readonly Vector4[] batchColors = new Vector4[MaxInstancesPerDraw];
    private Sprite cachedSprite;
    private Mesh cachedSpriteMesh;
    private bool cameraWasEnabled;

    private void OnEnable()
    {
        captureCamera = GetComponent<Camera>();
        cameraWasEnabled = captureCamera.enabled;
        originalTarget = captureCamera.targetTexture;
        captureCamera.enabled = false;
        captureCamera.clearFlags = CameraClearFlags.SolidColor;
        captureCamera.backgroundColor = Color.clear;

        CreateTargets();
        ApplyOutputProperties();
    }

    private void LateUpdate()
    {
        if (accumulationMaterial == null || outputRenderer == null
            || densityTexture == null || colorTexture == null)
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

            if (cachedSprite != particleRenderer.sprite)
            {
                FlushBatch(batchCount);
                batchCount = 0;
                CacheSpriteMesh(particleRenderer.sprite);
            }

            batchMatrices[batchCount] = particleRenderer.localToWorldMatrix;
            Color displayColor = particleRenderer.color;
            displayColor.a = Mathf.Max(displayColor.a, minimumVisibleAlpha);
            batchColors[batchCount] = displayColor;
            batchCount++;

            if (batchCount < MaxInstancesPerDraw)
                continue;

            FlushBatch(batchCount);
            batchCount = 0;
        }

        FlushBatch(batchCount);
    }

    private void FlushBatch(int count)
    {
        if (count <= 0 || cachedSpriteMesh == null || cachedSprite == null)
            return;

        batchProperties ??= new MaterialPropertyBlock();
        batchProperties.Clear();
        batchProperties.SetTexture(MainTexId, cachedSprite.texture);
        batchProperties.SetVectorArray(ParticleColorId, batchColors);

        commandBuffer.SetRenderTarget(densityTexture);
        commandBuffer.DrawMeshInstanced(
            cachedSpriteMesh,
            0,
            accumulationMaterial,
            0,
            batchMatrices,
            count,
            batchProperties);

        commandBuffer.SetRenderTarget(colorTexture);
        commandBuffer.DrawMeshInstanced(
            cachedSpriteMesh,
            0,
            accumulationMaterial,
            1,
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

        int width = originalTarget != null ? originalTarget.width : textureSize.x;
        int height = originalTarget != null ? originalTarget.height : textureSize.y;
        width = Mathf.Max(16, width);
        height = Mathf.Max(16, height);

        RenderTextureFormat densityFormat = SystemInfo.SupportsRenderTextureFormat(
            RenderTextureFormat.RHalf)
            ? RenderTextureFormat.RHalf
            : RenderTextureFormat.ARGBHalf;

        densityTexture = CreateTarget("LiquidDensity", width, height, densityFormat);
        colorTexture = CreateTarget("LiquidPremultipliedColor", width, height,
            RenderTextureFormat.ARGBHalf);
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
        outputProperties.SetFloat(ThresholdId, threshold);
        outputRenderer.SetPropertyBlock(outputProperties);
    }

    private void OnValidate()
    {
        textureSize.x = Mathf.Max(16, textureSize.x);
        textureSize.y = Mathf.Max(16, textureSize.y);
        threshold = Mathf.Clamp01(threshold);
        minimumVisibleAlpha = Mathf.Clamp01(minimumVisibleAlpha);

        if (isActiveAndEnabled)
            ApplyOutputProperties();
    }

    private void OnDisable()
    {
        if (captureCamera != null)
        {
            captureCamera.targetTexture = originalTarget;
            captureCamera.enabled = cameraWasEnabled;
        }

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
