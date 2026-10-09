using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment.Editor
{
    public static class FluidExperimentGarnishBuilder
    {
        private const string Root = FluidExperimentBuilder.Root;
        // Clockwise image-pixel outlines, with transparent margins excluded.
        private static readonly Vector2[] Lemon = Pixels(5,50, 26,47, 48,42, 72,33, 94,23, 96,40,
            89,51, 76,59, 59,65, 34,70, 16,70, 5,68);
        private static readonly Vector2[] Nananga = Pixels(14,92, 6,77, 7,67, 20,50, 37,36, 59,24,
            78,20, 87,22, 92,31, 88,43, 74,57, 54,71, 30,85);
        private static readonly Vector2[] Jar = Pixels(9,43, 16,39, 65,39, 80,27, 228,27, 242,39,
            295,39, 299,46, 299,77, 291,86, 291,470, 284,526, 265,559, 238,574,
            73,574, 43,559, 22,528, 16,476, 16,87, 9,78);

        [MenuItem("Slainte/Fluid GPU Experiment/Build Garnishes")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(FluidExperimentBuilder.ScenePath);
            var world = UnityEngine.Object.FindFirstObjectByType<FluidExperimentWorld>();
            if (world == null) throw new InvalidOperationException("Experiment world missing.");
            var lemon = BuildPair("synthLemonPeel", "Synth Lemon Peel", Lemon, new Vector2(50.5f, 47));
            var nananga = BuildPair("nanangaPeel", "Nananga Peel", Nananga, new Vector2(49, 56.5f));
            Place(world, lemon, new Vector2(7.8f, 3.4f));
            Place(world, nananga, new Vector2(9.5f, 3.4f));
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, FluidExperimentBuilder.ScenePath)) throw new IOException("Scene save failed.");
            AssetDatabase.SaveAssets();
            Debug.Log("[GarnishBuilder] Two source/piece pairs authored; F default and existing scene objects retained.");
        }

        private static GameObject BuildPair(string key, string label, Vector2[] pixels, Vector2 pivot)
        {
            var sprite = Import(key + "_garni", 180, pivot, 100);
            var jar = Import(key + "_bottle", 240, new Vector2(154, 302.5f), 590);
            var outline = Convert(pixels, pivot, 180);
            string profilePath = Root + "/Data/CollisionProfiles/" + key + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<FluidExperimentCollisionProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<FluidExperimentCollisionProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            profile.solids = new[] { new FluidExperimentHull { points = outline } };
            profile.sourcePixelSize = 1f / 180;
            profile.sourceDescription = key + "_garni.png: simplified opaque outline; original PNG preserved.";
            EditorUtility.SetDirty(profile);

            var original = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Ice.prefab");
            var piece = UnityEngine.Object.Instantiate(original);
            piece.name = key + "_Garnish";
            var body = piece.GetComponent<FluidExperimentBody>();
            body.kind = LabItemKind.Garnish; body.displayName = label; body.ingredient = null;
            body.capacityMl = body.remainingMl = body.pourMlPerSecond = 0;
            body.iceStock = 0; body.icePrefab = null; body.collisionProfile = profile;
            body.ApplyCollisionProfile();
            var oldPick = body.pickCollider;
            body.pickCollider = body.solidColliders[0];
            if (oldPick != body.pickCollider) UnityEngine.Object.DestroyImmediate(oldPick);
            var renderer = piece.GetComponentInChildren<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.transform.localPosition = Vector3.zero;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = Vector3.one;
            renderer.sortingOrder = 45;
            var rigidbody = piece.GetComponent<Rigidbody2D>();
            rigidbody.mass = .01f;
            rigidbody.linearDamping = 4f;
            rigidbody.angularDamping = 4f;
            body.garnishLiquidMotionTransfer = .2f;
            var prefab = PrefabUtility.SaveAsPrefabAsset(piece, Root + "/Prefabs/" + key + "_Garnish.prefab");
            Material material = renderer.sharedMaterial;
            UnityEngine.Object.DestroyImmediate(piece);

            var source = new GameObject(key + "_Source");
            var sourceRenderer = source.AddComponent<SpriteRenderer>();
            sourceRenderer.sprite = jar; sourceRenderer.sharedMaterial = material; sourceRenderer.sortingOrder = 20;
            var pick = source.AddComponent<PolygonCollider2D>();
            pick.isTrigger = true; pick.points = Convert(Jar, new Vector2(154, 302.5f), 240);
            var supply = source.AddComponent<FluidExperimentGarnishSource>();
            supply.displayName = label; supply.garnishPrefab = prefab.GetComponent<FluidExperimentBody>(); supply.pickCollider = pick;
            source.layer = 29;
            var icon = new GameObject("ContentsIcon"); icon.transform.SetParent(source.transform, false); icon.layer = 29;
            var iconRenderer = icon.AddComponent<SpriteRenderer>();
            iconRenderer.sprite = sprite; iconRenderer.sharedMaterial = material; iconRenderer.sortingOrder = 21;
            var result = PrefabUtility.SaveAsPrefabAsset(source, Root + "/Prefabs/" + key + "_Source.prefab");
            UnityEngine.Object.DestroyImmediate(source);
            return result;
        }
        private static Sprite Import(string key, float ppu, Vector2 pivot, int height)
        {
            string path = Root + "/Art/Garnishes/" + key + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu; importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(pivot.x / (key.EndsWith("_bottle") ? 310 : 100), (height - pivot.y) / height);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings); importer.SaveAndReimport();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
            if (sprite == null) throw new InvalidOperationException("Sprite import failed: " + path
                + "; assets=" + string.Join(",", AssetDatabase.LoadAllAssetsAtPath(path).Select(a => a.GetType().Name)));
            return sprite;
        }
        private static void Place(FluidExperimentWorld world, GameObject prefab, Vector2 position)
        {
            var existing = world.GetComponentsInChildren<FluidExperimentGarnishSource>(true)
                .Where(s => s.name == prefab.name).ToArray();
            foreach (var item in existing) UnityEngine.Object.DestroyImmediate(item.gameObject);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, world.transform);
            instance.transform.position = position;
        }
        private static Vector2[] Convert(Vector2[] pixels, Vector2 pivot, float ppu) =>
            pixels.Select(p => new Vector2(p.x - pivot.x, pivot.y - p.y) / ppu).ToArray();
        private static Vector2[] Pixels(params float[] values)
        {
            var points = new Vector2[values.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(values[i * 2], values[i * 2 + 1]);
            return points;
        }
    }
}
