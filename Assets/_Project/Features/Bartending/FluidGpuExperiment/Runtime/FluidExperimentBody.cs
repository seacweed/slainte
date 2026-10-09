using System;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum LabItemKind { Bottle, Glass, Jigger, Shaker, Spoon, IceBucket, Ice, Garnish }
    [Serializable]
    public sealed class FluidExperimentHull { public Vector2[] points = Array.Empty<Vector2>(); }

    /// <summary>Opt-in, slot-free body. No legacy controllers or global registries.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody2D))]
    public sealed partial class FluidExperimentBody : MonoBehaviour
    {
        public LabItemKind kind;
        public string displayName;
        public ItemDef ingredient;
        public Vector2[] liquidWall = Array.Empty<Vector2>();
        public bool wallClosed;
        public FluidExperimentHull[] extraSolidHulls = Array.Empty<FluidExperimentHull>();
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
        public FluidExperimentBody icePrefab;
        public int iceStock = 20;
        public float icePourInterval = .18f;
        [Tooltip("Retained for older comparison assets. F flow-following garnishes do not push liquid.")]
        [Range(0, 1)] public float garnishLiquidMotionTransfer = .2f;

        public FluidExperimentWorld World { get; private set; }
        public Rigidbody2D Body { get; private set; }
        public uint Id { get; internal set; }
        public bool IsHeld { get; private set; }
        // Physical contents share containment/contact policy, not ingredient or ice-effect identity.
        public bool IsLooseSolid => kind == LabItemKind.Ice || kind == LabItemKind.Garnish;
        public bool CanBePicked => !IsLooseSolid || !hasBeenPlaced;
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
        public event Action<FluidExperimentBody, bool> HeldChanged;
        private float pourCredit;
        private float iceTimer;
        private bool hasBeenPlaced;
        private Vector2 targetPosition;
        private float targetAngle;
        // Accumulate until the physics tick consumes it, including a return that
        // starts or finishes between ticks. Collision sweeps still use StepAngle.
        private float automaticPoseAngle;
        private Vector2 automaticPoseTranslation;

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
            ApplyCollisionProfile();
            InitializeShakerParts();
            RefreshIceStockVisual();
            SetSealed(sealedVessel);
        }

        private void OnEnable()
        {
            World = GetComponentInParent<FluidExperimentWorld>();
            if (World != null) World.Register(this);
            ResumeShakerParts();
        }

        private void OnDisable()
        {
            SuspendShakerParts();
            EndPourStream();
            ClearIceContainer();
            if (IsHeld) SetHeld(false);
            if (World != null) World.Unregister(this);
            World = null;
            IsHeld = false;
        }

        public void Attach(FluidExperimentWorld world)
        {
            if (World == world) return;
            if (World != null) World.Unregister(this);
            World = world;
            world.Register(this);
        }

        public void SetHeld(bool value)
        {
            if (Body == null) Body = GetComponent<Rigidbody2D>();
            // Capture contents before enabling the held/external collision policy.
            if (value && !IsHeld) World?.RefreshIceContainment();
            if (value && IsLooseSolid) ClearIceContainer();
            IsHeld = value;
            UpdateGarnishHoldCollision();
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
            targetPosition = UsesLiquidGarnishMotion ? position
                : World != null ? World.ConstrainHeldPosition(this, position, unwrappedAngle) : position;
            targetAngle = unwrappedAngle;
        }

        internal void RestorePickupPose(Vector2 position, float angle)
        {
            // Only picking up a tilted F glass changes the liquid's reference frame.
            // Manual rotation and the right-button upright return keep normal fluid physics.
            if (!IsHeld || kind != LabItemKind.Glass || World?.Liquid == null
                || !World.Liquid.CohesivePhysicsActive
                || Mathf.Abs(Mathf.DeltaAngle(HeldAngle, angle)) < .00001f)
            { RestoreHeldPose(position, angle); return; }

            ApplyHeldPose();
            Vector2 from = Position;
            float turn = Mathf.DeltaAngle(HeldAngle, angle);
            float previousAutomaticAngle = automaticPoseAngle;
            Vector2 previousAutomaticTranslation = automaticPoseTranslation;
            RestoreHeldPose(position, angle);
            World.Liquid.CarryPickupContents(Id, from, Position, turn);
            // RestoreHeldPose already rebased whole turns. Remove just this automatic
            // rotation from swept-wall history, preserving any genuine pending motion.
            RebasePickupHistory(from, Position, turn);
            automaticPoseAngle = previousAutomaticAngle;
            automaticPoseTranslation = Rotate(previousAutomaticTranslation, turn);
            foreach (FluidExperimentBody ice in World.Items)
            {
                if (ice == null || ice.iceContainer != this || ice.IsHeld || !ice.Body.simulated
                    || ice.UsesLiquidGarnishMotion) continue;
                // ApplyHeldPose also carried contained ice. Its swept GPU boundary must
                // not inject the same artificial pickup rotation back into the liquid.
                ice.RebasePickupHistory(from, Position, turn);
                ice.previousIcePosition = Position + Rotate(ice.previousIcePosition - from, turn);
                ice.Body.linearVelocity = Rotate(ice.Body.linearVelocity, turn);
            }
        }

        private void RebasePickupHistory(Vector2 from, Vector2 to, float turn)
        {
            PreviousPosition = to + Rotate(PreviousPosition - from, turn);
            PreviousAngle += turn;
        }

        internal void RestoreHeldPose(Vector2 position, float angle)
        {
            if (!IsHeld) return;
            ApplyHeldPose();
            // Automatic restoration takes the shortest arc, without unwinding completed turns.
            // Rebase only whole turns so the GPU still sees any motion pending this physics tick.
            PreviousAngle += angle - (HeldAngle + Mathf.DeltaAngle(HeldAngle, angle));
            automaticPoseAngle += Mathf.DeltaAngle(HeldAngle, angle);
            // Compare the same pointer target at the two orientations. Any extra
            // wall/ceiling correction is caused by restoration, not by the mouse.
            Vector2 previousOrientationPosition = UsesLiquidGarnishMotion ? position : World != null
                ? World.ConstrainHeldPosition(this, position, HeldAngle) : position;
            SetHeldPose(position, angle);
            automaticPoseTranslation += targetPosition - previousOrientationPosition;
            ApplyHeldPose();
        }

        internal void ApplyHeldPose()
        {
            if (!IsHeld) return;
            if ((Position - targetPosition).sqrMagnitude < 1e-12f
                && Mathf.Abs(Mathf.DeltaAngle(Angle, targetAngle)) < .00001f)
            {
                HeldAngle = targetAngle; // Keep unwrapped input history without rewriting an unchanged native pose.
                return;
            }
            TransportContainedIce(Position, HeldAngle, targetPosition, targetAngle);
            Body.position = targetPosition;
            Body.rotation = targetAngle;
            HeldAngle = targetAngle;
        }

        public void Release(Vector2 velocity, float angularVelocity)
        {
            ApplyHeldPose();
            ReleaseContainedIceVelocity(velocity, angularVelocity);
            SetHeld(false);
            MarkPlaced();
            Body.linearVelocity = velocity;
            Body.angularVelocity = angularVelocity;
        }

        internal void MarkPlaced()
        {
            if (IsLooseSolid) hasBeenPlaced = true;
        }
        internal void ResetPickupState() => hasBeenPlaced = false;

        public void Teleport(Vector2 position, float angle)
        {
            EndPourStream();
            if (IsLooseSolid) ClearIceContainer();
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
            EndPourStream();
            remainingMl = volume; iceStock = ice; pourCredit = 0; iceTimer = 0;
            RefreshIceStockVisual();
        }

        public void SetSealed(bool value)
        {
            sealedVessel = value;
            ApplyShakerSeal(value);
            World?.RefreshCollisionPairs();
        }

        partial void InitializeShakerParts();
        partial void ApplyShakerSeal(bool value);
        partial void SuspendShakerParts();
        partial void ResumeShakerParts();

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
            if (kind == LabItemKind.Garnish && collisionProfile != null)
            {
                foreach (var hull in collisionProfile.solids)
                    if (FluidExperimentCollisionProfile.Contains(hull.points, local)) return true;
                return false;
            }
            if (pickCollider is BoxCollider2D box)
                return new Rect(box.offset - box.size * .5f, box.size).Contains(local);
            return pickCollider != null && pickCollider.OverlapPoint(point);
        }
        public bool ContainsLiquid(Vector2 point)
        {
            Vector2 local = WorldToLocal(point);
            if (collisionProfile != null) return FluidExperimentCollisionProfile.Contains(LiquidInteriorPath, local);
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
            automaticPoseAngle = 0;
            automaticPoseTranslation = Vector2.zero;
            IceRecoveryTranslation = Vector2.zero;
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
                EmitBottle(dt);
            }
            if (kind == LabItemKind.IceBucket && icePrefab != null && iceStock > 0)
            {
                if (PourTilt < pourStartAngle) { iceTimer = 0; return; }
                iceTimer += dt;
                if (iceTimer < icePourInterval) return;
                iceTimer -= icePourInterval;
                Vector2 mouth = LocalToWorld(mouthLocal);
                Vector2 direction = Rotate(Vector2.up, Angle);
                FluidExperimentBody ice = Instantiate(icePrefab, mouth + direction * .3f, Quaternion.Euler(0, 0, Angle), World.transform);
                ice.Body.linearVelocity = MouthVelocity(dt) + direction * exitSpeed;
                ice.MarkPlaced(); // Poured stock is already placed, not a fresh hand pickup.
                iceStock--;
                RefreshIceStockVisual();
            }
        }
        internal Vector2 EmissionPointVelocity(Vector2 arm, float dt)
        {
            float inverseDt = 1 / Mathf.Max(dt, .0001f);
            return (Position - PreviousPosition - automaticPoseTranslation) * inverseDt
                + new Vector2(-arm.y, arm.x) * ((StepAngle - automaticPoseAngle) * Mathf.Deg2Rad * inverseDt);
        }

        private void Update()
        {
            // Infinite supplies must not keep sending already discarded pieces to the GPU.
            if (kind != LabItemKind.Garnish || IsHeld || World == null || World.Liquid == null
                || World.Liquid.settings == null || Position.y >= World.Liquid.settings.gpuLiquidWorldMin.y - 1) return;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
        private Vector2 MouthVelocity(float dt) => EmissionPointVelocity(LocalToWorld(mouthLocal) - Position, dt);
    }
}
