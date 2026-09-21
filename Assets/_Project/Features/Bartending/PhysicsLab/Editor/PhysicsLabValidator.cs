using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.Bartending.PhysicsLab.Editor
{
    public static class PhysicsLabValidator
    {
        private const string Key = "Slainte.PhysicsLab.Validation";
        [MenuItem("Slainte/Physics Lab/Validate Play Mode")]
        public static void Begin()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ValidateAssets();
            SessionState.SetBool(Key,true);
            EditorSceneManager.OpenScene(PhysicsLabBuilder.ScenePath);
            EditorApplication.isPlaying=true;
        }
        public static void BuildAndValidate()
        {
            PhysicsLabBuilder.Build();
            Begin();
        }
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            EditorApplication.playModeStateChanged -= StateChanged;
            EditorApplication.playModeStateChanged += StateChanged;
        }
        private static void StateChanged(PlayModeStateChange change)
        {
            if(change!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key,false))return;
            var runner=new GameObject("PhysicsLabValidationRunner").AddComponent<PhysicsLabValidationRunner>();
            runner.StartValidation();
        }
        public static void Finish(bool success,string report)
        {
            Directory.CreateDirectory(EvidenceDirectory);
            File.WriteAllText(Path.Combine(EvidenceDirectory,"validation.txt"),report);
            SessionState.EraseBool(Key);
            if(success)Debug.Log("[PhysicsLab] PASS\n"+report);
            else Debug.LogError("[PhysicsLab] FAIL\n"+report);
            EditorApplication.isPlaying=false;
            if(Application.isBatchMode)EditorApplication.delayCall+=()=>EditorApplication.Exit(success?0:1);
        }
        public static string EvidenceDirectory => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR") ?? "PhysicsLabEvidence/manual-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");
        private static void ValidateAssets()
        {
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(PhysicsLabBuilder.ScenePath)==null)throw new Exception("PhysicsLab scene is missing.");
            int count=0;
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{PhysicsLabBuilder.Root+"/Prefabs"}))
            {
                GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                PhysicsLabBody body=prefab.GetComponent<PhysicsLabBody>();
                if(body==null || body.solidColliders.Length==0)throw new Exception("Body/colliders missing: "+prefab.name);
                foreach(MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                    if(behaviour==null || behaviour.GetType().Namespace!="Slainte.Bartending.PhysicsLab")throw new Exception("Legacy or missing behaviour: "+prefab.name);
                count++;
            }
            if(count<10)throw new Exception("Incomplete prefab library: "+count);
        }
    }

    public sealed partial class PhysicsLabValidationRunner:MonoBehaviour
    {
        private readonly List<string> results=new List<string>();
        private readonly List<float> frameTimes=new List<float>();
        private PhysicsLabWorld world;
        private PhysicsLabGpuLiquid gpu;
        private PhysicsLabInteractor input;
        private bool finished;
        private bool measuring;
        private float startTime;
        public void StartValidation(){startTime=Time.realtimeSinceStartup;StartCoroutine(Guard());}
        private void Update()
        {
            if(measuring)frameTimes.Add(Time.unscaledDeltaTime*1000);
            if(!finished && Time.realtimeSinceStartup-startTime>240)Finish(false,"Validation exceeded 240 seconds.");
        }
        private IEnumerator Guard()
        {
            var runs=new Stack<IEnumerator>();runs.Push(Run());
            while(runs.Count>0)
            {
                bool more;
                object current=null;
                try{more=runs.Peek().MoveNext();if(more)current=runs.Peek().Current;}
                catch(Exception ex)
                {
                    while(runs.Count>0)(runs.Pop() as IDisposable)?.Dispose();
                    Finish(false,ex.ToString());yield break;
                }
                if(!more){(runs.Pop() as IDisposable)?.Dispose();continue;}
                if(current is IEnumerator nested){runs.Push(nested);continue;}
                yield return current;
            }
            Finish(true,"All runtime checks passed.");
        }
        private IEnumerator Frames(int count)
        {
            for(int i=0;i<count;i++)yield return new WaitForFixedUpdate();
        }
        private IEnumerator CompleteUprightReturn()
        {
            int frames=0;
            while(input.Returning && frames++<100)
            {
                input.AdvanceUprightReturn(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            if(input.Returning)throw new Exception("Upright return did not finish.");
        }
        private IEnumerator Run()
        {
            yield return null;
            world=FindFirstObjectByType<PhysicsLabWorld>();Require(world!=null,"Isolated world exists");
            gpu=world.Liquid;input=world.interactor;input.enabled=false;
            gpu.automaticReadback=false;
            Require(gpu.IsOperational,"GPU compute active: "+SystemInfo.graphicsDeviceName+" / "+SystemInfo.graphicsDeviceType);
            Require(GpuLiquidSystem.Instance==null && LiquidPool.Instance==null,"Legacy GPU and liquid pool are inactive");
            Require(FindObjectsByType<SlotController>(FindObjectsSortMode.None).Length==0,"Scene has zero slots");
            yield return Frames(80);
            Require(world.Items.All(x=>x.gameObject.layer==world.itemLayer),"All bodies share one layer");
            Require(world.Items.All(x=>x.Body.bodyType==RigidbodyType2D.Dynamic),"All resting bodies are Dynamic");
            Require(world.Items.All(x=>x.SolidBounds.min.y> -3.6f),"Every item rests above the physical floor");
            foreach(PhysicsLabBody item in world.Items)
            {
                Vector2 originalPosition=item.Position;float originalAngle=item.Angle;
                item.Teleport(originalPosition,73);
                Vector2 pointer=originalPosition+new Vector2(.17f,.11f);
                Require(input.Pick(item,pointer) && Mathf.Abs(item.Angle)<.001f && Mathf.Abs(item.HeldAngle)<.001f
                    && Vector2.Distance(item.Position,originalPosition)<.001f,"Pickup resets angle to zero without moving: "+item.name);
                input.MoveHeld(pointer);
                Require(Vector2.Distance(item.TargetPosition,originalPosition)<.001f,"Pickup preserves the pointer grab offset: "+item.name);
                input.ReleaseWithVelocity(Vector2.zero);
                item.Teleport(originalPosition,originalAngle);
            }
            yield return null;
            CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"initial-sandbox.png"));
            Require(gpu.SurfaceRenderingReady && gpu.SurfaceRenderingError==null,"Metaball surface uses the existing accumulation and composite shaders");
            gpu.useSurfaceRendering=false;
            CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"initial-particles.png"));
            gpu.useSurfaceRendering=true;
            ValidateSurfaceRendering();
            ValidatePouring();
            ValidateCollisionProfiles();
            PhysicsLabBody bottle=world.Items.First(x=>x.kind==LabItemKind.Bottle);
            PhysicsLabBody glass=world.Items.First(x=>x.kind==LabItemKind.Glass);
            PhysicsLabBody shaker=world.Items.First(x=>x.kind==LabItemKind.Shaker);
            ItemDef ingredient=bottle.ingredient;
            Require(input.Pick(bottle,bottle.Position),"Bottle pickup");
            Require(Physics2D.GetIgnoreCollision(bottle.solidColliders[0],glass.solidColliders[0]),"Held body collision suppressed");
            bottle.Teleport(new Vector2(-8,4),23);
            input.EndRotation(bottle.Position);
            // A new pickup resets the physically tilted pose before rotation begins.
            input.ReleaseWithVelocity(Vector2.zero);
            Vector2 gripLocal=new Vector2(.25f,.35f);
            input.Pick(bottle,bottle.Position+gripLocal);
            Require(Mathf.Abs(bottle.Angle)<.001f,"Tilted bottle pickup is upright immediately");
            float before=bottle.Angle;
            input.BeginRotation();input.RotateBy(720+135);
            yield return Frames(2);
            Require(Mathf.Abs(bottle.HeldAngle-before-855)<.1f,"Unlimited 855 degree accumulated rotation");
            Require(gpu.LastSubsteps==Mathf.Clamp(gpu.settings.gpuLiquidSubsteps,1,16),"Fast rotation leaves the fluid integration schedule fixed");
            Vector2 pose=bottle.Position;
            input.SendMessage("Sample",Time.unscaledTime);
            input.EndRotation(pose);input.MoveHeld(pose);
            Vector2 firstGrip=input.ReturnGrabPoint;
            input.AdvanceUprightReturn(input.uprightReturnDuration*.5f);
            Require(input.Returning && Mathf.Abs(bottle.Angle)>.1f && Mathf.Abs(bottle.Angle)<134.9f
                && Vector2.Distance(pose,bottle.Position)<.002f,"Upright return interpolates quickly without translating the body");
            Require(Vector2.Distance(input.ReturnGrabPoint,bottle.LocalToWorld(gripLocal))<.001f
                && Vector2.Distance(firstGrip,input.ReturnGrabPoint)>.01f,"Return cursor target follows the rotating grab point");
            input.EstimateRelease(out Vector2 resetVelocity,out float resetSpin);
            Require(resetVelocity==Vector2.zero && resetSpin==0,"Automatic upright return does not generate throw velocity or spin");
            Require(Mathf.Abs(bottle.PreviousAngle)<=180,"Automatic return does not unwind completed turns in GPU motion history");
            yield return CompleteUprightReturn();
            input.MoveHeld(pose);
            yield return Frames(3);
            Require(Vector2.Distance(pose,bottle.Position)<.002f && Mathf.Abs(bottle.Angle)<.05f && Mathf.Abs(bottle.HeldAngle)<.05f,
                "RMB release resets to zero at the current position without a pointer jump");
            input.BeginRotation();input.RotateBy(-495);yield return Frames(2);
            pose=bottle.Position;
            input.EndRotation(pose);yield return CompleteUprightReturn();input.MoveHeld(pose);yield return Frames(2);
            Require(Vector2.Distance(pose,bottle.Position)<.002f && Mathf.Abs(bottle.Angle)<.05f,
                "Repeated negative rotation also returns upright at the current position");
            input.BeginRotation();input.RotateBy(90);yield return Frames(2);input.EndRotation(bottle.Position);
            input.AdvanceUprightReturn(input.uprightReturnDuration*.5f);
            float interruptedAngle=bottle.Angle;
            input.BeginRotation();input.RotateBy(10);yield return Frames(2);
            Require(!input.Returning && input.Rotating && Mathf.Abs(bottle.HeldAngle-interruptedAngle-10)<.05f,
                "New RMB rotation interrupts upright return from its current angle");
            input.EndRotation(bottle.Position);yield return CompleteUprightReturn();
            // One complete turn is upright for pouring, regardless of accumulated angle.
            bottle.Teleport(new Vector2(-8,4),720);
            float capacity=bottle.remainingMl;
            yield return Frames(8);
            Require(Mathf.Abs(capacity-bottle.remainingMl)<.001f,"720 degrees does not pour when upright");
            bottle.Teleport(new Vector2(-8,4),135);
            input.ReleaseWithVelocity(Vector2.zero);
            yield return Frames(10);
            Require(bottle.remainingMl<capacity,"Unheld physically tilted bottle pours");
            Require(!Physics2D.GetIgnoreCollision(bottle.solidColliders[0],glass.solidColliders[0]),"Dropped body collision restored");
            bottle.Teleport(new Vector2(-8,3),35);input.Pick(bottle,bottle.Position);
            float throwStartAngle=bottle.Angle;
            input.ReleaseWithVelocity(new Vector2(5,4),120);
            yield return Frames(5);
            Require(bottle.Position.x> -7.8f && Mathf.Abs(Mathf.DeltaAngle(throwStartAngle,bottle.Angle))>3,"Throw transfers linear and angular velocity");
            // Keep emitters upright and out of subsequent volume tests.
            foreach(PhysicsLabBody item in world.Items.Where(x=>x.kind==LabItemKind.Bottle))
            {item.Teleport(new Vector2(-18+item.Id,1),0);item.pourMlPerSecond=0;}
            yield return Frames(5);
            Require(gpu.ReadSweepExhaustions()==0,"Pickup/rotation/throw contacts stay within the local sweep budget");
            gpu.ResetSimulation();
            shaker.SetSealed(true);shaker.Teleport(new Vector2(5,1),0);
            float filled=gpu.Fill(shaker,ingredient,40);
            Require(filled>10,"Filled sealed shaker on GPU");
            yield return Frames(8);gpu.ReadbackNow();
            Require(Mathf.Abs(gpu.SnapshotTotalMl-filled)<.001f && Mathf.Abs(gpu.VolumeIn(shaker.Id)-filled)<.001f,"GPU volume and ownership after fill");
            input.Pick(shaker,shaker.Position);input.BeginRotation();
            for(int i=0;i<36;i++){input.RotateBy(20);yield return new WaitForFixedUpdate();}
            input.EndRotation(shaker.Position);yield return CompleteUprightReturn();gpu.ReadbackNow();
            Require(Mathf.Abs(gpu.VolumeIn(shaker.Id)-filled)<.001f,"Sealed vessel retains liquid through two full rotations");
            input.BeginRotation();input.RotateBy(135);yield return Frames(2);
            Vector2 returnPosition=shaker.Position;
            input.EndRotation(returnPosition);yield return CompleteUprightReturn();yield return Frames(3);gpu.ReadbackNow();
            Require(Vector2.Distance(shaker.Position,returnPosition)<.002f && Mathf.Abs(shaker.Angle)<.05f
                && Mathf.Abs(gpu.VolumeIn(shaker.Id)-filled)<.001f,"Sealed vessel keeps GPU contents through upright return in place");
            input.BeginRotation();input.RotateBy(45);yield return Frames(2);
            input.ReleaseWithVelocity(Vector2.zero);input.Pick(shaker,shaker.Position);
            yield return Frames(3);gpu.ReadbackNow();
            Require(Mathf.Abs(shaker.Angle)<.05f && Mathf.Abs(gpu.VolumeIn(shaker.Id)-filled)<.001f,
                "Picking up a tilted sealed vessel restores upright and keeps its GPU contents");
            Require(gpu.Snapshot.Where(p=>p.Active!=0).All(p=>float.IsFinite(p.Position.x)&&float.IsFinite(p.Position.y)),"GPU positions remain finite");
            input.ReleaseWithVelocity(Vector2.zero);
            // A left-click drop during rotation must retain its actual tilted throw pose.
            bottle.Teleport(new Vector2(-8,4),0);input.Pick(bottle,bottle.Position);
            input.BeginRotation();input.RotateBy(35);yield return Frames(2);
            float droppedAngle=bottle.Angle;
            Require(input.Drop(bottle.Position) && !input.Rotating && !bottle.IsHeld
                && Mathf.Abs(Mathf.DeltaAngle(droppedAngle,bottle.Angle))<.05f,"Drop during rotation preserves the release angle");
            shaker.Teleport(new Vector2(5,2),0);
            gpu.ResetSimulation();
            filled=gpu.Fill(shaker,ingredient,40);
            yield return Frames(5);
            gpu.ReadbackNow();
            // Explicit swap in clear positions, using the same API as the pointer interactor.
            glass.Teleport(new Vector2(1,2),17);
            input.Pick(glass,glass.Position);
            Vector2 origin=glass.Position;float glassAngle=glass.Angle,shakerAngle=shaker.Angle;
            glass.SetHeldPose(shaker.Position,glassAngle);yield return Frames(2);
            bool swapped=world.TrySwap(glass,shaker,origin);
            Require(swapped,"Slot-free swap succeeds with different body sizes");
            Require(Mathf.Abs(Mathf.DeltaAngle(glassAngle,glass.Angle))<.01f&&Mathf.Abs(Mathf.DeltaAngle(shakerAngle,shaker.Angle))<.01f,"Swap preserves both angles");
            input.ReleaseWithVelocity(Vector2.zero);yield return Frames(3);gpu.ReadbackNow();
            Require(Mathf.Abs(gpu.VolumeIn(shaker.Id)-filled)<.001f,"Swap preserves GPU content amount and owner");
            // Actual passive collision must produce angular motion on an upright bottle.
            PhysicsLabBody hitter=world.Items.First(x=>x.kind==LabItemKind.Jigger);
            bottle=world.Items.Where(x=>x.kind==LabItemKind.Bottle).OrderByDescending(x=>x.SolidBounds.size.y/x.SolidBounds.size.x).First();
            bottle.Teleport(new Vector2(-7,0),0);Physics2D.SyncTransforms();
            bottle.Teleport(bottle.Position+Vector2.up*(-3.24f-bottle.SolidBounds.min.y),0);
            hitter.Teleport(new Vector2(-10,1),0);Physics2D.SyncTransforms();
            Vector2 impactCenter=(Vector2)bottle.SolidBounds.center+new Vector2(-2,bottle.SolidBounds.size.y*.35f);
            hitter.Teleport(hitter.Position+impactCenter-(Vector2)hitter.SolidBounds.center,0);
            results.Add("Collision setup: bottle="+bottle.name+", bounds="+bottle.SolidBounds+"; hitter="+hitter.SolidBounds);
            hitter.Body.linearVelocity=new Vector2(12,0);
            float greatestAngle=0;
            for(int i=0;i<50;i++){yield return new WaitForFixedUpdate();greatestAngle=Mathf.Max(greatestAngle,Mathf.Abs(Mathf.DeltaAngle(0,bottle.Angle)));}
            Require(greatestAngle>5,"Tool collision tips a bottle ("+greatestAngle.ToString("F1")+" degrees)");
            Require(gpu.ReadSweepExhaustions()==0,"Filled vessel rotation, swap and impact contacts stay within the local sweep budget");
            // Open a filled shaker and invert it: particles must leave its ownership.
            gpu.ResetSimulation();shaker.Teleport(new Vector2(5,3),0);shaker.SetSealed(true);
            filled=gpu.Fill(shaker,ingredient,40);yield return Frames(3);
            input.Pick(shaker,shaker.Position);input.BeginRotation();
            for(int i=0;i<18;i++){input.RotateBy(10);yield return new WaitForFixedUpdate();}
            shaker.SetSealed(false); // Keep RMB held: releasing it now restores the upright pose.
            yield return Frames(50);gpu.ReadbackNow();
            Require(gpu.VolumeIn(shaker.Id)<filled*.8f,"Open inverted vessel spills and releases ownership");
            input.ReleaseWithVelocity(Vector2.zero);
            // Snapshot renders before the stress field is added.
            world.showControls=true;
            yield return null;
            CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"sandbox.png"));
            gpu.ResetSimulation();
            for(int i=0;i<1024;i++)gpu.TryEmit(new Vector2(-7+(i%64)*.14f,7+(i/64)*.14f),Vector2.zero,ingredient,.5f,0);
            yield return Frames(2);gpu.ReadbackNow();
            Require(gpu.ActiveCount>=1000,"GPU stress uses at least 1000 real particles");
            measuring=true;
            yield return Frames(60);
            measuring=false;gpu.ReadbackNow();
            Require(gpu.Snapshot.Where(p=>p.Active!=0).All(p=>float.IsFinite(p.Position.x)&&float.IsFinite(p.Velocity.x)),"Stress state remains finite");
            if(frameTimes.Count>0)
            {
                frameTimes.Sort();
                results.Add("Batch Play Mode frame ms: p50="+Percentile(.5f).ToString("F2")+", p95="+Percentile(.95f).ToString("F2")+", p99="+Percentile(.99f).ToString("F2")+"; samples="+frameTimes.Count);
                results.Add("Frame intervals are batch scheduling diagnostics, not GPU timings or gameplay FPS.");
            }
            // Actual nozzle-to-vessel transfer, not a direct Fill call.
            gpu.ResetSimulation();
            glass.Teleport(new Vector2(0,0),0);glass.Body.bodyType=RigidbodyType2D.Kinematic;
            input.Pick(bottle,bottle.Position);
            Vector2 nozzle=glass.LocalToWorld(glass.mouthLocal)+Vector2.up*1.2f;
            bottle.Teleport(nozzle-bottle.PointAt(bottle.mouthLocal,Vector2.zero,180),180);
            bottle.pourMlPerSecond=20;bottle.remainingMl=100;
            yield return Frames(45);gpu.ReadbackNow();
            Require(gpu.VolumeIn(glass.Id)>2,"Bottle nozzle liquid is captured by a separate glass ("+gpu.VolumeIn(glass.Id).ToString("F1")+" ml)");
            Require(Mathf.Abs(100-bottle.remainingMl-gpu.SnapshotTotalMl)<.01f,"Nozzle transfer conserves emitted ml");
            yield return null;
            CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"pour-transfer.png"));
            input.BeginRotation();float prior=bottle.HeldAngle;
            input.RotateBy(-1080);yield return Frames(1);
            Require(Mathf.Abs(bottle.HeldAngle-prior+1080)<.1f,"Negative multi-turn rotation is also unrestricted");
            input.EndRotation(bottle.Position);input.ReleaseWithVelocity(Vector2.zero);bottle.pourMlPerSecond=0;
            glass.Body.bodyType=RigidbodyType2D.Dynamic;
            PhysicsLabBody bucket=world.Items.First(x=>x.kind==LabItemKind.IceBucket);
            input.Pick(bucket,bucket.Position);bucket.Teleport(new Vector2(9,5),160);
            int iceBefore=bucket.iceStock;
            yield return Frames(30);
            Require(bucket.iceStock<iceBefore && world.Items.Any(x=>x.kind==LabItemKind.Ice && x.Body.bodyType==RigidbodyType2D.Dynamic),"Tilted ice bucket emits physical colliding ice");
            input.ReleaseWithVelocity(Vector2.zero);
            world.ResetSession();yield return Frames(80);gpu.ReadbackNow();
            Require(input.Held==null && world.Items.Count==9 && world.Items.All(x=>x.Body.bodyType==RigidbodyType2D.Dynamic),"Reset removes spawned ice and restores all nine dynamic items");
            Require(gpu.IsOperational && gpu.ActiveCount>0,"Reset recreates seeded GPU liquid");
            foreach(PhysicsLabBody vessel in world.Items.Where(x=>x.kind==LabItemKind.Glass))
                Require(gpu.VolumeIn(vessel.Id)>10,"Settled open vessel retains ownership: "+vessel.name+" / "+gpu.VolumeIn(vessel.Id).ToString("F1")+" ml");
            PhysicsLabBody filledGlass=world.Items.First(x=>x.kind==LabItemKind.Glass);
            float carriedVolume=gpu.VolumeIn(filledGlass.Id);
            input.Pick(filledGlass,filledGlass.Position);
            Vector2 carriedStart=filledGlass.Position;
            for(int i=1;i<=40;i++)
            {filledGlass.SetHeldPose(carriedStart+Vector2.right*(i*.03f),filledGlass.HeldAngle);yield return new WaitForFixedUpdate();}
            gpu.ReadbackNow();
            Require(gpu.VolumeIn(filledGlass.Id)>=carriedVolume*.95f,"Slow carried glass preserves settled liquid ownership");
            input.ReleaseWithVelocity(Vector2.zero);
            // Pending reservations must not be freed by a readback before the spawn dispatch.
            gpu.ResetSimulation();
            int accepted=0;
            for(int i=0;i<gpu.settings.gpuLiquidParticleCapacity+1;i++)
                if(gpu.TryEmit(new Vector2(0,5),Vector2.zero,ingredient,.5f,0))accepted++;
            gpu.ReadbackNow();
            Require(accepted==gpu.settings.gpuLiquidParticleCapacity && !gpu.TryEmit(Vector2.zero,Vector2.zero,ingredient,.5f,0),"GPU capacity rejects overflow and retains pending reservations across readback");
            gpu.ResetSimulation();
            world.gameObject.SetActive(false);yield return null;world.gameObject.SetActive(true);
            yield return Frames(3);
            Require(gpu.IsOperational && world.Items.Count==9 && world.Items.All(x=>x.Body.bodyType==RigidbodyType2D.Dynamic),"World re-enable rebuilds GPU resources and item registration");
            CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"surface-after-reenable.png"));
            Require(gpu.SurfaceRenderingReady,"World re-enable recreates surface textures and renderer");
            yield return CapturePourSequence();
        }

        private void ValidateSurfaceRendering()
        {
            Camera camera=gpu.outputCamera;
            Vector3 originalPosition=camera.transform.position;
            float originalSize=camera.orthographicSize,highlight=gpu.settings.surfaceHighlightStrength,stretch=gpu.settings.airborneStretch;
            Color background=camera.backgroundColor;
            var ingredient=ScriptableObject.CreateInstance<ItemDef>();
            ingredient.liquidColor=new Color(.1f,.35f,.7f,1);ingredient.inheritMixedLiquidColor=false;
            string folder=PhysicsLabValidator.EvidenceDirectory;
            try
            {
                camera.transform.position=new Vector3(0,4,-20);camera.orthographicSize=.5f;camera.backgroundColor=Color.black;
                gpu.ResetSimulation();
                Require(gpu.TryEmit(new Vector2(-.0875f,4),Vector2.zero,ingredient,.5f,0)
                    && gpu.TryEmit(new Vector2(.0875f,4),Vector2.zero,ingredient,.5f,0),"Surface comparison uses the same two GPU particles");
                gpu.Step(.0001f);gpu.ReadbackNow();
                var before=(GpuLiquidParticle[])gpu.Snapshot.Clone();
                gpu.useSurfaceRendering=false;
                Color32[] dots=CaptureCamera(Path.Combine(folder,"probe-particles.png"));
                gpu.useSurfaceRendering=true;
                Color32[] surface=CaptureCamera(Path.Combine(folder,"probe-surface.png"));
                int middle=450*1600+800;
                Require(dots[middle].b<25 && surface[middle].b>70,
                    "Metaballs connect the gap between separate particle discs ("+dots[middle].b+" -> "+surface[middle].b+")");
                gpu.settings.surfaceHighlightStrength=0;
                Color32[] flat=CaptureCamera(Path.Combine(folder,"probe-no-highlight.png"));
                int lightDifference=0;
                for(int i=0;i<surface.Length;i++)lightDifference=Mathf.Max(lightDifference,surface[i].r-flat[i].r);
                Require(lightDifference>3,"Thin contour highlight contributes visible light (max red delta "+lightDifference+")");
                gpu.settings.surfaceHighlightStrength=highlight;
                CaptureCamera(Path.Combine(folder,"probe-resized.png"),800,450);
                Require(gpu.SurfaceTextureSize==new Vector2Int(800,450),"Surface textures follow camera target resolution");
                gpu.ReadbackNow();
                Require(before.Where((p,i)=>p.Position!=gpu.Snapshot[i].Position || p.Velocity!=gpu.Snapshot[i].Velocity
                    || p.VolumeMl!=gpu.Snapshot[i].VolumeMl || p.VesselId!=gpu.Snapshot[i].VesselId).Count()==0,
                    "Rendering mode, highlight and resolution changes leave GPU physics and ml untouched");
                gpu.ResetSimulation();gpu.TryEmit(new Vector2(0,4.15f),Vector2.down*4,ingredient,.5f,0);gpu.Step(.0001f);
                gpu.settings.airborneStretch=1;
                RectInt round=LitBounds(CaptureCamera(Path.Combine(folder,"probe-round.png")),1600,900);
                gpu.settings.airborneStretch=stretch;
                RectInt stream=LitBounds(CaptureCamera(Path.Combine(folder,"probe-stretched.png")),1600,900);
                Require(stream.height>round.height*1.2f && stream.width<round.width*.9f,"Airborne particle stretches along velocity while narrowing across it");
                Require(stream.center.y>450 && Mathf.Abs(stream.center.y-round.center.y)<3,"Surface UV orientation keeps liquid at its world position");
            }
            finally
            {
                camera.transform.position=originalPosition;camera.orthographicSize=originalSize;camera.backgroundColor=background;
                gpu.settings.surfaceHighlightStrength=highlight;gpu.settings.airborneStretch=stretch;
                gpu.useSurfaceRendering=true;gpu.ResetSimulation();Destroy(ingredient);
            }
        }

        private static RectInt LitBounds(Color32[] pixels,int width,int height)
        {
            int xMin=width,yMin=height,xMax=-1,yMax=-1;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                if(pixels[y*width+x].b<40)continue;
                xMin=Mathf.Min(xMin,x);yMin=Mathf.Min(yMin,y);xMax=Mathf.Max(xMax,x);yMax=Mathf.Max(yMax,y);
            }
            return new RectInt(xMin,yMin,Mathf.Max(0,xMax-xMin+1),Mathf.Max(0,yMax-yMin+1));
        }
        private float Percentile(float percentile)=>frameTimes[Mathf.Clamp(Mathf.CeilToInt(frameTimes.Count*percentile)-1,0,frameTimes.Count-1)];
        private static Color32[] CaptureCamera(string path,int width=1600,int height=900)
        {
            Camera camera=FindFirstObjectByType<PhysicsLabWorld>().interactor.inputCamera;
            var target=new RenderTexture(width,height,24);
            RenderTexture old=camera.targetTexture,active=RenderTexture.active;
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            Color32[] pixels=image.GetPixels32();
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
            camera.targetTexture=old;RenderTexture.active=active;
            Destroy(image);target.Release();Destroy(target);
            return pixels;
        }
        private void Require(bool condition,string message)
        {
            if(!condition)throw new Exception(message);
            results.Add("PASS: "+message);
        }
        private void Finish(bool success,string detail)
        {
            if(finished)return;finished=true;
            results.Add(detail);
            PhysicsLabValidator.Finish(success,string.Join("\n",results));
        }
    }
}
