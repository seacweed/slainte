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

public static class IceFirstPourValidation
{
    const string Key="FluidExperiment.IceFirstPour";
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.playModeStateChanged+=s=>
    {if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Key,false)){SessionState.EraseBool(Key);new GameObject("IceFirstPour").AddComponent<IceFirstPourRunner>();}};
    public static void Begin(){EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;}
}

// Do not seed liquid inside existing ice with Fill: pour actual births through
// the opening, covering the common ice-first order without a plain-pool reference.
public sealed class IceFirstPourRunner:MonoBehaviour
{
    readonly List<string> report=new List<string>(),errors=new List<string>();
    readonly List<FluidExperimentBody> cubes=new List<FluidExperimentBody>();
    FluidExperimentWorld world;FluidExperimentGpuLiquid gpu;FluidExperimentBody glass,prefab;bool failed;
    string Evidence=>Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool b,string text){report.Add((b?"PASS ":"FAIL ")+text);failed|=!b;}
    void Log(string m,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(m);}
    void Step(int tick){world.SendMessage("FixedUpdate");Physics2D.SyncTransforms();Physics2D.Simulate(.02f);world.TickLiquid(.02f);if(tick%5==0)gpu.ReadbackNow();}
    IEnumerator Start()
    {
        Application.logMessageReceived+=Log;for(int i=0;i<5;i++)yield return null;
        var comparison=FindFirstObjectByType<FluidExperimentComparison>();while(!comparison.Ready)yield return null;
        var previous=Physics2D.simulationMode;
        try
        {
            world=comparison.World;gpu=comparison.Gpu;comparison.automaticScenario=false;comparison.showControls=false;
            world.enabled=false;world.interactor.enabled=false;gpu.automaticReadback=false;Physics2D.simulationMode=SimulationMode2D.Script;
            glass=world.Items.First(b=>b.kind==LabItemKind.Glass&&b.name.Contains("highball"));
            prefab=world.Items.First(b=>b.kind==LabItemKind.IceBucket).icePrefab;
            var ingredient=world.Items.First(b=>b.ingredient!=null).ingredient;
            foreach(var mode in new[]{FluidExperimentIceLiquidMode.NonColliding,FluidExperimentIceLiquidMode.Colliding})
            {
                foreach(var c in cubes){c.gameObject.SetActive(false);Destroy(c.gameObject);}cubes.Clear();
                foreach(var b in world.Items.ToArray()){b.SetHeld(false);b.Teleport(new Vector2(-60-b.Id*3,20),0);b.Body.simulated=false;b.pourMlPerSecond=0;}
                gpu.ResetSimulation();gpu.iceLiquidMode=mode;glass.Body.simulated=true;glass.Body.bodyType=RigidbodyType2D.Kinematic;
                glass.Teleport(new Vector2(0,1.5f),0);glass.SetSealed(false);
                foreach(var b in world.Items){b.transform.SetPositionAndRotation(b.Position,Quaternion.Euler(0,0,b.Angle));b.SynchronizeHistory();}
                var r=glass.collisionProfile.InteriorBounds;
                for(int n=0;n<3;n++)
                {
                    var cube=Instantiate(prefab,glass.LocalToWorld(new Vector2((n-1)*.35f,r.yMax+.25f)),Quaternion.identity,world.transform);
                    typeof(FluidExperimentBody).GetMethod("MarkPlaced",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(cube,null);cubes.Add(cube);
                    for(int i=0;i<80;i++)Step(i);
                }
                for(int i=0;i<150;i++)Step(i);
                Check(gpu.VolumeIn(glass.Id)==0&&cubes.All(c=>c.ContainingVesselId==glass.Id),mode+" dry cubes settle before any liquid exists");
                float emitted=0,peakUp=0;var positions=cubes.ToDictionary(c=>c.Id,c=>new List<Vector2>());var speeds=new List<float>();var spins=new List<float>();
                for(int tick=0;tick<1000;tick++)
                {
                    if(tick<240&&tick%2==0)
                    {
                        Vector2 lip=glass.LocalToWorld(new Vector2(r.center.x+.15f*Mathf.Sin(tick*.2f),r.yMax+.16f));
                        if(gpu.TryEmit(lip,Vector2.down*.5f,ingredient,.5f))emitted+=.5f;
                    }
                    Step(tick);
                    if(tick%5==0)peakUp=Mathf.Max(peakUp,gpu.Snapshot.Where(p=>p.Active!=0).Select(p=>p.Velocity.y).DefaultIfEmpty(0).Max());
                    if(tick>=850)foreach(var c in cubes){positions[c.Id].Add(c.Position);speeds.Add(c.Body.linearVelocity.magnitude);spins.Add(Mathf.Abs(c.Body.angularVelocity));}
                }
                float span=positions.Values.Max(p=>Vector2.Distance(new Vector2(p.Min(v=>v.x),p.Min(v=>v.y)),new Vector2(p.Max(v=>v.x),p.Max(v=>v.y))));
                speeds.Sort();spins.Sort();float speed=speeds[(int)(speeds.Count*.95f)],spin=spins[(int)(spins.Count*.95f)];
                report.Add($"MEASURE {mode} emitted={emitted:R} contained={gpu.VolumeIn(glass.Id):R} span={span:R} speed95={speed:R} spin95={spin:R} peakWaterUp={peakUp:R}");
                Check(Math.Abs(emitted-60)<.001,mode+" pours exactly 60 ml through the opening");
                Check(gpu.VolumeIn(glass.Id)>59.5&&Math.Abs(gpu.Ledger.Total.ConservationErrorMl)<.001,mode+" retains the poured pool without loss");
                Check(span<.02f&&speed<.05f&&spin<2,mode+" stops self-excited rise and spin after ice-first pouring");
                Check(cubes.All(c=>c.Body.bodyType==RigidbodyType2D.Dynamic&&c.ContainingVesselId==glass.Id),mode+" retains three independent native cubes");
            }
            Check(errors.Count==0,"No Unity application errors");
        }
        catch(Exception e){failed=true;report.Add(e.ToString());}
        finally
        {
            Physics2D.simulationMode=previous;Application.logMessageReceived-=Log;report.AddRange(errors);
            File.WriteAllLines(Path.Combine(Evidence,"ice-first-validation.txt"),report);
            EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(failed?1:0);
        }
    }
}
