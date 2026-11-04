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

public static class ExperimentContactBreakageValidation
{
    const string Key="FluidExperiment.ContactBreakageValidation";
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.playModeStateChanged+=state=>
    {
        if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key,false))return;
        SessionState.EraseBool(Key);new GameObject("ContactBreakageValidation").AddComponent<ContactBreakageRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
    }
}

// Isolated GPU/native replay. All fixture-only body changes remain in this copy.
public sealed class ContactBreakageRunner:MonoBehaviour
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const float Dt=.01f;
    FluidExperimentComparison comparison;
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentBody glass;
    FluidExperimentBreakableGlass fracture;
    FluidExperimentBreakageProfile sourceProfile,testProfile;
    ItemDef ingredient;
    float floor;
    readonly List<string> report=new List<string>(),errors=new List<string>();
    string Evidence=>Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    object Field(object o,string name)=>o.GetType().GetField(name,Members).GetValue(o);
    object Invoke(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Members).Invoke(o,args);
    void Check(bool ok,string label){report.Add((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label);}
    void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
    GpuLiquidParticle[] Read(){gpu.ReadbackNow();return (GpuLiquidParticle[])gpu.Snapshot.Clone();}
    IEnumerator Start()
    {
        Application.logMessageReceived+=Log;yield return null;
        comparison=FindFirstObjectByType<FluidExperimentComparison>();
        while(!comparison.Ready)yield return null;
        var oldMode=Physics2D.simulationMode;var oldGravity=Physics2D.gravity;bool success=false;
        try
        {
            world=comparison.World;gpu=comparison.Gpu;comparison.automaticScenario=false;comparison.showControls=false;
            comparison.StartScenario(FluidExperimentScenario.Manual);
            world.seedLiquids=false;world.enabled=false;world.interactor.enabled=false;gpu.automaticReadback=false;
            Physics2D.simulationMode=SimulationMode2D.Script;Physics2D.gravity=new Vector2(0,-9.81f);
            glass=world.Items.First(b=>b.name.ToLowerInvariant().Contains("hurricane")&&b.kind==LabItemKind.Glass);
            fracture=glass.GetComponent<FluidExperimentBreakableGlass>();ingredient=world.Items.First(b=>b.ingredient!=null).ingredient;
            sourceProfile=fracture.profile;testProfile=Instantiate(sourceProfile);fracture.profile=testProfile;
            floor=world.floorColliders.First(c=>c!=null&&c.bounds.min.x<0&&c.bounds.max.x>0).bounds.max.y;
            Check(Application.isBatchMode&&gpu.CohesivePhysicsActive&&comparison.ActiveMode==FluidExperimentMode.FCoherentLiquid,
                "Authored default is F contact baseline in actual hidden GPU execution");
            Check(fracture.splashProfile==null&&!world.ExplosiveGlassBreakage,"Forced liquid fan and G fragment explosion are excluded from the baseline");
            Check(world.liquidSurfaces.Length==1&&world.liquidSurfaces[0]==world.floorColliders[0]&&world.shardLiquidCollision,
                "Authored comparison imports its actual floor and thin fragment liquid contacts");
            Check(testProfile.minimumImpactSpeed==6&&testProfile.minimumImpulsePerMass==4&&testProfile.shards.Length==16,
                "Glass thresholds, native motion inheritance and authored 16-piece geometry stay unchanged");
            StaticFloorRepair();
            FloorOnlyDrop();
            PoolDrop();
            VesselDrop(false);
            VesselDrop(true);
            StaticFragments();
            float right=Bottle(1),left=Bottle(-1);
            Check(right>.05f&&left<-.05f,"Opposite real bottle contacts transport liquid in opposite directions without a fan");
            Check(errors.Count==0,"No Unity, shader or buffer errors");success=true;
        }
        catch(Exception ex){report.Add(ex.ToString());}
        finally
        {
            if(fracture!=null){fracture.enabled=true;if(sourceProfile!=null)fracture.profile=sourceProfile;}
            if(testProfile!=null)Destroy(testProfile);
            Physics2D.simulationMode=oldMode;Physics2D.gravity=oldGravity;Application.logMessageReceived-=Log;
            report.AddRange(errors);
            File.WriteAllLines(Path.Combine(Evidence,"contact-breakage-validation.txt"),report);
            File.WriteAllText(Path.Combine(Evidence,"contact-breakage-result.txt"),success?"PASS":"FAIL");
            EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(success?0:1);
        }
    }
    void Reset(float gap=3)
    {
        fracture.enabled=true;world.ResetSession();world.enabled=false;world.interactor.enabled=false;gpu.automaticReadback=false;
        foreach(var b in world.Items.ToArray())
        {b.SetHeld(false);b.Teleport(new Vector2(-60-b.Id*3,20),0);b.Body.simulated=false;b.pourMlPerSecond=0;}
        gpu.ResetSimulation();
        var rb=glass.Body;rb.simulated=true;rb.bodyType=RigidbodyType2D.Kinematic;rb.gravityScale=1;rb.linearDamping=.1f;
        rb.angularDamping=.1f;rb.constraints=RigidbodyConstraints2D.None;rb.interpolation=RigidbodyInterpolation2D.None;
        glass.Teleport(new Vector2(0,floor+2),0);Physics2D.SyncTransforms();
        glass.Teleport(glass.Position+Vector2.up*(floor+gap-glass.SolidBounds.min.y),0);glass.SynchronizeHistory();
        Physics2D.SyncTransforms();world.RefreshCollisionPairs();
    }
    void Native(float dt=Dt)
    {
        world.SendMessage("FixedUpdate");
        foreach(var rb in world.GetComponentsInChildren<Rigidbody2D>())if(rb.simulated)rb.interpolation=RigidbodyInterpolation2D.None;
        Physics2D.SyncTransforms();Physics2D.Simulate(dt);
    }
    void Step(float dt=Dt){Native(dt);world.TickLiquid(dt);}
    void Fill()
    {
        Check(Mathf.Abs(gpu.Fill(glass,ingredient,40)-40)<.001f,"Fixture fills the original 40 ml hurricane");
        for(int i=0;i<240;i++)Step();Read();
        Check(Mathf.Abs(gpu.VolumeIn(glass.Id)-40)<.001f,"Filled fixture starts settled with 40 ml owned by the glass");
    }
    float ActiveMl(GpuLiquidParticle[] p)=>p.Where(x=>x.Active!=0).Sum(x=>x.VolumeMl);
    float Energy(GpuLiquidParticle[] particles)=>particles.Where(p=>p.Active!=0).Sum(p=>(.5f*p.Velocity.sqrMagnitude+9.81f*(p.Position.y-floor))*p.VolumeMl);
    void StaticFloorRepair()
    {
        Reset();glass.Body.simulated=false;var gravity=Physics2D.gravity;Physics2D.gravity=Vector2.zero;
        try
        {
            Check(gpu.TryEmit(new Vector2(0,floor+gpu.Radius*.2f),Vector2.zero,ingredient,.5f,0),"Static shallow-penetration fixture reserves");
            float peak=0,minY=float.MaxValue;
            for(int i=0;i<20;i++){Step();var p=Read().First(x=>x.Active!=0);peak=Mathf.Max(peak,p.Velocity.magnitude);minY=Mathf.Min(minY,p.Position.y);}
            report.Add($"MEASURE floorRepair peakSpeed={peak:R} minY={minY:R} floor={floor:R} radius={gpu.Radius:R}");
            Check(minY>=floor+gpu.Radius-.005f,"Static floor repair restores nonpenetration");
            Check(peak<.05f,"Stationary floor depenetration cannot launch liquid or create kinetic energy");
        }
        finally{Physics2D.gravity=gravity;}
    }
    void FloorOnlyDrop()
    {
        Reset();glass.Body.simulated=false;float initial=0,peak=0,minY=float.MaxValue;
        foreach(float x in new[]{-3f,-1f,1f,3f})
        {Check(gpu.TryEmit(new Vector2(x,floor+2),Vector2.down*4,ingredient,.5f,0),"Independent free drop reserves");initial+=(8+9.81f*2)*.5f;}
        for(int i=0;i<220;i++)
        {
            Step();var p=Read();peak=Mathf.Max(peak,Energy(p));
            foreach(var a in p)if(a.Active!=0)minY=Mathf.Min(minY,a.Position.y);
        }
        var final=Read();float speed=final.Where(p=>p.Active!=0).Max(p=>p.Velocity.magnitude);
        report.Add($"MEASURE freeDrop initialEnergy={initial:R} peakEnergy={peak:R} minY={minY:R} restingSpeed={speed:R}");
        Check(minY>=floor+gpu.Radius-.015f&&Mathf.Abs(ActiveMl(final)-2)<.001f,"Independent drops stay above the real liquid floor with exact volume");
        Check(peak<=initial*1.1f&&speed<.15f,"Static inelastic floor contact cannot add energy to isolated falling drops");
    }
    void PoolDrop()
    {
        Reset();Fill();var before=Read();gpu.ReleaseOwner(glass.Id);glass.Body.simulated=false;
        var after=Read();bool exact=true;
        for(int i=0;i<before.Length;i++){var a=before[i];var b=after[i];if(a.VesselId==glass.Id){exact&=b.VesselId==0;b.VesselId=a.VesselId;}exact&=a.Equals(b);}
        Check(exact,"Removing a vessel without fragments changes only ownership, not liquid velocity or position");
        float minY=float.MaxValue;
        for(int i=0;i<300;i++)
        {
            Step();var p=Read();foreach(var a in p)if(a.Active!=0)minY=Mathf.Min(minY,a.Position.y);
            if(i%4==0)Capture("pool-"+(i/4).ToString("000")+".png",new Vector2(0,floor+1.8f),3.5f);
        }
        var final=Read();
        report.Add($"MEASURE pool minY={minY:R} activeMl={ActiveMl(final):R} maxSpeed={final.Where(p=>p.Active!=0).Max(p=>p.Velocity.magnitude):R}");
        Check(minY>=floor+gpu.Radius-.025f&&Mathf.Abs(ActiveMl(final)-40)<.001f,"An uncontained 40 ml pool falls onto the floor instead of draining through it");
        Check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl)<.001,"Pool drop preserves volume accounting");
    }
    void CommitAndCheck()
    {
        var before=Read();var composition=(float[])gpu.CompositionSnapshot.Clone();uint owner=glass.Id;
        Invoke(world,"ProcessGlassBreakage");var after=Read();bool exact=true;
        for(int i=0;i<before.Length;i++){var a=before[i];var b=after[i];if(a.VesselId==owner){exact&=b.VesselId==0;b.VesselId=owner;}exact&=a.Equals(b);}
        Check(exact&&composition.SequenceEqual(gpu.CompositionSnapshot),"Fracture transaction releases liquid with bitwise preserved position, history, velocity, volume and composition");
        Check(fracture.IsBroken&&world.GlassShards.Count==16,"Fracture replaces the original glass with exactly sixteen shards");
        bool history=true;int segments=0;
        foreach(var shard in world.GlassShards)
        {
            var proxy=shard.GetComponent<FluidExperimentShardLiquidProxy>();
            history&=proxy!=null&&Vector2.Distance(proxy.PreviousPosition,shard.Body.position)<.0001f
                &&Mathf.Abs(Mathf.DeltaAngle(proxy.PreviousAngle,shard.Body.rotation))<.001f;
            if(proxy!=null)segments+=proxy.Segments.Count;
        }
        Check(history,"New shard histories start at the activated native pose, preventing a first-tick fictitious sweep");
        Invoke(gpu,"UploadGeometry",0f,1f,Dt);
        var edges=(GpuLiquidBoundarySegment[])Field(gpu,"boundaryUpload");
        Check(segments>0&&gpu.LastShardBoundaryCount==segments&&gpu.SkippedShardBoundaryCount==0,
            "All thin shard walls enter the very next GPU geometry upload without budget omission");
        Check(!edges.Any(e=>e.VesselId==owner),"Original vessel boundaries do not coexist with the replacement shard walls");
        report.Add($"MEASURE transition thinSegments={segments} uploaded={gpu.LastShardBoundaryCount} omitted={gpu.SkippedShardBoundaryCount}");
    }
    void VesselDrop(bool breaks)
    {
        Reset();Fill();fracture.enabled=breaks;glass.Body.bodyType=RigidbodyType2D.Dynamic;
        bool committed=false;float minY=float.MaxValue,maxSpeed=0;int clip=0;
        var rows=new List<string>{"seconds,broken,activeMl,minY,maxSpeed,energy,shardEdges,omittedEdges"};
        for(int i=0;i<260;i++)
        {
            Native();
            if(fracture.BreakQueued&&!committed){CommitAndCheck();committed=true;}
            world.TickLiquid(Dt);var p=Read();
            float y=p.Where(a=>a.Active!=0).Min(a=>a.Position.y),v=p.Where(a=>a.Active!=0).Max(a=>a.Velocity.magnitude);
            minY=Mathf.Min(minY,y);maxSpeed=Mathf.Max(maxSpeed,v);
            rows.Add(FormattableString.Invariant($"{(i+1)*Dt:R},{fracture.IsBroken},{ActiveMl(p):R},{y:R},{v:R},{Energy(p):R},{gpu.LastShardBoundaryCount},{gpu.SkippedShardBoundaryCount}"));
            if(i%4==0)Capture((breaks?"break-":"intact-")+(clip++).ToString("000")+".png",new Vector2(0,floor+2),4);
        }
        File.WriteAllLines(Path.Combine(Evidence,breaks?"break-trajectory.csv":"intact-trajectory.csv"),rows);
        report.Add($"MEASURE vessel breaks={breaks} committed={committed} minY={minY:R} maxSpeed={maxSpeed:R} activeMl={ActiveMl(Read()):R}");
        Check(committed==breaks&&fracture.IsBroken==breaks,"Intact and breakable controls follow their actual native break eligibility");
        Check(minY>=floor+gpu.Radius-.03f&&Mathf.Abs(ActiveMl(Read())-40)<.001f,"Vessel drop keeps forty ml above the liquid floor");
        Check(gpu.SkippedShardBoundaryCount==0&&Math.Abs(gpu.Ledger.Total.ConservationErrorMl)<.001,"Post-impact continuation has no missing shard walls or liquid accounting error");
        fracture.enabled=true;
    }
    void StaticFragments()
    {
        Reset();Fill();glass.Body.bodyType=RigidbodyType2D.Dynamic;glass.Body.gravityScale=0;
        Invoke(fracture,"QueueImpact",8f,glass.Body.mass*8,glass.Position);CommitAndCheck();
        foreach(var shard in world.GlassShards){shard.Body.bodyType=RigidbodyType2D.Kinematic;shard.Body.linearVelocity=Vector2.zero;shard.Body.angularVelocity=0;shard.GetComponent<FluidExperimentShardLiquidProxy>().SynchronizeHistory();}
        float escaped=0,peakSpeed=0;
        for(int i=0;i<240;i++)
        {
            Step();var p=Read();escaped=Mathf.Max(escaped,p.Where(a=>a.Active!=0&&!glass.ContainsLiquid(a.Position)).Sum(a=>a.VolumeMl));
            peakSpeed=Mathf.Max(peakSpeed,p.Where(a=>a.Active!=0).Max(a=>a.Velocity.magnitude));
        }
        report.Add($"MEASURE staticShards escapedMl={escaped:R} peakSpeed={peakSpeed:R}");
        Check(escaped<=.5f,"Static assembled thin shards do not expel a previously settled pool");
        Check(Math.Abs(ActiveMl(Read())-40)<.001&&gpu.SkippedShardBoundaryCount==0,"Static-fragment test preserves forty ml and uploads every wall");
    }
    float Bottle(int direction)
    {
        Reset();Fill();var bottle=world.Items.First(b=>b.kind==LabItemKind.Bottle);var rb=bottle.Body;
        rb.simulated=true;rb.bodyType=RigidbodyType2D.Dynamic;rb.gravityScale=0;rb.linearDamping=0;rb.constraints=RigidbodyConstraints2D.FreezeRotation|RigidbodyConstraints2D.FreezePositionY;
        Bounds g=glass.SolidBounds;bottle.Teleport(new Vector2(0,g.center.y),0);Physics2D.SyncTransforms();Bounds b=bottle.SolidBounds;
        bottle.Teleport(bottle.Position+new Vector2(direction>0?g.min.x-.5f-b.max.x:g.max.x+.5f-b.min.x,g.center.y-b.center.y),0);
        glass.Body.bodyType=RigidbodyType2D.Dynamic;glass.Body.gravityScale=0;rb.linearVelocity=Vector2.right*(direction*12);
        Physics2D.SyncTransforms();world.RefreshCollisionPairs();
        bool committed=false;float origin=0,shift=0;int frames=0,clip=0;
        for(int i=0;i<150;i++)
        {
            Native();if(fracture.BreakQueued&&!committed){var p=Read();origin=p.Where(a=>a.Active!=0).Sum(a=>a.Position.x*a.VolumeMl)/40;CommitAndCheck();committed=true;}
            world.TickLiquid(Dt);var current=Read();
            if(committed)
            {
                frames++;if(frames==30)shift=current.Where(a=>a.Active!=0).Sum(a=>a.Position.x*a.VolumeMl)/40-origin;
                if(frames<=80&&frames%4==0)Capture((direction>0?"bottle-right-":"bottle-left-")+(clip++).ToString("000")+".png",new Vector2(0,g.center.y-.2f),4);
            }
        }
        report.Add($"MEASURE bottle direction={direction} committed={committed} shiftAt300ms={shift:R} activeMl={ActiveMl(Read()):R} omitted={gpu.SkippedShardBoundaryCount}");
        Check(committed&&Math.Abs(gpu.Ledger.Total.ConservationErrorMl)<.001&&gpu.SkippedShardBoundaryCount==0,"Real bottle fracture uses physical contacts with preserved accounting and complete wall geometry");
        return shift;
    }
    void Capture(string name,Vector2 center,float halfHeight)
    {
        var camera=world.interactor.inputCamera;var oldTarget=camera.targetTexture;var oldPosition=camera.transform.position;
        float oldSize=camera.orthographicSize;var oldActive=RenderTexture.active;
        var target=new RenderTexture(960,720,24);var pixels=new Texture2D(960,720,TextureFormat.RGB24,false);
        var hidden=new List<SpriteRenderer>();
        try
        {
            foreach(var body in world.Items)if(!body.Body.simulated)foreach(var art in body.GetComponentsInChildren<SpriteRenderer>())if(art.enabled){hidden.Add(art);art.enabled=false;}
            camera.targetTexture=target;camera.transform.position=new Vector3(center.x,center.y,oldPosition.z);camera.orthographicSize=halfHeight;
            Physics2D.SyncTransforms();camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,960,720),0,0);pixels.Apply();
            File.WriteAllBytes(Path.Combine(Evidence,name),pixels.EncodeToPNG());
        }
        finally
        {
            foreach(var art in hidden)if(art!=null)art.enabled=true;
            camera.targetTexture=oldTarget;camera.transform.position=oldPosition;camera.orthographicSize=oldSize;RenderTexture.active=oldActive;
            target.Release();Destroy(target);Destroy(pixels);
        }
    }
}
