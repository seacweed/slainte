using UnityEngine;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BeakerController))]
    public sealed class CobblerShakerTechniqueController : MonoBehaviour
    {
        [Header("Cobbler")]
        [SerializeField] private bool integratedStrainer = true;

        [Header("Shake Gesture")]
        [SerializeField, Min(0.1f)] private float minimumSpeed = 3.5f;
        [SerializeField, Min(2)] private int requiredDirectionChanges = 4;
        [SerializeField, Min(0.1f)] private float gestureTimeout = 0.8f;

        private BeakerController shaker;
        private Vector3 previousPosition;
        private int previousDirection;
        private int directionChanges;
        private float lastDirectionChangeTime;

        public bool HasIntegratedStrainer => integratedStrainer;

        private void Awake()
        {
            shaker = GetComponent<BeakerController>();
            previousPosition = transform.position;
        }

        private void OnEnable()
        {
            previousPosition = transform.position;
            ResetGesture();
        }

        private void LateUpdate()
        {
            if (shaker == null)
                shaker = GetComponent<BeakerController>();

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

        private void ResetGesture()
        {
            previousDirection = 0;
            directionChanges = 0;
            lastDirectionChangeTime = Time.unscaledTime;
        }
    }
}
