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

// Imported only into the disposable harness by prepare_experiment.py. Real compute dispatches
// verify the Model D environment/entry contract; no desktop input or production scene is changed.
public static class ExperimentModelDInteractionValidation
{
    private const string Key = "Slainte.FluidGpuExperiment.ModelDInteractionValidation";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR")
        ?? "FluidGpuExperimentEvidence/model-d-interaction-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

    [InitializeOnLoadMethod]
    private static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("ModelDInteractionValidationRunner").AddComponent<ExperimentModelDInteractionValidationRunner>();
    };

    public static void Begin()
    {
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Path.Combine(Evidence, "model-d-interaction-result.txt"), "STARTED; no result yet\n");
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    public static void Finish(bool success, string report)
    {
        File.WriteAllText(Path.Combine(Evidence, "model-d-interaction-validation.txt"), report);
        File.WriteAllText(Path.Combine(Evidence, "model-d-interaction-result.txt"), success ? "PASS\n" : "FAIL\n");
        if (success) Debug.Log(report); else Debug.LogError(report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ExperimentModelDInteractionValidationRunner : MonoBehaviour
{
    private const float Dt = .02f;
    private const float Half = .6f;
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private readonly List<FluidExperimentBody> probes = new List<FluidExperimentBody>();
    private FluidExperimentComparison comparison;
    private FluidExperimentWorld world;
    private FluidExperimentGpuLiquid gpu;
    private FluidExperimentBody cup, source;
    private ItemDef[] ingredients;
    private SimulationMode2D originalMode;
    private Vector2 originalGravity;
    private bool controlled, finished;
    private int checks;
    private float started;

    private IEnumerator Start()
    {
        started = Time.realtimeSinceStartup;
        Application.logMessageReceived += OnLog;
        yield return null;
        comparison = FindFirstObjectByType<FluidExperimentComparison>();
        while (comparison != null && !comparison.Ready && Time.realtimeSinceStartup - started < 20)
            yield return null;
        bool success = true;
        try
        {
            Check(comparison != null && comparison.Ready, "authored comparison scene initializes");
            comparison.automaticScenario = false;
            comparison.showControls = false;
            comparison.enabled = false;
            comparison.SwitchMode(FluidExperimentMode.DImprovedSurface);
            comparison.StartScenario(FluidExperimentScenario.Manual);
            world = comparison.World;
            gpu = comparison.Gpu;
            world.enabled = false;
            world.interactor.enabled = false;
            world.seedLiquids = false;
            gpu.automaticReadback = false;
            Check(gpu.IsOperational, "D compute shader operational: " + gpu.Error);
            Check(gpu.ActiveSolver == FluidExperimentSolver.ReferenceSph && gpu.useImprovedSurface && !gpu.useImprovedPhysics,
                "D uses reference physics with improved surface and no E calibration");
            originalMode = Physics2D.simulationMode;
            originalGravity = Physics2D.gravity;
            controlled = true;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = Vector2.zero;
            ingredients = world.Items.Where(x => x.ingredient != null).Select(x => x.ingredient).Distinct().Take(2).ToArray();
            Check(ingredients.Length == 2, "two authored ingredients available for conservation checks");
            int i = 0;
            foreach (var body in world.Items)
            {
                body.SetHeld(false);
                body.Body.bodyType = RigidbodyType2D.Kinematic;
                body.Teleport(new Vector2(-50 - 4 * i++, 20), 0);
                body.pourMlPerSecond = 0;
            }
            cup = CreateCup("EntryProbe");
            source = CreateCup("SourceProbe");
            ValidateHeldEnvironmentClamp();
            ValidateWorldBoundaries();
            ValidateEnvironmentSurfaceMask();
            ValidateMouthEntry(false);
            ValidateMouthEntry(true);
            ValidateAuthoredMouthEntry();
            ValidateMovingMouth();
            ValidateOverlapRejection();
            ValidateClosedEntry();
            ValidateForeignOwnership();
            ValidateSameCupRecapture();
            ValidateIngredientCapture();
            ValidateTerminalRetirement();
            ValidateAuthoredShakerLiquidPassage();
            ExperimentIceBucketChecks.Run(world, Check);
            Check(errors.Count == 0, "no Unity errors during real GPU probes");
        }
        catch (Exception exception) { success = false; report.Add(exception.ToString()); }
        Finish(success);
    }

    private FluidExperimentBody CreateCup(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(world.transform, false);
        var body = go.AddComponent<FluidExperimentBody>();
        body.kind = LabItemKind.Glass;
        body.liquidWall = new[] { new Vector2(-Half, Half), new Vector2(-Half, -Half),
            new Vector2(Half, -Half), new Vector2(Half, Half) };
        body.wallClosed = false;
        body.contentRegions = new[] { new Rect(-Half, -Half, Half * 2, Half * 2) };
        body.mouthLocal = new Vector2(0, Half);
        body.pourMlPerSecond = 0;
        body.iceStock = 0;
        var pick = go.AddComponent<BoxCollider2D>();
        pick.isTrigger = true;
        pick.size = Vector2.one * (Half * 2);
        body.pickCollider = pick;
        var edges = new List<Collider2D>();
        for (int edge = 0; edge < 3; edge++)
        {
            var collider = go.AddComponent<EdgeCollider2D>();
            collider.points = new[] { body.liquidWall[edge], body.liquidWall[edge + 1] };
            edges.Add(collider);
        }
        body.solidColliders = edges.ToArray();
        body.Body.bodyType = RigidbodyType2D.Kinematic;
        body.Body.interpolation = RigidbodyInterpolation2D.None;
        body.Teleport(new Vector2(-40, 0), 0);
        probes.Add(body);
        return body;
    }

    private void ResetCase(string name, bool held = false, Vector2? position = null, float angle = 0)
    {
        report.Add("CASE " + name);
        foreach (var body in probes)
        {
            body.SetHeld(false);
            body.SetSealed(false);
            body.Body.bodyType = RigidbodyType2D.Kinematic;
            body.Teleport(new Vector2(-40, 0), 0);
        }
        cup.Teleport(position ?? new Vector2(0, 5), angle);
        cup.SetHeld(held);
        if (!held) cup.Body.bodyType = RigidbodyType2D.Kinematic;
        gpu.ResetSimulation();
        Physics2D.SyncTransforms();
        foreach (var body in world.Items) body.SynchronizeHistory();
    }

    private void Emit(Vector2 position, Vector2 velocity, uint owner = 0, int ingredient = 0, float volume = .5f)
        => Check(gpu.TryEmit(position, velocity, ingredients[ingredient], volume, owner), "accepted explicit " + volume + " ml probe");

    private void Step(int count = 1)
    {
        for (int i = 0; i < count; i++) world.TickLiquid(Dt);
        gpu.ReadbackNow();
        foreach (var p in gpu.Snapshot.Where(x => x.Active != 0))
            Check(float.IsFinite(p.Position.x) && float.IsFinite(p.Position.y)
                && float.IsFinite(p.Velocity.x) && float.IsFinite(p.Velocity.y), "active probe remains finite");
    }

    private GpuLiquidParticle Single(string label)
    {
        Check(gpu.ActiveCount == 1, label + " retains exactly one active particle");
        return gpu.Snapshot.Single(x => x.Active != 0);
    }

    private void MoveCup(Vector2 target, float angle = 0)
    {
        // Preserve the previous pose, just as the world's completed physics tick does. Teleport
        // deliberately resets motion history and would not test moving-mouth entry.
        cup.SetHeldPose(target, angle);
        typeof(FluidExperimentBody).GetMethod("ApplyHeldPose", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cup, null);
        Physics2D.SyncTransforms();
    }

    private void ValidateWorldBoundaries()
    {
        foreach (string name in new[] { "LeftBoundary", "RightBoundary", "TopBoundary" })
        {
            BoxCollider2D collider = world.GetComponentsInChildren<BoxCollider2D>()
                .Single(x => x.gameObject.name == name);
            Bounds bounds = collider.bounds;
            bool left = name == "LeftBoundary", top = name == "TopBoundary";
            Vector2 inward = top ? Vector2.down : left ? Vector2.right : Vector2.left;
            Vector2 contact = top ? new Vector2(0, bounds.min.y) : new Vector2(left ? bounds.max.x : bounds.min.x, 5);
            foreach (bool owned in new[] { false, true })
            {
                ResetCase(name + (owned ? " held-owned mouth exit" : " free drop"));
                uint owner = 0;
                if (owned)
                {
                    float angle = top ? 0 : left ? 90 : -90;
                    cup.Teleport(contact + inward * (Half + gpu.Radius + .005f), angle);
                    cup.SetHeld(true);
                    owner = cup.Id;
                }
                Vector2 seed = contact + inward * (gpu.Radius + .1f);
                Emit(seed, -inward * 20, owner);
                Step(3);
                GpuLiquidParticle p = Single(name);
                float clearance = Vector2.Dot(p.Position - contact, inward);
                Check(clearance >= gpu.Radius - .005f, name + " blocks fast disk on inside face; clearance=" + clearance);
                Check(Vector2.Dot(p.Velocity, inward) >= -.05f, name + " removes outward normal velocity");
                Close(gpu.Ledger.Total.RetiredMl, 0, name + " impact does not retire liquid");
                Accounting(name, .5, .5, 0);
                if (!owned)
                {
                    float angle = top ? 0 : left ? 90 : -90;
                    cup.Teleport(contact + inward * (Half + .2f), angle);
                    cup.SetHeld(true);
                    MoveCup(contact + inward * (Half + .005f), angle);
                    Step();
                    Check(Single(name + " recapture").VesselId == cup.Id,
                        name + " contacted drop remains catchable through a moving open mouth");
                    Accounting(name + " recapture", .5, .5, 0);
                }
            }
        }
        ResetCase("ceiling-right corner");
        var right = world.GetComponentsInChildren<BoxCollider2D>().Single(x => x.gameObject.name == "RightBoundary");
        var ceiling = world.GetComponentsInChildren<BoxCollider2D>().Single(x => x.gameObject.name == "TopBoundary");
        Vector2 corner = new Vector2(right.bounds.min.x, ceiling.bounds.min.y);
        Emit(corner - Vector2.one * .2f, Vector2.one * 14);
        Step(3);
        var atCorner = Single("corner");
        Check(atCorner.Position.x <= corner.x - gpu.Radius + .005f
            && atCorner.Position.y <= corner.y - gpu.Radius + .005f, "both corner faces constrain the particle disk");
        Accounting("corner", .5, .5, 0);
    }

    private void ValidateEnvironmentSurfaceMask()
    {
        ResetCase("D environment surface mask");
        Camera camera = gpu.outputCamera;
        BoxCollider2D right = world.rightBoundary, top = world.topBoundary;
        Vector2 corner = new Vector2(right.bounds.min.x, top.bounds.min.y);
        Emit(corner - Vector2.one * gpu.Radius, Vector2.zero);
        Step();
        var oldPosition = camera.transform.position;
        float oldSize = camera.orthographicSize, oldInterpolation = gpu.ImprovedSurfaceInterpolationOverride;
        RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
        var target = new RenderTexture(256, 256, 24);
        try
        {
            camera.transform.position = new Vector3(corner.x, corner.y, -20);
            camera.orthographicSize = .35f;
            camera.targetTexture = target;
            gpu.ImprovedSurfaceInterpolationOverride = 1;
            int Leaks(bool masked)
            {
                right.enabled = top.enabled = masked;
                camera.Render();
                var surface = (RenderTexture)typeof(FluidExperimentGpuLiquid)
                    .GetField("surfaceComposite", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gpu);
                var texture = new Texture2D(surface.width, surface.height, TextureFormat.RGBA32, false, true);
                try
                {
                    RenderTexture.active = surface;
                    texture.ReadPixels(new Rect(0, 0, surface.width, surface.height), 0, 0);
                    texture.Apply();
                    var pixels = texture.GetPixels();
                    int leaks = 0, visible = 0;
                    for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
                    {
                        if (pixels[y * texture.width + x].a <= .01f) continue;
                        visible++;
                        Vector2 point = camera.ViewportToWorldPoint(new Vector3((x + .5f) / texture.width,
                            (y + .5f) / texture.height, 20));
                        if (point.x > corner.x + .00001f || point.y > corner.y + .00001f) leaks++;
                    }
                    Check(visible > 0, "D environment mask keeps real visible liquid");
                    File.WriteAllBytes(Path.Combine(ExperimentModelDInteractionValidation.Evidence,
                        masked ? "D-world-mask.png" : "D-world-mask-control.png"), texture.EncodeToPNG());
                    return leaks;
                }
                finally { Destroy(texture); }
            }
            Check(Leaks(true) == 0, "D surface has zero visible pixels beyond the wall and ceiling");
            Check(Leaks(false) > 0, "disabled-boundary control proves the same splat reaches the forbidden area");
        }
        finally
        {
            right.enabled = top.enabled = true;
            camera.transform.position = oldPosition; camera.orthographicSize = oldSize;
            camera.targetTexture = oldTarget; gpu.ImprovedSurfaceInterpolationOverride = oldInterpolation;
            RenderTexture.active = oldActive; target.Release(); Destroy(target);
        }
    }

    private void ValidateHeldEnvironmentClamp()
    {
        ResetCase("held environment clamp", true);
        float left = world.GetComponentsInChildren<BoxCollider2D>().Single(x => x.gameObject.name == "LeftBoundary").bounds.max.x;
        float right = world.GetComponentsInChildren<BoxCollider2D>().Single(x => x.gameObject.name == "RightBoundary").bounds.min.x;
        float top = world.GetComponentsInChildren<BoxCollider2D>().Single(x => x.gameObject.name == "TopBoundary").bounds.min.y;
        foreach (float angle in new[] { 0, 45, 90, 225f })
        {
            foreach (float x in new[] { -100f, 100f })
            {
                cup.SetHeldPose(new Vector2(x, 100), angle);
                Vector2[] hull = cup.liquidWall.Select(p => cup.PointAt(p, cup.TargetPosition, cup.TargetAngle)).ToArray();
                Check(hull.Min(p => p.x) >= left - .001f && hull.Max(p => p.x) <= right + .001f
                    && hull.Max(p => p.y) <= top + .001f, "held solid envelope clamps to side/top at angle " + angle);
            }
        }
        float belowDrain = gpu.settings.gpuLiquidWorldMin.y - 5;
        cup.SetHeldPose(new Vector2(0, belowDrain), 0);
        Close(cup.TargetPosition.y, belowDrain, "held position preserves bottom drain access");
    }

    private void ValidateMouthEntry(bool held)
    {
        string label = held ? "held open mouth" : "unheld open mouth";
        ResetCase(label, held);
        Emit(cup.LocalToWorld(new Vector2(0, Half + .3f)), Vector2.down * 6);
        Step(5);
        Check(Single(label).VesselId == cup.Id, label + " captures inward crossing");
        Close(gpu.VolumeIn(cup.Id), .5, label + " owns captured volume");
        Accounting(label, .5, .5, 0);
    }

    private void ValidateAuthoredMouthEntry()
    {
        FluidExperimentBody[] glasses = world.Items.Where(x => x.kind == LabItemKind.Glass
            && x.collisionProfile != null && x.collisionProfile.interior.Length >= 3).ToArray();
        Check(glasses.Length >= 2, "authored highball and hurricane fixtures available");
        foreach (var glass in glasses)
        {
            string label = "authored held mouth " + glass.name;
            ResetCase(label);
            cup.Teleport(new Vector2(-40, 0), 0);
            glass.Teleport(new Vector2(0, 5), 0);
            glass.SetHeld(true);
            Vector2[] contour = glass.collisionProfile.interior;
            Vector2 rimA = glass.LocalToWorld(contour[contour.Length - 1]);
            Vector2 rimB = glass.LocalToWorld(contour[0]);
            Vector2 tangent = (rimB - rimA).normalized;
            Vector2 inward = new Vector2(-tangent.y, tangent.x);
            Emit((rimA + rimB) * .5f - inward * .2f, inward * 2);
            Step(12);
            Check(Single(label).VesselId == glass.Id, label + " acquires a slow drop through the actual authored rim");
            Accounting(label, .5, .5, 0);
            glass.SetHeld(false);
            glass.Body.bodyType = RigidbodyType2D.Kinematic;
            glass.Teleport(new Vector2(-50, 20), 0);
        }
    }

    private void ValidateMovingMouth()
    {
        ResetCase("moving open mouth", true);
        Vector2 seed = cup.LocalToWorld(new Vector2(0, Half + .2f));
        Emit(seed, Vector2.zero);
        Step();
        Check(Single("before moving mouth").VesselId == 0, "drop begins outside mouth and unowned");
        MoveCup(cup.Position + Vector2.up * .5f);
        Step();
        Check(Single("moving mouth").VesselId == cup.Id, "relative inward crossing captures a stationary drop");
        Accounting("moving mouth", .5, .5, 0);

        ResetCase("rotating open mouth", true);
        // With the mouth rotating counterclockwise, an external point near the mouth crosses the
        // opening into the bowl without crossing a side wall or the floor.
        Vector2 rotatingSeed = cup.LocalToWorld(new Vector2(.1f, Half + .12f));
        Emit(rotatingSeed, Vector2.zero);
        Step();
        Check(Single("before rotating mouth").VesselId == 0, "rotation probe begins outside mouth");
        MoveCup(cup.Position, 35);
        Step();
        Check(Single("rotating mouth").VesselId == cup.Id, "rotating mouth uses relative swept entry");
        Accounting("rotating mouth", .5, .5, 0);
    }

    private void ValidateOverlapRejection()
    {
        foreach (bool held in new[] { false, true })
        {
            ResetCase("interior overlap held=" + held, held);
            Emit(cup.Position, Vector2.zero);
            Step(3);
            Check(Single("interior overlap").VesselId == 0, "interior overlap without mouth entry never acquires ownership");
            Accounting("interior overlap", .5, .5, 0);
        }
        foreach (bool bottom in new[] { false, true })
        {
            ResetCase(bottom ? "bottom sweep" : "side sweep", true,
                bottom ? new Vector2(0, 7) : new Vector2(2, 5));
            Emit(new Vector2(0, 5), Vector2.zero);
            Step();
            MoveCup(new Vector2(0, 5));
            Step(3);
            Check(Single("side or bottom sweep").VesselId == 0,
                "sweeping a held side or bottom through a drop does not capture it");
            Accounting("non-mouth sweep", .5, .5, 0);
        }
    }

    private void ValidateClosedEntry()
    {
        ResetCase("closed mouth", true);
        cup.SetSealed(true);
        Emit(cup.LocalToWorld(new Vector2(0, Half + .3f)), Vector2.down * 6);
        Step(5);
        Check(Single("closed mouth").VesselId == 0, "closed vessel cannot capture an external drop");
        Accounting("closed mouth", .5, .5, 0);
    }

    private void ValidateForeignOwnership()
    {
        ResetCase("foreign contents", true, new Vector2(0, 3.8f));
        source.Teleport(new Vector2(0, 5), 0);
        Emit(source.Position, Vector2.zero, source.Id);
        Step();
        MoveCup(new Vector2(0, 5));
        Step(2);
        Check(Single("foreign contents").VesselId == source.Id,
            "moving open mouth cannot steal liquid still inside its source vessel");
        Close(gpu.VolumeIn(cup.Id), 0, "receiver acquires none of the foreign contents");
        Accounting("foreign contents", .5, .5, 0);
    }

    private void ValidateSameCupRecapture()
    {
        ResetCase("same-cup spill and recapture", true);
        Emit(cup.LocalToWorld(new Vector2(0, Half - .15f)), Vector2.up * 8, cup.Id);
        Step(2);
        Check(Single("spilled").VesselId == 0, "actual outward mouth crossing releases owner");
        Close(gpu.Ledger.Total.RetiredMl, 0, "airborne spill remains active and recoverable");
        MoveCup(cup.Position + Vector2.up);
        Step();
        Check(Single("recaptured").VesselId == cup.Id, "same held cup recaptures its active spilled drop through its mouth");
        Accounting("same-cup recapture", .5, .5, 0);
    }

    private void ValidateIngredientCapture()
    {
        ResetCase("ingredient-preserving capture", true);
        Emit(cup.LocalToWorld(new Vector2(-.3f, Half + .3f)), Vector2.down * 6, 0, 0, .375f);
        Emit(cup.LocalToWorld(new Vector2(.3f, Half + .3f)), Vector2.down * 6, 0, 1, .125f);
        Step(5);
        Check(gpu.ActiveCount == 2 && gpu.Snapshot.Where(x => x.Active != 0).All(x => x.VesselId == cup.Id),
            "both ingredient drops enter through the held mouth");
        Close(gpu.IngredientVolumeIn(cup.Id, ingredients[0]), .375, "capture preserves first ingredient ml");
        Close(gpu.IngredientVolumeIn(cup.Id, ingredients[1]), .125, "capture preserves second ingredient ml");
        Accounting("ingredient capture", .5, .5, 0);
    }

    private void ValidateTerminalRetirement()
    {
        ResetCase("terminal retirement");
        Vector2 retiredPosition = new Vector2(0, gpu.settings.gpuLiquidWorldMin.y - 1);
        Emit(retiredPosition, Vector2.zero, 0, 1, .375f);
        Step();
        Check(gpu.ActiveCount == 0, "out-of-domain particle retires");
        cup.Teleport(retiredPosition, 0);
        cup.SetHeld(true);
        Step(3);
        Check(gpu.ActiveCount == 0, "placing a cup over the retired position cannot resurrect liquid");
        Accounting("retired overlap", .375, 0, .375);
        cup.Teleport(new Vector2(0, 5), 0);
        Emit(cup.LocalToWorld(new Vector2(0, Half + .3f)), Vector2.down * 6, 0, 0, .5f);
        Step(5);
        Check(Single("post-retirement new birth").VesselId == cup.Id, "subsequent legitimate birth can enter the cup");
        Close(gpu.IngredientVolumeIn(cup.Id, ingredients[1]), 0, "retired ingredient never reappears after slot reuse");
        Accounting("post-retirement birth", .875, .5, .375);
    }

    private void ValidateAuthoredShakerLiquidPassage()
    {
        FluidExperimentBody shaker = world.Items.First(x => x.kind == LabItemKind.Shaker);
        Vector2 savedGravity = Physics2D.gravity;
        try
        {
            Physics2D.gravity = new Vector2(0, -9.81f);
            foreach (FluidExperimentShakerClosure closure in Enum.GetValues(typeof(FluidExperimentShakerClosure)))
            {
                string label = "authored inverted shaker " + closure;
                ResetCase(label);
                cup.Teleport(new Vector2(-40, 0), 0);
                shaker.Teleport(new Vector2(0, 6), 180);
                shaker.SetShakerClosure(closure);
                shaker.SetHeld(true);
                Vector2[] bowl = shaker.collisionProfile.interior;
                Vector2 bowlMouth = shaker.LocalToWorld((bowl[0] + bowl[bowl.Length - 1]) * .5f);
                Vector2 outward = FluidExperimentBody.Rotate(Vector2.up, shaker.Angle);
                Vector2 actualOutlet = closure == FluidExperimentShakerClosure.Straining
                    ? shaker.LocalToWorld(shaker.ShakerOutletLocal) : bowlMouth;
                Check(closure != FluidExperimentShakerClosure.Straining
                    || shaker.ShakerOutletWidth * Mathf.Abs(shaker.transform.lossyScale.x) > 2 * gpu.Radius,
                    label + " uses a physically passable D outlet");
                // The authored lid skirt extends .25 units below the bowl rim. Seed
                // clear of that real solid, rather than creating a particle inside metal.
                Vector2 liquidSeed = bowlMouth - outward * .45f;
                Check(!shaker.HasCap || (!FluidExperimentCollisionProfile.Contains(shaker.collisionProfile.lid,
                    shaker.WorldToLocal(liquidSeed)) && FluidExperimentCollisionProfile.EdgeDistance(shaker.collisionProfile.lid,
                    shaker.WorldToLocal(liquidSeed)) * Mathf.Abs(shaker.transform.lossyScale.x) > gpu.Radius),
                    label + " initial liquid disk is clear of the assembled lid solid");
                Emit(liquidSeed, outward * 4, shaker.Id);
                bool escaped = false;
                int firstExitStep = -1;
                Vector2 firstExitPosition = Vector2.zero;
                for (int step = 0; step < 30; step++)
                {
                    Step();
                    GpuLiquidParticle p = Single(label);
                    if (p.VesselId == 0)
                    {
                        escaped = true;
                        firstExitStep = step + 1;
                        firstExitPosition = p.Position;
                        break;
                    }
                    Check(p.VesselId == shaker.Id, label + " retains its own owner until a real exit");
                }
                if (closure == FluidExperimentShakerClosure.Closed)
                {
                    Check(!escaped, label + " keeps the driven drop inside throughout 30 GPU ticks");
                    Close(gpu.VolumeIn(shaker.Id), .5, label + " retains all liquid");
                }
                else
                {
                    Check(escaped, label + " permits a driven liquid drop to leave");
                    Check(Vector2.Dot(firstExitPosition - actualOutlet, outward) >= -.005f,
                        label + " releases ownership only beyond its actual outlet plane at step " + firstExitStep);
                    Close(gpu.VolumeIn(shaker.Id), 0, label + " accounts exited liquid as active free liquid");
                    Step(3);
                }
                Accounting(label, .5, .5, 0);
                CaptureShakerEvidence(shaker, closure.ToString());
                shaker.SetHeld(false);
                shaker.Body.bodyType = RigidbodyType2D.Kinematic;
                shaker.Teleport(new Vector2(-50, 20), 0);
            }
        }
        finally
        {
            Physics2D.gravity = savedGravity;
            shaker.SetHeld(false);
            shaker.Body.bodyType = RigidbodyType2D.Kinematic;
            shaker.SetShakerClosure(FluidExperimentShakerClosure.Closed);
            shaker.Teleport(new Vector2(-50, 20), 0);
        }
    }

    private void CaptureShakerEvidence(FluidExperimentBody shaker, string closure)
    {
        Camera camera = gpu.outputCamera;
        Check(camera != null, "D shaker capture uses the authored output camera");
        GpuLiquidParticle before = Single("before shaker capture");
        RenderTexture oldTarget = camera.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        Vector3 oldPosition = camera.transform.position;
        float oldSize = camera.orthographicSize, oldInterpolation = gpu.ImprovedSurfaceInterpolationOverride;
        var target = new RenderTexture(512, 512, 24);
        try
        {
            // A manually advanced fixture has no render interpolation step. Put its displayed
            // poses at the verified completed physics poses before checking physical hull masks.
            foreach (FluidExperimentBody body in world.Items)
                body.transform.SetPositionAndRotation(new Vector3(body.Position.x, body.Position.y, body.transform.position.z),
                    Quaternion.Euler(0, 0, body.Angle));
            Physics2D.SyncTransforms();
            Bounds framed = shaker.SolidBounds;
            framed.Encapsulate(before.Position);
            camera.transform.position = new Vector3(framed.center.x, framed.center.y, -20);
            camera.orthographicSize = Mathf.Max(framed.extents.x, framed.extents.y) + .4f;
            camera.targetTexture = target;
            gpu.ImprovedSurfaceInterpolationOverride = 1;
            camera.Render();
            // All deterministic captures run in one Unity frame. This metric counts frames,
            // not Camera.Render calls, so the current-frame marker is the appropriate guard.
            Check(gpu.LastRenderedSurfaceFrame == Time.frameCount && gpu.ImprovedSurfaceReady
                && gpu.SurfaceRenderingError == null,
                "D " + closure + " capture submitted actual improved surface rendering");
            SaveTarget(target, "D-shaker-" + closure + "-scene.png", false);
            var surface = (RenderTexture)typeof(FluidExperimentGpuLiquid)
                .GetField("surfaceComposite", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gpu);
            Check(surface != null, "D " + closure + " liquid-only target exists");
            SaveTarget(surface, "D-shaker-" + closure + "-liquid.png", true);
            gpu.ReadbackNow();
            GpuLiquidParticle after = Single("after shaker capture");
            Check(after.VesselId == before.VesselId && Vector2.Distance(after.Position, before.Position) < .000001f,
                "D " + closure + " evidence capture leaves particle physics and ownership unchanged");
        }
        finally
        {
            camera.targetTexture = oldTarget;
            camera.transform.position = oldPosition;
            camera.orthographicSize = oldSize;
            gpu.ImprovedSurfaceInterpolationOverride = oldInterpolation;
            RenderTexture.active = oldActive;
            target.Release();
            Destroy(target);
        }
    }

    private void SaveTarget(RenderTexture target, string filename, bool requireVisibleLiquid)
    {
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        try
        {
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            if (requireVisibleLiquid)
                Check(texture.GetPixels().Sum(p => (double)p.a) > .01,
                    filename + " contains visible rendered liquid pixels");
            File.WriteAllBytes(Path.Combine(ExperimentModelDInteractionValidation.Evidence, filename), texture.EncodeToPNG());
            report.Add("CAPTURE " + filename + " " + target.width + "x" + target.height);
        }
        finally { Destroy(texture); }
    }

    private void Accounting(string label, double generated, double active, double retired)
    {
        Check(gpu.Ledger != null && gpu.Ledger.Revision == gpu.SnapshotRevision, label + " coherent ledger revision");
        Close(gpu.Ledger.Total.GeneratedMl, generated, label + " generated ml");
        Close(gpu.Ledger.Total.ActiveMl, active, label + " active ml");
        Close(gpu.Ledger.Total.RetiredMl, retired, label + " retired ml");
        foreach (var entry in gpu.Ledger.Ingredients.Concat(new[] { gpu.Ledger.Total }))
        {
            string name = entry.Ingredient != null ? entry.Ingredient.name : "TOTAL";
            Close(entry.ConservationErrorMl, 0, label + " " + name + " conservation error");
            Close(entry.QueueErrorMl, 0, label + " " + name + " queue error");
        }
    }

    private void Check(bool condition, string label)
    {
        checks++;
        report.Add((condition ? "PASS " : "FAIL ") + label);
        if (!condition) throw new Exception(label);
    }

    private void Close(double actual, double expected, string label)
        => Check(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) <= .003,
            label + ": actual=" + actual.ToString("G10") + " expected=" + expected.ToString("G10"));

    private void OnLog(string message, string stack, LogType type)
    {
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            errors.Add(message + "\n" + stack);
    }

    private void Update()
    {
        if (!finished && Time.realtimeSinceStartup - started > 150)
        {
            report.Add("FAIL validation exceeded 150 seconds");
            Finish(false);
        }
    }

    private void Finish(bool success)
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        if (controlled) { Physics2D.simulationMode = originalMode; Physics2D.gravity = originalGravity; }
        foreach (var body in probes)
            if (body != null) { body.gameObject.SetActive(false); Destroy(body.gameObject); }
        report.Insert(0, (success ? "PASS " : "FAIL ") + checks + " checks; mode=D; GPU=" + SystemInfo.graphicsDeviceName);
        if (errors.Count > 0) report.Add(string.Join("\n", errors));
        ExperimentModelDInteractionValidation.Finish(success, string.Join("\n", report) + "\n");
    }
}
