using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending;
using Slainte.Bartending.FluidGpuExperiment;

public static class NoCohesionValidation
{
    const string Key = "Fluid.NoCohesionAudit";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key,false)) return;
        SessionState.EraseBool(Key); new GameObject("LiquidClumpingAudit").AddComponent<NoCohesionRunner>();
    };
    public static void BeginPairing() => Begin();
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key,true); EditorApplication.isPlaying=true;
    }
}

public sealed class NoCohesionRunner : MonoBehaviour
{
    const string Root="Assets/_Project/Features/Bartending/FluidGpuExperiment";
    readonly List<string> report=new List<string>(),errors=new List<string>();
    readonly List<Measure> measurements=new List<Measure>();
    FluidExperimentWorld world; FluidExperimentGpuLiquid gpu; Camera view; ItemDef ingredient;
    string originalSettings; FluidExperimentBody glass;
    string Evidence=>Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    [Serializable] public sealed class Measure
    {
        public string label; public float seconds,ml,width,height,meanNearest,minimumNearest,maxSpeed,meanDensity,maxCompression;
        public int particles,clusters;
    }
    [Serializable] public sealed class Results { public Measure[] measurements; }
    void Check(bool ok,string label) { report.Add((ok?"PASS ":"FAIL ")+label); if(!ok) throw new Exception(label); }
    void Log(string text,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) errors.Add(text); }
    void Configure(float tension,float shear,FluidExperimentMaterial material,bool pressure=true)
    {
        gpu.Dispose(); JsonUtility.FromJsonOverwrite(originalSettings,gpu.settings);
        gpu.settings.cohesiveSurfaceTension=tension;gpu.settings.cohesiveShearViscosity=shear;
        if(!pressure)gpu.settings.cohesiveDensityIterations=0;
        gpu.cohesiveMaterial=material;gpu.Initialize(world);gpu.automaticReadback=false;
        Check(gpu.IsOperational,"Diagnostic configuration initializes: tension="+tension+" shear="+shear+" material="+material);
    }
    Measure MeasureState(string label,float seconds)
    {
        gpu.ReadbackNow();var active=gpu.Snapshot.Select((p,i)=>(p,i)).Where(x=>x.p.Active!=0).ToArray();
        Check(active.All(x=>float.IsFinite(x.p.Position.x)&&float.IsFinite(x.p.Position.y)&&float.IsFinite(x.p.Velocity.x)&&float.IsFinite(x.p.Velocity.y)),"All active particle positions and velocities finite: "+label+" at "+seconds);
        var diagnostics=gpu.ReadCohesiveDiagnostics();int n=active.Length;var parents=Enumerable.Range(0,n).ToArray();
        int Find(int i) {while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
        float nearestSum=0,minimum=float.PositiveInfinity;float link=gpu.settings.referenceSmoothingRadius*gpu.settings.cohesiveRestDistanceRatio;
        for(int i=0;i<n;i++)
        {
            float nearest=float.PositiveInfinity;
            for(int j=0;j<n;j++) if(i!=j)
            {
                float distance=Vector2.Distance(active[i].p.Position,active[j].p.Position);nearest=Mathf.Min(nearest,distance);
                if(distance<=link*1.05f)parents[Find(i)]=Find(j);
            }
            if(n>1){nearestSum+=nearest;minimum=Mathf.Min(minimum,nearest);}
        }
        var m=new Measure{label=label,seconds=seconds,particles=n,ml=active.Sum(x=>x.p.VolumeMl),
            width=n>0?active.Max(x=>x.p.Position.x)-active.Min(x=>x.p.Position.x):0,
            height=n>0?active.Max(x=>x.p.Position.y)-active.Min(x=>x.p.Position.y):0,
            meanNearest=n>1?nearestSum/n:0,minimumNearest=n>1?minimum:0,
            maxSpeed=n>0?active.Max(x=>x.p.Velocity.magnitude):0,
            meanDensity=n>0?active.Average(x=>diagnostics[x.i].x):0,
            maxCompression=n>0?active.Max(x=>diagnostics[x.i].y):0,
            clusters=Enumerable.Range(0,n).Select(Find).Distinct().Count()};measurements.Add(m);
        report.Add("MEASURE "+JsonUtility.ToJson(m));return m;
    }
    Color[] Capture(string name,Vector2 center,float size,bool surface=true)
    {
        view.transform.position=new Vector3(center.x,center.y,-20);view.orthographicSize=size;view.aspect=1.5f;
        foreach(var body in world.Items)body.transform.SetPositionAndRotation(new Vector3(body.Position.x,body.Position.y,0),Quaternion.Euler(0,0,body.Angle));
        gpu.useSurfaceRendering=surface;gpu.ImprovedSurfaceInterpolationOverride=1;
        var rt=new RenderTexture(900,600,24);rt.Create();var previous=view.targetTexture;var active=RenderTexture.active;
        var image=new Texture2D(900,600,TextureFormat.RGBA32,false);
        try
        {
            view.targetTexture=rt;view.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,900,600),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(Evidence,name+".png"),image.EncodeToPNG());return image.GetPixels();
        }
        finally{view.targetTexture=previous;RenderTexture.active=active;rt.Release();Destroy(rt);Destroy(image);}
    }
    void Pair(float spacing,bool pressure=false)
    {
        Configure(pressure?18:0,pressure?1:0,FluidExperimentMaterial.Water,pressure);Physics2D.gravity=Vector2.zero;
        Check(gpu.TryEmit(new Vector2(-spacing*.5f,4),Vector2.zero,ingredient,.5f,0)
            &&gpu.TryEmit(new Vector2(spacing*.5f,4),Vector2.zero,ingredient,.5f,0),"Pair fixture emits two stationary nominal particles");
        gpu.Step(.0001f);if(pressure)for(int tick=0;tick<50;tick++)gpu.Step(.02f);var m=MeasureState("pair-"+spacing,pressure?1:0);
        Capture("pair-"+spacing+"-particles",new Vector2(0,4),.32f,false);
        Capture("pair-"+spacing+"-surface",new Vector2(0,4),.32f,true);
        var target=(RenderTexture)typeof(FluidExperimentGpuLiquid).GetField("surfaceComposite",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(gpu);
        var old=RenderTexture.active;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false);
        RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
        float midpointAlpha=pixels.GetPixel(target.width/2,target.height/2).a;RenderTexture.active=old;Destroy(pixels);
        float support=gpu.Radius*gpu.referenceSupportRadius;
        float midpointDensity=2*Mathf.Pow(Mathf.Max(0,1-Mathf.Pow(spacing/(2*support),2)),2);
        report.Add("PAIR spacing="+spacing+" physicalDiameter="+(gpu.Radius*2)+" surfaceSupport="+support
            +" analyticMidpointDensity="+midpointDensity+" threshold="+gpu.referenceDensityThreshold+" actualMidpointAlpha="+midpointAlpha);
        Check(Mathf.Abs(m.width-spacing)<.00001f,"Rendering pair preserves the requested physical separation");
    }
    void FreePatch(string label,float tension,float shear,FluidExperimentMaterial material)
    {
        Configure(tension,shear,material);Physics2D.gravity=Vector2.zero;
        for(int y=0;y<6;y++)for(int x=0;x<6;x++)
            if(!gpu.TryEmit(new Vector2((x-2.5f)*.15f,4+(y-2.5f)*.15f),Vector2.zero,ingredient,.5f,0))throw new Exception("Patch emission rejected");
        gpu.Step(.0001f);MeasureState(label,0);
        for(int tick=1;tick<=50;tick++){gpu.Step(.02f);if(tick==10||tick==25||tick==50)MeasureState(label,tick*.02f);}
        Capture(label+"-particles",new Vector2(0,4),.8f,false);Capture(label+"-surface",new Vector2(0,4),.8f,true);
        Check(Mathf.Abs(gpu.SnapshotTotalMl-18)<.0001f,"Free-patch diagnostic preserves 18ml: "+label);
    }
    void Vessel(string label,float tension)
    {
        Configure(tension,1,FluidExperimentMaterial.Water);Physics2D.gravity=new Vector2(0,-9.81f);
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/Glass_rock.prefab");
        glass=Instantiate(source,world.transform).GetComponent<FluidExperimentBody>();glass.Body.bodyType=RigidbodyType2D.Kinematic;glass.pourMlPerSecond=0;
        float bottom=glass.collisionProfile.solids.SelectMany(h=>h.points).Min(p=>p.y*glass.transform.lossyScale.y);
        glass.Teleport(new Vector2(0,-bottom+.002f),0);Physics2D.SyncTransforms();
        Check(Mathf.Abs(gpu.Fill(glass,ingredient,30)-30)<.0001f,"Stationary gravity vessel admits 30ml: "+label);
        gpu.Step(.0001f);MeasureState(label,0);
        for(int tick=1;tick<=100;tick++){gpu.Step(.02f);if(tick==10||tick==25||tick==50||tick==100)MeasureState(label,tick*.02f);}
        Capture(label+"-particles",new Vector2(0,1),1.15f,false);Capture(label+"-surface",new Vector2(0,1),1.15f,true);
        Check(Mathf.Abs(gpu.SnapshotTotalMl-30)<.0001f,"Stationary vessel retains 30ml: "+label);
        glass.gameObject.SetActive(false);Destroy(glass.gameObject);
    }
    IEnumerator Start()
    {
        Application.logMessageReceived+=Log;yield return null;yield return null;
        var gravity=Physics2D.gravity;var mode=Physics2D.simulationMode;bool success=false;
        try
        {
            var sandbox=FindFirstObjectByType<FluidExperimentComparison>();sandbox.ResetSandbox();sandbox.showControls=false;
            world=sandbox.World;gpu=world.Liquid;view=world.interactor.inputCamera;
            ingredient=world.Cocktails.ItemCatalog.ItemsById["item_1001"];originalSettings=JsonUtility.ToJson(gpu.settings);
            Check(gpu.settings.cohesiveSurfaceTension==0,"Current scene loads zero cohesion without test override");
            Check(gpu.settings.cohesiveDensityIterations==4 && gpu.settings.cohesiveShearViscosity==1,"Current scene retains 4 compression iterations and shear multiplier 1");
            world.enabled=false;world.interactor.enabled=false;world.Cocktails.showControls=false;
            Physics2D.simulationMode=SimulationMode2D.Script;
            foreach(var body in world.Items.ToArray())body.gameObject.SetActive(false);
            foreach(var source in world.GetComponentsInChildren<FluidExperimentGarnishSource>(true))source.gameObject.SetActive(false);
            view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=new Color(.12f,.15f,.19f,1);
            report.Add("SETTINGS "+originalSettings);
            FreePatch("water-default",18,1,FluidExperimentMaterial.Water);
            FreePatch("water-no-attraction",0,1,FluidExperimentMaterial.Water);
            Vessel("vessel-default",18);Vessel("vessel-no-attraction",0);
            var off=measurements.Last(m=>m.label=="water-no-attraction");
            var on=measurements.Last(m=>m.label=="water-default");
            Check(Mathf.Abs(off.width-.75f)<.0001f && off.maxSpeed<.0001f,"No-cohesion free patch preserves .75 width and remains stationary after 1 second");
            Check(on.width<off.width*.85f,"Original cohesion contracts the same patch by more than 15 percent");
            Check(errors.Count==0,"No Unity, shader or GPU errors in clumping diagnostics");success=true;
        }
        catch(Exception error){report.Add(error.ToString());}
        finally
        {
            Physics2D.gravity=gravity;Physics2D.simulationMode=mode;Application.logMessageReceived-=Log;report.AddRange(errors);
            File.WriteAllLines(Path.Combine(Evidence,"clumping-diagnostics.txt"),report);
            File.WriteAllText(Path.Combine(Evidence,"measurements.json"),JsonUtility.ToJson(new Results{measurements=measurements.ToArray()},true));
            File.WriteAllText(Path.Combine(Evidence,"clumping-result.txt"),success?"PASS":"FAIL");
            EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(success?0:1);
        }
    }
}
