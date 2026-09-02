using System;
using System.Collections.Generic;
using Slainte.Content;
using System.IO;
using System.Reflection;
using Slainte.Bartending;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Editor
{
    public static class GlassCollisionProfileValidator
    {
        private const string AssetRoot = BartendingAssetPaths.GlassArtRoot;
        private const string DefinitionRoot = "Assets/Resources/Bartending/ToolCabinet/";
        private const string SettingsPath =
            "Assets/Resources/Bartending/BusinessBartendingSettings.asset";
        private const float FloatTolerance = 0.01f;
        private static readonly int[] ExpectedLayerOrders = { 10, 11, 14, 15 };

        private static readonly Dictionary<string, ExpectedProfile> ExpectedProfiles = new(
            StringComparer.OrdinalIgnoreCase)
        {
            { "rock_front_line", new ExpectedProfile("rock", 200f) },
            { "cocktail_front_line", new ExpectedProfile("martini", 200f) },
            { "highball_front_white", new ExpectedProfile("highball", 400f) },
            { "hurricane_front_line", new ExpectedProfile("hurricane", 400f) }
        };

        [MenuItem("Tools/Slainte/Validate Layered Glass Collision Profiles")]
        public static void ValidateFromMenu()
        {
            ValidateAll();
            Debug.Log("[GlassCollisionProfileValidator] All layered glass profiles passed.");
        }

        public static void RunCommandLine()
        {
            try
            {
                ValidateAll();
                Debug.Log("[GlassCollisionProfileValidator] Command-line validation passed.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void ValidateAll()
        {
            Require(
                GlassCollisionProfiles.All.Count == ExpectedProfiles.Count,
                "Exactly four layered glass profiles are required.");

            for (int i = 0; i < GlassCollisionProfiles.All.Count; i++)
            {
                GlassCollisionProfileDefinition profile = GlassCollisionProfiles.All[i];
                ValidateDefinition(profile);
                ValidateImportedSpriteAndRuntimeGeometry(profile);
            }

            ValidatePlanningRecipeCapacity();
            ValidateProductionFallback();
            ValidateLayeredFactoryOutput();
        }

        private static void ValidateDefinition(GlassCollisionProfileDefinition profile)
        {
            Require(profile != null, "A null glass profile was registered.");
            Require(profile.IsValid(out string error), error);
            Require(
                ExpectedProfiles.TryGetValue(profile.SourceSpriteName, out ExpectedProfile expected),
                "Unexpected glass profile: " + profile.SourceSpriteName);
            Require(
                string.Equals(profile.GlassId, expected.GlassId, StringComparison.OrdinalIgnoreCase),
                profile.SourceSpriteName + " has the wrong glass id: " + profile.GlassId);
            Require(
                Mathf.Approximately(profile.CapacityMl, expected.CapacityMl),
                profile.SourceSpriteName + " has the wrong capacity: " + profile.CapacityMl);
            Require(
                profile.SourcePixelSize == GlassCollisionProfiles.SourcePixelSize,
                profile.SourceSpriteName + " must retain the 310 x 590 source dimensions.");

            IReadOnlyList<Vector2> edge = profile.EdgePathPixels;
            Require(
                edge[0].x < edge[edge.Count - 1].x,
                profile.SourceSpriteName + " U-edge endpoints are reversed.");
            Require(
                Mathf.Abs(edge[0].y - edge[edge.Count - 1].y) <= 2f,
                profile.SourceSpriteName + " open rim endpoints are not level.");
            Require(
                Vector2.Distance(edge[0], edge[edge.Count - 1])
                    > profile.SourcePixelSize.x * 0.5f,
                profile.SourceSpriteName + " U-edge opening is too narrow or closed.");

            for (int i = 0; i < profile.ContentTriggerPixels.Count; i++)
                ValidateTriggerInsideOpenEdge(profile, profile.ContentTriggerPixels[i], i);
        }

        private static void ValidateImportedSpriteAndRuntimeGeometry(
            GlassCollisionProfileDefinition profile)
        {
            string assetPath = AssetRoot + profile.SourceSpriteName + ".png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            Require(sprite != null, "Layered glass reference sprite is not imported: " + assetPath);
            Require(sprite.texture != null, "Layered glass reference sprite has no source texture: " + assetPath);
            Require(
                sprite.texture.width == profile.SourcePixelSize.x
                    && sprite.texture.height == profile.SourcePixelSize.y,
                profile.SourceSpriteName + " source image dimensions changed from 310 x 590.");
            Require(
                Mathf.Approximately(sprite.rect.x, 0f)
                    && Mathf.Approximately(sprite.rect.y, 0f)
                    && Mathf.Approximately(sprite.rect.width, profile.SourcePixelSize.x)
                    && Mathf.Approximately(sprite.rect.height, profile.SourcePixelSize.y),
                profile.SourceSpriteName + " sprite rectangle was trimmed from the full canvas.");
            Require(
                Mathf.Approximately(sprite.textureRect.x, 0f)
                    && Mathf.Approximately(sprite.textureRect.y, 0f)
                    && Mathf.Approximately(sprite.textureRect.width, profile.SourcePixelSize.x)
                    && Mathf.Approximately(sprite.textureRect.height, profile.SourcePixelSize.y),
                profile.SourceSpriteName + " texture rectangle was trimmed from the full canvas.");

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Require(importer != null, profile.SourceSpriteName + " has no TextureImporter.");
            Require(importer.spriteImportMode == SpriteImportMode.Single,
                profile.SourceSpriteName + " must use Sprite Mode Single.");
            Require(Mathf.Approximately(importer.spritePixelsPerUnit, 100f),
                profile.SourceSpriteName + " must use 100 pixels per unit.");
            TextureImporterSettings importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);
            Require(importerSettings.spriteMeshType == SpriteMeshType.FullRect,
                profile.SourceSpriteName + " must use a Full Rect sprite mesh.");
            ValidateEdgeAgainstSourcePixels(assetPath, profile);

            GameObject root = new GameObject("GlassProfileValidation_" + profile.SourceSpriteName);
            GameObject visualObject = new GameObject("Visual");
            ItemDef firstItem = null;
            ItemDef secondItem = null;
            GameObject firstParticle = null;
            GameObject secondParticle = null;
            try
            {
                visualObject.transform.SetParent(root.transform, false);
                SpriteRenderer visual = visualObject.AddComponent<SpriteRenderer>();
                visual.sprite = sprite;

                Vector3 originalRootScale = root.transform.localScale;
                Vector3 originalVisualPosition = visual.transform.localPosition;
                Quaternion originalVisualRotation = visual.transform.localRotation;
                Vector3 originalVisualScale = visual.transform.localScale;
                Rect originalTextureRect = sprite.textureRect;

                EdgeCollider2D edge = root.AddComponent<EdgeCollider2D>();
                BoxCollider2D legacyTrigger = root.AddComponent<BoxCollider2D>();
                legacyTrigger.isTrigger = true;
                GlassController glass = root.AddComponent<GlassController>();
                glass.ApplyCollisionProfile(profile, visual);

                Require(glass.ActiveCollisionProfile == profile,
                    profile.SourceSpriteName + " was not retained as the active profile.");
                Require(string.Equals(glass.GlassId, profile.GlassId, StringComparison.OrdinalIgnoreCase),
                    profile.SourceSpriteName + " did not configure its serving glass id.");
                Require(Mathf.Approximately(glass.CapacityMl, profile.CapacityMl),
                    profile.SourceSpriteName + " did not configure its capacity.");
                Require(glass.CanContain(profile.CapacityMl),
                    profile.SourceSpriteName + " rejects its declared capacity.");
                Require(!glass.CanContain(profile.CapacityMl + 1f),
                    profile.SourceSpriteName + " accepts more than its declared capacity.");

                Require(root.GetComponentsInChildren<PolygonCollider2D>(true).Length == 0,
                    profile.SourceSpriteName + " created a forbidden PolygonCollider2D.");
                Require(!edge.isTrigger,
                    profile.SourceSpriteName + " physical U-edge must not be a trigger.");
                Require(edge.pointCount == profile.EdgePathPixels.Count,
                    profile.SourceSpriteName + " generated the wrong U-edge point count.");
                Require(!legacyTrigger.enabled,
                    profile.SourceSpriteName + " left the broad legacy trigger enabled.");

                int activeTriggerCount = 0;
                BoxCollider2D[] boxes = root.GetComponentsInChildren<BoxCollider2D>(true);
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (boxes[i] != null && boxes[i].enabled && boxes[i].isTrigger)
                        activeTriggerCount++;
                }
                Require(activeTriggerCount == profile.ContentTriggerPixels.Count,
                    profile.SourceSpriteName + " generated the wrong content trigger count.");

                Require(root.transform.localScale == originalRootScale,
                    profile.SourceSpriteName + " changed the glass transform scale.");
                Require(visual.transform.localPosition == originalVisualPosition,
                    profile.SourceSpriteName + " moved the source visual.");
                Require(visual.transform.localRotation == originalVisualRotation,
                    profile.SourceSpriteName + " rotated the source visual.");
                Require(visual.transform.localScale == originalVisualScale,
                    profile.SourceSpriteName + " scaled the source visual.");
                Require(sprite.textureRect == originalTextureRect,
                    profile.SourceSpriteName + " changed the source sprite rectangle.");

                ValidateServeAndEvaluateFlow(
                    glass,
                    profile,
                    boxes,
                    out firstItem,
                    out secondItem,
                    out firstParticle,
                    out secondParticle);

                ValidatePhysicalContainment(profile, sprite);
            }
            finally
            {
                if (firstParticle != null)
                    UnityEngine.Object.DestroyImmediate(firstParticle);
                if (secondParticle != null)
                    UnityEngine.Object.DestroyImmediate(secondParticle);
                if (firstItem != null)
                    UnityEngine.Object.DestroyImmediate(firstItem);
                if (secondItem != null)
                    UnityEngine.Object.DestroyImmediate(secondItem);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ValidatePhysicalContainment(
            GlassCollisionProfileDefinition profile,
            Sprite sprite)
        {
            Scene physicsSceneContainer = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("PhysicalGlass_" + profile.SourceSpriteName);
                SceneManager.MoveGameObjectToScene(root, physicsSceneContainer);

                GameObject visualObject = new GameObject("Visual");
                visualObject.transform.SetParent(root.transform, false);
                SpriteRenderer visual = visualObject.AddComponent<SpriteRenderer>();
                visual.sprite = sprite;

                EdgeCollider2D edge = root.AddComponent<EdgeCollider2D>();
                BoxCollider2D legacyTrigger = root.AddComponent<BoxCollider2D>();
                legacyTrigger.isTrigger = true;
                GlassController glass = root.AddComponent<GlassController>();
                glass.ApplyCollisionProfile(profile, visual);

                PhysicsScene2D physicsScene = physicsSceneContainer.GetPhysicsScene2D();
                Require(physicsScene.IsValid(),
                    profile.SourceSpriteName + " did not create an isolated 2D physics scene.");

                Vector2 leftRim = root.transform.TransformPoint(edge.points[0]);
                Vector2 rightRim = root.transform.TransformPoint(edge.points[edge.pointCount - 1]);
                Vector2 spawnPoint = (leftRim + rightRim) * 0.5f + Vector2.up * 0.18f;

                SimulateContainedBody(
                    physicsSceneContainer,
                    physicsScene,
                    visual,
                    profile,
                    spawnPoint,
                    "LiquidParticle",
                    false);
                SimulateContainedBody(
                    physicsSceneContainer,
                    physicsScene,
                    visual,
                    profile,
                    spawnPoint,
                    "IceCube",
                    true);
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
                if (physicsSceneContainer.IsValid())
                    EditorSceneManager.ClosePreviewScene(physicsSceneContainer);
            }
        }

        private static void SimulateContainedBody(
            Scene scene,
            PhysicsScene2D physicsScene,
            SpriteRenderer visual,
            GlassCollisionProfileDefinition profile,
            Vector2 spawnPoint,
            string objectLabel,
            bool iceSized)
        {
            GameObject bodyObject = new GameObject(
                profile.SourceSpriteName + "_" + objectLabel + "PhysicsProbe");
            PhysicsMaterial2D material = new PhysicsMaterial2D(
                profile.SourceSpriteName + "_ContainmentMaterial")
            {
                friction = 0.45f,
                bounciness = 0f
            };

            try
            {
                SceneManager.MoveGameObjectToScene(bodyObject, scene);
                bodyObject.transform.position = spawnPoint;

                Collider2D probeCollider;
                if (iceSized)
                {
                    BoxCollider2D box = bodyObject.AddComponent<BoxCollider2D>();
                    box.size = new Vector2(0.14f, 0.14f);
                    probeCollider = box;
                }
                else
                {
                    CircleCollider2D circle = bodyObject.AddComponent<CircleCollider2D>();
                    circle.radius = 0.045f;
                    probeCollider = circle;
                }

                probeCollider.sharedMaterial = material;
                Rigidbody2D body = bodyObject.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Dynamic;
                body.gravityScale = 1f;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.interpolation = RigidbodyInterpolation2D.None;

                const float step = 1f / 120f;
                for (int i = 0; i < 420; i++)
                    Require(physicsScene.Simulate(step),
                        profile.SourceSpriteName + " could not advance its isolated physics scene.");

                Vector3 spriteLocal = visual.transform.InverseTransformPoint(body.position);
                Bounds spriteBounds = visual.sprite.bounds;
                Vector2 normalized = new Vector2(
                    Mathf.InverseLerp(spriteBounds.min.x, spriteBounds.max.x, spriteLocal.x),
                    Mathf.InverseLerp(spriteBounds.min.y, spriteBounds.max.y, spriteLocal.y));
                Vector2 sourcePixel = new Vector2(
                    normalized.x * profile.SourcePixelSize.x,
                    normalized.y * profile.SourcePixelSize.y);

                Require(IsInsideOpenEdge(profile.EdgePathPixels, sourcePixel, 8f),
                    profile.SourceSpriteName + " failed to retain the " + objectLabel
                        + " probe inside its hand-authored collision edge.");
                Require(sourcePixel.y <= profile.EdgePathPixels[0].y + 8f,
                    profile.SourceSpriteName + " left the " + objectLabel
                        + " probe above the open rim after simulation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bodyObject);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void ValidateServeAndEvaluateFlow(
            GlassController glass,
            GlassCollisionProfileDefinition profile,
            IReadOnlyList<BoxCollider2D> boxes,
            out ItemDef firstItem,
            out ItemDef secondItem,
            out GameObject firstParticle,
            out GameObject secondParticle)
        {
            InvokeNonPublic(glass, "EnsureLiquidTracker");
            VesselLiquidTracker tracker = glass.LiquidTracker;
            Require(tracker != null, profile.SourceSpriteName + " has no liquid tracker.");
            InvokeNonPublic(tracker, "Awake");
            InvokeNonPublic(tracker, "OnEnable");

            BoxCollider2D contentTrigger = null;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i] != null && boxes[i].enabled && boxes[i].isTrigger)
                {
                    contentTrigger = boxes[i];
                    break;
                }
            }
            Require(contentTrigger != null,
                profile.SourceSpriteName + " has no active content trigger for E2E validation.");

            firstItem = ScriptableObject.CreateInstance<ItemDef>();
            firstItem.id = profile.SourceSpriteName + "_ingredient_a";
            firstItem.displayName = "Validation A";
            firstItem.liquidColor = Color.red;
            secondItem = ScriptableObject.CreateInstance<ItemDef>();
            secondItem.id = profile.SourceSpriteName + "_ingredient_b";
            secondItem.displayName = "Validation B";
            secondItem.liquidColor = Color.blue;

            float firstVolume = profile.CapacityMl * 0.3f;
            float secondVolume = profile.CapacityMl * 0.2f;
            Vector2 center = contentTrigger.bounds.center;
            firstParticle = CreateParticle(
                profile.SourceSpriteName + "_ParticleA",
                center + Vector2.left * 0.01f,
                firstItem,
                firstVolume);
            secondParticle = CreateParticle(
                profile.SourceSpriteName + "_ParticleB",
                center + Vector2.right * 0.01f,
                secondItem,
                secondVolume);

            Physics2D.SyncTransforms();
            glass.SetContainsIce(true);

            CocktailRecipe recipe = new CocktailRecipe
            {
                id = profile.SourceSpriteName + "_validation_recipe",
                displayName = "Glass validation recipe",
                minTotalMl = firstVolume + secondVolume,
                maxTotalMl = firstVolume + secondVolume,
                toleranceMl = 0.01f,
                glassId = profile.GlassId,
                iceRequirement = IceRequirement.Required,
                requiredTechnique = CocktailTechnique.Shake
            };
            recipe.ingredients.Add(new CocktailRecipeIngredient
            {
                ingredientId = firstItem.id,
                item = firstItem,
                targetMl = firstVolume,
                toleranceMl = 0.01f
            });
            recipe.ingredients.Add(new CocktailRecipeIngredient
            {
                ingredientId = secondItem.id,
                item = secondItem,
                targetMl = secondVolume,
                toleranceMl = 0.01f
            });
            CocktailRecipeCatalog catalog = new CocktailRecipeCatalog();
            catalog.Add(recipe);
            CocktailEvaluator evaluator = new CocktailEvaluator(catalog);

            Rect serveRect = new Rect(50f, 60f, 120f, 80f);
            glass.ConfigureServeGesture(new FixedServeTarget(serveRect));
            CocktailComposition servedComposition = null;
            CocktailEvaluationResult evaluation = null;
            int serveCount = 0;
            glass.ServeRequested += servedGlass =>
            {
                serveCount++;
                servedComposition = servedGlass.LiquidTracker.BuildComposition();
                evaluation = evaluator.EvaluateRecipe(recipe.id, servedComposition);
            };

            Require(glass.TryRequestServeAtScreenPosition(serveRect.center, true),
                profile.SourceSpriteName + " serving gesture was rejected.");
            Require(!glass.TryRequestServeAtScreenPosition(serveRect.center, true),
                profile.SourceSpriteName + " allowed duplicate serving.");
            Require(serveCount == 1,
                profile.SourceSpriteName + " did not emit exactly one serving event.");
            Require(servedComposition != null,
                profile.SourceSpriteName + " did not deliver a composition to evaluation.");
            Require(string.Equals(
                    servedComposition.GlassId,
                    profile.GlassId,
                    StringComparison.OrdinalIgnoreCase),
                profile.SourceSpriteName + " lost its glass id during serving.");
            Require(servedComposition.HasIce,
                profile.SourceSpriteName + " lost its ice state during serving.");
            Require((servedComposition.Techniques & CocktailTechnique.Shake) != 0,
                profile.SourceSpriteName + " lost its shaking state during serving.");
            Require(Mathf.Abs(servedComposition.GetVolume(firstItem) - firstVolume) <= FloatTolerance,
                profile.SourceSpriteName + " lost the first ingredient during serving.");
            Require(Mathf.Abs(servedComposition.GetVolume(secondItem) - secondVolume) <= FloatTolerance,
                profile.SourceSpriteName + " lost the second ingredient during serving.");
            Require(evaluation != null && evaluation.isSuccess,
                profile.SourceSpriteName + " failed production-to-serving evaluation.");
            Require(evaluation.glassValid && evaluation.iceValid && evaluation.techniqueValid,
                profile.SourceSpriteName + " failed glass, ice, or technique evaluation.");

            InvokeNonPublic(tracker, "OnDisable");
        }

        private static GameObject CreateParticle(
            string name,
            Vector2 position,
            ItemDef item,
            float volumeMl)
        {
            GameObject particleObject = new GameObject(name);
            particleObject.transform.position = position;
            CircleCollider2D collider = particleObject.AddComponent<CircleCollider2D>();
            collider.radius = 0.015f;
            LiquidParticleData particle = particleObject.AddComponent<LiquidParticleData>();
            InvokeNonPublic(particle, "Awake");
            InvokeNonPublic(particle, "OnEnable");
            particle.SetPayload(item, volumeMl);
            particle.RecordTechnique(CocktailTechnique.Shake);
            return particleObject;
        }

        private static void ValidatePlanningRecipeCapacity()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:CocktailRecipeDef",
                new[]
                {
                    ProjectResourcePaths.AssetRoot
                    + ProjectResourcePaths.BartendingRecipes
                });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                CocktailRecipeDef recipe = AssetDatabase.LoadAssetAtPath<CocktailRecipeDef>(path);
                if (recipe == null || string.IsNullOrWhiteSpace(recipe.glassId))
                    continue;

                Require(
                    GlassCollisionProfiles.TryGetByGlassId(
                        recipe.glassId,
                        out GlassCollisionProfileDefinition profile),
                    "No layered glass profile supports recipe glass id '"
                        + recipe.glassId + "' in " + path);

                float ingredientTotal = 0f;
                if (recipe.ingredients != null)
                {
                    for (int ingredientIndex = 0;
                        ingredientIndex < recipe.ingredients.Count;
                        ingredientIndex++)
                    {
                        CocktailRecipeIngredientDef ingredient = recipe.ingredients[ingredientIndex];
                        if (ingredient != null)
                            ingredientTotal += Mathf.Max(0f, ingredient.targetMl);
                    }
                }

                float requiredMl = Mathf.Max(ingredientTotal, recipe.maxTotalMl);
                Require(requiredMl <= profile.CapacityMl + FloatTolerance,
                    path + " requires " + requiredMl + " ml but "
                        + profile.SourceSpriteName + " only declares " + profile.CapacityMl + " ml.");
            }
        }

        private static void ValidateProductionFallback()
        {
            GameObject root = new GameObject("ProductionGlassFallbackValidation");
            try
            {
                root.AddComponent<EdgeCollider2D>();
                root.AddComponent<BoxCollider2D>().isTrigger = true;
                GlassController glass = root.AddComponent<GlassController>();
                Require(string.Equals(glass.GlassId, "rock", StringComparison.OrdinalIgnoreCase),
                    "The production glass fallback must remain rock.");
                Require(Mathf.Approximately(glass.CapacityMl, 200f),
                    "The production glass fallback capacity must remain 200 ml.");
                Require(glass.ActiveCollisionProfile == null,
                    "A production glass without layered art received a collision profile.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ValidateLayeredFactoryOutput()
        {
            BusinessBartendingSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessBartendingSettings>(SettingsPath);
            Require(settings != null, "Business bartending settings are missing: " + SettingsPath);

            string[] definitionNames = { "Rock", "Martini", "Highball", "Hurricane" };
            GameObject parent = new GameObject("LayeredGlassFactoryValidation");
            try
            {
                for (int definitionIndex = 0;
                    definitionIndex < definitionNames.Length;
                    definitionIndex++)
                {
                    string path = DefinitionRoot + definitionNames[definitionIndex] + ".asset";
                    GlassDef definition = AssetDatabase.LoadAssetAtPath<GlassDef>(path);
                    Require(definition != null, "Glass definition is missing: " + path);

                    Sprite[] cabinetLayers = definition.GetCabinetLayers();
                    Sprite[] worldLayers = definition.GetWorldLayers();
                    Require(cabinetLayers.Length == ExpectedLayerOrders.Length,
                        path + " must provide four cabinet layers.");
                    Require(worldLayers.Length == ExpectedLayerOrders.Length,
                        path + " must provide four world layers.");
                    Require(definition.GetCollisionReferenceSprite() != null,
                        path + " has no collision reference sprite.");

                    for (int layerIndex = 0; layerIndex < worldLayers.Length; layerIndex++)
                    {
                        Require(cabinetLayers[layerIndex] != null,
                            path + " has a missing cabinet layer at index " + layerIndex + ".");
                        Require(worldLayers[layerIndex] != null,
                            path + " has a missing world layer at index " + layerIndex + ".");
                        Require(worldLayers[layerIndex].rect.size == GlassCollisionProfiles.SourcePixelSize,
                            path + " world layer " + layerIndex + " does not use the 310x590 canvas.");
                    }

                    GlassController glass = ToolCabinetWorldFactory.CreateGlass(
                        definition,
                        parent.transform,
                        settings,
                        0,
                        1f,
                        out GameObject instance);
                    Require(glass != null && instance != null,
                        path + " could not be created by ToolCabinetWorldFactory.");
                    try
                    {
                        int activeLayerCount = 0;
                        for (int layerIndex = 0;
                            layerIndex < ExpectedLayerOrders.Length;
                            layerIndex++)
                        {
                            Transform layer = instance.transform.Find(
                                "__ToolCabinetGlassLayer_" + layerIndex);
                            SpriteRenderer renderer = layer != null
                                ? layer.GetComponent<SpriteRenderer>()
                                : null;
                            Require(renderer != null && renderer.enabled,
                                path + " did not create active glass layer " + layerIndex + ".");
                            Require(renderer.sprite == worldLayers[layerIndex],
                                path + " created the wrong sprite at layer " + layerIndex + ".");
                            Require(renderer.sortingOrder == ExpectedLayerOrders[layerIndex],
                                path + " created the wrong sorting order at layer " + layerIndex + ".");
                        }

                        SpriteRenderer[] renderers =
                            instance.GetComponentsInChildren<SpriteRenderer>(true);
                        for (int i = 0; i < renderers.Length; i++)
                        {
                            if (renderers[i] != null && renderers[i].enabled)
                                activeLayerCount++;
                        }
                        Require(activeLayerCount == ExpectedLayerOrders.Length,
                            path + " must have exactly four active glass SpriteRenderers.");
                        Require(glass.ActiveCollisionProfile != null
                            && string.Equals(
                                glass.ActiveCollisionProfile.GlassId,
                                definition.glassId,
                                StringComparison.OrdinalIgnoreCase),
                            path + " did not receive its collision profile.");
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static void ValidateTriggerInsideOpenEdge(
            GlassCollisionProfileDefinition profile,
            Rect trigger,
            int triggerIndex)
        {
            Vector2[] samples =
            {
                trigger.center,
                new Vector2(trigger.xMin, trigger.yMin),
                new Vector2(trigger.xMax, trigger.yMin),
                new Vector2(trigger.xMin, trigger.yMax),
                new Vector2(trigger.xMax, trigger.yMax)
            };

            for (int i = 0; i < samples.Length; i++)
            {
                Require(IsInsideOpenEdge(profile.EdgePathPixels, samples[i], 3f),
                    profile.SourceSpriteName + " content trigger " + triggerIndex
                        + " extends outside the hand-authored open edge.");
            }
        }

        private static void ValidateEdgeAgainstSourcePixels(
            string assetPath,
            GlassCollisionProfileDefinition profile)
        {
            byte[] bytes = File.ReadAllBytes(Path.GetFullPath(assetPath));
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Require(source.LoadImage(bytes, false),
                    profile.SourceSpriteName + " source pixels could not be decoded.");
                Color32[] pixels = source.GetPixels32();
                const float maximumDistancePixels = 3f;
                IReadOnlyList<Vector2> edge = profile.EdgePathPixels;
                for (int segment = 0; segment < edge.Count - 1; segment++)
                {
                    Vector2 a = edge[segment];
                    Vector2 b = edge[segment + 1];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                    for (int step = 0; step <= steps; step++)
                    {
                        Vector2 sample = Vector2.Lerp(a, b, step / (float)steps);
                        Require(
                            HasVisiblePixelNear(
                                pixels,
                                source.width,
                                source.height,
                                sample,
                                maximumDistancePixels),
                            profile.SourceSpriteName + " collision edge is more than "
                                + maximumDistancePixels + " px from the drawn outline near "
                                + sample + ".");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static bool HasVisiblePixelNear(
            Color32[] pixels,
            int width,
            int height,
            Vector2 sample,
            float maximumDistance)
        {
            int radius = Mathf.CeilToInt(maximumDistance);
            int centerX = Mathf.RoundToInt(sample.x);
            int centerY = Mathf.RoundToInt(sample.y);
            float maximumDistanceSquared = maximumDistance * maximumDistance;
            for (int y = Mathf.Max(0, centerY - radius);
                y <= Mathf.Min(height - 1, centerY + radius);
                y++)
            {
                for (int x = Mathf.Max(0, centerX - radius);
                    x <= Mathf.Min(width - 1, centerX + radius);
                    x++)
                {
                    Vector2 offset = new Vector2(x, y) - sample;
                    if (offset.sqrMagnitude > maximumDistanceSquared)
                        continue;

                    Color32 color = pixels[y * width + x];
                    if (color.a >= 100)
                        return true;
                }
            }

            return false;
        }

        private static bool IsInsideOpenEdge(
            IReadOnlyList<Vector2> edge,
            Vector2 point,
            float tolerance)
        {
            float left = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            int intersections = 0;

            for (int i = 0; i < edge.Count - 1; i++)
            {
                Vector2 a = edge[i];
                Vector2 b = edge[i + 1];
                float minY = Mathf.Min(a.y, b.y);
                float maxY = Mathf.Max(a.y, b.y);
                if (point.y < minY - tolerance || point.y > maxY + tolerance)
                    continue;
                if (Mathf.Abs(a.y - b.y) <= 0.0001f)
                    continue;

                float t = Mathf.InverseLerp(a.y, b.y, point.y);
                float x = Mathf.Lerp(a.x, b.x, t);
                left = Mathf.Min(left, x);
                right = Mathf.Max(right, x);
                intersections++;
            }

            return intersections >= 2
                && point.x >= left + tolerance
                && point.x <= right - tolerance;
        }

        private static void InvokeNonPublic(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method != null,
                target.GetType().Name + "." + methodName + " was not found.");
            method.Invoke(target, null);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private readonly struct ExpectedProfile
        {
            public ExpectedProfile(string glassId, float capacityMl)
            {
                GlassId = glassId;
                CapacityMl = capacityMl;
            }

            public string GlassId { get; }
            public float CapacityMl { get; }
        }

        private sealed class FixedServeTarget : IBartendingServeTarget
        {
            private readonly Rect screenRect;

            public FixedServeTarget(Rect screenRect)
            {
                this.screenRect = screenRect;
            }

            public bool TryGetServeTargetScreenRect(out Rect targetRect)
            {
                targetRect = screenRect;
                return true;
            }
        }
    }
}
