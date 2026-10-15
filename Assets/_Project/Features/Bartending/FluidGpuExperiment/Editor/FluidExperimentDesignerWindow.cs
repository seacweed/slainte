using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Slainte.Bartending.FluidGpuExperiment.Editor
{
    public sealed class FluidExperimentDesignerWindow : EditorWindow
    {
        sealed class CatalogItem { public string path, name, category; public GameObject prefab; }
        [SerializeField] string sourcePath, search = "", category = "All";
        [SerializeField] float dropHeight = 3, fillMl = 40, duration = 4;
        [SerializeField] int repeats = 3;
        [SerializeField] Vector2 velocity, placePosition;
        [SerializeField] bool explosive;
        FluidExperimentDesignerSession session;
        FluidExperimentDesignerStage previewStage;
        const string RecoveryKey = "FluidDesigner.DraftRecovery";
        readonly List<CatalogItem> catalog = new List<CatalogItem>(), filtered = new List<CatalogItem>();
        ListView list;
        ScrollView inspector;
        Label status, results;
        VisualElement editorPanel;
        string notice = "Select an object. Edits stay in an isolated preview until Save.";
        public FluidExperimentDesignerSession Session => session;

        [MenuItem("Slainte/Fluid GPU Experiment/Designer")]
        public static void Open() => GetWindow<FluidExperimentDesignerWindow>("Fluid Designer").Show();
        void OnEnable()
        {
            minSize = new Vector2(850, 620);
            Undo.undoRedoPerformed += OnUndo;
            FluidExperimentDesignerPlayTest.Changed += UpdateStatus;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.projectChanged += RefreshCatalog;
            AssemblyReloadEvents.beforeAssemblyReload += PreserveDraft;
        }
        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
            FluidExperimentDesignerPlayTest.Changed -= UpdateStatus;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.projectChanged -= RefreshCatalog;
            AssemblyReloadEvents.beforeAssemblyReload -= PreserveDraft;
            ClosePreview(); session?.Dispose(); session = null;
        }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var toolbar = new Toolbar(); rootVisualElement.Add(toolbar);
            toolbar.Add(new ToolbarButton(() => Run(Save)) { text = "Save" });
            toolbar.Add(new ToolbarButton(() => Run(Revert)) { text = "Revert" });
            toolbar.Add(new ToolbarButton(() => Undo.PerformUndo()) { text = "Undo", tooltip = "Uses Unity's Undo history, including preview edits. Save clears this preview's saved edit records." });
            toolbar.Add(new ToolbarButton(() => Undo.PerformRedo()) { text = "Redo", tooltip = "Uses Unity's Redo history." });
            toolbar.Add(new ToolbarButton(() => Run(ShowSceneHandles)) { text = "Scene handles" });
            toolbar.Add(new ToolbarButton(() => Run(CreateVariant)) { text = "Create variant" });
            toolbar.Add(new ToolbarButton(() => Run(() => FluidExperimentAssemblyBuilder.AssembleNewScene())) { text = "Assemble runtime scene" });
            status = new Label(notice); status.style.whiteSpace = WhiteSpace.Normal;
            status.style.paddingLeft = 8; status.style.paddingTop = status.style.paddingBottom = 6;
            rootVisualElement.Add(status);
            var split = new TwoPaneSplitView(0, 250, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1; rootVisualElement.Add(split);
            var browser = new VisualElement(); browser.style.paddingLeft = browser.style.paddingRight = 6; split.Add(browser);
            var searchField = new ToolbarSearchField(); searchField.value = search;
            searchField.RegisterValueChangedCallback(e => { search = e.newValue; Filter(); }); browser.Add(searchField);
            var choices = new List<string> { "All" }; choices.AddRange(Enum.GetNames(typeof(LabItemKind))); choices.Add("Source");
            var categoryField = new DropdownField("Category", choices, choices.Contains(category) ? choices.IndexOf(category) : 0);
            categoryField.RegisterValueChangedCallback(e => { category = e.newValue; Filter(); }); browser.Add(categoryField);
            list = new ListView { fixedItemHeight = 52, selectionType = SelectionType.Single };
            list.style.flexGrow = 1;
            list.makeItem = () =>
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
                var image = new Image { name = "thumbnail", scaleMode = ScaleMode.ScaleToFit };
                image.style.width = image.style.height = 44; row.Add(image);
                var label = new Label { name = "name" }; label.style.whiteSpace = WhiteSpace.Normal; label.style.flexGrow = 1; row.Add(label); return row;
            };
            list.bindItem = (row, index) =>
            {
                var item = filtered[index]; row.Q<Label>("name").text = item.name + "\n" + item.category;
                var thumbnail = row.Q<Image>("thumbnail"); var preview = AssetPreview.GetAssetPreview(item.prefab);
                var art = item.prefab.GetComponentInChildren<SpriteRenderer>(true);
                if (preview != null) { thumbnail.sprite = null; thumbnail.image = preview; }
                else if (art != null && art.sprite != null) { thumbnail.image = null; thumbnail.sprite = art.sprite; }
                else { thumbnail.sprite = null; thumbnail.image = AssetPreview.GetMiniThumbnail(item.prefab); }
            };
            list.selectionChanged += values => { var item = values.OfType<CatalogItem>().FirstOrDefault(); if (item != null) Run(() => SelectPrefab(item.path)); };
            browser.Add(list);
            browser.Add(new Button(RefreshCatalog) { text = "Refresh catalog" });
            editorPanel = new VisualElement(); split.Add(editorPanel);
            inspector = new ScrollView(); inspector.style.flexGrow = 1; editorPanel.Add(inspector);
            RefreshCatalog();
            if (!EditorApplication.isPlayingOrWillChangePlaymode && FluidExperimentDesignerPlayTest.PendingRestore() != null) RestoreAfterPlayTest();
            else if (!EditorApplication.isPlayingOrWillChangePlaymode && !string.IsNullOrEmpty(SessionState.GetString(RecoveryKey, ""))) RestoreDraft();
            else if (session == null && !string.IsNullOrEmpty(sourcePath) && !EditorApplication.isPlayingOrWillChangePlaymode)
                Run(() => OpenSession(sourcePath));
            else BuildInspector();
            UpdateStatus();
        }
        void RefreshCatalog()
        {
            catalog.Clear();
            var registered = AssetDatabase.LoadAssetAtPath<FluidExperimentAuthoringCatalog>(FluidExperimentAssemblyBuilder.CatalogPath);
            foreach (var prefab in FluidExperimentDesignerSession.CatalogPrefabs())
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                var body = prefab.GetComponent<FluidExperimentBody>();
                var source = prefab.GetComponent<FluidExperimentGarnishSource>();
                var entry = registered != null ? registered.entries.FirstOrDefault(e => e != null && e.prefab == prefab) : null;
                catalog.Add(new CatalogItem { path = path, prefab = prefab,
                    name = entry != null && !string.IsNullOrEmpty(entry.displayName) ? entry.displayName
                        : body != null && !string.IsNullOrEmpty(body.displayName) ? body.displayName
                        : source != null && !string.IsNullOrEmpty(source.displayName) ? source.displayName : prefab.name,
                    category = body != null ? body.kind.ToString() : "Source" });
            }
            catalog.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase)); Filter();
        }
        void Filter()
        {
            filtered.Clear(); filtered.AddRange(catalog.Where(i => (category == "All" || i.category == category)
                && (string.IsNullOrWhiteSpace(search) || (i.name + " " + i.path).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)));
            if (list != null) { list.itemsSource = filtered; list.Rebuild(); }
        }
        public void SelectPrefab(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop the play test before selecting another prefab.");
            if (session != null && path == session.SourcePath) return;
            if (session != null && session.Dirty)
            {
                int answer = EditorUtility.DisplayDialogComplex("Unsaved designer changes", "Save the current isolated draft before switching objects?", "Save", "Cancel", "Discard");
                if (answer == 1) return; if (answer == 0) session.Save();
            }
            OpenSession(path);
        }
        void OpenSession(string path, string snapshot = null, string[] originalProfiles = null)
        {
            ClosePreview(); session?.Dispose();
            session = new FluidExperimentDesignerSession(path, snapshot, originalProfiles); sourcePath = path;
            notice = "Editing " + Path.GetFileNameWithoutExtension(path) + " · isolated preview";
            BuildInspector(); UpdateStatus();
        }
        void BuildInspector()
        {
            if (inspector == null) return;
            inspector.Unbind(); inspector.Clear();
            if (session == null || session.Root == null)
            { inspector.Add(new HelpBox("Choose a prefab from the catalog to edit its settings and geometry.", HelpBoxMessageType.Info)); AddTestPanel(); return; }
            var header = new Label(session.Root.name); header.style.unityFontStyleAndWeight = FontStyle.Bold; header.style.fontSize = 17; inspector.Add(header);
            inspector.Add(new Label(session.SourcePath) { tooltip = "Save writes this prefab; the preview never writes it automatically." });
            if (session.Body != null)
            {
                AddProperties("Basic", session.Body, "displayName", "kind", "ingredient", "capacityMl", "remainingMl", "sealedVessel", "rotationPivotLocal");
                AddProperties("Pouring", session.Body, "pourMlPerSecond", "pourStartAngle", "fullPourAngle", "exitSpeed", "mouthWidth");
                if (session.Body.collisionProfile == null)
                    AddProperties("Mouth", session.Body, "mouthLocal", "mouthDirectionLocal", "overrideMouthLip", "mouthLipLocal");
                else inspector.Add(new HelpBox("Mouth position and exit direction are authored in Collision settings below.", HelpBoxMessageType.Info));
                AddProperties("Stock and contents", session.Body, "icePrefab", "iceStock", "icePourInterval", "garnishLiquidMotionTransfer");
                var rigid = session.Body.GetComponent<Rigidbody2D>();
                if (rigid != null) AddProperties("Body physics", rigid, "m_Mass", "m_GravityScale", "m_LinearDamping", "m_AngularDamping");
                if (session.Body.pickCollider != null) AddProperties("Pick collider", session.Body.pickCollider, "m_Offset", "m_Size", "m_Radius", "m_Points", "m_Paths");
                if (session.Body.kind == LabItemKind.Glass && session.Breakable == null)
                    inspector.Add(new Button(() => Run(() => { session.EnableBreakage(); BuildInspector(); Changed(); }))
                    { text = "Enable breakage (requires matching shard geometry)" });
            }
            if (session.Source != null)
            {
                AddProperties("Garnish source", session.Source, "displayName", "garnishPrefab");
                if (session.Source.pickCollider != null) AddProperties("Source pick collider", session.Source.pickCollider, "m_Offset", "m_Points", "m_Paths");
            }
            var art = session.Root.GetComponentInChildren<SpriteRenderer>(true);
            if (art != null)
            {
                AddProperties("Artwork", art, "m_Sprite", "m_Color", "m_SortingOrder");
                AddProperties("Artwork transform", art.transform, "m_LocalPosition", "m_LocalScale");
            }
            foreach (var edit in session.Profiles)
            {
                var group = new Foldout { text = edit.label + " settings", value = edit.label != "Collision" };
                inspector.Add(group);
                var scope = new Label(edit.original == null ? "New settings asset will be created on Save."
                    : $"Shared asset: {edit.original.name} · referenced by {edit.references} project prefab/scene asset(s). Saving shared settings affects all users.");
                scope.style.whiteSpace = WhiteSpace.Normal; group.Add(scope);
                var clone = new Toggle("Clone for this prefab on Save") { value = edit.cloneForPrefab };
                clone.SetEnabled(edit.original != null);
                clone.RegisterValueChangedCallback(e => { edit.cloneForPrefab = e.newValue; Changed(); }); group.Add(clone);
                var serialized = new SerializedObject(edit.working);
                var property = serialized.GetIterator();
                bool enter = true;
                while (property.NextVisible(enter))
                {
                    enter = false;
                    if (property.name == "m_Script") continue;
                    AddField(group, property.Copy());
                }
                group.Bind(serialized);
            }
            var geometry = new Foldout { text = "Geometry editing", value = false }; inspector.Add(geometry);
            geometry.Add(new HelpBox("Scene handles: solid hulls orange, open liquid interior cyan, mouth green, pivot magenta. Drag points in the isolated preview stage. Collision arrays also support adding/removing points.", HelpBoxMessageType.Info));
            geometry.Add(new Button(() => Run(ShowSceneHandles)) { text = "Open isolated scene handles" });
            var placement = new Foldout { text = "Scene placement", value = false }; inspector.Add(placement);
            var position = new Vector2Field("Position") { value = placePosition }; position.RegisterValueChangedCallback(e => placePosition = e.newValue); placement.Add(position);
            placement.Add(new Button(() => Run(Place)) { text = "Save and place in current scene" });
            AddTestPanel();
        }
        void AddProperties(string title, Object target, params string[] names)
        {
            var group = new Foldout { text = title, value = title == "Basic" || title == "Garnish source" }; inspector.Add(group);
            var serialized = new SerializedObject(target);
            foreach (string name in names)
            {
                var property = serialized.FindProperty(name); if (property != null) AddField(group, property);
            }
            group.Bind(serialized);
        }
        void AddField(VisualElement parent, SerializedProperty property)
        {
            var field = new PropertyField(property); field.RegisterValueChangeCallback(_ => Changed()); parent.Add(field);
        }
        void AddTestPanel()
        {
            var panel = new Foldout { text = "Play test", value = true }; inspector.Add(panel);
            var height = new FloatField("Drop height") { value = dropHeight }; height.RegisterValueChangedCallback(e => dropHeight = e.newValue); panel.Add(height);
            var speed = new Vector2Field("Initial velocity") { value = velocity }; speed.RegisterValueChangedCallback(e => velocity = e.newValue); panel.Add(speed);
            var fill = new FloatField("Liquid fill (ml)") { value = fillMl }; fill.RegisterValueChangedCallback(e => fillMl = e.newValue); panel.Add(fill);
            var time = new FloatField("Seconds per run") { value = duration }; time.RegisterValueChangedCallback(e => duration = e.newValue); panel.Add(time);
            var repeat = new IntegerField("Repeats") { value = repeats }; repeat.RegisterValueChangedCallback(e => repeats = e.newValue); panel.Add(repeat);
            var burst = new Toggle("Explosive breakage") { value = explosive }; burst.RegisterValueChangedCallback(e => explosive = e.newValue); panel.Add(burst);
            var start = new Button(() => Run(StartTest)) { text = "Play isolated draft" };
            start.SetEnabled(session != null && session.Body != null && !EditorApplication.isPlayingOrWillChangePlaymode); panel.Add(start);
            if (session != null && session.Body == null)
                panel.Add(new HelpBox("This is a fixed garnish source. Place it in the runtime scene to dispense pieces, or select its garnish prefab to run a drop test.", HelpBoxMessageType.Info));
            panel.Add(new Button(FluidExperimentDesignerPlayTest.Stop) { text = "Stop test and restore editor" });
            results = new Label(FluidExperimentDesignerPlayTest.Results); results.style.whiteSpace = WhiteSpace.Normal; panel.Add(results);
        }
        void Changed()
        {
            if (session == null) return;
            session.MarkDirty(); hasUnsavedChanges = session.Dirty;
            saveChangesMessage = "Save the isolated fluid designer draft before closing?";
            if (previewStage != null) previewStage.RefreshPreview();
            UpdateStatus();
        }
        void OnUndo() { if (session != null && !EditorApplication.isPlaying) { session.ReconcileUndo(); Changed(); BuildInspector(); } }
        void Save()
        {
            RequireEditMode(); if (session == null) return;
            session.Save(); hasUnsavedChanges = false; notice = "Saved " + session.SourcePath;
            BuildInspector(); UpdateStatus();
        }
        public override void SaveChanges() { Run(Save); if (session == null || !session.Dirty) base.SaveChanges(); }
        public override void DiscardChanges() { Run(Revert); base.DiscardChanges(); }
        void Revert()
        {
            RequireEditMode(); if (string.IsNullOrEmpty(sourcePath)) return;
            OpenSession(sourcePath); hasUnsavedChanges = false; notice = "Reverted to saved prefab and settings."; UpdateStatus();
        }
        void CreateVariant()
        {
            RequireEditMode(); if (session == null) return;
            if (session.Dirty) throw new InvalidOperationException("Save the draft before creating a variant.");
            string path = EditorUtility.SaveFilePanelInProject("Create prefab variant", session.Root.name + " Variant", "prefab", "Choose the new variant asset path.", Path.GetDirectoryName(sourcePath));
            if (string.IsNullOrEmpty(path)) return;
            FluidExperimentDesignerSession.CreateVariant(sourcePath, path);
            OpenSession(path);
            foreach (var profile in session.Profiles) profile.cloneForPrefab = true;
            Changed(); RefreshCatalog(); BuildInspector();
        }
        void Place()
        {
            RequireEditMode(); if (session == null) return;
            Save(); ClosePreview();
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || EditorSceneManager.IsPreviewScene(scene)) throw new InvalidOperationException("Open a normal scene for placement.");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(session.SourcePath), scene);
            Undo.RegisterCreatedObjectUndo(root, "Place fluid object");
            var rig = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FluidExperimentRuntimeRig>(true)).FirstOrDefault();
            if (rig != null && rig.World != null) Undo.SetTransformParent(root.transform, rig.World.transform, "Parent fluid object");
            root.transform.position = new Vector3(placePosition.x, placePosition.y, 0); Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(scene); notice = "Placed " + root.name + " with prefab connection."; UpdateStatus();
        }
        void ShowSceneHandles()
        {
            RequireEditMode(); if (session == null) return;
            ClosePreview(); previewStage = CreateInstance<FluidExperimentDesignerStage>(); previewStage.Owner = this;
            StageUtility.GoToStage(previewStage, true);
            var view = SceneView.lastActiveSceneView ?? GetWindow<SceneView>();
            view.in2DMode = true; view.Frame(new Bounds(Vector3.zero, Vector3.one * 5), false); view.Show();
        }
        void ClosePreview()
        {
            if (previewStage == null) return;
            if (StageUtility.GetCurrentStage() == previewStage) StageUtility.GoBackToPreviousStage();
            if (previewStage != null) DestroyImmediate(previewStage); previewStage = null;
        }
        void StartTest()
        {
            RequireEditMode(); if (session == null) return;
            ClosePreview();
            FluidExperimentDesignerPlayTest.Start(session, new FluidExperimentDesignerPlayTest.Request
            { height = dropHeight, velocity = velocity, fillMl = fillMl, duration = duration, repeats = repeats, explosive = explosive });
            hasUnsavedChanges = false;
        }
        void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            { PreserveDraft(); ClosePreview(); session?.Dispose(); session = null; hasUnsavedChanges = false; }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (FluidExperimentDesignerPlayTest.PendingRestore() != null) RestoreAfterPlayTest();
                else if (!FluidExperimentDesignerPlayTest.Active) RestoreDraft();
            }
            UpdateStatus();
        }
        public static void RestoreAfterPlayTest()
        {
            var request = FluidExperimentDesignerPlayTest.PendingRestore(); if (request == null) return;
            var window = GetWindow<FluidExperimentDesignerWindow>("Fluid Designer");
            try
            {
                window.OpenSession(request.source, request.snapshot, request.originalProfiles);
                for (int i = 0; i < window.session.Profiles.Count && i < request.cloneProfiles.Length; i++)
                    window.session.Profiles[i].cloneForPrefab = request.cloneProfiles[i];
                window.session.RestoreDirtyState(request.dirty);
                window.hasUnsavedChanges = request.dirty;
                window.notice = "Play test complete. Draft restored; authored assets were not changed.";
                FluidExperimentDesignerPlayTest.FinishRestore(); window.BuildInspector(); window.UpdateStatus();
            }
            catch (Exception ex) { window.notice = "Draft recovery needs attention: " + ex.Message; window.UpdateStatus(); }
        }
        void PreserveDraft()
        {
            if (session == null || !session.Dirty || FluidExperimentDesignerPlayTest.Active
                || !string.IsNullOrEmpty(SessionState.GetString(RecoveryKey, ""))) return;
            try
            {
                var recovery = new FluidExperimentDesignerPlayTest.Request
                {
                    source = session.SourcePath, dirty = true,
                    originalProfiles = session.Profiles.Select(p => AssetDatabase.GetAssetPath(p.original)).ToArray(),
                    cloneProfiles = session.Profiles.Select(p => p.cloneForPrefab).ToArray(),
                    folder = FluidExperimentBuilder.Root + "/DesignerTemp/Recovery_" + Guid.NewGuid().ToString("N")
                };
                // Even an incomplete draft must survive a script reload or ordinary Play button.
                recovery.snapshot = session.SaveSnapshot(recovery.folder, false);
                SessionState.SetString(RecoveryKey, JsonUtility.ToJson(recovery));
            }
            catch (Exception ex) { Debug.LogError("Fluid Designer could not preserve the draft before reload: " + ex.Message); }
        }
        void RestoreDraft()
        {
            string json = SessionState.GetString(RecoveryKey, "");
            if (string.IsNullOrEmpty(json))
            {
                if (session == null && !string.IsNullOrEmpty(sourcePath)) Run(() => OpenSession(sourcePath));
                return;
            }
            try
            {
                var recovery = JsonUtility.FromJson<FluidExperimentDesignerPlayTest.Request>(json);
                OpenSession(recovery.source, recovery.snapshot, recovery.originalProfiles);
                for (int i = 0; i < session.Profiles.Count && i < recovery.cloneProfiles.Length; i++)
                    session.Profiles[i].cloneForPrefab = recovery.cloneProfiles[i];
                hasUnsavedChanges = true; notice = "Unsaved designer draft restored after Play Mode or script reload.";
                SessionState.EraseString(RecoveryKey);
                if (recovery.folder.StartsWith(FluidExperimentBuilder.Root + "/DesignerTemp/Recovery_", StringComparison.Ordinal))
                    AssetDatabase.DeleteAsset(recovery.folder);
                BuildInspector(); UpdateStatus();
            }
            catch (Exception ex) { notice = "Draft recovery needs attention: " + ex.Message; UpdateStatus(); }
        }
        void RequireEditMode()
        { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before editing or saving."); }
        void UpdateStatus()
        {
            if (status != null) status.text = FluidExperimentDesignerPlayTest.Active ? FluidExperimentDesignerPlayTest.Status
                : (session != null && session.Dirty ? "Unsaved draft · " : "") + notice;
            if (results != null) results.text = FluidExperimentDesignerPlayTest.Results;
            editorPanel?.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode || FluidExperimentDesignerPlayTest.Active);
        }
        void Run(Action action)
        {
            try { action(); }
            catch (Exception ex) { notice = ex.Message; UpdateStatus(); Debug.LogWarning("[Fluid Designer] " + ex.Message); }
        }
        internal void DrawHandles(Transform previewTransform)
        {
            if (session == null || previewTransform == null) return;
            if (session.Body == null)
            {
                if (session.Source != null) DrawPickCollider(session.Source.transform, session.Source.pickCollider, previewTransform);
                if (GUI.changed) { session.MarkDirty(); hasUnsavedChanges = true; UpdateStatus(); }
                return;
            }
            var body = session.Body; var profile = body.collisionProfile;
            if (profile != null)
            {
                if (profile.solids != null)
                    for (int i = 0; i < profile.solids.Length; i++)
                        if (profile.solids[i] != null) EditPath(profile, profile.solids[i].points, previewTransform, new Color(1, .55f, .12f), true, "solid " + i);
                EditPath(profile, profile.interior, previewTransform, Color.cyan, false, "interior");
                EditPath(profile, profile.lid, previewTransform, Color.yellow, false, "lid");
                profile.mouth = EditPoint(profile, profile.mouth, previewTransform, Color.green, "mouth");
            }
            else body.mouthLocal = EditPoint(body, body.mouthLocal, previewTransform, Color.green, "mouth");
            body.rotationPivotLocal = EditPoint(body, body.rotationPivotLocal, previewTransform, Color.magenta, "pivot");
            if (body.shakerProfile != null)
            {
                var shaker = body.shakerProfile;
                EditPath(shaker, shaker.leftGuide, previewTransform, new Color(.4f, .8f, 1), false, "left guide");
                EditPath(shaker, shaker.rightGuide, previewTransform, new Color(.4f, .8f, 1), false, "right guide");
                shaker.strainerRect = EditRect(shaker, shaker.strainerRect, previewTransform, new Color(.8f, .7f, 1), "strainer");
                shaker.capRect = EditRect(shaker, shaker.capRect, previewTransform, new Color(1, .7f, .8f), "cap");
            }
            DrawPickCollider(body.transform, body.pickCollider, previewTransform);
            if (GUI.changed) { session.MarkDirty(); hasUnsavedChanges = true; UpdateStatus(); }
        }
        Rect EditRect(Object owner, Rect rect, Transform previewTransform, Color color, string label)
        {
            Handles.color = color;
            Handles.DrawAAPolyLine(2, new[] { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin) }
                .Select(p => previewTransform.TransformPoint(p)).ToArray());
            Vector2 center = EditPoint(owner, rect.center, previewTransform, color, label + " center");
            Vector2 corner = EditPoint(owner, rect.max, previewTransform, color, label + " size");
            if (center != rect.center) rect.center = center;
            else if (corner != rect.max)
            {
                Vector2 size = (corner - rect.center) * 2;
                size = new Vector2(Mathf.Max(.001f, Mathf.Abs(size.x)), Mathf.Max(.001f, Mathf.Abs(size.y)));
                rect = new Rect(rect.center - size * .5f, size);
            }
            return rect;
        }
        void DrawPickCollider(Transform sourceTransform, Collider2D pick, Transform previewTransform)
        {
            if (pick == null) return;
            Vector2 ToBody(Vector2 p) => sourceTransform.InverseTransformPoint(pick.transform.TransformPoint(p));
            Vector2 FromBody(Vector2 p) => pick.transform.InverseTransformPoint(sourceTransform.TransformPoint(p));
            Color color = new Color(.6f, 1, .25f);
            if (pick is PolygonCollider2D polygon)
            {
                for (int path = 0; path < polygon.pathCount; path++)
                {
                    var points = polygon.GetPath(path).Select(p => ToBody(p + polygon.offset)).ToArray();
                    EditorGUI.BeginChangeCheck(); EditPath(polygon, points, previewTransform, color, true, "pick " + path);
                    if (EditorGUI.EndChangeCheck()) polygon.SetPath(path, points.Select(p => FromBody(p) - polygon.offset).ToArray());
                }
            }
            else if (pick is BoxCollider2D box)
            {
                Vector2 oldCenter = box.offset, half = box.size * .5f;
                var corners = new[] { oldCenter - half, oldCenter + new Vector2(half.x, -half.y), oldCenter + half, oldCenter + new Vector2(-half.x, half.y), oldCenter - half };
                Handles.color = color; Handles.DrawAAPolyLine(2, corners.Select(p => previewTransform.TransformPoint(ToBody(p))).ToArray());
                box.offset = FromBody(EditPoint(box, ToBody(box.offset), previewTransform, color, "pick center"));
                Vector2 corner = FromBody(EditPoint(box, ToBody(oldCenter + half), previewTransform, color, "pick size"));
                if (corner != oldCenter + half) box.size = new Vector2(Mathf.Max(.01f, Mathf.Abs(corner.x - oldCenter.x) * 2), Mathf.Max(.01f, Mathf.Abs(corner.y - oldCenter.y) * 2));
            }
        }
        void EditPath(Object owner, Vector2[] points, Transform transform, Color color, bool closed, string label)
        {
            if (points == null || points.Length == 0) return;
            Handles.color = color;
            var world = points.Select(p => transform.TransformPoint(p)).ToList(); if (closed) world.Add(world[0]);
            if (world.Count > 1) Handles.DrawAAPolyLine(3, world.ToArray());
            for (int i = 0; i < points.Length; i++) points[i] = EditPoint(owner, points[i], transform, color, label + "/" + i);
        }
        Vector2 EditPoint(Object owner, Vector2 point, Transform transform, Color color, string label)
        {
            Vector3 world = transform.TransformPoint(point); Handles.color = color;
            Handles.Label(world + Vector3.up * .035f, label);
            EditorGUI.BeginChangeCheck();
            Vector3 next = Handles.FreeMoveHandle(world, HandleUtility.GetHandleSize(world) * .045f, Vector3.zero, Handles.DotHandleCap);
            if (!EditorGUI.EndChangeCheck()) return point;
            Undo.RecordObject(owner, "Move " + label); EditorUtility.SetDirty(owner);
            return transform.InverseTransformPoint(next);
        }
    }

    public sealed class FluidExperimentDesignerStage : PreviewSceneStage
    {
        public FluidExperimentDesignerWindow Owner;
        GameObject preview;
        protected override GUIContent CreateHeaderContent() => new GUIContent("Fluid Designer · isolated preview");
        protected override bool OnOpenStage()
        {
            if (!base.OnOpenStage()) return false;
            RefreshPreview(); SceneView.duringSceneGui += OnSceneGUI; return preview != null;
        }
        protected override void OnCloseStage()
        { SceneView.duringSceneGui -= OnSceneGUI; base.OnCloseStage(); }
        public void RefreshPreview()
        {
            if (!scene.IsValid() || Owner == null || Owner.Session?.Root == null) return;
            if (preview != null) DestroyImmediate(preview);
            preview = Instantiate(Owner.Session.Root); preview.name = Owner.Session.Root.name;
            SceneManager.MoveGameObjectToScene(preview, scene); preview.transform.position = Vector3.zero;
            foreach (Transform child in preview.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.NotEditable;
            foreach (var rigid in preview.GetComponentsInChildren<Rigidbody2D>(true)) rigid.simulated = false;
            SceneView.RepaintAll();
        }
        void OnSceneGUI(SceneView view)
        {
            if (StageUtility.GetCurrentStage() != this || preview == null || Owner == null) return;
            var body = preview.GetComponentInChildren<FluidExperimentBody>(true);
            Owner.DrawHandles(body != null ? body.transform : preview.transform);
        }
    }
}
