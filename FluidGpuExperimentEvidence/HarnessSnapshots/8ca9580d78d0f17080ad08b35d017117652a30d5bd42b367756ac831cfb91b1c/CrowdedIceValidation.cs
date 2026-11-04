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

public static class CrowdedIceValidation
{
    const string Key="FluidExperiment.CrowdedIce";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged+=state=>
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false)) return;
        SessionState.EraseBool(Key);
        var runner=new GameObject("CrowdedIceValidation").AddComponent<CrowdedIceRunner>();
        runner.LowOnly=SessionState.GetBool(Key+".Low",false); SessionState.EraseBool(Key+".Low");
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key,true); EditorApplication.isPlaying=true;
    }
    public static void BeginLow() { SessionState.SetBool(Key+".Low",true); Begin(); }
}

// Real GPU particles and native polygon contacts, with a deterministic fixed-step
// driver. Every cube is measured; a stable last-spawned cube cannot hide stack jitter.
public sealed class CrowdedIceRunner : MonoBehaviour
{
    public bool LowOnly;
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    readonly List<string> report=new List<string>(), errors=new List<string>();
    readonly List<string> timeline=new List<string>{"case,tick,id,x,y,angle,vx,vy,spin,awake,recovery,owner,wet,flow_x,flow_y"};
    readonly List<string> liquidTimeline=new List<string>{"case,tick,slot,x,y,vx,vy,top,max_ice_speed,energy,owner_ml"};
    readonly List<FluidExperimentBody> cubes=new List<FluidExperimentBody>();
    FluidExperimentComparison comparison;
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentInteractor hand;
    FluidExperimentBody glass,prefab;
    ItemDef ingredient;
    readonly Dictionary<uint,float> lastRecovery=new Dictionary<uint,float>();
    bool failed;
    string Evidence=>Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool ok,string message) { report.Add((ok?"PASS ":"FAIL ")+message); failed|=!ok; }
    void Log(string message,string trace,LogType type)
    { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) errors.Add(message); }
    IEnumerator Start()
    {
        Application.logMessageReceived+=Log;
        for(int i=0;i<5;i++) yield return null;
        comparison=FindFirstObjectByType<FluidExperimentComparison>();
        while(!comparison.Ready) yield return null;
        var oldSimulation=Physics2D.simulationMode; var oldGravity=Physics2D.gravity;
        try
        {
            world=comparison.World; gpu=comparison.Gpu; hand=world.interactor;
            comparison.automaticScenario=false; comparison.showControls=false;
            world.enabled=false; hand.enabled=false; gpu.automaticReadback=false;
            Physics2D.simulationMode=SimulationMode2D.Script; Physics2D.gravity=new Vector2(0,-9.81f);
            glass=world.Items.First(b=>b.kind==LabItemKind.Glass&&b.name.Contains("highball"));
            prefab=world.Items.First(b=>b.kind==LabItemKind.IceBucket).icePrefab;
            ingredient=world.Items.First(b=>b.ingredient!=null).ingredient;
            if(!LowOnly)
            {
            Run("noncolliding-dry-8",FluidExperimentIceLiquidMode.NonColliding,8,false,false);
            Run("noncolliding-wet-8",FluidExperimentIceLiquidMode.NonColliding,8,true,false);
            Run("noncolliding-wet-12",FluidExperimentIceLiquidMode.NonColliding,12,true,false);
            }
            Run("colliding-low-3",FluidExperimentIceLiquidMode.Colliding,3,true,true);
            if(!LowOnly)
            {
            Run("colliding-wet-8",FluidExperimentIceLiquidMode.Colliding,8,true,false);
            Run("colliding-wet-12",FluidExperimentIceLiquidMode.Colliding,12,true,false);
            }
            Check(errors.Count==0,"No Unity application errors");
        }
        catch(Exception exception) { report.Add(exception.ToString()); failed=true; }
        finally
        {
            Physics2D.simulationMode=oldSimulation; Physics2D.gravity=oldGravity;
            Application.logMessageReceived-=Log; report.AddRange(errors);
            File.WriteAllLines(Path.Combine(Evidence,"crowded-ice-validation.txt"),report);
            File.WriteAllLines(Path.Combine(Evidence,"crowded-ice-timeline.csv"),timeline);
            File.WriteAllLines(Path.Combine(Evidence,"crowded-liquid-timeline.csv"),liquidTimeline);
            File.WriteAllText(Path.Combine(Evidence,"crowded-ice-result.txt"),failed?"FAIL":"PASS");
            EditorApplication.isPlaying=false; EditorApplication.delayCall+=()=>EditorApplication.Exit(failed?1:0);
        }
    }
    void Reset(FluidExperimentIceLiquidMode mode,bool wet)
    {
        hand.ReleaseWithVelocity(Vector2.zero);
        foreach(var cube in cubes) if(cube!=null) { cube.gameObject.SetActive(false); Destroy(cube.gameObject); }
        cubes.Clear();
        foreach(var body in world.Items.ToArray())
        {
            body.SetHeld(false); body.Teleport(new Vector2(-60-body.Id*3,20),0);
            body.Body.simulated=false; body.pourMlPerSecond=0;
        }
        gpu.ResetSimulation(); gpu.iceLiquidMode=mode;
        glass.Body.simulated=true; glass.Body.bodyType=RigidbodyType2D.Kinematic;
        glass.Teleport(new Vector2(0,1.5f),0); glass.SetSealed(false);
        foreach(var body in world.Items) body.transform.SetPositionAndRotation(body.Position,Quaternion.Euler(0,0,body.Angle));
        Physics2D.SyncTransforms();
        if(wet) Check(gpu.Fill(glass,ingredient,60)==60,"Queue exactly 60 ml for "+mode);
        for(int i=0;i<150;i++) Step(i%10==0);
    }
    void Step(bool read)
    {
        world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms();
        Physics2D.Simulate(.02f);
        var completed=cubes.ToDictionary(c=>c.Id,c=>c.Position);
        world.TickLiquid(.02f);
        foreach(var cube in cubes) lastRecovery[cube.Id]=Vector2.Distance(cube.Position,completed[cube.Id]);
        if(read) gpu.ReadbackNow();
    }
    void Run(string name,FluidExperimentIceLiquidMode mode,int count,bool wet,bool low)
    {
        Reset(mode,wet);
        float waterTop=gpu.Snapshot.Where(p=>p.Active!=0).Select(p=>p.Position.y+gpu.Radius).DefaultIfEmpty(0).Max();
        float spawnY=low?waterTop+.25f:glass.LocalToWorld(new Vector2(0,glass.collisionProfile.InteriorBounds.yMax)).y+.19f;
        int lastDrop=(count-1)*40, end=lastDrop+650;
        var positions=new Dictionary<uint,List<Vector2>>();
        var angles=new Dictionary<uint,List<float>>();
        var speeds=new List<float>(); var spins=new List<float>();
        float peakWaterSpeed=0, peakWaterUp=0, peakWaterTop=waterTop, lateWaterEnergy=0,lateSurface=0,maxRecovery=0,maxCubeSpeed=0,peakContactSpeed=0;
        int lateWaterSamples=0,asleep=0,samples=0; double maxLedgerError=0;
        for(int tick=0;tick<end;tick++)
        {
            GpuLiquidParticle[] diagnosticBefore=null;
            if(low && tick<160)
            {
                diagnosticBefore=new GpuLiquidParticle[gpu.Snapshot.Length];
                ((GraphicsBuffer)typeof(FluidExperimentGpuLiquid).GetField("particleBuffer",Private).GetValue(gpu)).GetData(diagnosticBefore);
            }
            if(tick<=lastDrop && tick%40==0)
            {
                int index=tick/40;
                float spawnX=low?(index-1)*.29f:(index%2==0?-.12f:.12f);
                var cube=Instantiate(prefab,new Vector2(spawnX,spawnY),Quaternion.Euler(0,0,index%2==0?5:-5),world.transform);
                cube.Teleport(cube.Position,index%2==0?5:-5);
                if(low)
                {
                    // The real dispenser refuses births inside existing solids.
                    // Match that precondition instead of injecting overlap energy.
                    bool clear=false;
                    for(int attempt=0;attempt<30;attempt++)
                    {
                        cube.transform.SetPositionAndRotation(cube.Position,Quaternion.Euler(0,0,cube.Angle));
                        Physics2D.SyncTransforms();
                        clear=cubes.All(other=>cube.solidColliders.All(a=>other.solidColliders.All(b=>a.Distance(b).distance>=.005f)))
                            &&cube.solidColliders.All(a=>glass.solidColliders.All(b=>a.Distance(b).distance>=.005f));
                        if(clear) break;
                        cube.Teleport(cube.Position+Vector2.up*.03f,cube.Angle);
                    }
                    Check(clear,name+" incoming cube starts outside every existing solid");
                }
                typeof(FluidExperimentBody).GetMethod("MarkPlaced",Private).Invoke(cube,null);
                cubes.Add(cube); positions[cube.Id]=new List<Vector2>(); angles[cube.Id]=new List<float>();
            }
            Step(tick%10==0);
            if(diagnosticBefore!=null)
            {
                var diagnosticAfter=new GpuLiquidParticle[diagnosticBefore.Length];
                ((GraphicsBuffer)typeof(FluidExperimentGpuLiquid).GetField("particleBuffer",Private).GetValue(gpu)).GetData(diagnosticAfter);
                for(int k=0;k<diagnosticAfter.Length;k++)
                    if(diagnosticBefore[k].Active!=0&&diagnosticBefore[k].VesselId==glass.Id&&diagnosticAfter[k].Active!=0&&diagnosticAfter[k].VesselId==0)
                        report.Add($"ESCAPE tick={tick} slot={k} start={diagnosticBefore[k].Position:R} end={diagnosticAfter[k].Position:R} previous={diagnosticAfter[k].PreviousPosition:R} velocity={diagnosticAfter[k].Velocity:R} scale={glass.transform.lossyScale:R}");
            }
            peakContactSpeed=Mathf.Max(peakContactSpeed,gpu.MaximumIceContactSpeed);
            bool tail=tick>=end-200;
            foreach(var cube in cubes)
            {
                maxCubeSpeed=Mathf.Max(maxCubeSpeed,cube.Body.linearVelocity.magnitude);
                if(!float.IsFinite(cube.Position.sqrMagnitude)||!float.IsFinite(cube.Body.linearVelocity.sqrMagnitude))
                    throw new Exception("Non-finite cube state in "+name);
                if(tail)
                {
                    positions[cube.Id].Add(cube.Position); angles[cube.Id].Add(cube.Angle);
                    speeds.Add(cube.Body.linearVelocity.magnitude); spins.Add(Mathf.Abs(cube.Body.angularVelocity));
                    maxRecovery=Mathf.Max(maxRecovery,lastRecovery[cube.Id]);
                    asleep+=cube.Body.IsSleeping()?1:0; samples++;
                }
                if(tick%5==0)
                {
                    gpu.TrySampleGarnishFlow(cube,out var flow,out float submerged);
                    timeline.Add(FormattableString.Invariant($"{name},{tick},{cube.Id},{cube.Position.x:R},{cube.Position.y:R},{cube.Angle:R},{cube.Body.linearVelocity.x:R},{cube.Body.linearVelocity.y:R},{cube.Body.angularVelocity:R},{cube.Body.IsAwake()},{lastRecovery[cube.Id]:R},{cube.ContainingVesselId},{submerged:R},{flow.x:R},{flow.y:R}"));
                }
            }
            if(wet&&tick%10==0)
            {
                var liquid=gpu.Snapshot.Where(p=>p.Active!=0).ToArray();
                var fastest=liquid.OrderByDescending(p=>p.Velocity.sqrMagnitude).FirstOrDefault();
                liquidTimeline.Add(FormattableString.Invariant($"{name},{tick},0,{fastest.Position.x:R},{fastest.Position.y:R},{fastest.Velocity.x:R},{fastest.Velocity.y:R},{liquid.Select(p=>p.Position.y+gpu.Radius).DefaultIfEmpty(0).Max():R},{cubes.Select(c=>c.Body.linearVelocity.magnitude).DefaultIfEmpty(0).Max():R},{liquid.Select(p=>p.Velocity.sqrMagnitude).DefaultIfEmpty(0).Average():R},{gpu.VolumeIn(glass.Id):R}"));
                peakWaterSpeed=Mathf.Max(peakWaterSpeed,liquid.Select(p=>p.Velocity.magnitude).DefaultIfEmpty(0).Max());
                peakWaterUp=Mathf.Max(peakWaterUp,liquid.Select(p=>p.Velocity.y).DefaultIfEmpty(0).Max());
                peakWaterTop=Mathf.Max(peakWaterTop,liquid.Select(p=>p.Position.y+gpu.Radius).DefaultIfEmpty(0).Max());
                maxLedgerError=Math.Max(maxLedgerError,Math.Abs(gpu.Ledger.Total.ConservationErrorMl));
                if(tail)
                {
                    var owned=liquid.Where(p=>p.VesselId==glass.Id).ToArray();
                    lateWaterEnergy+=owned.Select(p=>p.Velocity.sqrMagnitude).DefaultIfEmpty(0).Average();
                    var height=owned.Select(p=>p.Position.y+gpu.Radius).OrderBy(v=>v).ToArray();
                    lateSurface+=height.Length==0?waterTop:height[Mathf.Min(height.Length-1,(int)(height.Length*.95f))];
                    lateWaterSamples++;
                }
            }
        }
        float span=positions.Values.Max(p=>Vector2.Distance(new Vector2(p.Min(v=>v.x),p.Min(v=>v.y)),new Vector2(p.Max(v=>v.x),p.Max(v=>v.y))));
        float angularSpan=angles.Values.Max(p=>p.Max()-p.Min());
        float p95=Percentile(speeds,.95f), spin95=Percentile(spins,.95f);
        float waterEnergy=lateWaterEnergy/Mathf.Max(1,lateWaterSamples);
        float excessHeight=peakWaterTop-lateSurface/Mathf.Max(1,lateWaterSamples);
        float jitter=positions.Values.Max(p=>Mathf.Sqrt(p.Skip(1).Take(p.Count-2)
            .Select((v,i)=>(v-(p[i]+p[i+2])*.5f).sqrMagnitude).Average()));
        report.Add($"MEASURE {name} cubes={cubes.Count} contained={cubes.Count(c=>c.ContainingVesselId==glass.Id)} span={span:R} angularSpan={angularSpan:R} speed95={p95:R} spin95={spin95:R} sleepRatio={(float)asleep/Mathf.Max(1,samples):R} recovery={maxRecovery:R} peakCubeSpeed={maxCubeSpeed:R} peakWaterSpeed={peakWaterSpeed:R} peakWaterUp={peakWaterUp:R} peakWaterRise={peakWaterTop-waterTop:R} lateWaterEnergy={waterEnergy:R} containedMl={gpu.VolumeIn(glass.Id):R} ledgerError={maxLedgerError:R}");
        report.Add($"MEASURE surface {name} settledP95={lateSurface/Mathf.Max(1,lateWaterSamples):R} excessHeight={excessHeight:R} jitterRms={jitter:R} contactSpeed={peakContactSpeed:R}");
        // A freely floating trio can drift/bob with the surface. A loaded stack
        // must settle; neither case may hide high-frequency contact jitter.
        Check(span<(low?.1f:.015f)&&p95<(low?.15f:.05f)&&spin95<8&&jitter<.0015f,
            name+" has bounded drift and no sustained contact jitter");
        if(wet)
        {
            Check(maxLedgerError<.001,name+" conserves total and retired liquid");
            // Use the authored glass resolution: residual RMS travel must be
            // less than one source pixel per native fixed tick. Do not confuse
            // ordinary finite-resolution pressure noise with a launch/jet.
            float idleSpeed=glass.collisionProfile.sourcePixelSize/.02f;
            Check(waterEnergy<idleSpeed*idleSpeed,name+" idle liquid RMS travel stays below one source pixel per fixed tick");
            if(mode==FluidExperimentIceLiquidMode.Colliding)
                Check(peakWaterUp<peakContactSpeed*1.2f+.25f,name+" launch speed stays within actual ice input motion");
            if(low)
            {
                float rim=glass.LocalToWorld(new Vector2(0,glass.collisionProfile.InteriorBounds.yMax)).y;
                // The final P95 surface changes under displacement and films. A
                // true low-drop failure is a jet/overflow or wall loss, not that
                // the peak stands above a different final compressed pool level.
                Check(peakWaterUp<3&&peakWaterTop<rim+gpu.Radius&&Mathf.Abs(gpu.VolumeIn(glass.Id)-60)<.001f,
                    name+" low-energy drops retain all liquid below the open rim without a jet");
            }
        }
        Check(cubes.All(c=>c.Body.bodyType==RigidbodyType2D.Dynamic&&c.Body.simulated),name+" keeps native dynamic bodies");
    }
    static float Percentile(List<float> values,float q)
    { values.Sort(); return values[Mathf.Min(values.Count-1,(int)(values.Count*q))]; }
}
