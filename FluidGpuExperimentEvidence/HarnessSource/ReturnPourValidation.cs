using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

// Queued births expose inherited velocity before pressure/gravity can conceal a bad impulse.
// Every inspected batch then runs on the real D GPU and checks its quantity ledger.
public static class ExperimentReturnPourChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object obj, string name) => obj.GetType().GetField(name, Private | BindingFlags.Public).GetValue(obj);
    private static void Call(object obj, string name, params object[] args)
        => obj.GetType().GetMethod(name, Private).Invoke(obj, args);

    public static void Run(FluidExperimentWorld world, Action<bool, string> check)
    {
        var hand = world.interactor;
        // The old tangential-impulse threshold was calibrated on this long-lever
        // bottle. Awake/registration order must not substitute a different spout/pivot.
        var bottle = world.Items.First(x => x.kind == LabItemKind.Bottle && x.name == "Bottle_item_1002");
        var gpu = world.Liquid;
        var evidence = new List<string> { "angle,dt,moving,births,maxResidualTangent,oldReturnTangentPeak" };
        int allBirths = 0;
        float oldPeak = 0;
        foreach (float startAngle in new[] { 95f, 135f, 179f, -135f, 855f })
        foreach (float dt in new[] { .01f, .02f, 1f / 30 })
        foreach (bool moving in new[] { false, true })
        {
            hand.ReleaseWithVelocity(Vector2.zero);
            gpu.ResetSimulation();
            bottle.Teleport(new Vector2(0, 2), 0);
            bottle.ResetSupply(700, 0); bottle.pourMlPerSecond = 20;
            hand.Pick(bottle, bottle.Position);
            hand.BeginRotation(); hand.RotateBy(startAngle); Call(bottle, "ApplyHeldPose");
            check(Vector2.Distance(hand.RotationPointerWorld, bottle.LocalToWorld(bottle.rotationPivotLocal)) < .001f,
                "Rotation cursor target follows the actual pivot, including boundary constraints");
            bottle.SynchronizeHistory();
            hand.EndRotation(Vector2.zero);
            Vector2 userVelocity = moving ? new Vector2(1.25f, -.35f) : Vector2.zero;
            float elapsed = 0, tangentPeak = 0, oldCasePeak = 0;
            int births = 0;
            for (int tick = 0; tick < Mathf.CeilToInt(hand.uprightReturnDuration / dt) + 3; tick++)
            {
                // Two render updates per tick exercise accumulated restoration, including its final frame.
                for (int frame = 0; frame < 2; frame++)
                {
                    elapsed += dt * .5f;
                    if (hand.Returning) hand.AdvanceUprightReturn(dt * .5f, userVelocity * elapsed);
                    else hand.MoveHeld(userVelocity * elapsed);
                }
                Call(bottle, "ApplyHeldPose"); Call(bottle, "CaptureMotion"); Call(bottle, "Emit", dt);
                int pending = (int)Field(gpu, "pendingSpawnCount");
                var commands = (Array)Field(gpu, "spawnCommands");
                var streams = (GpuLiquidStreamParticle[])Field(gpu, "spawnStreams");
                for (int i = 0; i < pending; i++)
                {
                    object command = commands.GetValue(i);
                    Vector2 velocity = (Vector2)Field(command, "Velocity");
                    float fraction = streams[i].Delay / dt;
                    float angle = bottle.PreviousAngle + bottle.StepAngle * fraction;
                    Vector2 direction = FluidExperimentBody.Rotate(bottle.ExitDirectionLocal, angle);
                    Vector2 side = new Vector2(-direction.y, direction.x);
                    float tangent = Mathf.Abs(Vector2.Dot(velocity - userVelocity, side));
                    tangentPeak = Mathf.Max(tangentPeak, tangent);
                    check(tangent < .004f, "Return birth preserves mouse translation without automatic tangential velocity");
                    check(Vector2.Dot(velocity - userVelocity, direction) >= 0,
                        "Return birth still exits in the current nozzle direction");
                    Vector2 nozzle = (Vector2)Field(command, "Position");
                    Vector2 arm = nozzle - Vector2.Lerp(bottle.PreviousPosition, bottle.Position, fraction);
                    oldCasePeak = Mathf.Max(oldCasePeak, Mathf.Abs(Vector2.Dot(
                        new Vector2(-arm.y, arm.x) * (bottle.StepAngle * Mathf.Deg2Rad / dt), side)));
                    births++;
                }
                gpu.Step(dt); gpu.ReadbackNow(); bottle.SynchronizeHistory();
                check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .003,
                    "Real D GPU conserves liquid after return births");
                check(Math.Abs((700 - bottle.remainingMl) - gpu.EmittedMl) < .003,
                    "Bottle debit equals admitted liquid during return");
                check(gpu.Snapshot.Where(p => p.Active != 0).All(p => float.IsFinite(p.Velocity.x)
                    && float.IsFinite(p.Position.x) && float.IsFinite(p.Position.y)), "Return GPU state stays finite");
            }
            if (Mathf.Abs(startAngle) != 95) check(births > 0, "Pouring return case emits actual particles");
            allBirths += births; oldPeak = Mathf.Max(oldPeak, oldCasePeak);
            evidence.Add($"{startAngle},{dt},{moving},{births},{tangentPeak},{oldCasePeak}");
        }
        File.WriteAllLines(Path.Combine(ExperimentInputShakerValidation.Evidence, "return-births.csv"), evidence);
        check(allBirths > 15 && oldPeak > 10, $"Regression exercised births where the old return path added a large tangential impulse; bottle={bottle.name}, births={allBirths}, oldPeak={oldPeak:R}");

        // Manual angular motion remains physical. Do not cure the return by zeroing all rotation.
        hand.ReleaseWithVelocity(Vector2.zero); bottle.Teleport(new Vector2(0, 2), 0);
        hand.Pick(bottle, bottle.Position); hand.BeginRotation(); hand.RotateBy(135);
        Call(bottle, "ApplyHeldPose"); bottle.SynchronizeHistory();
        hand.RotateBy(5); Call(bottle, "ApplyHeldPose"); Call(bottle, "CaptureMotion");
        Vector2 armProbe = new Vector2(0, 1);
        Vector2 inherited = (Vector2)bottle.GetType().GetMethod("EmissionPointVelocity", Private).Invoke(bottle, new object[] { armProbe, .02f });
        Vector2 expected = (bottle.Position - bottle.PreviousPosition) / .02f + Vector2.left * (5 * Mathf.Deg2Rad / .02f);
        check(Vector2.Distance(inherited, expected) < .001f, "Manual rotation still transfers its tangential velocity");
        hand.ReleaseWithVelocity(Vector2.zero); bottle.pourMlPerSecond = 0; bottle.Teleport(new Vector2(-30, 20), 0);
        gpu.ResetSimulation();
        ValidateBoundaryReturn(world, check);
        ValidateBucket(world, check);
    }

    private static void ValidateBoundaryReturn(FluidExperimentWorld world, Action<bool, string> check)
    {
        var hand = world.interactor; var gpu = world.Liquid;
        foreach (var bottle in world.Items.Where(x => x.kind == LabItemKind.Bottle).ToArray())
        foreach (Vector2 edge in new[] { new Vector2(100, 2), new Vector2(-100, 2), new Vector2(0, 100) })
        foreach (bool moving in new[] { false, true })
        {
            hand.ReleaseWithVelocity(Vector2.zero); gpu.ResetSimulation();
            bottle.Teleport(new Vector2(0, 2), 0); bottle.ResetSupply(700, 0); bottle.pourMlPerSecond = 20;
            hand.Pick(bottle, bottle.Position); hand.BeginRotation(); hand.RotateBy(179);
            Call(bottle, "ApplyHeldPose"); bottle.SetHeldPose(edge, 179); Call(bottle, "ApplyHeldPose");
            Vector2 start = bottle.Position;
            bottle.SynchronizeHistory(); hand.EndRotation(Vector2.zero);
            Vector2 userVelocity = !moving ? Vector2.zero : edge.y > 50 ? Vector2.right : Vector2.up;
            float elapsed = 0;
            bool moved = false; int births = 0;
            for (int tick = 0; tick < 10; tick++)
            {
                for (int frame = 0; frame < 2; frame++)
                {
                    elapsed += .01f;
                    if (hand.Returning) hand.AdvanceUprightReturn(.01f, userVelocity * elapsed);
                    else hand.MoveHeld(userVelocity * elapsed);
                }
                Call(bottle, "ApplyHeldPose"); Call(bottle, "CaptureMotion"); Call(bottle, "Emit", .02f);
                moved |= Vector2.Distance(start, bottle.Position) > .03f;
                Vector2 inherited = (Vector2)bottle.GetType().GetMethod("EmissionPointVelocity", Private)
                    .Invoke(bottle, new object[] { Vector2.zero, .02f });
                check(Vector2.Distance(inherited, userVelocity) < .002f,
                    $"{bottle.name} return at {edge} preserves user velocity {userVelocity} without automatic clamp motion (actual={inherited:F5})");
                int pending = (int)Field(gpu, "pendingSpawnCount");
                var commands = (Array)Field(gpu, "spawnCommands");
                var streams = (GpuLiquidStreamParticle[])Field(gpu, "spawnStreams");
                for (int i = 0; i < pending; i++)
                {
                    object command = commands.GetValue(i);
                    Vector2 velocity = (Vector2)Field(command, "Velocity");
                    float angle = bottle.PreviousAngle + bottle.StepAngle * streams[i].Delay / .02f;
                    Vector2 direction = FluidExperimentBody.Rotate(bottle.ExitDirectionLocal, angle);
                    check(Mathf.Abs(Vector2.Dot(velocity - userVelocity, new Vector2(-direction.y, direction.x))) < .004f,
                        "Boundary-return birth has no artificial sideways launch");
                    births++;
                }
                gpu.Step(.02f); gpu.ReadbackNow(); bottle.SynchronizeHistory();
                check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .003,
                    "Boundary-return GPU dispatch conserves liquid");
            }
            check(moved && births > 0, "Boundary-return fixture actually moves the constrained bottle and emits liquid");
            hand.ReleaseWithVelocity(Vector2.zero); bottle.pourMlPerSecond = 0;
            bottle.Teleport(new Vector2(-30, 20), 0);
        }
        gpu.ResetSimulation();
    }

    private static void ValidateBucket(FluidExperimentWorld world, Action<bool, string> check)
    {
        var bucket = world.Items.First(x => x.kind == LabItemKind.IceBucket);
        check(bucket.iceStockRenderer != null && bucket.iceStockSprites.Length == 7
            && bucket.iceStockSprites.All(x => x != null), "Authored bucket wires all seven stock sprites and its front renderer");
        bucket.Teleport(new Vector2(0, 3), 0); bucket.ResetSupply(0, 20);
        var visited = new HashSet<int>();
        for (int stock = 20; stock >= 0; stock--)
        {
            int expected = stock == 20 ? 6 : stock == 0 ? 0 : stock / 4 + 1;
            check(bucket.iceStock == stock && bucket.iceStockRenderer.sprite == bucket.iceStockSprites[expected],
                "Bucket front matches actual remaining ice: " + stock);
            if (visited.Add(expected)) CaptureBucket(world, bucket, expected);
            if (stock == 0) break;
            bucket.SetHeld(true); bucket.Teleport(new Vector2(0, 3), 180);
            Call(bucket, "Emit", bucket.icePourInterval + .001f);
            var ice = world.Items.Last();
            check(ice.kind == LabItemKind.Ice && bucket.iceStock == stock - 1,
                "Actual bucket emission updates stock and display immediately");
            ice.gameObject.SetActive(false); UnityEngine.Object.Destroy(ice.gameObject);
            bucket.Teleport(new Vector2(0, 3), 0);
        }
        check(visited.Count == 7 && !bucket.IsVessel, "All seven visual states retain the solid stock-source contract");
        bucket.ResetSupply(0, 20);
        check(bucket.iceStockRenderer.sprite == bucket.iceStockSprites[6], "Reset restores the full bucket display");
        bucket.SetHeld(false); bucket.Teleport(new Vector2(-40, 20), 0);
    }

    private static void CaptureBucket(FluidExperimentWorld world, FluidExperimentBody bucket, int index)
    {
        var camera = world.interactor.inputCamera;
        Vector3 oldPosition = camera.transform.position;
        float oldSize = camera.orthographicSize;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var target = new RenderTexture(400, 500, 24); target.Create();
        var texture = new Texture2D(400, 500, TextureFormat.RGBA32, false);
        try
        {
            bucket.transform.SetPositionAndRotation(bucket.Position, Quaternion.Euler(0, 0, bucket.Angle));
            camera.transform.position = new Vector3(bucket.Position.x, bucket.Position.y, -20);
            camera.orthographicSize = 2.4f; camera.targetTexture = target; camera.Render();
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 400, 500), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(ExperimentInputShakerValidation.Evidence, "bucket-stock-" + index + ".png"), texture.EncodeToPNG());
        }
        finally
        {
            camera.transform.position = oldPosition; camera.orthographicSize = oldSize;
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(texture);
        }
    }
}
