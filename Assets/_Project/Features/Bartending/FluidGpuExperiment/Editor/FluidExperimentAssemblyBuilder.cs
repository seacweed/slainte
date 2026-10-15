using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Bartending.FluidGpuExperiment.Editor
{
    /// <summary>Additive, idempotent authoring. Existing designer values and scene overrides are preserved.</summary>
    public static class FluidExperimentAssemblyBuilder
    {
        public const string Root = FluidExperimentBuilder.Root;
        public const string RuntimeDirectory = Root + "/Prefabs/Runtime";
        public const string RuntimePrefabPath = RuntimeDirectory + "/FluidExperimentRuntime.prefab";
        public const string EnvironmentPrefabPath = RuntimeDirectory + "/FluidExperimentEnvironment.prefab";
        public const string ShardPrefabPath = RuntimeDirectory + "/GlassShard.prefab";
        public const string ShakerPartPrefabPath = RuntimeDirectory + "/ShakerPart.prefab";
        public const string CatalogPath = Root + "/Data/FluidExperimentCatalog.asset";
        public const string SplashProfilePath = Root + "/Data/DefaultBreakageSplash.asset";
        public const string ShakerProfilePath = Root + "/Data/ShakerParts.asset";
        public const string SampleScenePath = Root + "/Scenes/FluidRuntimeSample.unity";
        public const string EmptySceneTemplatePath = Root + "/Editor/Templates/EmptyFluidScene.unity";

        [MenuItem("Slainte/Fluid GPU Experiment/Build Reusable Runtime Assets")]
        public static void BuildAll()
        {
            RequireEditMode();
            EnsureFolder(RuntimeDirectory);
            var shard = BuildShardTemplate();
            var part = BuildPartTemplate();
            var splash = AssetDatabase.LoadAssetAtPath<FluidExperimentSplashProfile>(SplashProfilePath);
            if (splash == null)
            {
                splash = ScriptableObject.CreateInstance<FluidExperimentSplashProfile>();
                AssetDatabase.CreateAsset(splash, SplashProfilePath);
            }
            MigrateBreakageGeometry(shard);
            BindShakerTemplate(part);
            var catalog = BuildCatalog();
            BuildEnvironment();
            BuildRuntime(catalog, splash);
            AssetDatabase.SaveAssetIfDirty(catalog);
            if (!File.Exists(SampleScenePath))
            {
                Scene sample = CreateWorkingScene(SampleScenePath);
                bool saved = false;
                try
                {
                    CreateSandbox(sample, AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Glass_hurricane.prefab"));
                    var rig = sample.GetRootGameObjects().Select(root => root.GetComponent<FluidExperimentRuntimeRig>()).First(x => x != null);
                    rig.seedLiquids = true; rig.ApplyConfiguration();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(rig.world);
                    Place(rig, AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Bottle_item_1002.prefab"), new Vector2(-4, 2));
                    Place(rig, AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Shaker.prefab"), new Vector2(3, 2));
                    Place(rig, AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Spoon.prefab"), new Vector2(5, 2));
                    if (!EditorSceneManager.SaveScene(sample, SampleScenePath)) throw new IOException("Could not save runtime sample scene.");
                    saved = true;
                }
                finally
                {
                    EditorSceneManager.CloseScene(sample, true);
                    if (!saved) AssetDatabase.DeleteAsset(SampleScenePath);
                }
            }
            Debug.Log("[FluidRuntimeAssembly] Reusable runtime, environment, templates and catalog are ready. Existing assets preserved.");
        }

        [MenuItem("Slainte/Fluid GPU Experiment/Open Runtime Sample")]
        public static void OpenSample()
        {
            RequireEditMode();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildAll();
            EditorSceneManager.OpenScene(SampleScenePath);
        }

        [MenuItem("Slainte/Fluid GPU Experiment/Assemble New Runtime Scene")]
        private static void AssembleNewSceneMenu() => AssembleNewScene();

        public static FluidExperimentRuntimeRig AssembleNewScene()
        {
            RequireEditMode();
            BuildAll();
            EnsureFolder(Root + "/Scenes/Generated");
            string path = AssetDatabase.GenerateUniqueAssetPath(Root + "/Scenes/Generated/FluidRuntime.unity");
            Scene scene = CreateWorkingScene(path);
            SceneManager.SetActiveScene(scene);
            try
            {
                var rig = CreateSandbox(scene, AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Glass_hurricane.prefab"));
                if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Could not save the assembled runtime scene.");
                Selection.activeGameObject = rig.gameObject;
                return rig;
            }
            catch
            {
                EditorSceneManager.CloseScene(scene, true); AssetDatabase.DeleteAsset(path);
                throw;
            }
        }

        public static Scene CreateWorkingScene(string path)
        {
            RequireEditMode();
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".unity", StringComparison.Ordinal)
                || path.Split('/').Contains("..") || File.Exists(path))
                throw new ArgumentException("A new .unity path below Assets is required.", nameof(path));
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            if (!AssetDatabase.CopyAsset(EmptySceneTemplatePath, path))
                throw new IOException("Could not copy the empty runtime scene template to " + path);
            try { return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); }
            catch { AssetDatabase.DeleteAsset(path); throw; }
        }

        public static FluidExperimentRuntimeRig CreateSandbox(Scene target, GameObject testedPrefab)
        {
            RequireEditMode();
            if (!target.IsValid() || !target.isLoaded) throw new ArgumentException("A loaded destination scene is required.", nameof(target));
            var runtime = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
            var environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPrefabPath);
            if (runtime == null || environment == null)
                throw new InvalidOperationException("Build reusable runtime assets before creating a sandbox.");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(runtime, target);
            var rig = root.GetComponent<FluidExperimentRuntimeRig>();
            var environmentObject = (GameObject)PrefabUtility.InstantiatePrefab(environment, target);
            environmentObject.transform.SetParent(root.transform, false);
            rig.environment = environmentObject.GetComponent<FluidExperimentEnvironmentBinding>();
            var cameraObject = new GameObject("Fluid Runtime Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, target);
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.position = new Vector3(0, 4, -20);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 7;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .085f, .1f);
            camera.cullingMask = 1 << rig.world.itemLayer;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            rig.inputCamera = camera;
            rig.ApplyConfiguration();
            if (testedPrefab != null) Place(rig, testedPrefab, new Vector2(0, 2));
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig.world);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig.liquid);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rig.interactor);
            EditorSceneManager.MarkSceneDirty(target);
            return rig;
        }

        public static GameObject Place(FluidExperimentRuntimeRig rig, GameObject prefab, Vector2 position)
        {
            if (rig == null || prefab == null) throw new ArgumentNullException(rig == null ? nameof(rig) : nameof(prefab));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rig.world.transform);
            instance.transform.position = position;
            var body = instance.GetComponent<FluidExperimentBody>();
            if (body != null && body.collisionProfile != null)
            {
                float bottom = float.PositiveInfinity;
                foreach (var hull in body.collisionProfile.solids)
                    foreach (var point in hull.points) bottom = Mathf.Min(bottom, instance.transform.TransformPoint(point).y);
                if (float.IsFinite(bottom))
                {
                    float floor = rig.environment != null && rig.environment.table != null
                        ? rig.environment.table.transform.TransformPoint(rig.environment.table.offset
                            + Vector2.up * rig.environment.table.size.y * .5f).y : 0;
                    instance.transform.position += Vector3.up * (floor + .05f - bottom);
                }
            }
            var breakable = instance.GetComponent<FluidExperimentBreakableGlass>();
            if (breakable != null && breakable.splashProfile == null)
            {
                breakable.splashProfile = rig.defaultSplashProfile;
                PrefabUtility.RecordPrefabInstancePropertyModifications(breakable);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            return instance;
        }

        private static GameObject BuildShardTemplate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ShardPrefabPath);
            if (existing != null) return existing;
            var root = new GameObject("GlassShard");
            try
            {
                root.SetActive(false);
                root.AddComponent<FluidExperimentGlassShard>();
                root.AddComponent<Rigidbody2D>();
                root.AddComponent<PolygonCollider2D>();
                var art = new GameObject("Art"); art.transform.SetParent(root.transform, false);
                art.AddComponent<SpriteRenderer>();
                return PrefabUtility.SaveAsPrefabAsset(root, ShardPrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static GameObject BuildPartTemplate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ShakerPartPrefabPath);
            if (existing != null) return existing;
            var root = new GameObject("ShakerPart");
            try
            {
                root.SetActive(false);
                root.AddComponent<FluidExperimentShakerPart>();
                root.AddComponent<Rigidbody2D>().simulated = false;
                root.AddComponent<BoxCollider2D>().enabled = false;
                return PrefabUtility.SaveAsPrefabAsset(root, ShakerPartPrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void MigrateBreakageGeometry(GameObject shardTemplate)
        {
            string path = Root + "/Data/HurricaneBreakage.asset";
            var profile = AssetDatabase.LoadAssetAtPath<FluidExperimentBreakageProfile>(path);
            if (profile == null) throw new InvalidOperationException("Hurricane breakage profile missing.");
            bool changed = false;
            if (profile.generatedGeometry == null)
            {
                string geometryPath = Root + "/Data/HurricaneShardGeometry.asset";
                var geometry = AssetDatabase.LoadAssetAtPath<FluidExperimentShardGeometry>(geometryPath);
                if (geometry == null)
                {
                    geometry = ScriptableObject.CreateInstance<FluidExperimentShardGeometry>();
                    geometry.definitions = profile.shards;
                    geometry.sourceDescription = "Migrated authored hurricane shard silhouettes.";
                    AssetDatabase.CreateAsset(geometry, geometryPath);
                }
                profile.SetGeneratedGeometry(geometry); changed = true;
            }
            if (profile.shardTemplate == null) { profile.shardTemplate = shardTemplate; changed = true; }
            if (changed) { EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile); }
        }

        private static void BindShakerTemplate(GameObject partTemplate)
        {
            string path = Root + "/Prefabs/Shaker.prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return;
            var authored = asset.GetComponent<FluidExperimentBody>();
            if (authored.shakerPartTemplate != null && authored.shakerProfile != null
                && authored.shakerStrainerArt != null && authored.shakerCapArt != null) return;
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var body = contents.GetComponent<FluidExperimentBody>();
                if (body.shakerPartTemplate == null) body.shakerPartTemplate = partTemplate;
                foreach (var art in contents.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    string spriteName = art.sprite != null ? art.sprite.name : "";
                    if (body.shakerStrainerArt == null && (spriteName == "cobbler_strainer" || art.name == "ToolArt_4")) body.shakerStrainerArt = art;
                    if (body.shakerCapArt == null && (spriteName == "cobbler_lid" || art.name == "ToolArt_5")) body.shakerCapArt = art;
                }
                if (body.shakerProfile == null) body.shakerProfile = BuildShakerProfile(body);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static FluidExperimentShakerProfile BuildShakerProfile(FluidExperimentBody body)
        {
            var profile = AssetDatabase.LoadAssetAtPath<FluidExperimentShakerProfile>(ShakerProfilePath);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<FluidExperimentShakerProfile>();
            Vector2 Point(SpriteRenderer art, Vector2 pixel, Rect fallback)
            {
                if (art == null || art.sprite == null)
                    return new Vector2(Mathf.Lerp(fallback.xMin, fallback.xMax, (pixel.x - 53f) / 203f),
                        Mathf.Lerp(fallback.yMin, fallback.yMax, (pixel.y - 271f) / 162f));
                Bounds bounds = art.sprite.bounds;
                Vector3 local = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, pixel.x / 310f),
                    Mathf.Lerp(bounds.min.y, bounds.max.y, pixel.y / 590f), 0);
                return body.transform.InverseTransformPoint(art.transform.TransformPoint(local));
            }
            Rect PartRect(SpriteRenderer art, Rect pixel, Rect fallback)
            {
                if (art == null || art.sprite == null) return fallback;
                Vector2 a = Point(art, pixel.min, fallback), b = Point(art, pixel.max, fallback);
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            }
            profile.strainerRect = PartRect(body.shakerStrainerArt, new Rect(53, 271, 203, 162), profile.strainerRect);
            profile.capRect = PartRect(body.shakerCapArt, new Rect(105, 370, 99, 78), profile.capRect);
            Vector2[] bowl = body.collisionProfile != null ? body.collisionProfile.interior : body.liquidWall;
            if (bowl == null || bowl.Length < 3) throw new InvalidOperationException("Shaker requires an authored bowl before creating its part profile.");
            Vector2 Guide(float x, float y) => Point(body.shakerStrainerArt, new Vector2(x, y), profile.strainerRect);
            profile.leftGuide = new[] { bowl[0], Guide(67, 344), Guide(91, 369), Guide(121, 383), Guide(121, 433) };
            profile.rightGuide = new[] { bowl[bowl.Length - 1], Guide(243, 344), Guide(219, 369), Guide(189, 383), Guide(189, 433) };
            AssetDatabase.CreateAsset(profile, ShakerProfilePath);
            return profile;
        }

        private static FluidExperimentAuthoringCatalog BuildCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FluidExperimentAuthoringCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<FluidExperimentAuthoringCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            var entries = new List<FluidExperimentAuthoringCatalog.Entry>(catalog.entries);
            foreach (string path in Directory.GetFiles(Root + "/Prefabs", "*.prefab").OrderBy(path => path, StringComparer.Ordinal))
            {
                string normalized = path.Replace('\\', '/');
                string guid = AssetDatabase.AssetPathToGUID(normalized);
                if (entries.Any(entry => entry != null && entry.id == guid)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(normalized);
                var body = prefab.GetComponent<FluidExperimentBody>();
                var source = prefab.GetComponent<FluidExperimentGarnishSource>();
                entries.Add(new FluidExperimentAuthoringCatalog.Entry { id = guid, prefab = prefab,
                    displayName = body != null ? body.displayName : source != null ? source.displayName : prefab.name,
                    category = body != null ? body.kind.ToString() : "Garnish Source",
                    ingredient = body != null ? body.ingredient : null });
            }
            if (entries.Count != catalog.entries.Length)
            { catalog.entries = entries.ToArray(); EditorUtility.SetDirty(catalog); }
            return catalog;
        }

        private static void BuildEnvironment()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPrefabPath) != null) return;
            var root = new GameObject("FluidExperimentEnvironment");
            try
            {
                var binding = root.AddComponent<FluidExperimentEnvironmentBinding>();
                binding.table = Box(root.transform, "Table", new Vector2(0, -.25f), new Vector2(14, .5f));
                binding.leftBoundary = Box(root.transform, "LeftBoundary", new Vector2(-9, 4), new Vector2(.4f, 20));
                binding.rightBoundary = Box(root.transform, "RightBoundary", new Vector2(9, 4), new Vector2(.4f, 20));
                binding.topBoundary = Box(root.transform, "TopBoundary", new Vector2(0, 11), new Vector2(18, .4f));
                binding.solidFloors = new Collider2D[] { binding.table, binding.leftBoundary, binding.rightBoundary, binding.topBoundary };
                var line = binding.table.gameObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false; line.positionCount = 2; line.widthMultiplier = .04f;
                line.SetPositions(new[] { new Vector3(-7, .25f), new Vector3(7, .25f) });
                line.startColor = line.endColor = new Color(.45f, .55f, .6f);
                line.sortingOrder = -10;
                // Reuse the authored table's material instead of creating a transient material reference.
                Scene reference = EditorSceneManager.OpenPreviewScene(FluidExperimentBuilder.ScenePath);
                try
                {
                    var authored = reference.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<LineRenderer>()).FirstOrDefault();
                    if (authored != null) line.sharedMaterial = authored.sharedMaterial;
                }
                finally { EditorSceneManager.ClosePreviewScene(reference); }
                PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static BoxCollider2D Box(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name) { layer = 29 };
            go.transform.SetParent(parent, false); go.transform.localPosition = position;
            var collider = go.AddComponent<BoxCollider2D>(); collider.size = size;
            return collider;
        }

        private static void BuildRuntime(FluidExperimentAuthoringCatalog catalog, FluidExperimentSplashProfile splash)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath) != null) return;
            Scene reference = EditorSceneManager.OpenPreviewScene(FluidExperimentBuilder.ScenePath);
            var root = new GameObject("FluidExperimentRuntime");
            try
            {
                var source = reference.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<FluidExperimentWorld>()).First();
                var world = root.AddComponent<FluidExperimentWorld>();
                var liquid = root.AddComponent<FluidExperimentGpuLiquid>();
                EditorUtility.CopySerialized(source.liquid, liquid);
                var interactor = root.AddComponent<FluidExperimentInteractor>();
                EditorUtility.CopySerialized(source.interactor, interactor);
                var effects = root.AddComponent<FluidExperimentEffects>();
                effects.cosmeticShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/FluidExperimentEffects.shader");
                var rig = root.AddComponent<FluidExperimentRuntimeRig>();
                rig.world = world; rig.liquid = liquid; rig.interactor = interactor; rig.effects = effects;
                rig.liquidSettings = source.liquid.settings; rig.catalog = catalog; rig.defaultSplashProfile = splash;
                rig.defaultIngredient = catalog.entries.Select(entry => entry.ingredient).FirstOrDefault(item => item != null);
                rig.ApplyConfiguration();
                PrefabUtility.SaveAsPrefabAsset(root, RuntimePrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(reference);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before authoring runtime assets.");
        }
    }
}
