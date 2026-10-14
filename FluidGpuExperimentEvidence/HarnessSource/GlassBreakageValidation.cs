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

public static class ExperimentGlassBreakageValidation
{
    const string Key = "FluidExperiment.GlassBreakageValidation";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += s =>
    {
        if (s != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key); new GameObject("GlassBreakageValidation").AddComponent<GlassBreakageValidationRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

// Deliberate buffer reads/writes and scripted physics are confined to the disposable GPU harness.
// Native collisions test the trigger; controlled break transactions separately isolate conservation.
public sealed class GlassBreakageValidationRunner : MonoBehaviour
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    readonly List<string> report = new List<string>(), errors = new List<string>();
    FluidExperimentComparison comparison;
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentInteractor hand;
    FluidExperimentBody glass;
    FluidExperimentBreakableGlass breakable;
    FluidExperimentBreakageProfile sourceProfile, testProfile;
    ItemDef ingredient;
    float floorY, sourceGravity, sourceLinearDamping, sourceAngularDamping;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    object Field(object o, string name) => o.GetType().GetField(name, Members).GetValue(o);
    void SetField(object o, string name, object value) => o.GetType().GetField(name, Members).SetValue(o, value);
    object Invoke(object o, string name, params object[] args) => o.GetType().GetMethod(name, Members).Invoke(o, args);
    T Value<T>(object o, string name) => (T)o.GetType().GetField(name, Members).GetValue(o);
    bool Near(Vector2 a, Vector2 b, float tolerance = .0001f) => Vector2.Distance(a, b) <= tolerance;
    void Check(bool ok, string text) { report.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) throw new Exception(text); }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        while (!comparison.Ready) yield return null;
        var oldSimulation = Physics2D.simulationMode; var oldGravity = Physics2D.gravity;
        bool success = false;
        try
        {
            world = comparison.World; gpu = comparison.Gpu; hand = world.interactor;
            comparison.automaticScenario = false; comparison.showControls = false;
            world.seedLiquids = false;
            DisableAutomaticSimulation();
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = new Vector2(0, -9.81f);
            glass = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.ToLowerInvariant().Contains("hurricane"));
            breakable = glass.GetComponent<FluidExperimentBreakableGlass>();
            ingredient = world.Items.First(b => b.ingredient != null).ingredient;
            floorY = world.floorColliders.First(c => c != null && c.bounds.min.x < 0 && c.bounds.max.x > 0).bounds.max.y;
            sourceGravity = glass.Body.gravityScale; sourceLinearDamping = glass.Body.linearDamping;
            sourceAngularDamping = glass.Body.angularDamping;
            Check(Application.isBatchMode && gpu.CohesivePhysicsActive, "Hidden batch uses actual F GPU with no desktop input");
            Check(breakable != null && breakable.profile != null, "Authored hurricane has a configured breakage component");
            sourceProfile = breakable.profile;
            testProfile = Instantiate(sourceProfile); breakable.profile = testProfile;
            Check(testProfile.shards.Length == 16 && Mathf.Abs(testProfile.minimumImpactSpeed - 6f) < .0001f
                && Mathf.Abs(testProfile.minimumImpulsePerMass - 4f) < .0001f, "Authored profile has 16 shards and tougher default impact thresholds (6/4)");
            Check(world.Items.Where(b => b != glass).All(b => b.GetComponent<FluidExperimentBreakableGlass>() == null),
                "Only hurricane is breakable in this first version");
            GentleContact();
            HeldAndAutomaticPose();
            StrongDrop();
            FilledNativeDrop();
            StrongWallImpact();
            ShardPlacementAndInheritance();
            DetachedShakerIsolation();
            LiquidAndSolidConservation();
            NativeShardRestAndCleanup();
            ResetAndModeScope();
            Check(errors.Count == 0, "No Unity errors or assertions"); success = true;
        }
        catch (Exception ex) { report.Add(ex.ToString()); }
        finally
        {
            if (breakable != null && sourceProfile != null) breakable.profile = sourceProfile;
            if (testProfile != null) Destroy(testProfile);
            Physics2D.simulationMode = oldSimulation; Physics2D.gravity = oldGravity;
            Application.logMessageReceived -= Log; report.AddRange(errors);
            File.WriteAllText(Path.Combine(Evidence, "glass-breakage-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(Evidence, "glass-breakage-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
    void DisableAutomaticSimulation()
    { world.enabled = false; hand.enabled = false; gpu.automaticReadback = false; }
    void Reset(float angle = 0, float bottomGap = 1)
    {
        world.ResetSession(); DisableAutomaticSimulation();
        Check(!breakable.IsBroken && !breakable.BreakQueued && glass.gameObject.activeInHierarchy
            && world.Items.Contains(glass) && world.GlassShards.Count == 0, "Reset restores the original hurricane and clears all shards/queued break state");
        foreach (var b in world.Items.ToArray())
        {
            b.SetHeld(false); b.Teleport(new Vector2(-60 - b.Id * 3, 20), 0);
            b.Body.simulated = false; b.pourMlPerSecond = 0;
        }
        gpu.ResetSimulation();
        glass.Body.simulated = true; glass.Body.bodyType = RigidbodyType2D.Dynamic;
        glass.Body.gravityScale = sourceGravity; glass.Body.linearDamping = sourceLinearDamping;
        glass.Body.angularDamping = sourceAngularDamping; glass.Body.constraints = RigidbodyConstraints2D.None;
        glass.Teleport(new Vector2(0, floorY + 2), angle); glass.SetSealed(false);
        Physics2D.SyncTransforms();
        glass.Teleport(glass.Position + Vector2.up * (floorY + bottomGap - glass.SolidBounds.min.y), angle);
        Physics2D.SyncTransforms(); world.RefreshCollisionPairs();
    }
    void PhysicsStep(float dt = .01f)
    { world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms(); Physics2D.Simulate(dt); }
    void Step(float dt = .01f) { PhysicsStep(dt); world.TickLiquid(dt); }
    bool UntilQueued(int maximumSteps)
    {
        for (int i = 0; i < maximumSteps; i++)
        {
            PhysicsStep();
            if (breakable.BreakQueued) return true;
            world.TickLiquid(.01f);
        }
        return false;
    }
    void QueueControlledBreak()
    {
        Invoke(breakable, "QueueImpact", 8f, glass.Body.mass * 8f, glass.Position);
        Check(breakable.BreakQueued, "Controlled transaction fixture uses a valid queued impact");
    }
    void CompleteControlledBreak() { QueueControlledBreak(); Invoke(breakable, "CompleteBreak"); }
    GpuLiquidParticle[] Read() { gpu.ReadbackNow(); return (GpuLiquidParticle[])gpu.Snapshot.Clone(); }
    void GentleContact()
    {
        Reset(0, .025f);
        bool queued = UntilQueued(180);
        report.Add($"INFO gentle impactSpeed={breakable.LastImpactSpeed:R} impulse={breakable.LastImpactImpulse:R} mass={glass.Body.mass:R}");
        Check(!queued && !breakable.IsBroken && world.GlassShards.Count == 0, "Gentle short drop and repeated resting contacts do not break the glass");
        Check(glass.SolidBounds.min.y < floorY + .05f, "Gentle fixture actually reaches the physical floor");
    }
    void HeldAndAutomaticPose()
    {
        Reset(90, .025f);
        Check(hand.Pick(glass, glass.Position), "Tilted hurricane can be picked");
        for (int i = 0; i < 8; i++) Step();
        hand.BeginRotation(); hand.RotateBy(170);
        hand.MoveHeld(glass.Position + new Vector2(1, -.4f)); Step();
        hand.EndRotation(glass.Position); hand.AdvanceUprightReturn(.2f, glass.Position); Step();
        Check(!breakable.BreakQueued && !breakable.IsBroken, "Held movement, pickup upright correction and automatic rotation return do not break the glass");
        // A real full-kinematic contact tests the held gate independently of normal ignored held/floor pairs.
        glass.Body.useFullKinematicContacts = true;
        foreach (var c in glass.solidColliders) foreach (var floor in world.floorColliders)
            if (c != null && floor != null) Physics2D.IgnoreCollision(c, floor, false);
        hand.MoveHeld(new Vector2(0, floorY)); Step();
        Check(!breakable.BreakQueued && !breakable.IsBroken, "Forced native held contact still cannot auto-break");
        glass.Body.useFullKinematicContacts = false;
        hand.ReleaseWithVelocity(Vector2.zero);
    }
    void StrongDrop()
    {
        Reset(0, 3);
        Check(UntilQueued(160), "Actual high free fall queues an automatic break at default thresholds");
        Check(!breakable.IsBroken && glass.gameObject.activeInHierarchy && world.GlassShards.Count == 0,
            "Collision callback queues once while the original remains intact until liquid tick");
        report.Add($"INFO drop impactSpeed={breakable.LastImpactSpeed:R} impulse={breakable.LastImpactImpulse:R} mass={glass.Body.mass:R}");
        Check(breakable.LastImpactSpeed >= testProfile.minimumImpactSpeed
            && breakable.LastImpactImpulse / glass.Body.mass >= testProfile.minimumImpulsePerMass,
            "Native drop crosses both speed and normalized impulse thresholds");
        world.TickLiquid(.01f);
        Check(breakable.IsBroken && !glass.gameObject.activeInHierarchy && !world.Items.Contains(glass)
            && world.GlassShards.Count == 16, "Next liquid tick atomically replaces original geometry with 16 shards");
        Invoke(breakable, "CompleteBreak");
        Invoke(world, "ProcessGlassBreakage");
        Check(world.GlassShards.Count == 16, "Repeated completion/contact processing cannot duplicate shards");
    }
    void StrongWallImpact()
    {
        Reset(25, 1);
        glass.Body.gravityScale = 0;
        var wall = world.leftBoundary;
        Check(wall != null && wall.enabled, "Wall impact uses the authored world boundary");
        glass.Teleport(glass.Position + Vector2.right * (wall.bounds.max.x + .3f - glass.SolidBounds.min.x), 25);
        glass.Body.linearVelocity = new Vector2(-8, 0);
        Physics2D.SyncTransforms();
        Check(UntilQueued(60), "Actual fast side impact also queues breakage at default thresholds");
        world.TickLiquid(.01f);
        Check(breakable.IsBroken && world.GlassShards.Count == 16, "Wall collision completes the same single break transaction");
    }
    void FilledNativeDrop()
    {
        Reset(0, 3); glass.Body.bodyType = RigidbodyType2D.Kinematic;
        float accepted = gpu.Fill(glass, ingredient, 40);
        for (int i = 0; i < 120; i++) Step(.02f);
        Read();
        Check(Mathf.Abs(accepted - 40) < .001f && gpu.VolumeIn(glass.Id) >= 39.999f,
            "Full native-drop fixture starts with 40 ml of settled liquid");
        var icePrefab = world.Items.First(b => b.kind == LabItemKind.IceBucket).icePrefab;
        var garnishPrefab = world.GetComponentsInChildren<FluidExperimentGarnishSource>()[0].garnishPrefab;
        Vector2 localCenter = glass.collisionProfile.InteriorBounds.center;
        var ice = Instantiate(icePrefab, glass.LocalToWorld(localCenter + new Vector2(-.15f, -.2f)), Quaternion.identity, world.transform);
        var garnish = Instantiate(garnishPrefab, glass.LocalToWorld(localCenter + new Vector2(.1f, .35f)), Quaternion.identity, world.transform);
        ice.SynchronizeHistory(); garnish.SynchronizeHistory(); world.RefreshIceContainment();
        Check(ice.ContainingVesselId == glass.Id && garnish.ContainingVesselId == glass.Id,
            "Full native-drop fixture includes contained ice and garnish");
        glass.Body.bodyType = RigidbodyType2D.Dynamic;
        Check(UntilQueued(180), "A real 40 ml/ice/garnish high drop queues breakage at the unchanged default thresholds");
        uint owner = glass.Id;
        var before = Read(); var composition = (float[])gpu.CompositionSnapshot.Clone();
        var ledgerBefore = gpu.Ledger.Total;
        var solids = new[] { ice, garnish }; var positions = solids.Select(b => b.Position).ToArray();
        var velocities = solids.Select(b => b.Body.linearVelocity).ToArray(); var spins = solids.Select(b => b.Body.angularVelocity).ToArray();
        report.Add($"INFO filled-drop speed={breakable.LastImpactSpeed:R} impulse={breakable.LastImpactImpulse:R}"
            + $" activeMl={ledgerBefore.ActiveMl:R} ownedMl={gpu.VolumeIn(owner):R} iceOwner={ice.ContainingVesselId} garnishOwner={garnish.ContainingVesselId}");
        Check(before.Any(p => p.Active != 0 && p.VesselId == owner), "Filled vessel still owns liquid at the actual collision callback");
        // Commit the actual native callback before advancing the next liquid tick. This lets
        // the comparison distinguish break transaction effects from subsequent free-fall physics.
        Invoke(world, "ProcessGlassBreakage");
        var after = Read(); var ledgerAfter = gpu.Ledger.Total;
        bool exact = true;
        for (int i = 0; i < before.Length; i++)
        {
            var a = before[i]; var b = after[i];
            if (a.VesselId == owner) { exact &= b.VesselId == 0; b.VesselId = owner; }
            exact &= a.Equals(b);
        }
        Check(breakable.IsBroken && world.GlassShards.Count == 16 && exact
            && composition.SequenceEqual(gpu.CompositionSnapshot),
            "Actual filled-impact transaction creates 16 shards and changes only liquid owner IDs");
        Check(ledgerBefore.GeneratedMl == ledgerAfter.GeneratedMl && ledgerBefore.ActiveMl == ledgerAfter.ActiveMl
            && ledgerBefore.GpuPendingMl == ledgerAfter.GpuPendingMl && ledgerBefore.RetiredMl == ledgerAfter.RetiredMl
            && Math.Abs(ledgerAfter.ConservationErrorMl) < .0001,
            "Actual filled-impact transaction preserves every generated, active and pending milliliter");
        for (int i = 0; i < solids.Length; i++)
            Check(solids[i].ContainingVesselId == 0 && Near(solids[i].Position, positions[i])
                && Near(solids[i].Body.linearVelocity, velocities[i]) && solids[i].Body.angularVelocity == spins[i],
                "Actual impact releases " + solids[i].kind + " without adding a second impulse or position correction");
        for (int i = 0; i < 30; i++) Step(.02f);
        Read();
        Check(gpu.Snapshot.Where(p => p.Active != 0).All(p => p.VesselId != owner)
            && Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .001
            && Math.Abs(gpu.Ledger.Total.GeneratedMl - accepted) < .001,
            "Released liquid continues 30 real native/GPU ticks without stale vessel ownership or lost accounting");
        Check(solids.All(b => b != null && b.isActiveAndEnabled && b.ContainingVesselId == 0)
            && positions.Select((p, i) => Vector2.Distance(p, solids[i].Position)).All(d => d > .05f),
            "Released ice and garnish continue moving freely after the actual filled-glass break");
        Capture("filled-glass-after-break.png", new Vector2(0, floorY - .2f), 3.0f);
        foreach (var b in solids) { b.gameObject.SetActive(false); Destroy(b.gameObject); }
    }
    void ShardPlacementAndInheritance()
    {
        Reset(37, 2);
        var intactNeighbor = world.Items.First(b => b != glass && b.kind == LabItemKind.Glass);
        intactNeighbor.Teleport(new Vector2(3, floorY + 2), 0);
        intactNeighbor.Body.simulated = true; intactNeighbor.Body.bodyType = RigidbodyType2D.Kinematic;
        float oldSeparation = testProfile.separationSpeed; testProfile.separationSpeed = 0;
        glass.Body.linearVelocity = new Vector2(1.1f, .3f); glass.Body.angularVelocity = 72;
        Vector2 sourcePosition = glass.Position; float sourceAngle = glass.Angle, sourceMass = glass.Body.mass;
        var centers = testProfile.shards.Select(s => glass.LocalToWorld(s.localCenter)).ToArray();
        var velocities = centers.Select(p => glass.Body.GetPointVelocity(p)).ToArray();
        CompleteControlledBreak();
        var shards = world.GlassShards.ToArray();
        Check(shards.Length == 16 && shards.Select(s => s.DefinitionIndex).Distinct().Count() == 16,
            "All 16 supplied fragment definitions produce exactly one shard each");
        foreach (var s in shards)
        {
            int index = s.DefinitionIndex; var definition = testProfile.shards[index];
            Check(s.Source == breakable && Near(s.Body.position, centers[index])
                && Mathf.Abs(Mathf.DeltaAngle(s.Body.rotation, sourceAngle)) < .001f,
                $"Shard {index + 1:00} starts at its rotated source centroid");
            Check(Near(s.Body.linearVelocity, velocities[index], .0002f) && Mathf.Abs(s.Body.angularVelocity - 72) < .001f,
                $"Shard {index + 1:00} inherits point velocity and free spin without a burst"
                + $" (actualVelocity={s.Body.linearVelocity:F7}, expectedVelocity={velocities[index]:F7}, actualSpin={s.Body.angularVelocity:R}, expectedSpin=72)");
            Check(s.SolidCollider != null && s.SolidCollider.enabled && !s.SolidCollider.isTrigger
                && s.SolidCollider.pathCount > 0 && s.SolidCollider.GetPath(0).Length >= 3
                && s.Body.bodyType == RigidbodyType2D.Dynamic && s.Body.constraints == RigidbodyConstraints2D.None,
                $"Shard {index + 1:00} has real polygon physics and unrestricted rotation");
            var sprite = s.GetComponentInChildren<SpriteRenderer>();
            Check(sprite != null && sprite.sprite == definition.sprite && sprite.sprite != null
                && s.GetComponent<FluidExperimentBody>() == null,
                $"Shard {index + 1:00} preserves supplied art and is outside pickable fluid bodies");
        }
        Check(Mathf.Abs(shards.Sum(s => s.Body.mass) - sourceMass) < .0001f,
            "Fragment masses sum to the original glass mass");
        float totalArea = testProfile.shards.Sum(s => s.area);
        Check(shards.All(s => Mathf.Abs(s.Body.mass / sourceMass - testProfile.shards[s.DefinitionIndex].area / totalArea) < .0001f),
            "Mass distribution follows actual fragment area");
        Check(shards.All(s => world.floorColliders.Where(c => c != null && c.enabled).All(c => !Physics2D.GetIgnoreCollision(s.SolidCollider, c)))
            && shards.All(s => world.EnvironmentHulls.Where(h => h.Collider != null && h.Collider.enabled)
                .All(h => !Physics2D.GetIgnoreCollision(s.SolidCollider, h.Collider))),
            "Shards retain physical contacts with table and world walls");
        Check(shards.All(s => shards.Where(t => t != s).All(t => Physics2D.GetIgnoreCollision(s.SolidCollider, t.SolidCollider))),
            "Neighbor shards ignore each other to avoid fragment-pile jitter");
        Check(shards.All(s => world.Items.Where(b => b.Body.simulated).SelectMany(b => b.solidColliders).Where(c => c != null && c.enabled)
            .All(c => Physics2D.GetIgnoreCollision(s.SolidCollider, c))), "Fragments ignore intact items and cannot trigger a chain reaction");
        // Clear the scratch upload so an old tail cannot masquerade as active boundaries.
        var boundaryUpload = (Array)Field(gpu, "boundaryUpload"); Array.Clear(boundaryUpload, 0, boundaryUpload.Length);
        Invoke(gpu, "UploadGeometry", 0f, 1f, .01f);
        Check(!boundaryUpload.Cast<object>().Any(b => Value<uint>(b, "VesselId") == glass.Id),
            "Broken vessel and its fragments contribute no liquid collision boundary");
        Capture("shards-at-break.png", sourcePosition, 2.25f);
        testProfile.separationSpeed = oldSeparation;
    }
    void UploadPendingGpuBirths()
    {
        int count = (int)Field(gpu, "pendingSpawnCount");
        ((GraphicsBuffer)Field(gpu, "spawnCommandBuffer")).SetData((Array)Field(gpu, "spawnCommands"), 0, 0, count);
        ((GraphicsBuffer)Field(gpu, "spawnStreamBuffer")).SetData((Array)Field(gpu, "spawnStreams"), 0, 0, count);
        ((ComputeShader)Field(gpu, "simulationShader")).SetInt("_SpawnCount", count);
        Invoke(gpu, "DispatchForCount", Field(gpu, "spawnKernel"), count); SetField(gpu, "pendingSpawnCount", 0);
    }
    void DetachedShakerIsolation()
    {
        foreach (bool combined in new[] { false, true })
        {
            Reset(); CompleteControlledBreak();
            var shaker = world.Items.First(b => b.kind == LabItemKind.Shaker);
            shaker.Body.simulated = true; shaker.Body.bodyType = RigidbodyType2D.Kinematic;
            shaker.Teleport(new Vector2(3, floorY + 2), 0); shaker.SetShakerClosure(FluidExperimentShakerClosure.Closed);
            var cap = shaker.ShakerCap; var strainer = shaker.ShakerStrainer;
            if (!combined)
            {
                Check(cap.TryPickUp(cap.Position), "Detached-cap fixture can open the shaker cap");
                cap.MoveToPointer(cap.Position + Vector2.right); cap.Release(Vector2.zero);
            }
            Check(strainer.TryPickUp(strainer.Position), "Detached-strainer fixture can open the shaker strainer");
            strainer.MoveToPointer(strainer.Position + Vector2.left); strainer.Release(Vector2.zero);
            Check(!strainer.IsAttached && (combined ? cap.IsAttached : !cap.IsAttached),
                combined ? "Combined lid fixture carries the attached cap" : "Separate cap and strainer fixture is fully detached");
            shaker.gameObject.SetActive(false);
            Check(!strainer.gameObject.activeInHierarchy && !cap.gameObject.activeInHierarchy,
                "Disabling shaker suspends detached parts and clears their native contact pairs");
            shaker.gameObject.SetActive(true);
            // These component FixedUpdates run before native physics in ordinary playback.
            // Explicitly invoke them because this entire harness runs synchronous script simulation.
            strainer.SendMessage("FixedUpdate"); if (!cap.IsAttached) cap.SendMessage("FixedUpdate");
            PhysicsStep();
            var colliders = new[] { cap, strainer }.SelectMany(p => p.GetComponents<Collider2D>())
                .Where(c => c.isActiveAndEnabled).ToArray();
            Check(colliders.Length == 2 && strainer.Body.simulated && strainer.isActiveAndEnabled,
                "Resumed fixture restores both physical lid collider shapes");
            Check(world.GlassShards.All(s => colliders.All(c => Physics2D.GetIgnoreCollision(s.SolidCollider, c))),
                combined ? "Re-enabled strainer and carried-cap collider ignore every shard after FixedUpdate"
                    : "Re-enabled separate cap and strainer ignore every shard after FixedUpdate");
        }
    }
    void LiquidAndSolidConservation()
    {
        Reset(0, 1.5f); glass.Body.bodyType = RigidbodyType2D.Kinematic;
        Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        Check(gpu.TryEmit(center, new Vector2(.3f, -.2f), ingredient, .5f, glass.Id)
            && gpu.TryEmit(center + new Vector2(2, 1), Vector2.right, ingredient, .5f, 0)
            && gpu.TryEmit(center + new Vector2(-2, 1), Vector2.left, ingredient, .5f, 98765), "Active owned/spilled/foreign liquid fixtures reserve successfully");
        Step(.001f);
        Check(gpu.TryEmit(center + Vector2.up * .1f, new Vector2(-.1f, .2f), ingredient, .5f, glass.Id)
            && gpu.TryEmit(center + new Vector2(2, .5f), Vector2.up, ingredient, .5f, 98765), "Pending GPU owned/foreign birth fixtures reserve successfully");
        UploadPendingGpuBirths();
        Check(gpu.TryEmit(center - Vector2.up * .1f, new Vector2(.1f, -.3f), ingredient, .5f, glass.Id)
            && gpu.TryEmit(center + new Vector2(-2, .5f), Vector2.down, ingredient, .5f, 98765), "Pending CPU owned/foreign birth fixtures reserve successfully");
        var before = Read(); var composition = (float[])gpu.CompositionSnapshot.Clone(); var streams = gpu.ReadStreamParticles();
        int revision = gpu.SnapshotRevision, pendingCount = (int)Field(gpu, "pendingSpawnCount");
        var commands = (Array)Field(gpu, "spawnCommands"); var commandsBefore = (Array)commands.Clone();
        Check(before.Any(p => p.Active != 0 && p.VesselId == glass.Id)
            && Enumerable.Range(0, streams.Length).Any(i => streams[i].Pending != 0 && before[i].VesselId == glass.Id),
            "Conservation fixture contains both active and reserved-but-unborn owned GPU particles");
        var icePrefab = world.Items.First(b => b.kind == LabItemKind.IceBucket).icePrefab;
        var garnishPrefab = world.GetComponentsInChildren<FluidExperimentGarnishSource>()[0].garnishPrefab;
        var ice = Instantiate(icePrefab, center, Quaternion.identity, world.transform);
        var garnish = Instantiate(garnishPrefab, center, Quaternion.identity, world.transform);
        ice.Teleport(center, 17); garnish.Teleport(center, -23); world.RefreshIceContainment();
        Check(ice.ContainingVesselId == glass.Id && garnish.ContainingVesselId == glass.Id,
            "Ice and garnish initially belong to the hurricane");
        ice.Body.linearVelocity = new Vector2(.2f, -.1f); ice.Body.angularVelocity = 30;
        garnish.Body.linearVelocity = new Vector2(-.1f, .2f); garnish.Body.angularVelocity = -40;
        var solids = new[] { ice, garnish }; var positions = solids.Select(b => b.Position).ToArray();
        var velocities = solids.Select(b => b.Body.linearVelocity).ToArray(); var spins = solids.Select(b => b.Body.angularVelocity).ToArray();
        // The callback eligibility already has dedicated native tests; here a valid dynamic source queues the transaction.
        glass.Body.bodyType = RigidbodyType2D.Dynamic;
        uint owner = glass.Id; CompleteControlledBreak();
        Check(!gpu.TryTransferContents(owner, revision, out _), "Pre-break snapshot cannot be reused to transfer removed contents");
        var after = Read(); bool exactOwned = true, exactOthers = true;
        for (int i = 0; i < before.Length; i++)
        {
            var a = before[i]; var b = after[i];
            if (a.VesselId == owner)
            { exactOwned &= b.VesselId == 0; b.VesselId = owner; exactOwned &= a.Equals(b); }
            else exactOthers &= a.Equals(b);
        }
        Check(exactOwned, "Breaking clears only owner IDs while preserving every owned GPU position/history/velocity/ml/state");
        Check(exactOthers && composition.SequenceEqual(gpu.CompositionSnapshot) && streams.SequenceEqual(gpu.ReadStreamParticles()),
            "Foreign/spilled liquid, all composition and stream birth timing/tokens are unchanged");
        bool cpuExact = (int)Field(gpu, "pendingSpawnCount") == pendingCount;
        for (int i = 0; i < pendingCount; i++)
        {
            object a = commandsBefore.GetValue(i), b = commands.GetValue(i);
            if (Value<uint>(a, "VesselId") == owner)
            { cpuExact &= Value<uint>(b, "VesselId") == 0; b.GetType().GetField("VesselId").SetValue(b, owner); }
            cpuExact &= a.Equals(b);
        }
        Check(cpuExact, "CPU queued births keep all fields and count while losing only the broken owner ID");
        for (int i = 0; i < solids.Length; i++)
            Check(solids[i].ContainingVesselId == 0 && solids[i].isActiveAndEnabled && Near(solids[i].Position, positions[i])
                && Near(solids[i].Body.linearVelocity, velocities[i]) && solids[i].Body.angularVelocity == spins[i],
                solids[i].kind + " loses containment without teleportation, disappearance or added velocity");
        Check(world.GlassShards.All(s => solids.All(b => b.solidColliders.All(c => Physics2D.GetIgnoreCollision(s.SolidCollider, c)))),
            "New fragments cannot kick the released ice or garnish");
        gpu.Step(.001f); Read();
        Check(gpu.Snapshot.Where(p => p.Active != 0).All(p => p.VesselId != owner)
            && Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .0001,
            "Queued births activate without returning to the broken vessel or losing volume");
        ice.gameObject.SetActive(false); garnish.gameObject.SetActive(false); Destroy(ice.gameObject); Destroy(garnish.gameObject);
    }
    void NativeShardRestAndCleanup()
    {
        Reset(0, 1.5f); CompleteControlledBreak();
        for (int i = 0; i < 500; i++)
        { PhysicsStep(); Invoke(world, "ProcessGlassBreakage"); }
        var shards = world.GlassShards.ToArray();
        Check(shards.Length == 16, "All fragments remain on the table rather than being time-despawned");
        float minimum = shards.Min(s => s.SolidCollider.bounds.min.y);
        float maximumSpeed = shards.Max(s => s.Body.linearVelocity.magnitude);
        report.Add($"INFO shard rest floorY={floorY:R} minY={minimum:R} maxSpeed={maximumSpeed:R}");
        Check(shards.All(s => s.SolidCollider.bounds.min.y >= floorY - .025f
            && s.SolidCollider.bounds.min.y < floorY + .08f && s.Body.linearVelocity.magnitude < .15f),
            "Every freely simulated fragment settles against the physical floor without falling through");
        Capture("shards-resting.png", new Vector2(0, floorY + .65f), 1.5f);
        var escaped = shards[0]; escaped.Body.position = new Vector2(0, gpu.settings.gpuLiquidWorldMin.y - 100);
        Invoke(world, "ProcessGlassBreakage");
        Check(world.GlassShards.Count == 15 && (escaped == null || !escaped.gameObject.activeInHierarchy),
            "A fragment far outside the world is immediately removed from active simulation");
        Check(shards.Skip(1).All(s => s != null && s.gameObject.activeInHierarchy),
            "Offscreen cleanup leaves resting visible fragments intact");
    }
    void ResetAndModeScope()
    {
        for (int cycle = 0; cycle < 3; cycle++)
        {
            Reset(); CompleteControlledBreak();
            Check(world.GlassShards.Count == 16 && breakable.IsBroken, $"Reset/break cycle {cycle + 1} creates one complete fragment set");
        }
        comparison.SwitchMode(FluidExperimentMode.DImprovedSurface); DisableAutomaticSimulation();
        Check(!breakable.IsBroken && glass.gameObject.activeInHierarchy && world.GlassShards.Count == 0,
            "Switching away from F restores the original glass and clears fragments");
        Reset(0, 3);
        Check(!UntilQueued(180) && !breakable.IsBroken && world.GlassShards.Count == 0,
            "D keeps the legacy unbreakable behavior even under the same strong drop");
        comparison.SwitchMode(FluidExperimentMode.FCoherentLiquid); DisableAutomaticSimulation();
        Reset(0, 3);
        Check(UntilQueued(160), "Switching back to F restores automatic breakage");
        world.TickLiquid(.01f); Reset();
    }
    void Capture(string file, Vector2 center, float halfHeight)
    {
        var camera = hand.inputCamera;
        var oldTarget = camera.targetTexture; var oldPosition = camera.transform.position;
        float oldSize = camera.orthographicSize; var oldActive = RenderTexture.active;
        var target = new RenderTexture(960, 720, 24); var pixels = new Texture2D(960, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.transform.position = new Vector3(center.x, center.y, oldPosition.z);
            camera.orthographicSize = halfHeight; Physics2D.SyncTransforms(); camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); pixels.Apply(); File.WriteAllBytes(Path.Combine(Evidence, file), pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; camera.transform.position = oldPosition; camera.orthographicSize = oldSize;
            RenderTexture.active = oldActive; target.Release(); Destroy(target); Destroy(pixels);
        }
    }
}
