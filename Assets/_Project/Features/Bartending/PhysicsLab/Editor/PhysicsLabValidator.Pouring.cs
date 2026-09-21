using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Slainte.Bartending.PhysicsLab.Editor
{
    public sealed partial class PhysicsLabValidationRunner
    {
        private void ValidatePouring()
        {
            Vector2 gravity=Physics2D.gravity;
            float damping=gpu.settings.gpuLiquidVelocityDamping,viscosity=gpu.settings.gpuLiquidViscosity;
            var ingredient=ScriptableObject.CreateInstance<ItemDef>();
            ingredient.liquidColor=new Color(.1f,.35f,.7f,1);
            GameObject obstacle=null;
            try
            {
                int bottles=0;
                foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{PhysicsLabBuilder.Root+"/Prefabs"}))
                {
                    GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if(prefab.GetComponent<PhysicsLabBody>().kind!=LabItemKind.Bottle)continue;
                    var bottle=Instantiate(prefab,world.transform).GetComponent<PhysicsLabBody>();
                    bottle.SetHeld(true);bottle.Teleport(new Vector2(-15,8),0);
                    float upright=Vector2.SignedAngle(bottle.ExitDirectionLocal,Vector2.up);
                    float a=bottle.PourFlowAt(upright+90),b=bottle.PourFlowAt(upright+91),
                        c=bottle.PourFlowAt(upright+105),d=bottle.PourFlowAt(upright+120);
                    Vector2 nozzle=bottle.WorldToLocal(bottle.NozzleAt(1));
                    var box=(BoxCollider2D)bottle.pickCollider;
                    Vector2 closest=new Vector2(Mathf.Clamp(nozzle.x,box.offset.x-box.size.x*.5f,box.offset.x+box.size.x*.5f),
                        Mathf.Clamp(nozzle.y,box.offset.y-box.size.y*.5f,box.offset.y+box.size.y*.5f));
                    Require((nozzle-closest).magnitude>=gpu.Radius*.99f,"Nozzle starts outside the bottle hull: "+prefab.name);
                    Require(a<.00001f && b>0 && b<.02f && c>b && d>.999f,"Bottle flow rises smoothly from zero to full: "+prefab.name);
                    bottle.gameObject.SetActive(false);Destroy(bottle.gameObject);bottles++;
                }
                Require(bottles==15,"All 15 bottle profiles checked without changing shared ItemDefs");
                Physics2D.gravity=Vector2.zero;
                gpu.settings.gpuLiquidVelocityDamping=0;gpu.settings.gpuLiquidViscosity=0;
                gpu.ResetSimulation();uint stream=gpu.NewPourStream();
                gpu.TryEmitStream(new Vector2(-3,5),Vector2.right*2,ingredient,.5f,101,stream,0,.005f,.06f,out uint early);
                gpu.TryEmitStream(new Vector2(0,5),Vector2.right*2,ingredient,.5f,101,stream,0,.015f,.06f,out uint late);
                gpu.TryEmitStream(new Vector2(3,5),Vector2.right*2,ingredient,.5f,101,stream,0,.02f,.06f,out uint end);
                gpu.Step(.02f);gpu.ReadbackNow();var metadata=gpu.ReadStreamParticles();
                int Find(uint token)=>Array.FindIndex(metadata,x=>x.Token==token);
                // Physics coefficients are bound at initialization; account for the existing per-time damping.
                float halfDamping=Mathf.Pow(1-damping,.5f);
                results.Add("Birth offset positions: early="+gpu.Snapshot[Find(early)].Position.x.ToString("R")+", late="+gpu.Snapshot[Find(late)].Position.x.ToString("R"));
                Require(Mathf.Abs(gpu.Snapshot[Find(early)].Position.x+3-(.01f+.02f*halfDamping))<.00002f
                    && Mathf.Abs(gpu.Snapshot[Find(late)].Position.x-.01f)<.00002f,"Birth offsets integrate only 15 ms and 5 ms of a 20 ms tick");
                Require(Mathf.Abs(gpu.Snapshot[Find(early)].Velocity.x-2*Mathf.Pow(1-damping,1.5f))<.001f
                    && Mathf.Abs(gpu.Snapshot[Find(late)].Velocity.x-2*halfDamping)<.001f,"Newborn velocity reconstruction uses its effective integration time");
                Require(gpu.Snapshot[Find(end)].Active==1 && (gpu.Snapshot[Find(end)].Position-new Vector2(3,5)).magnitude<.00001f
                    && Mathf.Abs(gpu.SnapshotTotalMl-1.5f)<.001f,"End-of-tick birth is visible and conserved without premature motion");
                Require(gpu.LastSubsteps==2,"Emitter birth offsets preserve the global two-substep schedule");
                gpu.ResetSimulation();stream=gpu.NewPourStream();
                gpu.TryEmitStream(new Vector2(-.16f,5),Vector2.zero,ingredient,.5f,101,stream,0,0,.055f,out uint older);gpu.Step(.02f);
                gpu.TryEmitStream(new Vector2(.16f,5),Vector2.zero,ingredient,.5f,101,stream,older,0,.055f,out uint newer);
                gpu.Step(.0001f);gpu.ReadbackNow();var before=gpu.Snapshot.ToArray();
                Require(gpu.ReadStreamSegments().Count(x=>x.Active!=0 && (x.B-x.A).sqrMagnitude>.00001f)==1,"Consecutive same-source particles form exactly one connecting surface");
                Camera camera=gpu.outputCamera;Vector3 savedPosition=camera.transform.position;
                float savedSize=camera.orthographicSize;Color savedBackground=camera.backgroundColor;
                try
                {
                    camera.transform.position=new Vector3(0,5,-20);camera.orthographicSize=.5f;camera.backgroundColor=Color.black;
                    gpu.useStreamRendering=false;
                    Color32[] dots=CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"stream-before.png"));
                    gpu.useStreamRendering=true;
                    Color32[] connected=CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"stream-after.png"));
                    Require(dots[450*1600+800].b<25 && connected[450*1600+800].b>70,"Stream pass closes a visible gap the original metaballs leave empty");
                }
                finally {camera.transform.position=savedPosition;camera.orthographicSize=savedSize;camera.backgroundColor=savedBackground;gpu.useStreamRendering=true;}
                gpu.ReadbackNow();
                Require(before.Where((p,i)=>p.Position!=gpu.Snapshot[i].Position || p.Velocity!=gpu.Snapshot[i].Velocity
                    || p.VolumeMl!=gpu.Snapshot[i].VolumeMl || p.VesselId!=gpu.Snapshot[i].VesselId).Count()==0,"Stream display buffers leave physical particles and ml untouched");
                obstacle=new GameObject("StreamOcclusionFixture");obstacle.transform.SetParent(world.transform,false);
                var wall=obstacle.AddComponent<PhysicsLabBody>();wall.kind=LabItemKind.Spoon;
                wall.liquidWall=new[]{new Vector2(0,-.3f),new Vector2(0,.3f)};
                wall.SetHeld(true);wall.Teleport(new Vector2(0,5),0);gpu.Step(.0001f);
                Require(gpu.ReadStreamSegments().All(x=>x.Active==0 || (x.B-x.A).sqrMagnitude<.00001f),"Stream connection cannot bridge a solid between two non-contacting particles");
                wall.SetHeld(false);wall.Body.bodyType=RigidbodyType2D.Kinematic;
                wall.Teleport(new Vector2(.16f,5),0);gpu.Step(.0001f);metadata=gpu.ReadStreamParticles();
                Require(metadata[Array.FindIndex(metadata,x=>x.Token==newer)].Detached!=0,"Solid contact permanently detaches the struck stream particle");
                obstacle.SetActive(false);Destroy(obstacle);obstacle=null;
                gpu.ResetSimulation();
                gpu.TryEmitStream(new Vector2(-.16f,5),Vector2.zero,ingredient,.5f,101,gpu.NewPourStream(),0,0,.055f,out older);gpu.Step(.02f);
                gpu.TryEmitStream(new Vector2(.16f,5),Vector2.zero,ingredient,.5f,102,gpu.NewPourStream(),older,0,.055f,out newer);gpu.Step(.0001f);
                Require(gpu.ReadStreamSegments().All(x=>x.Active==0 || (x.B-x.A).sqrMagnitude<.00001f),"Different bottles and pour sessions cannot join");
                gpu.ResetSimulation();stream=gpu.NewPourStream();
                gpu.TryEmitStream(new Vector2(-.16f,5),Vector2.zero,ingredient,.5f,101,stream,0,0,.055f,out older);gpu.Step(.1f);
                gpu.TryEmitStream(new Vector2(.16f,5),Vector2.zero,ingredient,.5f,101,stream,older,0,.055f,out newer);gpu.Step(.0001f);
                Require(gpu.ReadStreamSegments().All(x=>x.Active==0 || (x.B-x.A).sqrMagnitude<.00001f),"Slow drips stay detached across a long emission gap");
                gpu.ResetSimulation();
                gpu.TryEmitStream(new Vector2(0,5),Vector2.zero,ingredient,.5f,101,stream,older,0,.055f,out newer);gpu.Step(.0001f);
                Require(gpu.ReadStreamSegments().All(x=>x.Active==0 || (x.B-x.A).sqrMagnitude<.00001f),"Reset and reused particle slots cannot resurrect an old connection");
                ValidateBottleEmissionSchedule();
            }
            finally
            {
                if(obstacle!=null){obstacle.SetActive(false);Destroy(obstacle);}
                Physics2D.gravity=gravity;gpu.settings.gpuLiquidVelocityDamping=damping;gpu.settings.gpuLiquidViscosity=viscosity;
                gpu.useStreamRendering=true;gpu.ResetSimulation();Destroy(ingredient);
            }
        }
        private void ValidateBottleEmissionSchedule()
        {
            var bottle=world.Items.First(x=>x.kind==LabItemKind.Bottle);
            Vector2 position=bottle.Position;float angle=bottle.Angle,stock=bottle.remainingMl,rate=bottle.pourMlPerSecond;
            bool held=bottle.IsHeld;
            try
            {
                bottle.SetHeld(true);bottle.Teleport(new Vector2(0,8),180);bottle.ResetSupply(10,0);bottle.pourMlPerSecond=100;
                gpu.ResetSimulation();bottle.SetHeldPose(new Vector2(.08f,8),180);
                world.SendMessage("FixedUpdate");world.TickLiquid(.02f);gpu.ReadbackNow();
                var births=gpu.ReadStreamParticles().Where(x=>x.SourceId==bottle.Id).OrderBy(x=>x.BirthTime).ToArray();
                Require(births.Length==4 && births.Select((x,i)=>Mathf.Abs(x.BirthTime-(i+1)*.005f)<.000002f).All(x=>x),
                    "Moving bottle distributes four births at 5 ms intervals within one fixed tick");
                Require(Mathf.Abs(bottle.remainingMl-8)<.00001f && Mathf.Abs(gpu.SnapshotTotalMl-2)<.0001f,
                    "Multiple births preserve accepted stock and GPU volume exactly");
                bottle.Teleport(new Vector2(0,8),0);bottle.ResetSupply(10,0);gpu.ResetSimulation();
                bottle.pourMlPerSecond=200; // Each short tilted interval must reach the 0.5 ml particle quantum.
                bottle.SetHeldPose(new Vector2(.08f,8),720);world.SendMessage("FixedUpdate");world.TickLiquid(.02f);
                Require(gpu.EmittedMl>0 && gpu.LastSubsteps==2,"Unwrapped multi-turn motion samples intervening pour angles without global refinement");
                bottle.Teleport(new Vector2(0,8),180);bottle.ResetSupply(10,0);bottle.pourMlPerSecond=20;gpu.ResetSimulation();
                int accepted=0;
                while(gpu.TryEmit(new Vector2(12,8),Vector2.zero,bottle.ingredient,.5f,0))accepted++;
                for(int i=0;i<8;i++)bottle.SendMessage("Emit",.02f);
                Require(accepted>0 && Mathf.Abs(bottle.remainingMl-10)<.00001f,"Rejected emission keeps bottle stock intact under GPU backpressure");
                gpu.ResetSimulation();world.TickLiquid(.02f);world.TickLiquid(.02f);
                Require(gpu.EmittedMl<=.50001f,"Clearing GPU backpressure does not release an accumulated burst");
            }
            finally
            {bottle.pourMlPerSecond=rate;bottle.ResetSupply(stock,0);bottle.Teleport(position,angle);bottle.SetHeld(held);gpu.ResetSimulation();}
        }
        private IEnumerator CapturePourSequence()
        {
            foreach(var item in world.Items.ToArray()){item.SetHeld(true);item.Teleport(new Vector2(-18,10),0);}
            var bottle=world.Items.First(x=>x.kind==LabItemKind.Bottle && x.name.Contains("1002"));
            var glass=world.Items.First(x=>x.name.Contains("highball"));
            bottle.pourMlPerSecond=20;bottle.ResetSupply(700,0);bottle.Teleport(new Vector2(0,3.5f),180);glass.Teleport(new Vector2(0,-.4f),0);
            Camera camera=gpu.outputCamera;camera.transform.position=new Vector3(0,1.6f,-20);camera.orthographicSize=4;
            gpu.ResetSimulation();
            for(int frame=0;frame<35;frame++)
            {
                yield return new WaitForFixedUpdate();
                if(frame==10 || frame==20 || frame==34)CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"pour-steady-"+frame+".png"));
            }
            gpu.ReadbackNow();
            Require(gpu.EmittedMl>5 && Mathf.Abs((700-bottle.remainingMl)-gpu.EmittedMl)<.001f,"Controlled bottle stream conserves stock and emitted ml");
            Require(gpu.ReadStreamSegments().Any(x=>x.Active!=0 && (x.B-x.A).sqrMagnitude>.00001f),"Controlled pour has a continuous GPU stream");
            gpu.useStreamRendering=false;CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"pour-metaballs.png"));
            gpu.useStreamRendering=true;CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"pour-stream.png"));
            yield return MeasureStreamSurfaceCost();
            for(int frame=0;frame<12;frame++)
            {bottle.SetHeldPose(new Vector2((frame+1)*.05f,3.5f),180-(frame+1)*2);yield return new WaitForFixedUpdate();if(frame==2 || frame==8)CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"pour-moving-"+frame+".png"));}
            bottle.SetHeldPose(new Vector2(.6f,3.5f),0);
            yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();float stopped=bottle.remainingMl;
            yield return Frames(8);
            Require(Mathf.Abs(stopped-bottle.remainingMl)<.00001f,"Upright bottle stops spawning while detached liquid continues falling");
            CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory,"pour-stopped.png"));
        }
        private IEnumerator MeasureStreamSurfaceCost()
        {
            bool enabled=world.enabled;
            world.enabled=false; // Compare exactly the same frozen fluid, camera and geometry.
            try
            {
            var recorder=Recorder.Get("PhysicsLab Stream Surface");recorder.enabled=true;
            long before=0,after=0;int beforeCount=0,afterCount=0;
            for(int mode=0;mode<2;mode++)
            {
                gpu.useStreamRendering=mode==1;
                for(int i=0;i<20;i++)
                {
                    yield return null;long ns=recorder.gpuElapsedNanoseconds;
                    if(i>4 && ns>0){if(mode==0){before+=ns;beforeCount++;}else{after+=ns;afterCount++;}}
                }
            }
            recorder.enabled=false;gpu.useStreamRendering=true;
            results.Add(beforeCount>0 && afterCount>0
                ? "GPU surface marker ms (batch, not gameplay FPS): metaballs="+(before/(double)beforeCount/1e6).ToString("F3")+", streams="+(after/(double)afterCount/1e6).ToString("F3")
                : "GPU surface marker timing unavailable in this batch editor; no GPU performance claim.");
            }
            finally {world.enabled=enabled;gpu.useStreamRendering=true;}
        }
    }
}
