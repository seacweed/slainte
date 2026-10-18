using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment.Editor
{
    /// <summary>Authors the isolated F/G dispenser without modifying any source artwork or legacy item.</summary>
    public static class FluidExperimentDispenserBuilder
    {
        private const string Root = FluidExperimentBuilder.Root;
        public const string PrefabPath = Root + "/Prefabs/Dispenser.prefab";
        private const float PixelsPerUnit = 200;
        private const byte AlphaThreshold = 8;
        private const float SimplificationPixels = 1.25f;
        // Bottom-left pixel coordinates, matching Texture2D.GetPixels32 and sprite pivots.
        private static readonly Vector2 PivotPixels = new Vector2(159.5f, 329);

        // Assembly/rebinding must not rebuild a prefab that a designer has already tuned.
        public static FluidExperimentDispenser EnsurePrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Build();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            var dispenser = prefab != null ? prefab.GetComponent<FluidExperimentDispenser>() : null;
            if (dispenser == null) throw new InvalidOperationException("Dispenser prefab is missing its controller.");
            return dispenser;
        }

        public static bool BindIfMissing(FluidExperimentWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.dispenserPrefab != null) return false;
            world.dispenserPrefab = EnsurePrefab();
            EditorUtility.SetDirty(world);
            return true;
        }

        [MenuItem("Slainte/Fluid GPU Experiment/Build Dispenser Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string art = Root + "/Art/Dispenser/";
            Vector2Int size;
            Vector2[] pixels = ReadSilhouette(art + "dispenser_None.png", out size);
            if (size != new Vector2Int(310, 590))
                throw new InvalidOperationException("Dispenser artwork must retain its original 310 x 590 canvas.");
            var none = Import(art + "dispenser_None.png", size);
            var ice = Import(art + "dispenser_Ice.png", size);
            var hot = Import(art + "dispenser_Hot.png", size);
            var profile = BuildProfile(pixels);
            var water = BuildHotWater(hot);
            var hoseMaterial = BuildHoseMaterial();
            var surface = BuildSurface();
            var icePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Ice.prefab");
            if (icePrefab == null) throw new InvalidOperationException("Existing experiment ice prefab is missing.");

            var prefab = new GameObject("Dispenser");
            prefab.SetActive(false);
            prefab.layer = 29;
            try
            {
                var rigidbody = prefab.AddComponent<Rigidbody2D>();
                rigidbody.bodyType = RigidbodyType2D.Kinematic;
                rigidbody.useFullKinematicContacts = true;
                rigidbody.mass = .35f;
                rigidbody.gravityScale = 0;
                rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
                var solid = prefab.AddComponent<PolygonCollider2D>();
                solid.points = profile.solids[0].points;
                solid.sharedMaterial = surface;
                var body = prefab.AddComponent<FluidExperimentBody>();
                body.kind = LabItemKind.Dispenser;
                body.displayName = "Ice / Hot Dispenser";
                body.capacityMl = body.remainingMl = body.pourMlPerSecond = 0;
                body.iceStock = 0;
                body.collisionProfile = profile;
                body.solidColliders = new Collider2D[] { solid };
                body.pickCollider = solid;
                body.ApplyCollisionProfile();

                var artObject = new GameObject("DispenserArt");
                artObject.layer = 29;
                artObject.transform.SetParent(prefab.transform, false);
                var renderer = artObject.AddComponent<SpriteRenderer>();
                renderer.sprite = none;
                renderer.sharedMaterial = icePrefab.GetComponentInChildren<SpriteRenderer>().sharedMaterial;
                renderer.sortingOrder = 30;
                var controller = prefab.AddComponent<FluidExperimentDispenser>();
                controller.body = body;
                controller.bodyRenderer = renderer;
                controller.noneSprite = none;
                controller.iceSprite = ice;
                controller.hotSprite = hot;
                controller.hotWater = water;
                controller.icePrefab = icePrefab.GetComponent<FluidExperimentBody>();
                controller.hoseMaterial = hoseMaterial;
                controller.iceButtonLocal = new Rect(-.2075f, .69f, .39f, .39f);
                controller.hotButtonLocal = new Rect(-.2075f, .19f, .39f, .39f);
                controller.mouthLocal = profile.mouth;
                controller.hoseAttachmentLocal = new Vector2(0, 1.30f);
                controller.dockPosition = new Vector2(11.6f, 7.3f);
                controller.hoseAnchor = new Vector2(11.6f, 9.7f);
                controller.capacityMl = 5000;
                controller.hotFlowMlPerSecond = 40;
                controller.iceInterval = .3f;
                controller.returnDuration = .5f;
                controller.exitSpeed = 2.4f;
                controller.hoseSegments = 20;
                prefab.transform.position = controller.dockPosition;
                prefab.SetActive(true);
                if (PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath) == null)
                    throw new IOException("Cannot save dispenser prefab.");
            }
            finally { UnityEngine.Object.DestroyImmediate(prefab); }
            AssetDatabase.SaveAssets();
            Debug.Log("[DispenserBuilder] Authored original sprite modes, " + pixels.Length
                + "-point silhouette, hot-water definition and dispenser prefab; source PNG bytes retained.");
        }

        [MenuItem("Slainte/Fluid GPU Experiment/Install Dispenser Into Comparison")]
        public static void InstallIntoComparison()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsurePrefab();
            var scene = EditorSceneManager.OpenScene(FluidExperimentBuilder.ScenePath);
            var world = UnityEngine.Object.FindFirstObjectByType<FluidExperimentWorld>();
            if (world == null) throw new InvalidOperationException("Experiment world missing.");
            if (!BindIfMissing(world)) return;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, FluidExperimentBuilder.ScenePath))
                throw new IOException("Comparison scene save failed.");
            Debug.Log("[DispenserBuilder] Connected runtime dispenser prefab; existing comparison content retained.");
        }

        private static FluidExperimentCollisionProfile BuildProfile(Vector2[] pixels)
        {
            const string path = Root + "/Data/CollisionProfiles/Dispenser.asset";
            var profile = AssetDatabase.LoadAssetAtPath<FluidExperimentCollisionProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<FluidExperimentCollisionProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            profile.solids = new[] { new FluidExperimentHull
            { points = pixels.Select(p => (p - PivotPixels) / PixelsPerUnit).ToArray() } };
            profile.lid = Array.Empty<Vector2>();
            profile.interior = Array.Empty<Vector2>();
            profile.mouth = new Vector2(0, -1.325f);
            profile.exitDirection = Vector2.down;
            profile.sourcePixelSize = 1 / PixelsPerUnit;
            profile.sourceDescription = "dispenser_None.png alpha > 8; 1.25 pixel contour simplification; "
                + "no glow included; 200 pixels/unit; original PNG unchanged.";
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static ItemDef BuildHotWater(Sprite icon)
        {
            const string path = Root + "/Data/DispenserHotWater.asset";
            var source = AssetDatabase.LoadAssetAtPath<ItemDef>("Assets/Resources/Bartending/Items/item_1014.asset");
            if (source == null) throw new InvalidOperationException("Existing hot-water ingredient missing.");
            var water = AssetDatabase.LoadAssetAtPath<ItemDef>(path);
            if (water == null)
            {
                water = ScriptableObject.CreateInstance<ItemDef>();
                AssetDatabase.CreateAsset(water, path);
            }
            EditorUtility.CopySerialized(source, water);
            water.name = "DispenserHotWater";
            // Preserve the existing ingredient ID so mixed composition remains recipe-compatible.
            water.icon = icon;
            water.capacityMl = 5000;
            water.servingTemperatureC = 80;
            water.overrideBottleGeometry = water.overrideBottleLiquidSpawn = water.overrideBottleClickCollider = false;
            EditorUtility.SetDirty(water);
            return water;
        }

        private static Material BuildHoseMaterial()
        {
            const string path = Root + "/Data/DispenserHose.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var source = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Data/TableLine.mat");
            if (source == null) throw new InvalidOperationException("Existing line material is missing.");
            material = new Material(source) { name = "DispenserHose", color = Color.white };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static PhysicsMaterial2D BuildSurface()
        {
            const string path = Root + "/Data/DispenserSurface.physicsMaterial2D";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
            if (material != null) return material;
            material = new PhysicsMaterial2D("DispenserSurface") { friction = .15f, bounciness = 0 };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Sprite Import(string path, Vector2Int expectedSize)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))
                    || texture.width != expectedSize.x || texture.height != expectedSize.y)
                    throw new IOException("Dispenser mode canvas does not match: " + path);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.isReadable = false;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = PivotPixels / (Vector2)expectedSize;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
            if (sprite == null) throw new InvalidOperationException("Dispenser sprite import failed: " + path);
            return sprite;
        }
        private static Vector2[] ReadSilhouette(string path, out Vector2Int size)
        {
            // Read original PNG alpha; imported texture settings must not alter geometry.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) throw new IOException("Cannot decode " + path);
                size = new Vector2Int(texture.width, texture.height);
                var colors = texture.GetPixels32();
                int width = size.x, height = size.y;
                bool Solid(int x, int y) => x >= 0 && y >= 0 && x < width && y < height
                    && colors[y * width + x].a > AlphaThreshold;
                var edges = new Dictionary<Vector2Int, List<Vector2Int>>();
                void Add(int ax, int ay, int bx, int by)
                {
                    var a = new Vector2Int(ax, ay);
                    if (!edges.TryGetValue(a, out var ends)) edges[a] = ends = new List<Vector2Int>();
                    ends.Add(new Vector2Int(bx, by));
                }
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (!Solid(x, y)) continue;
                    if (!Solid(x, y - 1)) Add(x, y, x + 1, y);
                    if (!Solid(x + 1, y)) Add(x + 1, y, x + 1, y + 1);
                    if (!Solid(x, y + 1)) Add(x + 1, y + 1, x, y + 1);
                    if (!Solid(x - 1, y)) Add(x, y + 1, x, y);
                }
                var loops = new List<Vector2[]>();
                while (edges.Count > 0)
                {
                    var start = edges.Keys.OrderBy(p => p.y).ThenBy(p => p.x).First();
                    var point = start;
                    var incoming = Vector2Int.right;
                    var loop = new List<Vector2>();
                    do
                    {
                        loop.Add(point);
                        if (!edges.TryGetValue(point, out var ends))
                            throw new InvalidOperationException("Open silhouette boundary: " + path);
                        // At a diagonal pixel junction prefer the left turn around the same
                        // connected region instead of producing a crossing polygon.
                        var next = ends.OrderByDescending(p => Turn(incoming, p - point)).First();
                        ends.Remove(next);
                        if (ends.Count == 0) edges.Remove(point);
                        incoming = next - point;
                        point = next;
                    } while (point != start);
                    loops.Add(loop.ToArray());
                }
                var outside = loops.Where(p => SignedArea(p) > 0).OrderByDescending(SignedArea).ToArray();
                if (outside.Length == 0) throw new InvalidOperationException("Empty dispenser: " + path);
                float area = SignedArea(outside[0]);
                // Ignore isolated antialiasing flecks while retaining the original artwork.
                float negligiblePixels = Mathf.Max(2, area * .001f);
                if (outside.Skip(1).Sum(p => SignedArea(p)) > negligiblePixels)
                    throw new InvalidOperationException("Dispenser has significant disconnected islands: " + path);
                if (loops.Where(p => SignedArea(p) < 0).Sum(p => -SignedArea(p)) > negligiblePixels)
                    throw new InvalidOperationException("Dispenser has significant holes needing multiple contours: " + path);
                for (float tolerance = SimplificationPixels; tolerance >= .09f; tolerance *= .5f)
                {
                    var simplified = SimplifyClosed(outside[0], tolerance);
                    if (simplified.Length >= 3 && IsSimple(simplified)) return simplified;
                }
                throw new InvalidOperationException("Could not produce a simple concave silhouette: " + path);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static float Turn(Vector2Int a, Vector2Int b) =>
            Mathf.Atan2(a.x * b.y - a.y * b.x, a.x * b.x + a.y * b.y);

        private static Vector2[] SimplifyClosed(Vector2[] points, float tolerance)
        {
            int split = 1;
            for (int i = 2; i < points.Length; i++)
                if ((points[i] - points[0]).sqrMagnitude > (points[split] - points[0]).sqrMagnitude) split = i;
            var result = new List<Vector2>();
            SimplifyOpen(points.Take(split + 1).ToArray(), tolerance, result);
            result.RemoveAt(result.Count - 1);
            SimplifyOpen(points.Skip(split).Concat(new[] { points[0] }).ToArray(), tolerance, result);
            result.RemoveAt(result.Count - 1);
            return result.ToArray();
        }

        private static void SimplifyOpen(Vector2[] points, float tolerance, List<Vector2> result)
        {
            var a = points[0];
            var b = points[points.Length - 1];
            var line = b - a;
            float largest = 0;
            int split = 0;
            for (int i = 1; i < points.Length - 1; i++)
            {
                float t = line.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(points[i] - a, line) / line.sqrMagnitude) : 0;
                float distance = (points[i] - (a + t * line)).sqrMagnitude;
                if (distance > largest) { largest = distance; split = i; }
            }
            if (largest > tolerance * tolerance)
            {
                SimplifyOpen(points.Take(split + 1).ToArray(), tolerance, result);
                result.RemoveAt(result.Count - 1);
                SimplifyOpen(points.Skip(split).ToArray(), tolerance, result);
            }
            else { result.Add(a); result.Add(b); }
        }

        private static bool IsSimple(Vector2[] points)
        {
            for (int i = 0; i < points.Length; i++)
            for (int j = i + 1; j < points.Length; j++)
            {
                if (j == i + 1 || (i == 0 && j == points.Length - 1)) continue;
                Vector2 a = points[i], b = points[(i + 1) % points.Length];
                Vector2 c = points[j], d = points[(j + 1) % points.Length];
                if (Cross(b - a, c - a) * Cross(b - a, d - a) <= 0
                    && Cross(d - c, a - c) * Cross(d - c, b - c) <= 0
                    && Mathf.Max(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)) <= Mathf.Min(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x))
                    && Mathf.Max(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)) <= Mathf.Min(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y))) return false;
            }
            return true;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static float SignedArea(Vector2[] polygon)
        {
            float area = 0;
            for (int i = 0; i < polygon.Length; i++) area += Cross(polygon[i], polygon[(i + 1) % polygon.Length]);
            return area * .5f;
        }
    }
}
