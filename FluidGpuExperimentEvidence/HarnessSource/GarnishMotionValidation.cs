using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

public static class ExperimentGarnishMotionValidation
{
    const string Key = "FluidExperiment.GarnishMotionValidation";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += s =>
    {
        if (s != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key); new GameObject("GarnishMotionValidation").AddComponent<GarnishMotionValidationRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

public sealed class GarnishMotionValidationRunner : MonoBehaviour
{
    readonly List<string> report = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<FluidExperimentBody> pieces = new List<FluidExperimentBody>();
    FluidExperimentComparison comparison;
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentInteractor hand;
    FluidExperimentBody glass;
    FluidExperimentGarnishSource[] supplies;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool ok, string text) { report.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) throw new Exception(text); }
    void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) errors.Add(message); }
    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        while (!comparison.Ready) yield return null;
        var simulation = Physics2D.simulationMode; var gravity = Physics2D.gravity;
        bool success = false;
        try
        {
            Check(Application.isBatchMode && comparison.ActiveMode == FluidExperimentMode.FCoherentLiquid, "Hidden batch starts in F");
            world = comparison.World; gpu = comparison.Gpu; hand = world.interactor;
            comparison.automaticScenario = false; comparison.showControls = false;
            world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
            supplies = world.GetComponentsInChildren<FluidExperimentGarnishSource>();
            glass = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = new Vector2(0, -9.81f);
            foreach (var supply in supplies)
            {
                foreach (float tilt in new[] { 0f, 35f }) WallMotion(supply, tilt);
                HeldIceOverlap(supply);
                NoFeedback(supply);
                Flow(supply);
                CarryAndEnvironment(supply);
                OtherReceivers(supply);
                RotationResponse(supply);
                MixedContact(supply, false);
                MixedContact(supply, true);
            }
            Reset(); comparison.SwitchMode(FluidExperimentMode.DImprovedSurface);
            world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
            var legacy = Spawn(supplies[0], glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center));
            Check(!legacy.UsesLiquidGarnishMotion && legacy.Body.bodyType == RigidbodyType2D.Dynamic, "D keeps legacy dynamic garnish physics");
            Reset(); comparison.SwitchMode(FluidExperimentMode.FCoherentLiquid);
            world.enabled = false; hand.enabled = false;
            Check(supplies.All(s => s != null) && world.Items.All(b => b.kind != LabItemKind.Garnish), "Mode switch/reset removes placed garnish and preserves supplies");
            Check(errors.Count == 0, "No Unity errors or assertions"); success = true;
        }
        catch (Exception ex) { report.Add(ex.ToString()); }
        finally
        {
            Physics2D.simulationMode = simulation; Physics2D.gravity = gravity;
            Application.logMessageReceived -= Log;
            report.AddRange(errors);
            File.WriteAllText(Path.Combine(Evidence, "garnish-motion-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(Evidence, "garnish-motion-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
    void Reset()
    {
        hand.ReleaseWithVelocity(Vector2.zero);
        foreach (var b in pieces) if (b != null) { b.gameObject.SetActive(false); Destroy(b.gameObject); }
        pieces.Clear();
        foreach (var b in world.Items.ToArray()) { b.SetHeld(false); b.Teleport(new Vector2(-60 - b.Id * 3, 20), 0); b.Body.simulated = false; b.pourMlPerSecond = 0; }
        gpu.ResetSimulation();
        glass.Body.simulated = true; glass.Body.bodyType = RigidbodyType2D.Kinematic;
        glass.Teleport(new Vector2(0, 1.5f), 0); glass.SetSealed(false);
        foreach (var b in world.Items) { b.transform.SetPositionAndRotation(b.Position, Quaternion.Euler(0, 0, b.Angle)); b.SynchronizeHistory(); }
        Physics2D.SyncTransforms();
    }
    FluidExperimentBody Spawn(FluidExperimentGarnishSource s, Vector2 position)
    { var b = Instantiate(s.garnishPrefab, position, Quaternion.identity, world.transform); b.Teleport(position, 0); pieces.Add(b); return b; }
    void Step(bool read = false)
    {
        world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms();
        Physics2D.Simulate(.02f); world.TickLiquid(.02f);
        if (read) gpu.ReadbackNow();
    }
    float Separation(FluidExperimentBody b) => b.solidColliders.Min(c => glass.solidColliders.Min(d => c.Distance(d).distance));
    // Native contacts admit solver slop. Keep it below one visible source pixel,
    // rather than requiring the old kinematic controller's positive skin gap.
    float NativeSlop(FluidExperimentBody b) => b.collisionProfile.sourcePixelSize + .0005f;
    void WallMotion(FluidExperimentGarnishSource s, float tilt)
    {
        Reset(); glass.Teleport(glass.Position, tilt);
        Rect r = glass.collisionProfile.InteriorBounds;
        var b = Spawn(s, glass.LocalToWorld(new Vector2(r.xMin - 1, r.center.y)));
        Check(hand.Pick(b, b.Position), s.name + " picks fresh garnish");
        Check(b.solidColliders.All(c => c.isTrigger), "Held garnish disables physical contact immediately");
        Vector2 across = glass.LocalToWorld(new Vector2(r.xMax + 1, r.center.y));
        hand.MoveHeld(across); Step();
        Check(Vector2.Distance(b.Position, across) < .0001f, "Held garnish freely crosses the entire glass");
        hand.MoveHeld(glass.LocalToWorld(r.center)); Step();
        Check(glass.ContainsLiquid(b.Position), "Held garnish can be positioned directly inside a glass");
        Check(b.GetComponentInChildren<SpriteRenderer>().sortingOrder < glass.GetComponentsInChildren<SpriteRenderer>().Max(v => v.sortingOrder), "Contained held garnish is drawn behind glass front");
        Vector2 wall = glass.LocalToWorld(new Vector2(r.xMax, r.center.y));
        hand.MoveHeld(wall); Step();
        Check(Vector2.Distance(b.Position, wall) < .0001f && Separation(b) < -.001f,
            "Held garnish can overlap a real glass wall without position correction");
        hand.BeginRotation(); hand.RotateBy(850); Step();
        Check(Mathf.Abs(b.TargetAngle - 850) < .01f, "Held rotation is not clipped by overlapping walls");
        hand.RotateBy(-5); Step();
        Check(Mathf.Abs(b.TargetAngle - 845) < .01f, "Held reverse rotation follows the full requested angle");
        hand.EndRotation(b.Position); hand.AdvanceUprightReturn(.2f, b.Position); Step();
        Check(Vector2.Distance(b.Position, wall) < .0001f && b.solidColliders.All(c => c.isTrigger),
            "Held upright return does not resolve overlapping walls");
        bool dropped = hand.Drop(b.Position);
        if (!dropped)
        {
            Vector2 restore = b.Position;
            for (int i = 0; i < 10; i++)
            {
                var d = b.solidColliders[0].Distance(glass.solidColliders[0]);
                report.Add($"DROP-DIAGNOSTIC tick={i} position={b.Position:F6} angle={b.Angle:R} gap={d.distance:R} normal={d.normal:F6} overlap={d.isOverlapped} valid={d.isValid}");
                b.Body.position += d.normal * (d.distance - .005f); Physics2D.SyncTransforms();
            }
            b.Body.position = restore; Physics2D.SyncTransforms();
        }
        Check(dropped && hand.Held == null, "Click-drop resolves a wall overlap and releases the garnish");
        Step();
        Check(b.Body.bodyType == RigidbodyType2D.Dynamic && b.solidColliders.All(c => !c.isTrigger)
            && !Physics2D.GetIgnoreCollision(b.solidColliders[0], glass.solidColliders[0]) && Separation(b) >= -NativeSlop(b),
            "Drop restores solid native contacts and leaves the glass wall safely");
        Check(!b.CanBePicked, "Click-dropped garnish still cannot be picked again");
        // Keep the released-motion regression independent of which side of the wall
        // was nearest to the overlapping click-drop pose.
        b.gameObject.SetActive(false);
        b = Spawn(s, glass.LocalToWorld(r.center));
        hand.Pick(b, b.Position);
        hand.ReleaseWithVelocity(glass.PointAt(Vector2.right * 6, Vector2.zero, tilt), 180);
        float worst = 100; var positions = new List<Vector2>();
        for (int i = 0; i < 260; i++)
        {
            world.SendMessage("FixedUpdate"); float before = Separation(b);
            Physics2D.SyncTransforms(); float synced = Separation(b);
            Physics2D.Simulate(.02f); float simulated = Separation(b);
            world.TickLiquid(.02f); float after = Separation(b);
            if (after < -NativeSlop(b)) report.Add($"CONTACT tick={i} before={before:R} synced={synced:R} simulated={simulated:R} after={after:R} pose={b.Position:F6}/{b.Angle:R}");
            worst = Mathf.Min(worst, after); if (i >= 210) positions.Add(b.Position);
        }
        float span = Vector2.Distance(new Vector2(positions.Min(p => p.x), positions.Min(p => p.y)), new Vector2(positions.Max(p => p.x), positions.Max(p => p.y)));
        report.Add($"MEASURE {s.name} tilt={tilt} released separation={worst:R} restSpan={span:R}");
        Check(worst >= -NativeSlop(b) && glass.ContainsLiquid(b.Position), "Native released contact stays within one pixel of wall and does not escape");
        Check(span < .012f, "Wall contact settles without sustained jitter");
        Check(!hand.Pick(b, b.Position) && !b.CanBePicked, "Placed garnish cannot be picked again");
        Capture(s.name + "-wall-" + tilt, glass.LocalToWorld(r.center), 1.8f);
        Check(s.TryDispense(hand, s.transform.position) && hand.Held != b, "Supply still dispenses a fresh piece");
        Step(); Check(hand.Held.GetComponentInChildren<SpriteRenderer>().sortingOrder > s.GetComponent<SpriteRenderer>().sortingOrder, "Fresh garnish stays visible over supply jar");
        pieces.Add(hand.Held); hand.ReleaseWithVelocity(Vector2.zero);
    }
    void HeldIceOverlap(FluidExperimentGarnishSource s)
    {
        Reset(); var gravity = Physics2D.gravity; Physics2D.gravity = Vector2.zero;
        try
        {
            var prefab = world.Items.First(v => v.kind == LabItemKind.IceBucket).icePrefab;
            var ice = Instantiate(prefab, new Vector2(-3, 3), Quaternion.identity, world.transform); pieces.Add(ice);
            var b = Spawn(s, new Vector2(-4, 3)); hand.Pick(b, b.Position);
            Vector2 start = ice.Position; float angle = ice.Angle;
            hand.MoveHeld(start); Step();
            Check(b.solidColliders.Min(c => ice.solidColliders.Min(d => c.Distance(d).distance)) < -.02f,
                "Held ice-contact fixture actually overlaps the ice");
            hand.BeginRotation();
            for (int i = 0; i < 20; i++) { hand.RotateBy(20); Step(); }
            Check(Vector2.Distance(ice.Position, start) < .0001f && Mathf.Abs(ice.Angle - angle) < .001f
                && ice.Body.linearVelocity.sqrMagnitude < .000001f && Mathf.Abs(ice.Body.angularVelocity) < .001f,
                "Moving/rotating held garnish does not push or spin overlapping dynamic ice");
            hand.ReleaseWithVelocity(Vector2.zero);
            Check(b.solidColliders.All(c => !c.isTrigger) && !Physics2D.GetIgnoreCollision(b.solidColliders[0], ice.solidColliders[0]),
                "Release restores garnish/ice solid contact");
        }
        finally { Physics2D.gravity = gravity; }
    }
    void EmitPatch(Vector2 center, Vector2 velocity)
    {
        var ingredient = world.Items.First(b => b.ingredient != null).ingredient;
        for (int y = -2; y <= 2; y++) for (int x = -2; x <= 2; x++)
            if (!gpu.TryEmit(center + new Vector2(x, y) * .11f, velocity, ingredient, .5f, glass.Id)) throw new Exception("Emission rejected");
    }
    void RotationResponse(FluidExperimentGarnishSource s)
    {
        Reset(); var b = Spawn(s, new Vector2(0, 4));
        b.Body.bodyType = RigidbodyType2D.Kinematic; Step();
        Check(b.Body.bodyType == RigidbodyType2D.Dynamic, "Existing unheld kinematic piece migrates to native dynamics");
        hand.Pick(b, b.Position); hand.ReleaseWithVelocity(Vector2.zero, 180);
        Check(b.Body.bodyType == RigidbodyType2D.Dynamic && b.Body.constraints == RigidbodyConstraints2D.None,
            "Released garnish is a native dynamic body with free rotation");
        for (int i = 0; i < 10; i++) Step();
        report.Add($"MEASURE {s.name} air angle={b.Angle:R} spin={b.GarnishAngularVelocity:R}");
        Check(b.GarnishAngularVelocity > 135 && b.Angle > 30, "Released rotation retains inertia instead of rapidly stopping in air");

        // Controlled, completed readback fixtures isolate velocity gradient and unequal
        // immersion from pressure/gravity; the main flow tests still dispatch the real GPU.
        void Sample(float angle, float waterline, float curl, Vector2 common, out float spin, out float torque)
        {
            Reset(); Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
            b = Spawn(s, center); b.Teleport(center, angle); Physics2D.SyncTransforms(); world.RefreshIceContainment();
            EmitPatch(center, Vector2.zero); world.TickLiquid(.02f); gpu.ReadbackNow();
            var data = gpu.Snapshot; Array.Clear(data, 0, data.Length); int n = 0;
            for (int y = -8; y <= 8; y++) for (int x = -8; x <= 8; x++)
            {
                Vector2 offset = new Vector2(x, y) * .04f;
                if (offset.y > waterline) continue;
                data[n++] = new GpuLiquidParticle { Active = 1, VolumeMl = .5f, VesselId = glass.Id,
                    Position = center + offset, Velocity = common + new Vector2(-offset.y, offset.x) * curl };
            }
            Check(gpu.TrySampleGarnishFlow(b, out _, out _, out spin, out torque), "Angular fixture has fresh, owned liquid support");
        }
        Sample(0, 1, 0, new Vector2(2, 1), out float uniformSpin, out float uniformTorque);
        Check(Mathf.Abs(uniformSpin) < .01f && Mathf.Abs(uniformTorque) < .01f, "Uniform fully submerged translation creates no artificial rotation");
        Sample(0, 1, 2, Vector2.zero, out float positive, out _);
        float before = b.Angle; Step();
        Check(positive > 20 && b.GarnishAngularVelocity > .1f && b.Angle > before, "Counterclockwise flow generates actual garnish rotation");
        Sample(0, 1, -2, Vector2.zero, out float negative, out _);
        before = b.Angle; Step();
        Check(negative < -20 && b.GarnishAngularVelocity < -.1f && b.Angle < before, "Reversed flow reverses actual garnish rotation");
        Sample(35, -.065f, 0, Vector2.zero, out _, out float liftA);
        Sample(215, -.065f, 0, Vector2.zero, out _, out float liftB);
        // A half-turn exchanges the two probes but preserves the same physical leveling direction.
        report.Add($"MEASURE {s.name} angular flow={positive:R}/{negative:R} unequal lift={liftA:R}/{liftB:R}");
        Check(liftA < -5 && liftB < -5 && Mathf.Abs(liftA - liftB) < .01f, "Unequal immersion levels the long axis consistently after a half-turn");

        Reset(); var r = glass.collisionProfile.InteriorBounds;
        b = Spawn(s, glass.LocalToWorld(new Vector2(r.center.x, r.yMin + .45f)));
        b.Teleport(b.Position, 40); float initial = b.Angle; float worst = 100;
        var angles = new List<float>(); var poses = new List<Vector2>();
        for (int i = 0; i < 450; i++)
        {
            Step(); worst = Mathf.Min(worst, Separation(b));
            if (i >= 400) { angles.Add(b.Angle); poses.Add(b.Position); }
        }
        float turn = Mathf.Abs(Mathf.DeltaAngle(initial, b.Angle));
        float jitter = angles.Max() - angles.Min();
        float motion = Vector2.Distance(new Vector2(poses.Min(p => p.x), poses.Min(p => p.y)), new Vector2(poses.Max(p => p.x), poses.Max(p => p.y)));
        report.Add($"MEASURE {s.name} resting roll={turn:R} angularSpan={jitter:R} positionSpan={motion:R} separation={worst:R}");
        Check(turn > 5, "Off-center wall contact tips/rolls a released garnish without initial spin");
        Check(worst >= -NativeSlop(b) && jitter < 2 && motion < .012f, "Native rolling settles within one pixel of wall without sustained jitter");
        Capture(s.name + "-rotation-rest", glass.LocalToWorld(r.center), 1.8f);
    }
    void OtherReceivers(FluidExperimentGarnishSource s)
    {
        var highball = glass;
        try
        {
            foreach (var receiver in world.Items.Where(v => v.kind == LabItemKind.Glass && v != highball).ToArray())
            {
                glass = receiver; Reset(); Rect r = glass.collisionProfile.InteriorBounds;
                var b = Spawn(s, glass.LocalToWorld(new Vector2(r.center.x, r.yMax + .8f)));
                hand.Pick(b, b.Position); hand.MoveHeld(glass.LocalToWorld(r.center)); Step();
                Check(Separation(b) >= -.0005f, s.name + " enters " + receiver.name + " without wall penetration");
                hand.ReleaseWithVelocity(Vector2.right * 2, 45);
                float worst = 100;
                for (int i = 0; i < 160; i++) { Step(); worst = Mathf.Min(worst, Separation(b)); }
                report.Add($"MEASURE receiver={receiver.name} garnish={s.name} clearance={worst:R}");
                Check(worst >= -NativeSlop(b), "Native curved/sloped receiver contact stays within one pixel of wall");
            }
        }
        finally { glass = highball; Reset(); }
    }
    void CarryAndEnvironment(FluidExperimentGarnishSource s)
    {
        Reset(); var b = Spawn(s, glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center));
        var gravity = Physics2D.gravity; Physics2D.gravity = Vector2.zero;
        try
        {
            Step(); world.RefreshIceContainment();
            Check(b.ContainingVesselId == glass.Id, "Released garnish acquires glass ownership");
            Vector2 position = b.Position, local = glass.WorldToLocal(position); float angle = b.Angle;
            glass.SetHeld(true); glass.SetHeldPose(glass.Position + new Vector2(.02f, .01f), 10);
            glass.SendMessage("ApplyHeldPose");
            Check(Separation(b) > .03f, "Carrier independence fixture keeps garnish clear of all glass walls");
            Check(Vector2.Distance(position, b.Position) < .00001f && Mathf.Abs(b.Angle - angle) < .00001f,
                "Moving/rotating a held glass never directly rewrites free garnish pose");
            Step();
            report.Add($"MEASURE {s.name} free carrier worldDrift={Vector2.Distance(position, b.Position):R} angleDrift={Mathf.Abs(b.Angle - angle):R} localDrift={Vector2.Distance(local, glass.WorldToLocal(b.Position)):R} gap={Separation(b):R}");
            Check(Separation(b) > .03f && Vector2.Distance(position, b.Position) < .0001f && Mathf.Abs(b.Angle - angle) < .001f,
                "Contact-free garnish keeps world pose while glass moves and tilts");
            Check(Vector2.Distance(local, glass.WorldToLocal(b.Position)) > .03f,
                "Garnish is free to move relative to its held glass");
            Check(!Physics2D.GetIgnoreCollision(b.solidColliders[0], glass.solidColliders[0]),
                "Native garnish contacts remain enabled against its held glass");
            Vector2 velocity = b.GarnishVelocity; float spin = b.GarnishAngularVelocity;
            glass.Release(Vector2.right * 3, 90);
            Check(Vector2.Distance(velocity, b.GarnishVelocity) < .0001f && Mathf.Abs(spin - b.GarnishAngularVelocity) < .001f,
                "Releasing the glass does not inject an unearned velocity or spin into garnish");
        }
        finally { Physics2D.gravity = gravity; }
        Reset(); b = Spawn(s, glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center));
        Step(); Vector2 oldLocal = glass.WorldToLocal(b.Position);
        glass.SetHeld(true); glass.SetHeldPose(glass.Position + Vector2.right * 2, 0); Step();
        report.Add($"MEASURE {s.name} fast wall local={glass.WorldToLocal(b.Position):F6} drift={Vector2.Distance(oldLocal, glass.WorldToLocal(b.Position)):R} gap={Separation(b):R}");
        Check(glass.ContainsLiquid(b.Position) && Separation(b) >= -NativeSlop(b)
            && Vector2.Distance(oldLocal, glass.WorldToLocal(b.Position)) > .1f,
            "Fast held wall crossing recovers only contact, without rigidly carrying contents");
        glass.SetHeld(true); glass.SetHeldPose(glass.Position, 180); Step();
        for (int i = 0; i < 150; i++) Step();
        Check(b.ContainingVesselId == 0, "Inverted open glass releases garnish through mouth");
        Reset(); b = Spawn(s, new Vector2(0, 4)); hand.Pick(b, b.Position);
        hand.MoveHeld(new Vector2(100, 100)); Step();
        Check(Vector2.Distance(b.Position, new Vector2(100, 100)) < .0001f && b.solidColliders.All(c => c.isTrigger),
            "Held garnish is not clamped by ceiling or side walls");
        foreach (var h in world.EnvironmentHulls)
        {
            hand.MoveHeld(h.Collider.bounds.center); Step();
            Check(Vector2.Distance(b.Position, h.Collider.bounds.center) < .0001f,
                "Held garnish can overlap a world boundary without being pushed out");
        }
        hand.MoveHeld(new Vector2(0, 4)); Step();
        hand.ReleaseWithVelocity(new Vector2(12, 12));
        for (int i = 0; i < 60; i++) Step();
        Check(world.EnvironmentHulls.All(h => b.solidColliders.All(c => c.Distance(h.Collider).distance >= -NativeSlop(b))), "Released garnish respects ceiling and side walls");
    }
    void NoFeedback(FluidExperimentGarnishSource s)
    {
        GpuLiquidParticle[] Run(bool garnish)
        {
            Reset(); Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
            if (garnish) { var b = Spawn(s, center); hand.Pick(b, center); }
            EmitPatch(center, Vector2.zero);
            for (int i = 0; i < 20; i++) Step();
            gpu.ReadbackNow(); return gpu.Snapshot.Where(p => p.Active != 0).ToArray();
        }
        var before = Run(false); var after = Run(true);
        float delta = before.Zip(after, (a, b) => Vector2.Distance(a.Position, b.Position) + Vector2.Distance(a.Velocity, b.Velocity)).DefaultIfEmpty(100).Max();
        report.Add($"MEASURE {s.name} liquid feedback delta={delta:R}");
        Check(before.Length == after.Length && delta < .00001f && Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .001, "Garnish does not displace/accelerate liquid or change its ledger");
    }
    void Flow(FluidExperimentGarnishSource s)
    {
        Reset(); Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        var b = Spawn(s, center); world.RefreshIceContainment();
        EmitPatch(center, Vector2.right * 1.5f); world.TickLiquid(.02f); gpu.ReadbackNow();
        Check(gpu.TrySampleGarnishFlow(b, out var velocity, out float submerged) && velocity.x > .2f && submerged > .2f, "Completed local liquid snapshot supplies flow and immersion");
        float startX = b.Position.x;
        Step();
        Check(b.GarnishVelocity.x > .01f && b.Position.x > startX, "Released garnish follows liquid flow");
        for (int i = 0; i < 18; i++) Step();
        Check(!gpu.TrySampleGarnishFlow(b, out _, out _), "Stale snapshot cannot keep driving garnish");
        gpu.ResetSimulation();
        Check(!gpu.TrySampleGarnishFlow(b, out _, out _), "Reset invalidates old liquid support");
        // Observe long settling using completed readback at the normal 5 Hz cadence.
        Reset(); gpu.Fill(glass, world.Items.First(v => v.ingredient != null).ingredient, 60);
        for (int i = 0; i < 100; i++) Step(i % 10 == 0);
        b = Spawn(s, glass.LocalToWorld(new Vector2(0, -.1f)));
        var tailAngles = new List<float>(); var tailPositions = new List<Vector2>(); float peakSpin = 0;
        for (int i = 0; i < 500; i++)
        {
            Step(i % 10 == 0);
            if (i >= 400)
            {
                tailAngles.Add(b.Angle); tailPositions.Add(b.Position);
                peakSpin = Mathf.Max(peakSpin, Mathf.Abs(b.GarnishAngularVelocity));
            }
        }
        float angularSpan = tailAngles.Max() - tailAngles.Min();
        float positionSpan = Vector2.Distance(new Vector2(tailPositions.Min(p => p.x), tailPositions.Min(p => p.y)),
            new Vector2(tailPositions.Max(p => p.x), tailPositions.Max(p => p.y)));
        report.Add($"MEASURE {s.name} float position={b.Position:F6} velocity={b.GarnishVelocity:F6} clearance={Separation(b):R} angle={b.Angle:R} angularSpan={angularSpan:R} peakSpin={peakSpin:R} positionSpan={positionSpan:R}");
        Check(Separation(b) >= -.0005f && float.IsFinite(b.GarnishVelocity.magnitude), "Flow-following garnish remains finite and outside glass walls");
        Check(angularSpan < 3 && peakSpin < 5 && positionSpan < .02f, "Real GPU surface support settles without sustained angular or positional jitter at 5 Hz readback");
        Capture(s.name + "-flow", glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center), 1.8f);
        // Exercise the user's actual trigger with real liquid, not just a settled glass.
        glass.SetHeld(true); Vector2 origin = glass.Position;
        var relativeAngles = new List<float>(); var relativePositions = new List<Vector2>(); float movingGap = 100;
        for (int i = 0; i < 150; i++)
        {
            float phase = (i + 1) * Mathf.PI * 2 / 150;
            glass.SetHeldPose(origin + Vector2.right * (.3f * Mathf.Sin(phase)), 25 * Mathf.Sin(phase));
            Step(i % 10 == 0);
            relativeAngles.Add(Mathf.DeltaAngle(glass.Angle, b.Angle));
            relativePositions.Add(glass.WorldToLocal(b.Position));
            movingGap = Mathf.Min(movingGap, Separation(b));
        }
        float relativeTurn = relativeAngles.Max() - relativeAngles.Min();
        float relativeTravel = Vector2.Distance(new Vector2(relativePositions.Min(p => p.x), relativePositions.Min(p => p.y)),
            new Vector2(relativePositions.Max(p => p.x), relativePositions.Max(p => p.y)));
        report.Add($"MEASURE {s.name} wet moving glass relativeTurn={relativeTurn:R} relativeTravel={relativeTravel:R} minGap={movingGap:R}");
        Check(relativeTurn > 15 && relativeTravel > .05f && b.Body.bodyType == RigidbodyType2D.Dynamic,
            "Real liquid garnish changes position and angle relative to a moving/tilting glass");
        Check(movingGap >= -NativeSlop(b) && glass.ContainsLiquid(b.Position),
            "Moving wet glass retains garnish through actual contacts within one-pixel wall tolerance");
    }
    void MixedContact(FluidExperimentGarnishSource s, bool wet)
    {
        Reset(); Rect r = glass.collisionProfile.InteriorBounds;
        var prefab = world.Items.First(v => v.kind == LabItemKind.IceBucket).icePrefab;
        var ice = Instantiate(prefab, glass.LocalToWorld(new Vector2(r.center.x, r.yMin + .27f)), Quaternion.identity, world.transform);
        pieces.Add(ice);
        var b = Spawn(s, glass.LocalToWorld(new Vector2(r.center.x, r.yMin + .7f)));
        Physics2D.SyncTransforms(); world.RefreshIceContainment(); glass.SetHeld(true);
        Check(!Physics2D.GetIgnoreCollision(b.solidColliders[0], ice.solidColliders[0]), "Garnish/ice native contacts are enabled in a held glass");
        if (wet) gpu.Fill(glass, world.Items.First(v => v.ingredient != null).ingredient, 40);
        var positions = new List<Vector2>(); var angles = new List<float>(); int contact = 0; float gap = 100;
        var contacts = new ContactPoint2D[32];
        for (int i = 0; i < 600; i++)
        {
            Step(i % 10 == 0);
            gap = Mathf.Min(gap, Separation(b));
            if (i < 500) continue;
            positions.Add(b.Position); angles.Add(b.Angle);
            int count = b.Body.GetContacts(contacts);
            for (int j = 0; j < count; j++)
                if (contacts[j].collider.attachedRigidbody == ice.Body || contacts[j].otherCollider.attachedRigidbody == ice.Body)
                { contact++; break; }
        }
        float span = Vector2.Distance(new Vector2(positions.Min(p => p.x), positions.Min(p => p.y)),
            new Vector2(positions.Max(p => p.x), positions.Max(p => p.y)));
        float turn = angles.Max() - angles.Min();
        report.Add($"MEASURE {s.name} mixed wet={wet} span={span:R} angularSpan={turn:R} iceContactSamples={contact} gap={gap:R}");
        Check(span < .01f && turn < 2 && gap >= -NativeSlop(b)
            && b.ContainingVesselId == glass.Id && ice.ContainingVesselId == glass.Id,
            "Mixed native garnish/ice settle without visible sustained jitter or wall escape");
        if (!wet) Check(contact > 40, "Dry mixed fixture rests on actual ice contacts");
        else
        {
            gpu.ReadbackNow();
            Check(Math.Abs(gpu.Ledger.Total.ActiveMl - 40) < .01 && Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .01,
                "Wet mixed fixture conserves all 40 ml");
            Capture(s.name + "-native-mixed", glass.LocalToWorld(r.center), 1.8f);
        }
    }
    void Capture(string name, Vector2 center, float size)
    {
        var camera = hand.inputCamera; var old = camera.transform.position; float oldSize = camera.orthographicSize;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var target = new RenderTexture(640, 640, 24); target.Create(); var texture = new Texture2D(640, 640, TextureFormat.RGBA32, false);
        try
        {
            foreach (var b in world.Items) b.transform.SetPositionAndRotation(b.Position, Quaternion.Euler(0, 0, b.Angle));
            camera.transform.position = new Vector3(center.x, center.y, old.z); camera.orthographicSize = size; camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(Evidence, name + ".png"), texture.EncodeToPNG());
        }
        finally { camera.targetTexture = oldTarget; camera.transform.position = old; camera.orthographicSize = oldSize; RenderTexture.active = oldActive; target.Release(); Destroy(target); Destroy(texture); }
    }
}
