using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment.Editor
{
    /// <summary>All authoring happens on an isolated prefab and cloned profiles until Save.</summary>
    public sealed class FluidExperimentDesignerSession : IDisposable
    {
        public sealed class ProfileEdit
        {
            public string label, property;
            public Component owner;
            public ScriptableObject original, working;
            public bool cloneForPrefab;
            public int references;
        }
        public string SourcePath { get; private set; }
        public GameObject Root { get; private set; }
        public FluidExperimentBody Body => Root != null ? Root.GetComponentInChildren<FluidExperimentBody>(true) : null;
        public FluidExperimentGarnishSource Source => Root != null ? Root.GetComponentInChildren<FluidExperimentGarnishSource>(true) : null;
        public FluidExperimentBreakableGlass Breakable => Body != null ? Body.GetComponent<FluidExperimentBreakableGlass>() : null;
        public IReadOnlyList<ProfileEdit> Profiles => profiles.Where(p => p.owner != null).ToArray();
        public bool Dirty { get; private set; }
        readonly List<ProfileEdit> profiles = new List<ProfileEdit>();

        public FluidExperimentDesignerSession(string sourcePath, string snapshotPath = null, string[] originalProfilePaths = null)
        {
            SourcePath = sourcePath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) == null)
                throw new ArgumentException("Select a prefab asset before opening the designer.", nameof(sourcePath));
            Root = PrefabUtility.LoadPrefabContents(string.IsNullOrEmpty(snapshotPath) ? sourcePath : snapshotPath);
            try
            {
                if (Body == null && Source == null) throw new InvalidOperationException("The selected prefab has no supported body or garnish source.");
                if (Body != null)
                {
                    AddProfile(Body, "collisionProfile", "Collision");
                    AddProfile(Body, "shakerProfile", "Shaker");
                }
                if (Breakable != null)
                {
                    AddProfile(Breakable, "profile", "Breakage");
                    AddProfile(Breakable, "splashProfile", "Splash", typeof(FluidExperimentSplashProfile));
                }
                if (originalProfilePaths != null)
                    for (int i = 0; i < profiles.Count && i < originalProfilePaths.Length; i++)
                    {
                        profiles[i].original = AssetDatabase.LoadAssetAtPath<ScriptableObject>(originalProfilePaths[i]);
                        profiles[i].references = CountReferences(profiles[i].original);
                    }
                Dirty = !string.IsNullOrEmpty(snapshotPath);
            }
            catch { Dispose(); throw; }
        }
        void AddProfile(Component owner, string propertyName, string label, Type createIfMissing = null)
        {
            var serialized = new SerializedObject(owner);
            var property = serialized.FindProperty(propertyName);
            if (property == null) return;
            var source = property.objectReferenceValue as ScriptableObject;
            if (source == null && createIfMissing == null) return;
            var working = source != null ? UnityEngine.Object.Instantiate(source) : ScriptableObject.CreateInstance(createIfMissing);
            working.name = (source != null ? source.name : Root.name + " " + label) + " (Designer preview)";
            working.hideFlags = HideFlags.HideAndDontSave;
            property.objectReferenceValue = working; serialized.ApplyModifiedPropertiesWithoutUndo();
            profiles.Add(new ProfileEdit { label = label, property = propertyName, owner = owner,
                original = source, working = working, references = CountReferences(source) });
        }
        public void EnableBreakage()
        {
            if (Body == null || Body.kind != LabItemKind.Glass || Breakable != null) return;
            Undo.AddComponent<FluidExperimentBreakableGlass>(Body.gameObject);
            AddProfile(Breakable, "profile", "Breakage", typeof(FluidExperimentBreakageProfile));
            AddProfile(Breakable, "splashProfile", "Splash", typeof(FluidExperimentSplashProfile));
            Dirty = true;
        }
        public void ReconcileUndo()
        {
            if (Breakable == null) return;
            foreach (string property in new[] { "profile", "splashProfile" })
            {
                var value = new SerializedObject(Breakable).FindProperty(property)?.objectReferenceValue;
                var existing = profiles.FirstOrDefault(p => p.property == property && (p.owner == Breakable || p.working == value));
                if (existing != null)
                {
                    existing.owner = Breakable;
                    Assign(existing, existing.working);
                }
                else AddProfile(Breakable, property, property == "profile" ? "Breakage" : "Splash",
                    property == "profile" ? typeof(FluidExperimentBreakageProfile) : typeof(FluidExperimentSplashProfile));
            }
        }
        public void RestoreDirtyState(bool dirty) => Dirty = dirty;
        public static GameObject CreateVariant(string sourcePath, string variantPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before creating a variant.");
            variantPath = variantPath.Replace('\\', '/');
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null || !variantPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !variantPath.EndsWith(".prefab", StringComparison.Ordinal) || variantPath.Split('/').Contains("..")
                || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(variantPath) != null)
                throw new ArgumentException("Choose an existing source prefab and a new prefab path under Assets.");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                instance.name = Path.GetFileNameWithoutExtension(variantPath);
                var result = PrefabUtility.SaveAsPrefabAsset(instance, variantPath, out bool saved);
                if (!saved || result == null || PrefabUtility.GetPrefabAssetType(result) != PrefabAssetType.Variant)
                    throw new IOException("Could not create a connected prefab variant.");
                var catalog = AssetDatabase.LoadAssetAtPath<FluidExperimentAuthoringCatalog>(FluidExperimentAssemblyBuilder.CatalogPath);
                if (catalog == null)
                {
                    catalog = ScriptableObject.CreateInstance<FluidExperimentAuthoringCatalog>();
                    AssetDatabase.CreateAsset(catalog, FluidExperimentAssemblyBuilder.CatalogPath);
                }
                if (catalog != null)
                {
                    var body = result.GetComponent<FluidExperimentBody>();
                    var entries = new List<FluidExperimentAuthoringCatalog.Entry>(catalog.entries ?? Array.Empty<FluidExperimentAuthoringCatalog.Entry>());
                    entries.Add(new FluidExperimentAuthoringCatalog.Entry { id = AssetDatabase.AssetPathToGUID(variantPath),
                        prefab = result, displayName = result.name, category = body != null ? body.kind.ToString() : "Source",
                        ingredient = body != null ? body.ingredient : null });
                    catalog.entries = entries.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
                }
                return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        public static GameObject[] CatalogPrefabs()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FluidExperimentAuthoringCatalog>(FluidExperimentAssemblyBuilder.CatalogPath);
            var registered = catalog != null ? (catalog.entries ?? Array.Empty<FluidExperimentAuthoringCatalog.Entry>()).Where(e => e != null).Select(e => e.prefab) : Enumerable.Empty<GameObject>();
            var discovered = AssetDatabase.FindAssets("t:Prefab", new[] { FluidExperimentBuilder.Root + "/Prefabs" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)));
            return registered.Concat(discovered).Where(prefab => prefab != null && prefab.GetComponent<FluidExperimentRuntimeRig>() == null
                && (prefab.GetComponent<FluidExperimentBody>() != null || prefab.GetComponent<FluidExperimentGarnishSource>() != null))
                .Distinct().ToArray();
        }
        public static int CountReferences(UnityEngine.Object asset)
        {
            if (asset == null) return 0;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return 0;
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets" }))
            {
                string prefab = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.GetDependencies(prefab, true).Contains(path)) count++;
            }
            return count;
        }
        public void MarkDirty()
        {
            Dirty = true;
            // Geometry preview is regenerated only on the isolated copy.
            if (Body != null && Body.collisionProfile != null && IsValidGeometry(Body.collisionProfile, out _))
                Body.ApplyCollisionProfile();
        }
        public static bool IsValidGeometry(FluidExperimentCollisionProfile profile, out string error)
        {
            error = null;
            if (profile == null) return true;
            if (profile.solids == null || profile.solids.Length == 0) error = "Collision needs at least one solid hull.";
            else if (profile.solids.Any(h => h == null || h.points == null || h.points.Length < 3
                || h.points.Any(p => !float.IsFinite(p.x) || !float.IsFinite(p.y))))
                error = "Every solid hull needs at least three finite points.";
            else if (profile.solids.Any(h => !SimplePolygon(h.points)))
                error = "Solid hulls must have nonzero area, distinct neighboring points and no crossing edges.";
            else if (profile.interior == null || (profile.interior.Length > 0 && profile.interior.Length < 3)
                || profile.interior.Any(p => !float.IsFinite(p.x) || !float.IsFinite(p.y)))
                error = "Interior must be empty or contain at least three finite points.";
            else if (!Finite(profile.mouth) || !Finite(profile.exitDirection)
                || profile.lid == null || profile.lid.Any(p => !Finite(p))) error = "Mouth, exit direction and lid points must be finite.";
            return error == null;
        }
        public void Validate()
        {
            if (Root == null) throw new InvalidOperationException("The designer session is closed.");
            if (Body != null)
            {
                if (!IsValidGeometry(Body.collisionProfile, out string error)) throw new InvalidOperationException(error);
                if (!Finite(Body.rotationPivotLocal) || !Finite(Body.mouthLocal) || !Finite(Body.mouthDirectionLocal)
                    || !float.IsFinite(Body.capacityMl) || Body.capacityMl < 0 || !float.IsFinite(Body.pourMlPerSecond)
                    || Body.pourMlPerSecond < 0) throw new InvalidOperationException("Mouth, pivot, capacity and flow must be valid finite values.");
                ValidateNumbers(Body);
                var shaker = Body.shakerProfile;
                if (shaker != null && (shaker.strainerRect.width <= 0 || shaker.strainerRect.height <= 0
                    || shaker.capRect.width <= 0 || shaker.capRect.height <= 0 || shaker.leftGuide == null || shaker.rightGuide == null
                    || shaker.leftGuide.Length < 2 || shaker.rightGuide.Length < 2 || shaker.halfThickness <= 0
                    || shaker.strainerMass <= 0 || shaker.capMass <= 0 || shaker.attachmentDistance <= 0))
                    throw new InvalidOperationException("Shaker parts require positive bounds/masses/thickness and two or more guide points per side.");
            }
            foreach (var profile in Profiles) ValidateNumbers(profile.working);
            if (Breakable != null)
            {
                if (Breakable.profile == null || Breakable.profile.shards.Length == 0)
                    throw new InvalidOperationException("Breakage requires authored shard geometry. Assign matching generated geometry before saving.");
                foreach (var shard in Breakable.profile.shards)
                    if (shard == null || shard.sprite == null || !float.IsFinite(shard.area) || shard.area <= 0
                        || !Finite(shard.localCenter) || !Finite(shard.spriteScale) || shard.spriteScale.x <= 0 || shard.spriteScale.y <= 0
                        || shard.outline == null || shard.outline.Length < 3 || shard.outline.Any(p => !Finite(p)))
                        throw new InvalidOperationException("Shard geometry contains a missing sprite or invalid area, scale or outline.");
                var material = Breakable.profile.shardMaterial;
                if (material != null && (!float.IsFinite(material.friction) || !float.IsFinite(material.bounciness)))
                    throw new InvalidOperationException("Shard material friction and bounce must be finite.");
            }
        }
        static bool Finite(Vector2 p) => float.IsFinite(p.x) && float.IsFinite(p.y);
        static bool SimplePolygon(Vector2[] points)
        {
            double area = 0;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Length];
                if ((a - b).sqrMagnitude < 1e-12f) return false;
                area += (double)a.x * b.y - (double)b.x * a.y;
                for (int j = i + 2; j < points.Length; j++)
                {
                    if (i == 0 && j == points.Length - 1) continue;
                    Vector2 c = points[j], d = points[(j + 1) % points.Length];
                    float c1 = Cross(b - a, c - a), c2 = Cross(b - a, d - a);
                    float c3 = Cross(d - c, a - c), c4 = Cross(d - c, b - c);
                    if (c1 * c2 < 0 && c3 * c4 < 0) return false;
                    if (Mathf.Abs(c1) < 1e-8f && OnSegment(a, b, c)) return false;
                    if (Mathf.Abs(c2) < 1e-8f && OnSegment(a, b, d)) return false;
                }
            }
            return Math.Abs(area) > 1e-8;
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool OnSegment(Vector2 a, Vector2 b, Vector2 p) => p.x >= Mathf.Min(a.x, b.x) - 1e-7f
            && p.x <= Mathf.Max(a.x, b.x) + 1e-7f && p.y >= Mathf.Min(a.y, b.y) - 1e-7f && p.y <= Mathf.Max(a.y, b.y) + 1e-7f;
        static void ValidateNumbers(UnityEngine.Object value)
        {
            var property = new SerializedObject(value).GetIterator();
            while (property.Next(true))
            {
                bool valid = property.propertyType != SerializedPropertyType.Float || double.IsFinite(property.doubleValue);
                if (property.propertyType == SerializedPropertyType.Vector2) valid &= Finite(property.vector2Value);
                if (property.propertyType == SerializedPropertyType.Vector3)
                { var p = property.vector3Value; valid &= float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z); }
                if (property.propertyType == SerializedPropertyType.Rect)
                { var r = property.rectValue; valid &= float.IsFinite(r.x) && float.IsFinite(r.y) && float.IsFinite(r.width) && float.IsFinite(r.height); }
                if (!valid) throw new InvalidOperationException(value.name + ": " + property.displayName + " must be finite.");
            }
        }
        static void Assign(ProfileEdit edit, ScriptableObject value)
        {
            var serialized = new SerializedObject(edit.owner);
            serialized.FindProperty(edit.property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        static string ProfileContents(ScriptableObject source)
        {
            // Clone metadata differs by design; compare authored fields and references only.
            // A live original may already be dirty from another editor, so equality must not
            // trigger a save of that unrelated draft as a side effect of saving this prefab.
            var normalized = UnityEngine.Object.Instantiate(source);
            try
            {
                normalized.name = ""; normalized.hideFlags = HideFlags.None;
                return EditorJsonUtility.ToJson(normalized);
            }
            finally { UnityEngine.Object.DestroyImmediate(normalized); }
        }
        public void Save()
        {
            Validate();
            var created = new List<string>();
            var active = Profiles.ToArray();
            var previous = active.Select(p => p.original).ToArray();
            var backups = active.Where(p => p.original != null).ToDictionary(p => p, p => UnityEngine.Object.Instantiate(p.original));
            var changedAssets = new HashSet<ScriptableObject>();
            try
            {
                foreach (var edit in active)
                {
                    if (edit.cloneForPrefab || edit.original == null)
                    {
                        string directory = Path.GetDirectoryName(SourcePath).Replace('\\', '/');
                        string path = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + Root.name + "_" + edit.label + ".asset");
                        var asset = UnityEngine.Object.Instantiate(edit.working); asset.hideFlags = HideFlags.None;
                        asset.name = Root.name + " " + edit.label;
                        AssetDatabase.CreateAsset(asset, path); created.Add(path); edit.original = asset;
                        changedAssets.Add(asset);
                    }
                    else if (ProfileContents(edit.working) != ProfileContents(edit.original))
                    {
                        string name = edit.original.name;
                        EditorUtility.CopySerialized(edit.working, edit.original);
                        edit.original.name = name; edit.original.hideFlags = HideFlags.None;
                        EditorUtility.SetDirty(edit.original);
                        changedAssets.Add(edit.original);
                    }
                    Assign(edit, edit.original);
                }
                if (Body != null) Body.ApplyCollisionProfile();
                PrefabUtility.SaveAsPrefabAsset(Root, SourcePath, out bool saved);
                if (!saved) throw new IOException("Unity could not save " + SourcePath);
                foreach (var asset in changedAssets) AssetDatabase.SaveAssetIfDirty(asset);
                foreach (var edit in active) { edit.cloneForPrefab = false; edit.references = CountReferences(edit.original); }
                Dirty = false;
                // Save is an explicit persistence boundary. Undo/Redo applies to the draft only;
                // do not create a partial asset-only Undo that omits the prefab file.
                foreach (var component in Root.GetComponentsInChildren<Component>(true))
                    if (component != null) { Undo.ClearUndo(component); if (component is Transform) Undo.ClearUndo(component.gameObject); }
                foreach (var edit in active) Undo.ClearUndo(edit.working);
            }
            catch
            {
                foreach (var backup in backups)
                    if (backup.Key.original != null && changedAssets.Contains(backup.Key.original)
                        && !created.Contains(AssetDatabase.GetAssetPath(backup.Key.original)))
                    { EditorUtility.CopySerialized(backup.Value, backup.Key.original); EditorUtility.SetDirty(backup.Key.original); }
                foreach (var path in created) AssetDatabase.DeleteAsset(path);
                for (int i = 0; i < active.Length; i++) active[i].original = previous[i];
                throw;
            }
            finally
            {
                foreach (var edit in active) if (edit.owner != null) Assign(edit, edit.working);
                foreach (var backup in backups.Values) UnityEngine.Object.DestroyImmediate(backup);
            }
        }
        public string SaveSnapshot(string folder, bool validate = true)
        {
            if (validate) Validate();
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            try
            {
                foreach (var edit in Profiles)
                {
                    var copy = UnityEngine.Object.Instantiate(edit.working); copy.hideFlags = HideFlags.None;
                    string profilePath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + edit.label + ".asset");
                    AssetDatabase.CreateAsset(copy, profilePath); Assign(edit, copy);
                }
                if (Body != null && IsValidGeometry(Body.collisionProfile, out _)) Body.ApplyCollisionProfile();
                string path = folder + "/TestItem.prefab";
                PrefabUtility.SaveAsPrefabAsset(Root, path, out bool saved);
                if (!saved) throw new IOException("Could not create the isolated test prefab.");
                return path;
            }
            finally { foreach (var edit in Profiles) Assign(edit, edit.working); }
        }
        public void Dispose()
        {
            if (Root != null) { Undo.ClearUndo(Root); PrefabUtility.UnloadPrefabContents(Root); Root = null; }
            foreach (var edit in profiles) if (edit.working != null)
            { Undo.ClearUndo(edit.working); UnityEngine.Object.DestroyImmediate(edit.working); }
            profiles.Clear();
        }
    }
}
