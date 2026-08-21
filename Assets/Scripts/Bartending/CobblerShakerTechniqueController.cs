using System;
using UnityEngine;

namespace Slainte.Bartending
{
    public enum ShakerIceMode
    {
        IcedShake,
        DryShake
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(BeakerController))]
    public sealed class CobblerShakerTechniqueController : MonoBehaviour
    {
        [Header("Cobbler Closure")]
        [SerializeField] private bool integratedStrainer = true;
        [SerializeField, Range(0.5f, 1f)] private float strainerWidthRatio = 0.9f;
        [SerializeField, Min(0.02f)] private float strainerThickness = 0.08f;
        [SerializeField, Range(0.5f, 1.1f)] private float capWidthRatio = 1f;
        [SerializeField, Min(0.02f)] private float capThickness = 0.1f;
        [SerializeField, Min(0f)] private float strainerGuideEdgeRadius = 0.04f;

        [Header("Shake Requirement")]
        [SerializeField] private ShakerIceMode iceMode = ShakerIceMode.IcedShake;
        [SerializeField, Min(0.1f)] private float minimumSpeed = 3.5f;
        [SerializeField, Min(0.2f)] private float requiredShakeDuration = 1.5f;
        [SerializeField, Min(1)] private int minimumReversals = 3;
        [SerializeField, Min(0.1f)] private float maximumReversalInterval = 0.65f;
        [SerializeField, Range(-0.95f, -0.05f)] private float reversalDotThreshold = -0.25f;

        private BeakerController shaker;
        private Vector3 previousPosition;
        private Vector2 previousFastDirection;
        private int reversalCount;
        private float lastReversalTime;
        private float qualifiedShakeTime;
        private bool barriersConfigured;
        private IceOnlyVesselBarrier strainerBarrier;
        private EdgeCollider2D leftStrainerGuide;
        private EdgeCollider2D rightStrainerGuide;
        private BoxCollider2D capBarrier;
        private bool strainerAttached = true;
        private bool capAttached = true;
        private bool shakeComplete;
        private int contentSignature;
        private bool hasContentSignature;

        public bool HasIntegratedStrainer => integratedStrainer;
        public bool IsStrainerAttached => strainerAttached;
        public bool IsCapAttached => capAttached;
        public bool IsFullyClosed => strainerAttached && capAttached;
        public bool IsShakeComplete => shakeComplete;
        public float ShakeProgress => Mathf.Clamp01(
            qualifiedShakeTime / Mathf.Max(0.01f, requiredShakeDuration));
        public bool HasLiquid => GetTracker()?.ParticleCount > 0;
        public bool HasRequiredIce => iceMode == ShakerIceMode.DryShake
            || (GetTracker()?.IceCount ?? 0) > 0;

        public event Action<float> ShakeProgressChanged;
        public event Action ShakeCompleted;

        private void Awake()
        {
            shaker = GetComponent<BeakerController>();
            previousPosition = transform.position;
        }

        private void OnEnable()
        {
            previousPosition = transform.position;
            ResetGesture(false);
        }

        private void LateUpdate()
        {
            if (shaker == null)
                shaker = GetComponent<BeakerController>();

            if (!barriersConfigured && shaker != null && shaker.LiquidTracker != null)
                ConfigurePhysicalClosures();

            VesselLiquidTracker tracker = GetTracker();
            RefreshContentVersion(tracker);

            Vector3 currentPosition = transform.position;
            float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            Vector2 velocity = (currentPosition - previousPosition) / deltaTime;
            previousPosition = currentPosition;

            if (shakeComplete || !CanAdvanceShake(tracker))
            {
                ResetGesture(false);
                return;
            }

            if (velocity.magnitude < minimumSpeed)
            {
                if (Time.unscaledTime - lastReversalTime > maximumReversalInterval)
                    ResetGesture(true);
                return;
            }

            Vector2 direction = velocity.normalized;
            if (previousFastDirection.sqrMagnitude > 0.5f
                && Vector2.Dot(previousFastDirection, direction) <= reversalDotThreshold)
            {
                reversalCount++;
                lastReversalTime = Time.unscaledTime;
            }
            previousFastDirection = direction;

            if (reversalCount > 0
                && Time.unscaledTime - lastReversalTime <= maximumReversalInterval)
            {
                qualifiedShakeTime += deltaTime;
                ShakeProgressChanged?.Invoke(ShakeProgress);
            }

            if (reversalCount >= minimumReversals
                && qualifiedShakeTime >= requiredShakeDuration)
            {
                MarkContentsAsShaken();
            }
        }

        public void SetClosureState(bool hasStrainer, bool hasCap)
        {
            strainerAttached = integratedStrainer && hasStrainer;
            capAttached = strainerAttached && hasCap;
            if (!barriersConfigured && GetTracker() != null)
                ConfigurePhysicalClosures();
            RefreshPhysicalClosures();
            if (!IsFullyClosed)
                ResetGesture(true);
        }

        public void MarkContentsAsShaken()
        {
            VesselLiquidTracker tracker = GetTracker();
            if (!CanAdvanceShake(tracker))
                return;

            bool hasIce = tracker.IceCount > 0;
            foreach (LiquidParticleData particle in tracker.Particles)
            {
                if (particle == null)
                    continue;

                particle.RecordTechnique(CocktailTechnique.Shake);
                if (particle.payload != null)
                    particle.payload.wasShakenWithIce |= hasIce;
            }

            shakeComplete = true;
            qualifiedShakeTime = requiredShakeDuration;
            ShakeProgressChanged?.Invoke(1f);
            ShakeCompleted?.Invoke();
        }

        private bool CanAdvanceShake(VesselLiquidTracker tracker)
        {
            return shaker != null
                && shaker.IsPickedUp
                && IsFullyClosed
                && tracker != null
                && tracker.ParticleCount > 0
                && (iceMode == ShakerIceMode.DryShake || tracker.IceCount > 0);
        }

        private VesselLiquidTracker GetTracker()
        {
            return shaker != null
                ? shaker.LiquidTracker
                : GetComponent<VesselLiquidTracker>();
        }

        private void ConfigurePhysicalClosures()
        {
            if (shaker == null || shaker.LiquidTracker == null)
                return;

            strainerBarrier = GetComponentInChildren<IceOnlyVesselBarrier>(true);
            if (strainerBarrier == null)
            {
                GameObject barrierObject = new GameObject("__IntegratedStrainerBarrier");
                barrierObject.transform.SetParent(transform, false);
                barrierObject.layer = gameObject.layer;
                strainerBarrier = barrierObject.AddComponent<IceOnlyVesselBarrier>();
                barrierObject.AddComponent<BoxCollider2D>();
            }

            Transform capTransform = transform.Find("__CobblerCapBarrier");
            if (capTransform == null)
            {
                GameObject capObject = new GameObject("__CobblerCapBarrier");
                capObject.transform.SetParent(transform, false);
                capObject.layer = gameObject.layer;
                capTransform = capObject.transform;
            }
            capBarrier = capTransform.GetComponent<BoxCollider2D>();
            if (capBarrier == null)
                capBarrier = capTransform.gameObject.AddComponent<BoxCollider2D>();

            leftStrainerGuide = GetOrCreateStrainerGuide("__CobblerStrainerGuideLeft");
            rightStrainerGuide = GetOrCreateStrainerGuide("__CobblerStrainerGuideRight");

            float top = shaker.colliderYOffset + shaker.height * 0.5f;
            ConfigureBarrier(
                strainerBarrier.GetComponent<BoxCollider2D>(),
                top - strainerThickness * 0.5f,
                shaker.topWidth * strainerWidthRatio,
                strainerThickness);
            ConfigureBarrier(
                capBarrier,
                top + capThickness * 0.5f,
                shaker.topWidth * capWidthRatio,
                capThickness);
            ConfigureStrainerFlowGuides(top);

            barriersConfigured = true;
            RefreshPhysicalClosures();
        }

        private EdgeCollider2D GetOrCreateStrainerGuide(string objectName)
        {
            Transform guideTransform = transform.Find(objectName);
            if (guideTransform == null)
            {
                GameObject guideObject = new GameObject(objectName);
                guideObject.transform.SetParent(transform, false);
                guideObject.layer = gameObject.layer;
                guideTransform = guideObject.transform;
            }

            EdgeCollider2D guide = guideTransform.GetComponent<EdgeCollider2D>();
            if (guide == null)
                guide = guideTransform.gameObject.AddComponent<EdgeCollider2D>();

            guideTransform.localPosition = Vector3.zero;
            guideTransform.localRotation = Quaternion.identity;
            guideTransform.localScale = Vector3.one;
            guide.isTrigger = false;
            guide.edgeRadius = Mathf.Max(0f, strainerGuideEdgeRadius);
            return guide;
        }

        private void ConfigureStrainerFlowGuides(float shakerTop)
        {
            Vector2[] leftPoints;
            Vector2[] rightPoints;
            if (!TryBuildSpriteAlignedGuidePoints(
                    shakerTop,
                    out leftPoints,
                    out rightPoints))
            {
                BuildFallbackGuidePoints(shakerTop, out leftPoints, out rightPoints);
            }

            leftStrainerGuide.points = leftPoints;
            rightStrainerGuide.points = rightPoints;
        }

        private bool TryBuildSpriteAlignedGuidePoints(
            float shakerTop,
            out Vector2[] leftPoints,
            out Vector2[] rightPoints)
        {
            leftPoints = null;
            rightPoints = null;

            ShakerVisualLayer strainerLayer = null;
            ShakerVisualLayer[] visualLayers =
                GetComponentsInChildren<ShakerVisualLayer>(true);
            for (int i = 0; i < visualLayers.Length; i++)
            {
                if (visualLayers[i] != null
                    && visualLayers[i].Role == ShakerVisualRole.Strainer)
                {
                    strainerLayer = visualLayers[i];
                    break;
                }
            }

            SpriteRenderer renderer = strainerLayer != null
                ? strainerLayer.GetComponent<SpriteRenderer>()
                : null;
            if (renderer == null || renderer.sprite == null)
                return false;

            // Pixel anchors follow the opaque inner edge of the 310x590
            // cobbler_strainer sprite: dome shoulder, neck base, then outlet.
            Vector2[] leftPixels =
            {
                new Vector2(67f, 344f),
                new Vector2(91f, 369f),
                new Vector2(121f, 383f),
                new Vector2(121f, 433f)
            };
            Vector2[] rightPixels =
            {
                new Vector2(243f, 344f),
                new Vector2(219f, 369f),
                new Vector2(189f, 383f),
                new Vector2(189f, 433f)
            };

            leftPoints = BuildGuidePointsFromPixels(
                renderer,
                shakerTop,
                -shaker.topWidth * 0.5f,
                leftPixels);
            rightPoints = BuildGuidePointsFromPixels(
                renderer,
                shakerTop,
                shaker.topWidth * 0.5f,
                rightPixels);
            return leftPoints != null && rightPoints != null;
        }

        private Vector2[] BuildGuidePointsFromPixels(
            SpriteRenderer renderer,
            float shakerTop,
            float rimX,
            Vector2[] sourcePixels)
        {
            const float sourceWidth = 310f;
            const float sourceHeight = 590f;
            Bounds bounds = renderer.sprite.bounds;
            Vector2[] points = new Vector2[sourcePixels.Length + 1];
            points[0] = new Vector2(rimX, shakerTop);

            for (int i = 0; i < sourcePixels.Length; i++)
            {
                Vector2 pixel = sourcePixels[i];
                Vector3 spriteLocal = new Vector3(
                    Mathf.Lerp(bounds.min.x, bounds.max.x, pixel.x / sourceWidth),
                    Mathf.Lerp(bounds.min.y, bounds.max.y, pixel.y / sourceHeight),
                    0f);
                Vector3 world = renderer.transform.TransformPoint(spriteLocal);
                Vector3 shakerLocal = transform.InverseTransformPoint(world);
                points[i + 1] = new Vector2(shakerLocal.x, shakerLocal.y);
            }

            return points;
        }

        private void BuildFallbackGuidePoints(
            float shakerTop,
            out Vector2[] leftPoints,
            out Vector2[] rightPoints)
        {
            float halfRim = shaker.topWidth * 0.5f;
            float halfOutlet = Mathf.Max(0.16f, shaker.topWidth * 0.17f);
            float shoulderY = shakerTop + shaker.height * 0.16f;
            float outletY = shakerTop + shaker.height * 0.31f;

            leftPoints = new[]
            {
                new Vector2(-halfRim, shakerTop),
                new Vector2(-shaker.topWidth * 0.41f, shakerTop + shaker.height * 0.06f),
                new Vector2(-shaker.topWidth * 0.29f, shakerTop + shaker.height * 0.12f),
                new Vector2(-halfOutlet, shoulderY),
                new Vector2(-halfOutlet, outletY)
            };
            rightPoints = new[]
            {
                new Vector2(halfRim, shakerTop),
                new Vector2(shaker.topWidth * 0.41f, shakerTop + shaker.height * 0.06f),
                new Vector2(shaker.topWidth * 0.29f, shakerTop + shaker.height * 0.12f),
                new Vector2(halfOutlet, shoulderY),
                new Vector2(halfOutlet, outletY)
            };
        }

        private void ConfigureBarrier(
            BoxCollider2D collider,
            float localY,
            float width,
            float thickness)
        {
            Transform barrier = collider.transform;
            barrier.localPosition = new Vector3(0f, localY, 0f);
            barrier.localRotation = Quaternion.identity;
            barrier.localScale = Vector3.one;
            collider.isTrigger = false;
            collider.size = new Vector2(
                Mathf.Max(0.1f, width),
                Mathf.Max(0.02f, thickness));
        }

        private void RefreshPhysicalClosures()
        {
            if (!barriersConfigured)
                return;

            strainerBarrier.gameObject.SetActive(strainerAttached);
            leftStrainerGuide.gameObject.SetActive(strainerAttached);
            rightStrainerGuide.gameObject.SetActive(strainerAttached);
            capBarrier.gameObject.SetActive(capAttached);
            shaker.LiquidTracker?.RefreshCollisionGeometry();
        }

        private void RefreshContentVersion(VesselLiquidTracker tracker)
        {
            int signature = CalculateContentSignature(tracker);
            if (!hasContentSignature)
            {
                contentSignature = signature;
                hasContentSignature = true;
                return;
            }

            if (contentSignature == signature)
                return;

            contentSignature = signature;
            shakeComplete = false;
            ResetGesture(true);
        }

        private static int CalculateContentSignature(VesselLiquidTracker tracker)
        {
            if (tracker == null)
                return 0;

            unchecked
            {
                int sum = 17;
                int xor = 0;
                foreach (LiquidParticleData particle in tracker.Particles)
                {
                    if (particle == null)
                        continue;
                    int volume = particle.payload != null
                        ? Mathf.RoundToInt(particle.payload.TotalVolumeMl * 10f)
                        : 0;
                    int value = particle.GetInstanceID() * 397 ^ volume;
                    sum += value;
                    xor ^= value;
                }

                foreach (IceCubeController ice in tracker.IceCubes)
                {
                    if (ice == null)
                        continue;
                    int value = ice.GetInstanceID() * 31;
                    sum += value;
                    xor ^= value;
                }

                return sum * 397 ^ xor;
            }
        }

        private void ResetGesture(bool notify)
        {
            previousFastDirection = Vector2.zero;
            reversalCount = 0;
            lastReversalTime = Time.unscaledTime;
            if (!shakeComplete)
                qualifiedShakeTime = 0f;
            if (notify)
                ShakeProgressChanged?.Invoke(ShakeProgress);
        }
    }
}
