using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Slainte.Bartending.PhysicsLab;
using Slainte.Bartending.PhysicsLab.Editor;

public sealed partial class BoundaryBenchmarkRunner
{
    void RunDifferential()
    {
        var report = new List<string>();
        foreach (int scenario in new[] { 0, 1, 2, 3 })
        {
            // Real profiles; move through large distances and unwrapped turns, then
            // exercise sparse, re-registered IDs and bodies with no ownership contour.
            foreach (var body in world.Items.ToArray())
            {
                body.SynchronizeHistory();
                if (scenario > 0)
                {
                    body.SetHeld(true);
                    if (body.kind == LabItemKind.Shaker) body.SetSealed(true);
                    body.SetHeldPose(body.Position + new Vector2(.7f, -.3f), body.Angle + (scenario == 1 ? 855 : -410));
                    body.GetType().GetMethod("ApplyHeldPose", Private).Invoke(body, null);
                    body.GetType().GetMethod("CaptureMotion", Private).Invoke(body, null);
                }
            }
            if (scenario == 3)
            {
                var body = world.Items[0];
                for (int i = 0; i < 300; i++) { world.Unregister(body); world.Register(body); }
            }
            Seed(1000);
            var random = new System.Random(4107 + scenario);
            float R(float a, float b) => a + (b - a) * (float)random.NextDouble();
            var offsets = new Vector2[particles.Length];
            for (int i = 0; i < particles.Length; i++)
            {
                var body = world.Items[i % world.Items.Count];
                Vector2 p = body.Position + new Vector2(R(-2, 2), R(-2, 2));
                particles[i] = new GpuLiquidParticle { Position = p,
                    PreviousPosition = p + new Vector2(R(-5, 5), R(-5, 5)),
                    Velocity = new Vector2(R(-4, 4), R(-4, 4)), VolumeMl = .5f, Active = 1,
                    VesselId = i % 5 == 0 ? 0u : i % 7 == 0 ? 999999u : body.Id };
                offsets[i] = new Vector2(R(-1, 1), R(-1, 1)) * (i % 10 == 0 ? 10 : .2f);
                streams[i].StepDt = .01f; streams[i].StartFraction = (i % 4) * .25f;
            }
            Call("UploadGeometry", 0f, 1f, .01f);
            foreach (string kernelName in new[] { "SweepBoundaries", "ApplyDeltaAndBoundaries", "CalculateDensityLambda", "CalculatePositionDelta", "UpdateVelocities", "BuildStreamSurface" })
            {
                GpuLiquidParticle[] referenceParticles = null;
                Vector2[] referenceDeltas = null;
                float[] referenceLambdas = null;
                GpuLiquidStreamSegment[] referenceSegments = null;
                float largest = 0;
                for (int mode = 0; mode < 2; mode++)
                {
                    if (mode == 0) shader.EnableKeyword("PHYSICSLAB_LINEAR_BOUNDARIES");
                    else shader.DisableKeyword("PHYSICSLAB_LINEAR_BOUNDARIES");
                    Restore();
                    ((GraphicsBuffer)Field("positionDeltaBuffer")).SetData(offsets);
                    ((GraphicsBuffer)Field("lambdaBuffer")).SetData(new float[particles.Length]);
                    ((GraphicsBuffer)Field("velocitySnapshotBuffer")).SetData(particles.Select(p => p.Velocity).ToArray());
                    if (kernelName == "BuildStreamSurface") Call("PrepareStreamSurface", new object[] { null });
                    else shader.Dispatch(shader.FindKernel(kernelName), particles.Length / 64, 1, 1);
                    var actual = new GpuLiquidParticle[particles.Length];
                    var deltas = new Vector2[particles.Length]; var lambdas = new float[particles.Length];
                    var segments = new GpuLiquidStreamSegment[particles.Length + 64];
                    ((GraphicsBuffer)Field("particleBuffer")).GetData(actual);
                    ((GraphicsBuffer)Field("positionDeltaBuffer")).GetData(deltas);
                    ((GraphicsBuffer)Field("lambdaBuffer")).GetData(lambdas);
                    ((GraphicsBuffer)Field("streamSegmentBuffer")).GetData(segments);
                    if (mode == 0) { referenceParticles = actual; referenceDeltas = deltas; referenceLambdas = lambdas; referenceSegments = segments; continue; }
                    for (int i = 0; i < particles.Length; i++)
                    {
                        float error = Mathf.Max(Vector2.Distance(actual[i].Position, referenceParticles[i].Position),
                            Vector2.Distance(actual[i].Velocity, referenceParticles[i].Velocity),
                            Vector2.Distance(deltas[i], referenceDeltas[i]), Mathf.Abs(lambdas[i] - referenceLambdas[i]));
                        largest = Mathf.Max(largest, error);
                        if (float.IsNaN(error) || float.IsInfinity(error) || error > .002f || actual[i].VesselId != referenceParticles[i].VesselId)
                            throw new Exception($"Differential {scenario}/{kernelName}/{i}: error={error}, owner={actual[i].VesselId}/{referenceParticles[i].VesselId}");
                    }
                    if (kernelName == "BuildStreamSurface")
                    for (int i = 0; i < segments.Length; i++)
                        if (segments[i].Active != referenceSegments[i].Active || Vector2.Distance(segments[i].A, referenceSegments[i].A) > .0001f || Vector2.Distance(segments[i].B, referenceSegments[i].B) > .0001f)
                            throw new Exception($"Stream differential {scenario}/{i}");
                }
                report.Add($"PASS: scenario={scenario} kernel={kernelName} particles={particles.Length} maxAbsoluteError={largest:R}");
                File.WriteAllLines(Path.Combine(PhysicsLabValidator.EvidenceDirectory, "differential.txt"), report);
            }
        }
        // Empty geometry after unregistering every body must not read old group data.
        foreach (var body in world.Items.ToArray()) world.Unregister(body);
        gpu.Step(.02f); gpu.ReadbackNow();
        if (gpu.Snapshot.Any(p => float.IsNaN(p.Position.x) || float.IsNaN(p.Position.y))) throw new Exception("Empty geometry produced NaN");
        report.Add("PASS: empty geometry after unregistering all bodies");
        File.WriteAllLines(Path.Combine(PhysicsLabValidator.EvidenceDirectory, "differential.txt"), report);
    }
}
