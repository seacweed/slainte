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

public static class FloatingContentsValidation
{
    const string Key = "FluidExperiment.FloatingContentsValidation";
    [InitializeOnLoadMethod] static void Register()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
            SessionState.EraseBool(Key);
            var runner = new GameObject("FloatingContentsValidation").AddComponent<FloatingContentsValidationRunner>();
            runner.RestOnly = SessionState.GetBool(Key + ".Rest", false);
            runner.ModesOnly = SessionState.GetBool(Key + ".Modes", false);
            SessionState.EraseBool(Key + ".Rest");
            SessionState.EraseBool(Key + ".Modes");
        };
    }
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    public static void BeginRest()
    {
        SessionState.SetBool(Key + ".Rest", true);
        Begin();
    }
    public static void BeginModes()
    {
        SessionState.SetBool(Key + ".Modes", true);
        Begin();
    }
}

public sealed class FloatingContentsValidationRunner : MonoBehaviour
{
    public bool RestOnly, ModesOnly;
    readonly List<string> report = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<string> samples = new List<string> { "case,tick,glass_x,glass_y,glass_angle,x,y,angle,vx,vy,spin,owner" };
    readonly List<FluidExperimentBody> pieces = new List<FluidExperimentBody>();
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentInteractor hand;
    FluidExperimentBody glass, icePrefab;
    FluidExperimentGarnishSource source;
    ItemDef ingredient;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    GraphicsBuffer Particles => (GraphicsBuffer)gpu.GetType().GetField("particleBuffer", Private).GetValue(gpu);
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool pass, string message)
    {
        report.Add((pass ? "PASS " : "FAIL ") + message);
        if (!pass) throw new Exception(message);
    }
    void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) errors.Add(message);
    }
    IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        yield return null;
        var comparison = FindFirstObjectByType<FluidExperimentComparison>();
        while (!comparison.Ready) yield return null;
        var oldSimulation = Physics2D.simulationMode;
        var oldGravity = Physics2D.gravity;
        bool success = false;
        try
        {
            world = comparison.World; gpu = comparison.Gpu; hand = world.interactor;
            comparison.automaticScenario = false; comparison.showControls = false;
            world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = new Vector2(0, -9.81f);
            glass = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
            icePrefab = world.Items.First(b => b.kind == LabItemKind.IceBucket).icePrefab;
            source = world.GetComponentsInChildren<FluidExperimentGarnishSource>().First();
            ingredient = world.Items.First(b => b.ingredient != null).ingredient;
            if (ModesOnly)
            {
                Check(gpu.iceLiquidMode==FluidExperimentIceLiquidMode.NonColliding,"Default ice mode is non-colliding");
                foreach(var mode in new[]{FluidExperimentMode.FCoherentLiquid,FluidExperimentMode.GExplosiveBreakage})
                {
                    comparison.SwitchMode(mode); world.enabled=false; hand.enabled=false; gpu.automaticReadback=false;
                    foreach(var iceMode in new[]{FluidExperimentIceLiquidMode.NonColliding,FluidExperimentIceLiquidMode.Colliding})
                    {
                        gpu.iceLiquidMode=iceMode;
                        report.Add("INFO MODES "+mode+" / "+iceMode);
                        IceModeProbe();
                        if(iceMode==FluidExperimentIceLiquidMode.Colliding) IceEnergyControls();
                        LiquidRelease(true,true); SolidCarry(false); FloatAndFlow(false); FloatAndFlow(true);
                        comparison.ResetComparison(); world.enabled=false; hand.enabled=false;
                        Check(gpu.iceLiquidMode==iceMode,"R/restart preserves selected ice collision mode");
                    }
                    Check(gpu.iceLiquidMode==FluidExperimentIceLiquidMode.Colliding,"F/G selection preserves ice mode");
                }
                comparison.SwitchMode(FluidExperimentMode.DImprovedSurface);
                Check(!gpu.IceLiquidCollisionEnabled,"Ice variant does not activate F/G contact policy in D");
                Check(errors.Count==0,"Mode variants have no Unity application errors");
                success=true; yield break;
            }
            if (RestOnly)
            {
                foreach(var mode in new[]{FluidExperimentMode.FCoherentLiquid,FluidExperimentMode.GExplosiveBreakage})
                {
                    comparison.SwitchMode(mode);
                    world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
                    gpu.iceLiquidMode=FluidExperimentIceLiquidMode.NonColliding;
                    report.Add("INFO REST "+mode);
                    StationaryContents("baseline", 0, false);
                    StationaryContents("inside-drop", 1, false);
                    StationaryContents("drop", 1, true);
                    StationaryContents("three-drops", 3, true);
                    StationaryContents("mixed-drops",3,true,true);
                }
                Check(errors.Count == 0, "Stationary diagnostic has no Unity errors");
                success = true;
                yield break;
            }
            foreach (var mode in new[] { FluidExperimentMode.FCoherentLiquid, FluidExperimentMode.GExplosiveBreakage })
            {
                comparison.SwitchMode(mode); world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
                report.Add("INFO MODE " + mode);
                Check(gpu.CohesivePhysicsActive, "F/G solver is active");
                LiquidRelease(true);
                LiquidRelease(true, true);
                LiquidRelease(false);
                CorrectedRelease();
                QueuedRelease();
                SolidCarry(false);
                SolidCarry(true);
                StaleFlowReprojection();
                FloatAndFlow(false);
                FloatAndFlow(true);
                MovingWetContents(false);
                MovingWetContents(true);
                if (mode == FluidExperimentMode.FCoherentLiquid)
                {
                    SettledFloat(false);
                    SettledFloat(true);
                }
            }
            comparison.SwitchMode(FluidExperimentMode.DImprovedSurface);
            world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
            ResetFixture(); var legacyIce = Spawn(false); hand.Pick(glass,glass.Position);
            Vector2 oldPosition = legacyIce.Position;
            glass.SetHeldPose(glass.Position+Vector2.right*.04f,0);
            typeof(FluidExperimentBody).GetMethod("ApplyHeldPose",Private).Invoke(glass,null);
            Check(!legacyIce.UsesLiquidSolidMotion && Vector2.Distance(legacyIce.Position-oldPosition,Vector2.right*.04f)<.00001f,
                "D retains its previous carried-ice policy");
            Check(errors.Count == 0, "No Unity application errors or assertions");
            success = true;
        }
        catch (Exception exception) { report.Add(exception.ToString()); }
        finally
        {
            Physics2D.simulationMode = oldSimulation; Physics2D.gravity = oldGravity;
            Application.logMessageReceived -= OnLog;
            report.AddRange(errors);
            File.WriteAllText(Path.Combine(Evidence, "held-contents-diagnosis.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(Evidence, "held-contents-result.txt"), success ? "PASS" : "FAIL");
            File.WriteAllLines(Path.Combine(Evidence, "floating-contents-timeline.csv"), samples);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
    void ResetFixture()
    {
        hand.ReleaseWithVelocity(Vector2.zero);
        foreach (var piece in pieces) if (piece != null) { piece.gameObject.SetActive(false); Destroy(piece.gameObject); }
        pieces.Clear();
        foreach (var body in world.Items.ToArray())
        {
            body.SetHeld(false); body.Teleport(new Vector2(-60 - body.Id * 3, 20), 0);
            body.Body.simulated = false; body.pourMlPerSecond = 0;
        }
        gpu.ResetSimulation();
        glass.Body.simulated = true; glass.Body.bodyType = RigidbodyType2D.Kinematic;
        glass.Teleport(new Vector2(0, 1.5f), 0); glass.SetSealed(false);
        foreach (var body in world.Items)
            body.transform.SetPositionAndRotation(body.Position, Quaternion.Euler(0,0,body.Angle));
        Physics2D.SyncTransforms();
    }
    void StationaryContents(string name, int count, bool drop, bool mixed=false)
    {
        ResetFixture(); gpu.Fill(glass, ingredient, 60);
        for(int i=0;i<150;i++) Step(i%10==0);
        var water=Read().Where(p=>p.Active!=0 && p.VesselId==glass.Id).ToArray();
        float surface=water.Select(p=>p.Position.y).OrderBy(y=>y).ElementAt((int)(water.Length*.95f))+gpu.Radius;
        float firstY=0, maximumY=float.NegativeInfinity, maximumSpeed=0, peakWaterSpeed=0;
        float lateWaterEnergy=0, peakSurface=surface, lateSolidSpan=0;
        var lateHeights=new List<float>();
        FluidExperimentBody piece=null;
        for(int i=0;i<600;i++)
        {
            if (count>0 && (i==0 || (drop && i%150==0 && i/150<count)))
            {
                piece=Spawn(mixed && i/150==2);
                if(drop) piece.Teleport(new Vector2((i/150%2==0?-.1f:.1f), surface+piece.SolidBounds.extents.y+.06f),0);
                typeof(FluidExperimentBody).GetMethod("MarkPlaced", Private).Invoke(piece,null);
                world.RefreshIceContainment();
                if(i==0) firstY=piece.Position.y;
            }
            Step(i%10==0);
            if(piece!=null)
            {
                Record("rest-"+name,i,piece);
                maximumY=Mathf.Max(maximumY,piece.Position.y);
                maximumSpeed=Mathf.Max(maximumSpeed,piece.Body.linearVelocity.magnitude);
                if(i>=500) lateHeights.Add(piece.Position.y);
            }
            if(i%10!=0) continue;
            water=gpu.Snapshot.Where(p=>p.Active!=0).ToArray();
            float meanEnergy=water.Length==0?0:water.Average(p=>p.Velocity.sqrMagnitude);
            peakWaterSpeed=Mathf.Max(peakWaterSpeed,water.Length==0?0:water.Max(p=>p.Velocity.magnitude));
            peakSurface=Mathf.Max(peakSurface,water.Length==0?surface:water.Max(p=>p.Position.y+gpu.Radius));
            if(i>=500) lateWaterEnergy+=meanEnergy/10;
            report.Add($"REST_SAMPLE {name} tick={i} solidY={(piece==null?0:piece.Position.y):R} vy={(piece==null?0:piece.Body.linearVelocity.y):R} waterEnergy={meanEnergy:R} waterTop={(water.Length==0?0:water.Max(p=>p.Position.y)):R} ownerMl={gpu.VolumeIn(glass.Id):R}");
        }
        if(lateHeights.Count>0) lateSolidSpan=lateHeights.Max()-lateHeights.Min();
        report.Add($"MEASURE rest name={name} initialSurface={surface:R} firstY={firstY:R} maximumY={maximumY:R} maxSolidSpeed={maximumSpeed:R} maxWaterSpeed={peakWaterSpeed:R} peakSurfaceRise={peakSurface-surface:R} lateWaterEnergy={lateWaterEnergy:R} lateSolidSpan={lateSolidSpan:R} finalOwnerMl={gpu.VolumeIn(glass.Id):R}");
        Check(peakWaterSpeed<.5f && peakSurface-surface<.08f && lateWaterEnergy<.02f,
            "Non-colliding stationary contents cannot inject a liquid splash");
        Check(lateSolidSpan<.04f && Mathf.Abs(gpu.VolumeIn(glass.Id)-60)<.001f,
            "Stationary contents settle and retain all 60 ml");
        Capture("rest-"+name);
    }
    void IceModeProbe()
    {
        ResetFixture();
        gpu.Step(.001f); // Initialize lazy surface buffers before querying their upload.
        var mask=gpu.GetType().GetMethod("UploadImprovedSurfaceHulls",Private);
        mask.Invoke(gpu,null); int emptyMasks=gpu.ImprovedSurfaceMaskHullCount;
        var ice=Spawn(false); ice.Body.bodyType=RigidbodyType2D.Kinematic;
        var garnish=Spawn(true); garnish.Body.bodyType=RigidbodyType2D.Kinematic;
        mask.Invoke(gpu,null);
        bool collision=gpu.iceLiquidMode==FluidExperimentIceLiquidMode.Colliding;
        Check(gpu.ImprovedSurfaceMaskHullCount==emptyMasks+(collision?ice.collisionProfile.solids.Length:0),
            "Surface mask follows ice variant; garnish never adds a liquid mask");
        int slot=SeedOwned(Vector2.left*2,false);
        var before=Read();
        Vector2 start=new Vector2(ice.SolidBounds.max.x+gpu.Radius+.04f,ice.Position.y);
        before[slot].Position=start; before[slot].PreviousPosition=start+Vector2.right*.04f;
        before[slot].Velocity=Vector2.left*2; Particles.SetData(before);
        for(int i=0;i<10;i++) gpu.Step(.02f);
        var after=Read(); float end=after[slot].Position.x;
        report.Add($"MEASURE ice-contact variant={gpu.iceLiquidMode} startX={start.x:R} endX={end:R} face={ice.SolidBounds.max.x:R} speed={after[slot].Velocity}");
        Check(collision?end>ice.SolidBounds.max.x:end<ice.Position.x+.05f,
            "Actual GPU particle "+(collision?"collides with ice face":"passes through ice without receiving a solid impulse"));
        var exact=(GpuLiquidParticle[])after.Clone(); Vector2 solidSpeed=ice.Body.linearVelocity;
        gpu.iceLiquidMode=collision?FluidExperimentIceLiquidMode.NonColliding:FluidExperimentIceLiquidMode.Colliding;
        var toggled=Read();
        Check(exact.SequenceEqual(toggled)&&ice.Body.linearVelocity==solidSpeed,
            "Live variant change preserves particle state and native solid velocity without a reset");
        gpu.iceLiquidMode=collision?FluidExperimentIceLiquidMode.Colliding:FluidExperimentIceLiquidMode.NonColliding;
    }
    void IceEnergyControls()
    {
        var oldGravity=Physics2D.gravity; Physics2D.gravity=Vector2.zero;
        try
        {
            ResetFixture(); Vector2 center=glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
            var left=Spawn(false); var right=Spawn(false);
            left.Teleport(center+Vector2.left*.14f,0); right.Teleport(center+Vector2.right*.14f,0);
            left.Body.bodyType=right.Body.bodyType=RigidbodyType2D.Kinematic;
            world.RefreshIceContainment(); Physics2D.SyncTransforms();
            for(int i=-4;i<=4;i++) gpu.TryEmit(center+Vector2.up*(i*.025f),Vector2.zero,ingredient,.5f,glass.Id);
            float peak=0;
            for(int i=0;i<40;i++)
            {
                gpu.Step(.02f); var water=Read().Where(p=>p.Active!=0).ToArray();
                peak=Mathf.Max(peak,water.Select(p=>p.Velocity.magnitude).DefaultIfEmpty(0).Max());
            }
            report.Add($"MEASURE stationary-wedge speed={peak:R} ml={gpu.VolumeIn(glass.Id):R}");
            Check(peak<.01f&&Mathf.Abs(gpu.VolumeIn(glass.Id)-4.5f)<.001f,
                "Compressed stationary ice gap cannot create a jet, shrink liquid, or discard its ml");
            ResetFixture(); var ice=Spawn(false); ice.Teleport(new Vector2(0,.1f),0);
            world.RefreshIceContainment();
            int slot=SeedOwned(Vector2.right*7,false);
            gpu.Step(.02f); var fast=Read()[slot];
            Check(fast.Velocity.x>6.8f&&gpu.MaximumIceContactSpeed<.001f,
                "A real fast incoming flow retains momentum in a vessel containing stationary ice");
            ResetFixture(); slot=SeedOwned(Vector2.zero,false);
            var free=Read();
            free[slot].Position=glass.LocalToWorld(new Vector2(glass.collisionProfile.InteriorBounds.xMin+gpu.Radius+.025f,
                glass.collisionProfile.InteriorBounds.center.y));
            free[slot].PreviousPosition=free[slot].Position; free[slot].Velocity=Vector2.zero;
            free[slot].VesselId=0; free[slot].Padding=0xbf800000u;
            // A previously ice-touched free drop must still receive a genuine
            // moving receiver wall impulse, rather than retaining a zero cap.
            Particles.SetData(free); Check(hand.Pick(glass,glass.Position),"Moving receiver probe picks the glass");
            glass.SetHeldPose(glass.Position+Vector2.right*.04f,0);
            Step(true); var hit=Read()[slot];
            report.Add($"MEASURE ice-history moving-wall vx={hit.Velocity.x:R} owner={hit.VesselId}");
            Check(hit.Velocity.x>1.5f,"Ice contact history preserves a subsequent real moving-wall impulse");
        }
        finally { Physics2D.gravity=oldGravity; }
    }
    GpuLiquidParticle[] Read()
    {
        gpu.ReadbackNow(); return (GpuLiquidParticle[])gpu.Snapshot.Clone();
    }
    int SeedOwned(Vector2 velocity, bool freeParticle)
    {
        Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        Check(gpu.TryEmit(center, velocity, ingredient, .5f, glass.Id), "Seed owned liquid");
        if (freeParticle) Check(gpu.TryEmit(new Vector2(5, 4), new Vector2(-.7f, .25f), ingredient, .5f, 0), "Seed unrelated free liquid");
        world.TickLiquid(.001f);
        var particles = Read();
        int slot = Array.FindIndex(particles, p => p.Active != 0 && p.VesselId == glass.Id);
        Check(slot >= 0, "Seed materialized on GPU");
        particles[slot].Position = center;
        particles[slot].Velocity = velocity;
        particles[slot].PreviousPosition = center - velocity * .02f;
        Particles.SetData(particles);
        return slot;
    }
    void LiquidRelease(bool click, bool moving = false)
    {
        ResetFixture(); Check(hand.Pick(glass, glass.Position), "Pick glass for release probe");
        int slot = SeedOwned(new Vector2(1.2f, -.4f), true);
        if (moving)
        {
            ((IList)typeof(FluidExperimentInteractor).GetField("samples", Private).GetValue(hand)).Clear();
            var sample = typeof(FluidExperimentInteractor).GetMethod("Sample", Private);
            sample.Invoke(hand, new object[] { 10f });
            glass.SetHeldPose(glass.Position + new Vector2(.04f, 0), 0);
            typeof(FluidExperimentBody).GetMethod("ApplyHeldPose", Private).Invoke(glass, null);
            sample.Invoke(hand, new object[] { 10.02f });
        }
        var before = Read(); Vector2 start = glass.Position;
        int freeSlot = Array.FindIndex(before, p => p.Active != 0 && p.VesselId == 0);
        if (click) Check(hand.Drop(glass.Position), "Actual click-drop succeeds without obstacle correction");
        else hand.ReleaseWithVelocity(new Vector2(2, .3f), 30);
        var after = Read();
        report.Add($"MEASURE release click={click} moving={moving} glassDelta={glass.Position-start} glassVelocity={glass.Body.linearVelocity} liquidBefore={before[slot].Velocity} liquidAfter={after[slot].Velocity} historyBefore={before[slot].Position-before[slot].PreviousPosition} historyAfter={after[slot].Position-after[slot].PreviousPosition}");
        Check(Vector2.Distance(glass.Position, start) < .00001f, "Release probe has zero position correction");
        Check(after[slot].VolumeMl == before[slot].VolumeMl && after[slot].VesselId == before[slot].VesselId, "Release preserves amount and owner");
        Check(after[freeSlot].Velocity == before[freeSlot].Velocity, "Release leaves unrelated free liquid velocity unchanged");
        if (click)
        {
            Check(after[slot].Velocity == before[slot].Velocity, "Click-drop preserves owned liquid world velocity");
            Check(Vector2.Distance(after[slot].Position - after[slot].PreviousPosition,
                before[slot].Position - before[slot].PreviousPosition) < .00001f, "Click-drop preserves liquid motion history");
        }
        else Check(after[slot].Velocity == before[slot].Velocity, "Programmatic release bypasses the click-drop velocity reset");
    }
    FluidExperimentBody Spawn(bool garnish)
    {
        Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        var piece = Instantiate(garnish ? source.garnishPrefab : icePrefab, center, Quaternion.identity, world.transform);
        piece.Teleport(center, 0); pieces.Add(piece); world.RefreshIceContainment();
        return piece;
    }
    void SolidCarry(bool garnish)
    {
        ResetFixture(); var piece = Spawn(garnish);
        Check(piece.ContainingVesselId == glass.Id, "Solid belongs to probe glass");
        Check(hand.Pick(glass, glass.Position), "Pick glass for carry probe");
        Vector2 before = piece.Position, glassBefore = glass.Position;
        Vector2 delta = new Vector2(.06f, .04f);
        glass.SetHeldPose(glassBefore + delta, 0);
        typeof(FluidExperimentBody).GetMethod("ApplyHeldPose", Private).Invoke(glass, null);
        report.Add($"MEASURE carry garnish={garnish} glassDelta={glass.Position-glassBefore} solidDelta={piece.Position-before} glassNativeVelocity={glass.Body.linearVelocity} impliedPoseVelocity={delta/.02f} solidVelocity={piece.Body.linearVelocity}");
        Check(Vector2.Distance(piece.Position, before) < .00001f, "Neither ice nor garnish is transported by held vessel translation");
        Vector2 contactVelocity = (Vector2)typeof(FluidExperimentBody).GetMethod("ContactPointVelocity", Private)
            .Invoke(glass, new object[] { glass.Position, .02f });
        Check(Vector2.Distance(contactVelocity, delta / .02f) < .0001f, "Contact recovery uses actual cursor-driven wall speed");
        float angle = piece.Angle;
        glass.SetHeldPose(glass.Position, 15);
        typeof(FluidExperimentBody).GetMethod("ApplyHeldPose", Private).Invoke(glass, null);
        Check(Vector2.Distance(piece.Position, before) < .00001f && piece.Angle == angle,
            "Neither ice nor garnish is rigidly rotated with its held vessel");
        piece.Body.linearVelocity = new Vector2(.3f, -.2f); piece.Body.angularVelocity = 17;
        hand.ReleaseWithVelocity(new Vector2(2, 1), 80);
        Check(piece.Body.linearVelocity == new Vector2(.3f, -.2f) && piece.Body.angularVelocity == 17,
            "Releasing vessel does not inject a second velocity/spin into contents");
    }
    void StaleFlowReprojection()
    {
        ResetFixture(); var garnish = Spawn(true);
        SeedOwned(new Vector2(.5f, 0), false);
        gpu.ReadbackNow();
        var snapshot = gpu.Snapshot;
        Array.Clear(snapshot, 0, snapshot.Length);
        Vector2 center = garnish.Position;
        int slot = 0;
        for (int x=-2; x<=2; x++) for (int y=-1; y<=1; y++)
            snapshot[slot++] = new GpuLiquidParticle { Active=1, VesselId=glass.Id, VolumeMl=.5f,
                Position=center + new Vector2(x,y)*.04f, Velocity=new Vector2(.5f,0) };
        Check(gpu.TrySampleGarnishFlow(garnish, out _, out float beforeWet), "Synthetic completed snapshot initially supports garnish");
        // Move poses only. Keep the recorded world-space liquid snapshot unchanged.
        Vector2 delta = new Vector2(1, 0);
        glass.Body.position += delta; garnish.Body.position += delta;
        Physics2D.SyncTransforms();
        bool sampled = gpu.TrySampleGarnishFlow(garnish, out Vector2 flow, out float afterWet);
        report.Add($"MEASURE reproject unchangedSnapshot=true glassDelta={delta} sampled={sampled} submergedBefore={beforeWet:R} submergedAfter={afterWet:R} sampledFlow={flow}");
        Check(!sampled && afterWet == 0, "Moving glass cannot carry an old world-space liquid sample with it");
        report.Add("INFO Reprojection probe is synthetic and demonstrates sampler policy, not measured gameplay quality.");
    }

    void Step(bool read = false)
    {
        world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms();
        Physics2D.Simulate(.02f); world.TickLiquid(.02f);
        if (read) gpu.ReadbackNow();
    }
    void CorrectedRelease()
    {
        ResetFixture(); Check(hand.Pick(glass, glass.Position), "Pick glass for corrected release");
        int slot = SeedOwned(new Vector2(.7f, -.3f), true);
        // Put the vessel into the authored floor after materializing its contents.
        var floor = world.floorColliders.First(c => c != null);
        float shift = floor.bounds.max.y - glass.SolidBounds.min.y - .08f;
        glass.SetHeldPose(glass.Position + Vector2.up * shift, 0);
        typeof(FluidExperimentBody).GetMethod("ApplyHeldPose", Private).Invoke(glass, null);
        var before = Read(); Vector2 origin = glass.Position;
        Check(hand.Drop(glass.Position), "Click-drop resolves a real floor overlap");
        var after = Read(); Vector2 delta = glass.Position - origin;
        Check(delta.y > .05f, "Correction fixture actually moves the vessel out of the floor");
        Check(after[slot].Velocity == before[slot].Velocity
            && Vector2.Distance(after[slot].Position, before[slot].Position + delta) < .00001f
            && Vector2.Distance(after[slot].PreviousPosition, before[slot].PreviousPosition + delta) < .00001f,
            "Overlap correction translates both liquid histories and retains velocity");
    }
    void FloatAndFlow(bool garnish)
    {
        ResetFixture(); var piece = Spawn(garnish);
        Check(hand.Pick(glass, glass.Position), "Float probe holds actual glass");
        SeedOwned(Vector2.zero, false); gpu.ReadbackNow();
        // A completed controlled snapshot isolates force response from changing GPU
        // pressure. Real moving liquid is exercised separately below at 5 Hz readback.
        void Patch(Vector2 velocity)
        {
            Array.Clear(gpu.Snapshot, 0, gpu.Snapshot.Length); int n = 0;
            Vector2 center = piece.Position;
            // Include undisturbed liquid outside the solid contact layer, rather
            // than only a field occupying the inside of a colliding ice cube.
            for (int x=-9; x<=9; x++) for (int y=-9; y<=9; y++)
                gpu.Snapshot[n++] = new GpuLiquidParticle { Active=1, VesselId=glass.Id, VolumeMl=.5f,
                    Position=center+new Vector2(x,y)*.035f, Velocity=velocity };
        }
        Vector2 start = piece.Position; Patch(Vector2.zero);
        world.SendMessage("FixedUpdate"); Physics2D.Simulate(.02f);
        report.Add($"MEASURE buoyancy garnish={garnish} dy={piece.Position.y-start.y:R} vy={piece.Body.linearVelocity.y:R}");
        Check(piece.Body.linearVelocity.y > .005f, "Submerged ice/garnish accelerates upward from buoyancy");
        piece.Body.linearVelocity = Vector2.zero; Patch(new Vector2(1, 0));
        world.SendMessage("FixedUpdate"); Physics2D.Simulate(.02f);
        Check(piece.Body.linearVelocity.x > .01f && piece.Body.linearVelocity.x < .2f,
            "Flow drag accelerates contents gradually without synchronizing their velocity");
        float forward = piece.Body.linearVelocity.x;
        Patch(new Vector2(-1, 0)); world.SendMessage("FixedUpdate"); Physics2D.Simulate(.02f);
        Check(piece.Body.linearVelocity.x < forward, "Reversed local flow reverses drag acceleration");
        Patch(Vector2.zero); piece.Body.linearVelocity = Vector2.right;
        world.SendMessage("FixedUpdate"); Physics2D.Simulate(.02f);
        Check(piece.Body.linearVelocity.x > .7f && piece.Body.linearVelocity.x < .99f,
            "Stopping flow damps relative motion while retaining solid inertia");
    }
    void MovingWetContents(bool garnish)
    {
        ResetFixture(); gpu.Fill(glass, ingredient, 60);
        for (int i=0; i<150; i++) Step(i%10==0);
        var piece = Spawn(garnish); Vector2 origin = glass.Position;
        hand.Pick(glass, glass.Position);
        var local = new List<Vector2>(); var turns = new List<float>();
        float minimumGap = float.PositiveInfinity;
        for (int i=0; i<200; i++)
        {
            float phase = i * Mathf.PI * 2 / 100;
            float x = i < 150 ? .2f * Mathf.Sin(phase) : 0;
            float turn = i < 150 ? 15 * Mathf.Sin(phase) : 0;
            glass.SetHeldPose(origin + Vector2.right*x, turn);
            Step(i%10==0);
            Record("moving-"+garnish,i,piece);
            local.Add(glass.WorldToLocal(piece.Position)); turns.Add(Mathf.DeltaAngle(glass.Angle, piece.Angle));
            foreach (var a in piece.solidColliders) foreach (var b in glass.solidColliders)
                minimumGap = Mathf.Min(minimumGap, a.Distance(b).distance);
        }
        float span = Vector2.Distance(new Vector2(local.Min(p=>p.x),local.Min(p=>p.y)),
            new Vector2(local.Max(p=>p.x),local.Max(p=>p.y)));
        report.Add($"MEASURE real-wet garnish={garnish} relativeSpan={span:R} turnSpan={turns.Max()-turns.Min():R} gap={minimumGap:R} owner={piece.ContainingVesselId} ml={gpu.VolumeIn(glass.Id):R}");
        Check(span > .04f && turns.Max()-turns.Min() > 10, "Moving/rotating wet glass permits independent contents movement");
        Check(float.IsFinite(piece.Position.x) && float.IsFinite(piece.Body.linearVelocity.magnitude), "Repeated direction changes and stopping remain finite");
        Check(minimumGap >= -piece.collisionProfile.sourcePixelSize-.001f, "Native and swept contacts keep deep wall penetration below one pixel");
        Vector2 before = piece.Body.linearVelocity; float spin = piece.Body.angularVelocity;
        Check(hand.Drop(glass.Position), "Actual click release after wet movement succeeds");
        Check(piece.Body.linearVelocity == before && piece.Body.angularVelocity == spin,
            "Actual wet click-drop preserves solid velocity and angular momentum");
        gpu.ReadbackNow(); Check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl)<.001, "Moving wet fixture conserves all generated liquid");
    }

    object Field(string name) => gpu.GetType().GetField(name, Private).GetValue(gpu);
    void QueuedRelease()
    {
        ResetFixture(); hand.Pick(glass, glass.Position);
        int active = SeedOwned(new Vector2(.6f, -.2f), true);
        Vector2 center = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        gpu.TryEmit(center, new Vector2(.4f, .7f), ingredient, .25f, glass.Id);
        ((GraphicsBuffer)Field("spawnCommandBuffer")).SetData((Array)Field("spawnCommands"), 0, 0, 1);
        ((GraphicsBuffer)Field("spawnStreamBuffer")).SetData((Array)Field("spawnStreams"), 0, 0, 1);
        ((ComputeShader)Field("simulationShader")).SetInt("_SpawnCount", 1);
        gpu.GetType().GetMethod("DispatchForCount", Private).Invoke(gpu, new object[] { (int)Field("spawnKernel"), 1 });
        gpu.GetType().GetField("pendingSpawnCount", Private).SetValue(gpu, 0);
        gpu.TryEmit(center, new Vector2(-.3f, .8f), ingredient, .125f, glass.Id);
        var before = Read(); var streams = gpu.ReadStreamParticles();
        var composition = (float[])gpu.CompositionSnapshot.Clone();
        int pending = Array.FindIndex(streams, s => s.Pending != 0);
        Check(pending >= 0 && before[pending].Active == 0, "Reserved GPU birth remains inactive before corrected release");
        object cpu = ((Array)Field("spawnCommands")).GetValue(0);
        Vector2 cpuPosition = (Vector2)cpu.GetType().GetField("Position").GetValue(cpu);
        Vector2 cpuVelocity = (Vector2)cpu.GetType().GetField("Velocity").GetValue(cpu);
        var floor = world.floorColliders.First(c => c != null);
        glass.SetHeldPose(glass.Position + Vector2.up*(floor.bounds.max.y-glass.SolidBounds.min.y-.08f),0);
        typeof(FluidExperimentBody).GetMethod("ApplyHeldPose", Private).Invoke(glass,null);
        Vector2 origin = glass.Position;
        Check(hand.Drop(glass.Position), "Corrected click-drop with active and both pending queues succeeds");
        Vector2 delta = glass.Position-origin; var after = Read();
        foreach (int slot in new[] {active,pending})
            Check(after[slot].Velocity==before[slot].Velocity && after[slot].VolumeMl==before[slot].VolumeMl
                && after[slot].StateFlags==before[slot].StateFlags && after[slot].TemperatureC==before[slot].TemperatureC
                && Vector2.Distance(after[slot].Position,before[slot].Position+delta)<.00001f
                && Vector2.Distance(after[slot].PreviousPosition,before[slot].PreviousPosition+delta)<.00001f,
                "Active/reserved birth retains velocity, history, volume, state and temperature");
        object cpuAfter = ((Array)Field("spawnCommands")).GetValue(0);
        Check((int)Field("pendingSpawnCount")==1
            && (Vector2)cpuAfter.GetType().GetField("Velocity").GetValue(cpuAfter)==cpuVelocity
            && Vector2.Distance((Vector2)cpuAfter.GetType().GetField("Position").GetValue(cpuAfter),cpuPosition+delta)<.00001f,
            "CPU pending birth translates once without replacing velocity or consuming its reservation");
        Check(streams.SequenceEqual(gpu.ReadStreamParticles()), "Reserved birth keeps stream identity and scheduled timing");
        Check(composition.SequenceEqual(gpu.CompositionSnapshot), "Release correction preserves every particle ingredient fraction");
    }
    void SettledFloat(bool garnish)
    {
        float Run(bool wet, out float span)
        {
            ResetFixture();
            if(wet) gpu.Fill(glass,ingredient,60);
            for(int i=0;i<150;i++) Step(i%10==0);
            var piece=Spawn(garnish); hand.Pick(glass,glass.Position);
            var heights=new List<float>();
            for(int i=0;i<400;i++)
            {
                Step(i%10==0);
                Record("settle-"+garnish+"-"+wet,i,piece);
                if(i>=350) heights.Add(piece.Position.y);
            }
            span=heights.Max()-heights.Min();
            return heights.Average();
        }
        float dry=Run(false,out float drySpan), wet=Run(true,out float wetSpan);
        report.Add($"MEASURE real-float garnish={garnish} dryY={dry:R} wetY={wet:R} rise={wet-dry:R} drySpan={drySpan:R} wetSpan={wetSpan:R}");
        Check(wet-dry>.06f, "Real GPU liquid supports contents above their dry resting height");
        Check(wetSpan<.04f, "Buoyant contents settle without sustained visible vertical jitter");
        Capture("floating-"+(garnish?"garnish":"ice"));
    }
    void Record(string name,int tick,FluidExperimentBody piece)
    {
        samples.Add(FormattableString.Invariant($"{name},{tick},{glass.Position.x:R},{glass.Position.y:R},{glass.Angle:R},{piece.Position.x:R},{piece.Position.y:R},{piece.Angle:R},{piece.Body.linearVelocity.x:R},{piece.Body.linearVelocity.y:R},{piece.Body.angularVelocity:R},{piece.ContainingVesselId}"));
    }
    void Capture(string name)
    {
        var camera=hand.inputCamera;
        var oldPosition=camera.transform.position; float oldSize=camera.orthographicSize;
        var oldTarget=camera.targetTexture; var oldActive=RenderTexture.active;
        var target=new RenderTexture(600,800,24); var pixels=new Texture2D(600,800,TextureFormat.RGB24,false);
        try
        {
            Vector2 center=glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
            camera.transform.position=new Vector3(center.x,center.y,-10); camera.orthographicSize=1.6f;
            camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,600,800),0,0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Evidence,name+".png"),pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=oldTarget; camera.transform.position=oldPosition; camera.orthographicSize=oldSize;
            RenderTexture.active=oldActive; target.Release(); Destroy(target); Destroy(pixels);
        }
    }
}

