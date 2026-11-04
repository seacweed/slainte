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

public static class ExperimentShardMotionValidation
{
    const string Key="FluidExperiment.ShardMotionValidation";
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.playModeStateChanged+=state=>
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false))return;
        SessionState.EraseBool(Key);new GameObject("ShardMotionValidation").AddComponent<ShardMotionRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
    }
}

public sealed class ShardMotionRunner:MonoBehaviour
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const float Dt=.01f;
    FluidExperimentComparison comparison;
    FluidExperimentWorld world;
    FluidExperimentBody glass,bottle;
    FluidExperimentBreakableGlass fracture;
    FluidExperimentBreakageProfile sourceProfile,profile;
    readonly List<string> report=new List<string>(),errors=new List<string>();
    float floor;
    string Evidence=>Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    object Invoke(object o,string method,params object[] args)=>o.GetType().GetMethod(method,Members).Invoke(o,args);
    void Check(bool ok,string label){report.Add((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label);}
    void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
    IEnumerator Start()
    {
        Application.logMessageReceived+=Log;yield return null;
        comparison=FindFirstObjectByType<FluidExperimentComparison>();while(!comparison.Ready)yield return null;
        var oldMode=Physics2D.simulationMode;var oldGravity=Physics2D.gravity;bool success=false;
        try
        {
            comparison.showControls=false;comparison.ResetSandbox();
            world=comparison.World;world.seedLiquids=false;world.enabled=false;world.interactor.enabled=false;world.Liquid.automaticReadback=false;
            Physics2D.simulationMode=SimulationMode2D.Script;Physics2D.gravity=new Vector2(0,-9.81f);
            glass=world.Items.First(b=>b.kind==LabItemKind.Glass&&b.name.ToLowerInvariant().Contains("hurricane"));
            bottle=world.Items.First(b=>b.kind==LabItemKind.Bottle);fracture=glass.GetComponent<FluidExperimentBreakableGlass>();
            sourceProfile=fracture.profile;profile=Instantiate(sourceProfile);fracture.profile=profile;
            floor=world.floorColliders.First(c=>c!=null&&c.bounds.min.x<0&&c.bounds.max.x>0).bounds.max.y;
            Check(Application.isBatchMode&&comparison.Gpu.CohesivePhysicsActive&&!world.ExplosiveGlassBreakage,
                "Native shard replay uses authored F without G explosion");
            Check(profile.separationSpeed==0&&profile.impactScatterScale==0&&Mathf.Abs(profile.impactVelocityTransfer-.8f)<.0001f,
                "Authored F disables extra radial energy and explicitly retains measured impulse transfer");
            Check(profile.shards.Length==16&&profile.minimumImpactSpeed==6&&profile.minimumImpulsePerMass==4,
                "The sixteen authored images/outlines and existing break thresholds are retained");
            BirthMotion();
            OverlapAndRearm();
            NativeScenario("drop",0,0,0);
            NativeScenario("spin",0,0,90);
            NativeScenario("bottle-right",1,12,0);
            NativeScenario("bottle-left",-1,12,0);
            Reset();Check(world.GlassShards.Count==0&&world.ShardContactPairCount==0&&world.PendingShardContactCount==0,
                "Reset removes all shards and contact lifecycle state");
            Check(errors.Count==0,"No native, Unity or shader errors");success=true;
        }
        catch(Exception ex){report.Add(ex.ToString());}
        finally
        {
            if(fracture!=null&&sourceProfile!=null)fracture.profile=sourceProfile;if(profile!=null)Destroy(profile);
            Physics2D.simulationMode=oldMode;Physics2D.gravity=oldGravity;Application.logMessageReceived-=Log;report.AddRange(errors);
            File.WriteAllLines(Path.Combine(Evidence,"shard-motion-validation.txt"),report);
            File.WriteAllText(Path.Combine(Evidence,"shard-motion-result.txt"),success?"PASS":"FAIL");
            EditorApplication.isPlaying=false;EditorApplication.delayCall+=()=>EditorApplication.Exit(success?0:1);
        }
    }
    void Reset(float gap=4)
    {
        world.ResetSession();world.enabled=false;world.interactor.enabled=false;world.Liquid.automaticReadback=false;
        foreach(var item in world.Items.ToArray())
        {item.SetHeld(false);item.Teleport(new Vector2(-60-item.Id*3,20),0);item.Body.simulated=false;item.pourMlPerSecond=0;}
        world.Liquid.ResetSimulation();
        var rb=glass.Body;rb.simulated=true;rb.bodyType=RigidbodyType2D.Dynamic;rb.gravityScale=1;rb.linearDamping=.1f;rb.angularDamping=.1f;
        rb.constraints=RigidbodyConstraints2D.None;rb.interpolation=RigidbodyInterpolation2D.None;
        glass.Teleport(new Vector2(0,floor+2),0);Physics2D.SyncTransforms();
        glass.Teleport(glass.Position+Vector2.up*(floor+gap-glass.SolidBounds.min.y),0);glass.SynchronizeHistory();
        Physics2D.SyncTransforms();world.RefreshCollisionPairs();
    }
    void Native()
    {
        world.SendMessage("FixedUpdate");
        foreach(var rb in world.GetComponentsInChildren<Rigidbody2D>())if(rb.simulated)rb.interpolation=RigidbodyInterpolation2D.None;
        Physics2D.SyncTransforms();Physics2D.Simulate(Dt);
        Invoke(world,"ProcessGlassBreakage");
    }
    void ControlledBreak()
    {
        Invoke(fracture,"CaptureBeforePhysics");Invoke(fracture,"QueueImpact",8f,glass.Body.mass*8,glass.Position);
        Check(fracture.BreakQueued,"Controlled fixture queues a valid fracture without inventing a native impact");
        Invoke(world,"ProcessGlassBreakage");
        Check(fracture.IsBroken&&world.GlassShards.Count==16,"One complete native fragment set replaces the intact glass");
    }
    float Energy()=>world.GlassShards.Where(s=>s!=null&&s.isActiveAndEnabled).Sum(s=>
        .5f*s.Body.mass*s.Body.linearVelocity.sqrMagnitude+.5f*s.Body.inertia*Mathf.Pow(s.Body.angularVelocity*Mathf.Deg2Rad,2)
        +s.Body.mass*9.81f*(s.Body.worldCenterOfMass.y-floor));
    float SystemEnergy(bool includeBottle)=>Energy()+(includeBottle?
        .5f*bottle.Body.mass*bottle.Body.linearVelocity.sqrMagnitude
        +.5f*bottle.Body.inertia*Mathf.Pow(bottle.Body.angularVelocity*Mathf.Deg2Rad,2)
        +bottle.Body.mass*9.81f*(bottle.Body.worldCenterOfMass.y-floor):0);
    void BirthMotion()
    {
        Reset();glass.Body.gravityScale=0;glass.Body.linearVelocity=new Vector2(2,.4f);glass.Body.angularVelocity=90;
        Vector2 velocity=glass.Body.linearVelocity,center=glass.Body.worldCenterOfMass;float omega=90*Mathf.Deg2Rad,mass=glass.Body.mass;
        ControlledBreak();float maxError=0,maxComOffset=0;
        foreach(var s in world.GlassShards)
        {
            Vector2 arm=s.Body.worldCenterOfMass-center;
            Vector2 expected=velocity+new Vector2(-arm.y,arm.x)*omega;
            maxError=Mathf.Max(maxError,Vector2.Distance(s.Body.linearVelocity,expected));
            maxComOffset=Mathf.Max(maxComOffset,Vector2.Distance(s.Body.worldCenterOfMass,s.Body.position));
            Check(s.Body.mass>0&&float.IsFinite(s.Body.mass)&&s.Body.inertia>0&&float.IsFinite(s.Body.inertia),
                "Shard "+s.DefinitionIndex+" has finite positive native mass and inertia");
            Check(s.SolidCollider.points.SequenceEqual(profile.shards[s.DefinitionIndex].outline),
                "Shard "+s.DefinitionIndex+" retains its authored collision outline");
        }
        float total=world.GlassShards.Sum(s=>s.Body.mass);
        report.Add($"MEASURE birth mass={mass:R} shardMass={total:R} maxPointVelocityError={maxError:R} maxComOffset={maxComOffset:R} pairs={world.ShardContactPairCount} pending={world.PendingShardContactCount}");
        Check(Mathf.Abs(total-mass)<.00001f,"Fragment masses sum to the intact source mass");
        Check(maxError<.0005f&&world.GlassShards.All(s=>Mathf.Abs(s.Body.angularVelocity-90)<.001f),
            "Every native shard COM inherits the original point velocity and spin without a radial or random kick");
    }
    void OverlapAndRearm()
    {
        Reset();glass.Body.gravityScale=0;
        var rb=bottle.Body;rb.simulated=true;rb.bodyType=RigidbodyType2D.Kinematic;rb.gravityScale=0;
        rb.linearVelocity=Vector2.zero;rb.angularVelocity=0;
        bottle.Teleport(glass.Position,0);Physics2D.SyncTransforms();world.RefreshCollisionPairs();
        ControlledBreak();
        var overlaps=world.GlassShards.Where(s=>s.SolidCollider.Distance(bottle.solidColliders[0]).isOverlapped).ToArray();
        Check(overlaps.Length>0&&world.PendingShardContactCount>0,"An actually overlapped birth records deferred contact pairs");
        Check(overlaps.All(s=>bottle.solidColliders.All(c=>!s.SolidCollider.Distance(c).isOverlapped||Physics2D.GetIgnoreCollision(s.SolidCollider,c))),
            "Initially penetrating bottle/shard pairs cannot receive a second separation impulse");
        float peak=0;
        var gravity=Physics2D.gravity;Physics2D.gravity=Vector2.zero;
        for(int i=0;i<30;i++){Native();peak=Mathf.Max(peak,world.GlassShards.Max(s=>s.Body.linearVelocity.magnitude));}
        Physics2D.gravity=gravity;
        report.Add($"MEASURE overlap stationaryPeak={peak:R} pending={world.PendingShardContactCount}");
        Check(peak<.15f,"Zero-energy overlapping birth does not explode before separation");
        bottle.Teleport(glass.Position+Vector2.right*8,0);bottle.SynchronizeHistory();Physics2D.SyncTransforms();
        world.RefreshCollisionPairs();
        Check(world.GlassShards.All(s=>bottle.solidColliders.All(c=>!Physics2D.GetIgnoreCollision(s.SolidCollider,c))),
            "Bottle contact arms once the initially overlapped pair really separates");
        bottle.SetHeld(true);
        Check(world.GlassShards.All(s=>bottle.solidColliders.All(c=>Physics2D.GetIgnoreCollision(s.SolidCollider,c))),
            "Held ordinary objects retain external shard isolation");
        bottle.SetHeld(false);Physics2D.SyncTransforms();world.RefreshCollisionPairs();
        Check(world.GlassShards.All(s=>bottle.solidColliders.All(c=>!Physics2D.GetIgnoreCollision(s.SolidCollider,c))),
            "Releasing a separated ordinary object restores shard contact");
        // An already-armed pair stays armed even when a subsequent contact overlaps.
        bottle.Teleport(world.GlassShards[0].Body.position,0);Physics2D.SyncTransforms();world.RefreshCollisionPairs();
        Check(!Physics2D.GetIgnoreCollision(world.GlassShards[0].SolidCollider,bottle.solidColliders[0]),
            "A later overlapping contact cannot silently disable an armed pair");
    }
    void NativeScenario(string label,int direction,float speed,float spin)
    {
        Reset(direction==0?3:.025f);glass.Body.angularVelocity=spin;
        if(direction!=0)
        {
            // Side impact hits a real supported glass. Both the thrown bottle
            // and the resulting fragments keep gravity and free rotation.
            var rb=bottle.Body;rb.simulated=true;rb.bodyType=RigidbodyType2D.Dynamic;rb.gravityScale=1;
            rb.linearDamping=.1f;rb.angularDamping=.1f;rb.constraints=RigidbodyConstraints2D.None;
            rb.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
            Bounds g=glass.SolidBounds;bottle.Teleport(new Vector2(0,g.center.y),0);Physics2D.SyncTransforms();Bounds b=bottle.SolidBounds;
            bottle.Teleport(bottle.Position+new Vector2(direction>0?g.min.x-.5f-b.max.x:g.max.x+.5f-b.min.x,g.center.y-b.center.y),0);
            rb.linearVelocity=Vector2.right*(direction*speed);Physics2D.SyncTransforms();world.RefreshCollisionPairs();
        }
        int tick=0;for(;tick<300&&!fracture.IsBroken;tick++)Native();
        Check(fracture.IsBroken&&world.GlassShards.Count==16,label+" native contact breaks at the unchanged thresholds");
        var shards=world.GlassShards.ToArray();float mass=shards.Sum(s=>s.Body.mass);
        Vector2 mean=shards.Aggregate(Vector2.zero,(v,s)=>v+s.Body.linearVelocity*s.Body.mass)/mass;
        float birthEnergy=SystemEnergy(direction!=0),peakEnergy=birthEnergy,lateSpeed=0,lateSpin=0;int clip=0;
        var rows=new List<string>{"seconds,energy,maxSpeed,maxSpin,pendingPairs,armedPairs"};
        for(int i=0;i<1000;i++)
        {
            Native();float energy=SystemEnergy(direction!=0),maxSpeed=shards.Max(s=>s.Body.linearVelocity.magnitude),maxSpin=shards.Max(s=>Mathf.Abs(s.Body.angularVelocity));
            peakEnergy=Mathf.Max(peakEnergy,energy);
            if(i>800){lateSpeed=Mathf.Max(lateSpeed,maxSpeed);lateSpin=Mathf.Max(lateSpin,maxSpin);}
            rows.Add(FormattableString.Invariant($"{(i+1)*Dt:R},{energy:R},{maxSpeed:R},{maxSpin:R},{world.PendingShardContactCount},{world.ShardContactPairCount-world.PendingShardContactCount}"));
            if(i<240&&i%4==0)Capture(label+"-"+(clip++).ToString("000")+".png",new Vector2(direction==0?0:direction*2,floor+2),4.5f);
        }
        File.WriteAllLines(Path.Combine(Evidence,label+"-trajectory.csv"),rows);
        int enabled=0,total=0;float overlap=0;
        for(int a=0;a<shards.Length;a++)for(int b=a+1;b<shards.Length;b++)
        {
            total++;if(!Physics2D.GetIgnoreCollision(shards[a].SolidCollider,shards[b].SolidCollider))enabled++;
            var distance=shards[a].SolidCollider.Distance(shards[b].SolidCollider);if(distance.isValid)overlap=Mathf.Max(overlap,-distance.distance);
        }
        report.Add($"MEASURE {label} breakSeconds={tick*Dt:R} mean={mean:R} birthEnergy={birthEnergy:R} peakEnergy={peakEnergy:R} lateSpeed={lateSpeed:R} lateSpin={lateSpin:R} enabledSiblingPairs={enabled}/{total} maxOverlap={overlap:R} pending={world.PendingShardContactCount}");
        if(direction!=0)Check(direction*mean.x>.5f,label+" retains measured directional momentum without a radial burst");
        Check(enabled>total*.8f,label+" restores most sibling contacts instead of permanently ghosting the set");
        Check(overlap<.015f,label+" stays within small native contact penetration tolerance instead of deep interpenetration");
        Check(peakEnergy<=birthEnergy*1.25f+.01f,label+" does not create a large post-birth energy jump against static surroundings");
        Check(lateSpeed<.2f&&lateSpin<15,label+" settles without persistent linear or angular jitter");
        Check(shards.All(s=>float.IsFinite(s.Body.position.x)&&float.IsFinite(s.Body.position.y)&&s.SolidCollider.bounds.min.y>=floor-.03f),
            label+" keeps finite bodies above the physical floor");
    }
    void Capture(string name,Vector2 center,float halfHeight)
    {
        var camera=world.interactor.inputCamera;var oldTarget=camera.targetTexture;var oldPosition=camera.transform.position;
        float oldSize=camera.orthographicSize;var oldActive=RenderTexture.active;
        var target=new RenderTexture(960,720,24);var pixels=new Texture2D(960,720,TextureFormat.RGB24,false);var hidden=new List<SpriteRenderer>();
        try
        {
            foreach(var item in world.Items)if(!item.Body.simulated)foreach(var art in item.GetComponentsInChildren<SpriteRenderer>())if(art.enabled){hidden.Add(art);art.enabled=false;}
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
