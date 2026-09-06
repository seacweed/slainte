using System.Collections;
using UnityEngine;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    public sealed class LiquidStressHarness : MonoBehaviour
    {
        private const int TimingSampleCapacity = 600;

        [Header("GPU Liquid Stress Test")]
        [SerializeField] private bool drawPanel = true;
        [SerializeField, Min(100)] private int defaultParticleCount = 1500;
        [SerializeField, Min(0.1f)] private float shakeAmplitude = 0.9f;
        [SerializeField, Min(0.1f)] private float shakeFrequencyHz = 2.7f;
        [SerializeField, Range(0f, 90f)] private float shakeAngle = 28f;

        private readonly float[] frameTimesMs = new float[TimingSampleCapacity];
        private readonly float[] sortedFrameTimesMs = new float[TimingSampleCapacity];

        private BartendingSandboxBootstrap bootstrap;
        private BartendingSessionInstance session;
        private BeakerController shaker;
        private VesselLiquidTracker tracker;
        private Rigidbody2D shakerBody;
        private Coroutine spawnRoutine;
        private Coroutine pourRoutine;
        private Vector2 shakerOrigin;
        private float shakerOriginAngle;
        private float shakeStartTime;
        private int timingSampleCount;
        private int timingWriteIndex;
        private int requestedParticleCount;
        private int emittedParticleCount;
        private ItemDef firstTestIngredient;
        private ItemDef secondTestIngredient;
        private bool autoShake;
        private string status = "F6/F7/F8: 1000/2000/3000 particles";

        public float P95FrameTimeMs { get; private set; }
        public float P99FrameTimeMs { get; private set; }
        public int RequestedParticleCount => requestedParticleCount;
        public int EmittedParticleCount => emittedParticleCount;
        public bool IsAutoShaking => autoShake;
        public VesselLiquidTracker TargetTracker => tracker;
        public ItemDef FirstTestIngredient => firstTestIngredient;
        public ItemDef SecondTestIngredient => secondTestIngredient;

        public void Initialize(
            BartendingSandboxBootstrap owner,
            BartendingSessionInstance currentSession)
        {
            StopStressOperations();
            bootstrap = owner;
            session = currentSession;
            shaker = session?.CobblerShaker as BeakerController;
            tracker = shaker != null ? shaker.LiquidTracker : null;
            shakerBody = shaker != null ? shaker.GetComponent<Rigidbody2D>() : null;
            if (shaker != null)
            {
                shakerOrigin = shaker.transform.position;
                shakerOriginAngle = shaker.transform.eulerAngles.z;
                CobblerShakerTechniqueController technique =
                    shaker.GetComponent<CobblerShakerTechniqueController>();
                technique?.SetClosureState(true, true);
            }

            ResetTimingSamples();
            status = session?.GpuLiquidSystem != null
                ? "GPU ready | F3: pour | F6/F7/F8: 1000/2000/3000 | F9: shake | F10: reset"
                : "GPU backend unavailable; stress test disabled";
        }

        private void Update()
        {
            RecordFrameTime();
            if (Input.GetKeyDown(KeyCode.F3))
                StartVisualPourTest();
            if (Input.GetKeyDown(KeyCode.F5))
                LoadAndShake(defaultParticleCount);
            if (Input.GetKeyDown(KeyCode.F6))
                LoadAndShake(1000);
            if (Input.GetKeyDown(KeyCode.F7))
                LoadAndShake(2000);
            if (Input.GetKeyDown(KeyCode.F8))
                LoadAndShake(3000);
            if (Input.GetKeyDown(KeyCode.F9))
                SetAutoShake(!autoShake);
            if (Input.GetKeyDown(KeyCode.F10))
                ResetParticles();
        }

        private void FixedUpdate()
        {
            if (!autoShake || shaker == null)
                return;

            float elapsed = Time.unscaledTime - shakeStartTime;
            float phase = elapsed * shakeFrequencyHz * Mathf.PI * 2f;
            Vector2 target = shakerOrigin + new Vector2(
                Mathf.Sin(phase) * shakeAmplitude,
                Mathf.Sin(phase * 0.53f) * shakeAmplitude * 0.22f);
            float angle = shakerOriginAngle + Mathf.Sin(phase * 0.81f) * shakeAngle;
            if (shakerBody != null)
            {
                shakerBody.MovePosition(target);
                shakerBody.MoveRotation(angle);
            }
            else
            {
                shaker.transform.SetPositionAndRotation(
                    new Vector3(target.x, target.y, shaker.transform.position.z),
                    Quaternion.Euler(0f, 0f, angle));
            }
        }

        public void LoadAndShake(int particleCount)
        {
            LoadParticles(particleCount, true);
        }

        public void StartVisualPourTest()
        {
            if (pourRoutine != null)
                StopCoroutine(pourRoutine);
            pourRoutine = StartCoroutine(VisualPourTestRoutine());
        }

        private IEnumerator VisualPourTestRoutine()
        {
            SetAutoShake(false);
            session?.LiquidBackend?.ResetSimulation();

            BottleController bottle = null;
            if (bootstrap?.TestBottles != null)
            {
                for (int i = 0; i < bootstrap.TestBottles.Count; i++)
                {
                    BottleController candidate = bootstrap.TestBottles[i];
                    if (candidate?.BottleData != null
                        && candidate.BottleData.liquidType == BottleLiquidType.Vodka)
                    {
                        bottle = candidate;
                        break;
                    }
                }

                if (bottle == null && bootstrap.TestBottles.Count > 0)
                    bottle = bootstrap.TestBottles[0];
            }
            BeakerController target = session?.Beaker as BeakerController;
            if (bottle == null || target == null)
            {
                status = "Bottle or beaker is unavailable for the pour test";
                pourRoutine = null;
                yield break;
            }

            Vector2 originalMouthPosition = bottle.GetStressTestMouthPosition();
            float originalAngle = bottle.transform.eulerAngles.z;
            Vector2 beakerMouth = target.transform.TransformPoint(new Vector3(
                target.topWidth * 0.18f,
                target.colliderYOffset + target.height * 0.5f + 0.38f,
                0f));
            bottle.SetStressTestPourPose(beakerMouth, 112f);
            requestedParticleCount = 0;
            emittedParticleCount = 0;
            status = $"Visual pour active: {bottle.BottleData.displayName} into the beaker";

            yield return new WaitForSeconds(2.5f);

            bottle.SetStressTestPourPose(originalMouthPosition, originalAngle);
            GpuLiquidSystem gpu = session?.GpuLiquidSystem;
            emittedParticleCount = gpu != null ? gpu.ActiveParticleCount : 0;
            status = $"Visual pour complete: {emittedParticleCount} particles emitted";
            pourRoutine = null;
        }

        public void LoadParticles(int particleCount, bool beginShaking)
        {
            if (spawnRoutine != null)
                StopCoroutine(spawnRoutine);
            spawnRoutine = StartCoroutine(LoadParticlesRoutine(particleCount, beginShaking));
        }

        private IEnumerator LoadParticlesRoutine(int particleCount, bool beginShaking)
        {
            SetAutoShake(false);
            GpuLiquidSystem gpu = session?.GpuLiquidSystem;
            if (gpu == null || !gpu.IsOperational || shaker == null)
            {
                status = "GPU backend or shaker is unavailable";
                spawnRoutine = null;
                yield break;
            }

            gpu.ResetSimulation();
            yield return new WaitForFixedUpdate();

            tracker = shaker.LiquidTracker;
            shakerBody = shaker.GetComponent<Rigidbody2D>();

            requestedParticleCount = Mathf.Clamp(particleCount, 1, gpu.ParticleCapacity);
            emittedParticleCount = 0;
            ResetTimingSamples();

            ItemDef first = GetTestIngredient(0);
            ItemDef second = GetTestIngredient(1) ?? first;
            if (first == null)
            {
                status = "No bottle ingredient is available for the stress test";
                spawnRoutine = null;
                yield break;
            }

            firstTestIngredient = first;
            secondTestIngredient = second;

            float width = Mathf.Max(0.2f, Mathf.Min(shaker.bottomWidth, shaker.topWidth) * 0.72f);
            float height = Mathf.Max(0.2f, shaker.height * 0.68f);
            float particleVolumeMl = gpu.DefaultParticleVolumeMl;
            for (int i = 0; i < requestedParticleCount; i++)
            {
                float x = Halton(i + 1, 2) - 0.5f;
                float y = Halton(i + 1, 3) - 0.5f;
                Vector3 world = shaker.transform.TransformPoint(new Vector3(
                    x * width,
                    y * height - shaker.height * 0.05f,
                    0f));
                Vector2 initialVelocity = new Vector2(
                    Mathf.Sin(i * 1.618f),
                    Mathf.Cos(i * 0.754f)) * 1.4f;
                ItemDef ingredient = (i & 1) == 0 ? first : second;
                if (gpu.TryEmitIntoVessel(
                        world,
                        initialVelocity,
                        ingredient,
                        particleVolumeMl,
                        tracker))
                    emittedParticleCount++;
            }

            status = beginShaking
                ? $"Loaded {emittedParticleCount}/{requestedParticleCount}; auto shake active"
                : $"Loaded {emittedParticleCount}/{requestedParticleCount}; waiting before shake";
            SetAutoShake(beginShaking);
            spawnRoutine = null;
        }

        public void SetAutoShake(bool enabled)
        {
            if (shaker == null)
                enabled = false;
            if (autoShake == enabled)
                return;

            autoShake = enabled;
            if (enabled)
            {
                shakerOrigin = shaker.transform.position;
                shakerOriginAngle = shaker.transform.eulerAngles.z;
                shakeStartTime = Time.unscaledTime;
                status = $"Loaded {emittedParticleCount}/{requestedParticleCount}; auto shake active";
            }
            else
            {
                RestoreShakerPose();
                if (emittedParticleCount > 0)
                    status = $"Loaded {emittedParticleCount}/{requestedParticleCount}; auto shake paused";
            }
        }

        public void ResetParticles()
        {
            SetAutoShake(false);
            session?.LiquidBackend?.ResetSimulation();
            requestedParticleCount = 0;
            emittedParticleCount = 0;
            firstTestIngredient = null;
            secondTestIngredient = null;
            ResetTimingSamples();
            status = "Liquid simulation reset";
        }

        public string BuildReport()
        {
            GpuLiquidSystem gpu = session?.GpuLiquidSystem;
            string particleText = gpu != null
                ? $"{gpu.ActiveParticleCount}/{gpu.ParticleCapacity}"
                : "legacy";
            string uniformity = "snapshot pending";
            if (gpu != null
                && tracker != null
                && gpu.TryGetSnapshot(tracker, out GpuLiquidVesselSnapshot snapshot))
            {
                uniformity = $"max deviation {snapshot.MaximumCompositionDeviation:0.000}, outliers {snapshot.OutOfToleranceParticleCount}";
            }

            return $"GPU Liquid Stress | particles {particleText} | p95 {P95FrameTimeMs:0.00} ms | p99 {P99FrameTimeMs:0.00} ms\n{uniformity}\n{status}";
        }

        private ItemDef GetTestIngredient(int index)
        {
            if (bootstrap?.TestBottles != null
                && index >= 0
                && index < bootstrap.TestBottles.Count)
            {
                return bootstrap.TestBottles[index]?.BottleData;
            }

            return null;
        }

        private static float Halton(int index, int radix)
        {
            float result = 0f;
            float fraction = 1f / radix;
            while (index > 0)
            {
                result += fraction * (index % radix);
                index /= radix;
                fraction /= radix;
            }
            return result;
        }

        private void RecordFrameTime()
        {
            frameTimesMs[timingWriteIndex] = Time.unscaledDeltaTime * 1000f;
            timingWriteIndex = (timingWriteIndex + 1) % TimingSampleCapacity;
            timingSampleCount = Mathf.Min(timingSampleCount + 1, TimingSampleCapacity);

            if (timingSampleCount < 30 || Time.frameCount % 15 != 0)
                return;

            System.Array.Copy(frameTimesMs, sortedFrameTimesMs, timingSampleCount);
            System.Array.Sort(sortedFrameTimesMs, 0, timingSampleCount);
            P95FrameTimeMs = Percentile(sortedFrameTimesMs, timingSampleCount, 0.95f);
            P99FrameTimeMs = Percentile(sortedFrameTimesMs, timingSampleCount, 0.99f);
        }

        private static float Percentile(float[] sorted, int count, float percentile)
        {
            int index = Mathf.Clamp(
                Mathf.CeilToInt(count * percentile) - 1,
                0,
                count - 1);
            return sorted[index];
        }

        private void ResetTimingSamples()
        {
            timingSampleCount = 0;
            timingWriteIndex = 0;
            P95FrameTimeMs = 0f;
            P99FrameTimeMs = 0f;
            System.Array.Clear(frameTimesMs, 0, frameTimesMs.Length);
        }

        private void RestoreShakerPose()
        {
            if (shaker == null)
                return;
            if (shakerBody != null)
            {
                shakerBody.position = shakerOrigin;
                shakerBody.rotation = shakerOriginAngle;
                shakerBody.linearVelocity = Vector2.zero;
                shakerBody.angularVelocity = 0f;
            }
            shaker.transform.SetPositionAndRotation(
                new Vector3(shakerOrigin.x, shakerOrigin.y, shaker.transform.position.z),
                Quaternion.Euler(0f, 0f, shakerOriginAngle));
        }

        private void StopStressOperations()
        {
            if (pourRoutine != null)
            {
                StopCoroutine(pourRoutine);
                pourRoutine = null;
            }
            if (spawnRoutine != null)
            {
                StopCoroutine(spawnRoutine);
                spawnRoutine = null;
            }
            if (shaker != null)
                SetAutoShake(false);
            else
                autoShake = false;
        }

        private void OnDisable()
        {
            StopStressOperations();
        }

        private void OnGUI()
        {
            if (!drawPanel || !Application.isPlaying)
                return;
            GUI.Box(new Rect(12f, 170f, 610f, 78f), BuildReport());
        }
    }
}
