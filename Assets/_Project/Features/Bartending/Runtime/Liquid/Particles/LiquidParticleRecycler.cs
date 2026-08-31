using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Slainte.Bartending
{
    internal readonly struct LiquidScreenBounds
    {
        public LiquidScreenBounds(float left, float right, float bottom)
        {
            IsAvailable = true;
            Left = left;
            Right = right;
            Bottom = bottom;
        }

        public bool IsAvailable { get; }
        public float Left { get; }
        public float Right { get; }
        public float Bottom { get; }

        public bool IsOutside(Vector3 position)
        {
            return IsAvailable
                && (position.x < Left || position.x > Right || position.y < Bottom);
        }
    }

    [DisallowMultipleComponent]
    [MovedFrom(true, "", "Assembly-CSharp", "ReturnToPool")]
    public sealed class LiquidParticleRecycler : MonoBehaviour
    {
        [Header("World Bounds")]
        public float bottomLimit = -10f;
        public float topLimit = 12f;
        [Min(0f)] public float horizontalLimit = 14f;

        [Header("Spill Cleanup")]
        public bool recycleUncontainedParticles = true;
        [Min(0f)] public float settledOutsideRecycleDelay = 5f;
        [Min(0f)] public float maxOutsideVesselLifetime = 20f;
        [Min(0f)] public float settledSpeedThreshold = 0.08f;

        private LiquidParticleData particleData;
        private Rigidbody2D body;
        private float outsideVesselTimer;
        private float settledOutsideTimer;

        private void Awake()
        {
            CacheComponents();
        }

        private void OnEnable()
        {
            outsideVesselTimer = 0f;
            settledOutsideTimer = 0f;
            CacheComponents();
        }

        internal bool ShouldRecycle(LiquidScreenBounds screenBounds, float deltaTime)
        {
            Vector3 position = transform.position;
            if (position.y < bottomLimit
                || position.y > topLimit
                || (horizontalLimit > 0f && Mathf.Abs(position.x) > horizontalLimit))
            {
                return true;
            }

            if (!recycleUncontainedParticles)
                return false;

            CacheComponents();
            if (particleData != null && particleData.VesselOwner != null)
            {
                outsideVesselTimer = 0f;
                settledOutsideTimer = 0f;
                return false;
            }

            // 용기를 벗어난 입자가 왼쪽/오른쪽/아래로 화면을 이탈하면 정지 유예 없이 즉시 회수한다.
            // 위쪽은 의도적으로 제외 — 기존 topLimit 하드 경계 동작을 그대로 유지한다.
            if (screenBounds.IsOutside(position))
                return true;

            deltaTime = Mathf.Max(0f, deltaTime);
            outsideVesselTimer += deltaTime;

            float speedThreshold = Mathf.Max(0f, settledSpeedThreshold);
            bool isSettled = body == null
                || body.linearVelocity.sqrMagnitude <= speedThreshold * speedThreshold;
            settledOutsideTimer = isSettled
                ? settledOutsideTimer + deltaTime
                : 0f;

            bool settledTooLong = settledOutsideRecycleDelay > 0f
                && settledOutsideTimer >= settledOutsideRecycleDelay;
            bool outsideTooLong = maxOutsideVesselLifetime > 0f
                && outsideVesselTimer >= maxOutsideVesselLifetime;
            return settledTooLong || outsideTooLong;
        }

        private void CacheComponents()
        {
            if (particleData == null)
                particleData = GetComponent<LiquidParticleData>();

            if (body == null)
                body = GetComponent<Rigidbody2D>();
        }
    }
}
