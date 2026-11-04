using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

public static class ExperimentIceBucketChecks
{
    public static void Run(FluidExperimentWorld source, Action<bool, string> require)
    {
        var sourceBucket = source.Items.First(b => b.kind == LabItemKind.IceBucket);
        var sourceGlass = source.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
        require(!sourceBucket.IsVessel && sourceBucket.contentRegions.Length == 0
            && sourceBucket.collisionProfile.interior.Length == 0,
            "Bucket is a stock source without any liquid interior or ownership trigger");
        var profile = sourceBucket.collisionProfile;
        Vector2 exteriorCenter = FluidExperimentCollisionProfile.BoundsOf(profile.solids[0].points).center;
        require(FluidExperimentCollisionProfile.Contains(profile.solids[0].points, exteriorCenter),
            "Bucket exterior is a filled solid silhouette, not a hollow trapping shell");

        var root = new GameObject("Model D ice and bucket fixtures");
        var world = root.AddComponent<FluidExperimentWorld>();
        world.enabled = false; world.seedLiquids = false; world.showControls = false;
        Vector2 previousGravity = Physics2D.gravity;
        SimulationMode2D previousMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.gravity = Vector2.zero;
        try
        {
            var vessel = UnityEngine.Object.Instantiate(sourceGlass, new Vector3(80, 80), Quaternion.identity, root.transform);
            vessel.Body.simulated = true; vessel.SetHeld(false); vessel.SetSealed(false);
            vessel.Body.bodyType = RigidbodyType2D.Kinematic;
            var ice = UnityEngine.Object.Instantiate(sourceBucket.icePrefab, vessel.Position, Quaternion.identity, root.transform);
            ice.Body.simulated = true; ice.Body.gravityScale = 1;
            Vector2 localCenter = vessel.collisionProfile.InteriorBounds.center;
            ice.Teleport(vessel.LocalToWorld(localCenter), 0);
            Physics2D.SyncTransforms(); world.RefreshIceContainment();
            require(ice.ContainingVesselId == vessel.Id, "Ice initially inside the real glass acquires that glass before pickup");
            vessel.SetHeld(true);
            require(vessel.solidColliders.Where(c => c != null && c.enabled).All(c =>
                ice.solidColliders.Where(d => d != null && d.enabled).All(d => !Physics2D.GetIgnoreCollision(c, d))),
                "Held glass keeps every enabled collider contact with its contained ice");
            require(ice.Body.bodyType == RigidbodyType2D.Dynamic && ice.Body.gravityScale == 1,
                "Contained ice remains a dynamic Rigidbody with gravity");

            var outside = UnityEngine.Object.Instantiate(sourceBucket.icePrefab, new Vector3(87, 80), Quaternion.identity, root.transform);
            Physics2D.SyncTransforms(); world.RefreshIceContainment();
            require(outside.ContainingVesselId == 0 && vessel.solidColliders.Where(c => c != null && c.enabled).All(c =>
                outside.solidColliders.Where(d => d != null && d.enabled).All(d => Physics2D.GetIgnoreCollision(c, d))),
                "External ice retains the held vessel collision isolation");

            Vector2 relative = vessel.WorldToLocal(ice.Position);
            Vector2 velocity = new Vector2(.15f, -.1f);
            ice.Body.linearVelocity = velocity; ice.Body.angularVelocity = 17;
            vessel.SetHeldPose(new Vector2(83, 82), 725);
            world.SendMessage("FixedUpdate");
            require(Vector2.Distance(vessel.WorldToLocal(ice.Position), relative) < .0001f,
                "Fast translation and multiple unwrapped turns transport internal ice across the full cursor pose change");
            require(Vector2.Distance(ice.Body.linearVelocity, velocity) < .0001f && Mathf.Abs(ice.Body.angularVelocity - 17) < .0001f,
                "Cursor transport preserves free ice velocity and spin instead of freezing contents");
            Physics2D.SyncTransforms(); Physics2D.Simulate(.02f); world.RefreshIceContainment();
            require(Vector2.Distance(vessel.WorldToLocal(ice.Position), relative) > .001f,
                "Contained ice still moves independently during actual Physics2D simulation");

            ice.Body.position = vessel.LocalToWorld(localCenter + Vector2.right * 5);
            ice.Body.linearVelocity = FluidExperimentBody.Rotate(Vector2.right * 100, vessel.Angle);
            world.RefreshIceContainment();
            require(ice.ContainingVesselId == vessel.Id && vessel.ContainsLiquid(ice.Position),
                "Swept interior protection intercepts a fast relative side-wall crossing");

            ResetInside(world, vessel, ice, localCenter);
            vessel.SetHeld(true);
            Vector2 mouth = vessel.LocalToWorld(vessel.mouthLocal);
            Vector2 outward = FluidExperimentBody.Rotate(Vector2.up, vessel.Angle);
            ice.Body.position = mouth + outward;
            ice.Body.linearVelocity = outward * 8;
            world.RefreshIceContainment();
            require(ice.ContainingVesselId == 0 && Vector2.Distance(ice.Position, mouth + outward) < .001f,
                "Ice can leave an open mouth without being snapped back into the held glass");

            ResetInside(world, vessel, ice, localCenter);
            vessel.SetHeld(true); vessel.sealedVessel = true;
            mouth = vessel.LocalToWorld(vessel.mouthLocal);
            ice.Body.position = mouth + Vector2.up;
            ice.Body.linearVelocity = Vector2.up * 100;
            world.RefreshIceContainment();
            require(ice.ContainingVesselId == vessel.Id && vessel.ContainsLiquid(ice.Position),
                "A blocked ice mouth retains fast upward ice while an open mouth releases it");

            ResetInside(world, vessel, ice, localCenter);
            vessel.SetHeld(true); vessel.SetHeldPose(vessel.Position, 180); world.SendMessage("FixedUpdate");
            Physics2D.gravity = new Vector2(0, -9.81f);
            for (int step = 0; step < 100 && ice.ContainingVesselId != 0; step++)
            {
                Physics2D.SyncTransforms(); Physics2D.Simulate(.02f); world.RefreshIceContainment();
            }
            require(ice.ContainingVesselId == 0,
                "Gravity pours dynamic ice out of an inverted held glass during actual Physics2D steps");
            Physics2D.gravity = Vector2.zero;

            var bucket = UnityEngine.Object.Instantiate(sourceBucket, new Vector3(70, 80), Quaternion.identity, root.transform);
            bucket.contentRegions = new[] { new Rect(-1, -1, 2, 2) };
            bucket.ApplyCollisionProfile();
            require(!bucket.IsVessel && bucket.contentRegions.Length == 0,
                "Applying a stock-source profile clears stale serialized vessel regions");
            bucket.SetHeld(true); bucket.Teleport(new Vector2(70, 80), 180);
            int stock = bucket.iceStock, count = world.Items.Count;
            world.TickLiquid(bucket.icePourInterval + .001f);
            require(bucket.iceStock == stock - 1 && world.Items.Count == count + 1,
                "Solid bucket still emits one physical ice cube and deducts one stock unit");
            var emitted = world.Items.Last();
            require(emitted.kind == LabItemKind.Ice && emitted.ContainingVesselId == 0
                && !bucket.solidColliders.Any(c => c != null && c.enabled && c.OverlapPoint(emitted.Position)),
                "Bucket ice spawns outside the filled exterior without acquiring a liquid owner");

            ValidateAuthoredShaker(world, root.transform, sourceBucket.icePrefab, outside, require);
        }
        finally
        {
            Physics2D.gravity = previousGravity;
            Physics2D.simulationMode = previousMode;
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }
    }

    private static void ResetInside(FluidExperimentWorld world, FluidExperimentBody vessel,
        FluidExperimentBody ice, Vector2 center)
    {
        vessel.SetHeld(false); vessel.SetSealed(false);
        vessel.Body.bodyType = RigidbodyType2D.Kinematic;
        vessel.Teleport(new Vector2(80, 80), 0);
        ice.Teleport(vessel.LocalToWorld(center), 0);
        Physics2D.SyncTransforms(); world.RefreshIceContainment();
    }

    private static void ValidateAuthoredShaker(FluidExperimentWorld world, Transform parent,
        FluidExperimentBody icePrefab, FluidExperimentBody externalIce, Action<bool, string> require)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<FluidExperimentBody>(
            "Assets/_Project/Features/Bartending/FluidGpuExperiment/Prefabs/Shaker.prefab");
        require(prefab != null, "Authored shaker prefab is available for real ice closure checks");
        if (prefab == null) return;
        var shaker = UnityEngine.Object.Instantiate(prefab, new Vector3(75, 85), Quaternion.identity, parent);
        var ice = UnityEngine.Object.Instantiate(icePrefab, shaker.Position, Quaternion.identity, parent);
        Physics2D.gravity = new Vector2(0, -9.81f);
        foreach (FluidExperimentShakerClosure closure in Enum.GetValues(typeof(FluidExperimentShakerClosure)))
        {
            shaker.SetHeld(false); shaker.SetShakerClosure(FluidExperimentShakerClosure.Open);
            shaker.Body.bodyType = RigidbodyType2D.Kinematic;
            shaker.Teleport(new Vector2(75, 85), 0);
            ice.Teleport(shaker.LocalToWorld(shaker.collisionProfile.InteriorBounds.center), 0);
            Physics2D.SyncTransforms(); world.RefreshIceContainment();
            require(ice.ContainingVesselId == shaker.Id, closure + ": actual ice starts owned inside the authored shaker bowl");
            shaker.SetShakerClosure(closure); shaker.SetHeld(true);
            shaker.SetHeldPose(shaker.Position, 180); world.SendMessage("FixedUpdate");
            for (int step = 0; step < 100; step++)
            {
                Physics2D.SyncTransforms(); Physics2D.Simulate(.02f); world.RefreshIceContainment();
            }
            bool retained = ice.ContainingVesselId == shaker.Id;
            require(closure == FluidExperimentShakerClosure.Open ? !retained : retained,
                closure + ": actual inverted Physics2D shaker " + (closure == FluidExperimentShakerClosure.Open
                    ? "releases ice through its fully open bowl" : "retains ice behind its assembled strainer"));
            require(ice.Body.bodyType == RigidbodyType2D.Dynamic && ice.Body.gravityScale > 0,
                closure + ": shaker closure never freezes its ice Rigidbody");
        }

        shaker.SetHeld(false); shaker.SetShakerClosure(FluidExperimentShakerClosure.Open);
        shaker.Body.bodyType = RigidbodyType2D.Kinematic;
        shaker.Teleport(new Vector2(75, 85), 0);
        ice.Teleport(shaker.LocalToWorld(shaker.collisionProfile.InteriorBounds.center), 0);
        Physics2D.SyncTransforms(); world.RefreshIceContainment(); shaker.SetHeld(true);
        uint oldId = shaker.Id;
        shaker.gameObject.SetActive(false);
        require(ice.ContainingVesselId == 0,
            "Disabling a held container immediately releases the contained ice owner");
        require(ice.solidColliders.Where(c => c != null && c.enabled).All(c =>
            externalIce.solidColliders.Where(d => d != null && d.enabled).All(d => !Physics2D.GetIgnoreCollision(c, d))),
            "Container removal restores formerly isolated ice-to-external-ice contacts");
        shaker.gameObject.SetActive(true);
        require(shaker.Id != oldId && ice.ContainingVesselId != oldId,
            "Re-enabling a container gives it a new identity without resurrecting the old ice owner");
        Physics2D.gravity = Vector2.zero;
    }
}
