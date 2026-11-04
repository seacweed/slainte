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

public static class IceBuoyancyValidation
{
    const string Key="FluidExperiment.IceBuoyancy";
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.playModeStateChanged+=state=>
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false))return;
        SessionState.EraseBool(Key);var runner=new GameObject("IceBuoyancyValidation").AddComponent<IceBuoyancyRunner>();
        runner.Smooth=SessionState.GetBool(Key+".Smooth",false);SessionState.EraseBool(Key+".Smooth");
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
    }
    public static void BeginSmooth(){SessionState.SetBool(Key+".Smooth",true);Begin();}
}

// Exercise immersion and contacts throughout entry and vessel movement, rather
// than accepting a quiet final stack as proof that its earlier motion was valid.
public sealed class IceBuoyancyRunner:MonoBehaviour
{
    public bool Smooth;
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    readonly List<string> report=new List<string>(),errors=new List<string>();
    readonly List<string> trace=new List<string>{"case,tick,id,x,y,angle,vx,vy,spin,wet,flow_x,flow_y,recovery,overlap,owner,glass_x,glass_y,glass_angle,native_vx,native_vy,repair_vx,repair_vy"};
    readonly List<FluidExperimentBody> cubes=new List<FluidExperimentBody>();
    FluidExperimentComparison comparison;FluidExperimentWorld world;FluidExperimentGpuLiquid gpu;
    FluidExperimentBody glass,prefab;bool failed;
    readonly Dictionary<uint,Vector2> nativePosition=new Dictionary<uint,Vector2>(),nativeVelocity=new Dictionary<uint,Vector2>(),repairVelocity=new Dictionary<uint,Vector2>();
    string Evidence=>Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool value,string message){report.Add((value?"PASS ":"FAIL ")+message);failed|=!value;}
    void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
    IEnumerator Start()
    {
        Application.logMessageReceived+=Log;
        for(int i=0;i<5;i++)yield return null;
        comparison=FindFirstObjectByType<FluidExperimentComparison>();while(!comparison.Ready)yield return null;
        var oldMode=Physics2D.simulationMode;
        try
        {
            world=comparison.World;gpu=comparison.Gpu;comparison.automaticScenario=false;comparison.showControls=false;
            world.interactor.enabled=false;world.enabled=false;gpu.automaticReadback=false;
            Physics2D.simulationMode=SimulationMode2D.Script;
            glass=world.Items.First(b=>b.kind==LabItemKind.Glass&&b.name.Contains("highball"));
            prefab=world.Items.First(b=>b.kind==LabItemKind.IceBucket).icePrefab;
            foreach(var mode in new[]{FluidExperimentIceLiquidMode.NonColliding,FluidExperimentIceLiquidMode.Colliding})
            {
                Reset(mode,0);var dry=Spawn(glass.LocalToWorld(new Vector2(0,glass.collisionProfile.InteriorBounds.yMin+.3f)),0);
                for(int i=0;i<350;i++)Step(i%5==0);
                Check(dry.Body.IsSleeping(),mode+" dry ice can sleep without repeated zero-force wakeups");
                Reset(mode,180);SamplerChecks(mode);
                Run(mode,1);Run(mode,8);
            }
            Check(errors.Count==0,"No Unity application errors");
        }
        catch(Exception e){failed=true;report.Add(e.ToString());}
        finally
        {
            Physics2D.simulationMode=oldMode;Application.logMessageReceived-=Log;report.AddRange(errors);
            File.WriteAllLines(Path.Combine(Evidence,"ice-buoyancy-validation.txt"),report);
            File.WriteAllLines(Path.Combine(Evidence,"ice-buoyancy-trace.csv"),trace);
            EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(failed?1:0);
        }
    }
    void Reset(FluidExperimentIceLiquidMode mode,float ml)
    {
        foreach(var c in cubes){c.gameObject.SetActive(false);Destroy(c.gameObject);}cubes.Clear();
        foreach(var b in world.Items.ToArray())
        {b.SetHeld(false);b.Teleport(new Vector2(-60-b.Id*3,20),0);b.Body.simulated=false;b.pourMlPerSecond=0;}
        gpu.ResetSimulation();gpu.iceLiquidMode=mode;
        glass.Body.simulated=true;glass.Body.bodyType=RigidbodyType2D.Kinematic;
        glass.Teleport(new Vector2(0,1.5f),0);glass.SetSealed(false);
        foreach(var b in world.Items){b.transform.SetPositionAndRotation(b.Position,Quaternion.Euler(0,0,b.Angle));b.SynchronizeHistory();}
        Physics2D.SyncTransforms();float queued=gpu.Fill(glass,world.Items.First(b=>b.ingredient!=null).ingredient,ml);
        report.Add($"FIXTURE {mode} queuedMl={queued:R} capacity={glass.capacityMl:R}");
        for(int i=0;i<150;i++)Step(i%5==0);
        report.Add($"FIXTURE {mode} settledMl={gpu.VolumeIn(glass.Id):R}");
    }
    FluidExperimentBody Spawn(Vector2 point,float angle)
    {
        var c=Instantiate(prefab,point,Quaternion.Euler(0,0,angle),world.transform);c.Teleport(point,angle);
        typeof(FluidExperimentBody).GetMethod("MarkPlaced",Private).Invoke(c,null);cubes.Add(c);
        Physics2D.SyncTransforms();world.RefreshIceContainment();return c;
    }
    void Step(bool read)
    {
        world.SendMessage("FixedUpdate");Physics2D.SyncTransforms();Physics2D.Simulate(.02f);
        foreach(var c in cubes){nativePosition[c.Id]=c.Position;nativeVelocity[c.Id]=c.Body.linearVelocity;}
        world.TickLiquid(.02f);
        foreach(var c in cubes)repairVelocity[c.Id]=c.Body.linearVelocity-nativeVelocity[c.Id];
        if(read)gpu.ReadbackNow();
    }
    void SamplerChecks(FluidExperimentIceLiquidMode mode)
    {
        // A shallow local puddle plus a distant tall column must not make this
        // cube fully submerged or pull it with the distant column's current.
        var c=Spawn(glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center),0);
        var saved=(GpuLiquidParticle[])gpu.Snapshot.Clone();Array.Clear(gpu.Snapshot,0,gpu.Snapshot.Length);
        Bounds box=c.SolidBounds;int slot=0;float localSurface=box.min.y+box.size.y*.15f;
        for(int x=0;x<5;x++)for(int y=0;y<3;y++)
            gpu.Snapshot[slot++]=new GpuLiquidParticle{Active=1,VesselId=glass.Id,VolumeMl=.5f,
                Position=new Vector2(box.center.x+(x-2)*gpu.Radius*.5f,localSurface-gpu.Radius-y*gpu.Radius*.5f)};
        for(int i=0;i<80;i++)gpu.Snapshot[slot++]=new GpuLiquidParticle{Active=1,VesselId=glass.Id,VolumeMl=.5f,
            Position=new Vector2(box.max.x+gpu.Radius*6+(i%4)*gpu.Radius,box.max.y+gpu.Radius*(i/4)*.2f),Velocity=Vector2.right*4};
        gpu.TrySampleGarnishFlow(c,out var flow,out float wet);
        report.Add($"MEASURE sampler {mode} localWet={wet:R} remoteFlow={flow:R}");
        Check(wet<.3f,mode+" distant liquid does not create local buoyancy");
        Check(flow.magnitude<.05f,mode+" distant current does not drive a stationary local puddle");
        Array.Clear(gpu.Snapshot,0,gpu.Snapshot.Length);
        for(int i=0;i<40;i++)gpu.Snapshot[i]=new GpuLiquidParticle{Active=1,VesselId=glass.Id,VolumeMl=.5f,
            Position=new Vector2(box.center.x+(i%5-2)*gpu.Radius*.5f,box.max.y+gpu.Radius*6+(i/5)*gpu.Radius*.5f)};
        gpu.TrySampleGarnishFlow(c,out flow,out wet);
        Check(wet==0,mode+" detached liquid above the cube supplies no buoyancy");
        Array.Clear(gpu.Snapshot,0,gpu.Snapshot.Length);gpu.TrySampleGarnishFlow(c,out flow,out wet);
        Check(wet==0&&flow==Vector2.zero,mode+" dry ice has no fluid force");
        Array.Copy(saved,gpu.Snapshot,saved.Length);
    }
    void Run(FluidExperimentIceLiquidMode mode,int count)
    {
        Reset(mode,180);string name=mode+"-"+count;var origin=glass.Position;
        float top=glass.LocalToWorld(new Vector2(0,glass.collisionProfile.InteriorBounds.yMax)).y;
        float maxOverlap=0,tailSpan=0,airLift=0,peakSpin=0;int overlapTicks=0;
        var tails=new Dictionary<uint,List<Vector2>>();var spins=new List<float>();
        // Preserve the strict final three-second limits, after the completed
        // buoyant return/contact settling rather than including its final roll.
        int drops=(count-1)*55,end=drops+1250;
        for(int tick=0;tick<end;tick++)
        {
            if(tick<=drops&&tick%55==0)
            {var c=Spawn(new Vector2((tick/55%2==0?-.12f:.12f),top+.3f),tick/55%2==0?5:-5);tails[c.Id]=new List<Vector2>();}
            if(tick==drops+450)glass.SetHeld(true);
            if(tick>=drops+450&&tick<drops+650)
            {float phase=Smooth?(tick-drops-450)*Mathf.PI*2/200:(tick-drops-450)*.06f;glass.SetHeldPose(origin+Vector2.right*(.2f*Mathf.Sin(phase)),10*Mathf.Sin(phase));}
            if(tick==drops+650)glass.SetHeldPose(origin,0);
            Step(tick%5==0);
            foreach(var c in cubes)
            {
                gpu.TrySampleGarnishFlow(c,out var flow,out float wet);float overlap=0;
                foreach(var other in cubes)if(c!=other)foreach(var a in c.solidColliders)foreach(var b in other.solidColliders)
                {var distance=a.Distance(b);if(distance.isValid)overlap=Mathf.Max(overlap,-distance.distance);}
                maxOverlap=Mathf.Max(maxOverlap,overlap);if(overlap>.03f)overlapTicks++;
                if(wet==0&&c.Body.linearVelocity.y>.1f&&tick>drops+200&&tick<drops+450)airLift=Mathf.Max(airLift,c.Body.linearVelocity.y);
                if(tick>=end-150){tails[c.Id].Add(c.Position);spins.Add(Mathf.Abs(c.Body.angularVelocity));}
                peakSpin=Mathf.Max(peakSpin,Mathf.Abs(c.Body.angularVelocity));
                if(tick%5==0)trace.Add(FormattableString.Invariant($"{name},{tick},{c.Id},{c.Position.x:R},{c.Position.y:R},{c.Angle:R},{c.Body.linearVelocity.x:R},{c.Body.linearVelocity.y:R},{c.Body.angularVelocity:R},{wet:R},{flow.x:R},{flow.y:R},{Vector2.Distance(c.Position,nativePosition[c.Id]):R},{overlap:R},{c.ContainingVesselId},{glass.Position.x:R},{glass.Position.y:R},{glass.Angle:R},{nativeVelocity[c.Id].x:R},{nativeVelocity[c.Id].y:R},{repairVelocity[c.Id].x:R},{repairVelocity[c.Id].y:R}"));
            }
            if(tick==drops+440||tick==end-1)Capture(name+"-"+tick);
        }
        tailSpan=tails.Values.Max(p=>Vector2.Distance(new Vector2(p.Min(v=>v.x),p.Min(v=>v.y)),new Vector2(p.Max(v=>v.x),p.Max(v=>v.y))));
        spins.Sort();float spin95=spins[(int)(spins.Count*.95f)];
        report.Add($"MEASURE {name} maxOverlap={maxOverlap:R} overlapTicks={overlapTicks} tailSpan={tailSpan:R} spin95={spin95:R} peakSpin={peakSpin:R} dryRise={airLift:R} ml={gpu.VolumeIn(glass.Id):R}");
        Check(tailSpan<.02f&&spin95<2,name+" stops drifting and rotating in still liquid after vessel movement");
        Check(maxOverlap<.04f,name+" avoids deep interpenetration throughout drops and vessel movement");
    }
    void Capture(string name)
    {
        foreach(var c in cubes)c.transform.SetPositionAndRotation(c.Position,Quaternion.Euler(0,0,c.Angle));
        var camera=world.interactor.inputCamera;var oldPosition=camera.transform.position;var oldSize=camera.orthographicSize;
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
        var target=new RenderTexture(600,800,24);var pixels=new Texture2D(600,800,TextureFormat.RGB24,false);
        try
        {var center=glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);camera.transform.position=new Vector3(center.x,center.y,-10);camera.orthographicSize=1.6f;
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,600,800),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(Evidence,name+".png"),pixels.EncodeToPNG());}
        finally{camera.targetTexture=oldTarget;camera.transform.position=oldPosition;camera.orthographicSize=oldSize;RenderTexture.active=oldActive;target.Release();Destroy(target);Destroy(pixels);}
    }
}
