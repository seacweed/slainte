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
    FluidExperimentWorld world; FluidExperimentInteractor hand; FluidExperimentBody spoon,bottle;
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
        Check(hand.Held==spoon&&spoon.SpoonContactEnabled&&spoon.solidColliders.All(c=>!c.isTrigger)&&!Ignored(),label);
        Check(spoon.Body.useFullKinematicContacts&&!hand.Rotating&&!hand.Returning&&!hand.PointerCaptured&&!hand.PointerHandoffPending,label+" enables native contacts without cursor capture");
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
            spoon=world.Items.First(x=>x.kind==LabItemKind.Spoon); bottle=world.Items.First(x=>x.kind==LabItemKind.Bottle);
            foreach(var item in world.Items.ToArray()) if(item!=spoon&&item!=bottle) item.gameObject.SetActive(false);
            Physics2D.simulationMode=SimulationMode2D.Script; Physics2D.gravity=Vector2.zero;
            spoon.Teleport(new Vector2(0,4),0); bottle.Teleport(Vector2.zero,0); Physics2D.SyncTransforms();
            bottle.Teleport(new Vector2(.12f,2.6f)-(Vector2)bottle.SolidBounds.center,0); bottle.pourMlPerSecond=0;
            bottle.Body.constraints=RigidbodyConstraints2D.FreezeRotation; Physics2D.SyncTransforms();
            var probe=spoon.gameObject.AddComponent<SpoonContactProbe>(); probe.target=bottle;
            hand.pointerBlockRect=default; hand.inputCamera.transform.position=new Vector3(0,4,-20); hand.inputCamera.orthographicSize=6;
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground=true; mouse=InputSystem.AddDevice<Mouse>(); keyboard=InputSystem.AddDevice<Keyboard>();
            Check(hand.Pick(spoon,spoon.Position),"Actual flipped spoon can be picked in F");
            Frame(spoon.Position,false); Ghost("Held spoon initially passes through other solids");
            hand.BeginRotation(); Check(!hand.Rotating,"Direct rotation API also rejects spoons");
            Vector2 initialBottle=bottle.Position; for(int i=0;i<3;i++) Step();
            Check(probe.contacts==0&&Vector2.Distance(initialBottle,bottle.Position)<.0001f,"Ghost spoon overlap produces no real native contact or bottle displacement");
            Frame(spoon.Position,true); Solid("Real RMB press enables spoon solid collision");
            foreach(var floor in world.floorColliders) if(floor!=null&&floor.enabled)
                Check(!Physics2D.GetIgnoreCollision(spoon.solidColliders[0],floor),"RMB restores spoon contacts with authored environment");
            for(int i=0;i<5;i++) Step();
            report.Add("MEASURE nativeContactCallbacks="+probe.contacts+" bottleDisplacement="+Vector2.Distance(initialBottle,bottle.Position));
            Check(probe.contacts>0&&Vector2.Distance(initialBottle,bottle.Position)>.001f,"RMB spoon generates actual native contact and moves the dynamic bottle");
            float angle=spoon.TargetAngle; Vector2 target=spoon.TargetPosition+new Vector2(.15f,.08f);
            Frame(target,true,new Vector2(20,500));
            Check(Mathf.Abs(spoon.TargetAngle-angle)<.0001f&&Vector2.Distance(spoon.TargetPosition,target)<.001f,"RMB mouse travel follows pointer without rotating spoon");
            Step(); Check(Vector2.Distance(spoon.Position,target)<.03f,"Contact mode uses completed native held movement");
            Frame(target,false); Ghost("Releasing RMB restores held pass-through immediately");
            Check(!spoon.Body.useFullKinematicContacts,"RMB release restores original full-kinematic contact setting");
            var shaker=Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Features/Bartending/FluidGpuExperiment/Prefabs/Shaker.prefab"),world.transform).GetComponent<FluidExperimentBody>();
            shaker.Teleport(new Vector2(5,4),0);
            var part=shaker.ShakerStrainer; Check(part.TryPickUp(part.Position),"Actual strainer can detach for spoon pair test");
            part.Release(Vector2.zero); world.RefreshCollisionPairs();
            Check(Physics2D.GetIgnoreCollision(spoon.solidColliders[0],part.SolidCollider),"Ghost spoon ignores detached strainer");
            Frame(target,true);
            Check(!Physics2D.GetIgnoreCollision(spoon.solidColliders[0],part.SolidCollider),"RMB immediately restores detached strainer contact without waiting for FixedUpdate");
            Frame(target,false);
            Check(Physics2D.GetIgnoreCollision(spoon.solidColliders[0],part.SolidCollider),"RMB release immediately ignores detached strainer again");
            shaker.gameObject.SetActive(false);
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
            bottle.gameObject.SetActive(false); Frame(target,true,left:true);
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
