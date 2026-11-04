using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Slainte.Bartending.PhysicsLab.Editor
{
    public sealed partial class PhysicsLabValidationRunner
    {
        private void ValidateContainedStreams(ItemDef ingredient)
        {
            var fixture = new GameObject("ContainedStreamFixture");
            fixture.transform.SetParent(world.transform, false);
            var vessel = fixture.AddComponent<PhysicsLabBody>();
            vessel.kind = LabItemKind.Glass;
            vessel.liquidWall = new[] { new Vector2(-.6f, 1), new Vector2(-.6f, -1.5f),
                new Vector2(.6f, -1.5f), new Vector2(.6f, 1) };
            vessel.contentRegions = new[] { new Rect(-.6f, -1.5f, 1.2f, 2.5f) };
            vessel.SetHeld(true);
            vessel.Teleport(new Vector2(10, 5), 0);
            Camera camera = gpu.outputCamera;
            Vector3 cameraPosition = camera.transform.position;
            float cameraSize = camera.orthographicSize;
            Color cameraBackground = camera.backgroundColor;
            try
            {
                gpu.ResetSimulation();
                uint stream = gpu.NewPourStream();
                gpu.TryEmitStream(new Vector2(10, 5.84f), Vector2.zero, ingredient, .5f, 101, stream, 0, 0, .055f, out uint older);
                gpu.Step(.02f);
                gpu.TryEmitStream(new Vector2(10, 6.16f), Vector2.zero, ingredient, .5f, 101, stream, older, 0, .055f, out uint newer);
                gpu.Step(.0001f); gpu.ReadbackNow();
                var metadata = gpu.ReadStreamParticles();
                int oldIndex = Array.FindIndex(metadata, x => x.Token == older);
                int newIndex = Array.FindIndex(metadata, x => x.Token == newer);
                Require(gpu.Snapshot[oldIndex].VesselId == vessel.Id && metadata[oldIndex].Detached == 0
                    && gpu.Snapshot[newIndex].VesselId == 0, "Crossing the virtual rim assigns ownership without detaching the stream");
                Require((gpu.ReadStreamSegments()[newIndex].B - gpu.Snapshot[oldIndex].Position).sqrMagnitude < .000001f,
                    "Stream connection crosses the open rim from outside to inside");

                gpu.ResetSimulation(); stream = gpu.NewPourStream();
                gpu.TryEmitStream(new Vector2(10, 5), Vector2.zero, ingredient, .5f, 101, stream, 0, 0, .055f, out older);
                gpu.Step(.02f);
                gpu.TryEmitStream(new Vector2(10, 5.32f), Vector2.zero, ingredient, .5f, 101, stream, older, 0, .055f, out newer);
                gpu.Step(.0001f); gpu.ReadbackNow();
                var before = gpu.Snapshot.ToArray(); metadata = gpu.ReadStreamParticles();
                Require(metadata.Where(x => x.Token != 0).All(x => x.Detached == 0),
                    "Sparse particles falling inside a glass are not mistaken for pooled liquid");
                camera.transform.position = new Vector3(10, 5.16f, -20); camera.orthographicSize = .5f; camera.backgroundColor = Color.black;
                gpu.useStreamRendering = false;
                Color32[] dots = CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory, "contained-before.png"));
                gpu.useStreamRendering = true;
                Color32[] joined = CaptureCamera(Path.Combine(PhysicsLabValidator.EvidenceDirectory, "contained-after.png"));
                Require(dots[450 * 1600 + 800].b < 25 && joined[450 * 1600 + 800].b > 70,
                    "Camera pixels show a continuous stream instead of separated balls inside the glass");
                gpu.ReadbackNow();
                Require(before.Where((p, i) => p.Position != gpu.Snapshot[i].Position || p.Velocity != gpu.Snapshot[i].Velocity
                    || p.VesselId != gpu.Snapshot[i].VesselId || p.VolumeMl != gpu.Snapshot[i].VolumeMl).Count() == 0,
                    "Contained stream rendering preserves positions, velocities, ownership and volume");

                gpu.ResetSimulation(); stream = gpu.NewPourStream();
                Vector2 pool = new Vector2(10, 4.4f);
                gpu.TryEmit(pool, Vector2.zero, ingredient, .5f, vessel.Id);
                gpu.TryEmitStream(pool + Vector2.up * gpu.Radius * 1.9f, Vector2.zero, ingredient, .5f, 101, stream, 0, 0, .055f, out older);
                gpu.Step(.02f); gpu.ReadbackNow(); metadata = gpu.ReadStreamParticles();
                oldIndex = Array.FindIndex(metadata, x => x.Token == older);
                Require(metadata[oldIndex].Detached == 2, "Contact with existing liquid absorbs a falling stream particle");
                Vector2 endpoint = gpu.Snapshot[oldIndex].Position;
                gpu.TryEmitStream(endpoint + Vector2.up * .32f, Vector2.zero, ingredient, .5f, 101, stream, older, 0, .055f, out newer);
                gpu.Step(.0001f); gpu.ReadbackNow(); metadata = gpu.ReadStreamParticles();
                newIndex = Array.FindIndex(metadata, x => x.Token == newer);
                var segments = gpu.ReadStreamSegments();
                Require(metadata[newIndex].Detached == 0 && segments[oldIndex].Active == 0
                    && (segments[newIndex].B - gpu.Snapshot[oldIndex].Position).sqrMagnitude < .000001f,
                    "Falling stream ends on the liquid surface without drawing a ribbon through pooled particles");
                Require(Mathf.Abs(gpu.SnapshotTotalMl - 1.5f) < .00001f,
                    "Joining the liquid surface keeps all physical particle volume");

                gpu.ResetSimulation(); stream = gpu.NewPourStream();
                // Both particles belong to this held vessel, so its own wall still collides.
                gpu.TryEmitStream(new Vector2(10.59f, 5), Vector2.zero, ingredient, .5f, 101, stream, 0, 0, .055f, out older);
                gpu.Step(.02f); metadata = gpu.ReadStreamParticles();
                oldIndex = Array.FindIndex(metadata, x => x.Token == older);
                Require(metadata[oldIndex].Detached == 1, "Actual inner-wall contact still breaks the stream inside a held glass");
            }
            finally
            {
                gpu.useStreamRendering = true;
                camera.transform.position = cameraPosition; camera.orthographicSize = cameraSize; camera.backgroundColor = cameraBackground;
                fixture.SetActive(false); Destroy(fixture); gpu.ResetSimulation();
            }
        }
    }
}
