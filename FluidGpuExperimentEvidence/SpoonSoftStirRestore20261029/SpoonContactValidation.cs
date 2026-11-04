using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Slainte.Bartending.FluidGpuExperiment;

public static class SpoonContactValidation
{
    const string Key="Fluid.SpoonContact";
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.playModeStateChanged+=s=>
    {
        if(s!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false)) return;
        SessionState.EraseBool(Key); new GameObject("SpoonContactValidation").AddComponent<SpoonContactRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key,true); EditorApplication.isPlaying=true;
    }
}

public sealed class SpoonContactProbe:MonoBehaviour
{
    public FluidExperimentBody target;
    public int contacts;
    void OnCollisionEnter2D(Collision2D contact) { if(contact.collider.GetComponentInParent<FluidExperimentBody>()==target) contacts++; }
    void OnCollisionStay2D(Collision2D contact) { if(contact.collider.GetComponentInParent<FluidExperimentBody>()==target) contacts++; }
}

public sealed class SpoonContactRunner:MonoBehaviour
{
    const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    readonly List<string> report=new List<string>(),errors=new List<string>();
    FluidExperimentWorld world; FluidExperimentInteractor hand; FluidExperimentBody spoon,bottle,glass;
    Mouse mouse; Keyboard keyboard;
    readonly MethodInfo manualUpdate=typeof(InputSystem).GetMethod("Update",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(InputUpdateType)},null);
    string Evidence=>Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool ok,string label) { report.Add((ok?"PASS ":"FAIL ")+label); if(!ok) throw new Exception(label); }
    void Log(string message,string trace,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) errors.Add(message); }
    void Frame(Vector2 worldPoint,bool right,Vector2 delta=default,bool escape=false,bool left=false)
    {
        var screen=(Vector2)hand.inputCamera.WorldToScreenPoint(worldPoint);
        InputSystem.QueueStateEvent(mouse,new MouseState{position=screen,delta=delta}.WithButton(MouseButton.Right,right).WithButton(MouseButton.Left,left));
        InputSystem.QueueStateEvent(keyboard,escape?new KeyboardState(Key.Escape):new KeyboardState());
        manualUpdate.Invoke(null,new object[]{InputUpdateType.Manual}); mouse.MakeCurrent(); keyboard.MakeCurrent();
        typeof(FluidExperimentInteractor).GetMethod("Update",Hidden).Invoke(hand,null);
    }
    void Step()
    {
        world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms(); Physics2D.Simulate(.02f); world.TickLiquid(.02f);
    }
    bool Ignored()=>Physics2D.GetIgnoreCollision(spoon.solidColliders[0],bottle.solidColliders[0]);
    void Ghost(string label)
    {
        Check(hand.Held==spoon&&!spoon.SpoonContactEnabled&&spoon.solidColliders.All(c=>c.isTrigger)&&Ignored(),label);
        Check(!hand.Rotating&&!hand.Returning&&!hand.PointerCaptured&&!hand.PointerHandoffPending,label+" does not own rotation cursor");
    }
    void Solid(string label)
    {
        Check(hand.Held==spoon&&spoon.SpoonContactEnabled&&spoon.solidColliders.All(c=>c.isTrigger)&&Ignored(),label);
        Check(!spoon.Body.useFullKinematicContacts&&!hand.Rotating&&!hand.Returning&&!hand.PointerCaptured&&!hand.PointerHandoffPending,label+" keeps native solids ghosted without cursor capture");
    }
    void LiquidContact()
    {
        var gpu=world.Liquid; var ingredient=bottle.ingredient;
        Vector2 point=Vector2.zero; bool found=false;
        foreach(var hull in spoon.collisionProfile.solids)
            for(int i=0;i<hull.points.Length&&!found;i++)
            {
                Vector2 a=hull.points[i], b=hull.points[(i+1)%hull.points.Length];
                if((a.y+b.y)*.5f<1.2f) continue;
                Vector2 edge=spoon.LocalToWorld(b)-spoon.LocalToWorld(a);
                Vector2 normal=new Vector2(-edge.y,edge.x).normalized;
                foreach(float sign in new[]{1f,-1f})
                {
                    Vector2 candidate=spoon.LocalToWorld((a+b)*.5f)+normal*(sign*gpu.Radius*.4f);
                    if(!FluidExperimentCollisionProfile.Contains(hull.points,spoon.WorldToLocal(candidate))&&glass.ContainsLiquidDisk(glass.WorldToLocal(candidate),gpu.Radius))
                    { point=candidate; found=true; break; }
                }
            }
        Check(found,"Controlled GPU sample lies outside spoon bowl within a real particle contact radius and inside glass");
        Vector2 Sample(bool enabled,uint owner)
        {
            gpu.ResetSimulation(); spoon.SetSpoonContactEnabled(enabled); spoon.SetHeldPose(spoon.Position,0); Step();
            if(!gpu.TryEmit(point,Vector2.zero,ingredient,.5f,owner)) throw new Exception("Diagnostic particle rejected");
            gpu.Step(.001f); gpu.ReadbackNow();
            return gpu.Snapshot.First(p=>p.Active!=0).Position;
        }
        Vector2 control=Sample(false,glass.Id), contacted=Sample(true,glass.Id), free=Sample(true,0);
        report.Add("MEASURE GPU control="+control+" contacted="+contacted+" free="+free+" emission="+point);
        Check(Vector2.Distance(contacted,control)<.005f,"Stationary overlapping spoon has no solid collision with owned liquid");
        Check(Vector2.Distance(free,point)<.005f,"Unowned free liquid at same location ignores held spoon");
        Check(Vector2.Distance(control,point)<.005f,"RMB-off owned liquid at same location ignores held spoon");
        gpu.ResetSimulation();
        gpu.Fill(glass,ingredient,30); gpu.Step(.01f); gpu.ReadbackNow();
        Capture();
    }
    void Capture()
    {
        var camera=hand.inputCamera; camera.transform.position=new Vector3(0,4,-20); camera.orthographicSize=3.5f;
        var rt=new RenderTexture(700,700,24); rt.Create(); var image=new Texture2D(700,700,TextureFormat.RGBA32,false);
        var previous=camera.targetTexture; var active=RenderTexture.active;
        try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,700,700),0,0); image.Apply(); File.WriteAllBytes(Path.Combine(Evidence,"spoon-in-vessel.png"),image.EncodeToPNG()); }
        finally { camera.targetTexture=previous; RenderTexture.active=active; rt.Release(); Destroy(rt); Destroy(image); }
    }
    void OverlapActivation(Vector2 home,Vector2 tip)
    {
        Frame(home,false); Step(); Vector2 vesselPosition=glass.Position, safePosition=spoon.Position;
        Frame(home,true); Step();
        Check(spoon.SpoonLiquidVessel==glass&&!spoon.SpoonWaitingForClearance&&Vector2.Distance(spoon.Position,safePosition)<.002f,"RMB while visually overlapping a clear cavity activates without a pose jump");
        Vector2 overlap=Vector2.zero; bool found=false;
        for(float x=.15f;x<.7f;x+=.025f)
        {
            Frame(home+Vector2.right*x,false); Step();
            float gap=spoon.solidColliders.Min(c=>glass.solidColliders.Min(d=>c.Distance(d).distance));
            if(gap<-.01f&&glass.ContainsLiquid(spoon.LocalToWorld(tip))) { overlap=spoon.Position; found=true; break; }
        }
        Check(found,"Controlled already-overlapping wall fixture has tip inside cavity and real native penetration");
        Frame(overlap,true); Step();
        Check(spoon.SpoonWaitingForClearance&&spoon.SpoonLiquidVessel==null,"RMB on an existing wall overlap stays pending without liquid interaction");
        Check(Vector2.Distance(spoon.Position,overlap)<.00001f&&Vector2.Distance(glass.Position,vesselPosition)<.00001f&&glass.Body.linearVelocity.sqrMagnitude<.000001f,"Pending wall overlap neither snaps spoon nor pushes glass");
        Frame(overlap,false); Step();
        Check(!spoon.SpoonWaitingForClearance&&spoon.SpoonLiquidVessel==null&&Vector2.Distance(spoon.Position,overlap)<.00001f,"Releasing RMB while overlapped cancels pending mode without depenetration");
        Frame(overlap,true); Step(); Frame(home,true); Step();
        Check(spoon.SpoonLiquidVessel==glass&&!spoon.SpoonWaitingForClearance&&Vector2.Distance(spoon.Position,home)<.002f,"Moving inward out of wall overlap admits vessel while continuing to hold RMB");
        Frame(home,false); Step();
    }
    void OtherVessels(Vector2 restoreHome,Vector2 tip)
    {
        foreach(string name in new[]{"rock","martini","hurricane"})
        foreach(float angle in new[]{0f,20f})
        {
            spoon.SetSpoonContactEnabled(false);
            var receiver=Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Features/Bartending/FluidGpuExperiment/Prefabs/Glass_"+name+".prefab"),world.transform).GetComponent<FluidExperimentBody>();
            receiver.Teleport(new Vector2(8,5),angle); receiver.SetSealed(false); receiver.pourMlPerSecond=0;
            Vector2 home=Vector2.zero; bool admitted=false;
            var bounds=receiver.collisionProfile.InteriorBounds;
            for(float depth=.35f;depth<bounds.height-.05f&&!admitted;depth+=.1f)
            {
                Vector2 probe=receiver.LocalToWorld(new Vector2(bounds.center.x,bounds.yMax-depth));
                if(!receiver.ContainsLiquid(probe)) continue;
                home=probe-spoon.PointAt(tip,Vector2.zero,0);
                Frame(home,false); Step(); Frame(home,true); Step();
                admitted=spoon.SpoonLiquidVessel==receiver;
            }
            Check(admitted,"Actual "+name+" cavity admits safe spoon at tilt "+angle);
            var pose=(receiver.Position,receiver.Angle,receiver.Body.linearVelocity,receiver.Body.angularVelocity);
            Vector2 request=home+FluidExperimentBody.Rotate(Vector2.right*8,angle);
            Frame(request,true); Step();
            var path=receiver.collisionProfile.interior;
            Vector2 lip=receiver.LocalToWorld(path[0]), mouth=receiver.LocalToWorld(path[path.Length-1])-lip;
            Vector2 outward=new Vector2(-mouth.y,mouth.x).normalized;
            if(Vector2.Dot(receiver.LocalToWorld(path[path.Length/2])-lip,outward)>0) outward=-outward;
            bool outsideMouth=spoon.collisionProfile.solids.SelectMany(h=>h.points).All(p=>Vector2.Dot(spoon.LocalToWorld(p)-lip,outward)>.0001f);
            report.Add("MEASURE "+name+" tilt="+angle+" requested="+request+" actual="+spoon.Position+" owner="+spoon.SpoonLiquidVessel?.name+" fullyOutsideMouth="+outsideMouth);
            Check(Vector2.Distance(spoon.Position,request)>1&&(spoon.SpoonLiquidVessel==receiver||outsideMouth),"Finite wall blocks side crossing or slides spoon out through open mouth in "+name+" at tilt "+angle);
            Check(spoon.solidColliders.Min(c=>receiver.solidColliders.Min(d=>c.Distance(d).distance))>=-.003f,"Native silhouette stays outside "+name+" wall at tilt "+angle);
            Check(pose.Equals((receiver.Position,receiver.Angle,receiver.Body.linearVelocity,receiver.Body.angularVelocity)),"Spoon leaves "+name+" pose and velocities unchanged at tilt "+angle);
            spoon.SetSpoonContactEnabled(false); receiver.gameObject.SetActive(false); Destroy(receiver.gameObject);
        }
        Frame(restoreHome,false); Step(); Frame(restoreHome,true); Step();
        Check(spoon.SpoonLiquidVessel==glass,"Original glass interaction resumes after other vessel cases");
    }
    IEnumerator Start()
    {
        Application.logMessageReceived+=Log; yield return null; yield return null;
        var oldMode=Physics2D.simulationMode; var oldGravity=Physics2D.gravity;
        var oldMouse=Mouse.current; var oldKeyboard=Keyboard.current;
        var oldUpdate=InputSystem.settings.updateMode; var oldBackground=InputSystem.settings.backgroundBehavior;
        var oldEditor=InputSystem.settings.editorInputBehaviorInPlayMode; bool oldRun=Application.runInBackground;
        var oldLock=Cursor.lockState; bool oldVisible=Cursor.visible; bool success=false;
        try
        {
            var comparison=FindFirstObjectByType<FluidExperimentComparison>(); comparison.automaticScenario=false; comparison.ResetComparison(); comparison.showControls=false;
            world=comparison.World; hand=world.interactor; world.enabled=false; hand.enabled=false;
            world.Cocktails.showControls=false; world.Liquid.automaticReadback=false; world.Liquid.ResetSimulation();
            spoon=world.Items.First(x=>x.kind==LabItemKind.Spoon); glass=world.Items.First(x=>x.kind==LabItemKind.Glass&&x.name.Contains("highball")); bottle=world.Items.First(x=>x.kind==LabItemKind.Bottle);
            foreach(var item in world.Items.ToArray()) if(item!=spoon&&item!=bottle&&item!=glass) item.gameObject.SetActive(false);
            Physics2D.simulationMode=SimulationMode2D.Script; Physics2D.gravity=Vector2.zero;
            spoon.Teleport(new Vector2(3,4),0); glass.Teleport(new Vector2(0,4),0); glass.SetSealed(false); bottle.Teleport(Vector2.zero,0); Physics2D.SyncTransforms();
            bottle.Teleport(new Vector2(3.12f,2.6f)-(Vector2)bottle.SolidBounds.center,0); bottle.pourMlPerSecond=0;
            bottle.Body.constraints=RigidbodyConstraints2D.FreezeRotation; Physics2D.SyncTransforms();
            var probe=spoon.gameObject.AddComponent<SpoonContactProbe>(); probe.target=bottle;
            hand.pointerBlockRect=default; hand.inputCamera.transform.position=new Vector3(0,4,-20); hand.inputCamera.orthographicSize=6;
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground=true; mouse=InputSystem.AddDevice<Mouse>(); keyboard=InputSystem.AddDevice<Keyboard>();
            Check(hand.Pick(spoon,spoon.Position),"Flipped spoon can be picked");
            Frame(spoon.Position,false); Ghost("Held spoon without RMB remains ghosted");
            hand.BeginRotation(); Check(!hand.Rotating,"Spoon cannot enter rotation");
            Vector2 initialBottle=bottle.Position;
            Frame(spoon.Position,true); Solid("RMB outside vessel remains ghosted");
            for(int i=0;i<5;i++) Step();
            Check(spoon.SpoonLiquidVessel==null&&probe.contacts==0&&Vector2.Distance(initialBottle,bottle.Position)<.0001f,"RMB outside vessel neither contacts nor pushes a real bottle");
            Vector2 tip=spoon.collisionProfile.solids.SelectMany(h=>h.points).OrderBy(p=>p.y*spoon.transform.lossyScale.y).First();
            Vector2 center=glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
            Vector2 home=center-spoon.PointAt(tip,Vector2.zero,0), target=home;
            OverlapActivation(home,tip);
            Frame(home,true); Step();
            Check(spoon.SpoonLiquidVessel==glass,"RMB spoon tip entering actual cavity acquires that vessel");
            Check(spoon.solidColliders.All(c=>c.isTrigger)&&Physics2D.GetIgnoreCollision(spoon.solidColliders[0],glass.solidColliders[0]),"Inside vessel still has no native spoon/glass impulse pair");
            Vector2 glassPosition=glass.Position; float glassAngle=glass.Angle;
            foreach(Vector2 offset in new[]{new Vector2(10,0),new Vector2(-10,0),new Vector2(0,-10)})
            {
                Frame(home,true); Step(); Frame(home+offset,true,new Vector2(20,500)); Step();
                Check(Vector2.Distance(spoon.Position,home+offset)>1&&glass.ContainsLiquid(spoon.LocalToWorld(tip)),"Large cursor move stops spoon at finite vessel wall: "+offset);
                float separation=spoon.solidColliders.Min(c=>glass.solidColliders.Min(d=>c.Distance(d).distance));
                Check(separation>=-.003f,"Actual native spoon silhouette stays clear of vessel wall: "+separation);
                Check(Vector2.Distance(glassPosition,glass.Position)<.00001f&&Mathf.Abs(glass.Angle-glassAngle)<.00001f&&glass.Body.linearVelocity.sqrMagnitude<.000001f&&Mathf.Abs(glass.Body.angularVelocity)<.00001f,"Wall constraint never moves or accelerates free dynamic glass");
                Check(Mathf.Abs(spoon.TargetAngle)<.00001f&&!hand.Rotating&&!hand.PointerCaptured,"Large RMB mouse delta never rotates or captures spoon cursor");
            }
            Frame(home,true); Step();
            glass.Body.linearVelocity=new Vector2(.1f,0); glass.Body.angularVelocity=3;
            world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms(); Physics2D.Simulate(.02f);
            var nativePose=(glass.Position,glass.Angle,glass.Body.linearVelocity,glass.Body.angularVelocity);
            world.TickLiquid(.02f);
            Check(nativePose.Equals((glass.Position,glass.Angle,glass.Body.linearVelocity,glass.Body.angularVelocity)),"Post-physics spoon recovery preserves freely moving/rotating vessel pose and velocity exactly");
            glass.Body.linearVelocity=Vector2.zero; glass.Body.angularVelocity=0; glass.Teleport(glassPosition,glassAngle);
            Frame(home,true); Step();
            Frame(home+Vector2.up*5,true); Step();
            Check(spoon.SpoonLiquidVessel==null,"Spoon can withdraw through open mouth and releases vessel constraint");
            Frame(home,true); Step(); Check(spoon.SpoonLiquidVessel==glass,"Spoon can reenter after withdrawal");
            OtherVessels(home,tip);
            LiquidContact();
            Frame(home,false); Ghost("RMB release immediately clears vessel-only interaction");
            Check(spoon.SpoonLiquidVessel==null,"RMB release clears selected vessel");
            Frame(home+Vector2.right*3,false); Step();
            Check(Vector2.Distance(spoon.Position,home+Vector2.right*3)<.001f,"Without RMB spoon crosses former vessel wall freely");
            target=spoon.Position;
            Frame(target,true); Solid("Second RMB press can re-enable collision");
            Frame(target,true,escape:true); Ghost("Escape cancels collision");
            Frame(target,true); Ghost("Escape requires physical RMB release before rearming");
            Frame(target,false); Frame(target,true); Solid("Release and new press rearm after Escape");
            hand.SendMessage("OnApplicationFocus",false); Ghost("Focus loss cancels collision");
            Frame(target,true); Ghost("Focus resume while still pressing cannot rearm");
            Frame(target,false); Frame(target,true); Solid("Fresh press after focus resumes collision");
            Vector2 screen=hand.inputCamera.WorldToScreenPoint(target);
            hand.pointerBlockRect=new Rect(screen.x-20,Screen.height-screen.y-20,40,40);
            Frame(target,true); Ghost("Visible UI hit region suppresses spoon contact input"); hand.pointerBlockRect=default;
            Frame(target,false); Frame(target,true); Solid("RMB away from UI resumes collision");
            Mouse originalTestMouse=mouse; mouse=InputSystem.AddDevice<Mouse>();
            Frame(target,true); Ghost("Mouse-device replacement cancels contact until release");
            InputSystem.RemoveDevice(originalTestMouse); Frame(target,false); Frame(target,true);
            Solid("New device release and press can rearm");
            bottle.gameObject.SetActive(false); glass.gameObject.SetActive(false); Frame(target,true,left:true);
            Check(hand.Held==null&&!spoon.SpoonContactEnabled&&spoon.solidColliders.All(c=>!c.isTrigger),"Left-click drop while RMB held restores released solid collision");
            Check(!spoon.Body.useFullKinematicContacts&&Mathf.Abs(spoon.Body.angularVelocity)<.001f,"Drop restores native settings and adds no RMB spin");
            bottle.gameObject.SetActive(true); Frame(spoon.Position,false); hand.Pick(spoon,spoon.Position); Frame(spoon.Position,true);
            world.ResetSession(); Check(hand.Held==null&&!spoon.SpoonContactEnabled&&spoon.solidColliders.All(c=>!c.isTrigger),"World Reset restores spoon collision and clears held mode");
            Frame(spoon.Position,false); hand.Pick(spoon,spoon.Position); Frame(spoon.Position,true); hand.SendMessage("OnDisable");
            Check(hand.Held==null&&!spoon.SpoonContactEnabled&&spoon.solidColliders.All(c=>!c.isTrigger),"Interactor disable restores physical spoon state");
            Frame(spoon.Position,false); hand.Pick(spoon,spoon.Position); Frame(spoon.Position,true); spoon.gameObject.SetActive(false);
            Check(hand.Held==null&&!spoon.SpoonContactEnabled&&spoon.solidColliders.All(c=>!c.isTrigger),"Spoon disable forgets held input and restores authored collider flags"); spoon.gameObject.SetActive(true);
            Check(Cursor.lockState==oldLock&&Cursor.visible==oldVisible,"All spoon interactions preserve native cursor lock and visibility");
            Check(errors.Count==0,"No Unity, native physics or input errors"); success=true;
        }
        catch(Exception error) { report.Add(error.ToString()); }
        finally
        {
            hand?.ReleaseWithVelocity(Vector2.zero); Physics2D.simulationMode=oldMode; Physics2D.gravity=oldGravity;
            if(mouse!=null&&mouse.added) InputSystem.RemoveDevice(mouse); if(keyboard!=null&&keyboard.added) InputSystem.RemoveDevice(keyboard);
            if(oldMouse!=null&&oldMouse.added) oldMouse.MakeCurrent(); if(oldKeyboard!=null&&oldKeyboard.added) oldKeyboard.MakeCurrent();
            InputSystem.settings.updateMode=oldUpdate; InputSystem.settings.backgroundBehavior=oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode=oldEditor; Application.runInBackground=oldRun;
            Application.logMessageReceived-=Log; report.AddRange(errors);
            File.WriteAllLines(Path.Combine(Evidence,"spoon-contact-validation.txt"),report);
            File.WriteAllText(Path.Combine(Evidence,"spoon-contact-result.txt"),success?"PASS":"FAIL");
            EditorApplication.isPlaying=false; EditorApplication.delayCall+=()=>EditorApplication.Exit(success?0:1);
        }
    }
}
