using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

// This fixture leaves FixedUpdate, native Physics2D and async GPU readback running.
// Controller commands provide intent; it never manually ticks the simulation.
public static class FloatingContentsPlayLoopValidation
{
    const string Key = "FluidExperiment.FloatingContentsPlayLoop";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key,false)) return;
        SessionState.EraseBool(Key);
        var runner=new GameObject("FloatingContentsPlayLoop").AddComponent<FloatingContentsPlayLoopRunner>();
        runner.Nananga=SessionState.GetBool(Key+".Nananga",false);
        SessionState.EraseBool(Key+".Nananga");
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key,true); EditorApplication.isPlaying=true;
    }
    public static void BeginNananga()
    {
        SessionState.SetBool(Key+".Nananga",true); Begin();
    }
}

[DefaultExecutionOrder(10000)]
public sealed class FloatingContentsPlayLoopRunner : MonoBehaviour
{
    public bool Nananga;
    readonly List<string> report=new List<string>();
    readonly List<string> errors=new List<string>();
    readonly List<string> timeline=new List<string>{"tick,kind,glass_x,glass_angle,x,y,vx,vy,spin,owner,snapshot_revision"};
    readonly List<string> renderTimeline=new List<string>{"frame,time,visual_gap,physics_gap,position_lag,angle_lag,interpolation,glass_lag"};
    FluidExperimentBody renderGlass, renderGarnish;
    Camera renderCamera;
    RenderTexture renderTarget;
    float worstVisualGap=100, worstPhysicsGap=100, maximumLag;
    int renderedFrames;
    bool failed;
    void Check(bool condition,string message)
    {
        report.Add((condition?"PASS ":"FAIL ")+message);
        if(!condition) failed=true;
    }
    void Log(string message,string trace,LogType type)
    {
        if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) errors.Add(message);
    }
    IEnumerator Start()
    {
        IEnumerator run=Run();
        while(true)
        {
            bool more=false; object current=null;
            try { more=run.MoveNext(); if(more) current=run.Current; }
            catch(Exception exception) { report.Add(exception.ToString()); failed=true; break; }
            if(!more) break;
            yield return current;
        }
        Finish();
    }
    IEnumerator Run()
    {
        Application.logMessageReceived+=Log;
        // Comparison performs a deferred initial mode reset after Ready becomes
        // true. Let that authored startup finish before modifying fixture poses.
        for(int startup=0;startup<5;startup++) yield return null;
        var comparison=FindFirstObjectByType<FluidExperimentComparison>();
        while(!comparison.Ready) yield return null;
        var world=comparison.World; var gpu=comparison.Gpu; var hand=world.interactor;
        comparison.automaticScenario=false; comparison.showControls=false; world.showControls=false; hand.enabled=false;
        Check(world.enabled&&Physics2D.simulationMode==SimulationMode2D.FixedUpdate&&gpu.automaticReadback,
            "Native automatic world/Physics2D/async-readback scheduling remains enabled");
        var glass=world.Items.First(b=>b.kind==LabItemKind.Glass&&b.name.Contains("highball"));
        var icePrefab=world.Items.First(b=>b.kind==LabItemKind.IceBucket).icePrefab;
        var sources=world.GetComponentsInChildren<FluidExperimentGarnishSource>();
        var garnishPrefab=(Nananga?sources.Last():sources.First()).garnishPrefab;
        report.Add("INFO garnish="+garnishPrefab.name+" iceMode="+gpu.iceLiquidMode);
        var ingredient=world.Items.First(b=>b.ingredient!=null).ingredient;
        foreach(var body in world.Items.ToArray())
        {
            body.SetHeld(false); body.Teleport(new Vector2(-60-body.Id*3,20),0);
            body.Body.simulated=false; body.pourMlPerSecond=0;
            body.transform.SetPositionAndRotation(body.Position,Quaternion.Euler(0,0,body.Angle));
        }
        gpu.ResetSimulation(); glass.Body.simulated=true; glass.Teleport(new Vector2(0,1.5f),0);
        glass.Body.bodyType=RigidbodyType2D.Kinematic;
        glass.transform.SetPositionAndRotation(new Vector3(0,1.5f,0),Quaternion.identity);
        Physics2D.SyncTransforms(); glass.SynchronizeHistory();
        report.Add($"INFO fixture initial native={glass.Position} rendered={glass.transform.position} leftWall={world.leftBoundary.bounds.center} rightWall={world.rightBoundary.bounds.center}");
        Check(gpu.Fill(glass,ingredient,60)==60,"Native-loop fixture queues 60 ml");
        var wait=new WaitForFixedUpdate();
        for(int i=0;i<100;i++) yield return wait;
        var center=glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        var ice=Instantiate(icePrefab,center+new Vector2(-.15f,.05f),Quaternion.identity,world.transform);
        var garnish=Instantiate(garnishPrefab,center+new Vector2(.15f,.2f),Quaternion.identity,world.transform);
        var solids=new[]{ice,garnish}; var local=new[]{new List<Vector2>(),new List<Vector2>()};
        for(int i=0;i<100;i++) yield return wait;
        // Match real interaction order: place contents in the unheld receiver,
        // establish ownership, then pick up that receiver with the single hand.
        if(solids.Any(s=>s==null))
        {
            Check(false,"Contents survive the unheld receiver setup"); yield break;
        }
        Check(Vector2.Distance(glass.Position,new Vector2(0,1.5f))<.01f,"Deferred comparison startup cannot reset the fixture pose");
        Check(solids.All(s=>s.ContainingVesselId==glass.Id),"Native receiver acquires both solids before pickup");
        Check(hand.Pick(glass,glass.Position),"Controller picks the actual filled glass");
        renderGlass=glass; renderGarnish=garnish; renderCamera=hand.inputCamera;
        int revision=gpu.SnapshotRevision; Vector2 origin=glass.Position;
        for(int i=0;i<200;i++)
        {
            float phase=i*Mathf.PI*2/60;
            glass.SetHeldPose(origin+Vector2.right*(i<150?.5f*Mathf.Sin(phase):0),i<150?25*Mathf.Sin(phase):0);
            yield return wait;
            if(solids.Any(s=>s==null))
            {
                Check(false,"Contents survive automatic receiver movement"); yield break;
            }
            for(int n=0;n<solids.Length;n++)
            {
                var solid=solids[n]; local[n].Add(glass.WorldToLocal(solid.Position));
                timeline.Add(FormattableString.Invariant($"{i},{solid.kind},{glass.Position.x:R},{glass.Angle:R},{solid.Position.x:R},{solid.Position.y:R},{solid.Body.linearVelocity.x:R},{solid.Body.linearVelocity.y:R},{solid.Body.angularVelocity:R},{solid.ContainingVesselId},{gpu.SnapshotRevision}"));
            }
        }
        Check(gpu.SnapshotRevision>revision,"Actual asynchronous snapshots continue during automatic movement");
        renderCamera=null;
        report.Add($"MEASURE render frames={renderedFrames} visualGap={worstVisualGap:R} physicsGap={worstPhysicsGap:R} lag={maximumLag:R}");
        Check(renderedFrames>=20,"Native loop samples actual camera render frames");
        Check(worstVisualGap>=-.012f,"Rendered garnish remains within one source pixel of glass walls");
        for(int n=0;n<solids.Length;n++)
        {
            var positions=local[n]; var solid=solids[n];
            float span=Vector2.Distance(new Vector2(positions.Min(p=>p.x),positions.Min(p=>p.y)),new Vector2(positions.Max(p=>p.x),positions.Max(p=>p.y)));
            report.Add($"MEASURE native-loop {solid.kind} relativeSpan={span:R} owner={solid.ContainingVesselId} speed={solid.Body.linearVelocity.magnitude:R}");
            Check(span>.04f&&span<3&&solid.ContainingVesselId==glass.Id
                &&solid.Body.bodyType==RigidbodyType2D.Dynamic&&solid.Body.linearVelocity.magnitude<20,
                "Native automatic loop retains independent bounded "+solid.kind+" motion inside receiver");
        }
        gpu.ReadbackNow(); var before=(GpuLiquidParticle[])gpu.Snapshot.Clone();
        var velocities=solids.Select(s=>s.Body.linearVelocity).ToArray(); var spins=solids.Select(s=>s.Body.angularVelocity).ToArray();
        Check(hand.Drop(glass.Position),"Actual click-drop succeeds in automatic Play loop");
        gpu.ReadbackNow(); var after=gpu.Snapshot;
        var owned=Enumerable.Range(0,before.Length).Where(i=>before[i].Active!=0&&before[i].VesselId==glass.Id).ToArray();
        Check(owned.Length>0&&owned.All(i=>before[i].Velocity==after[i].Velocity
            &&Vector2.Distance(before[i].Position-before[i].PreviousPosition,after[i].Position-after[i].PreviousPosition)<.00001f),
            "Automatic-loop click-drop preserves every owned liquid velocity and motion history");
        Check(solids.Select((s,i)=>s.Body.linearVelocity==velocities[i]&&s.Body.angularVelocity==spins[i]).All(v=>v),
            "Automatic-loop click-drop adds no solid velocity or spin");
        Check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl)<.001,"Automatic loop conserves generated liquid");
        Check(errors.Count==0,"No Unity application errors or assertions");
        yield return wait;
        Check(glass.Body.interpolation==RigidbodyInterpolation2D.Interpolate
            &&solids.All(s=>s.Body.interpolation==RigidbodyInterpolation2D.Interpolate),
            "Releasing the glass restores the previous interpolation policy");
    }
    void Finish()
    {
        renderCamera=null;
        if(renderTarget!=null) { renderTarget.Release(); Destroy(renderTarget); }
        Application.logMessageReceived-=Log; report.AddRange(errors);
        string evidence=Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
        File.WriteAllLines(Path.Combine(evidence,"floating-playloop-validation.txt"),report);
        File.WriteAllLines(Path.Combine(evidence,"floating-playloop-timeline.csv"),timeline);
        File.WriteAllLines(Path.Combine(evidence,"floating-render-timeline.csv"),renderTimeline);
        File.WriteAllText(Path.Combine(evidence,"floating-playloop-result.txt"),failed?"FAIL":"PASS");
        EditorApplication.isPlaying=false;
        EditorApplication.delayCall+=()=>EditorApplication.Exit(failed?1:0);
    }
    void LateUpdate()
    {
        if(renderCamera==null||renderGarnish==null||renderGlass==null) return;
        float visual=WallGap(true), physical=WallGap(false);
        float lag=Vector2.Distance(renderGarnish.transform.position,renderGarnish.Position);
        worstVisualGap=Mathf.Min(worstVisualGap,visual); worstPhysicsGap=Mathf.Min(worstPhysicsGap,physical);
        maximumLag=Mathf.Max(maximumLag,lag); renderedFrames++;
        renderTimeline.Add(FormattableString.Invariant($"{Time.frameCount},{Time.time:R},{visual:R},{physical:R},{lag:R},{Mathf.DeltaAngle(renderGarnish.Angle,renderGarnish.transform.eulerAngles.z):R},{renderGarnish.Body.interpolation},{Vector2.Distance(renderGlass.transform.position,renderGlass.Position):R}"));
        // Batch mode does not present Game View frames. Render the recorded transform
        // state explicitly, while leaving automatic physics and async readback running.
        if(renderTarget==null) renderTarget=new RenderTexture(160,200,24);
        var previous=renderCamera.targetTexture;
        try { renderCamera.targetTexture=renderTarget; renderCamera.Render(); }
        finally { renderCamera.targetTexture=previous; }
    }
    float WallGap(bool visual)
    {
        if(!visual) return renderGarnish.solidColliders.Min(a=>renderGlass.solidColliders.Min(b=>a.Distance(b).distance));
        float closest=100;
        var polygon=renderGlass.LiquidInteriorPath;
        foreach(var hull in renderGarnish.collisionProfile.solids)
        foreach(var vertex in hull.points)
        {
            Vector2 point=renderGlass.transform.InverseTransformPoint(renderGarnish.transform.TransformPoint(vertex));
            // An open mouth is a legitimate exit, not a wall crossing.
            if(point.y>renderGlass.collisionProfile.InteriorBounds.yMax) continue;
            float distance=100; bool inside=false;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
            {
                Vector2 a0=polygon[j],b0=polygon[i],edge=b0-a0;
                float t=Mathf.Clamp01(Vector2.Dot(point-a0,edge)/Mathf.Max(1e-12f,edge.sqrMagnitude));
                distance=Mathf.Min(distance,Vector2.Distance(point,a0+edge*t));
                if((a0.y>point.y)!=(b0.y>point.y)
                    &&point.x<(b0.x-a0.x)*(point.y-a0.y)/(b0.y-a0.y)+a0.x) inside=!inside;
            }
            closest=Mathf.Min(closest,inside?distance:-distance);
        }
        return closest;
    }
}
