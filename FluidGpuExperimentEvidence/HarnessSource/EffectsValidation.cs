using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

public static class ExperimentEffectsChecks
{
    public static IEnumerator Run(FluidExperimentWorld world, Action<bool, string> require)
    {
        Vector2 gravity = new Vector2(0, -9.81f);
        Vector2 buoyancy = FluidExperimentEffects.IceForce(1, 1, 1, 8, Vector2.zero, Vector2.zero, gravity, .02f);
        require(buoyancy.y > 9.81f && buoyancy.y < 12 && buoyancy.x == 0,
            "Water supports submerged ice with the .917 density ratio");
        require(FluidExperimentEffects.IceForce(1, 0, 1, 8, Vector2.one * 100, Vector2.zero, gravity, .02f) == Vector2.zero,
            "Dry ice receives no fluid force and keeps table pass-through gravity");
        Vector2 drag = FluidExperimentEffects.IceForce(1, 1, 1, 35, new Vector2(100, 0), Vector2.zero, gravity, .02f);
        require(drag.x < 0 && Mathf.Abs(drag.x) <= 19.621f && drag.magnitude < 31,
            "High relative speed and syrup viscosity produce bounded opposing drag");

        var gpu = world.Liquid;
        var effects = UnityEngine.Object.FindFirstObjectByType<FluidExperimentEffects>();
        require(effects != null, "Comparison owns the isolated E effects component");
        effects.enabled = false; effects.world = world;
        gpu.useImprovedPhysics = true; gpu.automaticReadback = false; gpu.ResetSimulation();
        foreach (var body in world.Items)
        {
            body.SetHeld(true); body.Teleport(new Vector2(-18, 10), 0); body.pourMlPerSecond = 0;
            body.Body.simulated = false;
        }
        var vessel = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
        var ingredient = world.Items.First(b => b.ingredient != null).ingredient;
        vessel.SetHeld(false); vessel.Body.bodyType = RigidbodyType2D.Kinematic; vessel.Teleport(new Vector2(0, 2), 0);
        Vector2 center = vessel.LocalToWorld(vessel.collisionProfile.InteriorBounds.center);
        var iceObject = new GameObject("Effects contact validation ice");
        iceObject.transform.SetParent(world.transform, false);
        iceObject.transform.position = center;
        iceObject.AddComponent<Rigidbody2D>();
        var collider = iceObject.AddComponent<BoxCollider2D>(); collider.size = new Vector2(.25f, .25f);
        var ice = iceObject.AddComponent<FluidExperimentBody>();
        ice.kind = LabItemKind.Ice; ice.solidColliders = new Collider2D[] { collider }; ice.Attach(world);
        ice.SetHeld(true); ice.Teleport(center, 0);
        try
        {
            int births = 0;
            for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
            {
                Vector2 point = center + new Vector2(x, y) * .075f;
                if (vessel.ContainsLiquidDisk(vessel.WorldToLocal(point), gpu.Radius)
                    && gpu.TryEmit(point, new Vector2(.7f, .4f), ingredient, .5f, vessel.Id)) births++;
            }
            require(births >= 9, "Actual vessel-owned GPU liquid supplies the effects fixture");
            world.TickLiquid(.002f);
            ice.SetHeld(false); ice.Body.simulated = true;
            Physics2D.SyncTransforms();
            gpu.ReadbackNow(); effects.RefreshSnapshot();
            effects.ApplyIceResponse(.02f);
            require(effects.LastAppliedIceCount == 1, "Actual completed owned-liquid snapshot applies ice response");
            ice.SetHeld(true); effects.ApplyIceResponse(.02f);
            require(effects.LastAppliedIceCount == 0, "Held ice receives no fluid force");
            ice.SetHeld(false); vessel.SetHeld(true); effects.ApplyIceResponse(.02f);
            require(effects.LastAppliedIceCount == 0, "Liquid owned by a held vessel cannot push external ice");
            vessel.SetHeld(false); vessel.Body.bodyType = RigidbodyType2D.Kinematic;
            ice.Teleport(center + Vector2.right * 10, 0); effects.ApplyIceResponse(.02f);
            require(effects.LastAppliedIceCount == 0, "Ice outside the liquid receives no stale snapshot force");

            // Repeated candidate batches exercise the capped cosmetic pool using genuine GPU particles.
            double generated = gpu.Ledger.Total.GeneratedMl;
            var spawn = typeof(FluidExperimentEffects).GetMethod("SpawnCosmetics", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < 40; i++) spawn.Invoke(effects, new object[] { gpu.Snapshot });
            effects.StepCosmetics(.01f);
            require(effects.ActiveCosmeticCount > 0 && effects.ActiveCosmeticCount <= FluidExperimentEffects.MaximumCosmetics,
                "Foam/bubble candidates use a bounded 128-effect pool");
            require(gpu.Ledger.Total.GeneratedMl == generated, "Cosmetics create zero logical liquid ml");
            effects.StepCosmetics(6);
            require(effects.ActiveCosmeticCount == 0, "Every cosmetic expires after its bounded material lifetime");
            require(effects.AudioSourceCount == 0, "Audio defaults off without allocating audio resources");
            effects.enableAudio = true; effects.audioVolume = 0; effects.SendMessage("Update");
            require(effects.AudioSourceCount == 2, "Opt-in audio creates exactly two bounded procedural sources");
            effects.enableAudio = false; effects.SendMessage("Update");
            require(effects.AudioSourceCount == 0, "Disabling optional audio releases sources and clips");
            gpu.useImprovedPhysics = false; effects.SendMessage("Update"); effects.ApplyIceResponse(.02f);
            require(effects.LastAppliedIceCount == 0 && effects.ActiveCosmeticCount == 0,
                "Leaving E immediately removes all secondary response and cosmetic state");
            yield return null;
        }
        finally
        {
            iceObject.SetActive(false); UnityEngine.Object.Destroy(iceObject);
            effects.enableAudio = false; effects.enabled = true; effects.enabled = false;
        }
    }
}
