using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    [DefaultExecutionOrder(200)]
    public sealed partial class FluidExperimentWorld : MonoBehaviour
    {
        public FluidExperimentGpuLiquid liquid;
        public FluidExperimentInteractor interactor;
        public Collider2D[] floorColliders = new Collider2D[0];
        public int itemLayer = 29;
        public bool showControls = true;
        public bool seedLiquids = true;
        public bool SimulationSuspended;
        [System.NonSerialized] public float SpawnedIceScale = 1;
        public FluidExperimentGpuLiquid Liquid => liquid;
        public IReadOnlyList<FluidExperimentBody> Items => items;
        private readonly List<FluidExperimentBody> items = new List<FluidExperimentBody>();
        private uint nextId = 1;
        private Coroutine simulation;
        private bool started;
        private readonly List<InitialState> initialState = new List<InitialState>();
        private struct InitialState
        {
            public FluidExperimentBody body;
            public Vector2 position;
            public float angle, volume;
            public int ice;
            public bool closed;
        }

        public void Register(FluidExperimentBody item)
        {
            if (items.Contains(item)) return;
            if (item.kind == LabItemKind.Ice)
            {
                // Re-registering a stored or re-enabled cube must not shrink it again.
                item.transform.localScale *= SpawnedIceScale / item.AppliedWorldIceScale;
                item.AppliedWorldIceScale = SpawnedIceScale;
            }
            item.Id = nextId++;
            items.Add(item);
            RememberVisualItem(item);
            foreach (Transform child in item.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = itemLayer;
            RefreshCollisionPairs();
        }
        public void Unregister(FluidExperimentBody item)
        {
            items.Remove(item);
            foreach (FluidExperimentBody other in items)
                if (other != null && other.ContainingVesselId == item.Id) other.ClearIceContainer();
            RefreshCollisionPairs();
            if (interactor != null) interactor.Forget(item);
            if (liquid != null) liquid.ReleaseOwner(item.Id);
        }
        private void Start()
        {
            started = true;
            InitializeDispenser();
            foreach (FluidExperimentBody body in GetComponentsInChildren<FluidExperimentBody>()) body.Attach(this);
            if (liquid != null) liquid.Initialize(this);
            foreach (FluidExperimentBody item in items)
                initialState.Add(new InitialState { body=item,position=item.Position,angle=item.Angle,
                    volume=item.remainingMl,ice=item.iceStock,closed=item.sealedVessel });
            SeedLiquids();
            simulation = StartCoroutine(SimulateAfterPhysics());
        }
        private void OnEnable()
        {
            if (!started) return;
            if (liquid != null) liquid.Initialize(this);
            simulation = StartCoroutine(SimulateAfterPhysics());
        }
        private void SeedLiquids()
        {
            if (seedLiquids && liquid != null && liquid.IsOperational)
            {
                ItemDef ingredient = null;
                foreach (FluidExperimentBody item in items) if (item.ingredient != null) { ingredient = item.ingredient; break; }
                foreach (FluidExperimentBody item in items)
                    if (item.kind == LabItemKind.Glass || item.kind == LabItemKind.Shaker)
                        liquid.Fill(item, ingredient, 60);
            }
        }
        private void FixedUpdate()
        {
            if (SimulationSuspended) return;
            if (Dispenser != null) Dispenser.StepMotion(Time.fixedDeltaTime);
            RefreshIceContainment();
            UpdateShardContactPairs();
            foreach (FluidExperimentBody item in items) if (item != null) item.StepHeldPose();
            foreach (FluidExperimentBody item in items) if (item != null) item.StepGarnishMotion(Time.fixedDeltaTime);
            CaptureGlassBreakageMotion();
        }
        private IEnumerator SimulateAfterPhysics()
        {
            var wait = new WaitForFixedUpdate();
            while (true)
            {
                yield return wait; // Sample completed Rigidbody2D physics, not rendered/interpolated Transforms.
                TickLiquid(Time.fixedDeltaTime);
            }
        }
        public void TickLiquid(float dt)
        {
            if (SimulationSuspended) return;
            foreach (FluidExperimentBody item in items) if (item != null) item.CompleteHeldMotion();
            ProcessGlassBreakage();
            if (Dispenser != null && Dispenser.isActiveAndEnabled) Dispenser.Tick(dt);
            RefreshIceContainment();
            foreach (FluidExperimentBody item in items) if (item != null) item.ResolveCompletedGarnishContacts();
            for (int i = 0; i < items.Count; i++)
            {
                FluidExperimentBody item = items[i];
                if (item == null) continue;
                item.CaptureMotion();
                item.Emit(dt);
            }
            if (liquid != null && liquid.IsOperational) liquid.Step(dt);
            ObserveCocktailTechniques(dt);
            foreach (FluidExperimentBody item in items) if (item != null) item.SynchronizeHistory();
            SynchronizeShardHistory();
        }
        private void OnDisable()
        {
            if (simulation != null) StopCoroutine(simulation);
            simulation = null;
        }
        public void RefreshCollisionPairs()
        {
            RefreshGlassShardContacts();
            for (int i = 0; i < items.Count; i++)
            {
                FluidExperimentBody a = items[i];
                if (a == null) continue;
                foreach (Collider2D c in a.solidColliders)
                {
                    if (c == null || !c.enabled) continue;
                    foreach (Collider2D floor in floorColliders)
                        if (floor != null && floor.enabled && Physics2D.GetIgnoreCollision(c, floor) != (a.SuppressHeldSolidContacts || a.IsLooseSolid))
                            Physics2D.IgnoreCollision(c, floor, a.SuppressHeldSolidContacts || a.IsLooseSolid);
                    for (int j = i + 1; j < items.Count; j++)
                    {
                        FluidExperimentBody b = items[j];
                        if (b == null) continue;
                        bool sameContents = a.ContainingVesselId == b.Id || b.ContainingVesselId == a.Id
                            || (a.ContainingVesselId != 0 && a.ContainingVesselId == b.ContainingVesselId);
                        bool ignore = (a.SuppressHeldSolidContacts || b.SuppressHeldSolidContacts) && !sameContents;
                        // Contents follow the same external isolation as their held container.
                        if (!sameContents && (a.IsInHeldVessel || b.IsInHeldVessel)) ignore = true;
                        // Docked/returning dispensers retain native contacts. Held dispenser
                        // colliders are triggers, which suppress contacts with every solid.
                        if (a.kind == LabItemKind.Dispenser || b.kind == LabItemKind.Dispenser) ignore = false;
                        foreach (Collider2D other in b.solidColliders)
                            if (other != null && other.enabled && Physics2D.GetIgnoreCollision(c, other) != ignore)
                                Physics2D.IgnoreCollision(c, other, ignore);
                    }
                }
            }
            foreach (var item in items)
                if (item != null && item.kind == LabItemKind.Shaker)
                {
                    item.ShakerCap?.RefreshCollisionPairs();
                    item.ShakerStrainer?.RefreshCollisionPairs();
                }
        }
        public FluidExperimentBody FindSwapTarget(FluidExperimentBody held, Vector2 pointer)
        {
            UpdateVisualSorting();
            FluidExperimentBody best = null;
            float distance = float.MaxValue;
            long bestVisualKey = long.MinValue;
            foreach (FluidExperimentBody item in items)
            {
                if (item == null || item == held || item.IsHeld || item.IsLooseSolid || item.kind == LabItemKind.Dispenser) continue;
                if (!item.Contains(pointer) || !item.SolidBounds.Intersects(held.SolidBounds)) continue;
                float d = (item.Position - pointer).sqrMagnitude;
                long key = ObjectSortingActive ? VisualSortKey(item) : 0;
                if (best == null || key > bestVisualKey || (key == bestVisualKey && d < distance))
                { best = item; distance = d; bestVisualKey = key; }
            }
            return best;
        }
        public bool TryResolveRelease(FluidExperimentBody item, FluidExperimentBody nextHeld = null)
        {
            Physics2D.SyncTransforms();
            if (item.UsesLiquidGarnishMotion) return item.TryResolveGarnishRelease();
            Vector2 original = item.Position;
            LiftAboveFloor(item);
            Physics2D.SyncTransforms();
            // Resolve initial penetration before re-enabling collision; do not launch intersecting bodies.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                bool overlap = false;
                foreach (FluidExperimentBody other in items)
                {
                    if (other == null || other == item || other == nextHeld || other.IsHeld
                        || other.IsLooseSolid) continue;
                    foreach (Collider2D c in item.solidColliders)
                    foreach (Collider2D d in other.solidColliders)
                    {
                        if (c == null || d == null || !c.enabled || !d.enabled) continue;
                        ColliderDistance2D distance = c.Distance(d);
                        if (!distance.isOverlapped || distance.distance >= -.002f) continue;
                        item.Body.position += distance.normal * (distance.distance - .005f);
                        Physics2D.SyncTransforms(); overlap = true;
                    }
                }
                if (!overlap)
                {
                    Vector2 delta = item.Position - original;
                    liquid?.TranslateContentsPreservingMotion(item.Id, delta);
                    item.TranslateContainedSolids(delta);
                    item.Teleport(item.Position, item.Angle);
                    return true;
                }
            }
            item.Body.position = original;
            Physics2D.SyncTransforms();
            return false;
        }
        private void LiftAboveFloor(FluidExperimentBody item)
        {
            if (item.IsLooseSolid) return;
            Bounds bounds = item.SolidBounds;
            foreach (Collider2D floor in floorColliders)
            {
                if (floor == null) continue;
                Bounds f = floor.bounds;
                if (bounds.max.x < f.min.x || bounds.min.x > f.max.x) continue;
                if (bounds.min.y < f.max.y) item.Body.position += Vector2.up * (f.max.y - bounds.min.y + .005f);
            }
        }
        private void Update()
        {
            if (showControls && Input.GetKeyDown(KeyCode.R)) ResetSession();
        }
        public void ResetSession()
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.WorldReset);
            if (interactor != null) interactor.ReleaseWithVelocity(Vector2.zero);
            ResetGlassBreakage();
            for(int i=items.Count-1;i>=0;i--)
            {
                FluidExperimentBody item=items[i];
                if(!initialState.Exists(s=>s.body==item))
                {item.gameObject.SetActive(false);Destroy(item.gameObject);}
            }
            liquid?.ResetSimulation();
            foreach(InitialState state in initialState)
            {
                if(state.body==null || state.body.kind == LabItemKind.Dispenser)continue;
                state.body.SetHeld(false);
                state.body.ResetPickupState();
                state.body.Teleport(state.position,state.angle);
                state.body.SetSealed(state.closed);
                state.body.ResetSupply(state.volume,state.ice);
            }
            Physics2D.SyncTransforms();
            if (Dispenser != null) Dispenser.ResetDispenser();
            SeedLiquids();
            ResetVisualSorting();
        }
        private void OnGUI()
        {
            if (!showControls) return;
            GUI.Box(new Rect(12, 12, 690, 112), "FLUID SANDBOX  |  independent prefabs\n"
                + "LMB: pick / place / throw    RMB: rotate / dispense / stir\n"
                + "Click on an item: release/throw held, pick target\n"
                + "Click shaker cap/strainer to detach    C: cycle closure    R: restart sandbox\n"
                + (liquid != null && liquid.IsOperational ? "GPU cohesive liquid active" : "GPU unavailable: " + liquid?.Error));
        }
    }
}
