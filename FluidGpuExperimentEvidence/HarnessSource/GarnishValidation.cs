using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Slainte.Bartending.FluidGpuExperiment;

public static class ExperimentGarnishValidation
{
    private const string Key = "FluidExperiment.GarnishValidation";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    [InitializeOnLoadMethod] private static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key); new GameObject("GarnishValidation").AddComponent<ExperimentGarnishValidationRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

public sealed class ExperimentGarnishValidationRunner : MonoBehaviour
{
    private const float Dt = .02f;
    private readonly List<string> report = new List<string>(), errors = new List<string>();
    private readonly List<FluidExperimentBody> pieces = new List<FluidExperimentBody>();
    private FluidExperimentComparison comparison;
    private FluidExperimentWorld world;
    private FluidExperimentGpuLiquid gpu;
    private FluidExperimentInteractor hand;
    private FluidExperimentBody glass;
    private FluidExperimentGarnishSource[] sources;
    private bool finished;
    private IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        float deadline = Time.realtimeSinceStartup + 30;
        while (comparison != null && !comparison.Ready && Time.realtimeSinceStartup < deadline) yield return null;
        var oldMode = Physics2D.simulationMode; Vector2 oldGravity = Physics2D.gravity;
        try
        {
            Check(Application.isBatchMode && comparison != null && comparison.Ready, "Hidden GPU batch initializes without desktop input");
            Check(comparison.ActiveMode == FluidExperimentMode.FCoherentLiquid, "Authored default remains F");
            world = comparison.World; gpu = comparison.Gpu; hand = world.interactor;
            comparison.automaticScenario = false; comparison.showControls = false;
            world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
            sources = world.GetComponentsInChildren<FluidExperimentGarnishSource>();
            Check(sources.Length == 2 && sources.Select(s => s.garnishPrefab).Distinct().Count() == 2, "Scene has two distinct authored supplies and piece prefabs");
            Capture("Garnish-Manual", new Vector2(0, 2), 8, 1280, 720);
            Capture("Garnish-Supplies", new Vector2(8.65f, 3.4f), 1.6f, 960, 640);
            InputChecks();
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = new Vector2(0, -9.81f);
            glass = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
            foreach (bool held in new[] { false, true }) Settling(held);
            foreach (var source in sources)
            {
                Check(Mathf.Abs(source.garnishPrefab.garnishLiquidMotionTransfer - .2f) < .00001f, source.name + " authors gentle F liquid response");
                var authoredBody = source.garnishPrefab.GetComponent<Rigidbody2D>();
                Check(authoredBody.linearDamping == 4 && authoredBody.angularDamping == 4 && authoredBody.mass == .01f,
                    source.name + " authors peel damping without changing mass");
                float previous = MovingContact(source, 1);
                float gentle = MovingContact(source, .2f);
                Check(gentle < previous * .5f, source.name + " moving contact reduces peak liquid speed by at least half");
                ResponseGeometry(source);
                ResponseGeometry(source, 372);
                var oldImpact = DropImpact(source, 1); var gentleImpact = DropImpact(source, .2f);
                Check(gentleImpact.x < oldImpact.x * .8f, source.name + " actual free drop reduces peak liquid energy by at least 20 percent");
                Check(gentleImpact.y + .001f >= oldImpact.y, source.name + " gentle drop does not increase discarded liquid");
            }
            foreach (var mode in new[] { FluidExperimentMode.FCoherentLiquid, FluidExperimentMode.DImprovedSurface })
                SurfaceMask(mode);
            ClearPieces();
            comparison.ResetComparison(); world.enabled = false; hand.enabled = false;
            Check(sources.All(s => s != null && s.isActiveAndEnabled && s.Contains(s.transform.position)), "Reset preserves both fixed supplies");
            Check(world.Items.All(b => b.kind != LabItemKind.Garnish) && hand.Held == null, "Reset removes generated pieces and clears hand");
            foreach (var mode in new[] { FluidExperimentMode.DImprovedSurface, FluidExperimentMode.FCoherentLiquid })
            {
                comparison.SwitchMode(mode); world.enabled = false; hand.enabled = false;
                Check(hand.PickAt(sources[0].transform.position) && hand.Held.kind == LabItemKind.Garnish, mode + " retains functional click supply");
                comparison.ResetComparison();
                Check(hand.Held == null && world.Items.All(b => b.kind != LabItemKind.Garnish), mode + " resets held generated garnish");
            }
            Check(errors.Count == 0, "No Unity errors, assertions or exceptions");
            Finish(true);
        }
        catch (Exception ex) { report.Add(ex.ToString()); Finish(false); }
        finally { Physics2D.simulationMode = oldMode; Physics2D.gravity = oldGravity; }
    }

    private void InputChecks()
    {
        var oldMouse = Mouse.current; var oldKeyboard = Keyboard.current;
        var oldUpdate = InputSystem.settings.updateMode; var oldBackground = InputSystem.settings.backgroundBehavior;
        var oldEditor = InputSystem.settings.editorInputBehaviorInPlayMode; bool oldRun = Application.runInBackground;
        Rect oldBlock = hand.pointerBlockRect;
        var mouse = InputSystem.AddDevice<Mouse>("GarnishVirtualMouse");
        var keyboard = InputSystem.AddDevice<Keyboard>("GarnishVirtualKeyboard");
        var update = typeof(FluidExperimentInteractor).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        var inputUpdate = typeof(InputSystem).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(InputUpdateType) }, null);
        void Frame(Vector2 point, bool left)
        {
            Vector2 screen = hand.inputCamera.WorldToScreenPoint(point);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left, left));
            inputUpdate.Invoke(null, new object[] { InputUpdateType.Manual }); mouse.MakeCurrent(); keyboard.MakeCurrent(); update.Invoke(hand, null);
        }
        void Click(Vector2 point) { Frame(point, false); Frame(point, true); }
        try
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground = true; hand.pointerBlockRect = default;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            foreach (var source in sources)
            {
                var jar = source.transform.position;
                var sprite = source.garnishPrefab.GetComponentInChildren<SpriteRenderer>().sprite;
                Check(sprite != null && source.GetComponent<SpriteRenderer>().sprite != null, source.name + " has imported non-null source and piece sprites");
                Check(source.GetComponent<Rigidbody2D>() == null && source.pickCollider.isTrigger, source.name + " is a fixed click supply without solid interior");
                Check(!source.Contains(source.transform.TransformPoint(new Vector2(.64f, 1.2f))), source.name + " transparent margins do not dispense");
                int count = world.Items.Count;
                Click(jar);
                var piece = hand.Held;
                Check(piece != null && piece.kind == LabItemKind.Garnish && world.Items.Count == count + 1, source.name + " actual left-click creates and holds exactly one piece");
                Check(piece.GetComponentInChildren<SpriteRenderer>().sprite == sprite, source.name + " dispenses its matching sprite");
                update.Invoke(hand, null); Frame(jar, true);
                Check(world.Items.Count == count + 1, "Repeated Update and held mouse button cannot duplicate spawn");
                Vector2 move = (Vector2)jar + new Vector2(-1, .6f);
                Frame(move, false); world.SendMessage("FixedUpdate");
                Check(Vector2.Distance(piece.Position, move) < .002f, "Held garnish follows the cursor at its centered pivot");
                Click(jar);
                Check(hand.Held == null && world.Items.Count == count + 1 && !piece.IsHeld, "Second click over a jar drops only; no automatic refill or jar pickup");
                Check(piece.Body.bodyType == RigidbodyType2D.Dynamic, "Released garnish becomes dynamic");
                var placed = piece;
                Check(!placed.CanBePicked && !hand.Pick(placed, placed.Position), "Placed garnish rejects direct pickup");
                Click(piece.LocalToWorld(piece.collisionProfile.solids[0].points.Aggregate(Vector2.zero, (a,b) => a+b) / piece.collisionProfile.solids[0].points.Length));
                piece = hand.Held;
                Check(piece != null && piece != placed && world.Items.Count == count + 2,
                    "Clicking a used piece over a supply dispenses one new piece instead of picking the old one");
                var receiver = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
                Click(receiver.LocalToWorld(receiver.collisionProfile.InteriorBounds.center));
                world.RefreshIceContainment();
                Check(hand.Held == null && piece.ContainingVesselId == receiver.Id, "Clicking inside a glass places garnish without automatically picking up the glass");
                Check(!piece.CanBePicked && hand.PickAt(piece.Position) && hand.Held == receiver,
                    "Placed garnish is skipped so the receiving glass remains pickable");
                hand.ReleaseWithVelocity(Vector2.zero);
                Check(hand.Pick(receiver, receiver.Position), "Glass can still be picked repeatedly");
                hand.ReleaseWithVelocity(Vector2.zero);
                Check(source.TryDispense(hand, jar), "Supply dispenses another fresh garnish after placement");
                var thrown = hand.Held;
                hand.ReleaseWithVelocity(new Vector2(.3f, .2f), 25);
                Check((thrown.Body.linearVelocity - new Vector2(.3f,.2f)).magnitude < .001f && Mathf.Abs(thrown.Body.angularVelocity - 25) < .001f
                    && !thrown.CanBePicked && !hand.Pick(thrown, thrown.Position), "Explicit throw preserves motion and permanently closes pickup");
                foreach (var used in new[] { placed, piece, thrown }) { used.gameObject.SetActive(false); Destroy(used.gameObject); }
                Check(source.transform.position == jar, "Supply stays at its authored position");
            }
            var bucket = world.Items.First(b => b.kind == LabItemKind.IceBucket);
            var freshIce = Spawn(bucket.icePrefab, new Vector2(-6,5));
            Physics2D.SyncTransforms();
            Click(freshIce.Position);
            Check(hand.Held == freshIce, "A fresh ice piece permits its first pickup");
            Click(new Vector2(-6,5));
            Check(hand.Held == null && !freshIce.CanBePicked && !hand.Pick(freshIce,freshIce.Position), "Placed ice rejects direct pickup");
            Click(freshIce.Position);
            Check(hand.Held == null, "Placed ice rejects subsequent actual clicks");
            freshIce.Teleport(new Vector2(-6,5.5f),0); freshIce.gameObject.SetActive(false); freshIce.gameObject.SetActive(true);
            Check(!freshIce.CanBePicked && !hand.Pick(freshIce,freshIce.Position), "Teleport and disable/enable cannot reset placed state");
            freshIce.gameObject.SetActive(false); Destroy(freshIce.gameObject); pieces.Clear();
            Vector2 bucketStart = bucket.Position; float bucketAngle = bucket.Angle; int stock = bucket.iceStock;
            bucket.Teleport(new Vector2(30,30),180); bucket.iceStock = 1;
            var beforeIce = new HashSet<FluidExperimentBody>(world.Items);
            typeof(FluidExperimentBody).GetMethod("Emit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bucket,new object[] {1f});
            var pouredIce = world.Items.Single(b => !beforeIce.Contains(b) && b.kind == LabItemKind.Ice);
            Check(bucket.iceStock == 0 && !pouredIce.CanBePicked && !hand.Pick(pouredIce,pouredIce.Position), "Bucket-poured ice is already placed and cannot be picked");
            pouredIce.gameObject.SetActive(false); Destroy(pouredIce.gameObject);
            bucket.Teleport(bucketStart,bucketAngle); bucket.ResetSupply(bucket.remainingMl,stock);
            // Exercise the actual screen exclusion path before any source mutation.
            Vector2 blocked = hand.inputCamera.WorldToScreenPoint(sources[0].transform.position);
            hand.pointerBlockRect = new Rect(blocked.x - 10, Screen.height - blocked.y - 10, 20, 20);
            int before = world.Items.Count; Click(sources[0].transform.position);
            Check(hand.Held == null && world.Items.Count == before, "UI-covered supply clicks do not create garnish");
        }
        finally
        {
            hand.ReleaseWithVelocity(Vector2.zero); hand.pointerBlockRect = oldBlock;
            InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(keyboard);
            if (oldMouse != null && oldMouse.added) oldMouse.MakeCurrent(); if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
            InputSystem.settings.updateMode = oldUpdate; InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditor; Application.runInBackground = oldRun;
        }
    }
    private void ClearPieces()
    {
        hand.ReleaseWithVelocity(Vector2.zero);
        foreach (var b in pieces) if (b != null) { b.gameObject.SetActive(false); Destroy(b.gameObject); }
        pieces.Clear();
    }
    private void Prepare()
    {
        ClearPieces();
        foreach (var b in world.Items.ToArray()) { b.SetHeld(false); b.Teleport(new Vector2(-60 - b.Id * 3, 20), 0); b.Body.simulated = false; b.pourMlPerSecond = 0; }
        gpu.ResetSimulation();
        // Enabling simulation restores the native pose from the Transform. Enable
        // first, then teleport, so the preceding inverted fixture cannot return.
        glass.Body.simulated = true; glass.Body.bodyType = RigidbodyType2D.Kinematic;
        glass.Teleport(new Vector2(0, 1.5f), 0); glass.SetSealed(false);
        // Synchronous test cases share one rendered frame. Flush every reset pose
        // so a dirty interpolated Transform cannot restore the previous inverted glass.
        foreach (var b in world.Items) b.transform.SetPositionAndRotation(b.Position, Quaternion.Euler(0,0,b.Angle));
        Physics2D.SyncTransforms();
        foreach (var b in world.Items) b.SynchronizeHistory();
    }
    private FluidExperimentBody Spawn(FluidExperimentBody prefab, Vector2 position)
    {
        var b = Instantiate(prefab, position, Quaternion.identity, world.transform); b.Teleport(position, 0); pieces.Add(b); return b;
    }
    private void Step()
    {
        world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms();
        if (!Physics2D.Simulate(Dt)) throw new Exception("Physics2D step rejected"); world.TickLiquid(Dt);
    }
    private float Clearance(FluidExperimentBody b, Vector2 p) => b.SolidClearance(FluidExperimentBody.Rotate(p - b.Position, -b.Angle));
    private void Settling(bool held)
    {
        Prepare(); Rect r = glass.collisionProfile.InteriorBounds;
        var icePrefab = world.Items.First(b => b.kind == LabItemKind.IceBucket).icePrefab;
        Spawn(icePrefab, glass.LocalToWorld(new Vector2(r.center.x, r.yMin + .27f)));
        for (int i = 0; i < 2; i++) Spawn(sources[i].garnishPrefab, glass.LocalToWorld(new Vector2(r.center.x, r.yMin + .7f + i * .4f)));
        Physics2D.SyncTransforms(); world.RefreshIceContainment();
        Check(pieces.All(b => b.ContainingVesselId == glass.Id), "Mixed ice/garnish fixture acquires vessel before pickup");
        if (held) glass.SetHeld(true);
        world.RefreshCollisionPairs(); foreach (var b in world.Items) b.SynchronizeHistory();
        var ingredient = world.Items.First(b => b.ingredient != null).ingredient;
        float amount = 0, spacing = gpu.Radius * 1.75f;
        for (float y = r.yMin + spacing; y < r.yMax - spacing && amount < 40; y += spacing)
        for (float x = r.xMin + spacing; x < r.xMax - spacing && amount < 40; x += spacing)
        {
            Vector2 local = new Vector2(x,y), point = glass.LocalToWorld(local);
            if (!glass.ContainsLiquidDisk(local, gpu.Radius) || pieces.Any(b => Clearance(b,point) < gpu.Radius + .005f)) continue;
            if (!gpu.TryEmit(point, Vector2.zero, ingredient, .5f, glass.Id)) throw new Exception("Fixture emission rejected"); amount += .5f;
        }
        Check(amount == 40, "Mixed fixture admits 40 ml outside all solids");
        var samples = new List<Vector2>[] {new List<Vector2>(),new List<Vector2>()};
        int sleep = 0, contact = 0, near = 0, inside = 0; float nearSpeed = 0;
        var contacts = new ContactPoint2D[32];
        for (int tick = 0; tick < 400; tick++)
        {
            Step(); if (tick < 300) continue;
            for (int i = 0; i < 2; i++)
            {
                var b = pieces[i+1]; samples[i].Add(b.Position); if (b.Body.IsSleeping()) sleep++;
                if (b.Body.GetContacts(contacts) > 0) contact++;
            }
            gpu.ReadbackNow();
            foreach (var p in gpu.Snapshot.Where(p => p.Active != 0))
            {
                if (!float.IsFinite(p.Position.x) || !float.IsFinite(p.Velocity.magnitude)) throw new Exception("Nonfinite liquid");
                float d = pieces.Min(b => Clearance(b, p.Position));
                if (d < -.002f) inside++;
                if (d < gpu.Radius * 2.5f) { near++; nearSpeed += p.Velocity.magnitude; }
            }
        }
        float span = samples.Max(ps => (new Vector2(ps.Max(p=>p.x),ps.Max(p=>p.y)) - new Vector2(ps.Min(p=>p.x),ps.Min(p=>p.y))).magnitude);
        report.Add($"MEASURE held={held}, span={span:R}, sleep={sleep}/200, contacts={contact}/200, near={near}, speed={nearSpeed/Mathf.Max(1,near):R}, penetration={inside}, activeMl={gpu.Ledger.Total.ActiveMl}");
        Check(pieces.All(b => b.ContainingVesselId == glass.Id), "Mixed contents remain in the stationary vessel");
        Check(span <= .01f && sleep >= 160 && contact >= 100, "Garnishes settle with native contacts and sleep; no visible sustained jitter");
        Check(near > 0 && inside == 0 && nearSpeed/near < .1f, "Actual liquid contacts stay bounded and outside garnish/ice");
        Check(Math.Abs(gpu.Ledger.Total.ActiveMl - 40) < .01 && Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .01, "Mixed fixture retains all 40 ml without ledger error");
        Capture(held ? "Garnish-Held-Contact" : "Garnish-Rest-Contact", glass.LocalToWorld(r.center), 1.7f, 640, 640);
        if (!held) return;
        Physics2D.SyncTransforms();
        var before = pieces.Select(b => glass.WorldToLocal(b.Position)).ToArray();
        glass.SetHeldPose(glass.Position + new Vector2(2,1), 725); world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms();
        for (int i=0;i<pieces.Count;i++)
        {
            Vector2 after = glass.WorldToLocal(pieces[i].Position);
            report.Add($"MEASURE transport {pieces[i].name}, before={before[i]:F6}, after={after:F6}, delta={Vector2.Distance(after,before[i]):R}, owner={pieces[i].ContainingVesselId}, vessel={glass.Id}");
            Check(Vector2.Distance(after,before[i]) < .003f, "Fast multi-turn vessel motion transports its solid contents");
        }
        for (int tick=0;tick<5;tick++) Step();
        Check(pieces.All(b => b.ContainingVesselId == glass.Id), "Moved held vessel retains ice and both garnishes");
        glass.SetHeldPose(glass.Position, 180); world.SendMessage("FixedUpdate");
        for (int tick=0;tick<200;tick++) Step();
        Check(pieces.Where(b=>b.kind==LabItemKind.Garnish).All(b=>b.ContainingVesselId==0), "Both garnishes can spill through the open mouth");
    }
    private float MovingContact(FluidExperimentGarnishSource source, float transfer)
    {
        Prepare(); Rect r = glass.collisionProfile.InteriorBounds;
        var b = Spawn(source.garnishPrefab, glass.LocalToWorld(r.center) + Vector2.left*.14f);
        b.garnishLiquidMotionTransfer = transfer;
        b.Body.bodyType = RigidbodyType2D.Kinematic;
        Physics2D.SyncTransforms(); world.RefreshIceContainment(); glass.SetHeld(true);
        Check(b.ContainingVesselId == glass.Id, source.name + " moving piece shares held-vessel contact owner");
        var path = b.collisionProfile.solids[0].points;
        float area=0; for(int i=0;i<path.Length;i++) area += path[i].x*path[(i+1)%path.Length].y-path[(i+1)%path.Length].x*path[i].y;
        Vector2 anchor=Vector2.zero, normal=Vector2.zero; float best=float.NegativeInfinity;
        for(int i=0;i<path.Length;i++)
        {
            Vector2 edge=path[(i+1)%path.Length]-path[i], n=new Vector2(edge.y,-edge.x).normalized*Mathf.Sign(area), mid=(path[i]+path[(i+1)%path.Length])*.5f;
            if(n.x>.7f && mid.x>best) { best=mid.x; anchor=mid; normal=n; }
        }
        Vector2 point=b.LocalToWorld(anchor+normal*(gpu.Radius+.03f));
        Check(best>float.NegativeInfinity && Clearance(b,point)>gpu.Radius && glass.ContainsLiquidDisk(glass.WorldToLocal(point+Vector2.right*.14f),gpu.Radius), "Moving probe has initial solid and endpoint wall clearance");
        var ingredient=world.Items.First(x=>x.ingredient!=null).ingredient;
        Check(gpu.TryEmit(point,Vector2.zero,ingredient,.5f,glass.Id), "Moving probe emits one independent half-ml particle");
        var gravity=Physics2D.gravity; Physics2D.gravity=Vector2.zero;
        try
        {
            foreach(var body in world.Items) body.SynchronizeHistory(); world.TickLiquid(Dt); gpu.ReadbackNow();
            Vector2 first=gpu.Snapshot.Single(p=>p.Active!=0).Position; b.Body.linearVelocity=Vector2.right*.7f;
            float maxX=0;
            for(int i=0;i<10;i++)
            {
                Step(); gpu.ReadbackNow(); var particle=gpu.Snapshot.Single(p=>p.Active!=0);
                if(Clearance(b,particle.Position)<-.002f || !float.IsFinite(particle.Velocity.magnitude)) throw new Exception("Moving garnish penetration/nonfinite state");
                maxX=Mathf.Max(maxX,particle.Velocity.x);
            }
            float travel=gpu.Snapshot.Single(p=>p.Active!=0).Position.x-first.x;
            report.Add($"MEASURE {source.name} transfer={transfer:R} moving liquid travel={travel:R}, vx={maxX:R}");
            Check(travel>.04f && maxX>.1f && Math.Abs(gpu.Ledger.Total.ActiveMl-.5)<.001, "Moving garnish pushes liquid and preserves quantity");
            return maxX;
        }
        finally { Physics2D.gravity=gravity; }
    }
    private void ResponseGeometry(FluidExperimentGarnishSource source, float turn = 12)
    {
        Prepare(); var b = Spawn(source.garnishPrefab, glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center));
        b.Body.bodyType = RigidbodyType2D.Kinematic;
        Physics2D.SyncTransforms(); world.RefreshIceContainment(); glass.SetHeld(true);
        foreach (var item in world.Items) item.SynchronizeHistory();
        var capture = typeof(FluidExperimentBody).GetMethod("CaptureMotion", BindingFlags.Instance | BindingFlags.NonPublic);
        var upload = typeof(FluidExperimentGpuLiquid).GetMethod("UploadGeometry", BindingFlags.Instance | BindingFlags.NonPublic);
        var field = typeof(FluidExperimentGpuLiquid).GetField("boundaryUpload", BindingFlags.Instance | BindingFlags.NonPublic);
        Vector2[] Edge(float transfer, float from = 0)
        {
            b.garnishLiquidMotionTransfer = transfer; upload.Invoke(gpu, new object[] { from, 1f, Dt*(1-from) });
            Vector2 a = b.LocalToWorld(b.collisionProfile.solids[0].points[0]);
            Vector2 end = b.LocalToWorld(b.collisionProfile.solids[0].points[1]);
            foreach (object edge in (Array)field.GetValue(gpu))
            {
                var type = edge.GetType(); Vector2 Read(string key) => (Vector2)type.GetField(key).GetValue(edge);
                if ((Read("A") - a).sqrMagnitude < 1e-10f && (Read("B") - end).sqrMagnitude < 1e-10f)
                    return new[] { Read("A"), Read("B"), Read("VelocityA"), Read("VelocityB"), Read("StartPosition"), Read("EndPosition") };
            }
            throw new Exception("Authored garnish edge absent from upload");
        }
        // Exact carried geometry includes both translation and rotation. Attenuating
        // world velocity here would incorrectly weaken the glass's common motion.
        glass.SetHeldPose(glass.Position + new Vector2(.02f, .03f), turn);
        world.SendMessage("FixedUpdate");
        foreach (var item in world.Items) capture.Invoke(item, null);
        var full = Edge(1); var gentle = Edge(.2f);
        report.Add($"MEASURE carrier owner={b.ContainingVesselId}/{glass.Id}, fullA={full[2]:R}, gentleA={gentle[2]:R}, fullB={full[3]:R}, gentleB={gentle[3]:R}, glassFrom={glass.PreviousPosition:R}, glassTo={glass.Position:R}, glassTurn={glass.StepAngle:R}, pieceFrom={b.PreviousPosition:R}, pieceTo={b.Position:R}, pieceTurn={b.StepAngle:R}");
        Check((full[2]-gentle[2]).magnitude < .0001f && (full[3]-gentle[3]).magnitude < .0001f,
            source.name + " preserves liquid response to pure carrier translation and rotation");
        full = Edge(1,.5f); gentle = Edge(.2f,.5f);
        Check((full[2]-gentle[2]).magnitude < .0001f && (full[3]-gentle[3]).magnitude < .0001f,
            "Carrier response remains unchanged with split GPU substeps");
        foreach (var item in world.Items) item.SynchronizeHistory();
        b.Body.position += new Vector2(.005f,-.007f); b.Body.rotation += 4;
        capture.Invoke(b,null); full = Edge(1); gentle = Edge(.2f);
        Check(new[] {0,1,4,5}.All(i => full[i] == gentle[i]), "Response tuning preserves actual garnish geometry and sweep trajectory");
        Check((gentle[2] - full[2]*.2f).magnitude < .0001f && (gentle[3] - full[3]*.2f).magnitude < .0001f,
            source.name + " scales its own translation and angular liquid response");
        gpu.useCohesivePhysics = false;
        try
        {
            var legacy = Edge(.2f);
            Check(legacy[2] == full[2] && legacy[3] == full[3], "Non-F boundary response ignores the garnish coefficient");
        }
        finally { gpu.useCohesivePhysics = true; }
    }
    private Vector2 DropImpact(FluidExperimentGarnishSource source, float transfer)
    {
        Prepare(); Rect r = glass.collisionProfile.InteriorBounds;
        Physics2D.SyncTransforms();
        foreach (var item in world.Items) item.SynchronizeHistory();
        Vector2 glassStart = glass.Position;
        Check((glassStart-new Vector2(0,1.5f)).sqrMagnitude<1e-8f && Mathf.Abs(Mathf.DeltaAngle(glass.Angle,0))<.001f,
            "Free-drop receiver starts upright at the intended position");
        var ingredient = world.Items.First(b => b.ingredient != null).ingredient;
        float amount = 0, spacing = gpu.Radius * 1.75f;
        for (float y = r.yMin + spacing; y < r.yMax - spacing && amount < 40; y += spacing)
        for (float x = r.xMin + spacing; x < r.xMax - spacing && amount < 40; x += spacing)
        {
            Vector2 local = new Vector2(x,y);
            if (!glass.ContainsLiquidDisk(local, gpu.Radius)) continue;
            if (!gpu.TryEmit(glass.LocalToWorld(local), Vector2.zero, ingredient, .5f, glass.Id)) throw new Exception("Drop fixture emission rejected");
            amount += .5f;
        }
        Check(amount == 40, "Drop fixture seeds identical 40 ml before garnish spawn");
        for (int i=0;i<150;i++) Step();
        gpu.ReadbackNow();
        report.Add($"MEASURE pre-drop glass={glass.Position:R}, angle={glass.Angle:R}, start={glassStart:R}, activeMl={gpu.Ledger.Total.ActiveMl:R}");
        Check((glass.Position-glassStart).sqrMagnitude<1e-8f && Math.Abs(gpu.Ledger.Total.ActiveMl-40)<.001,
            "Drop fixture retains a stationary glass and settled 40 ml before impact");
        var b = Spawn(source.garnishPrefab, glass.LocalToWorld(new Vector2(r.center.x,r.yMax-.3f)));
        b.garnishLiquidMotionTransfer = transfer;
        if (transfer == 1) { b.Body.linearDamping = .05f; b.Body.angularDamping = .1f; }
        Physics2D.SyncTransforms(); world.RefreshIceContainment();
        float peakEnergy = 0, peakUp = 0; int contacts = 0;
        for (int tick=0;tick<150;tick++)
        {
            Step(); gpu.ReadbackNow(); float energy = 0;
            foreach (var p in gpu.Snapshot.Where(p=>p.Active!=0))
            {
                if (!float.IsFinite(p.Velocity.sqrMagnitude) || Clearance(b,p.Position)<-.002f) throw new Exception("Drop liquid nonfinite/inside garnish");
                energy += p.VolumeMl*p.Velocity.sqrMagnitude;
                peakUp = Mathf.Max(peakUp,p.Velocity.y);
                if (Clearance(b,p.Position)<gpu.Radius*1.5f) contacts++;
            }
            peakEnergy = Mathf.Max(peakEnergy,energy);
        }
        report.Add($"MEASURE {source.name} transfer={transfer:R} drop peakVolumeWeightedSpeedSquared={peakEnergy:R}, peakUpSpeed={peakUp:R}, contacts={contacts}, activeMl={gpu.Ledger.Total.ActiveMl:R}, ledgerError={gpu.Ledger.Total.ConservationErrorMl:R}");
        // An open-rim impact can legitimately eject liquid. Compare retained ml
        // between the paired runs and require exact accounting in both.
        Check(contacts>0 && Math.Abs(gpu.Ledger.Total.ConservationErrorMl)<.001,
            "Free-drop fixture makes real liquid contacts with conserved total accounting");
        return new Vector2(peakEnergy,(float)gpu.Ledger.Total.ActiveMl);
    }
    private void SurfaceMask(FluidExperimentMode mode)
    {
        ClearPieces(); comparison.SwitchMode(mode); world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
        var oldGravity = Physics2D.gravity; Physics2D.gravity = Vector2.zero;
        gpu.ImprovedSurfaceInterpolationOverride = 1;
        try
        {
            foreach (var source in sources)
            {
                Prepare(); Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
                var ingredient = world.Items.First(b => b.ingredient != null).ingredient;
                Check(gpu.TryEmit(center,Vector2.zero,ingredient,.5f,glass.Id), "Surface-only fixture emits one isolated half-ml particle");
                world.TickLiquid(Dt); gpu.ReadbackNow(); var physical = gpu.Snapshot.Where(p=>p.Active!=0).ToArray();
                string prefix = "Mask-" + mode + "-" + source.name;
                Capture(prefix+"-Before",center,.8f,256,256);
                var baseline = SurfaceAlpha(); int count = gpu.ImprovedSurfaceMaskHullCount;
                Check(baseline.Sum() > 1, "Surface fixture has visible baseline liquid coverage");
                var garnish = Spawn(source.garnishPrefab,center); garnish.Body.bodyType = RigidbodyType2D.Kinematic;
                // Deliberate visual overlap without advancing physics isolates the mask
                // from the existing, still-enabled physical garnish/liquid collision.
                Capture(prefix+"-Garnish",center,.8f,256,256);
                var after = SurfaceAlpha(); float difference = baseline.Zip(after,(a,b)=>Mathf.Abs(a-b)).Sum();
                bool f = mode == FluidExperimentMode.FCoherentLiquid;
                Check(gpu.ImprovedSurfaceMaskHullCount == count + (f ? 0 : 1), mode + " applies the intended garnish-only mask policy");
                Check(f ? difference < .0001f : difference > 1, mode + " actual liquid composite " + (f ? "keeps coverage under garnish" : "retains the comparison mask"));
                gpu.ReadbackNow(); var current = gpu.Snapshot.Where(p=>p.Active!=0).ToArray();
                Check(current.Length == physical.Length && current.Zip(physical,(a,b)=>a.Position==b.Position && a.Velocity==b.Velocity && a.VolumeMl==b.VolumeMl).All(v=>v),
                    "Rendering the garnish cannot change GPU particle position, velocity or ml");
                // Keep the body registered while checking the ice control: unregister
                // calls ReleaseOwner and invalidates display history until the next tick.
                garnish.Teleport(new Vector2(40,40),0);
                var ice = Spawn(world.Items.First(b=>b.kind==LabItemKind.IceBucket).icePrefab,center);
                ice.Body.bodyType = RigidbodyType2D.Kinematic;
                Capture(prefix+"-Ice",center,.8f,256,256);
                float iceDifference = baseline.Zip(SurfaceAlpha(),(a,b)=>Mathf.Abs(a-b)).Sum();
                report.Add($"MEASURE mask {mode} baselineHulls={count}, iceHulls={gpu.ImprovedSurfaceMaskHullCount}, baselineAlpha={baseline.Sum():R}, garnishDifference={difference:R}, iceDifference={iceDifference:R}, seed={center:R}, particle={physical[0].Position:R}, ice={ice.Position:R}, iceClearance={Clearance(ice,physical[0].Position):R}");
                Check(gpu.ImprovedSurfaceMaskHullCount == count + (f ? 1 : 2) && iceDifference > 1, mode + " ice still masks the liquid normally");
                report.Add($"MEASURE {mode} {source.name} garnishMaskPixelDifference={difference:R}, iceMaskPixelDifference={iceDifference:R}");
            }
        }
        finally { gpu.ImprovedSurfaceInterpolationOverride = -1; Physics2D.gravity = oldGravity; ClearPieces(); }
    }
    private float[] SurfaceAlpha()
    {
        var target = (RenderTexture)typeof(FluidExperimentGpuLiquid).GetField("surfaceComposite",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(gpu);
        if (target == null) throw new Exception("Rendered liquid composite missing");
        var old = RenderTexture.active; var texture = new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false);
        try
        {
            RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0); texture.Apply();
            return texture.GetPixels().Select(p=>p.a).ToArray();
        }
        finally { RenderTexture.active=old; Destroy(texture); }
    }
    private void Capture(string name, Vector2 center, float size, int width, int height)
    {
        var camera=hand.inputCamera; var oldPosition=camera.transform.position; float oldSize=camera.orthographicSize;
        var oldTarget=camera.targetTexture; var oldActive=RenderTexture.active;
        var target=new RenderTexture(width,height,24); target.Create(); var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);
        try
        {
            foreach(var b in world.Items) b.transform.SetPositionAndRotation(b.Position,Quaternion.Euler(0,0,b.Angle));
            camera.transform.position=new Vector3(center.x,center.y,oldPosition.z); camera.orthographicSize=size;
            camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
            texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
            File.WriteAllBytes(Path.Combine(ExperimentGarnishValidation.Evidence,name+".png"),texture.EncodeToPNG());
        }
        finally { camera.targetTexture=oldTarget; camera.transform.position=oldPosition; camera.orthographicSize=oldSize; RenderTexture.active=oldActive; target.Release(); Destroy(target); Destroy(texture); }
    }
    private void Check(bool valid,string message) { report.Add((valid?"PASS ":"FAIL ")+message); if(!valid) throw new Exception(message); }
    private void Log(string text,string stack,LogType type) { if(!finished && (type==LogType.Error || type==LogType.Exception || type==LogType.Assert)) errors.Add(text); }
    private void Finish(bool success)
    {
        finished=true; Application.logMessageReceived-=Log; report.AddRange(errors);
        File.WriteAllText(Path.Combine(ExperimentGarnishValidation.Evidence,"garnish-validation.txt"),string.Join("\n",report));
        File.WriteAllText(Path.Combine(ExperimentGarnishValidation.Evidence,"garnish-result.txt"),success?"PASS":"FAIL");
        if(success) Debug.Log("[GarnishValidation] PASS"); else Debug.LogError("[GarnishValidation] FAIL\n"+string.Join("\n",report));
        EditorApplication.isPlaying=false; EditorApplication.delayCall+=()=>EditorApplication.Exit(success?0:1);
    }
}
