using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Slainte.Bartending;
using Slainte.Bartending.FluidGpuExperiment;

public static class LiquidPolishValidation
{
    const string Key = "Fluid.LiquidPolish";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key); new GameObject("LiquidPolishValidation").AddComponent<LiquidPolishRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

public sealed class LiquidPolishRunner : MonoBehaviour
{
    const string Root = "Assets/_Project/Features/Bartending/FluidGpuExperiment";
    const int Width = 1440, Height = 900;
    readonly List<string> report = new List<string>(), errors = new List<string>();
    readonly List<FluidExperimentBody> glasses = new List<FluidExperimentBody>(), ice = new List<FluidExperimentBody>();
    FluidExperimentWorld world; FluidExperimentGpuLiquid gpu; Camera camera;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool ok, string label) { report.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) throw new Exception(label); }
    void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    FluidExperimentBody Spawn(string prefab, Vector2 center)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + prefab + ".prefab");
        var body = Instantiate(source, world.transform).GetComponent<FluidExperimentBody>();
        body.Body.bodyType = RigidbodyType2D.Kinematic; body.pourMlPerSecond = 0; body.Teleport(Vector2.zero, 0);
        Physics2D.SyncTransforms(); var renderers = body.GetComponentsInChildren<SpriteRenderer>(); var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        body.Teleport(center - (Vector2)bounds.center, 0); Physics2D.SyncTransforms(); return body;
    }
    SpriteRenderer Art(FluidExperimentBody body) => body.GetComponentInChildren<SpriteRenderer>();
    RenderTexture OwnerTexture(uint id)
    {
        var renderer = gpu.GetComponentsInChildren<MeshRenderer>().First(r => r.name == "FluidExperiment Liquid Owner " + id);
        var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
        return (RenderTexture)properties.GetTexture("_SurfaceTex");
    }
    Color32[] Read(RenderTexture target)
    {
        var previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try { RenderTexture.active = target; image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply(); return image.GetPixels32(); }
        finally { RenderTexture.active = previous; Destroy(image); }
    }
    Color32[] Capture(string name, int width = Width, int height = Height)
    {
        // This frozen fixture renders in the same coroutine as Teleport; publish the
        // completed native poses explicitly instead of waiting for render interpolation.
        foreach (var body in world.Items)
            if (body != null) body.transform.SetPositionAndRotation(new Vector3(body.Position.x, body.Position.y, body.transform.position.z), Quaternion.Euler(0,0,body.Angle));
        var target = new RenderTexture(width,height,24); target.Create(); var previous = camera.targetTexture;
        try
        {
            camera.targetTexture = target; camera.Render(); var pixels = Read(target);
            var image = new Texture2D(width,height,TextureFormat.RGBA32,false); image.SetPixels32(pixels); image.Apply();
            File.WriteAllBytes(Path.Combine(Evidence,name + ".png"),image.EncodeToPNG()); Destroy(image); return pixels;
        }
        finally { camera.targetTexture = previous; target.Release(); Destroy(target); }
    }
    IEnumerator Start()
    {
        Application.logMessageReceived += Log; yield return null; yield return null;
        var mode = Physics2D.simulationMode; var gravity = Physics2D.gravity; bool success = false;
        try
        {
            var sandbox = FindFirstObjectByType<FluidExperimentComparison>(); sandbox.ResetSandbox(); sandbox.showControls = false;
            world = sandbox.World; gpu = world.Liquid; camera = world.interactor.inputCamera;
            var ingredients = world.Items.Where(b => b.kind == LabItemKind.Bottle && b.ingredient != null).Select(b => b.ingredient).Take(3).ToArray();
            world.enabled = false; world.interactor.enabled = false; world.Cocktails.showControls = false; gpu.automaticReadback = false;
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = Vector2.zero;
            foreach (var body in world.Items.ToArray()) body.gameObject.SetActive(false);
            foreach (var source in world.GetComponentsInChildren<FluidExperimentGarnishSource>(true)) source.gameObject.SetActive(false);
            gpu.ResetSimulation();
            camera.transform.position = new Vector3(0,2.8f,-20); camera.orthographicSize = 3.2f; camera.aspect = Width / (float)Height;
            camera.backgroundColor = new Color(.12f,.15f,.19f,1); camera.clearFlags = CameraClearFlags.SolidColor;
            var names = new[] { "Glass_highball", "Glass_rock", "Glass_martini" };
            for (int i = 0; i < names.Length; i++)
            {
                var glass = Spawn(names[i],new Vector2(-3.4f + i * 3.4f,2.6f)); glasses.Add(glass);
                report.Add("INFO vessel=" + glass.IsVessel + " operational=" + gpu.IsOperational + " error=" + gpu.Error
                    + " position=" + glass.Position + " scale=" + glass.transform.lossyScale + " radius=" + gpu.Radius
                    + " regions=" + string.Join(";", glass.contentRegions.Select(r => r.ToString())) + " ingredient=" + ingredients[i % ingredients.Length].id);
                Check(gpu.Fill(glass,ingredients[i % ingredients.Length],i == 0 ? 45 : 28) > 0, names[i] + " contains actual authored ingredient particles");
            }
            gpu.Step(.0001f); gpu.ReadbackNow(); gpu.ImprovedSurfaceInterpolationOverride = 1;
            foreach (var glass in glasses)
            {
                var particles = gpu.Snapshot.Where(p => p.Active != 0 && p.VesselId == glass.Id).ToArray();
                Vector2 center = new Vector2(particles.Average(p => p.Position.x), particles.Max(p => p.Position.y) + gpu.Radius * .25f);
                ice.Add(Spawn("Ice",center));
            }
            var dry = Spawn("Ice",new Vector2(0,.7f));
            world.RefreshIceContainment(); world.UpdateVisualSorting();
            Check(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 && gpu.ImprovedSurfaceReady, "Actual D3D11 improved liquid surface is ready");
            Check(ice.Select((body,i) => body.ContainingVesselId == glasses[i].Id).All(x => x), "Every wet ice belongs to its actual containing vessel");
            Check(dry.ContainingVesselId == 0, "Dry ice fixture has no containing vessel");
            var particlesBefore = gpu.Snapshot.ToArray(); var mixtureBefore = gpu.CompositionSnapshot.ToArray();
            var poses = world.Items.Select(b => (b.Position,b.Angle,b.Body.linearVelocity,b.Body.angularVelocity)).ToArray();
            var originals = ice.Select(b => Art(b).sharedMaterial).ToArray(); var dryMaterial = Art(dry).sharedMaterial;
            gpu.settings.polishLiquidAppearance = false; var before = Capture("before");
            var mask = Read(OwnerTexture(glasses[0].Id));
            float rim = gpu.settings.surfaceRimStrength, shade = gpu.settings.surfaceLowerEdgeShade;
            gpu.settings.polishLiquidAppearance = true; gpu.settings.surfaceRimStrength = gpu.settings.surfaceLowerEdgeShade = 0;
            var iceOnly = Capture("ice-only");
            Check(ice.All(b => Art(b).sharedMaterial.shader == gpu.settings.iceImmersionShader), "Contained ice uses the immersion shader");
            for (int i = 0; i < ice.Count; i++)
            {
                var properties = new MaterialPropertyBlock(); Art(ice[i]).GetPropertyBlock(properties);
                Check(properties.GetTexture("_IceLiquidTex") == OwnerTexture(glasses[i].Id), "Ice samples its own vessel texture " + i);
            }
            Check(Art(dry).sharedMaterial == dryMaterial, "Unowned dry ice retains its original material");
            var bounds = Art(ice[0]).bounds;
            Vector3 min = camera.WorldToViewportPoint(bounds.min), max = camera.WorldToViewportPoint(bounds.max);
            int wetChanges = 0, dryChanges = 0;
            for (int y = Mathf.Max(0,(int)(min.y*Height)); y < Mathf.Min(Height,(int)(max.y*Height)); y++)
            for (int x = Mathf.Max(0,(int)(min.x*Width)); x < Mathf.Min(Width,(int)(max.x*Width)); x++)
            {
                int index = y*Width+x; var a = before[index]; var b = iceOnly[index];
                if (Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b) <= 3) continue;
                if (mask[index].a >= 8) wetChanges++; else dryChanges++;
            }
            report.Add("MEASURE firstIceWetPixelChanges=" + wetChanges + " dryPixelChanges=" + dryChanges);
            Check(wetChanges > 10 && dryChanges < 10, "Visible ice tint changes submerged pixels while dry pixels stay unchanged");
            gpu.settings.surfaceRimStrength = rim; gpu.settings.surfaceLowerEdgeShade = shade;
            var after = Capture("after");
            Check(before.Zip(after,(a,b) => a.Equals(b) ? 0 : 1).Sum() > 100, "Polished render has an observable pixel difference");
            Check(mask.SequenceEqual(Read(OwnerTexture(glasses[0].Id))), "Appearance changes preserve the exact liquid color/coverage render target");
            var comparison = new Texture2D(Width*2,Height,TextureFormat.RGBA32,false);
            comparison.SetPixels32(0,0,Width,Height,before); comparison.SetPixels32(Width,0,Width,Height,after); comparison.Apply();
            File.WriteAllBytes(Path.Combine(Evidence,"comparison.png"),comparison.EncodeToPNG()); Destroy(comparison);
            Capture("resized",1024,768);
            Check(OwnerTexture(glasses[0].Id).width == 1024, "Owner textures resize and remain available to ice");
            gpu.settings.polishLiquidAppearance = false; var reverted = Capture("reverted");
            Check(ice.Select((b,i) => Art(b).sharedMaterial == originals[i]).All(x => x), "Disabling appearance restores every original ice material");
            Check(before.SequenceEqual(reverted), "The appearance toggle restores the exact original render");
            gpu.ReadbackNow();
            Check(particlesBefore.SequenceEqual(gpu.Snapshot) && mixtureBefore.SequenceEqual(gpu.CompositionSnapshot), "Rendering preserves every GPU particle, ml, temperature, technique and mixture value");
            Check(poses.SequenceEqual(world.Items.Select(b => (b.Position,b.Angle,b.Body.linearVelocity,b.Body.angularVelocity))), "Rendering preserves all body poses and velocities");
            Check(ice.All(b => Mathf.Abs(Art(b).color.a - .55f) < .0001f), "Authored ice alpha remains 0.55");
            gpu.settings.polishLiquidAppearance = true; Capture("ready"); gpu.Dispose();
            Check(ice.Select((b,i) => Art(b).sharedMaterial == originals[i]).All(x => x), "Disposal restores ice materials before releasing render targets");
            Check(errors.Count == 0, "No Unity, shader or GPU errors"); success = true;
        }
        catch (Exception error) { report.Add(error.ToString()); }
        finally
        {
            Physics2D.simulationMode = mode; Physics2D.gravity = gravity; Application.logMessageReceived -= Log; report.AddRange(errors);
            File.WriteAllLines(Path.Combine(Evidence,"liquid-polish-validation.txt"),report);
            File.WriteAllText(Path.Combine(Evidence,"liquid-polish-result.txt"),success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
