using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

// Imported only into the disposable harness. Exercises public interaction APIs,
// real collider state and actual compute dispatches against the authored scene.
public static class ExperimentSwapValidation
{
    private const string Key = "Slainte.FluidGpuExperiment.SwapValidation";
    private const string ScenePath = "Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR")
        ?? "FluidGpuExperimentEvidence/swap-manual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    public static void Begin()
    {
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Path.Combine(Evidence, "swap-validation.txt"), "STARTED; no result yet.\n");
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    private static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("ExperimentSwapValidationRunner").AddComponent<ExperimentSwapValidationRunner>().Begin();
    }

    public static void Finish(bool success, string report)
    {
        File.WriteAllText(Path.Combine(Evidence, "swap-validation.txt"), report);
        File.WriteAllText(Path.Combine(Evidence, "swap-result.txt"), success ? "PASS\n" : "FAIL\n");
        if (success) Debug.Log("[ExperimentSwapValidation] PASS\n" + report);
        else Debug.LogError("[ExperimentSwapValidation] FAIL\n" + report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ExperimentSwapValidationRunner : MonoBehaviour
{
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private FluidExperimentComparison comparison;
    private FluidExperimentWorld world;
    private FluidExperimentGpuLiquid gpu;
    private FluidExperimentInteractor hand;
    private SimulationMode2D originalMode;
    private Vector2 originalGravity;
    private bool controlledPhysics;
    private bool finished;
    private float started;

    public void Begin()
    {
        started = Time.realtimeSinceStartup;
        Application.logMessageReceived += OnLog;
        StartCoroutine(Guard());
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            errors.Add(message + "\n" + stack);
    }

    private void Update()
    {
        if (!finished && Time.realtimeSinceStartup - started > 150)
            Finish(false, "Swap validation exceeded 150 seconds.");
    }

    private IEnumerator Guard()
    {
        IEnumerator run = Run();
        while (!finished)
        {
            bool more;
            object next = null;
            try
            {
                if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
                more = run.MoveNext();
                if (more) next = run.Current;
            }
            catch (Exception exception)
            {
                (run as IDisposable)?.Dispose();
                Finish(false, exception.ToString());
                yield break;
            }
            if (!more) break;
            yield return next;
        }
        (run as IDisposable)?.Dispose();
        if (!finished) Finish(errors.Count == 0, errors.Count == 0
            ? "A/B/C/D/E swap handoff, content ownership and held collision checks passed."
            : string.Join("\n", errors));
    }

    private IEnumerator Run()
    {
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        Require(comparison != null, "Authored comparison scene initializes");
        while (!comparison.Ready)
        {
            if (Time.realtimeSinceStartup - started > 20)
                throw new Exception("Comparison initialization timed out: " + comparison.Error);
            yield return null;
        }
        originalMode = Physics2D.simulationMode;
        originalGravity = Physics2D.gravity;
        controlledPhysics = true;
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.gravity = Vector2.zero;
        comparison.automaticScenario = false;
        comparison.showControls = false;

        foreach (FluidExperimentMode mode in Enum.GetValues(typeof(FluidExperimentMode)))
        {
            comparison.SwitchMode(mode);
            comparison.StartScenario(FluidExperimentScenario.Manual);
            world = comparison.World;
            gpu = comparison.Gpu;
            hand = world.interactor;
            world.enabled = false;
            hand.enabled = false;
            world.showControls = false;
            gpu.automaticReadback = false;
            Require(gpu.IsOperational, mode + ": actual GPU operational on " + SystemInfo.graphicsDeviceName);
            int index = 0;
            foreach (FluidExperimentBody body in world.Items)
            {
                body.SetHeld(false);
                body.Body.simulated = true;
                body.Body.bodyType = RigidbodyType2D.Kinematic;
                body.Teleport(new Vector2(-50 - index++ * 4, 20), 0);
                body.pourMlPerSecond = 0;
            }
            var a = world.Items.First(body => body.kind == LabItemKind.Shaker);
            var b = world.Items.First(body => body.kind == LabItemKind.Glass && body.name.Contains("highball"));
            var icePrefab = world.Items.First(body => body.icePrefab != null).icePrefab;
            var ice = Instantiate(icePrefab, new Vector3(10, 7, 0), Quaternion.identity, world.transform);
            ice.Body.bodyType = RigidbodyType2D.Kinematic;
            a.Teleport(new Vector2(-6, 5), 0);
            b.Teleport(new Vector2(0, 5), 0);
            a.SetSealed(true);
            Physics2D.SyncTransforms();
            world.RefreshCollisionPairs();
            ValidateSwap(mode, a, b);
            ValidateCollisionPolicy(mode, a, b, ice);
            ValidateGpuHeldIce(mode, b, ice);
            ice.Teleport(new Vector2(12, 10), 0);
            ValidateHeldVesselPassesThroughFreeLiquid(mode, b);
            ValidateThrowHandoff(mode, a, b);
            ValidateReturnAndRejection(mode, a, b);
            ValidateIceTableFall(mode, a, b, ice);
            ice.gameObject.SetActive(false);
            Destroy(ice.gameObject);
            yield return null;
        }
    }

    private void ValidateSwap(FluidExperimentMode mode, FluidExperimentBody a, FluidExperimentBody b)
    {
        string label = mode + " swap";
        Vector2 aOrigin = a.Position;
        Vector2 bOrigin = b.Position;
        Vector2 grabOffset = new Vector2(.2f, 0);
        Require(hand.Pick(a, a.Position + grabOffset), label + ": pick original vessel through public API");
        Vector2 pointer = b.pickCollider.bounds.center;
        hand.MoveHeld(pointer);
        ApplyHeldPose();
        Vector2 releasePosition = a.Position;
        Require(Vector2.Distance(releasePosition, aOrigin) > 4,
            label + ": held vessel has moved far from its pickup origin");
        Require(world.FindSwapTarget(a, pointer) == b, label + ": authored overlapping target is selected");

        a.SynchronizeHistory();
        b.SynchronizeHistory();
        gpu.ResetSimulation();
        ItemDef ingredient = world.Items.First(body => body.ingredient != null).ingredient;
        Require(gpu.Fill(a, ingredient, 2) > 0 && gpu.Fill(b, ingredient, 2) > 0,
            label + ": both overlapping vessels accept owner-tagged liquid");
        world.TickLiquid(.00002f);
        gpu.ReadbackNow();
        GpuLiquidParticle[] before = gpu.Snapshot.ToArray();
        Require(before.Any(p => p.Active != 0 && p.VesselId == a.Id)
            && before.Any(p => p.Active != 0 && p.VesselId == b.Id),
            label + ": both vessels own actual GPU particles before handoff");

        Require(hand.Drop(pointer), label + ": slow overlapping Drop succeeds");
        Require(hand.Held == b && b.IsHeld && !a.IsHeld,
            label + ": released A becomes dynamic while B becomes held");
        Require(a.Body.bodyType == RigidbodyType2D.Dynamic && b.Body.bodyType == RigidbodyType2D.Kinematic,
            label + ": Rigidbody body types agree with handoff");
        Near(a.Position, releasePosition, label + ": released A remains at its current position");
        Near(b.Position, bOrigin, label + ": new held B remains at its own position");
        Near(hand.PickupOrigin, bOrigin, label + ": pickup bookkeeping now belongs to B");
        Near(a.Body.linearVelocity, Vector2.zero, label + ": swap adds no throw velocity");
        Require(Mathf.Abs(a.Body.angularVelocity) < .0001f, label + ": swap adds no spin");
        gpu.ReadbackNow();
        Require(before.Zip(gpu.Snapshot, (old, current) => old.Active == current.Active
            && old.VesselId == current.VesselId && Mathf.Abs(old.VolumeMl - current.VolumeMl) < .00001f
            && (old.Active == 0 || Vector2.Distance(old.Position, current.Position) < .00001f)).All(x => x),
            label + ": handoff preserves GPU particle position, mass and owner IDs");

        hand.MoveHeld(pointer);
        ApplyHeldPose();
        Near(b.Position, bOrigin, label + ": unchanged cursor does not jump newly held B");
        Vector2 movedPointer = pointer + new Vector2(3, 1);
        hand.MoveHeld(movedPointer);
        ApplyHeldPose();
        Near(b.Position, bOrigin + new Vector2(3, 1), label + ": newly held B follows subsequent cursor motion");
        Near(a.Position, releasePosition, label + ": moving B leaves released A in place");

        pointer = a.pickCollider.bounds.center;
        hand.MoveHeld(pointer);
        ApplyHeldPose();
        Vector2 secondReleasePosition = b.Position;
        Vector2 secondTargetPosition = a.Position;
        Require(world.FindSwapTarget(b, pointer) == a, label + ": repeated swap can target released A");
        Require(hand.Drop(pointer), label + ": repeated Drop succeeds");
        Require(hand.Held == a && a.IsHeld && !b.IsHeld, label + ": repeated swap reverses held identity");
        Near(b.Position, secondReleasePosition, label + ": second release keeps B at its current position");
        Near(a.Position, secondTargetPosition, label + ": repeated handoff never returns A to original pickup position");
        hand.MoveHeld(pointer + Vector2.up);
        ApplyHeldPose();
        Near(a.Position, secondTargetPosition + Vector2.up, label + ": original A follows cursor after being picked again");
        hand.ReleaseWithVelocity(Vector2.zero);
    }

    private void ValidateCollisionPolicy(FluidExperimentMode mode, FluidExperimentBody a,
        FluidExperimentBody b, FluidExperimentBody ice)
    {
        string label = mode + " held collisions";
        a.Teleport(new Vector2(0, 5), 0);
        b.Teleport(new Vector2(5, 5), 0);
        ice.Teleport(new Vector2(8, 5), 0);
        Physics2D.SyncTransforms();
        world.RefreshCollisionPairs();
        Require(PairIgnored(a, b, false) && PairIgnored(a, ice, false) && FloorIgnored(a, false),
            label + ": unheld body, ice and floor collisions start enabled");
        Require(hand.Pick(a, a.Position), label + ": vessel pickup succeeds");
        Require(PairIgnored(a, b, true) && PairIgnored(a, ice, true) && FloorIgnored(a, true),
            label + ": held vessel ignores every enabled body, ice and floor pair");
        Collider2D wall = a.solidColliders.First(c => c != null && c.enabled);
        ice.Teleport(wall.bounds.center, 0);
        ice.Body.bodyType = RigidbodyType2D.Dynamic;
        Physics2D.SyncTransforms();
        Vector2 iceBefore = ice.Position;
        Require(Physics2D.Simulate(.02f), label + ": actual Physics2D simulation advances");
        Near(ice.Position, iceBefore, label + ": intersecting held vessel gives ice no collision displacement");
        Require(!a.solidColliders.Where(c => c != null && c.enabled).Any(c =>
            ice.solidColliders.Where(d => d != null && d.enabled).Any(d => c.IsTouching(d))),
            label + ": held vessel produces no ice contact");
        hand.ReleaseWithVelocity(Vector2.zero);
        Require(PairIgnored(a, b, false) && PairIgnored(a, ice, false) && FloorIgnored(a, false),
            label + ": releasing vessel restores all body, ice and floor collision flags");
        ice.Teleport(new Vector2(8, 5), 0);
        Require(hand.Pick(ice, ice.Position), label + ": ice itself can be picked up");
        Require(PairIgnored(ice, a, true) && PairIgnored(ice, b, true) && FloorIgnored(ice, true),
            label + ": held ice ignores other bodies and floor");
        hand.ReleaseWithVelocity(Vector2.zero);
        Require(PairIgnored(ice, a, false) && PairIgnored(ice, b, false) && FloorIgnored(ice, true),
            label + ": releasing ice restores body contacts while continuing to ignore the table");
    }

    private void ValidateIceTableFall(FluidExperimentMode mode, FluidExperimentBody solid,
        FluidExperimentBody vessel, FluidExperimentBody ice)
    {
        string label = mode + " ice table fall";
        Collider2D table = world.floorColliders.First(c => c != null && c.enabled);
        Bounds tableBounds = table.bounds;
        solid.Teleport(new Vector2(-6, tableBounds.max.y + 3), 0);
        solid.SetHeld(false);
        vessel.Teleport(new Vector2(6, 5), 0);
        ice.Teleport(new Vector2(0, tableBounds.max.y + 1), 0);
        ice.SetHeld(false);
        Physics2D.SyncTransforms();
        Require(FloorIgnored(ice, true) && FloorIgnored(solid, false),
            label + ": only ice ignores the table while ordinary items retain support");
        Require(PairIgnored(ice, vessel, false), label + ": ice still collides with unheld vessels");

        Vector2 previousGravity = Physics2D.gravity;
        try
        {
            Physics2D.gravity = new Vector2(0, -9.81f);
            bool simulated = true;
            for (int tick = 0; tick < 100; tick++) simulated &= Physics2D.Simulate(.02f);
            Physics2D.SyncTransforms();
            Require(simulated, label + ": actual gravity simulation advances for two seconds");
            Require(ice.SolidBounds.max.y < tableBounds.min.y - .5f,
                label + ": released ice falls entirely below the table");
            Require(solid.SolidBounds.min.y >= tableBounds.max.y - .05f,
                label + ": ordinary item remains above the table");
            Require(solid.solidColliders.Any(c => c != null && c.enabled && c.IsTouching(table)),
                label + ": ordinary item is supported by actual table contact");
        }
        finally { Physics2D.gravity = previousGravity; }

        ice.Teleport(new Vector2(0, tableBounds.min.y - 1), 0);
        Require(hand.Pick(ice, ice.Position), label + ": fallen ice can be picked up");
        Vector2 belowTable = ice.Position;
        Require(hand.Drop(belowTable) && hand.Held == null, label + ": below-table ice release succeeds");
        Near(ice.Position, belowTable, label + ": dropping ice below the table never snaps it upward");
        Require(FloorIgnored(ice, true), label + ": pickup/drop preserves ice table pass-through");
        ice.gameObject.SetActive(false);
        ice.gameObject.SetActive(true);
        Require(FloorIgnored(ice, true), label + ": re-enabled ice re-registers with table pass-through");
    }

    private void ApplyHeldPose()
    {
        world.SendMessage("FixedUpdate");
        Physics2D.SyncTransforms();
    }

    private struct ThrowResult
    {
        public Vector2 position, velocity, simulatedPosition, simulatedVelocity;
        public float angle, spin, simulatedAngle, simulatedSpin;
    }

    private void ValidateThrowHandoff(FluidExperimentMode mode, FluidExperimentBody a, FluidExperimentBody b)
    {
        foreach (bool rotate in new[] { false, true })
        {
            string label = mode + (rotate ? " spinning handoff" : " fast handoff");
            ThrowResult normal = RunSampledThrow(a, b, false, rotate, label + " normal");
            ThrowResult swap = RunSampledThrow(a, b, true, rotate, label + " swap");
            Near(swap.position, normal.position, label + ": same gesture releases at the same position");
            Near(swap.velocity, normal.velocity, label + ": swap preserves normal throw velocity");
            Require(Mathf.Abs(swap.angle - normal.angle) < .0001f
                && Mathf.Abs(swap.spin - normal.spin) < .001f,
                label + ": swap preserves normal throw angle and angular velocity");
            Near(swap.simulatedPosition, normal.simulatedPosition,
                label + ": held target does not alter subsequent throw trajectory");
            Near(swap.simulatedVelocity, normal.simulatedVelocity,
                label + ": held target does not alter subsequent throw speed");
            Require(Mathf.Abs(Mathf.DeltaAngle(swap.simulatedAngle, normal.simulatedAngle)) < .001f
                && Mathf.Abs(swap.simulatedSpin - normal.simulatedSpin) < .001f,
                label + ": simulated spin matches normal release");
        }
    }

    private ThrowResult RunSampledThrow(FluidExperimentBody a, FluidExperimentBody b,
        bool target, bool rotate, string label)
    {
        a.Teleport(new Vector2(-6, 5), 0);
        b.Teleport(new Vector2(-30, 10), 0);
        b.Body.bodyType = RigidbodyType2D.Kinematic;
        Require(hand.Pick(a, a.Position), label + ": pick through public API");
        hand.MoveHeld(new Vector2(-.6f, 5));
        ApplyHeldPose();
        Vector2 sampleStart = a.TargetPosition;
        RecordSample(10, true);
        if (rotate)
        {
            hand.BeginRotation();
            hand.RotateBy(30);
        }
        else hand.MoveHeld(new Vector2(0, 5.2f));
        RecordSample(10.05f);
        hand.EstimateRelease(out Vector2 velocity, out float spin);
        Near(velocity, (a.TargetPosition - sampleStart) / (10.05f - 10),
            label + ": velocity comes from actual sampled input targets");
        Require(rotate ? Mathf.Abs(spin) > 45 : velocity.magnitude > 1.4f,
            label + ": gesture exceeds the former " + (rotate ? "spin" : "speed") + " swap cutoff");
        Vector2 pointer = a.TargetPosition;
        if (target) PlacePickCenter(b, pointer);
        Vector2 targetPosition = b.Position;
        Vector2 releasePosition = a.TargetPosition;
        float releaseAngle = a.TargetAngle;
        // The final input pose has deliberately not received a fixed update.
        Require(hand.Drop(pointer), label + ": Drop accepts the sampled gesture before the next physics tick");
        Require(hand.Held == (target ? b : null) && !a.IsHeld,
            label + ": target presence alone determines the next held object");
        Near(a.Position, releasePosition, label + ": release commits the latest input position");
        Near(a.Body.linearVelocity, velocity, label + ": release retains sampled linear velocity");
        Require(Mathf.Abs(a.Body.angularVelocity - spin) < .001f
            && Mathf.Abs(Mathf.DeltaAngle(a.Angle, releaseAngle)) < .001f,
            label + ": release retains sampled angular velocity and pose");
        if (target)
        {
            hand.EstimateRelease(out Vector2 freshVelocity, out float freshSpin);
            Near(freshVelocity, Vector2.zero, label + ": newly picked target starts with fresh velocity history");
            Require(Mathf.Abs(freshSpin) < .001f && Mathf.Abs(b.Body.angularVelocity) < .001f,
                label + ": newly picked target inherits no spin");
            Near(b.Body.linearVelocity, Vector2.zero, label + ": newly picked target inherits no impulse");
            Require(PairIgnored(a, b, true), label + ": thrown body ignores its held overlapping target");
        }
        var result = new ThrowResult { position = a.Position, angle = a.Angle,
            velocity = a.Body.linearVelocity, spin = a.Body.angularVelocity };
        for (int tick = 0; tick < 3; tick++)
            Require(Physics2D.Simulate(.02f), label + ": post-release physics tick " + tick);
        result.simulatedPosition = a.Position;
        result.simulatedVelocity = a.Body.linearVelocity;
        result.simulatedAngle = a.Angle;
        result.simulatedSpin = a.Body.angularVelocity;
        Require(rotate ? Mathf.Abs(Mathf.DeltaAngle(result.angle, a.Angle)) > 1
            : Vector2.Distance(result.position, a.Position) > .05f,
            label + ": released body actually continues its sampled motion");
        if (target)
        {
            Near(b.Position, targetPosition, label + ": held target stays still during throw simulation");
            hand.ReleaseWithVelocity(Vector2.zero);
        }
        return result;
    }

    private void ValidateReturnAndRejection(FluidExperimentMode mode, FluidExperimentBody a,
        FluidExperimentBody b)
    {
        string label = mode + " automatic return";
        foreach (bool target in new[] { false, true })
        {
            a.Teleport(new Vector2(0, 5), 0);
            b.Teleport(new Vector2(-30, 10), 0);
            Require(hand.Pick(a, a.Position), label + ": pick for " + (target ? "swap" : "normal release"));
            RecordSample(20, true);
            hand.BeginRotation();
            hand.RotateBy(60);
            RecordSample(20.05f);
            hand.EndRotation(a.TargetPosition);
            hand.AdvanceUprightReturn(hand.uprightReturnDuration * .5f);
            Require(hand.Returning, label + ": fixture drops midway through upright animation");
            Vector2 pointer = a.Position;
            if (target) PlacePickCenter(b, pointer);
            Require(hand.Drop(pointer) && hand.Held == (target ? b : null),
                label + ": release and handoff both succeed during automatic return");
            Near(a.Body.linearVelocity, Vector2.zero, label + ": automatic return produces no throw impulse");
            Require(Mathf.Abs(a.Body.angularVelocity) < .001f,
                label + ": automatic return produces no spin impulse");
            if (target) hand.ReleaseWithVelocity(Vector2.zero);
        }

        label = mode + " rejected release";
        a.Teleport(new Vector2(-20, 10), 0);
        var held = CreateBox("ReleaseProbe", new Vector2(0, 5), Vector2.one);
        var left = CreateBox("ReleaseBlockLeft", new Vector2(-1.25f, 5), new Vector2(2, 20));
        var right = CreateBox("ReleaseBlockRight", new Vector2(1.25f, 5), new Vector2(2, 20));
        PlacePickCenter(b, held.Position);
        Require(hand.Pick(held, held.Position), label + ": pick fixture inside a gap narrower than its width");
        RecordSample(30, true);
        hand.BeginRotation();
        hand.RotateBy(15);
        RecordSample(30.05f);
        hand.EstimateRelease(out Vector2 expectedVelocity, out float expectedSpin);
        Vector2 attemptedPosition = held.TargetPosition;
        Require(!hand.Drop(attemptedPosition), label + ": unresolved penetration rejects the drop");
        Require(hand.Held == held && held.IsHeld && !b.IsHeld && hand.Rotating,
            label + ": rejected handoff preserves held identity and rotation mode");
        Near(held.Position, attemptedPosition, label + ": rejected release restores the attempted position");
        hand.EstimateRelease(out Vector2 retainedVelocity, out float retainedSpin);
        Near(retainedVelocity, expectedVelocity, label + ": rejected release preserves motion samples");
        Require(Mathf.Abs(retainedSpin - expectedSpin) < .001f && Mathf.Abs(retainedSpin) > 45,
            label + ": rejected release preserves sampled spin");
        hand.EndRotation(attemptedPosition);
        hand.AdvanceUprightReturn(hand.uprightReturnDuration * .5f);
        Require(!hand.Drop(attemptedPosition) && hand.Returning && hand.Held == held,
            label + ": rejected drop also preserves an in-progress upright return");
        left.gameObject.SetActive(false);
        right.gameObject.SetActive(false);
        Require(hand.Drop(attemptedPosition) && hand.Held == b,
            label + ": removing blockers permits the same handoff to complete");
        Near(held.Body.linearVelocity, Vector2.zero, label + ": recovered automatic return still suppresses throw");
        Require(Mathf.Abs(held.Body.angularVelocity) < .001f,
            label + ": recovered automatic return still suppresses spin");
        hand.ReleaseWithVelocity(Vector2.zero);
        held.gameObject.SetActive(false);
        Destroy(held.gameObject);
        Destroy(left.gameObject);
        Destroy(right.gameObject);
    }

    private FluidExperimentBody CreateBox(string name, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(world.transform, false);
        var collider = go.AddComponent<BoxCollider2D>();
        collider.size = size;
        var body = go.AddComponent<FluidExperimentBody>();
        body.kind = LabItemKind.Spoon;
        body.solidColliders = new Collider2D[] { collider };
        body.Body.bodyType = RigidbodyType2D.Kinematic;
        body.Teleport(position, 0);
        world.RefreshCollisionPairs();
        return body;
    }

    private void PlacePickCenter(FluidExperimentBody body, Vector2 point)
    {
        Physics2D.SyncTransforms();
        Vector2 offset = (Vector2)body.pickCollider.bounds.center - body.Position;
        body.Teleport(point - offset, 0);
        Physics2D.SyncTransforms();
    }

    private void RecordSample(float time, bool clear = false)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = typeof(FluidExperimentInteractor);
        if (clear) ((IList)type.GetField("samples", flags).GetValue(hand)).Clear();
        type.GetMethod("Sample", flags).Invoke(hand, new object[] { time });
    }

    private void ValidateGpuHeldIce(FluidExperimentMode mode, FluidExperimentBody vessel,
        FluidExperimentBody ice)
    {
        string label = mode + " GPU held ice";
        Vector2[] contour = ice.collisionProfile != null
            ? ice.collisionProfile.solids[0].points : ice.liquidWall;
        Require(contour.Length >= 2, label + ": authored ice has a real solid boundary");
        Vector2 edgeLocal = (contour[0] + contour[1]) * .5f;
        ice.Teleport(new Vector2(8, 5), 0);
        Vector2 seed = ice.LocalToWorld(edgeLocal);
        GpuLiquidParticle colliding = ProbeParticle(seed, 0);
        Require(Vector2.Distance(colliding.Position, seed) > .005f,
            label + ": unheld ice positive control displaces an intersecting free particle");
        Require(hand.Pick(ice, ice.Position), label + ": pick ice for GPU collision probe");
        GpuLiquidParticle ignored = ProbeParticle(seed, 0);
        Near(ignored.Position, seed, label + ": held ice leaves the same free particle undisturbed");
        Require(ignored.VesselId == 0, label + ": held ice does not capture the free particle");
        hand.ReleaseWithVelocity(Vector2.zero);

        Vector2 local = vessel.contentRegions[0].center;
        Require(vessel.ContainsLiquidDisk(local, gpu.Radius),
            label + ": owned-particle seed fits inside the actual glass");
        seed = vessel.LocalToWorld(local);
        GpuLiquidParticle baseline = ProbeParticle(seed, vessel.Id);
        Near(baseline.Position, seed, label + ": one owned particle is stationary without nearby ice");
        ice.Teleport(seed - ice.PointAt(edgeLocal, Vector2.zero, 0), 0);
        GpuLiquidParticle ownedContact = ProbeParticle(seed, vessel.Id);
        Require(Vector2.Distance(ownedContact.Position, seed) > .005f,
            label + ": unheld glass positive control permits ice-to-liquid contact");
        Require(hand.Pick(vessel, vessel.Position), label + ": pick glass while ice intersects its liquid");
        GpuLiquidParticle heldContents = ProbeParticle(seed, vessel.Id);
        Require(ice.ContainingVesselId == vessel.Id && Vector2.Distance(heldContents.Position, seed) > .005f,
            label + ": held glass liquid retains contact with its contained ice");
        Require(heldContents.VesselId == vessel.Id,
            label + ": held glass retains its liquid owner ID");
        hand.ReleaseWithVelocity(Vector2.zero);
    }

    private void ValidateHeldVesselPassesThroughFreeLiquid(FluidExperimentMode mode, FluidExperimentBody vessel)
    {
        string label = mode + " held vessel versus owner-zero liquid";
        Vector2 start = new Vector2(3, 5), finish = new Vector2(7, 5);
        Vector2 local = vessel.contentRegions[0].center;
        vessel.Teleport(start, 0);
        Vector2 seed = vessel.PointAt(local, finish, 0);
        Require(!vessel.ContainsLiquid(seed), label + ": free seed begins outside the actual interior");
        GpuLiquidParticle initial = ProbeParticle(seed, 0);
        Require(initial.VesselId == 0, label + ": initially free particle stays unowned");
        Near(initial.Position, seed, label + ": initially free particle has no external impulse");
        Require(hand.Pick(vessel, start), label + ": pick vessel for lateral sweep");
        bool wallMovedParticle = false;
        for (int step = 1; step <= 16; step++)
        {
            hand.MoveHeld(Vector2.Lerp(start, finish, step / 16f));
            ApplyHeldPose(); world.TickLiquid(.02f); gpu.ReadbackNow();
            Require(gpu.ActiveCount == 1, label + ": sweep retains the free particle");
            GpuLiquidParticle particle = gpu.Snapshot.Single(p => p.Active != 0);
            Require(particle.VesselId == 0, label + ": held wall crossing never captures owner-zero liquid");
            wallMovedParticle |= Vector2.Distance(particle.Position, seed) > gpu.Radius;
        }
        Require(vessel.ContainsLiquid(seed) && wallMovedParticle,
            label + ": held wall displaces free liquid without absorbing it through its side");
        for (int step = 0; step < 8; step++) world.TickLiquid(.02f);
        gpu.ReadbackNow();
        GpuLiquidParticle enclosed = gpu.Snapshot.Single(p => p.Active != 0);
        Require(enclosed.VesselId == 0, label + ": continued overlap does not convert free liquid into held contents");
        hand.ReleaseWithVelocity(Vector2.zero);
        world.TickLiquid(.02f); gpu.ReadbackNow();
        Require(gpu.Snapshot.Single(p => p.Active != 0).VesselId == 0,
            label + ": release does not bypass the open-mouth entry requirement");
    }

    private GpuLiquidParticle ProbeParticle(Vector2 seed, uint owner)
    {
        foreach (FluidExperimentBody body in world.Items) body.SynchronizeHistory();
        Physics2D.SyncTransforms();
        gpu.ResetSimulation();
        ItemDef ingredient = world.Items.First(body => body.ingredient != null).ingredient;
        Require(gpu.TryEmit(seed, Vector2.zero, ingredient, gpu.ParticleVolumeMl, owner),
            "Single-particle GPU collision probe accepts seed");
        gpu.Step(.02f);
        gpu.ReadbackNow();
        Require(gpu.ActiveCount == 1, "Single-particle GPU collision probe retains one active particle");
        return gpu.Snapshot.Single(particle => particle.Active != 0);
    }

    private bool PairIgnored(FluidExperimentBody a, FluidExperimentBody b, bool expected)
    {
        var pairs = from c in a.solidColliders where c != null && c.enabled
                    from d in b.solidColliders where d != null && d.enabled select new[] { c, d };
        return pairs.Any() && pairs.All(pair => Physics2D.GetIgnoreCollision(pair[0], pair[1]) == expected);
    }

    private bool FloorIgnored(FluidExperimentBody body, bool expected)
    {
        var pairs = from c in body.solidColliders where c != null && c.enabled
                    from d in world.floorColliders where d != null && d.enabled select new[] { c, d };
        return pairs.Any() && pairs.All(pair => Physics2D.GetIgnoreCollision(pair[0], pair[1]) == expected);
    }

    private void Near(Vector2 actual, Vector2 expected, string label)
    {
        Require(Vector2.Distance(actual, expected) < .0001f,
            label + " (actual " + actual.ToString("F5") + ", expected " + expected.ToString("F5") + ")");
    }

    private void Require(bool condition, string label)
    {
        report.Add((condition ? "PASS " : "FAIL ") + label);
        if (!condition) throw new Exception(label);
    }

    private void Finish(bool success, string detail)
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        if (controlledPhysics)
        {
            Physics2D.simulationMode = originalMode;
            Physics2D.gravity = originalGravity;
        }
        report.Add(detail);
        ExperimentSwapValidation.Finish(success, string.Join("\n", report) + "\n");
    }
}
