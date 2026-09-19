using System;
using UnityEngine;

namespace Slainte.Bartending.PhysicsLab
{
    public enum LabItemKind { Bottle, Glass, Jigger, Shaker, Spoon, IceBucket, Ice }
    [Serializable]
    public sealed class PhysicsLabHull { public Vector2[] points = Array.Empty<Vector2>(); }

    /// <summary>Opt-in, slot-free body. No legacy controllers or global registries.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody2D))]
    public sealed class PhysicsLabBody : MonoBehaviour
    {
        public LabItemKind kind;
        public string displayName;
        public ItemDef ingredient;
        public Vector2[] liquidWall = Array.Empty<Vector2>();
        public bool wallClosed;
        public PhysicsLabHull[] extraSolidHulls = Array.Empty<PhysicsLabHull>();
        public Rect[] contentRegions = Array.Empty<Rect>();
        public Vector2 mouthLocal;
        public Vector2 rotationPivotLocal;
        public Collider2D[] solidColliders = Array.Empty<Collider2D>();
        public Collider2D pickCollider;
        public GameObject capVisual;
        public Collider2D capCollider;
        public bool sealedVessel;
        [Min(0)] public float capacityMl = 700;
        [Min(0)] public float remainingMl = 700;
        [Min(0)] public float pourMlPerSecond = 20;
        public float pourStartAngle = 90;
        public float fullPourAngle = 120;
        public float exitSpeed = 2.4f;
        public PhysicsLabBody icePrefab;
        public int iceStock = 20;
        public float icePourInterval = .18f;

        public PhysicsLabWorld World { get; private set; }
        public Rigidbody2D Body { get; private set; }
        public uint Id { get; internal set; }
        public bool IsHeld { get; private set; }
        public bool IsVessel => contentRegions.Length > 0;
        public Vector2 Position => Body != null ? Body.position : (Vector2)transform.position;
        public float Angle => Body != null ? Body.rotation : transform.eulerAngles.z;
        public float HeldAngle { get; private set; }
        public Vector2 TargetPosition => IsHeld ? targetPosition : Position;
        public float TargetAngle => IsHeld ? targetAngle : Angle;
        public Vector2 PreviousPosition { get; private set; }
        public float PreviousAngle { get; private set; }
        public float StepAngle { get; private set; }
        public float PourTilt => Vector2.Angle(WorldVector(Vector2.up, Angle), Vector2.up);
        public event Action<PhysicsLabBody, bool> HeldChanged;
        private float pourCredit;
        private float iceTimer;
        private Vector2 targetPosition;
        private float targetAngle;

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            Body.bodyType = RigidbodyType2D.Dynamic;
            Body.constraints = RigidbodyConstraints2D.None;
            Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            targetPosition = Body.position;
            targetAngle = Body.rotation;
            SynchronizeHistory();
            SetSealed(sealedVessel);
        }

        private void OnEnable()
        {
            World = GetComponentInParent<PhysicsLabWorld>();
            if (World != null) World.Register(this);
        }

        private void OnDisable()
        {
            if (IsHeld) SetHeld(false);
            if (World != null) World.Unregister(this);
            World = null;
            IsHeld = false;
        }

        public void Attach(PhysicsLabWorld world)
        {
            if (World == world) return;
            if (World != null) World.Unregister(this);
            World = world;
            world.Register(this);
        }

        public void SetHeld(bool value)
        {
            if (Body == null) Body = GetComponent<Rigidbody2D>();
            IsHeld = value;
            Body.bodyType = value ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0;
            if (value)
            {
                targetPosition = Body.position;
                HeldAngle = targetAngle = Body.rotation;
            }
            else Body.WakeUp();
            World?.RefreshCollisionPairs();
            HeldChanged?.Invoke(this, value);
        }

        public void SetHeldPose(Vector2 position, float unwrappedAngle)
        {
            if (!IsHeld) return;
            targetPosition = position;
            targetAngle = unwrappedAngle;
        }

        internal void ApplyHeldPose()
        {
            if (!IsHeld) return;
            Body.position = targetPosition;
            Body.rotation = targetAngle;
            HeldAngle = targetAngle;
        }

        public void Release(Vector2 velocity, float angularVelocity)
        {
            ApplyHeldPose();
            SetHeld(false);
            Body.linearVelocity = velocity;
            Body.angularVelocity = angularVelocity;
        }

        public void Teleport(Vector2 position, float angle)
        {
            Body.position = position;
            Body.rotation = angle;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0;
            targetPosition = position;
            targetAngle = HeldAngle = angle;
            SynchronizeHistory();
        }

        public void ResetSupply(float volume, int ice)
        {
            remainingMl = volume; iceStock = ice; pourCredit = 0; iceTimer = 0;
        }

        public void SetSealed(bool value)
        {
            sealedVessel = value;
            if (capVisual != null) capVisual.SetActive(value);
            if (capCollider != null) capCollider.enabled = value;
            World?.RefreshCollisionPairs();
        }

        public Vector2 LocalToWorld(Vector2 local) => PointAt(local, Position, Angle);
        public Vector2 WorldToLocal(Vector2 point)
        {
            Vector2 rotated = Rotate(point - Position, -Angle);
            Vector3 scale = transform.lossyScale;
            return new Vector2(rotated.x / scale.x, rotated.y / scale.y);
        }
        public Vector2 PointAt(Vector2 local, Vector2 position, float angle)
        {
            Vector3 scale = transform.lossyScale;
            return position + Rotate(new Vector2(local.x * scale.x, local.y * scale.y), angle);
        }
        public static Vector2 Rotate(Vector2 p, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector2(p.x * Mathf.Cos(a) - p.y * Mathf.Sin(a), p.x * Mathf.Sin(a) + p.y * Mathf.Cos(a));
        }
        private static Vector2 WorldVector(Vector2 v, float angle) => Rotate(v, angle);
        public bool Contains(Vector2 point)
        {
            Vector2 local = WorldToLocal(point);
            if (pickCollider is BoxCollider2D box)
                return new Rect(box.offset - box.size * .5f, box.size).Contains(local);
            return pickCollider != null && pickCollider.OverlapPoint(point);
        }
        public bool ContainsLiquid(Vector2 point)
        {
            Vector2 local = WorldToLocal(point);
            foreach (Rect rect in contentRegions) if (rect.Contains(local)) return true;
            return false;
        }
        public Bounds SolidBounds
        {
            get
            {
                Bounds bounds = new Bounds(Position, Vector3.zero);
                bool found = false;
                foreach (Collider2D c in solidColliders)
                {
                    if (c == null || !c.enabled) continue;
                    if (!found) { bounds = c.bounds; found = true; }
                    else bounds.Encapsulate(c.bounds);
                }
                return bounds;
            }
        }
        public void SynchronizeHistory()
        {
            PreviousPosition = Position;
            PreviousAngle = IsHeld ? HeldAngle : Angle;
            StepAngle = 0;
        }
        internal void CaptureMotion()
        {
            StepAngle = IsHeld ? HeldAngle - PreviousAngle : Mathf.DeltaAngle(PreviousAngle, Angle);
        }

        internal void Emit(float dt)
        {
            if (World == null) return;
            if (kind == LabItemKind.Bottle && World.Liquid != null && World.Liquid.IsOperational)
            {
                float tilt = PourTilt;
                if (tilt < pourStartAngle || remainingMl <= 0) { pourCredit = 0; return; }
                float flow = Mathf.Lerp(.65f, 1, Mathf.InverseLerp(pourStartAngle, fullPourAngle, tilt));
                pourCredit += dt * pourMlPerSecond * flow;
                Vector2 direction = Rotate(Vector2.up, Angle);
                Vector2 mouth = LocalToWorld(mouthLocal);
                Vector2 velocity = MouthVelocity(dt) + direction * exitSpeed * flow;
                int count = 0;
                while (remainingMl > 0 && count++ < 64)
                {
                    float volume = Mathf.Min(World.Liquid.ParticleVolumeMl, remainingMl);
                    if (pourCredit < volume) break;
                    // Place the nozzle outside the body's solid boundary.
                    if (!World.Liquid.TryEmit(mouth + direction * World.Liquid.Radius * 1.5f,
                            velocity, ingredient, volume, 0)) break;
                    remainingMl -= volume;
                    pourCredit -= volume;
                }
            }
            if (kind == LabItemKind.IceBucket && icePrefab != null && iceStock > 0)
            {
                if (PourTilt < pourStartAngle) { iceTimer = 0; return; }
                iceTimer += dt;
                if (iceTimer < icePourInterval) return;
                iceTimer -= icePourInterval;
                Vector2 mouth = LocalToWorld(mouthLocal);
                Vector2 direction = Rotate(Vector2.up, Angle);
                PhysicsLabBody ice = Instantiate(icePrefab, mouth + direction * .3f, Quaternion.Euler(0, 0, Angle), World.transform);
                ice.Body.linearVelocity = MouthVelocity(dt) + direction * exitSpeed;
                iceStock--;
            }
        }
        private Vector2 MouthVelocity(float dt) => IsHeld
            ? (LocalToWorld(mouthLocal) - PointAt(mouthLocal, PreviousPosition, PreviousAngle)) / Mathf.Max(dt, .0001f)
            : Body.GetPointVelocity(LocalToWorld(mouthLocal));
    }
}
