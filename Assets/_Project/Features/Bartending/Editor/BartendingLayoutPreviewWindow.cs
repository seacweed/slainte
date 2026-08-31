using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Bartending.EditorTools
{
    public sealed class BartendingLayoutPreviewWindow : EditorWindow
    {
        private const string SettingsPath =
            "Assets/Resources/Bartending/BusinessBartendingSettings.asset";
        private static BartendingSessionInstance previewSession;
        private static GameObject previewHost;

        [MenuItem("Slainte/Bartending/Layout Preview")]
        private static void Open()
        {
            GetWindow<BartendingLayoutPreviewWindow>("Bartending Layout");
        }

        private void OnGUI()
        {
            Scene scene = SceneManager.GetActiveScene();
            EditorGUILayout.LabelField("Target Scene", scene.IsValid() ? scene.name : "None");
            EditorGUILayout.HelpBox(
                "Slot-bound tools follow TableSlots. Move the IceBin preview in the Scene view, then apply its position to settings.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create / Refresh Preview"))
                    CreatePreview();
                if (GUILayout.Button("Clear Preview"))
                    ClearPreview();
            }

            using (new EditorGUI.DisabledScope(previewSession?.IceBin == null))
            {
                if (GUILayout.Button("Apply IceBin Position To Settings"))
                    ApplyIceBinPosition();
            }

            if (previewSession?.IceBin != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.Vector3Field(
                    "Preview IceBin Local Position",
                    previewSession.IceBin.transform.localPosition);
                if (GUILayout.Button("Select IceBin In Scene"))
                    Selection.activeGameObject = previewSession.IceBin.gameObject;
            }
        }

        private static void CreatePreview()
        {
            ClearPreview();

            Scene scene = SceneManager.GetActiveScene();
            RectTransform counter = FindRectInScene(scene, "BarCounter");
            RectTransform slots = FindRectInScene(scene, "TableSlots");
            BusinessBartendingSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessBartendingSettings>(SettingsPath);
            if (counter == null || settings == null)
            {
                Debug.LogError(
                    "Bartending preview requires BarCounter and BusinessBartendingSettings.");
                return;
            }

            previewHost = new GameObject("__BartendingPreviewHost")
            {
                hideFlags = HideFlags.DontSaveInEditor
            };
            SceneManager.MoveGameObjectToScene(previewHost, scene);
            previewSession = BartendingSessionBuilder.Build(
                previewHost.transform,
                counter,
                slots,
                settings,
                BartendingSessionBuildMode.Preview);
            if (previewSession?.IceBin != null)
                Selection.activeGameObject = previewSession.IceBin.gameObject;
            SceneView.RepaintAll();
        }

        private static void ApplyIceBinPosition()
        {
            if (previewSession?.IceBin == null)
                return;

            BusinessBartendingSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessBartendingSettings>(SettingsPath);
            if (settings == null)
                return;

            Undo.RecordObject(settings, "Apply Bartending IceBin Position");
            settings.iceBinPosition = previewSession.IceBin.transform.localPosition;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("IceBin position saved to BusinessBartendingSettings.");
        }

        private static void ClearPreview()
        {
            if (previewSession != null)
            {
                previewSession.Destroy(true);
                previewSession = null;
            }

            if (previewHost != null)
            {
                DestroyImmediate(previewHost);
                previewHost = null;
            }

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root != null && root.name == "__BartendingPreviewHost")
                    DestroyImmediate(root);
            }

            SceneView.RepaintAll();
        }

        private static RectTransform FindRectInScene(Scene scene, string objectName)
        {
            if (!scene.IsValid())
                return null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rect.name == objectName)
                        return rect;
                }
            }

            return null;
        }

        [InitializeOnLoadMethod]
        private static void RegisterCleanup()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        }

        private static void HandlePlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                ClearPreview();
        }
    }

    public static class BartendingSandboxSceneGenerator
    {
        private const string SandboxScenePath = "Assets/Scenes/Dev/BartendingSandbox.unity";

        [MenuItem("Slainte/Bartending/Create or Open Sandbox Scene")]
        public static void CreateOrOpen()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxScenePath) != null)
            {
                EditorSceneManager.OpenScene(SandboxScenePath);
                return;
            }

            GenerateScene();
        }

        public static void GenerateFromCommandLine()
        {
            GenerateScene();
        }

        private static void GenerateScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes/Dev"))
                AssetDatabase.CreateFolder("Assets/Scenes", "Dev");

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            GameObject host = new GameObject("BartendingSandbox");
            BartendingSandboxBootstrap bootstrap = host.AddComponent<BartendingSandboxBootstrap>();
            bootstrap.EnsureLayout();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SandboxScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Bartending sandbox scene created: " + SandboxScenePath);
        }
    }
}
