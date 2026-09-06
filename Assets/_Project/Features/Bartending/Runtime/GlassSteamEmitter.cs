using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    public sealed class GlassSteamEmitter : MonoBehaviour
    {
        private const float SampleInterval = 0.1f;
        private const float MinimumParticleVolumeMl = 0.001f;
        private const float SurfaceBandWorldUnits = 0.18f;
        private const float FullEmissionTemperatureC = 80f;

        [Header("품질 검증")]
        [SerializeField] private bool useDebugTemperature;
        [SerializeField] private float debugTemperatureC = 80f;

        private readonly List<LiquidParticleData> hotSurfaceParticles = new();

        private VesselLiquidTracker tracker;
        private ParticleSystem steam;
        private ParticleSystemRenderer steamRenderer;
        private Material runtimeMaterial;
        private Sprite smokeSprite;
        private Sprite runtimeSmokeSprite;
        private Texture2D runtimeSmokeTexture;
        private float startTemperatureC = 55f;
        private float stopTemperatureC = 48f;
        private float emissionRate = 5f;
        private float emissionHalfWidth = 0.5f;
        private float emissionStrength;
        private float nextSampleTime;
        private float lastSampleTime;
        private float emissionAccumulator;
        private bool isEmitting;
        private bool gpuHotSurfaceAvailable;
        private Vector2 gpuHotSurfaceWorldPosition;
        private int sourceCursor;

        public void Initialize(
            VesselLiquidTracker liquidTracker,
            float localTopY,
            float emissionWidth,
            float startTemperature,
            float stopTemperature,
            float particlesPerSecond)
        {
            tracker = liquidTracker;
            startTemperatureC = startTemperature;
            stopTemperatureC = Mathf.Min(stopTemperature, startTemperature - 0.1f);
            emissionRate = Mathf.Max(0f, particlesPerSecond);
            emissionHalfWidth = Mathf.Max(0.05f, emissionWidth * 0.5f);

            EnsureParticleSystem(localTopY);
            SetEmission(false);
        }

        private void Update()
        {
            if (tracker == null || steam == null || Time.unscaledTime < nextSampleTime)
                return;

            float now = Time.unscaledTime;
            float elapsed = lastSampleTime > 0f
                ? Mathf.Clamp(now - lastSampleTime, 0f, 0.5f)
                : SampleInterval;
            lastSampleTime = now;
            nextSampleTime = now + SampleInterval;

            RefreshHotSurfaceParticles();
            if (hotSurfaceParticles.Count == 0 && !gpuHotSurfaceAvailable)
            {
                SetEmission(false);
                return;
            }

            SetEmission(true);
            emissionAccumulator += emissionRate * emissionStrength * elapsed;
            int emitCount = Mathf.Min(8, Mathf.FloorToInt(emissionAccumulator));
            if (emitCount <= 0)
                return;

            emissionAccumulator -= emitCount;
            for (int i = 0; i < emitCount; i++)
                EmitFromNextHotParticle();
        }

        [ContextMenu("품질 검증/입자를 뜨겁게 간주")]
        private void EnableHotSteamForQa()
        {
            useDebugTemperature = true;
            debugTemperatureC = Mathf.Max(debugTemperatureC, startTemperatureC + 10f);
            nextSampleTime = 0f;
        }

        [ContextMenu("품질 검증/실제 액체 온도 사용")]
        private void DisableTemperatureOverride()
        {
            useDebugTemperature = false;
            nextSampleTime = 0f;
        }

        private void RefreshHotSurfaceParticles()
        {
            hotSurfaceParticles.Clear();
            emissionStrength = 0f;
            gpuHotSurfaceAvailable = false;

            GpuLiquidSystem gpu = GpuLiquidSystem.Instance;
            if (gpu != null
                && gpu.IsOperational
                && gpu.TryGetSnapshot(tracker, out GpuLiquidVesselSnapshot snapshot))
            {
                float gpuThreshold = isEmitting ? stopTemperatureC : startTemperatureC;
                float temperatureC = useDebugTemperature
                    ? debugTemperatureC
                    : snapshot.SurfaceTemperatureC;
                if (snapshot.HasSurfaceSample && temperatureC >= gpuThreshold)
                {
                    gpuHotSurfaceAvailable = true;
                    gpuHotSurfaceWorldPosition = snapshot.SurfaceWorldPosition;
                    emissionStrength = EvaluateEmissionStrength(temperatureC);
                }
                return;
            }

            IReadOnlyCollection<LiquidParticleData> particles = tracker.Particles;
            float highestY = float.NegativeInfinity;

            foreach (LiquidParticleData particle in particles)
            {
                if (!IsValidLiquidParticle(particle))
                    continue;

                highestY = Mathf.Max(highestY, particle.transform.position.y);
            }

            if (float.IsNegativeInfinity(highestY))
                return;

            float minimumSurfaceY = highestY - SurfaceBandWorldUnits;
            float threshold = isEmitting ? stopTemperatureC : startTemperatureC;
            float weightedStrength = 0f;
            float hotSurfaceVolumeMl = 0f;
            foreach (LiquidParticleData particle in particles)
            {
                if (!IsValidLiquidParticle(particle)
                    || particle.transform.position.y < minimumSurfaceY
                    || GetTemperatureC(particle) < threshold)
                    continue;

                hotSurfaceParticles.Add(particle);
                float volumeMl = particle.payload.TotalVolumeMl;
                weightedStrength += EvaluateEmissionStrength(GetTemperatureC(particle)) * volumeMl;
                hotSurfaceVolumeMl += volumeMl;
            }

            if (hotSurfaceParticles.Count > 0)
            {
                emissionStrength = hotSurfaceVolumeMl > 0f
                    ? weightedStrength / hotSurfaceVolumeMl
                    : 0f;
                ConfigureSmokeSprite(hotSurfaceParticles[0]);
            }
        }

        private static bool IsValidLiquidParticle(LiquidParticleData particle)
        {
            return particle != null
                && particle.payload != null
                && particle.payload.TotalVolumeMl > MinimumParticleVolumeMl;
        }

        private float GetTemperatureC(LiquidParticleData particle)
        {
            return useDebugTemperature
                ? debugTemperatureC
                : particle.payload.temperatureC;
        }

        private float EvaluateEmissionStrength(float temperatureC)
        {
            float fullTemperatureC = Mathf.Max(
                FullEmissionTemperatureC,
                startTemperatureC + 0.1f);
            return Mathf.InverseLerp(stopTemperatureC, fullTemperatureC, temperatureC);
        }

        private void EmitFromNextHotParticle()
        {
            Vector3 sourceWorldPosition;
            if (gpuHotSurfaceAvailable)
            {
                sourceWorldPosition = gpuHotSurfaceWorldPosition;
            }
            else
            {
                if (hotSurfaceParticles.Count == 0)
                    return;

                sourceCursor %= hotSurfaceParticles.Count;
                LiquidParticleData source = hotSurfaceParticles[sourceCursor];
                sourceCursor = (sourceCursor + 1) % hotSurfaceParticles.Count;
                if (source == null)
                    return;
                sourceWorldPosition = source.transform.position;
            }

            Vector3 localPosition = transform.InverseTransformPoint(sourceWorldPosition);
            float horizontalJitter = emissionHalfWidth * 0.06f;
            localPosition.x = Mathf.Clamp(
                localPosition.x + Random.Range(-horizontalJitter, horizontalJitter),
                -emissionHalfWidth,
                emissionHalfWidth);

            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
            {
                position = transform.TransformPoint(localPosition) + Vector3.up * 0.03f,
                applyShapeToPosition = false
            };
            steam.Emit(emitParams, 1);
        }

        private void ConfigureSmokeSprite(LiquidParticleData source)
        {
            SpriteRenderer sourceRenderer = source != null
                ? source.GetComponent<SpriteRenderer>()
                : null;
            Sprite sprite = sourceRenderer != null ? sourceRenderer.sprite : null;
            if (sprite == null || sprite == smokeSprite)
                return;

            smokeSprite = sprite;
            ParticleSystem.TextureSheetAnimationModule animation = steam.textureSheetAnimation;
            animation.enabled = true;
            animation.mode = ParticleSystemAnimationMode.Sprites;
            for (int i = animation.spriteCount - 1; i >= 0; i--)
                animation.RemoveSprite(i);
            animation.AddSprite(smokeSprite);

            if (runtimeMaterial != null)
            {
                runtimeMaterial.mainTexture = smokeSprite.texture;
                if (runtimeMaterial.HasProperty("_BaseMap"))
                    runtimeMaterial.SetTexture("_BaseMap", smokeSprite.texture);
                if (runtimeMaterial.HasProperty("_MainTex"))
                    runtimeMaterial.SetTexture("_MainTex", smokeSprite.texture);
            }

            if (steamRenderer != null && sourceRenderer != null)
            {
                steamRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
                steamRenderer.sortingOrder = sourceRenderer.sortingOrder + 5;
            }
        }

        private void EnsureParticleSystem(float localTopY)
        {
            if (steam != null)
                return;

            GameObject child = new GameObject("연기 입자");
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            child.transform.localPosition = new Vector3(0f, localTopY, -0.05f);

            steam = child.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = steam.main;
            main.loop = true;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.98f, 1f, 0.2f));
            main.maxParticles = 24;

            ParticleSystem.EmissionModule emission = steam.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = steam.shape;
            shape.enabled = false;

            ParticleSystem.VelocityOverLifetimeModule velocity = steam.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            AnimationCurve puffSizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.65f),
                new Keyframe(0.3f, 1f),
                new Keyframe(1f, 1.2f));
            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = steam.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, puffSizeCurve);

            ParticleSystem.RotationOverLifetimeModule rotationOverLifetime = steam.rotationOverLifetime;
            rotationOverLifetime.enabled = true;
            rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);

            ParticleSystem.NoiseModule noise = steam.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = 0.045f;
            noise.frequency = 0.65f;
            noise.scrollSpeed = 0.24f;
            noise.damping = true;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.92f, 0.96f, 1f), 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.22f, 0.14f),
                    new GradientAlphaKey(0.16f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = steam.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            steamRenderer = steam.GetComponent<ParticleSystemRenderer>();
            steamRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                runtimeMaterial = new Material(shader)
                {
                    name = "실행 중 생성된 연기 재질",
                    hideFlags = HideFlags.DontSave
                };
                steamRenderer.sharedMaterial = runtimeMaterial;
            }

            EnsureFallbackSmokeSprite();
        }

        private void EnsureFallbackSmokeSprite()
        {
            if (steam == null || smokeSprite != null)
                return;

            const int size = 32;
            runtimeSmokeTexture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "실행 중 생성된 연기 텍스처",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new Vector2(
                        (x + 0.5f) / size * 2f - 1f,
                        (y + 0.5f) / size * 2f - 1f);
                    float alpha = Mathf.SmoothStep(1f, 0f, point.sqrMagnitude);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            runtimeSmokeTexture.SetPixels(pixels);
            runtimeSmokeTexture.Apply(false, true);
            runtimeSmokeSprite = Sprite.Create(
                runtimeSmokeTexture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            runtimeSmokeSprite.name = "실행 중 생성된 연기 스프라이트";
            smokeSprite = runtimeSmokeSprite;

            ParticleSystem.TextureSheetAnimationModule animation = steam.textureSheetAnimation;
            animation.enabled = true;
            animation.mode = ParticleSystemAnimationMode.Sprites;
            animation.AddSprite(smokeSprite);
            if (runtimeMaterial != null)
            {
                runtimeMaterial.mainTexture = runtimeSmokeTexture;
                if (runtimeMaterial.HasProperty("_BaseMap"))
                    runtimeMaterial.SetTexture("_BaseMap", runtimeSmokeTexture);
                if (runtimeMaterial.HasProperty("_MainTex"))
                    runtimeMaterial.SetTexture("_MainTex", runtimeSmokeTexture);
            }
        }

        private void SetEmission(bool enabled)
        {
            if (steam == null)
                return;

            if (enabled)
            {
                isEmitting = true;
                if (!steam.isPlaying)
                    steam.Play(true);
            }
            else
            {
                if (!isEmitting)
                    return;

                isEmitting = false;
                emissionStrength = 0f;
                emissionAccumulator = 0f;
                steam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
            if (runtimeSmokeSprite != null)
                Destroy(runtimeSmokeSprite);
            if (runtimeSmokeTexture != null)
                Destroy(runtimeSmokeTexture);
        }
    }
}
