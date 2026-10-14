using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment.Editor
{
    /// <summary>Authors collision silhouettes from original PNG alpha without rewriting the artwork.</summary>
    public static class FluidExperimentBreakageBuilder
    {
        private const string Root = FluidExperimentBuilder.Root;
        private const string PrefabPath = Root + "/Prefabs/Glass_hurricane.prefab";
        private const string ProfilePath = Root + "/Data/HurricaneBreakage.asset";
        private const byte AlphaThreshold = 8;
        private const float SimplificationPixels = 1.25f;

        [MenuItem("Slainte/Fluid GPU Experiment/Select Hurricane Breakage Settings")]
        public static void SelectSettings()
        {
            var profile = AssetDatabase.LoadAssetAtPath<FluidExperimentBreakageProfile>(ProfilePath);
            if (profile == null)
            {
                Debug.LogWarning("Hurricane breakage settings are missing. Run Build Hurricane Breakage first.");
                return;
            }
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        [MenuItem("Slainte/Fluid GPU Experiment/Build Hurricane Breakage")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var art = prefab.GetComponentsInChildren<SpriteRenderer>(true)
                    .First(r => r.name.StartsWith("GlassArt_", StringComparison.Ordinal) && r.sprite != null);
                if (art.transform.parent != prefab.transform || art.transform.localRotation != Quaternion.identity)
                    throw new InvalidOperationException("Hurricane art must use an unrotated direct child transform.");
                var original = art.sprite;
                var scale = (Vector2)art.transform.localScale;
                var profile = AssetDatabase.LoadAssetAtPath<FluidExperimentBreakageProfile>(ProfilePath);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<FluidExperimentBreakageProfile>();
                    AssetDatabase.CreateAsset(profile, ProfilePath);
                }
                var definitions = new List<FluidExperimentBreakageProfile.Shard>();
                for (int number = 1; number <= 16; number++)
                {
                    string path = Root + "/Art/Shards/Hurricane/hurricane_shard_" + number.ToString("00") + ".png";
                    Vector2Int size;
                    Vector2[] pixels = ReadSilhouette(path, out size);
                    if (original.rect.width != size.x || original.rect.height != size.y)
                        throw new InvalidOperationException("Shard canvas must match the original hurricane art: " + path);
                    Vector2 center = PolygonCentroid(pixels);
                    float units = 1f / original.pixelsPerUnit;
                    definitions.Add(new FluidExperimentBreakageProfile.Shard
                    {
                        sprite = Import(path, original.pixelsPerUnit, center / (Vector2)size),
                        localCenter = (Vector2)art.transform.localPosition
                            + Vector2.Scale((center - original.pivot) * units, scale),
                        outline = pixels.Select(p => Vector2.Scale((p - center) * units, scale)).ToArray(),
                        area = Mathf.Abs(SignedArea(pixels) * units * units * scale.x * scale.y),
                        spriteScale = scale
                    });
                    Debug.Log("[HurricaneBreakageBuilder] Shard " + number.ToString("00")
                        + ": " + pixels.Length + " silhouette points; area=" + definitions.Last().area);
                }
                profile.shards = definitions.ToArray();
                if (profile.shardMaterial == null) profile.shardMaterial = BuildMaterial();
                EditorUtility.SetDirty(profile);
                var breakable = prefab.GetComponent<FluidExperimentBreakableGlass>();
                if (breakable == null) breakable = prefab.AddComponent<FluidExperimentBreakableGlass>();
                breakable.profile = profile;
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
                AssetDatabase.SaveAssets();

                // The current comparison scene is a connected prefab instance. Do not reserialize it
                // unless an older disconnected hurricane actually needs its own component/reference.
                var scene = EditorSceneManager.OpenScene(FluidExperimentBuilder.ScenePath);
                bool changed = false;
                foreach (var sceneRoot in scene.GetRootGameObjects())
                foreach (var body in sceneRoot.GetComponentsInChildren<FluidExperimentBody>(true))
                {
                    if (body.name != "Glass_hurricane") continue;
                    var instance = body.GetComponent<FluidExperimentBreakableGlass>();
                    if (instance != null && instance.profile == profile) continue;
                    if (instance == null) instance = body.gameObject.AddComponent<FluidExperimentBreakableGlass>();
                    instance.profile = profile;
                    EditorUtility.SetDirty(instance);
                    if (PrefabUtility.IsPartOfPrefabInstance(instance))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                    changed = true;
                }
                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene, FluidExperimentBuilder.ScenePath))
                        throw new IOException("Comparison scene save failed.");
                }
                Debug.Log("[HurricaneBreakageBuilder] Authored 16 centroid-pivot shards; PNG bytes preserved. Scene changed=" + changed);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        private static PhysicsMaterial2D BuildMaterial()
        {
            const string path = Root + "/Data/HurricaneShard.physicsMaterial2D";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
            if (material != null) return material;
            material = new PhysicsMaterial2D("HurricaneShard") { friction = .25f, bounciness = .08f };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Sprite Import(string path, float pixelsPerUnit, Vector2 pivot)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.isReadable = false;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
            if (sprite == null) throw new InvalidOperationException("Shard sprite import failed: " + path);
            return sprite;
        }

        private static Vector2[] ReadSilhouette(string path, out Vector2Int size)
        {
            // Threshold deliberately includes the half-transparent 05/06/07 pieces. Reading the
            // original PNG also avoids compressed/imported texture alpha changing the collider.
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
                if (outside.Length == 0) throw new InvalidOperationException("Empty shard: " + path);
                float area = SignedArea(outside[0]);
                // A one-pixel alpha fleck occurs in source shard 15. Keep the artwork intact,
                // but do not create a second physical body for such antialiasing debris.
                float negligiblePixels = Mathf.Max(2, area * .001f);
                if (outside.Skip(1).Sum(p => SignedArea(p)) > negligiblePixels)
                    throw new InvalidOperationException("Shard has significant disconnected islands: " + path);
                if (loops.Where(p => SignedArea(p) < 0).Sum(p => -SignedArea(p)) > negligiblePixels)
                    throw new InvalidOperationException("Shard has significant holes needing multiple contours: " + path);
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
        private static Vector2 PolygonCentroid(Vector2[] polygon)
        {
            Vector2 sum = Vector2.zero;
            float area6 = 0;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                float cross = Cross(a, b);
                sum += (a + b) * cross;
                area6 += 3 * cross;
            }
            if (Mathf.Abs(area6) < .001f) throw new InvalidOperationException("Degenerate shard polygon.");
            return sum / area6;
        }
    }
}
