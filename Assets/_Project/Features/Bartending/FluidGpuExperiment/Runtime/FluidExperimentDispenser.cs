using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum FluidExperimentDispenserState { Docked, Held, Retracting }
    public enum FluidExperimentDispenserMode { None, Ice, Hot }

    /// <summary>A hose tool owns its input/motion state; its body supplies shared solid geometry only.
    /// Held motion follows the pointer upright; automatic retraction never collides.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(FluidExperimentBody))]
    public sealed partial class FluidExperimentDispenser : MonoBehaviour
    {
        public FluidExperimentBody body;
        public SpriteRenderer bodyRenderer;
        public Sprite noneSprite, iceSprite, hotSprite;
        public ItemDef hotWater;
        public FluidExperimentBody icePrefab;
        public Material hoseMaterial;
        public Rect iceButtonLocal, hotButtonLocal;
        public Vector2 mouthLocal, hoseAttachmentLocal;
        public Vector2 dockPosition = new Vector2(11.6f, 7.3f), hoseAnchor = new Vector2(11.6f, 9.7f);
        [Min(0)] public float capacityMl = 5000, hotFlowMlPerSecond = 40;
        [Min(.02f)] public float iceInterval = .3f, returnDuration = .5f;
        [Min(0)] public float exitSpeed = 2.4f;
        [Range(4, 32)] public int hoseSegments = 20;
        [Min(.01f)] public float hoseWidth = .115f;
        [Range(0, .3f)] public float hoseSlackRatio = .09f;
        [Min(0)] public float hoseExtraLength = .12f;
        [Header("Hanging tool and retracting cable")]
        [Min(.1f)] public float maximumReelSpeed = 12;
        [Min(.1f)] public float reelAcceleration = 24;
        [Min(0)] public float suspensionGravityScale = 1;
        [Min(0)] public float suspensionLinearDamping = .9f;
        [Min(0)] public float suspensionAngularDamping = 1.1f;
        public FluidExperimentBody Body => body;
        public FluidExperimentDispenserState State { get; private set; }
        public FluidExperimentDispenserMode Mode { get; private set; }
        public bool CollisionSuppressed { get; private set; }
        public float RemainingMl => remainingMl;
        public bool IsAvailable => isActiveAndEnabled && body != null && body.World != null
            && body.World.Liquid != null && body.World.Liquid.CohesivePhysicsActive;
        public bool IsDispensing => dispensing && IsAvailable && State == FluidExperimentDispenserState.Held
            && Mode != FluidExperimentDispenserMode.None && remainingMl > 0;
        public Vector2 MouthWorld => body.LocalToWorld(mouthLocal);
        public Vector2 DockWorld => dockPosition;
        public Vector2 IceButtonWorld => body.LocalToWorld(iceButtonLocal.center);
        public Vector2 HotButtonWorld => body.LocalToWorld(hotButtonLocal.center);
        public LineRenderer Hose => hose;
        public DistanceJoint2D SuspensionJoint => suspensionJoint;
        public Vector2 SuspensionPointWorld => body.LocalToWorld(hoseAttachmentLocal);
        public string BlockedReason { get; private set; }
        private float remainingMl;
        private bool dispensing;
        private Vector2 pointerOffset, target;

        private void Awake() { EnsureInitialized(); }
        private void OnEnable()
        {
            if (suspensionJoint == null || restCableLength <= 0 || !IsAvailable) return;
            suspensionJoint.connectedAnchor = hoseAnchor;
            suspensionJoint.distance = restCableLength;
            suspensionJoint.enabled = true;
            ConfigureMotion(true);
        }
        private void EnsureInitialized()
        {
            if (body == null) body = GetComponent<FluidExperimentBody>();
            InitializeSuspension();
            if (hose == null) InitializeHose();
        }
        public bool Contains(Vector2 point) => IsAvailable && body.Contains(point);
        public bool TryClickMode(Vector2 point)
        {
            if (!IsAvailable) return false;
            Vector2 local = body.WorldToLocal(point);
            bool ice = iceButtonLocal.Contains(local), hot = hotButtonLocal.Contains(local);
            if (!ice && !hot) return false;
            if (State != FluidExperimentDispenserState.Docked) return true;
            FluidExperimentDispenserMode next = ice ? FluidExperimentDispenserMode.Ice : FluidExperimentDispenserMode.Hot;
            Mode = Mode == next ? FluidExperimentDispenserMode.None : next;
            StopEmission(); RefreshSprite();
            return true;
        }
        public bool TryPick(Vector2 point)
        {
            if (!IsAvailable || State != FluidExperimentDispenserState.Docked || !Contains(point)) return false;
            Vector2 grabbedLocal = body.WorldToLocal(point);
            State = FluidExperimentDispenserState.Held;
            suspensionJoint.enabled = false;
            pointerOffset = body.PointAt(grabbedLocal, Vector2.zero, 0);
            target = point - pointerOffset;
            ConfigureMotion(true); StopEmission();
            ApplyDirectPose(target); // Upright in this same input frame, even during dock swing.
            lastHeldPosition = target; heldVelocity = Vector2.zero;
            return true;
        }
        public void MoveToPointer(Vector2 point)
        {
            if (State != FluidExperimentDispenserState.Held) return;
            target = point - pointerOffset;
            ApplyDirectPose(target);
        }
        public void RebasePointer(Vector2 point)
        {
            // Preserve the grabbed point during OS cursor handoff.
            if (State == FluidExperimentDispenserState.Held) pointerOffset = point - target;
        }
        public void SetDispensing(bool value)
        {
            if (!value || !IsAvailable || State != FluidExperimentDispenserState.Held)
            { StopEmission(); return; }
            dispensing = true;
        }
        public void BeginRetract()
        {
            StopEmission();
            if (State == FluidExperimentDispenserState.Docked || body == null) return;
            if (State == FluidExperimentDispenserState.Retracting) return;
            State = FluidExperimentDispenserState.Retracting;
            target = dockPosition;
            suspensionJoint.distance = Mathf.Max(restCableLength, Vector2.Distance(hoseAnchor, SuspensionPointWorld)
                * (1 + Mathf.Clamp(hoseSlackRatio, 0, .3f)) + Mathf.Max(0, hoseExtraLength));
            reelSpeed = 0;
            retractSpeed = Mathf.Clamp((suspensionJoint.distance - restCableLength) / Mathf.Max(.2f, returnDuration), 2, Mathf.Max(2, maximumReelSpeed));
            suspensionJoint.enabled = true;
            ConfigureMotion(true);
            body.Body.linearVelocity = Vector2.ClampMagnitude(heldVelocity, maximumReelSpeed);
            body.Body.WakeUp();
        }
        public void ResetDispenser()
        {
            EnsureInitialized(); StopEmission();
            suspensionJoint.enabled = false;
            remainingMl = Mathf.Max(0, capacityMl); Mode = FluidExperimentDispenserMode.None;
            State = FluidExperimentDispenserState.Docked; target = dockPosition;
            reelSpeed = 0; heldVelocity = Vector2.zero;
            spawnedIce.Clear(); SpawnedIceCount = 0; BlockedReason = null;
            restCableLength = Mathf.Max(.12f, hoseAnchor.y - (dockPosition + body.PointAt(hoseAttachmentLocal, Vector2.zero, 0)).y);
            dockPosition = hoseAnchor - Vector2.up * restCableLength - body.PointAt(hoseAttachmentLocal, Vector2.zero, 0);
            ApplyDirectPose(dockPosition);
            suspensionJoint.anchor = hoseAttachmentLocal;
            suspensionJoint.connectedAnchor = hoseAnchor;
            suspensionJoint.distance = restCableLength;
            suspensionJoint.enabled = true;
            ConfigureMotion(true);
            lastHeldPosition = body.Position;
            RefreshSprite(); ResetHose();
        }
        private void ApplyDirectPose(Vector2 position, float angle = 0)
        {
            body.Teleport(position, angle);
            // Rigidbody interpolation otherwise displays the previous physics pose.
            body.transform.SetPositionAndRotation(new Vector3(position.x, position.y, body.transform.position.z), Quaternion.Euler(0, 0, angle));
        }
        private void ConfigureMotion(bool suppressCollisions)
        {
            CollisionSuppressed = suppressCollisions;
            var rb = body.Body;
            bool hanging = suspensionJoint != null && suspensionJoint.enabled && State != FluidExperimentDispenserState.Held;
            rb.bodyType = hanging ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
            rb.interpolation = hanging ? RigidbodyInterpolation2D.Interpolate : RigidbodyInterpolation2D.None;
            rb.gravityScale = hanging ? Mathf.Max(0, suspensionGravityScale) : 0;
            rb.constraints = hanging ? RigidbodyConstraints2D.None : RigidbodyConstraints2D.FreezeRotation;
            rb.linearDamping = hanging ? Mathf.Max(0, suspensionLinearDamping) : 0;
            rb.angularDamping = hanging ? Mathf.Max(0, suspensionAngularDamping) : 0;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0; rb.rotation = 0;
            foreach (var collider in body.solidColliders)
                if (collider != null) collider.isTrigger = suppressCollisions;
            body.SynchronizeHistory();
            body.World?.RefreshCollisionPairs();
        }
        private void RefreshSprite()
        {
            if (bodyRenderer != null) bodyRenderer.sprite = Mode == FluidExperimentDispenserMode.Ice ? iceSprite
                : Mode == FluidExperimentDispenserMode.Hot ? hotSprite : noneSprite;
        }
        private void OnDisable()
        {
            StopEmission();
            if (suspensionJoint != null) suspensionJoint.enabled = false;
            if (body == null || body.Body == null) return;
            // Component-only disable must not leave an active, intangible tool behind.
            State = FluidExperimentDispenserState.Docked;
            target = dockPosition; reelSpeed = 0; heldVelocity = Vector2.zero;
            ApplyDirectPose(dockPosition); ConfigureMotion(false);
        }
        private void OnGUI()
        {
            if (!IsAvailable || !body.World.Cocktails.showControls) return;
            Camera camera = body.World.interactor != null ? body.World.interactor.inputCamera : null;
            if (camera == null) camera = body.World.Liquid.outputCamera;
            if (camera == null) camera = Camera.main;
            if (camera == null) return;
            Vector3 p = camera.WorldToScreenPoint(body.Position + new Vector2(0, -1.65f));
            if (p.z <= 0) return;
            GUI.Label(new Rect(p.x - 90, Screen.height - p.y, 210, 40),
                Mode + " | " + remainingMl.ToString("0.#") + " / " + capacityMl.ToString("0") + " ml"
                + "\n" + (!string.IsNullOrEmpty(BlockedReason) ? BlockedReason
                    : State == FluidExperimentDispenserState.Held ? "RMB: dispense | LMB: return"
                    : State == FluidExperimentDispenserState.Docked ? "Buttons: mode | Body: pick" : "Returning"));
        }
    }
}
