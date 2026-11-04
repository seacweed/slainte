using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

public static class HeldLiquidOnlyValidation
{
    const string Key="FluidExperiment.HeldLiquidOnly";
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.playModeStateChanged+=state=>
    {if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Key,false)){SessionState.EraseBool(Key);new GameObject("HeldLiquidOnly").AddComponent<HeldLiquidOnlyRunner>();}};
    public static void Begin(){EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;}
}

// Negative control for the remaining fluid-retention proxy: identical native
// hand motion without any ice or garnish, preserving normal asynchronous reads.
public sealed class HeldLiquidOnlyRunner:MonoBehaviour
{
    readonly List<string> report=new List<string>(),errors=new List<string>();bool failed;
    string Evidence=>Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool ok,string text){report.Add((ok?"PASS ":"FAIL ")+text);failed|=!ok;}
    void Log(string m,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(m);}
    IEnumerator Start()
    {
        Application.logMessageReceived+=Log;var wait=new WaitForFixedUpdate();
        for(int i=0;i<5;i++)yield return null;
        var comparison=FindFirstObjectByType<FluidExperimentComparison>();while(!comparison.Ready)yield return null;
        var world=comparison.World;var gpu=comparison.Gpu;var hand=world.interactor;
        comparison.automaticScenario=false;comparison.showControls=false;hand.enabled=false;
        var glass=world.Items.First(b=>b.kind==LabItemKind.Glass&&b.name.Contains("highball"));
        var ingredient=world.Items.First(b=>b.ingredient!=null).ingredient;
        for(int repeat=0;repeat<2;repeat++)
        {
            comparison.SwitchMode(FluidExperimentMode.FCoherentLiquid);hand.enabled=false;gpu.iceLiquidMode=FluidExperimentIceLiquidMode.NonColliding;
            foreach(var body in world.Items.ToArray())
            {body.SetHeld(false);body.Teleport(new Vector2(-60-body.Id*3,20),0);body.Body.simulated=false;body.pourMlPerSecond=0;body.transform.SetPositionAndRotation(body.Position,Quaternion.Euler(0,0,body.Angle));}
            gpu.ResetSimulation();glass.Body.simulated=true;glass.Body.bodyType=RigidbodyType2D.Kinematic;glass.Teleport(new Vector2(0,1.5f),0);glass.SetSealed(false);
            glass.transform.SetPositionAndRotation(glass.Position,Quaternion.identity);Physics2D.SyncTransforms();glass.SynchronizeHistory();
            Check(!world.Items.Any(b=>b.IsLooseSolid&&b.Body.simulated),"Negative control contains no active ice or garnish");
            Check(gpu.Fill(glass,ingredient,60)==60,"Negative control queues exactly 60 ml");
            // Match the elapsed pre-motion period of the eight-cube case.
            for(int i=0;i<1120;i++)yield return wait;
            Check(hand.Pick(glass,glass.Position),"Negative control picks actual receiver");var origin=glass.Position;
            for(int i=0;i<150;i++)
            {float phase=i*Mathf.PI*2/100;glass.SetHeldPose(origin+Vector2.right*(.16f*Mathf.Sin(phase)),8*Mathf.Sin(phase));yield return wait;}
            glass.SetHeldPose(origin,0);for(int i=0;i<500;i++)yield return wait;
            gpu.ReadbackNow();var before=(GpuLiquidParticle[])gpu.Snapshot.Clone();
            Check(hand.Drop(glass.Position),"Negative control uses actual click-drop");gpu.ReadbackNow();
            Check(Enumerable.Range(0,before.Length).Where(i=>before[i].Active!=0&&before[i].VesselId==glass.Id).All(i=>before[i].Velocity==gpu.Snapshot[i].Velocity),"Negative control preserves liquid velocity on release");
            Check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl)<.001,"Negative control conserves total liquid");
            report.Add($"MEASURE repeat={repeat} containedMl={gpu.VolumeIn(glass.Id):R} activeMl={gpu.SnapshotTotalMl:R} retiredMl={60-gpu.SnapshotTotalMl:R}");
        }
        Check(errors.Count==0,"No Unity application errors");Application.logMessageReceived-=Log;report.AddRange(errors);
        File.WriteAllLines(Path.Combine(Evidence,"held-liquid-only-validation.txt"),report);
        EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(failed?1:0);
    }
}
