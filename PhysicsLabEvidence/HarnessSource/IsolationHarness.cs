using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.PhysicsLab;

public static class IsolationHarness
{
    private const string Key = "PhysicsLab.IsolationHarness";
    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }
    public static void Begin()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }
    private static void Entered(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key,false)) return;
        SessionState.EraseBool(Key);
        Run();
    }
    private static readonly List<string> report = new List<string>();
    private static PhysicsLabWorld world;
    private static PhysicsLabGpuLiquid gpu;
    private static PhysicsLabBody remote, open, mover;
    private static ItemDef ingredient;
    private static int maximumSteps;
    private static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    private static void Require(bool condition,string message)
    { report.Add((condition?"PASS: ":"FAIL: ")+message);if(!condition)throw new Exception(message); }
    private static PhysicsLabBody Body(string name,Vector2[] wall,Rect[] regions,Vector2 position,bool closed)
    {
        GameObject obj=new GameObject(name);obj.transform.SetParent(world.transform,false);
        obj.transform.position=position;
        PhysicsLabBody body=obj.AddComponent<PhysicsLabBody>();
        body.liquidWall=wall;body.contentRegions=regions;body.sealedVessel=closed;
        body.kind=regions.Length>0?LabItemKind.Glass:LabItemKind.Spoon;
        body.wallClosed=regions.Length==0;body.pourMlPerSecond=0;
        body.Body.bodyType=RigidbodyType2D.Kinematic;
        return body;
    }
    private static void Setup()
    {
        var root=new GameObject("IsolatedGPUFixture");world=root.AddComponent<PhysicsLabWorld>();
        world.enabled=false;world.seedLiquids=false;world.showControls=false;
        gpu=root.AddComponent<PhysicsLabGpuLiquid>();world.liquid=gpu;
        gpu.renderParticles=false;gpu.automaticReadback=false;
        gpu.settings=ScriptableObject.CreateInstance<PhysicsLabLiquidSettings>();
        gpu.settings.gpuLiquidParticleCapacity=256;
        gpu.settings.gpuLiquidComputeShader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/PhysicsLabLiquid.compute");
        gpu.drawShader=Shader.Find("Hidden/Internal-Colored");
        ingredient=ScriptableObject.CreateInstance<ItemDef>();
        Vector2[] bowl={new Vector2(-1,1.5f),new Vector2(-1,-1.5f),new Vector2(1,-1.5f),new Vector2(1,1.5f)};
        Rect[] interior={new Rect(-.95f,-1.45f,1.9f,2.9f)};
        remote=Body("RemoteSealedVessel",bowl,interior,new Vector2(5,0),true);
        open=Body("RemoteOpenVessel",bowl,interior,new Vector2(10,0),false);
        mover=Body("UnrelatedHeldSolid",new[]{new Vector2(-.1f,-1),new Vector2(.1f,-1),new Vector2(.1f,1),new Vector2(-.1f,1)},Array.Empty<Rect>(),new Vector2(-12,5),false);
        mover.SetHeld(true);gpu.Initialize(world);
        Require(gpu.IsOperational,"Actual compute GPU: "+SystemInfo.graphicsDeviceName+" / "+SystemInfo.graphicsDeviceType);
    }
    private static void Tick()
    {
        world.SendMessage("FixedUpdate");world.TickLiquid(.02f);
        maximumSteps=Math.Max(maximumSteps,gpu.LastSubsteps);
    }
    private static void Seed(PhysicsLabBody vessel,int count)
    {
        for(int i=0;i<count;i++)RequireEmit(vessel.LocalToWorld(new Vector2(-.6f+(i%8)*.16f,-1.1f+(i/8)*.16f)),vessel.Id);
    }
    private static void RequireEmit(Vector2 position,uint owner)
    { if(!gpu.TryEmit(position,Vector2.zero,ingredient,.5f,owner))throw new Exception("Fixture emission rejected"); }
    private static void Reset()
    {
        gpu.ResetSimulation();remote.SetHeld(false);open.SetHeld(false);
        remote.Body.bodyType=open.Body.bodyType=RigidbodyType2D.Kinematic;
        remote.SetSealed(true);remote.Teleport(new Vector2(5,0),0);open.Teleport(new Vector2(10,0),0);
        mover.SetHeld(true);mover.Teleport(new Vector2(-12,5),0);
        maximumSteps=0;
    }
    private static GpuLiquidParticle[] RemoteRun(int mode)
    {
        Reset();Seed(remote,32);Seed(open,32);
        for(int i=0;i<60;i++)Tick();
        maximumSteps=0;
        for(int i=0;i<20;i++)
        {
            if(mode==1)mover.SetHeldPose(new Vector2(-12+(i%2==0?2.6f:0),5),0);
            if(mode==2)mover.SetHeldPose(new Vector2(-12,5),i*180f);
            Tick();
        }
        gpu.ReadbackNow();return gpu.Snapshot.ToArray();
    }
    private static (float position,float velocity) Difference(GpuLiquidParticle[] a,GpuLiquidParticle[] b)
    {
        float p=0,v=0;int n=0;
        for(int i=0;i<a.Length;i++)
        {
            if(a[i].Active!=b[i].Active || a[i].VesselId!=b[i].VesselId)return(float.PositiveInfinity,float.PositiveInfinity);
            if(a[i].Active==0)continue;
            p+=(a[i].Position-b[i].Position).sqrMagnitude;v+=(a[i].Velocity-b[i].Velocity).sqrMagnitude;n++;
        }
        return(Mathf.Sqrt(p/Mathf.Max(1,n)),Mathf.Sqrt(v/Mathf.Max(1,n)));
    }
    private static void Run()
    {
        bool success=false;
        try
        {
            Setup();
            GpuLiquidParticle[] a=RemoteRun(0);int restingSteps=maximumSteps;
            GpuLiquidParticle[] repeat=RemoteRun(0);
            GpuLiquidParticle[] translated=RemoteRun(1);int translatedSteps=maximumSteps;
            GpuLiquidParticle[] rotated=RemoteRun(2);int rotatedSteps=maximumSteps;
            var noise=Difference(a,repeat);var translation=Difference(a,translated);var rotation=Difference(a,rotated);
            report.Add($"A/A repeat RMS position={noise.position:R}, velocity={noise.velocity:R}");
            report.Add($"Remote drag RMS position={translation.position:R}, velocity={translation.velocity:R}; steps={translatedSteps}");
            report.Add($"Remote rotation RMS position={rotation.position:R}, velocity={rotation.velocity:R}; steps={rotatedSteps}");
            Require(translatedSteps==restingSteps && rotatedSteps==restingSteps,"Unrelated motion leaves the liquid solver step count fixed");
            Require(translation.position<=Mathf.Max(.002f,noise.position*3) && translation.velocity<=Mathf.Max(.02f,noise.velocity*3),"Remote open/sealed liquid position and velocity unaffected by fast drag");
            Require(rotation.position<=Mathf.Max(.002f,noise.position*3) && rotation.velocity<=Mathf.Max(.02f,noise.velocity*3),"Remote open/sealed liquid position and velocity unaffected by multi-turn rotation");
            // A contact must still act: sweep a real unheld solid entirely through an airborne particle.
            Reset();mover.SetHeld(false);mover.Body.bodyType=RigidbodyType2D.Kinematic;mover.Teleport(new Vector2(-2,5),0);
            RequireEmit(new Vector2(0,5),0);Tick();
            mover.Body.position=new Vector2(2,5);Tick();gpu.ReadbackNow();
            GpuLiquidParticle hit=gpu.Snapshot.First(p=>p.Active!=0);
            Require(hit.Position.x>1.8f,"Fast crossing solid pushes contacted fluid instead of tunnelling: x="+hit.Position.x);
            // Fast filled-vessel translation and arbitrary accumulated angle do not change the global schedule.
            Reset();Seed(remote,32);for(int i=0;i<30;i++)Tick();
            remote.SetHeld(true);remote.SetHeldPose(new Vector2(7,3),855);Tick();gpu.ReadbackNow();
            Require(Mathf.Abs(gpu.VolumeIn(remote.Id)-16)<.001f,"Sealed vessel retains contents during fast translation and 855-degree rotation");
            Require(gpu.Snapshot.Where(p=>p.Active!=0).All(p=>float.IsFinite(p.Position.x)&&float.IsFinite(p.Velocity.y)),"Fast-contact state remains finite");
            uint exhausted=gpu.ReadSweepExhaustions();
            Require(exhausted==0,"Fast-contact sweep finishes within its local work budget (exhaustions="+exhausted+")");
            remote.SetSealed(false);remote.Teleport(new Vector2(5,3),180);
            // Teleport is a deliberate discontinuity: seed anew before testing an inverted opening.
            gpu.ResetSimulation();Seed(remote,32);for(int i=0;i<60;i++)Tick();gpu.ReadbackNow();
            Require(gpu.VolumeIn(remote.Id)<8,"Open inverted vessel still spills");
            // A configuration change may alter resolution, but the damping rate is measured in time.
            Vector2 savedGravity=Physics2D.gravity;
            Physics2D.gravity=Vector2.zero;
            float[] speed=new float[2];
            try
            {
                for(int run=0;run<2;run++)
                {
                    Reset();gpu.settings.gpuLiquidSubsteps=run==0?1:4;
                    if(!gpu.TryEmit(new Vector2(0,8),new Vector2(3,0),ingredient,.5f,0))throw new Exception("Damping fixture emission rejected");
                    for(int frame=0;frame<20;frame++)Tick();
                    gpu.ReadbackNow();speed[run]=gpu.Snapshot.First(p=>p.Active!=0).Velocity.x;
                }
            }
            finally{Physics2D.gravity=savedGravity;gpu.settings.gpuLiquidSubsteps=2;}
            report.Add($"Damping after 0.4 simulated seconds: 1 substep={speed[0]:R}, 4 substeps={speed[1]:R}");
            Require(Mathf.Abs(speed[0]-speed[1])<.001f,"Free-particle damping rate is independent of configured substep count");
            success=true;report.Add("All isolated GPU regression checks passed.");
        }
        catch(Exception ex){report.Add(ex.ToString());}
        finally
        {
            if(gpu!=null)gpu.Dispose();
            Directory.CreateDirectory(Evidence);File.WriteAllText(Path.Combine(Evidence,"validation.txt"),string.Join("\n",report));
            Debug.Log("[PhysicsLabIsolation] "+(success?"PASS":"FAIL")+"\n"+string.Join("\n",report));
            EditorApplication.Exit(success?0:1);
        }
    }
}
