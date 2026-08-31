using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Slainte.Bartending
{
    [MovedFrom(true, "", "Assembly-CSharp", "LiquidReaction")]
    public class LiquidReaction : MonoBehaviour
    {
        [HideInInspector] public SpriteRenderer spriteRenderer;
        [HideInInspector] public Rigidbody2D rb;
        [HideInInspector] public LiquidParticleData particleData;

        [Header("Mix Settings")]
        public float mixSpeed = 0.25f;
        public float reactionCooldown = 0.05f;
        public float compositionDifferenceTolerance = 0.001f;
        public float passiveMixSpeed = 0.005f;
        public float agitationMixSpeed = 0.12f;
        public float agitationMixRadius = 0.16f;
        public float agitationMixInterval = 0.05f;
        public float agitationVelocityThreshold = 0.15f;
        public float agitationFullMixRelativeSpeed = 0.45f;
        public int agitationMaxPartners = 3;
        private float lastReactionTime;
        private float nextAgitationMixTime;
        private LiquidReaction recentMixPartner;
        private float recentMixPartnerTime;

        [Header("Optimization Settings")]
        public float sleepVelocityThreshold = 0.05f;
        public float timeToSleep = 2.0f;
        private float settleTimer = 0f;
        private bool isLogicallySleeping = false;
        private Collider2D ownCollider;
        private int agitationSearchOffset;

        private static readonly Collider2D[] NearbyParticles = new Collider2D[32];

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            rb = GetComponent<Rigidbody2D>();
            particleData = GetComponent<LiquidParticleData>();
            ownCollider = GetComponent<Collider2D>();
        }

        void FixedUpdate()
        {
            TryMixNearbyParticlesByAgitation();
        }

        public void CheckSleepState(float deltaTime)
        {
            if (isLogicallySleeping) return;
            if (rb == null) return;

            if (rb.linearVelocity.sqrMagnitude < sleepVelocityThreshold)
            {
                settleTimer += deltaTime;
                if (settleTimer >= timeToSleep)
                    GoToSleep();
            }
            else
            {
                settleTimer = 0f;
            }
        }

        void GoToSleep()
        {
            isLogicallySleeping = true;
            rb.Sleep();
        }

        public void WakeUp()
        {
            isLogicallySleeping = false;
            settleTimer = 0f;

            // 확산 믹싱 스케줄에 랜덤 지터를 줘서, 같은 물리 프레임에 스폰/재활성화된
            // 입자들이 영구적으로 같은 틱에 몰리는 것을 방지한다.
            nextAgitationMixTime = Time.time + Random.Range(0f, agitationMixInterval);

            if (rb != null && !rb.IsAwake())
                rb.WakeUp();
        }

        void OnCollisionEnter2D(Collision2D collision)
        {
            TryMixCollision(collision);
        }

        void TryMixCollision(Collision2D collision)
        {
            if (isLogicallySleeping) return;

            if (collision.gameObject.TryGetComponent(out LiquidReaction otherParticle))
            {
                if (!HasMeaningfulMixTarget(otherParticle))
                    return;

                if (Time.time < lastReactionTime + reactionCooldown)
                    return;

                MixAttributes(otherParticle);
                lastReactionTime = Time.time;
                RecordRecentMixPartner(otherParticle);
            }
        }

        void RecordRecentMixPartner(LiquidReaction partner)
        {
            recentMixPartner = partner;
            recentMixPartnerTime = Time.time;
        }

        bool WasRecentlyMixedWith(LiquidReaction partner)
        {
            return recentMixPartner == partner
                && (Time.time - recentMixPartnerTime) < agitationMixInterval;
        }

        void MixAttributes(LiquidReaction other)
        {
            if (other == null)
                return;

            MixPayloadAndVisuals(other);
            MixPhysicalAttributes(other);
        }

        bool HasMeaningfulMixTarget(LiquidReaction other)
        {
            if (other == null || !CanInteractWith(other))
                return false;

            if (particleData == null)
                particleData = GetComponent<LiquidParticleData>();

            if (other.particleData == null)
                other.particleData = other.GetComponent<LiquidParticleData>();

            if (particleData != null
                && other.particleData != null
                && particleData.HasDifferentComposition(other.particleData, compositionDifferenceTolerance))
            {
                return true;
            }

            return HasDifferentPhysicalAttributes(other);
        }

        bool CanInteractWith(LiquidReaction other)
        {
            if (other == null)
                return false;

            if (particleData == null)
                particleData = GetComponent<LiquidParticleData>();

            if (other.particleData == null)
                other.particleData = other.GetComponent<LiquidParticleData>();

            return particleData == null
                || other.particleData == null
                || particleData.CanInteractWith(other.particleData);
        }

        void MixPayloadAndVisuals(LiquidReaction other)
        {
            if (particleData == null)
                particleData = GetComponent<LiquidParticleData>();

            if (other.particleData == null)
                other.particleData = other.GetComponent<LiquidParticleData>();

            if (particleData == null || other.particleData == null)
                return;

            MixParticleData(other, mixSpeed);
        }

        void MixParticleData(LiquidReaction other, float strength, bool wakeParticles = true)
        {
            if (wakeParticles && isLogicallySleeping)
                WakeUp();

            if (wakeParticles && other.isLogicallySleeping)
                other.WakeUp();

            particleData.MixPayloadWith(other.particleData, strength);
            particleData.ApplyVisualFromPayload();
            other.particleData.ApplyVisualFromPayload();
        }

        void TryMixNearbyParticlesByAgitation()
        {
            if (isLogicallySleeping) return;
            if (rb == null) return;
            if (Time.time < nextAgitationMixTime) return;

            nextAgitationMixTime = Time.time + agitationMixInterval;

            ContactFilter2D contactFilter = new ContactFilter2D();
            contactFilter.SetLayerMask(1 << gameObject.layer);
            contactFilter.useTriggers = false;

            int count = Physics2D.OverlapCircle(
                transform.position,
                agitationMixRadius,
                contactFilter,
                NearbyParticles);

            int mixedPartners = 0;
            if (count <= 0)
                return;

            agitationSearchOffset = (agitationSearchOffset + 1) % count;

            for (int step = 0; step < count; step++)
            {
                int i = (agitationSearchOffset + step) % count;
                Collider2D hit = NearbyParticles[i];
                NearbyParticles[i] = null;

                if (hit == null || hit == ownCollider)
                    continue;

                if (!hit.TryGetComponent(out LiquidReaction other))
                    continue;

                // 같은 쌍을 양쪽에서 각각 판정/믹싱하지 않도록, 인스턴스 ID가 더 작은 쪽만 처리한다.
                if (GetInstanceID() > other.GetInstanceID())
                    continue;

                // 직전 물리 충돌(OnCollisionEnter2D)로 이미 이 상대와 믹싱했다면 이번 틱은 건너뛴다.
                if (WasRecentlyMixedWith(other))
                    continue;

                if (!HasMeaningfulMixTarget(other))
                    continue;

                float relativeMixStrength = GetRelativeVelocityMixStrength(other);
                float mixStrength = Mathf.Clamp01(passiveMixSpeed + relativeMixStrength);
                if (mixStrength <= 0f)
                    continue;

                MixParticleData(other, mixStrength, relativeMixStrength > 0f);
                MixPhysicalAttributes(other);
                RecordRecentMixPartner(other);
                mixedPartners++;

                if (mixedPartners >= agitationMaxPartners)
                    break;
            }

            for (int i = 0; i < count; i++)
                NearbyParticles[i] = null;
        }

        float GetRelativeVelocityMixStrength(LiquidReaction other)
        {
            if (other == null || rb == null || other.rb == null)
                return 0f;

            float relativeSpeed = (rb.linearVelocity - other.rb.linearVelocity).magnitude;
            if (relativeSpeed < agitationVelocityThreshold)
                return 0f;

            float fullMixSpeed = Mathf.Max(
                agitationVelocityThreshold + 0.001f,
                agitationFullMixRelativeSpeed);
            float agitation = Mathf.InverseLerp(
                agitationVelocityThreshold,
                fullMixSpeed,
                relativeSpeed);

            return agitationMixSpeed * agitation;
        }

        void MixPhysicalAttributes(LiquidReaction other)
        {
            if (rb == null || other.rb == null)
                return;

            Rigidbody2D otherRb = other.rb;

            if (!HasDifferentPhysicalAttributes(other))
                return;

            float averageMass = (rb.mass + otherRb.mass) / 2f;
            rb.mass = averageMass;
            otherRb.mass = averageMass;

            float averageLinearDamping = (rb.linearDamping + otherRb.linearDamping) / 2f;
            rb.linearDamping = averageLinearDamping;
            otherRb.linearDamping = averageLinearDamping;

            float averageGravityScale = (rb.gravityScale + otherRb.gravityScale) / 2f;
            rb.gravityScale = averageGravityScale;
            otherRb.gravityScale = averageGravityScale;
        }

        bool HasDifferentPhysicalAttributes(LiquidReaction other)
        {
            if (rb == null || other == null || other.rb == null)
                return false;

            Rigidbody2D otherRb = other.rb;
            return !Mathf.Approximately(rb.mass, otherRb.mass)
                || !Mathf.Approximately(rb.linearDamping, otherRb.linearDamping)
                || !Mathf.Approximately(rb.gravityScale, otherRb.gravityScale);
        }
    }
}
