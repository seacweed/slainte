using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

public static class MixedFloatSettleValidation
{
    const string Key="FluidExperiment.MixedFloatSettle";
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.playModeStateChanged+=state=>
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false)) return;
        SessionState.EraseBool(Key); new GameObject("MixedFloatSettle").AddComponent<MixedFloatSettleRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key,true); EditorApplication.isPlaying=true;
    }
}

// Track both members through successive windows. A rolling buoyant pair is not
// classified as high-frequency jitter merely because it has not settled at 12s.
public sealed class MixedFloatSettleRunner:MonoBehaviour
{
    readonly List<string> report=new List<string>(),errors=new List<string>();
    readonly List<string> csv=new List<string>{"source,tick,kind,x,y,angle,vx,vy,spin,wet,flow_x,flow_y"};
    bool failed;
    string Evidence=>Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool ok,string text){report.Add((ok?"PASS ":"FAIL ")+text); failed|=!ok;}
    void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
    IEnumerator Start()
    {
        Application.logMessageReceived+=Log;
        for(int i=0;i<5;i++)yield return null;
        var comparison=FindFirstObjectByType<FluidExperimentComparison>();
        while(!comparison.Ready)yield return null;
        var world=comparison.World; var gpu=comparison.Gpu; var hand=world.interactor;
        var oldSimulation=Physics2D.simulationMode; var oldGravity=Physics2D.gravity;
        var spawned=new List<FluidExperimentBody>();
        try
        {
            comparison.automaticScenario=false; comparison.showControls=false;
            world.enabled=false; hand.enabled=false; gpu.automaticReadback=false;
            Physics2D.simulationMode=SimulationMode2D.Script; Physics2D.gravity=new Vector2(0,-9.81f);
            var glass=world.Items.First(b=>b.kind==LabItemKind.Glass&&b.name.Contains("highball"));
            var prefab=world.Items.First(b=>b.kind==LabItemKind.IceBucket).icePrefab;
            foreach(var source in world.GetComponentsInChildren<FluidExperimentGarnishSource>())
            {
                hand.ReleaseWithVelocity(Vector2.zero);
                foreach(var b in spawned){b.gameObject.SetActive(false);Destroy(b.gameObject);} spawned.Clear();
                foreach(var b in world.Items.ToArray())
                {b.SetHeld(false);b.Teleport(new Vector2(-60-b.Id*3,20),0);b.Body.simulated=false;b.pourMlPerSecond=0;}
                gpu.ResetSimulation(); gpu.iceLiquidMode=FluidExperimentIceLiquidMode.NonColliding;
                glass.Body.simulated=true;glass.Body.bodyType=RigidbodyType2D.Kinematic;
                glass.Teleport(new Vector2(0,1.5f),0);glass.SetSealed(false);
                foreach(var b in world.Items){b.transform.SetPositionAndRotation(b.Position,Quaternion.Euler(0,0,b.Angle));b.SynchronizeHistory();}
                var r=glass.collisionProfile.InteriorBounds;
                var ice=Instantiate(prefab,glass.LocalToWorld(new Vector2(r.center.x,r.yMin+.27f)),Quaternion.identity,world.transform);
                var garnish=Instantiate(source.garnishPrefab,glass.LocalToWorld(new Vector2(r.center.x,r.yMin+.7f)),Quaternion.identity,world.transform);
                garnish.Teleport(garnish.Position,0);spawned.Add(ice);spawned.Add(garnish);
                Physics2D.SyncTransforms();world.RefreshIceContainment();glass.SetHeld(true);
                Check(gpu.Fill(glass,world.Items.First(b=>b.ingredient!=null).ingredient,40)==40,"Mixed pair queues 40 ml");
                var points=new List<Vector2>();var angles=new List<float>();
                for(int tick=0;tick<1600;tick++)
                {
                    world.SendMessage("FixedUpdate");Physics2D.SyncTransforms();Physics2D.Simulate(.02f);world.TickLiquid(.02f);
                    if(tick%10==0)gpu.ReadbackNow();
                    foreach(var b in spawned)
                    {
                        gpu.TrySampleGarnishFlow(b,out var flow,out float wet);
                        csv.Add(FormattableString.Invariant($"{source.name},{tick},{b.kind},{b.Position.x:R},{b.Position.y:R},{b.Angle:R},{b.Body.linearVelocity.x:R},{b.Body.linearVelocity.y:R},{b.Body.angularVelocity:R},{wet:R},{flow.x:R},{flow.y:R}"));
                    }
                    points.Add(garnish.Position);angles.Add(garnish.Angle);
                    if(tick%100==99)
                    {
                        float span=Vector2.Distance(new Vector2(points.Min(p=>p.x),points.Min(p=>p.y)),new Vector2(points.Max(p=>p.x),points.Max(p=>p.y)));
                        float turn=angles.Max()-angles.Min();
                        float jitter=Mathf.Sqrt(points.Skip(1).Take(points.Count-2).Select((p,i)=>(p-(points[i]+points[i+2])*.5f).sqrMagnitude).Average());
                        report.Add($"MEASURE {source.name} seconds={(tick+1)*.02f:R} span={span:R} angleSpan={turn:R} jitter={jitter:R} ml={gpu.VolumeIn(glass.Id):R}");
                        if(tick>=1399)Check(span<.1f&&turn<20&&jitter<.0015f,"Mixed floating pair has bounded slow roll without rapid jitter");
                        points.Clear();angles.Clear();
                    }
                }
                Check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl)<.001&&Math.Abs(gpu.VolumeIn(glass.Id)-40)<.001,"Mixed float retains all 40 ml");
            }
            Check(errors.Count==0,"No Unity application errors");
        }
        catch(Exception exception){failed=true;report.Add(exception.ToString());}
        finally
        {
            Physics2D.simulationMode=oldSimulation;Physics2D.gravity=oldGravity;Application.logMessageReceived-=Log;report.AddRange(errors);
            File.WriteAllLines(Path.Combine(Evidence,"mixed-float-validation.txt"),report);
            File.WriteAllLines(Path.Combine(Evidence,"mixed-float-timeline.csv"),csv);
            File.WriteAllText(Path.Combine(Evidence,"mixed-float-result.txt"),failed?"FAIL":"PASS");
            EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(failed?1:0);
        }
    }
}
