using System.Collections.Generic;
using System.Text;
using Slainte.Content;
using UnityEngine;

namespace Slainte.Bartending
{
    // VesselLiquidTracker.BuildComposition()이 특정 순간 용기 안에 있는 입자들을 합산해 만드는 스냅샷.
    // CocktailEvaluator/CocktailOrderEvaluator가 이 데이터만 보고 레시피 판정을 한다.
    public sealed class CocktailComposition
    {
        private readonly Dictionary<ItemDef, float> volumes = new();
        private float thermalVolumeMl;
        private float weightedTemperature;
        private LiquidPayload finalColorPayload;
        private Color cachedFinalColor;
        private bool finalColorDirty = true;
        private CocktailTechnique explicitTechniques;
        private bool explicitShakenWithIce;
        private float stirAttemptedVolumeMl;
        private float stirredVolumeMl;
        private float shakenVolumeMl;
        private float shakenWithIceVolumeMl;

        public IReadOnlyDictionary<ItemDef, float> Volumes => volumes;
        public float TotalVolumeMl { get; private set; }
        // 입자 부피로 가중 평균한 온도. 아직 샘플이 없으면(빈 용기) 상온(20C)으로 취급.
        public float AverageTemperatureC => thermalVolumeMl > 0f
            ? weightedTemperature / thermalVolumeMl
            : 20f;
        public string GlassId { get; private set; } = string.Empty;
        public bool HasIce { get; private set; }
        public int IceCount { get; private set; }
        public bool StirAttempted => (explicitTechniques & CocktailTechnique.Stir) != 0
            || stirAttemptedVolumeMl > 0.0001f;
        // 얼음과 함께 흔든 게 "완료"로 인정되려면 현재 총량 전체가 그 상태를 거쳐야 한다(CoversAllContents 참고).
        public bool WasShakenWithIce => explicitShakenWithIce
            || CoversAllContents(shakenWithIceVolumeMl);
        // 입자 단위로 누적된 부피(stirredVolumeMl 등)만으로는 "완료"를 보장할 수 없으므로,
        // 여기서는 관측된 적 있는 기법만 표시하고 완료 여부 판정은 GetEffectiveTechniques()가 담당한다.
        public CocktailTechnique Techniques
        {
            get
            {
                CocktailTechnique techniques = explicitTechniques;
                if (stirredVolumeMl > 0.0001f)
                    techniques |= CocktailTechnique.Stir;
                if (shakenVolumeMl > 0.0001f)
                    techniques |= CocktailTechnique.Shake;
                return techniques;
            }
        }

        public void Add(ItemDef item, float volumeMl)
        {
            if (item == null || volumeMl <= 0f)
                return;

            if (!volumes.ContainsKey(item))
                volumes.Add(item, 0f);

            volumes[item] += volumeMl;
            TotalVolumeMl += volumeMl;
            finalColorDirty = true;
        }

        public float GetVolume(ItemDef item)
        {
            return item != null && volumes.TryGetValue(item, out float volumeMl)
                ? volumeMl
                : 0f;
        }

        public void AddThermalSample(float volumeMl, float temperatureC)
        {
            if (volumeMl <= 0f)
                return;

            thermalVolumeMl += volumeMl;
            weightedTemperature += temperatureC * volumeMl;
        }

        public void RecordTechnique(CocktailTechnique technique)
        {
            explicitTechniques |= technique;
        }

        public void RecordShakenWithIce(bool value)
        {
            explicitShakenWithIce |= value;
        }

        public void RecordParticleTechniqueState(
            float volumeMl,
            CocktailTechnique techniques,
            bool stirAttempted,
            bool wasShakenWithIce)
        {
            if (volumeMl <= 0f)
                return;

            if (stirAttempted)
                stirAttemptedVolumeMl += volumeMl;
            if ((techniques & CocktailTechnique.Stir) != 0)
                stirredVolumeMl += volumeMl;
            if ((techniques & CocktailTechnique.Shake) != 0)
                shakenVolumeMl += volumeMl;
            if (wasShakenWithIce)
                shakenWithIceVolumeMl += volumeMl;
        }

        public void SetServingStyle(string glassId, bool hasIce)
        {
            SetServingStyle(glassId, hasIce ? 1 : 0);
        }

        public void SetServingStyle(string glassId, int iceCount)
        {
            GlassId = glassId?.Trim() ?? string.Empty;
            IceCount = Mathf.Max(0, iceCount);
            HasIce = IceCount > 0;
        }

        // 기법이 "완료"로 인정되는 기준: 해당 기법을 거친 부피가 현재 총 부피 전체를 덮어야 한다.
        // 예를 들어 젓다가 중간에 새 재료를 더 부으면, 새로 들어온 부피만큼은 아직 저어지지 않았으므로
        // 완료로 치지 않는다(CoversAllContents가 이 "전량 커버" 조건을 검사).
        public CocktailTechnique GetEffectiveTechniques()
        {
            CocktailTechnique completed = explicitTechniques;
            if (CoversAllContents(stirredVolumeMl))
                completed |= CocktailTechnique.Stir;
            if (CoversAllContents(shakenVolumeMl))
                completed |= CocktailTechnique.Shake;
            return completed == CocktailTechnique.None ? CocktailTechnique.Build : completed;
        }

        // 레시피가 요구하는 기법과 실제로 이 용기에서 관측된 상태를 비교한다.
        // Build는 "아무 기법도 시도되지 않았어야" 성립하는 소거법 조건이라 별도 분기로 처리한다.
        public bool MatchesRequiredTechnique(CocktailTechnique required)
        {
            if (required == CocktailTechnique.None)
                return true;

            bool anyShake = (explicitTechniques & CocktailTechnique.Shake) != 0
                || shakenVolumeMl > 0.0001f;
            if (required == CocktailTechnique.Build)
                return !StirAttempted && !anyShake;

            if (required == CocktailTechnique.Stir)
                return StirAttempted
                    && ((explicitTechniques & CocktailTechnique.Stir) != 0
                        || CoversAllContents(stirredVolumeMl))
                    && !anyShake;

            if (required == CocktailTechnique.Shake)
                return !StirAttempted
                    && ((explicitTechniques & CocktailTechnique.Shake) != 0
                        || CoversAllContents(shakenVolumeMl));

            return GetEffectiveTechniques() == required;
        }

        public bool MatchesShakeIceRequirement(IceRequirement requirement)
        {
            if (requirement == IceRequirement.Any)
                return true;
            if (requirement == IceRequirement.Required)
                return explicitShakenWithIce || CoversAllContents(shakenWithIceVolumeMl);
            return !explicitShakenWithIce && shakenWithIceVolumeMl <= 0.0001f;
        }

        // "완료" 판정의 공통 기준. coveredVolumeMl(예: 저어진 부피)이 현재 총 부피를 전부 덮어야
        // true — 부분적으로만 기법이 적용된 상태는 완료로 인정하지 않는다.
        private bool CoversAllContents(float coveredVolumeMl)
        {
            return TotalVolumeMl > 0.0001f
                && coveredVolumeMl >= TotalVolumeMl - 0.0001f;
        }

        // 색상 계산은 비용이 있어(portions 재구성 + 혼합 연산) Add()가 호출될 때만 dirty로 표시하고,
        // 실제 계산은 요청 시점까지 지연한다.
        public Color EvaluateFinalColor()
        {
            if (!finalColorDirty)
                return cachedFinalColor;

            finalColorPayload ??= new LiquidPayload();
            finalColorPayload.portions.Clear();
            foreach (KeyValuePair<ItemDef, float> pair in volumes)
            {
                if (pair.Key == null || pair.Value <= 0f)
                    continue;

                finalColorPayload.portions.Add(new LiquidPortion
                {
                    sourceItem = pair.Key,
                    volumeMl = pair.Value
                });
            }

            cachedFinalColor = finalColorPayload.EvaluateColor();
            finalColorDirty = false;
            return cachedFinalColor;
        }
    }

    // 병/잔/비커 등 액체를 담는 오브젝트에 붙는 컴포넌트. Trigger Collider2D 안에 들어온
    // LiquidParticleData/IceCubeController를 "소유"하고, 물리적으로 겹치는 다른 용기의 내용물과는
    // 서로 충돌을 무시하도록 Physics2D.IgnoreCollision 행렬을 관리해 용기별 액체를 격리한다.
    // 레시피 판정은 이 컴포넌트가 BuildComposition()으로 만든 스냅샷(CocktailComposition)만 본다.
    [RequireComponent(typeof(Collider2D))]
    public sealed class VesselLiquidTracker : MonoBehaviour
    {
        // 전역 레지스트리. 새 용기/입자/얼음이 등록될 때마다 기존 전체와의 충돌 무시 여부를
        // 다시 계산해야 하므로(RefreshParticleIsolation 등) static으로 모든 인스턴스가 공유한다.
        private static readonly HashSet<VesselLiquidTracker> activeVessels = new();
        private static readonly HashSet<LiquidParticleData> activeParticles = new();
        private static readonly HashSet<IceCubeController> activeIceCubes = new();
        private static bool liquidIceCollisionEnabled;
        private static CocktailEvaluator debugCocktailEvaluator;
        private static bool debugEvaluatorInitialized;
        private static bool runtimeDebugLabelsEnabled;
        private static float runtimeDebugLabelsRefreshInterval = 0.2f;
        private readonly HashSet<LiquidParticleData> particles = new();
        private readonly HashSet<LiquidParticleData> ownedParticles = new();
        private readonly HashSet<LiquidParticleData> pendingParticleReleases = new();
        private readonly HashSet<IceCubeController> iceCubes = new();
        private readonly HashSet<IceCubeController> ownedIceCubes = new();
        private readonly HashSet<IceCubeController> pendingIceReleases = new();
        private readonly HashSet<LiquidParticleData> externalMotionParticles = new();
        private readonly HashSet<IceCubeController> externalMotionIceCubes = new();
        private readonly List<Collider2D> overlapResults = new();
        private readonly List<LiquidParticleData> ownerReleaseBuffer = new();
        private readonly List<IceCubeController> iceReleaseBuffer = new();
        private readonly StringBuilder debugTextBuilder = new();
        private Collider2D[] colliders;
        private ContactFilter2D scanFilter;
        private GUIStyle debugBoxStyle;
        private string servingGlassId = string.Empty;
        private bool hasIce;
        private int interactionPriority;
        private bool externalMotionContentsCaptured;
        private int contentVersion;

        [Header("Debug View")]
        [SerializeField] private bool drawDebugGizmos = false;
        [SerializeField] private bool drawOnlyWhenSelected = false;
        [SerializeField] private bool drawRuntimeLabel = false;
        [SerializeField] private Color triggerDebugColor = new Color(0.15f, 0.85f, 1f, 0.8f);
        [SerializeField] private Color particleDebugColor = new Color(1f, 0.92f, 0.25f, 0.9f);
        [SerializeField] private Color connectionDebugColor = new Color(0.5f, 1f, 0.65f, 0.45f);
        [SerializeField, Min(0.01f)] private float debugParticleRadius = 0.06f;
        [SerializeField, Min(0.05f)] private float runtimeDebugRefreshInterval = 0.2f;

        private float nextRuntimeDebugRefreshTime;
        private string cachedRuntimeDebugText = string.Empty;

        public int ParticleCount
        {
            get
            {
                Cleanup();
                RefreshTrackedParticles();
                return particles.Count;
            }
        }

        public IReadOnlyCollection<LiquidParticleData> Particles
        {
            get
            {
                Cleanup();
                RefreshTrackedParticles();
                return particles;
            }
        }

        public int IceCount
        {
            get
            {
                Cleanup();
                RefreshTrackedIceCubes();
                return iceCubes.Count;
            }
        }

        public IReadOnlyCollection<IceCubeController> IceCubes
        {
            get
            {
                Cleanup();
                RefreshTrackedIceCubes();
                return iceCubes;
            }
        }

        private void Awake()
        {
            CacheColliders();
            scanFilter = ContactFilter2D.noFilter;
            scanFilter.useTriggers = true;
        }

        internal int InteractionPriority => interactionPriority;
        internal static HashSet<LiquidParticleData> ActiveParticles => activeParticles;
        public static bool LiquidIceCollisionEnabled => liquidIceCollisionEnabled;
        public int ContentVersion => contentVersion;

        public static void SetLiquidIceCollisionEnabled(bool enabled)
        {
            if (liquidIceCollisionEnabled == enabled)
                return;

            liquidIceCollisionEnabled = enabled;
            RefreshLiquidIceCollisions();
        }

        public static void SetRuntimeDebugLabelDefaults(
            bool enabled,
            float refreshInterval)
        {
            runtimeDebugLabelsEnabled = enabled;
            runtimeDebugLabelsRefreshInterval = Mathf.Max(0.05f, refreshInterval);

            foreach (VesselLiquidTracker vessel in activeVessels)
            {
                vessel?.ConfigureRuntimeDebugLabel(
                    runtimeDebugLabelsEnabled,
                    runtimeDebugLabelsRefreshInterval);
            }
        }

        internal void SetInteractionPriority(int priority)
        {
            interactionPriority = priority;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInteractionRegistry()
        {
            activeVessels.Clear();
            activeParticles.Clear();
            activeIceCubes.Clear();
            liquidIceCollisionEnabled = false;
            debugCocktailEvaluator = null;
            debugEvaluatorInitialized = false;
            runtimeDebugLabelsEnabled = false;
            runtimeDebugLabelsRefreshInterval = 0.2f;
        }

        private void OnEnable()
        {
            ConfigureRuntimeDebugLabel(
                runtimeDebugLabelsEnabled,
                runtimeDebugLabelsRefreshInterval);
            RegisterVessel(this);
        }

        private void OnDisable()
        {
            ownerReleaseBuffer.Clear();
            foreach (LiquidParticleData particle in ownedParticles)
            {
                if (particle != null)
                    ownerReleaseBuffer.Add(particle);
            }

            for (int i = 0; i < ownerReleaseBuffer.Count; i++)
                ownerReleaseBuffer[i].ReleaseVesselOwner(this);

            ownerReleaseBuffer.Clear();
            ownedParticles.Clear();
            particles.Clear();
            pendingParticleReleases.Clear();

            iceReleaseBuffer.Clear();
            foreach (IceCubeController iceCube in ownedIceCubes)
            {
                if (iceCube != null)
                    iceReleaseBuffer.Add(iceCube);
            }

            for (int i = 0; i < iceReleaseBuffer.Count; i++)
                iceReleaseBuffer[i].ReleaseVesselOwner(this);

            iceReleaseBuffer.Clear();
            ownedIceCubes.Clear();
            iceCubes.Clear();
            pendingIceReleases.Clear();
            EndExternalMotion();
            UnregisterVessel(this);
        }

        private void FixedUpdate()
        {
            ProcessPendingOwnerReleases();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Track(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            Track(other);
        }

        // 트리거를 벗어나도 곧바로 소유권을 놓지 않고 pending 목록에 넣어둔다. 실제 반환 여부는
        // FixedUpdate의 ProcessPendingOwnerReleases에서 한 번 더 확인한다(경계에서의 떨림으로
        // 같은 프레임에 재진입하는 경우까지 소유권을 뺏기지 않게 하기 위함).
        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.TryGetComponent(out LiquidParticleData particle))
            {
                particles.Remove(particle);
                if (particle.VesselOwner == this)
                    pendingParticleReleases.Add(particle);
            }

            IceCubeController iceCube = other.GetComponentInParent<IceCubeController>();
            if (iceCube == null)
                return;

            iceCubes.Remove(iceCube);
            if (iceCube.VesselOwner == this)
                pendingIceReleases.Add(iceCube);
        }

        // 현재 이 용기가 소유한 입자·얼음을 스캔해 CocktailComposition 스냅샷으로 합산한다.
        // 판정(CocktailEvaluator)과 디버그 표시 양쪽이 이 메서드 하나로 통일된 결과를 얻는다.
        public CocktailComposition BuildComposition()
        {
            Cleanup();
            RefreshTrackedParticles();
            RefreshTrackedIceCubes();

            CocktailComposition composition = new CocktailComposition();
            int servingIceCount = iceCubes.Count;
            if (servingIceCount == 0 && hasIce)
                servingIceCount = 1;
            composition.SetServingStyle(servingGlassId, servingIceCount);
            foreach (LiquidParticleData particle in particles)
            {
                if (particle == null || particle.payload == null)
                    continue;

                for (int i = 0; i < particle.payload.portions.Count; i++)
                {
                    LiquidPortion portion = particle.payload.portions[i];
                    composition.Add(portion.sourceItem, portion.volumeMl);
                }

                composition.AddThermalSample(
                    particle.payload.TotalVolumeMl,
                    particle.payload.temperatureC);
                composition.RecordParticleTechniqueState(
                    particle.payload.TotalVolumeMl,
                    particle.payload.techniques,
                    particle.payload.stirAttempted,
                    particle.payload.wasShakenWithIce);
            }

            return composition;
        }

        public void MarkContentsAsStirAttempted()
        {
            Cleanup();
            RefreshTrackedParticles();
            foreach (LiquidParticleData particle in particles)
                particle?.RecordStirAttempt();
        }

        public void MarkContentsAsStirred()
        {
            Cleanup();
            RefreshTrackedParticles();
            foreach (LiquidParticleData particle in particles)
            {
                if (particle == null)
                    continue;
                particle.RecordStirAttempt();
                particle.RecordTechnique(CocktailTechnique.Stir);
            }
        }

        // 용기 안 입자들이 서로 얼마나 고르게 섞였는지 나타내는 0~1 지표.
        // 각 입자의 재료 비율을 용기 전체 평균 비율과 비교해 편차를 부피 가중 평균한다 —
        // 0이면 모든 입자가 전체 평균과 동일한 조성(완전히 섞임), 1에 가까울수록 재료별로 분리된 상태.
        public float CalculateMeanCompositionDeviation()
        {
            Cleanup();
            RefreshTrackedParticles();

            Dictionary<ItemDef, float> totalByItem = new();
            float totalVolumeMl = 0f;
            foreach (LiquidParticleData particle in particles)
            {
                LiquidPayload payload = particle != null ? particle.payload : null;
                if (payload == null)
                    continue;

                for (int i = 0; i < payload.portions.Count; i++)
                {
                    LiquidPortion portion = payload.portions[i];
                    if (portion.sourceItem == null || portion.volumeMl <= 0f)
                        continue;

                    totalByItem.TryGetValue(portion.sourceItem, out float currentMl);
                    totalByItem[portion.sourceItem] = currentMl + portion.volumeMl;
                    totalVolumeMl += portion.volumeMl;
                }
            }

            if (totalVolumeMl <= 0.0001f)
                return 1f;

            float weightedDeviation = 0f;
            foreach (LiquidParticleData particle in particles)
            {
                LiquidPayload payload = particle != null ? particle.payload : null;
                float particleVolumeMl = payload != null ? payload.TotalVolumeMl : 0f;
                if (particleVolumeMl <= 0.0001f)
                    continue;

                float particleDeviation = 0f;
                foreach (KeyValuePair<ItemDef, float> pair in totalByItem)
                {
                    float vesselRatio = pair.Value / totalVolumeMl;
                    float particleRatio = payload.GetVolume(pair.Key) / particleVolumeMl;
                    particleDeviation += Mathf.Abs(particleRatio - vesselRatio);
                }

                weightedDeviation += particleDeviation * 0.5f * particleVolumeMl;
            }

            return Mathf.Clamp01(weightedDeviation / totalVolumeMl);
        }

        public void ConfigureServingStyle(string glassId, bool containsIce = false)
        {
            servingGlassId = glassId?.Trim() ?? string.Empty;
            hasIce = containsIce;
        }

        public void SetHasIce(bool value)
        {
            hasIce = value;
        }

        public void BeginExternalMotion()
        {
            Cleanup();
            RefreshTrackedParticles();
            RefreshTrackedIceCubes();

            externalMotionParticles.Clear();
            foreach (LiquidParticleData particle in particles)
            {
                if (!IsInvalidParticle(particle))
                    externalMotionParticles.Add(particle);
            }

            externalMotionIceCubes.Clear();
            foreach (IceCubeController iceCube in iceCubes)
            {
                if (!IsInvalidIceCube(iceCube) && !iceCube.IsDragging)
                    externalMotionIceCubes.Add(iceCube);
            }

            externalMotionContentsCaptured = true;
        }

        public void EndExternalMotion()
        {
            externalMotionContentsCaptured = false;
            externalMotionParticles.Clear();
            externalMotionIceCubes.Clear();
        }

        public void TranslateTrackedParticles(Vector2 delta)
        {
            if (delta.sqrMagnitude <= 0.000001f)
                return;

            if (externalMotionContentsCaptured)
            {
                TranslateExternalMotionContents(delta);
                return;
            }

            Cleanup();
            RefreshTrackedParticles();

            foreach (LiquidParticleData particle in particles)
                TranslateParticle(particle, delta);

            TranslateTrackedIceCubes(delta);
        }

        private void TranslateExternalMotionContents(Vector2 delta)
        {
            externalMotionParticles.RemoveWhere(IsInvalidParticle);
            foreach (LiquidParticleData particle in externalMotionParticles)
                TranslateParticle(particle, delta);

            externalMotionIceCubes.RemoveWhere(IsInvalidIceCube);
            foreach (IceCubeController iceCube in externalMotionIceCubes)
                iceCube?.Translate(delta);
        }

        private static void TranslateParticle(LiquidParticleData particle, Vector2 delta)
        {
            if (particle == null)
                return;

            Rigidbody2D particleBody = particle.GetComponent<Rigidbody2D>();
            if (particleBody != null)
            {
                Vector2 targetPosition = particleBody.position + delta;
                particleBody.position = targetPosition;
                particle.transform.position = new Vector3(
                    targetPosition.x,
                    targetPosition.y,
                    particle.transform.position.z);
                if (particleBody.simulated)
                    particleBody.WakeUp();
            }
            else
            {
                particle.transform.position += (Vector3)delta;
            }
        }

        private void TranslateTrackedIceCubes(Vector2 delta)
        {
            Cleanup();
            RefreshTrackedIceCubes();
            foreach (IceCubeController iceCube in iceCubes)
                iceCube?.Translate(delta);
        }

        private void Track(Collider2D other)
        {
            if (other == null)
                return;

            if (other.TryGetComponent(out LiquidParticleData particle))
                TrackParticle(particle);

            IceCubeController iceCube = other.GetComponentInParent<IceCubeController>();
            if (iceCube != null)
                TrackIceCube(iceCube);
        }

        // 소유권 전이 규칙: 이미 다른 용기가 소유 중이면(그리고 그 용기 트리거를 실제로 벗어났다면)
        // 먼저 반환시킨 뒤, 소유자가 없을 때만 이 지점에서 우선순위가 가장 높은 용기가 새로 가져간다.
        // 이렇게 하면 두 용기가 겹치는 구간에서 같은 입자가 양쪽에 동시에 카운트되지 않는다.
        private void TrackParticle(LiquidParticleData particle)
        {
            if (particle == null)
                return;

            if (particle.hasBeenCollected)
                return;

            pendingParticleReleases.Remove(particle);

            VesselLiquidTracker previousOwner = particle.VesselOwner;
            if (previousOwner != null
                && previousOwner != this
                && !previousOwner.ContainsTriggerPoint(particle.transform.position))
            {
                particle.ReleaseVesselOwner(previousOwner);
            }

            if (particle.VesselOwner == null)
                particle.TryAssignVesselOwner(FindPreferredOwner(particle.transform.position));

            if (particle.VesselOwner == this)
                particles.Add(particle);
        }

        private void TrackIceCube(IceCubeController iceCube)
        {
            if (iceCube == null || iceCube.IsDragging || !iceCube.gameObject.activeInHierarchy)
                return;

            pendingIceReleases.Remove(iceCube);

            VesselLiquidTracker previousOwner = iceCube.VesselOwner;
            if (previousOwner != null
                && previousOwner != this
                && !previousOwner.ContainsTriggerPoint(iceCube.PhysicsPosition))
            {
                iceCube.ReleaseVesselOwner(previousOwner);
            }

            if (iceCube.VesselOwner == null)
                iceCube.TryAssignVesselOwner(FindPreferredOwner(iceCube.PhysicsPosition));

            if (iceCube.VesselOwner == this)
                iceCubes.Add(iceCube);
        }

        // 한 지점을 여러 용기의 트리거가 동시에 덮을 수 있으므로(예: 잔이 병 위에 겹칠 때)
        // interactionPriority가 더 높은 쪽을 우선하고, 우선순위가 같으면 InstanceID로
        // 결정적인(매 프레임 결과가 바뀌지 않는) 타이브레이크를 한다.
        private static VesselLiquidTracker FindPreferredOwner(Vector2 worldPoint)
        {
            VesselLiquidTracker preferred = null;
            foreach (VesselLiquidTracker vessel in activeVessels)
            {
                if (vessel == null
                    || !vessel.isActiveAndEnabled
                    || !vessel.ContainsTriggerPoint(worldPoint))
                {
                    continue;
                }

                if (preferred == null
                    || vessel.interactionPriority > preferred.interactionPriority
                    || (vessel.interactionPriority == preferred.interactionPriority
                        && vessel.GetInstanceID() > preferred.GetInstanceID()))
                {
                    preferred = vessel;
                }
            }

            return preferred;
        }

        private bool ContainsTriggerPoint(Vector2 worldPoint)
        {
            CacheColliders();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D trigger = colliders[i];
                if (trigger != null
                    && trigger.enabled
                    && trigger.isTrigger
                    && trigger.gameObject.activeInHierarchy
                    && trigger.OverlapPoint(worldPoint))
                {
                    return true;
                }
            }

            return false;
        }

        private void Cleanup()
        {
            particles.RemoveWhere(IsInvalidParticle);
            ownedParticles.RemoveWhere(IsInvalidParticle);
            pendingParticleReleases.RemoveWhere(
                particle => IsInvalidParticle(particle) || particle.VesselOwner != this);
            iceCubes.RemoveWhere(IsInvalidIceCube);
            ownedIceCubes.RemoveWhere(IsInvalidIceCube);
            pendingIceReleases.RemoveWhere(
                iceCube => IsInvalidIceCube(iceCube) || iceCube.VesselOwner != this);
        }

        // OnTriggerExit2D에서 pending 처리된 항목들을 매 FixedUpdate마다 재확인해,
        // 그새 다시 트리거 안으로 들어왔다면(ContainsTriggerPoint) 소유권을 유지하고
        // 정말로 벗어난 경우에만 실제로 반환한다.
        private void ProcessPendingOwnerReleases()
        {
            Cleanup();

            ownerReleaseBuffer.Clear();
            foreach (LiquidParticleData particle in pendingParticleReleases)
            {
                if (particle.VesselOwner != this
                    || ContainsTriggerPoint(particle.transform.position))
                {
                    continue;
                }

                ownerReleaseBuffer.Add(particle);
            }

            for (int i = 0; i < ownerReleaseBuffer.Count; i++)
            {
                LiquidParticleData particle = ownerReleaseBuffer[i];
                pendingParticleReleases.Remove(particle);
                particle.ReleaseVesselOwner(this);
            }
            ownerReleaseBuffer.Clear();

            iceReleaseBuffer.Clear();
            foreach (IceCubeController iceCube in pendingIceReleases)
            {
                if (iceCube.VesselOwner != this
                    || ContainsTriggerPoint(iceCube.PhysicsPosition))
                {
                    continue;
                }

                iceReleaseBuffer.Add(iceCube);
            }

            for (int i = 0; i < iceReleaseBuffer.Count; i++)
            {
                IceCubeController iceCube = iceReleaseBuffer[i];
                pendingIceReleases.Remove(iceCube);
                iceCube.ReleaseVesselOwner(this);
            }
            iceReleaseBuffer.Clear();
        }

        private void RefreshTrackedParticles()
        {
            particles.Clear();
            CacheColliders();

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D trigger = colliders[i];
                if (trigger == null || !trigger.enabled || !trigger.isTrigger)
                    continue;

                overlapResults.Clear();
                trigger.Overlap(scanFilter, overlapResults);

                for (int j = 0; j < overlapResults.Count; j++)
                    Track(overlapResults[j]);
            }

            overlapResults.Clear();
        }

        private void RefreshTrackedIceCubes()
        {
            iceCubes.Clear();

            foreach (IceCubeController ownedIce in ownedIceCubes)
            {
                if (ownedIce == null || ownedIce.IsDragging)
                    continue;

                if (ContainsTriggerPoint(ownedIce.PhysicsPosition))
                {
                    iceCubes.Add(ownedIce);
                }
                else
                {
                    pendingIceReleases.Add(ownedIce);
                }
            }

            foreach (IceCubeController iceCube in activeIceCubes)
            {
                if (iceCube != null
                    && !iceCube.IsDragging
                    && ContainsTriggerPoint(iceCube.PhysicsPosition))
                {
                    TrackIceCube(iceCube);
                }
            }
        }

        internal void RegisterOwnedParticle(LiquidParticleData particle)
        {
            if (particle != null && ownedParticles.Add(particle))
                contentVersion++;
        }

        internal void UnregisterOwnedParticle(LiquidParticleData particle)
        {
            if (particle != null)
            {
                if (ownedParticles.Remove(particle))
                    contentVersion++;
                particles.Remove(particle);
                pendingParticleReleases.Remove(particle);
            }
        }

        internal void RegisterOwnedIceCube(IceCubeController iceCube)
        {
            if (iceCube != null)
                ownedIceCubes.Add(iceCube);
        }

        internal void UnregisterOwnedIceCube(IceCubeController iceCube)
        {
            if (iceCube == null)
                return;
            ownedIceCubes.Remove(iceCube);
            iceCubes.Remove(iceCube);
            pendingIceReleases.Remove(iceCube);
        }

        internal static void RegisterIceCube(IceCubeController iceCube)
        {
            if (iceCube == null || !activeIceCubes.Add(iceCube))
                return;

            RefreshIceIsolation(iceCube);
        }

        internal static void UnregisterIceCube(IceCubeController iceCube)
        {
            if (iceCube == null)
                return;

            Collider2D iceCollider = iceCube.PhysicsCollider;
            if (iceCollider != null)
            {
                foreach (IceCubeController otherIce in activeIceCubes)
                {
                    if (otherIce == null || otherIce == iceCube || otherIce.PhysicsCollider == null)
                        continue;

                    Physics2D.IgnoreCollision(iceCollider, otherIce.PhysicsCollider, false);
                }

                foreach (LiquidParticleData particle in activeParticles)
                {
                    if (particle != null && particle.ParticleCollider != null)
                        Physics2D.IgnoreCollision(iceCollider, particle.ParticleCollider, false);
                }

                foreach (VesselLiquidTracker vessel in activeVessels)
                    SetIceVesselCollision(iceCollider, vessel, false);
            }

            activeIceCubes.Remove(iceCube);
        }

        internal static void RegisterParticle(LiquidParticleData particle)
        {
            if (particle == null || !activeParticles.Add(particle))
                return;

            RefreshParticleIsolation(particle);
        }

        internal static void UnregisterParticle(LiquidParticleData particle)
        {
            if (particle == null)
                return;

            Collider2D particleCollider = particle.ParticleCollider;
            if (particleCollider != null)
            {
                foreach (LiquidParticleData other in activeParticles)
                {
                    if (other == null || other == particle || other.ParticleCollider == null)
                        continue;

                    Physics2D.IgnoreCollision(particleCollider, other.ParticleCollider, false);
                }

                foreach (VesselLiquidTracker vessel in activeVessels)
                    SetParticleVesselCollision(particleCollider, vessel, false);

                foreach (IceCubeController iceCube in activeIceCubes)
                {
                    if (iceCube != null && iceCube.PhysicsCollider != null)
                        Physics2D.IgnoreCollision(particleCollider, iceCube.PhysicsCollider, false);
                }
            }

            activeParticles.Remove(particle);
        }

        // 소유자가 다른 입자·얼음·용기 벽끼리는 Physics2D.IgnoreCollision으로 서로 충돌을 꺼서,
        // 겹쳐 보이는 두 용기(예: 잔 위의 병)의 내용물이 물리적으로 밀어내거나 섞이지 않게 한다.
        // 소유자가 없는(아직 배정 전) 입자는 모든 용기와 충돌 가능 상태로 둬서 Track()이 담당 용기를 정할 수 있게 한다.
        internal static void RefreshParticleIsolation(LiquidParticleData particle)
        {
            if (particle == null || !particle.isActiveAndEnabled)
                return;

            Collider2D particleCollider = particle.ParticleCollider;
            if (particleCollider == null)
                return;

            foreach (VesselLiquidTracker vessel in activeVessels)
            {
                bool ignoreVessel = particle.VesselOwner != null
                    && particle.VesselOwner != vessel;
                SetParticleVesselCollision(particleCollider, vessel, ignoreVessel);
            }

            foreach (LiquidParticleData other in activeParticles)
            {
                if (other == null || other == particle || other.ParticleCollider == null)
                    continue;

                bool ignoreParticle = particle.VesselOwner != null
                    && other.VesselOwner != null
                    && particle.VesselOwner != other.VesselOwner;
                Physics2D.IgnoreCollision(
                    particleCollider,
                    other.ParticleCollider,
                    ignoreParticle);
            }

            foreach (IceCubeController iceCube in activeIceCubes)
            {
                if (iceCube == null || iceCube.PhysicsCollider == null)
                    continue;

                Physics2D.IgnoreCollision(
                    particleCollider,
                    iceCube.PhysicsCollider,
                    ShouldIgnoreLiquidIceCollision(particle, iceCube));
            }
        }

        internal static void RefreshIceIsolation(IceCubeController iceCube)
        {
            if (iceCube == null || !iceCube.isActiveAndEnabled)
                return;

            Collider2D iceCollider = iceCube.PhysicsCollider;
            if (iceCollider == null)
                return;

            foreach (VesselLiquidTracker vessel in activeVessels)
            {
                bool ignoreVessel = iceCube.VesselOwner != null
                    && iceCube.VesselOwner != vessel;
                SetIceVesselCollision(iceCollider, vessel, ignoreVessel);
            }

            foreach (IceCubeController otherIce in activeIceCubes)
            {
                if (otherIce == null || otherIce == iceCube || otherIce.PhysicsCollider == null)
                    continue;

                bool ignoreIce = iceCube.VesselOwner != null
                    && otherIce.VesselOwner != null
                    && iceCube.VesselOwner != otherIce.VesselOwner;
                Physics2D.IgnoreCollision(iceCollider, otherIce.PhysicsCollider, ignoreIce);
            }

            foreach (LiquidParticleData particle in activeParticles)
            {
                if (particle == null || particle.ParticleCollider == null)
                    continue;

                Physics2D.IgnoreCollision(
                    iceCollider,
                    particle.ParticleCollider,
                    ShouldIgnoreLiquidIceCollision(particle, iceCube));
            }
        }

        private static void RefreshLiquidIceCollisions()
        {
            foreach (LiquidParticleData particle in activeParticles)
            {
                if (particle == null || particle.ParticleCollider == null)
                    continue;

                foreach (IceCubeController iceCube in activeIceCubes)
                {
                    if (iceCube == null || iceCube.PhysicsCollider == null)
                        continue;

                    Physics2D.IgnoreCollision(
                        particle.ParticleCollider,
                        iceCube.PhysicsCollider,
                        ShouldIgnoreLiquidIceCollision(particle, iceCube));
                }
            }
        }

        private static bool ShouldIgnoreLiquidIceCollision(
            LiquidParticleData particle,
            IceCubeController iceCube)
        {
            return !liquidIceCollisionEnabled
                || (particle.VesselOwner != null
                    && iceCube.VesselOwner != null
                    && particle.VesselOwner != iceCube.VesselOwner);
        }

        private static void RegisterVessel(VesselLiquidTracker vessel)
        {
            if (vessel == null || !activeVessels.Add(vessel))
                return;

            vessel.CacheColliders();
            foreach (LiquidParticleData particle in activeParticles)
            {
                if (particle == null || particle.ParticleCollider == null)
                    continue;

                bool ignoreVessel = particle.VesselOwner != null
                    && particle.VesselOwner != vessel;
                SetParticleVesselCollision(particle.ParticleCollider, vessel, ignoreVessel);
            }

            foreach (IceCubeController iceCube in activeIceCubes)
            {
                if (iceCube == null || iceCube.PhysicsCollider == null)
                    continue;

                bool ignoreVessel = iceCube.VesselOwner != null
                    && iceCube.VesselOwner != vessel;
                SetIceVesselCollision(iceCube.PhysicsCollider, vessel, ignoreVessel);
            }
        }

        private static void UnregisterVessel(VesselLiquidTracker vessel)
        {
            if (vessel == null)
                return;

            foreach (LiquidParticleData particle in activeParticles)
            {
                if (particle != null && particle.ParticleCollider != null)
                    SetParticleVesselCollision(particle.ParticleCollider, vessel, false);
            }

            foreach (IceCubeController iceCube in activeIceCubes)
            {
                if (iceCube != null && iceCube.PhysicsCollider != null)
                    SetIceVesselCollision(iceCube.PhysicsCollider, vessel, false);
            }

            activeVessels.Remove(vessel);
        }

        private static void SetParticleVesselCollision(
            Collider2D particleCollider,
            VesselLiquidTracker vessel,
            bool ignore)
        {
            if (particleCollider == null || vessel == null)
                return;

            vessel.CacheColliders();
            for (int i = 0; i < vessel.colliders.Length; i++)
            {
                Collider2D vesselCollider = vessel.colliders[i];
                if (vesselCollider == null || vesselCollider.isTrigger)
                    continue;

                bool iceOnlyBarrier = vesselCollider.GetComponent<IceOnlyVesselBarrier>() != null;
                Physics2D.IgnoreCollision(
                    particleCollider,
                    vesselCollider,
                    ignore || iceOnlyBarrier);
            }
        }

        private static void SetIceVesselCollision(
            Collider2D iceCollider,
            VesselLiquidTracker vessel,
            bool ignore)
        {
            if (iceCollider == null || vessel == null)
                return;

            vessel.CacheColliders();
            for (int i = 0; i < vessel.colliders.Length; i++)
            {
                Collider2D vesselCollider = vessel.colliders[i];
                if (vesselCollider == null || vesselCollider.isTrigger)
                    continue;

                Physics2D.IgnoreCollision(iceCollider, vesselCollider, ignore);
            }
        }

        internal void RefreshCollisionGeometry()
        {
            CacheColliders();

            foreach (LiquidParticleData particle in activeParticles)
                RefreshParticleIsolation(particle);

            foreach (IceCubeController iceCube in activeIceCubes)
                RefreshIceIsolation(iceCube);
        }

        private void CacheColliders()
        {
            colliders = GetComponentsInChildren<Collider2D>();
        }

        private static bool IsInvalidParticle(LiquidParticleData particle)
        {
            return particle == null
                || particle.hasBeenCollected
                || !particle.gameObject.activeInHierarchy;
        }

        private static bool IsInvalidIceCube(IceCubeController iceCube)
        {
            return iceCube == null || !iceCube.gameObject.activeInHierarchy;
        }

        public bool HasTriggerCollider()
        {
            if (colliders == null || colliders.Length == 0)
                CacheColliders();

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].isTrigger)
                    return true;
            }

            return false;
        }

        public void SetDebugViewEnabled(bool enabled)
        {
            drawDebugGizmos = enabled;
            drawRuntimeLabel = enabled;
            InvalidateRuntimeDebugSnapshot();
        }

        public void ConfigureRuntimeDebugLabel(bool enabled, float refreshInterval)
        {
            drawRuntimeLabel = enabled && (Application.isEditor || Debug.isDebugBuild);
            runtimeDebugRefreshInterval = Mathf.Max(0.05f, refreshInterval);
            InvalidateRuntimeDebugSnapshot();
        }

        // ── 이하 디버그 전용 시각화(Gizmo/OnGUI) — 게임플레이 판정 로직과 무관, 개발 중 조성 확인용 ──
        private void OnDrawGizmos()
        {
            if (!drawDebugGizmos || drawOnlyWhenSelected)
                return;

            DrawDebugGizmos();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebugGizmos || !drawOnlyWhenSelected)
                return;

            DrawDebugGizmos();
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !drawRuntimeLabel)
                return;

            Camera camera = Camera.main;
            if (camera == null && BartendingViewport.Active == null)
                return;

            RefreshRuntimeDebugSnapshot();
            Vector2 screenPosition = BartendingViewport.GetPointerScreenPosition(
                camera,
                GetDebugLabelWorldPosition());

            EnsureDebugBoxStyle();

            const float width = 280f;
            GUIContent content = new GUIContent(cachedRuntimeDebugText);
            float height = debugBoxStyle.CalcHeight(content, width);
            Rect rect = new Rect(
                screenPosition.x + 8f,
                Screen.height - screenPosition.y - height - 8f,
                width,
                height);

            GUI.Box(rect, content, debugBoxStyle);
        }

        private void DrawDebugGizmos()
        {
            CacheColliders();

            Color previousColor = Gizmos.color;

            Gizmos.color = triggerDebugColor;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.isTrigger)
                    continue;

                DrawColliderGizmo(collider);
            }

            if (Application.isPlaying)
            {
                RefreshTrackedParticles();
                Vector3 origin = GetDebugLabelWorldPosition();

                foreach (LiquidParticleData particle in particles)
                {
                    if (particle == null)
                        continue;

                    Vector3 position = particle.transform.position;
                    Gizmos.color = connectionDebugColor;
                    Gizmos.DrawLine(origin, position);
                    Gizmos.color = particleDebugColor;
                    Gizmos.DrawWireSphere(position, debugParticleRadius);
                }

#if UNITY_EDITOR
                CocktailComposition composition = BuildComposition();
                UnityEditor.Handles.Label(
                    origin,
                    BuildDebugText(composition, EvaluateForDebug(composition)));
#endif
            }

            Gizmos.color = previousColor;
        }

        private void DrawColliderGizmo(Collider2D collider)
        {
            if (collider is BoxCollider2D boxCollider)
            {
                Matrix4x4 previousMatrix = Gizmos.matrix;
                Gizmos.matrix = boxCollider.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(boxCollider.offset, boxCollider.size);
                Gizmos.matrix = previousMatrix;
                return;
            }

            if (collider is CircleCollider2D circleCollider)
            {
                Vector3 center = circleCollider.transform.TransformPoint(circleCollider.offset);
                Vector3 scale = circleCollider.transform.lossyScale;
                float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                Gizmos.DrawWireSphere(center, circleCollider.radius * radiusScale);
                return;
            }

            Bounds bounds = collider.bounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

        private Vector3 GetDebugLabelWorldPosition()
        {
            CacheColliders();

            bool hasBounds = false;
            Bounds combinedBounds = default;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.isTrigger)
                    continue;

                if (!hasBounds)
                {
                    combinedBounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(collider.bounds);
                }
            }

            if (!hasBounds)
                return transform.position + Vector3.up * 0.5f;

            return new Vector3(combinedBounds.center.x, combinedBounds.max.y + 0.25f, transform.position.z);
        }

        private void RefreshRuntimeDebugSnapshot()
        {
            float now = Time.unscaledTime;
            if (!string.IsNullOrEmpty(cachedRuntimeDebugText)
                && now < nextRuntimeDebugRefreshTime)
            {
                return;
            }

            CocktailComposition composition = BuildComposition();
            cachedRuntimeDebugText = BuildDebugText(
                composition,
                EvaluateForDebug(composition));
            nextRuntimeDebugRefreshTime = now
                + Mathf.Max(0.05f, runtimeDebugRefreshInterval);
        }

        private void InvalidateRuntimeDebugSnapshot()
        {
            cachedRuntimeDebugText = string.Empty;
            nextRuntimeDebugRefreshTime = 0f;
        }

        private static CocktailEvaluationResult EvaluateForDebug(
            CocktailComposition composition)
        {
            if (composition == null || composition.TotalVolumeMl <= 0.0001f)
                return null;

            CocktailEvaluator evaluator = GetDebugCocktailEvaluator();
            return evaluator != null ? evaluator.Evaluate(composition) : null;
        }

        private static CocktailEvaluator GetDebugCocktailEvaluator()
        {
            if (debugEvaluatorInitialized)
                return debugCocktailEvaluator;

            debugEvaluatorInitialized = true;
            ItemDefCatalog itemCatalog = ItemDefCatalog.LoadFromResources(
                ProjectResourcePaths.BartendingItems,
                null);
            CocktailRecipeCatalog recipeCatalog =
                CocktailRecipeDataLoader.LoadDefault(itemCatalog);
            if (recipeCatalog != null && recipeCatalog.Count > 0)
                debugCocktailEvaluator = new CocktailEvaluator(recipeCatalog);
            return debugCocktailEvaluator;
        }

        private string BuildDebugText(
            CocktailComposition composition,
            CocktailEvaluationResult evaluation)
        {
            debugTextBuilder.Clear();
            debugTextBuilder.Append(name);
            debugTextBuilder.AppendLine(" 액체 추적기");

            debugTextBuilder.Append("분류: ");
            if (composition.TotalVolumeMl <= 0.0001f)
            {
                debugTextBuilder.AppendLine("빈 잔");
            }
            else if (evaluation != null
                && evaluation.isSuccess
                && evaluation.matchedRecipe != null)
            {
                debugTextBuilder.AppendLine(GetRecipeLabel(evaluation.matchedRecipe));
            }
            else
            {
                debugTextBuilder.AppendLine("미분류");
                if (evaluation != null
                    && !string.IsNullOrWhiteSpace(evaluation.failureReason))
                {
                    debugTextBuilder.Append("사유: ");
                    debugTextBuilder.AppendLine(evaluation.failureReason);
                }
            }

            CocktailTechnique effectiveTechniques = composition.GetEffectiveTechniques();
            CocktailTechnique observedTechniques = composition.Techniques;
            bool stirCompleted = (effectiveTechniques & CocktailTechnique.Stir) != 0;
            bool shakeCompleted = (effectiveTechniques & CocktailTechnique.Shake) != 0;
            bool shakeObserved = (observedTechniques & CocktailTechnique.Shake) != 0;
            bool anyShakenWithIce = !composition.MatchesShakeIceRequirement(
                IceRequirement.None);

            debugTextBuilder.Append("판정 기법: ");
            debugTextBuilder.AppendLine(GetTechniqueLabel(effectiveTechniques));
            debugTextBuilder.Append("Stir: ");
            debugTextBuilder.Append(stirCompleted
                ? "완료"
                : composition.StirAttempted ? "시도(미완료)" : "사용 안 함");
            debugTextBuilder.Append("  Shake: ");
            debugTextBuilder.Append(shakeCompleted
                ? "완료"
                : shakeObserved ? "일부만 적용" : "사용 안 함");
            if (shakeObserved || shakeCompleted)
                debugTextBuilder.Append(anyShakenWithIce ? "(얼음 사용)" : "(얼음 없음)");
            debugTextBuilder.AppendLine();

            debugTextBuilder.Append("잔: ");
            debugTextBuilder.Append(GetGlassLabel(composition.GlassId));
            debugTextBuilder.Append("  얼음: ");
            debugTextBuilder.Append(composition.IceCount);
            debugTextBuilder.AppendLine("개");
            debugTextBuilder.Append("총량: ");
            debugTextBuilder.Append(composition.TotalVolumeMl.ToString("0.##"));
            debugTextBuilder.Append(" ml  입자: ");
            debugTextBuilder.Append(particles.Count);
            debugTextBuilder.Append("  온도: ");
            debugTextBuilder.Append(composition.AverageTemperatureC.ToString("0.#"));
            debugTextBuilder.AppendLine(" C");

            foreach (KeyValuePair<ItemDef, float> pair in composition.Volumes)
            {
                float ratio = composition.TotalVolumeMl > 0f
                    ? pair.Value / composition.TotalVolumeMl * 100f
                    : 0f;

                debugTextBuilder.Append(GetItemLabel(pair.Key));
                debugTextBuilder.Append(": ");
                debugTextBuilder.Append(pair.Value.ToString("0.##"));
                debugTextBuilder.Append(" ml / ");
                debugTextBuilder.Append(ratio.ToString("0.#"));
                debugTextBuilder.AppendLine("%");
            }

            if (composition.Volumes.Count == 0)
                debugTextBuilder.AppendLine("비어 있음");

            return debugTextBuilder.ToString();
        }

        private static string GetRecipeLabel(CocktailRecipe recipe)
        {
            if (recipe == null)
                return "미분류";
            if (!string.IsNullOrWhiteSpace(recipe.displayName))
                return recipe.displayName;
            if (!string.IsNullOrWhiteSpace(recipe.id))
                return recipe.id;
            return "이름 없는 레시피";
        }

        private static string GetTechniqueLabel(CocktailTechnique techniques)
        {
            bool hasStir = (techniques & CocktailTechnique.Stir) != 0;
            bool hasShake = (techniques & CocktailTechnique.Shake) != 0;
            if (hasStir && hasShake)
                return "Stir + Shake";
            if (hasStir)
                return "Stir";
            if (hasShake)
                return "Shake";
            return "Build";
        }

        private static string GetGlassLabel(string glassId)
        {
            if (string.IsNullOrWhiteSpace(glassId))
                return "미지정";

            return glassId.Trim().ToLowerInvariant() switch
            {
                "rock" => "락 글라스",
                "highball" => "하이볼 글라스",
                "hurricane" => "허리케인 글라스",
                "martini" => "마티니 글라스",
                _ => glassId
            };
        }

        private static string GetItemLabel(ItemDef item)
        {
            if (item == null)
                return "알 수 없음";

            if (!string.IsNullOrWhiteSpace(item.displayName))
                return item.displayName;

            if (!string.IsNullOrWhiteSpace(item.id))
                return item.id;

            return item.name;
        }

        private void EnsureDebugBoxStyle()
        {
            if (debugBoxStyle != null)
                return;

            debugBoxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                wordWrap = true,
                padding = new RectOffset(6, 6, 5, 5)
            };
            debugBoxStyle.normal.textColor = Color.white;
        }
    }
}
