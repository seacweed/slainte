using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

public static class CrowdedIcePlayLoopValidation
{
    const string Key="FluidExperiment.CrowdedIcePlayLoop";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged+=state=>
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false)) return;
        SessionState.EraseBool(Key); new GameObject("CrowdedIcePlayLoop").AddComponent<CrowdedIcePlayLoopRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key,true); EditorApplication.isPlaying=true;
    }
}

// No manual Physics2D.Simulate, FixedUpdate, or liquid.Step calls. Controllers
// provide intent while the real world coroutine and asynchronous readback run.
public sealed class CrowdedIcePlayLoopRunner : MonoBehaviour
{
    readonly List<string> report=new List<string>(),errors=new List<string>();
    readonly List<string> timeline=new List<string>{"mode,phase,tick,id,x,y,angle,vx,vy,spin,owner,glass_x,glass_angle"};
    readonly List<FluidExperimentBody> cubes=new List<FluidExperimentBody>();
    FluidExperimentComparison comparison;
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentInteractor hand;
    FluidExperimentBody glass,prefab;
    ItemDef ingredient;
    bool failed;
    string Evidence=>Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool ok,string message) { report.Add((ok?"PASS ":"FAIL ")+message); failed|=!ok; }
    void Log(string message,string trace,LogType type)
    { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) errors.Add(message); }
    IEnumerator Start()
    {
        Application.logMessageReceived+=Log;
        IEnumerator run=Run();
        while(true)
        {
            bool more; object current=null;
            try { more=run.MoveNext(); if(more) current=run.Current; }
            catch(Exception exception) { report.Add(exception.ToString()); failed=true; break; }
            if(!more) break; yield return current;
        }
        Application.logMessageReceived-=Log; report.AddRange(errors);
        File.WriteAllLines(Path.Combine(Evidence,"crowded-playloop-validation.txt"),report);
        File.WriteAllLines(Path.Combine(Evidence,"crowded-playloop-timeline.csv"),timeline);
        File.WriteAllText(Path.Combine(Evidence,"crowded-playloop-result.txt"),failed?"FAIL":"PASS");
        EditorApplication.isPlaying=false; EditorApplication.delayCall+=()=>EditorApplication.Exit(failed?1:0);
    }
    IEnumerator Run()
    {
        for(int i=0;i<5;i++) yield return null;
        comparison=FindFirstObjectByType<FluidExperimentComparison>();
        while(!comparison.Ready) yield return null;
        world=comparison.World; gpu=comparison.Gpu; hand=world.interactor;
        comparison.automaticScenario=false; comparison.showControls=false; hand.enabled=false;
        glass=world.Items.First(b=>b.kind==LabItemKind.Glass&&b.name.Contains("highball"));
        prefab=world.Items.First(b=>b.kind==LabItemKind.IceBucket).icePrefab;
        ingredient=world.Items.First(b=>b.ingredient!=null).ingredient;
        var wait=new WaitForFixedUpdate();
        foreach(var mode in new[]{FluidExperimentIceLiquidMode.NonColliding,FluidExperimentIceLiquidMode.Colliding})
        {
            comparison.SwitchMode(mode==FluidExperimentIceLiquidMode.Colliding?
                FluidExperimentMode.GExplosiveBreakage:FluidExperimentMode.FCoherentLiquid);
            hand.enabled=false; gpu.iceLiquidMode=mode; cubes.Clear();
            foreach(var body in world.Items.ToArray())
            {
                body.SetHeld(false); body.Teleport(new Vector2(-60-body.Id*3,20),0);
                body.Body.simulated=false; body.pourMlPerSecond=0;
                body.transform.SetPositionAndRotation(body.Position,Quaternion.Euler(0,0,body.Angle));
            }
            gpu.ResetSimulation(); glass.Body.simulated=true; glass.Body.bodyType=RigidbodyType2D.Kinematic;
            glass.Teleport(new Vector2(0,1.5f),0); glass.SetSealed(false);
            glass.transform.SetPositionAndRotation(glass.Position,Quaternion.identity);
            Physics2D.SyncTransforms(); glass.SynchronizeHistory();
            Check(world.enabled&&Physics2D.simulationMode==SimulationMode2D.FixedUpdate&&gpu.automaticReadback,
                mode+" keeps automatic native physics and async GPU scheduling");
            Check(gpu.Fill(glass,ingredient,60)==60,mode+" native fixture queues 60 ml");
            for(int i=0;i<150;i++) yield return wait;
            float top=glass.LocalToWorld(new Vector2(0,glass.collisionProfile.InteriorBounds.yMax)).y;
            for(int n=0;n<12;n++)
            {
                var cube=Instantiate(prefab,new Vector2(n%2==0?-.12f:.12f,top+.19f),Quaternion.Euler(0,0,n%2==0?5:-5),world.transform);
                typeof(FluidExperimentBody).GetMethod("MarkPlaced",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(cube,null);
                cubes.Add(cube);
                for(int i=0;i<40;i++) yield return wait;
            }
            var rest=cubes.ToDictionary(c=>c.Id,c=>new List<Vector2>());
            for(int i=0;i<650;i++)
            {
                yield return wait; Record("rest",i);
                if(i>=550) foreach(var cube in cubes) rest[cube.Id].Add(cube.Position);
            }
            MeasureRest(rest,mode+" native initial rest");
            Check(cubes.All(c=>c.ContainingVesselId==glass.Id&&c.Body.bodyType==RigidbodyType2D.Dynamic),
                mode+" retains all twelve dynamic cubes in receiver");
            Check(gpu.VolumeIn(glass.Id)>=55,mode+" retains the pool after twelve ice drops");
            int revision=gpu.SnapshotRevision;
            Check(hand.Pick(glass,glass.Position),mode+" picks crowded receiver");
            Vector2 origin=glass.Position;
            var moved=cubes.ToDictionary(c=>c.Id,c=>new List<Vector2>());
            for(int i=0;i<150;i++)
            {
                float phase=i*Mathf.PI*2/100;
                glass.SetHeldPose(origin+Vector2.right*(.16f*Mathf.Sin(phase)),8*Mathf.Sin(phase));
                yield return wait; Record("moving",i);
                foreach(var cube in cubes) moved[cube.Id].Add(glass.WorldToLocal(cube.Position));
            }
            float relative=moved.Values.Max(Span);
            Check(relative>.03f&&cubes.All(c=>c.Body.bodyType==RigidbodyType2D.Dynamic),
                mode+" remains mobile after settling rather than freezing the stack");
            glass.SetHeldPose(origin,0); rest=cubes.ToDictionary(c=>c.Id,c=>new List<Vector2>());
            for(int i=0;i<500;i++)
            {
                yield return wait; Record("stopped",i);
                if(i>=400) foreach(var cube in cubes) rest[cube.Id].Add(cube.Position);
            }
            MeasureRest(rest,mode+" native stop after movement");
            Check(gpu.SnapshotRevision>revision,mode+" continues real asynchronous snapshots during motion");
            gpu.ReadbackNow(); var before=(GpuLiquidParticle[])gpu.Snapshot.Clone();
            var speeds=cubes.Select(c=>c.Body.linearVelocity).ToArray(); var spins=cubes.Select(c=>c.Body.angularVelocity).ToArray();
            Check(hand.Drop(glass.Position),mode+" actual click-drop succeeds");
            gpu.ReadbackNow(); var after=gpu.Snapshot;
            Check(Enumerable.Range(0,before.Length).Where(i=>before[i].Active!=0&&before[i].VesselId==glass.Id)
                .All(i=>before[i].Velocity==after[i].Velocity
                    &&Vector2.Distance(before[i].Position-before[i].PreviousPosition,after[i].Position-after[i].PreviousPosition)<.00001f),
                mode+" click-drop preserves every owned particle velocity and motion history");
            Check(cubes.Select((c,i)=>c.Body.linearVelocity==speeds[i]&&c.Body.angularVelocity==spins[i]).All(v=>v),
                mode+" click-drop adds no cube speed or spin");
            Check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl)<.001,mode+" native loop conserves liquid");
            Check(gpu.VolumeIn(glass.Id)>=55,mode+" modest receiver motion does not pump the pool through its walls");
            report.Add($"MEASURE native {mode} relativeTravel={relative:R} containedMl={gpu.VolumeIn(glass.Id):R}");
        }
        Check(errors.Count==0,"Native crowded loop has no application errors");
    }
    void Record(string phase,int tick)
    {
        foreach(var c in cubes) timeline.Add(FormattableString.Invariant($"{gpu.iceLiquidMode},{phase},{tick},{c.Id},{c.Position.x:R},{c.Position.y:R},{c.Angle:R},{c.Body.linearVelocity.x:R},{c.Body.linearVelocity.y:R},{c.Body.angularVelocity:R},{c.ContainingVesselId},{glass.Position.x:R},{glass.Angle:R}"));
    }
    void MeasureRest(Dictionary<uint,List<Vector2>> positions,string name)
    {
        float span=positions.Values.Max(Span);
        report.Add($"MEASURE {name} maxSpan={span:R} cubes={positions.Count}");
        Check(span<.015f,name+" every cube settles below the jitter limit");
    }
    static float Span(List<Vector2> p)=>Vector2.Distance(new Vector2(p.Min(v=>v.x),p.Min(v=>v.y)),new Vector2(p.Max(v=>v.x),p.Max(v=>v.y)));
}
