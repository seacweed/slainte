using UnityEngine;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BeakerController))]
    public sealed class CobblerShakerTechniqueController : MonoBehaviour
    {
        [Header("Cobbler")]
        [SerializeField] private bool integratedStrainer = true;
        [SerializeField, Range(0.5f, 1f)] private float strainerWidthRatio = 0.9f;
        [SerializeField, Min(0.02f)] private float strainerThickness = 0.08f;

        [Header("Shake Gesture")]
        [SerializeField, Min(0.1f)] private float minimumSpeed = 3.5f;
        [SerializeField, Min(2)] private int requiredDirectionChanges = 4;
        [SerializeField, Min(0.1f)] private float gestureTimeout = 0.8f;

        private BeakerController shaker;
        private Vector3 previousPosition;
        private int previousDirection;
        private int directionChanges;
        private float lastDirectionChangeTime;
        private bool strainerConfigured;
        private IceOnlyVesselBarrier strainerBarrier;

        public bool HasIntegratedStrainer => integratedStrainer;

        private void Awake()
        {
            shaker = GetComponent<BeakerController>();
            previousPosition = transform.position;
        }

        private void OnEnable()
        {
            previousPosition = transform.position;
            strainerConfigured = false;
            ResetGesture();
        }

        private void LateUpdate()
        {
            if (shaker == null)
                shaker = GetComponent<BeakerController>();

            if (!strainerConfigured && shaker != null && shaker.LiquidTracker != null)
            {
                ConfigurePhysicalStrainer();
                strainerConfigured = true;
            }

            Vector3 currentPosition = transform.position;
            float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            Vector2 velocity = (currentPosition - previousPosition) / deltaTime;
            previousPosition = currentPosition;

            if (shaker == null || !shaker.IsPickedUp)
            {
                ResetGesture();
                return;
            }

            if (Time.unscaledTime - lastDirectionChangeTime > gestureTimeout)
                ResetGesture();

            if (velocity.magnitude < minimumSpeed)
                return;

            float dominantAxis = Mathf.Abs(velocity.x) >= Mathf.Abs(velocity.y)
                ? velocity.x
                : velocity.y;
            int direction = dominantAxis >= 0f ? 1 : -1;
            if (previousDirection != 0 && direction != previousDirection)
            {
                directionChanges++;
                lastDirectionChangeTime = Time.unscaledTime;
            }
            previousDirection = direction;

            if (directionChanges >= requiredDirectionChanges)
            {
                MarkContentsAsShaken();
                ResetGesture();
            }
        }

        public void MarkContentsAsShaken()
        {
            VesselLiquidTracker tracker = shaker != null
                ? shaker.LiquidTracker
                : GetComponent<VesselLiquidTracker>();
            if (tracker == null)
                return;

            foreach (LiquidParticleData particle in tracker.Particles)
                particle?.RecordTechnique(CocktailTechnique.Shake);
        }

        private void ConfigurePhysicalStrainer()
        {
            strainerBarrier = GetComponentInChildren<IceOnlyVesselBarrier>(true);
            if (!integratedStrainer)
            {
                if (strainerBarrier != null)
                    strainerBarrier.gameObject.SetActive(false);

                shaker.LiquidTracker.RefreshCollisionGeometry();
                return;
            }

            if (strainerBarrier == null)
            {
                GameObject barrierObject = new GameObject("__IntegratedStrainerBarrier");
                barrierObject.transform.SetParent(transform, false);
                barrierObject.layer = gameObject.layer;
                strainerBarrier = barrierObject.AddComponent<IceOnlyVesselBarrier>();
                barrierObject.AddComponent<BoxCollider2D>();
            }

            GameObject barrier = strainerBarrier.gameObject;
            barrier.SetActive(true);
            barrier.layer = gameObject.layer;
            barrier.transform.localPosition = new Vector3(
                0f,
                shaker.colliderYOffset + shaker.height * 0.5f - strainerThickness * 0.5f,
                0f);
            barrier.transform.localRotation = Quaternion.identity;
            barrier.transform.localScale = Vector3.one;

            BoxCollider2D collider = barrier.GetComponent<BoxCollider2D>();
            collider.isTrigger = false;
            collider.size = new Vector2(
                Mathf.Max(0.1f, shaker.topWidth * strainerWidthRatio),
                Mathf.Max(0.02f, strainerThickness));

            shaker.LiquidTracker.RefreshCollisionGeometry();
        }

        private void ResetGesture()
        {
            previousDirection = 0;
            directionChanges = 0;
            lastDirectionChangeTime = Time.unscaledTime;
        }
    }
}
