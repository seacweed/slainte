using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment.Editor
{
    /// <summary>Read-only authoring checks. Never applies scene overrides or rebuilds assets.</summary>
    public static class FluidExperimentPrefabAudit
    {
        [MenuItem("Slainte/Fluid GPU Experiment/Validate Prefab References")]
        public static void ValidateMenu()
        {
            var errors = ValidateAssets();
            if (errors.Count > 0)
                Debug.LogError("[FluidPrefabAudit]\n" + string.Join("\n", errors));
            else
                Debug.Log("[FluidPrefabAudit] Prefab identities, catalog, runtime and saved scene references are valid. No assets changed.");
        }

        public static List<string> ValidateAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before auditing saved prefab references.");
            var errors = new List<string>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var catalog = AssetDatabase.LoadAssetAtPath<FluidExperimentAuthoringCatalog>(FluidExperimentAssemblyBuilder.CatalogPath);
            if (catalog == null || catalog.entries == null)
            {
                errors.Add("Authoring catalog is missing.");
                return errors;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var bottleIngredients = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in catalog.entries)
            {
                if (entry == null) { errors.Add("Catalog contains an empty entry."); continue; }
                if (string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id))
                    errors.Add("Catalog has an empty or duplicate ID: " + entry.id);
                if (entry.prefab == null) { errors.Add("Catalog prefab is missing: " + entry.id); continue; }
                string path = AssetDatabase.GetAssetPath(entry.prefab);
                if (!paths.Add(path)) errors.Add("Catalog repeats prefab: " + path);
                var body = entry.prefab.GetComponent<FluidExperimentBody>();
                if (entry.prefab.GetComponent<FluidExperimentDispenser>() != null)
                    errors.Add(path + ": dispenser must be owned by World, outside the spawn catalog.");
                if (body != null && entry.ingredient != body.ingredient)
                    errors.Add(path + ": catalog ingredient differs from the prefab ingredient.");
                if (body != null && body.kind == LabItemKind.Bottle)
                {
                    if (entry.id != AssetDatabase.AssetPathToGUID(path) || entry.category != "Bottle"
                        || entry.displayName != body.displayName)
                        errors.Add(path + ": bottle catalog identity, category or display name differs from its prefab.");
                    if (body.ingredient != null && !bottleIngredients.Add(body.ingredient.id))
                        errors.Add(path + ": multiple bottle entries use ingredient " + body.ingredient.id);
                    if (body.ingredient != null && Path.GetFileNameWithoutExtension(path) != "Bottle_" + body.ingredient.id)
                        errors.Add(path + ": bottle filename differs from its ingredient ID.");
                }
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { FluidExperimentBuilder.Root + "/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var body = prefab != null ? prefab.GetComponent<FluidExperimentBody>() : null;
                if (body != null && body.kind == LabItemKind.Bottle && !paths.Contains(path))
                    errors.Add(path + ": bottle is missing from the authoring catalog.");
            }
            paths.Add(FluidExperimentDispenserBuilder.PrefabPath);
            paths.Add(FluidExperimentAssemblyBuilder.RuntimePrefabPath);
            paths.Add(FluidExperimentAssemblyBuilder.EnvironmentPrefabPath);
            paths.Add(FluidExperimentAssemblyBuilder.ShardPrefabPath);
            paths.Add(FluidExperimentAssemblyBuilder.ShakerPartPrefabPath);
            foreach (string path in paths.OrderBy(value => value, StringComparer.Ordinal))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { errors.Add("Missing prefab: " + path); continue; }
                ValidateHierarchy(prefab, path, errors);
                var rig = prefab.GetComponent<FluidExperimentRuntimeRig>();
                if (rig != null) ValidateRig(rig, path, false, errors);
            }
            foreach (string path in new[] { FluidExperimentBuilder.ScenePath, FluidExperimentAssemblyBuilder.SampleScenePath })
            {
                var scene = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        ValidateHierarchy(root, path, errors);
                        foreach (var rig in root.GetComponentsInChildren<FluidExperimentRuntimeRig>(true))
                            ValidateRig(rig, path, true, errors);
                    }
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            return errors;
        }

        private static void ValidateHierarchy(GameObject root, string path, List<string> errors)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                    errors.Add(path + ": missing script on " + transform.name);
            foreach (var body in root.GetComponentsInChildren<FluidExperimentBody>(true))
            {
                string label = path + "/" + body.name;
                if (body.GetComponent<Rigidbody2D>() == null || body.collisionProfile == null)
                    errors.Add(label + ": body physics or collision profile is missing.");
                if (body.kind == LabItemKind.Glass && string.IsNullOrWhiteSpace(body.servingGlassId))
                    errors.Add(label + ": recipe glass ID is missing.");
                if (body.kind == LabItemKind.Bottle && (body.ingredient == null || string.IsNullOrWhiteSpace(body.ingredient.id)))
                    errors.Add(label + ": bottle ingredient or ingredient ID is missing.");
                if (body.kind == LabItemKind.Bottle) ValidateBottle(body, label, errors);
                if (body.kind == LabItemKind.IceBucket && body.icePrefab == null)
                    errors.Add(label + ": ice prefab is missing.");
                if (body.kind == LabItemKind.Shaker && (body.shakerProfile == null || body.shakerPartTemplate == null
                    || body.shakerCapArt == null || body.shakerStrainerArt == null))
                    errors.Add(label + ": shaker part, artwork or profile reference is missing.");
            }
            foreach (var source in root.GetComponentsInChildren<FluidExperimentGarnishSource>(true))
                if (source.garnishPrefab == null || source.garnishPrefab.kind != LabItemKind.Garnish || source.pickCollider == null)
                    errors.Add(path + ": garnish source requires its piece prefab and pick collider.");
            foreach (var dispenser in root.GetComponentsInChildren<FluidExperimentDispenser>(true))
                if (dispenser.body == null || dispenser.hotWater == null || dispenser.icePrefab == null
                    || dispenser.bodyRenderer == null || dispenser.noneSprite == null || dispenser.iceSprite == null
                    || dispenser.hotSprite == null || dispenser.hoseMaterial == null)
                    errors.Add(path + ": dispenser supply or artwork reference is missing.");
        }

        private static void ValidateBottle(FluidExperimentBody body, string label, List<string> errors)
        {
            if (body.pickCollider == null || !body.pickCollider.isTrigger
                || !body.pickCollider.transform.IsChildOf(body.transform))
                errors.Add(label + ": bottle pick trigger must belong to this bottle.");
            if (body.solidColliders == null || body.solidColliders.Length == 0
                || body.solidColliders.Any(c => c == null || c.isTrigger || !c.transform.IsChildOf(body.transform)))
                errors.Add(label + ": bottle solid colliders are missing, triggers or foreign references.");
            if (!body.GetComponentsInChildren<SpriteRenderer>(true).Any(r => r.sprite != null))
                errors.Add(label + ": bottle artwork is missing.");
            var profile = body.collisionProfile;
            if (profile != null && (profile.solids == null || profile.solids.Length == 0
                || profile.solids.Any(h => h == null || h.points == null || h.points.Length < 3
                    || h.points.Any(p => !float.IsFinite(p.x) || !float.IsFinite(p.y)))
                || !float.IsFinite(profile.mouth.x) || !float.IsFinite(profile.mouth.y)
                || !float.IsFinite(profile.exitDirection.x) || !float.IsFinite(profile.exitDirection.y)
                || profile.exitDirection.sqrMagnitude < .000001f))
                errors.Add(label + ": bottle collision hull, mouth or exit direction is invalid.");
            if (!float.IsFinite(body.capacityMl) || !float.IsFinite(body.remainingMl)
                || body.capacityMl <= 0 || body.remainingMl < 0 || body.remainingMl > body.capacityMl
                || !float.IsFinite(body.pourMlPerSecond) || body.pourMlPerSecond <= 0
                || !float.IsFinite(body.pourStartAngle) || !float.IsFinite(body.fullPourAngle)
                || body.pourStartAngle < 0 || body.fullPourAngle <= body.pourStartAngle || body.fullPourAngle > 180
                || !float.IsFinite(body.exitSpeed) || body.exitSpeed < 0)
                errors.Add(label + ": bottle supply or pouring settings are invalid.");
            if (body.gameObject.scene.IsValid())
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(body);
                if (source == null)
                    errors.Add(label + ": scene bottle has no source prefab connection.");
                else if (body.ingredient != source.ingredient || body.collisionProfile != source.collisionProfile)
                    errors.Add(label + ": scene bottle overrides its prefab ingredient or collision profile.");
            }
        }

        private static void ValidateRig(FluidExperimentRuntimeRig rig, string path, bool sceneInstance, List<string> errors)
        {
            var cocktails = rig.GetComponent<FluidExperimentCocktailSession>();
            if (cocktails == null || cocktails.world != rig.world || cocktails.uiFont == null)
                errors.Add(path + ": cocktail session, world binding or UI font is missing.");
            if (rig.world == null || rig.liquid == null || rig.interactor == null
                || rig.catalog == null || rig.liquidSettings == null || rig.defaultIngredient == null
                || rig.defaultSplashProfile == null)
            { errors.Add(path + ": runtime module or settings reference is missing."); return; }
            if (rig.world.gameObject != rig.gameObject || rig.liquid.gameObject != rig.gameObject
                || rig.interactor.gameObject != rig.gameObject
                || rig.world.liquid != rig.liquid || rig.world.interactor != rig.interactor
                || rig.interactor.world != rig.world || rig.world.dispenserPrefab == null)
                errors.Add(path + ": runtime modules are not connected to their own world.");
            if (rig.liquidSettings.gpuLiquidComputeShader == null || rig.liquid.drawShader == null
                || rig.liquid.improvedSurfaceCompute == null || rig.liquid.improvedAccumulationShader == null
                || rig.liquid.improvedCompositeShader == null)
                errors.Add(path + ": runtime simulation or rendering shader is missing.");
            // Prefab cameras/environments are intentionally supplied by each scene.
            if (sceneInstance && (rig.inputCamera == null || rig.environment == null))
                errors.Add(path + ": scene runtime requires explicit camera and environment bindings.");
        }
    }
}
