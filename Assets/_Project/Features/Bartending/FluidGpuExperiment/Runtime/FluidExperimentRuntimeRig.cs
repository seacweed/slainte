using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum FluidExperimentBreakageStyle { Natural, Explosive }

    /// <summary>Reusable production composition. The comparison UI is an optional external client.</summary>
    [DefaultExecutionOrder(-1000), DisallowMultipleComponent]
    public sealed class FluidExperimentRuntimeRig : MonoBehaviour
    {
        public FluidExperimentWorld world;
        public FluidExperimentGpuLiquid liquid;
        public FluidExperimentInteractor interactor;
        public FluidExperimentEffects effects;
        public Camera inputCamera;
        public FluidExperimentEnvironmentBinding environment;
        public FluidExperimentAuthoringCatalog catalog;
        public FluidExperimentLiquidSettings liquidSettings;
        public ItemDef defaultIngredient;
        public FluidExperimentSplashProfile defaultSplashProfile;
        public FluidExperimentBreakageStyle breakageStyle = FluidExperimentBreakageStyle.Natural;
        public bool shardLiquidCollision = true;
        public bool seedLiquids;
        public bool enablePointerInput = true;
        public FluidExperimentWorld World => world;
        public FluidExperimentGpuLiquid Liquid => liquid;
        public FluidExperimentInteractor Interactor => interactor;
        public bool Ready => liquid != null && liquid.IsOperational;
        public string Status => Ready ? "Ready" : liquid != null ? liquid.Error : "Liquid module missing";
        private FluidExperimentLiquidSettings runtimeSettings;
        private FluidExperimentLiquidSettings runtimeSettingsSource;
        private Transform staging;
        private readonly List<GameObject> spawnedObjects = new List<GameObject>();

        private void Awake() => ApplyConfiguration();

        public void ApplyConfiguration()
        {
            if (world == null) world = GetComponent<FluidExperimentWorld>();
            if (liquid == null) liquid = GetComponent<FluidExperimentGpuLiquid>();
            if (interactor == null) interactor = GetComponent<FluidExperimentInteractor>();
            if (effects == null) effects = GetComponent<FluidExperimentEffects>();
            if (world == null || liquid == null || interactor == null)
                throw new InvalidOperationException("Runtime rig requires its World, Liquid and Interactor components.");
            world.liquid = liquid; world.interactor = interactor; world.showControls = false;
            world.seedLiquids = seedLiquids;
            world.ExplosiveGlassBreakage = breakageStyle == FluidExperimentBreakageStyle.Explosive;
            world.shardLiquidCollision = shardLiquidCollision;
            interactor.world = world; interactor.inputCamera = inputCamera;
            interactor.enabled = enablePointerInput;
            liquid.outputCamera = inputCamera;
            liquid.solver = FluidExperimentSolver.ReferenceSph;
            liquid.useCohesivePhysics = true; liquid.useImprovedPhysics = false;
            liquid.useReferenceSurface = true; liquid.useImprovedSurface = true;
            liquid.useSurfaceRendering = true; liquid.useStreamRendering = true;
            bool restartLiquid = false;
            if (Application.isPlaying && runtimeSettingsSource != liquidSettings)
            {
                restartLiquid = liquid.IsOperational;
                if (restartLiquid) liquid.Dispose();
                if (runtimeSettings != null) Destroy(runtimeSettings);
                runtimeSettings = null; runtimeSettingsSource = liquidSettings;
                if (liquidSettings != null)
                {
                    runtimeSettings = Instantiate(liquidSettings);
                    runtimeSettings.name = liquidSettings.name + " (runtime rig)";
                    runtimeSettings.hideFlags = HideFlags.DontSave;
                }
            }
            liquid.settings = runtimeSettings != null ? runtimeSettings : liquidSettings;
            if (effects != null) effects.world = world;
            if (environment != null) environment.BindTo(world);
            foreach (var glass in world.GetComponentsInChildren<FluidExperimentBreakableGlass>(true))
                if (glass.splashProfile == null) glass.splashProfile = defaultSplashProfile;
            if (restartLiquid) liquid.Initialize(world);
        }

        public FluidExperimentBody Spawn(FluidExperimentBody prefab, Vector2 position, float angle = 0)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            return SpawnObject(prefab.gameObject, position, angle).GetComponent<FluidExperimentBody>();
        }

        public GameObject SpawnObject(GameObject prefab, Vector2 position, float angle = 0)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (world == null) ApplyConfiguration();
            if (staging == null)
            {
                var stage = new GameObject("Inactive spawn staging");
                stage.SetActive(false); stage.transform.SetParent(transform, false); staging = stage.transform;
            }
            // Instantiate below an inactive parent so configuration precedes Awake/OnEnable.
            var instance = Instantiate(prefab, staging, false);
            instance.SetActive(false);
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, angle));
            instance.transform.SetParent(world.transform, true);
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = world.itemLayer;
            foreach (var glass in instance.GetComponentsInChildren<FluidExperimentBreakableGlass>(true))
                if (glass.splashProfile == null) glass.splashProfile = defaultSplashProfile;
            instance.SetActive(true);
            foreach (var body in instance.GetComponentsInChildren<FluidExperimentBody>()) body.Attach(world);
            spawnedObjects.Add(instance);
            return instance;
        }

        public void ResetRig()
        {
            ApplyConfiguration();
            if (!Application.isPlaying) return;
            world.ResetSession();
            // Sources such as garnish jars have no FluidExperimentBody and are outside the world's body registry.
            foreach (var instance in spawnedObjects)
                if (instance != null) { instance.SetActive(false); Destroy(instance); }
            spawnedObjects.Clear();
        }

        // ResetRig restores authored targets before a repeat; dynamic spawned targets are cleared by ResetRig.
        public void PrepareDrop(FluidExperimentBody target, float height, Vector2 velocity, float fillMl)
        {
            if (!Application.isPlaying || target == null || target.World != world)
                throw new ArgumentException("A drop requires an active target in this running rig.", nameof(target));
            if (!float.IsFinite(height) || height < 0 || !float.IsFinite(fillMl) || fillMl < 0
                || !float.IsFinite(velocity.x) || !float.IsFinite(velocity.y))
                throw new ArgumentOutOfRangeException(nameof(height));
            liquid.ResetSimulation();
            target.SetHeld(false);
            float floor = environment != null && environment.table != null ? environment.table.bounds.max.y : 0;
            float bottomOffset = target.Position.y - target.SolidBounds.min.y;
            target.Teleport(new Vector2(target.Position.x, floor + height + bottomOffset), target.Angle);
            if (fillMl > 0 && defaultIngredient != null) liquid.Fill(target, defaultIngredient, fillMl);
            target.Body.linearVelocity = velocity;
            Physics2D.SyncTransforms();
        }

        private void OnDestroy()
        {
            if (liquid != null && liquid.settings == runtimeSettings) liquid.settings = liquidSettings;
            if (runtimeSettings != null) Destroy(runtimeSettings);
        }
    }
}
