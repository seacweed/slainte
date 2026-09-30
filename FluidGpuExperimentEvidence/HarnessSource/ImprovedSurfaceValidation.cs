using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using Slainte.Bartending.FluidGpuExperiment;
using UnityEngine;

public static class ExperimentSurfaceValidation
{
    private const string Key = "Slainte.FluidGpuExperiment.SurfaceValidation";
    public static string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR")
        ?? "FluidGpuExperimentEvidence/surface-manual";
    [InitializeOnLoadMethod]
    private static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("SurfaceValidationRunner").AddComponent<ExperimentSurfaceValidationRunner>();
    };
    public static void Begin()
    {
        Directory.CreateDirectory(Evidence);
        File.WriteAllText(Path.Combine(Evidence, "surface-result.txt"), "STARTED; no result yet\n");
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    public static void Finish(bool success, string report)
    {
        File.WriteAllText(Path.Combine(Evidence, "surface-validation.txt"), report);
        File.WriteAllText(Path.Combine(Evidence, "surface-result.txt"), success ? "PASS\n" : "FAIL\n");
        if (success) Debug.Log(report); else Debug.LogError(report);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
    }
}

public sealed class ExperimentSurfaceValidationRunner : MonoBehaviour
{
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private int checks;
    private void OnEnable() => Application.logMessageReceived += OnLog;
    private void OnDisable() => Application.logMessageReceived -= OnLog;
    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + stack);
    }
    private void Require(bool condition, string label)
    {
        checks++; if (!condition) throw new InvalidOperationException(label); report.Add("PASS " + label);
    }
    private IEnumerator Start()
    {
        yield return null;
        var comparison = FindFirstObjectByType<FluidExperimentComparison>();
        float start = Time.realtimeSinceStartup;
        while (comparison != null && !comparison.Ready && Time.realtimeSinceStartup - start < 20) yield return null;
        IEnumerator validation = null;
        bool success = true;
        try
        {
            Require(comparison != null && comparison.Ready, "comparison initialized within 20 seconds");
            comparison.automaticScenario = false;
            validation = ExperimentImprovedSurfaceValidation.Run(comparison, ExperimentSurfaceValidation.Evidence, Require);
        }
        catch (Exception exception) { success = false; report.Add(exception.ToString()); }
        while (success)
        {
            bool more = false; object current = null;
            try
            {
                Require(errors.Count == 0, "no renderer, compute, or runtime errors");
                more = validation.MoveNext(); if (more) current = validation.Current;
            }
            catch (Exception exception) { success = false; report.Add(exception.ToString()); }
            if (!more || !success) break;
            yield return current;
        }
        (validation as IDisposable)?.Dispose();
        if (errors.Count != 0) { success = false; report.AddRange(errors); }
        report.Insert(0, (success ? "PASS " : "FAIL ") + checks + " checks; GPU=" + SystemInfo.graphicsDeviceName);
        Application.logMessageReceived -= OnLog;
        ExperimentSurfaceValidation.Finish(success, string.Join("\n", report));
    }
}

// Called by the experiment's evidence runner after shader import. It uses the actual GPU
// renderer and authored profiles; it does not start an editor or change project assets.
public static class ExperimentImprovedSurfaceValidation
{
    private const float Dt = .02f;
    private static readonly FieldInfo Composite = typeof(FluidExperimentGpuLiquid)
        .GetField("surfaceComposite", BindingFlags.Instance | BindingFlags.NonPublic);

    public static IEnumerator Run(FluidExperimentComparison comparison, string evidence, Action<bool, string> require)
    {
        Directory.CreateDirectory(evidence);
        bool oldEnabled = comparison.enabled, oldControls = comparison.showControls;
        var rows = new List<string> { "mode,fps,frame,fraction,expected_x,expected_y,observed_x,observed_y,error_world,alpha_sum" };
        try
        {
            comparison.enabled = false; comparison.showControls = false;
            foreach (FluidExperimentMode mode in new[] { FluidExperimentMode.DImprovedSurface, FluidExperimentMode.ECalibratedLiquid })
            {
                comparison.SwitchMode(mode);
                comparison.StartScenario(FluidExperimentScenario.Manual);
                FluidExperimentWorld world = comparison.World;
                FluidExperimentGpuLiquid gpu = comparison.Gpu;
                world.enabled = false; world.interactor.enabled = false;
                gpu.automaticReadback = false;
                FluidExperimentBody vessel = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
                FluidExperimentBody bottle = world.Items.First(b => b.ingredient != null);
                foreach (FluidExperimentBody body in world.Items)
                {
                    body.SetHeld(true); body.Teleport(new Vector2(-18, 10), 0); body.Body.simulated = false;
                    body.pourMlPerSecond = 0;
                }
                Physics2D.SyncTransforms();
                Camera camera = gpu.outputCamera;
                camera.transform.position = new Vector3(0, 2, -20); camera.orthographicSize = 1.5f;
                var target = new RenderTexture(256, 256, 24);
                RenderTexture oldTarget = camera.targetTexture;
                camera.targetTexture = target;
                try
                {
                    // One isolated particle follows a known full-tick path. Read start before
                    // the second tick, so accidentally using substep previousPosition fails.
                    gpu.ResetSimulation();
                    require(gpu.TryEmit(new Vector2(-.2f, 2), new Vector2(2, -.5f), bottle.ingredient, .5f, 0), mode + ": interpolation fixture accepted");
                    world.TickLiquid(Dt); gpu.ReadbackNow();
                    GpuLiquidParticle first = gpu.Snapshot.Single(p => p.Active != 0);
                    world.TickLiquid(Dt); gpu.ReadbackNow();
                    GpuLiquidParticle[] physicalBefore = gpu.Snapshot.ToArray();
                    GpuImprovedSurfaceParticle ellipse = gpu.ReadImprovedSurfaceParticles().Single(p => p.Active != 0);
                    require(gpu.ImprovedSurfaceReady && gpu.ImprovedSurfaceError == null, mode + ": improved renderer initialized");
                    require(Vector2.Distance(ellipse.StartPosition, first.Position) < .00001f
                        && Vector2.Distance(ellipse.EndPosition, physicalBefore.Single(p => p.Active != 0).Position) < .00001f,
                        mode + ": endpoints span the whole fixed tick, not the last substep");
                    foreach (int fps in new[] { 30, 60, 120, 144 })
                    {
                        for (int frame = 0; frame < 8; frame++)
                        {
                            float fraction = (frame / (float)fps / Dt) % 1;
                            gpu.ImprovedSurfaceInterpolationOverride = fraction;
                            ImageSample image = ReadSurface(gpu, camera, frame == 0 ? Path.Combine(evidence, mode + "-interpolation-" + fps + "fps.png") : null);
                            Vector2 expected = Vector2.Lerp(ellipse.StartPosition, ellipse.EndPosition, fraction);
                            float error = Vector2.Distance(expected, image.centroid);
                            float pixelSize = 2 * camera.orthographicSize / image.height;
                            require(image.alpha > .01 && error <= pixelSize * 1.5f,
                                mode + ": " + fps + "fps fraction " + F(fraction) + " renders at the interpolated GPU endpoint");
                            rows.Add(string.Join(",", mode, fps, frame, F(fraction), F(expected.x), F(expected.y),
                                F(image.centroid.x), F(image.centroid.y), F(error), F(image.alpha)));
                        }
                        yield return null;
                    }
                    gpu.ReadbackNow();
                    require(SamePhysical(physicalBefore, gpu.Snapshot), mode + ": all frame-rate render probes leave physical positions, velocities, owners and ml unchanged");

                    // A fractional particle has proportional integrated splat area even at
                    // non-circular aspect. This catches both full-drop sizing and double ml weighting.
                    gpu.ResetSimulation();
                    gpu.TryEmit(new Vector2(-.5f, 2), Vector2.zero, bottle.ingredient, .5f, 0);
                    gpu.TryEmit(new Vector2(.5f, 2), Vector2.zero, bottle.ingredient, .25f, 0);
                    world.TickLiquid(Dt); gpu.ReadbackNow();
                    GpuImprovedSurfaceParticle[] partial = gpu.ReadImprovedSurfaceParticles().Where(p => p.Active != 0).OrderBy(p => p.EndPosition.x).ToArray();
                    require(partial.Length == 2 && Mathf.Abs(IntegratedSplat(partial[1]) / IntegratedSplat(partial[0]) - .5f) < .0001f,
                        mode + ": .25ml contributes exactly half the integrated density area of .5ml");

                    // Freeze a synthetic neighborhood after genuine spawning. Only the evidence
                    // fixture changes positions; the renderer must follow covariance, not velocity.
                    gpu.ResetSimulation();
                    for (int i = 0; i < 7; i++) gpu.TryEmit(new Vector2((i - 3) * .04f, 2), Vector2.down, bottle.ingredient, .5f, 0);
                    world.TickLiquid(Dt); gpu.ReadbackNow();
                    GpuLiquidParticle[] neighborhood = gpu.Snapshot.ToArray();
                    int[] neighborhoodSlots = Enumerable.Range(0, neighborhood.Length).Where(i => neighborhood[i].Active != 0).ToArray();
                    require(neighborhoodSlots.Length == 7, mode + ": covariance fixture contains seven particles");
                    for (int i = 0; i < neighborhoodSlots.Length; i++)
                    {
                        int slot = neighborhoodSlots[i];
                        neighborhood[slot].Position = new Vector2((i - 3) * .04f, 2);
                        neighborhood[slot].Velocity = Vector2.down;
                    }
                    BuildFrozenSurface(gpu, neighborhood);
                    GpuImprovedSurfaceParticle horizontal = gpu.ReadImprovedSurfaceParticles()[neighborhoodSlots[3]];
                    require(Mathf.Abs(horizontal.Direction.x) > .99f && horizontal.Radii.x / horizontal.Radii.y > 1.2f,
                        mode + ": horizontal neighbor covariance controls anisotropy despite vertical velocity");
                    for (int i = 0; i < neighborhoodSlots.Length; i++)
                        neighborhood[neighborhoodSlots[i]].Position = new Vector2(0, 2 + (i - 3) * .04f);
                    BuildFrozenSurface(gpu, neighborhood);
                    GpuImprovedSurfaceParticle vertical = gpu.ReadImprovedSurfaceParticles()[neighborhoodSlots[3]];
                    require(Mathf.Abs(vertical.Direction.y) > .99f
                        && Mathf.Abs(IntegratedSplat(horizontal) - IntegratedSplat(vertical)) < .000001f,
                        mode + ": rotated covariance preserves integrated density area");

                    // Full-tick birth delay is visible to rendering, rather than reusing old slot history.
                    gpu.ResetSimulation();
                    Vector2 birth = new Vector2(.2f, 2.2f);
                    require(gpu.TryEmitStream(birth, Vector2.down, bottle.ingredient, .5f, bottle.Id,
                        gpu.NewPourStream(), 0, Dt * .75f, .04f, out uint token), mode + ": delayed birth accepted");
                    world.TickLiquid(Dt); gpu.ReadbackNow();
                    ellipse = gpu.ReadImprovedSurfaceParticles().Single(p => p.Active != 0);
                    require(ellipse.Token == token && Vector2.Distance(ellipse.StartPosition, birth) < .00001f
                        && Mathf.Abs(ellipse.BirthFraction - .75f) < .0001f,
                        mode + ": delayed birth records its exact nozzle origin and fraction after reset");
                    gpu.ImprovedSurfaceInterpolationOverride = .5f;
                    require(ReadSurface(gpu, camera, null).alpha < .00001, mode + ": unborn particle produces no visible density");
                    gpu.ImprovedSurfaceInterpolationOverride = .95f;
                    require(ReadSurface(gpu, camera, null).alpha > .01, mode + ": delayed particle appears after its birth fraction");

                    // Dense authored vessel boundary: the surface must clip actual glass material,
                    // while its open rim remains an opening. Physical particles are never edited.
                    vessel.SetHeld(false); vessel.Body.bodyType = RigidbodyType2D.Kinematic;
                    vessel.Teleport(Vector2.zero, 0); Physics2D.SyncTransforms();
                    gpu.ResetSimulation(); gpu.Fill(vessel, bottle.ingredient, 30);
                    for (int step = 0; step < 60; step++) world.TickLiquid(Dt);
                    gpu.ReadbackNow();
                    gpu.ImprovedSurfaceInterpolationOverride = 1;
                    Rect interior = vessel.collisionProfile.InteriorBounds;
                    Vector2 center = vessel.LocalToWorld(interior.center);
                    camera.transform.position = new Vector3(center.x, center.y, -20); camera.orthographicSize = 1.6f;
                    ImageSample walls = ReadSurface(gpu, camera, Path.Combine(evidence, mode + "-wall-mask.png"));
                    int wallPixels = 0, leaks = 0;
                    for (int y = 0; y < walls.height; y++) for (int x = 0; x < walls.width; x++)
                    {
                        Vector2 point = camera.ViewportToWorldPoint(new Vector3((x + .5f) / walls.width, (y + .5f) / walls.height, 20));
                        if (vessel.SolidClearance(point - vessel.Position) >= -.0001f) continue;
                        wallPixels++; if (walls.pixels[y * walls.width + x].a > .005f) leaks++;
                    }
                    require(wallPixels > 20 && leaks == 0 && walls.alpha > 1 && gpu.ImprovedSurfaceMaskHullCount > 0,
                        mode + ": actual authored thin-wall pixels are masked without erasing the contained liquid (" + wallPixels
                        + " wall pixels, " + leaks + " leaks, alpha=" + F(walls.alpha) + ", hulls=" + gpu.ImprovedSurfaceMaskHullCount + ")");
                    int outsideLobes = CountExteriorPixels(walls, camera, vessel);
                    require(outsideLobes == 0, mode + ": contained splats produce no disconnected density beyond the outer side or bottom walls ("
                        + outsideLobes + " exterior pixels)");

                    vessel.SetHeld(true);
                    ImageSample heldWalls = ReadSurface(gpu, camera, null);
                    int heldLeaks = 0;
                    for (int y = 0; y < heldWalls.height; y++) for (int x = 0; x < heldWalls.width; x++)
                    {
                        Vector2 point = camera.ViewportToWorldPoint(new Vector3((x + .5f) / heldWalls.width, (y + .5f) / heldWalls.height, 20));
                        if (vessel.SolidClearance(point - vessel.Position) < -.0001f && heldWalls.pixels[y * heldWalls.width + x].a > .005f) heldLeaks++;
                    }
                    require(heldLeaks == 0 && CountExteriorPixels(heldWalls, camera, vessel) == 0 && heldWalls.alpha > 1,
                        mode + ": held vessel still blocks its own liquid support through and beyond its walls");
                    Vector2[] hull = vessel.collisionProfile.solids[0].points;
                    // This profile is one concave U-shaped solid. Its vertex centroid lies
                    // inside the hollow bowl, so use a verified point in its left lip wall.
                    Vector2 wallLocal = (hull[0] + hull[hull.Length - 1]) * .5f + Vector2.down * .03f;
                    require(FluidExperimentCollisionProfile.Contains(hull, wallLocal), mode + ": foreign-ghost fixture begins inside actual solid glass");
                    Vector2 wallCenter = vessel.LocalToWorld(wallLocal);
                    gpu.ResetSimulation(); gpu.TryEmit(wallCenter, Vector2.zero, bottle.ingredient, .5f, 0); world.TickLiquid(.001f);
                    ImageSample ghost = ReadSurface(gpu, camera, null);
                    int ghostWallPixels = 0;
                    for (int y = 0; y < ghost.height; y++) for (int x = 0; x < ghost.width; x++)
                    {
                        Vector2 point = camera.ViewportToWorldPoint(new Vector3((x + .5f) / ghost.width, (y + .5f) / ghost.height, 20));
                        if (vessel.SolidClearance(point - vessel.Position) < -.0001f && ghost.pixels[y * ghost.width + x].a > .01f) ghostWallPixels++;
                    }
                    require(ghostWallPixels > 0, mode + ": held foreign vessel remains visually ghosted for free liquid");
                    vessel.SetHeld(false);

                    gpu.ResetSimulation();
                    Vector2 lip = vessel.LocalToWorld(vessel.mouthLocal);
                    gpu.TryEmit(lip + Vector2.up * .015f, Vector2.zero, bottle.ingredient, .5f, 0);
                    world.TickLiquid(.001f); gpu.ReadbackNow();
                    gpu.ImprovedSurfaceInterpolationOverride = 1;
                    ImageSample rim = ReadSurface(gpu, camera, Path.Combine(evidence, mode + "-open-rim.png"));
                    Vector3 uv = camera.WorldToViewportPoint(lip);
                    int px = Mathf.Clamp((int)(uv.x * rim.width), 0, rim.width - 1), py = Mathf.Clamp((int)(uv.y * rim.height), 0, rim.height - 1);
                    require(rim.pixels[py * rim.width + px].a > .01f, mode + ": virtual ownership rim does not mask a falling drop");

                    gpu.ResetSimulation();
                    Vector2 belowGlass = vessel.LocalToWorld(new Vector2(interior.center.x, interior.yMin - .25f));
                    gpu.TryEmit(belowGlass, Vector2.zero, bottle.ingredient, .5f, 0); world.TickLiquid(.001f);
                    ImageSample spilled = ReadSurface(gpu, camera, Path.Combine(evidence, mode + "-below-glass-spill.png"));
                    require(spilled.alpha > .01 && Vector2.Distance(spilled.centroid, belowGlass) < 2 * camera.orthographicSize / spilled.height * 1.5f,
                        mode + ": real spilled particles remain visible below the glass instead of being clipped to its interior");

                    gpu.ResetSimulation(); gpu.Fill(vessel, bottle.ingredient, 2); world.TickLiquid(Dt); gpu.ReadbackNow();
                    Vector2 shift = new Vector2(.4f, .15f);
                    gpu.SwapContents(vessel.Id, shift, 0, Vector2.zero); vessel.Teleport(vessel.Position + shift, 0);
                    gpu.ReadbackNow();
                    GpuImprovedSurfaceParticle[] rebased = gpu.ReadImprovedSurfaceParticles();
                    require(gpu.Snapshot.Select((p, i) => p.Active == 0 || (Vector2.Distance(rebased[i].StartPosition, p.Position) < .00001f
                        && Vector2.Distance(rebased[i].EndPosition, p.Position) < .00001f)).All(x => x),
                        mode + ": explicit vessel translation rebases both display endpoints without a ghost trail");
                }
                finally
                {
                    gpu.ImprovedSurfaceInterpolationOverride = -1;
                    camera.targetTexture = oldTarget; target.Release(); UnityEngine.Object.Destroy(target);
                }
            }
            File.WriteAllLines(Path.Combine(evidence, "surface-interpolation.csv"), rows);
        }
        finally
        {
            comparison.showControls = oldControls; comparison.enabled = oldEnabled;
        }
    }

    private sealed class ImageSample
    {
        public int width, height;
        public Color[] pixels;
        public double alpha;
        public Vector2 centroid;
    }
    private static ImageSample ReadSurface(FluidExperimentGpuLiquid gpu, Camera camera, string filename)
    {
        // These fixtures disable Rigidbody simulation and advance only the liquid. Teleport
        // writes the physics pose, while mask/art use the displayed Transform. Match the
        // established VolumeValidation capture synchronization for these frozen bodies.
        var world = (FluidExperimentWorld)typeof(FluidExperimentGpuLiquid)
            .GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gpu);
        foreach (FluidExperimentBody body in world.Items)
            if (!body.Body.simulated) body.transform.SetPositionAndRotation(
                new Vector3(body.Position.x, body.Position.y, body.transform.position.z), Quaternion.Euler(0, 0, body.Angle));
        Physics2D.SyncTransforms();
        camera.Render();
        var target = (RenderTexture)Composite.GetValue(gpu);
        if (target == null) throw new InvalidOperationException("No improved surface output was rendered: " + gpu.ImprovedSurfaceError);
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        try
        {
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            Color[] pixels = texture.GetPixels();
            var result = new ImageSample { width = target.width, height = target.height, pixels = pixels };
            double xSum = 0, ySum = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                double alpha = pixels[i].a;
                result.alpha += alpha; xSum += (i % target.width + .5) * alpha; ySum += (i / target.width + .5) * alpha;
            }
            if (result.alpha > .0000001) result.centroid = camera.ViewportToWorldPoint(new Vector3(
                (float)(xSum / result.alpha / target.width), (float)(ySum / result.alpha / target.height), 20));
            if (filename != null)
            {
                var png = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
                try { png.SetPixels(pixels); png.Apply(); File.WriteAllBytes(filename, png.EncodeToPNG()); }
                finally { UnityEngine.Object.Destroy(png); }
            }
            return result;
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(texture); }
    }
    private static float IntegratedSplat(GpuImprovedSurfaceParticle p) => p.Radii.x * p.Radii.y * p.CoverageWeight;
    private static int CountExteriorPixels(ImageSample image, Camera camera, FluidExperimentBody vessel)
    {
        int visible = 0;
        Vector2[] interior = vessel.collisionProfile.interior;
        float rim = Mathf.Min(interior[0].y, interior[interior.Length - 1].y);
        for (int y = 0; y < image.height; y++) for (int x = 0; x < image.width; x++)
        {
            if (image.pixels[y * image.width + x].a <= .005f) continue;
            Vector2 point = camera.ViewportToWorldPoint(new Vector3((x + .5f) / image.width, (y + .5f) / image.height, 20));
            Vector2 local = vessel.transform.InverseTransformPoint(point);
            if (local.y < rim && !FluidExperimentCollisionProfile.Contains(interior, local)) visible++;
        }
        return visible;
    }
    private static void BuildFrozenSurface(FluidExperimentGpuLiquid gpu, GpuLiquidParticle[] particles)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = typeof(FluidExperimentGpuLiquid);
        ((GraphicsBuffer)type.GetField("particleBuffer", hidden).GetValue(gpu)).SetData(particles);
        type.GetMethod("ResetImprovedSurfaceHistory", hidden).Invoke(gpu, null);
        type.GetMethod("BeginImprovedSurfaceTick", hidden).Invoke(gpu, new object[] { Dt });
        type.GetMethod("RebuildGrid", hidden).Invoke(gpu, null);
        type.GetMethod("EndImprovedSurfaceTick", hidden).Invoke(gpu, new object[] { Dt });
    }
    private static bool SamePhysical(GpuLiquidParticle[] a, GpuLiquidParticle[] b) => a.Length == b.Length && a.Zip(b, (x, y) =>
        x.Active == y.Active && x.VesselId == y.VesselId && x.VolumeMl == y.VolumeMl && x.Position == y.Position
        && x.PreviousPosition == y.PreviousPosition && x.Velocity == y.Velocity).All(x => x);
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
