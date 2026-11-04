using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Slainte.Bartending.FluidGpuExperiment;

public static class SpriteLayerValidation
{
    const string Key = "Fluid.SpriteLayerAudit";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key); new GameObject("SpriteLayerAudit").AddComponent<SpriteLayerAuditRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

public sealed class SpriteLayerAuditRunner : MonoBehaviour
{
    const string Root = "Assets/_Project/Features/Bartending/FluidGpuExperiment";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<string> report = new List<string>();
    readonly List<FluidExperimentBody> spawned = new List<FluidExperimentBody>();
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentInteractor hand;
    Camera camera;
    ItemDef ingredient;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool ok, string label) { report.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) throw new Exception(label); }
    IEnumerator Start()
    {
        yield return null; yield return null;
        var mode = Physics2D.simulationMode; var gravity = Physics2D.gravity; bool success = false;
        try
        {
            var comparison = FindFirstObjectByType<FluidExperimentComparison>();
            comparison.automaticScenario = false; comparison.ResetComparison(); comparison.showControls = false;
            world = comparison.World; gpu = world.Liquid; hand = world.interactor; camera = hand.inputCamera;
            ingredient = world.Items.First(body => body.ingredient != null).ingredient;
            world.enabled = false; hand.enabled = false; gpu.automaticReadback = false;
            if (world.Cocktails != null) world.Cocktails.showControls = false;
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = Vector2.zero;
            foreach (var body in world.Items.ToArray()) body.gameObject.SetActive(false);
            foreach (var source in world.GetComponentsInChildren<FluidExperimentGarnishSource>(true)) source.gameObject.SetActive(false);
            camera.backgroundColor = new Color(.16f, .18f, .21f, 1); camera.clearFlags = CameraClearFlags.SolidColor;
            report.Add("MEASURE pipeline=" + GraphicsSettings.currentRenderPipeline?.name + " camera=" + camera.orthographic
                + " sortMode=" + camera.transparencySortMode + " axis=" + camera.transparencySortAxis);
            DumpCatalog();
            OverlapBottleAndGlass();
            SameOrderBottles();
            GarnishAndIce();
            ToolsAndLiquid();
            Picking();
            success = true;
        }
        catch (Exception error) { report.Add(error.ToString()); }
        finally
        {
            Physics2D.simulationMode = mode; Physics2D.gravity = gravity;
            File.WriteAllLines(Path.Combine(Evidence, "sprite-layer-validation.txt"), report);
            File.WriteAllText(Path.Combine(Evidence, "sprite-layer-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
    void ResetCase()
    {
        hand.ReleaseWithVelocity(Vector2.zero);
        foreach (var body in spawned) if (body != null) { body.gameObject.SetActive(false); Destroy(body.gameObject); }
        spawned.Clear(); gpu.ResetSimulation();
    }
    FluidExperimentBody Spawn(string name, Vector2 center, float angle = 0)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + name + ".prefab");
        var body = Instantiate(prefab, world.transform).GetComponent<FluidExperimentBody>();
        body.Teleport(Vector2.zero, angle); body.Body.bodyType = RigidbodyType2D.Kinematic; body.pourMlPerSecond = 0;
        Physics2D.SyncTransforms();
        Bounds bounds = ArtBounds(body);
        body.Teleport(center - (Vector2)bounds.center, angle); Physics2D.SyncTransforms();
        spawned.Add(body); return body;
    }
    Bounds ArtBounds(FluidExperimentBody body)
    {
        var art = body.GetComponentsInChildren<SpriteRenderer>(true); Bounds bounds = art[0].bounds;
        foreach (var renderer in art.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }
    void DumpCatalog()
    {
        var lines = new List<string> { "prefab\trenderer\tsprite\torder\tlayer\tz\tqueue\tshader\tsortingGroups" };
        foreach (string path in Directory.GetFiles(Root + "/Prefabs", "*.prefab"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var renderer in prefab.GetComponentsInChildren<SpriteRenderer>(true))
                lines.Add(string.Join("\t", prefab.name, renderer.name, renderer.sprite != null ? renderer.sprite.name : "none",
                    renderer.sortingOrder, renderer.sortingLayerName, renderer.transform.position.z,
                    renderer.sharedMaterial != null ? renderer.sharedMaterial.renderQueue : -1,
                    renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : "none",
                    prefab.GetComponentsInChildren<SortingGroup>(true).Length));
        }
        File.WriteAllLines(Path.Combine(Evidence, "loaded-renderers.tsv"), lines);
    }
    Color32[] Capture(string name, Vector2 center, float size)
    {
        foreach (var body in spawned) if (body != null)
            body.transform.SetPositionAndRotation(new Vector3(body.Position.x, body.Position.y, body.transform.position.z), Quaternion.Euler(0, 0, body.Angle));
        var target = new RenderTexture(900, 700, 24); target.Create();
        var texture = new Texture2D(900, 700, TextureFormat.RGBA32, false);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        camera.transform.position = new Vector3(center.x, center.y, -20); camera.orthographicSize = size;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 900, 700), 0, 0); texture.Apply();
            var pixels = texture.GetPixels32(); File.WriteAllBytes(Path.Combine(Evidence, name + ".png"), texture.EncodeToPNG());
            return pixels;
        }
        finally { camera.targetTexture = oldTarget; RenderTexture.active = oldActive; target.Release(); Destroy(target); Destroy(texture); }
    }
    void OverlapBottleAndGlass()
    {
        ResetCase(); var glass = Spawn("Glass_highball", new Vector2(0, 2)); var bottle = Spawn("Bottle_item_1001", new Vector2(.1f, 2.1f), 20);
        int[] orders = bottle.GetComponentsInChildren<SpriteRenderer>().Select(renderer => renderer.sortingOrder).ToArray();
        Capture("01-bottle-glass-rest", new Vector2(0, 2), 2.8f);
        bottle.SetHeld(true);
        Capture("02-bottle-glass-held", new Vector2(0, 2), 2.8f);
        Check(orders.SequenceEqual(bottle.GetComponentsInChildren<SpriteRenderer>().Select(renderer => renderer.sortingOrder)),
            "Holding an ordinary bottle leaves its order unchanged at 3; glass fronts remain 4");
    }
    void SameOrderBottles()
    {
        ResetCase(); Spawn("Bottle_item_1001", new Vector2(-.15f, 2)); Spawn("Bottle_item_1002", new Vector2(.15f, 2));
        var first = Capture("03-bottles-create-a-b", new Vector2(0, 2), 2.8f);
        ResetCase(); Spawn("Bottle_item_1002", new Vector2(.15f, 2)); Spawn("Bottle_item_1001", new Vector2(-.15f, 2));
        var second = Capture("04-bottles-create-b-a", new Vector2(0, 2), 2.8f);
        int changed = first.Zip(second, (a, b) => a.Equals(b) ? 0 : 1).Sum();
        report.Add("MEASURE same-order bottle creation reversal changedPixels=" + changed);
    }
    void GarnishAndIce()
    {
        ResetCase(); var glass = Spawn("Glass_highball", new Vector2(0, 2));
        gpu.Fill(glass, ingredient, 60); gpu.Step(.01f); gpu.ReadbackNow();
        Vector2 inside = glass.LocalToWorld(glass.collisionProfile.InteriorBounds.center);
        var garnish = Spawn("nanangaPeel_Garnish", new Vector2(2.5f, 2)); garnish.SetHeld(true);
        var renderer = garnish.GetComponentInChildren<SpriteRenderer>();
        var update = typeof(FluidExperimentBody).GetMethod("UpdateGarnishSorting", Hidden);
        update.Invoke(garnish, null); int outside = renderer.sortingOrder;
        garnish.Teleport(inside, 0); update.Invoke(garnish, null); int heldInside = renderer.sortingOrder;
        Capture("05-held-garnish-over-glass", new Vector2(0, 2), 2.8f);
        report.Add("MEASURE garnish outside=" + outside + " heldInside=" + heldInside + " held=" + garnish.IsHeld + " owner=" + garnish.ContainingVesselId);
        Check(outside == 45 && heldInside == 2 && garnish.IsHeld && garnish.ContainingVesselId == 0,
            "Held garnish loses foreground order merely by overlapping any vessel interior, despite having no owner");
        garnish.SetHeld(false); garnish.Body.bodyType = RigidbodyType2D.Kinematic;
        var ice = Spawn("Ice", new Vector2(0, 2)); ice.Teleport(inside + new Vector2(.2f, .4f), 0);
        world.RefreshIceContainment(); update.Invoke(garnish, null);
        Capture("06-ice-garnish-container", new Vector2(0, 2), 2.8f);
        Check(ice.GetComponentInChildren<SpriteRenderer>().sortingOrder == 5 && renderer.sortingOrder == 2,
            "Contained ice stays above all glass fronts while contained garnish is below them");
        var surface = world.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == "FluidExperiment Liquid Surface");
        report.Add("MEASURE liquid surface order=" + surface?.sortingOrder + " queue=" + surface?.sharedMaterial.renderQueue + " z=" + surface?.transform.position.z);
    }
    void ToolsAndLiquid()
    {
        ResetCase(); var glass = Spawn("Glass_highball", new Vector2(0, 2));
        gpu.Fill(glass, ingredient, 60); gpu.Step(.01f); gpu.ReadbackNow();
        var jigger = Spawn("Jigger", new Vector2(.15f, 2));
        var spoon = Spawn("Spoon", new Vector2(-.25f, 2.5f));
        Capture("07-tools-over-liquid", new Vector2(0, 2), 2.8f);
        Check(jigger.GetComponentsInChildren<SpriteRenderer>().All(r => r.sortingOrder == -1)
            && spoon.GetComponentsInChildren<SpriteRenderer>().All(r => r.sortingOrder == -1),
            "Both jigger faces and the entire spoon share the back order -1 below liquid order 0");
    }
    void Picking()
    {
        ResetCase(); var bottle = Spawn("Bottle_item_1001", new Vector2(0, 2)); var jigger = Spawn("Jigger", new Vector2(.2f, 2));
        Bounds bounds = ArtBounds(jigger); Vector2 chosen = Vector2.zero; bool found = false;
        for (float y = bounds.min.y; y < bounds.max.y && !found; y += .02f)
            for (float x = bounds.min.x; x < bounds.max.x && !found; x += .02f)
            {
                var point = new Vector2(x, y);
                if (bottle.Contains(point) && jigger.Contains(point)
                    && (jigger.Position - point).sqrMagnitude < (bottle.Position - point).sqrMagnitude)
                { chosen = point; found = true; }
            }
        Check(found, "A real overlapping bottle/jigger pick region exists");
        bool picked = hand.PickAt(chosen);
        Capture("08-pick-behind-visible-bottle", new Vector2(0, 2), 2.8f);
        report.Add("MEASURE click=" + chosen + " picked=" + hand.Held?.kind + " bottleOrder=3 jiggerOrder=-1");
        Check(picked && hand.Held == jigger, "Nearest-origin picking can select the lower-order jigger behind a bottle");
    }
}
