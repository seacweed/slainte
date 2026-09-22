using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    [DefaultExecutionOrder(200)]
    public sealed class FluidExperimentWorld : MonoBehaviour
    {
        public FluidExperimentGpuLiquid liquid;
        public FluidExperimentInteractor interactor;
        public Collider2D[] floorColliders = new Collider2D[0];
        public int itemLayer = 29;
        public bool showControls = true;
        public bool seedLiquids = true;
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
            item.Id = nextId++;
            items.Add(item);
            foreach (Transform child in item.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = itemLayer;
            RefreshCollisionPairs();
        }
        public void Unregister(FluidExperimentBody item)
        {
            items.Remove(item);
            if (interactor != null) interactor.Forget(item);
            if (liquid != null) liquid.ReleaseOwner(item.Id);
        }
        private void Start()
        {
            started = true;
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
            foreach (FluidExperimentBody item in items) if (item != null) item.ApplyHeldPose();
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
            for (int i = 0; i < items.Count; i++)
            {
                FluidExperimentBody item = items[i];
                if (item == null) continue;
                item.CaptureMotion();
                item.Emit(dt);
            }
            if (liquid != null && liquid.IsOperational) liquid.Step(dt);
            foreach (FluidExperimentBody item in items) if (item != null) item.SynchronizeHistory();
        }
        private void OnDisable()
        {
            if (simulation != null) StopCoroutine(simulation);
            simulation = null;
        }
        public void RefreshCollisionPairs()
        {
            for (int i = 0; i < items.Count; i++)
            {
                FluidExperimentBody a = items[i];
                if (a == null) continue;
                foreach (Collider2D c in a.solidColliders)
                {
                    if (c == null || !c.enabled) continue;
                    foreach (Collider2D floor in floorColliders)
                        if (floor != null && floor.enabled) Physics2D.IgnoreCollision(c, floor, a.IsHeld);
                    for (int j = i + 1; j < items.Count; j++)
                    {
                        FluidExperimentBody b = items[j];
                        if (b == null) continue;
                        bool ignore = (a.IsHeld || b.IsHeld)
                            && a.kind != LabItemKind.Ice && b.kind != LabItemKind.Ice;
                        foreach (Collider2D other in b.solidColliders)
                            if (other != null && other.enabled) Physics2D.IgnoreCollision(c, other, ignore);
                    }
                }
            }
        }
        public FluidExperimentBody FindSwapTarget(FluidExperimentBody held, Vector2 pointer)
        {
            FluidExperimentBody best = null;
            float distance = float.MaxValue;
            foreach (FluidExperimentBody item in items)
            {
                if (item == null || item == held || item.IsHeld || item.kind == LabItemKind.Ice) continue;
                if (!item.Contains(pointer) || !item.SolidBounds.Intersects(held.SolidBounds)) continue;
                float d = (item.Position - pointer).sqrMagnitude;
                if (d < distance) { best = item; distance = d; }
            }
            return best;
        }
        public bool TrySwap(FluidExperimentBody a, FluidExperimentBody b, Vector2 origin)
        {
            if (a == null || b == null || a == b || !a.IsHeld || b.IsHeld) return false;
            Vector2 oldA = a.Position, oldB = b.Position;
            float bottomB = b.SolidBounds.min.y;
            var carriedA = CaptureIce(a);
            var carriedB = CaptureIce(b);
            a.Body.position = oldB;
            b.Body.position = origin;
            Physics2D.SyncTransforms();
            a.Body.position += Vector2.up * (bottomB - a.SolidBounds.min.y);
            LiftAboveFloor(b);
            Physics2D.SyncTransforms();
            bool valid = !OverlapsSolids(a, b, carriedA, carriedB)
                && !OverlapsSolids(b, a, carriedA, carriedB)
                && !BodiesOverlap(a, b);
            Vector2 newA = a.Position, newB = b.Position;
            a.Body.position = oldA; b.Body.position = oldB;
            Physics2D.SyncTransforms();
            if (!valid) return false;
            // Both GPU translations execute in a single kernel against the original owner IDs.
            liquid?.SwapContents(a.Id, newA - oldA, b.Id, newB - oldB);
            a.Teleport(newA, a.Angle);
            b.Teleport(newB, b.Angle);
            TranslateIce(carriedA, newA - oldA);
            TranslateIce(carriedB, newB - oldB);
            Physics2D.SyncTransforms();
            return true;
        }
        private List<FluidExperimentBody> CaptureIce(FluidExperimentBody vessel)
        {
            var result = new List<FluidExperimentBody>();
            if (!vessel.IsVessel) return result;
            foreach (FluidExperimentBody item in items)
                if (item != null && item.kind == LabItemKind.Ice && !item.IsHeld && vessel.ContainsLiquid(item.Position)) result.Add(item);
            return result;
        }
        private static void TranslateIce(List<FluidExperimentBody> ice, Vector2 delta)
        {
            foreach (FluidExperimentBody body in ice) body.Teleport(body.Position + delta, body.Angle);
        }
        public bool TryResolveRelease(FluidExperimentBody item)
        {
            Vector2 original = item.Position;
            LiftAboveFloor(item);
            Physics2D.SyncTransforms();
            // Resolve initial penetration before re-enabling collision; do not launch intersecting bodies.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                bool overlap = false;
                foreach (FluidExperimentBody other in items)
                {
                    if (other == null || other == item || other.kind == LabItemKind.Ice) continue;
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
                    liquid?.SwapContents(item.Id, delta, 0, Vector2.zero);
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
            Bounds bounds = item.SolidBounds;
            foreach (Collider2D floor in floorColliders)
            {
                if (floor == null) continue;
                Bounds f = floor.bounds;
                if (bounds.max.x < f.min.x || bounds.min.x > f.max.x) continue;
                if (bounds.min.y < f.max.y) item.Body.position += Vector2.up * (f.max.y - bounds.min.y + .005f);
            }
        }
        private bool OverlapsSolids(FluidExperimentBody body, FluidExperimentBody exclude, List<FluidExperimentBody> carryA, List<FluidExperimentBody> carryB)
        {
            foreach (FluidExperimentBody other in items)
            {
                if (other == null || other == body || other == exclude || carryA.Contains(other) || carryB.Contains(other)) continue;
                if (BodiesOverlap(body, other)) return true;
            }
            foreach (Collider2D c in body.solidColliders)
            foreach (Collider2D f in floorColliders)
                if (c != null && c.enabled && f != null && c.Distance(f).distance < -.01f) return true;
            return false;
        }
        private static bool BodiesOverlap(FluidExperimentBody a, FluidExperimentBody b)
        {
            foreach (Collider2D c in a.solidColliders)
            foreach (Collider2D d in b.solidColliders)
                if (c != null && d != null && c.enabled && d.enabled && c.Distance(d).distance < -.01f) return true;
            return false;
        }
        private void Update()
        {
            if (showControls && Input.GetKeyDown(KeyCode.R)) ResetSession();
        }
        public void ResetSession()
        {
            if (interactor != null && interactor.Held != null) interactor.ReleaseWithVelocity(Vector2.zero);
            for(int i=items.Count-1;i>=0;i--)
            {
                FluidExperimentBody item=items[i];
                if(!initialState.Exists(s=>s.body==item))
                {item.gameObject.SetActive(false);Destroy(item.gameObject);}
            }
            liquid?.ResetSimulation();
            foreach(InitialState state in initialState)
            {
                if(state.body==null)continue;
                state.body.SetHeld(false);
                state.body.Teleport(state.position,state.angle);
                state.body.SetSealed(state.closed);
                state.body.ResetSupply(state.volume,state.ice);
            }
            Physics2D.SyncTransforms();
            SeedLiquids();
        }
        private void OnGUI()
        {
            if (!showControls) return;
            GUI.Box(new Rect(12, 12, 690, 112), "PHYSICS LAB  |  independent slot-free prefabs\n"
                + "LMB: pick / place / throw    RMB + mouse Y: unlimited rotation\n"
                + "Pick / release RMB: angle 0    Slow place on an item: swap\n"
                + "C: toggle held shaker lid    R: restart sandbox\n"
                + (liquid != null && liquid.IsOperational ? "GPU PBF/XPBD active" : "GPU unavailable: " + liquid?.Error));
        }
    }
}
