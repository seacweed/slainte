using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Slainte.Bartending.FluidGpuExperiment;

public static class SpriteLayerFixValidation
{
    const string Key = "Fluid.SpriteLayerFix";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key); new GameObject("SpriteLayerFixValidation").AddComponent<SpriteLayerFixRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

public sealed class SpriteLayerFixRunner : MonoBehaviour
{
    const string Root = "Assets/_Project/Features/Bartending/FluidGpuExperiment";
    readonly List<string> report = new List<string>(), errors = new List<string>();
    readonly List<FluidExperimentBody> spawned = new List<FluidExperimentBody>();
    FluidExperimentComparison comparison;
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentInteractor hand;
    Camera camera;
    ItemDef ingredient;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool value, string label) { report.Add((value ? "PASS " : "FAIL ") + label); if (!value) throw new Exception(label); }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null; yield return null;
        var mode = Physics2D.simulationMode; var gravity = Physics2D.gravity; bool success = false;
        try
        {
            comparison = FindFirstObjectByType<FluidExperimentComparison>();
            comparison.automaticScenario = false; comparison.ResetComparison(); comparison.showControls = false;
            world = comparison.World; gpu = world.Liquid; hand = world.interactor; camera = hand.inputCamera;
            ingredient = world.Items.First(body => body.ingredient != null).ingredient;
            world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
            if (world.Cocktails != null) world.Cocktails.showControls = false;
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = Vector2.zero;
            foreach (var body in world.Items.ToArray()) body.gameObject.SetActive(false);
            foreach (var source in world.GetComponentsInChildren<FluidExperimentGarnishSource>(true)) source.gameObject.SetActive(false);
            camera.backgroundColor = new Color(.16f, .18f, .21f, 1); camera.clearFlags = CameraClearFlags.SolidColor;
            Check(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 && gpu.IsOperational && world.ObjectSortingActive,
                "Actual F scene initializes GPU object sorting on D3D11");
            Bottle(); Contents(); Tools(); Picking(); Parts(); LiquidIsolation(); Modes();
            Check(errors.Count == 0, "No Unity, shader or buffer errors"); success = true;
        }
        catch (Exception error) { report.Add(error.ToString()); }
        finally
        {
            Physics2D.simulationMode = mode; Physics2D.gravity = gravity; Application.logMessageReceived -= Log;
            report.AddRange(errors); File.WriteAllLines(Path.Combine(Evidence, "sprite-layer-fix-validation.txt"), report);
            File.WriteAllText(Path.Combine(Evidence, "sprite-layer-fix-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
    void ResetCase()
    {
        hand.ReleaseWithVelocity(Vector2.zero);
        foreach (var body in spawned) if (body != null) { body.gameObject.SetActive(false); Destroy(body.gameObject); }
        spawned.Clear(); gpu.ResetSimulation(); world.enableObjectSorting = true; world.UpdateVisualSorting();
    }
    FluidExperimentBody Spawn(string name, Vector2 center, float angle = 0)
    {
        var body = Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + name + ".prefab"), world.transform)
            .GetComponent<FluidExperimentBody>();
        body.Teleport(Vector2.zero, angle); body.Body.bodyType = RigidbodyType2D.Kinematic; body.pourMlPerSecond = 0;
        Physics2D.SyncTransforms(); Bounds bounds = ArtBounds(body);
        body.Teleport(center - (Vector2)bounds.center, angle); Physics2D.SyncTransforms(); spawned.Add(body); return body;
    }
    Bounds ArtBounds(FluidExperimentBody body)
    {
        var art = body.GetComponentsInChildren<SpriteRenderer>(true); Bounds bounds = art[0].bounds;
        foreach (var renderer in art.Skip(1)) bounds.Encapsulate(renderer.bounds); return bounds;
    }
    SpriteRenderer[] Art(Component body) => body.GetComponentsInChildren<SpriteRenderer>();
    int Front(Component body) => Art(body).Max(r => r.sortingOrder);
    int Back(Component body) => Art(body).Min(r => r.sortingOrder);
    MeshRenderer Surface(FluidExperimentBody body) => gpu.GetComponentsInChildren<MeshRenderer>()
        .First(r => r.name == "FluidExperiment Liquid Owner " + body.Id);
    RenderTexture SurfaceTexture(MeshRenderer renderer)
    {
        var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
        return (RenderTexture)properties.GetTexture("_SurfaceTex");
    }
    Color32[] Read(RenderTexture target)
    {
        var old = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply(); return image.GetPixels32(); }
        finally { RenderTexture.active = old; Destroy(image); }
    }
    Color32[] Capture(string name, Vector2 center, float size, int width = 900)
    {
        foreach (var body in spawned) if (body != null)
            body.transform.SetPositionAndRotation(new Vector3(body.Position.x, body.Position.y, body.transform.position.z), Quaternion.Euler(0,0,body.Angle));
        var target = new RenderTexture(width, 700, 24); target.Create();
        var texture = new Texture2D(width, 700, TextureFormat.RGBA32, false);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        camera.transform.position = new Vector3(center.x,center.y,-20); camera.orthographicSize = size;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0,0,width,700),0,0); texture.Apply();
            var pixels = texture.GetPixels32(); File.WriteAllBytes(Path.Combine(Evidence,name + ".png"),texture.EncodeToPNG()); return pixels;
        }
        finally { camera.targetTexture = oldTarget; RenderTexture.active = oldActive; target.Release(); Destroy(target); Destroy(texture); }
    }
    void Bottle()
    {
        ResetCase(); var glass = Spawn("Glass_highball",new Vector2(0,2)); var bottle = Spawn("Bottle_item_1001",new Vector2(.1f,2.1f),20);
        bottle.SetHeld(true); world.enableObjectSorting = false; world.UpdateVisualSorting();
        var before = Capture("01-bottle-authored-control",new Vector2(0,2),2.8f);
        Check(Front(bottle) == 3 && Front(glass) == 4,"Opt-out preserves authored bottle and glass orders");
        world.enableObjectSorting = true; var after = Capture("02-bottle-held-fixed",new Vector2(0,2),2.8f);
        Check(Back(bottle) > Front(glass),"Held bottle draws above every face of the glass");
        Check(before.Zip(after,(a,b)=>a.Equals(b)?0:1).Sum() > 50,"Actual pixels change at the held bottle/glass overlap");
        bottle.SetHeld(false); world.UpdateVisualSorting();
        Check(Back(bottle) > Front(glass),"Released bottle keeps deterministic foreground relative to untouched glass");
    }
    void Contents()
    {
        ResetCase(); var glass = Spawn("Glass_highball",new Vector2(0,2));
        gpu.Fill(glass,ingredient,60); gpu.Step(.01f); gpu.ReadbackNow();
        Vector2 inside = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        var garnish = Spawn("nanangaPeel_Garnish",new Vector2(2.5f,2)); garnish.SetHeld(true); garnish.Teleport(inside,0);
        Capture("03-held-garnish-fixed",new Vector2(0,2),2.8f);
        Check(garnish.ContainingVesselId == 0 && Back(garnish) > Front(glass),"Unowned held garnish stays in foreground while crossing vessel interior");
        garnish.SetHeld(false); garnish.Body.bodyType = RigidbodyType2D.Kinematic;
        var ice = Spawn("Ice",new Vector2(0,2)); ice.Teleport(inside+new Vector2(.2f,.4f),0);
        world.RefreshIceContainment(); Capture("04-contained-ice-garnish-fixed",new Vector2(0,2),2.8f);
        Check(garnish.ContainingVesselId == glass.Id && ice.ContainingVesselId == glass.Id,"Actual containment owns both released garnish and ice");
        Check(Back(garnish) > Surface(glass).sortingOrder && Front(garnish) < Front(glass)
            && Back(ice) > Surface(glass).sortingOrder && Front(ice) < Front(glass),"Both contained solids share the band between liquid and front glass");
        var particles = gpu.Snapshot.ToArray(); var composition = gpu.CompositionSnapshot.ToArray();
        var poses = spawned.Select(b=>(b.Position,b.Angle,b.Body.linearVelocity,b.Body.angularVelocity)).ToArray();
        glass.SetHeld(true); var heldPoses = spawned.Select(b=>(b.Position,b.Angle,b.Body.linearVelocity,b.Body.angularVelocity)).ToArray();
        gpu.ReadbackNow(); particles=gpu.Snapshot.ToArray(); composition=gpu.CompositionSnapshot.ToArray();
        Capture("05-filled-glass-held-fixed",new Vector2(0,2),2.8f); gpu.ReadbackNow();
        Check(Back(glass) < Surface(glass).sortingOrder && Surface(glass).sortingOrder < Front(glass),"Raised filled glass carries its GPU liquid between its own faces");
        Check(Read(SurfaceTexture(Surface(glass))).Count(c=>c.a>10) > 100,"Raised vessel surface contains actual nontransparent GPU liquid pixels");
        Check(particles.SequenceEqual(gpu.Snapshot) && composition.SequenceEqual(gpu.CompositionSnapshot),"Rendering and sorting leave GPU particle/composition state byte-equivalent");
        Check(heldPoses.SequenceEqual(spawned.Select(b=>(b.Position,b.Angle,b.Body.linearVelocity,b.Body.angularVelocity))),"Sorting and rendering do not move or accelerate native rigidbodies");
    }
    void Tools()
    {
        ResetCase(); var glass = Spawn("Glass_highball",new Vector2(0,2)); gpu.Fill(glass,ingredient,60); gpu.Step(.01f); gpu.ReadbackNow();
        var jigger = Spawn("Jigger",new Vector2(1.25f,2)); gpu.Fill(jigger,ingredient,20); gpu.Step(.01f); gpu.ReadbackNow(); jigger.SetHeld(true);
        var spoon = Spawn("Spoon",new Vector2(-.25f,2.5f));
        Vector2 tip = spoon.collisionProfile.solids.SelectMany(h=>h.points).OrderBy(p=>p.y).First();
        Vector2 probe = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        spoon.Teleport(spoon.Position + probe - spoon.LocalToWorld(tip),0); spoon.SetHeld(true);
        Capture("06-tools-own-liquid-fixed",new Vector2(.4f,2),2.8f);
        Check(Back(jigger)<Surface(jigger).sortingOrder && Surface(jigger).sortingOrder<Front(jigger),"Jigger back and front straddle its own liquid surface");
        Check(Back(spoon)>Surface(glass).sortingOrder && Front(spoon)<Front(glass),"Inserted spoon tip uses vessel content band above liquid and behind glass front");
        spoon.Teleport(spoon.Position+new Vector2(3,0),0); world.UpdateVisualSorting();
        Check(Back(spoon)>Front(glass),"Withdrawn held spoon returns to foreground");
    }
    void Picking()
    {
        ResetCase(); var bottle = Spawn("Bottle_item_1001",new Vector2(0,2)); var jigger = Spawn("Jigger",new Vector2(.2f,2));
        Bounds bounds=ArtBounds(jigger); Vector2 chosen=Vector2.zero; bool found=false;
        for(float y=bounds.min.y;y<bounds.max.y&&!found;y+=.02f)
            for(float x=bounds.min.x;x<bounds.max.x&&!found;x+=.02f)
            {
                var point=new Vector2(x,y);
                if(bottle.Contains(point)&&jigger.Contains(point)&&(jigger.Position-point).sqrMagnitude<(bottle.Position-point).sqrMagnitude)
                { chosen=point; found=true; }
            }
        Check(found,"Real bottle/jigger overlap includes pointer nearer hidden jigger origin");
        Check(hand.PickAt(chosen)&&hand.Held==bottle,"Click selects visible foreground bottle instead of nearest hidden origin");
        hand.ReleaseWithVelocity(Vector2.zero); bottle.Body.bodyType=RigidbodyType2D.Kinematic;
        // Move the jigger in front without changing its type or the overlapping test point.
        jigger.SetHeld(true); world.UpdateVisualSorting(); jigger.SetHeld(false); world.UpdateVisualSorting();
        Check(hand.PickAt(chosen)&&hand.Held==jigger,"Click follows actual changed visual order after jigger is brought forward");
        Capture("07-pick-visible-jigger-fixed",new Vector2(0,2),2.8f);
    }
    void Parts()
    {
        ResetCase(); var glass=Spawn("Glass_highball",new Vector2(0,2)); var shaker=Spawn("Shaker",new Vector2(.2f,2));
        var strainer=shaker.ShakerStrainer; var cap=shaker.ShakerCap;
        Check(strainer.TryPickUp(strainer.Position),"Actual strainer detaches with its cap"); world.UpdateVisualSorting();
        Check(!strainer.IsAttached&&cap.IsAttached&&Back(strainer)>Front(glass),"Detached held strainer and carried cap draw in foreground");
        Check(Art(cap).All(r=>r.sortingOrder>Back(strainer)),"Carried cap retains internal front order on detached strainer");
        Capture("08-detached-strainer-fixed",new Vector2(0,2),2.8f);
        strainer.Release(Vector2.zero); world.UpdateVisualSorting();
        Check(Back(strainer)>Front(glass),"Released detached part retains stable visual group");
        Check(strainer.TryAttach(),"Strainer reattaches using existing native mechanism"); world.UpdateVisualSorting();
        Check(cap.TryPickUp(cap.Position),"Actual cap detaches independently"); world.UpdateVisualSorting();
        Check(Back(cap)>Front(shaker),"Held detached cap draws above full shaker"); cap.Release(Vector2.zero);
    }
    void LiquidIsolation()
    {
        ResetCase(); var a=Spawn("Glass_highball",new Vector2(-.8f,2)); var b=Spawn("Glass_highball",new Vector2(.8f,2));
        gpu.Fill(a,ingredient,60); gpu.Step(.01f); gpu.ReadbackNow(); Capture("09-two-vessels-one-filled",new Vector2(0,2),2.8f);
        Check(Read(SurfaceTexture(Surface(a))).Any(c=>c.a>10),"Filled vessel owner layer contains liquid");
        Check(Read(SurfaceTexture(Surface(b))).All(c=>c.a==0),"Empty vessel owner layer excludes all other vessel particles");
        var free=gpu.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="FluidExperiment Liquid Surface");
        Check(Read(SurfaceTexture(free)).All(c=>c.a==0),"Free-liquid layer excludes all contained liquid");
        gpu.Fill(b,ingredient,20); gpu.Step(.01f); gpu.ReadbackNow(); b.SetHeld(true); Capture("10-two-filled-vessels",new Vector2(0,2),2.8f);
        Check(Back(b)>Front(a)&&Surface(b).sortingOrder>Front(a),"Raised vessel and its own liquid both sort above another vessel");
        var aPixels=Read(SurfaceTexture(Surface(a))); var bPixels=Read(SurfaceTexture(Surface(b)));
        Check(aPixels.Zip(bPixels,(x,y)=>x.a>10&&y.a>10).All(overlap=>!overlap),"Separated owners produce disjoint real liquid masks");
        int previousWidth=gpu.SurfaceTextureSize.x; Capture("11-resized-owner-targets",new Vector2(0,2),2.8f,650);
        Check(gpu.SurfaceTextureSize.x!=previousWidth&&SurfaceTexture(Surface(a)).width==gpu.SurfaceTextureSize.x
            &&SurfaceTexture(Surface(b)).width==gpu.SurfaceTextureSize.x,"Camera resize recreates all owner textures at matching dimensions");
        uint oldId=b.Id; b.gameObject.SetActive(false); Capture("12-removed-owner",new Vector2(0,2),2.8f);
        Check(!gpu.GetComponentsInChildren<MeshRenderer>().Any(r=>r.name=="FluidExperiment Liquid Owner "+oldId&&!r.forceRenderingOff),"Removed vessel output immediately stops rendering");
        gpu.ResetSimulation(); Capture("13-reset-clears-liquid",new Vector2(0,2),2.8f);
        Check(Read(SurfaceTexture(Surface(a))).All(c=>c.a==0),"Simulation reset clears retained owner textures before next display");
    }
    void Modes()
    {
        ResetCase(); var glass=Spawn("Glass_highball",new Vector2(0,2)); var bottle=Spawn("Bottle_item_1001",new Vector2(.1f,2.1f));
        world.UpdateVisualSorting();
        // Exercise the same active-physics predicate used by comparison mode switching without resetting these fixtures.
        gpu.useCohesivePhysics=false; world.UpdateVisualSorting();
        Check(!world.ObjectSortingActive&&Front(glass)==4&&Back(glass)==-2&&Front(bottle)==3,"Leaving cohesive F/G restores exact authored A-E sprite orders");
        gpu.useCohesivePhysics=true; world.UpdateVisualSorting();
        Check(world.ObjectSortingActive&&Back(glass)>Front(bottle),"Returning to cohesive mode reapplies deterministic groups");
        ResetCase(); comparison.SwitchMode(FluidExperimentMode.GExplosiveBreakage); world.enabled=false; hand.enabled=false;
        Check(gpu.IsOperational&&world.ObjectSortingActive,"Actual G comparison mode initializes sorting after GPU resource disposal/recreation");
        comparison.SwitchMode(FluidExperimentMode.DImprovedSurface); world.enabled=false; hand.enabled=false;
        Check(gpu.IsOperational&&!world.ObjectSortingActive,"Actual D comparison keeps authored legacy rendering policy");
    }
}
