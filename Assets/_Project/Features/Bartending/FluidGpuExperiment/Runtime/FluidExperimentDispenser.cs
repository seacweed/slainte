using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum FluidExperimentDispenserState { Docked, Held, Retracting }
    public enum FluidExperimentDispenserMode { None, Ice, Hot }

    /// <summary>A hose tool owns its input/motion state; its body supplies shared solid geometry only.
    /// Keeping it out of ordinary held-body teleportation preserves physical contacts while pouring.</summary>
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
        public FluidExperimentBody Body => body;
        public FluidExperimentDispenserState State { get; private set; }
        public FluidExperimentDispenserMode Mode { get; private set; }
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
        public string BlockedReason { get; private set; }
        private float remainingMl;
        private bool dispensing;
        private Vector2 pointerOffset, target;

        private void Awake() { EnsureInitialized(); }
        private void EnsureInitialized()
        {
            if (body == null) body = GetComponent<FluidExperimentBody>();
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
            State = FluidExperimentDispenserState.Held;
            pointerOffset = point - body.Position; target = body.Position;
            ConfigureMotion(false); StopEmission();
            return true;
        }
        public void MoveToPointer(Vector2 point)
        {
            if (State == FluidExperimentDispenserState.Held)
                target = body.World.ConstrainHeldPosition(body, point - pointerOffset, 0);
        }
        public void RebasePointer(Vector2 point)
        {
            // Preserve the desired pose, including a target behind an obstacle, during OS cursor handoff.
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
            State = FluidExperimentDispenserState.Retracting;
            target = dockPosition; hasWaypoint = false; stalledTime = 0;
            retractSpeed = Mathf.Clamp(Vector2.Distance(body.Position, dockPosition) / Mathf.Max(.1f, returnDuration), 5, 35);
            ConfigureMotion(false);
        }
        public void ResetDispenser()
        {
            EnsureInitialized(); StopEmission();
            remainingMl = Mathf.Max(0, capacityMl); Mode = FluidExperimentDispenserMode.None;
            State = FluidExperimentDispenserState.Docked; target = dockPosition; hasWaypoint = false;
            spawnedIce.Clear(); SpawnedIceCount = 0; BlockedReason = null;
            body.Teleport(dockPosition, 0); ConfigureMotion(true); body.SynchronizeHistory();
            RefreshSprite(); ResetHose();
        }
        private void ConfigureMotion(bool docked)
        {
            var rb = body.Body;
            rb.bodyType = docked ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            rb.gravityScale = 0; rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0; rb.rotation = 0;
            body.World?.RefreshCollisionPairs();
        }
        private void RefreshSprite()
        {
            if (bodyRenderer != null) bodyRenderer.sprite = Mode == FluidExperimentDispenserMode.Ice ? iceSprite
                : Mode == FluidExperimentDispenserMode.Hot ? hotSprite : noneSprite;
        }
        private void OnDisable() { StopEmission(); }
        private void OnGUI()
        {
            if (!IsAvailable) return;
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
