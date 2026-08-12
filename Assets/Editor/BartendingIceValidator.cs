using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Bartending.EditorTools
{
    public static class BartendingIceValidator
    {
        private const string SandboxScenePath = "Assets/Scenes/Dev/BartendingSandbox.unity";
        private const string RunningKey = "Slainte.BartendingIceValidator.Running";
        private static int phase;
        private static int phaseFrames;
        private static double startedAt;
        private static IceCubeController testCube;
        private static VesselLiquidTracker glassTracker;
        private static VesselLiquidTracker shakerTracker;

        [MenuItem("Slainte/Bartending/Validate Sandbox And Ice")]
        public static void RunFromMenu()
        {
            Begin(false);
        }

        public static void RunFromCommandLine()
        {
            Begin(true);
        }

        public static void ValidateLayoutPreviewFromCommandLine()
        {
            GameObject host = null;
            BartendingSessionInstance preview = null;
            try
            {
                Scene scene = EditorSceneManager.OpenScene("Assets/BusinessScene.unity");
                RectTransform counter = FindRect(scene, "BarCounter");
                RectTransform slots = FindRect(scene, "TableSlots");
                BusinessBartendingSettings settings =
                    Resources.Load<BusinessBartendingSettings>("Bartending/BusinessBartendingSettings");
                Require(counter != null, "BusinessScene has no BarCounter for layout preview.");
                Require(slots != null, "BusinessScene has no TableSlots for layout preview.");
                Require(settings != null, "Business bartending settings could not be loaded.");

                host = new GameObject("__BartendingPreviewValidationHost");
                preview = BartendingSessionBuilder.Build(
                    host.transform,
                    counter,
                    slots,
                    settings,
                    BartendingSessionBuildMode.Preview);
                Require(preview != null, "Layout preview build returned null.");
                Require(preview.IceBin != null, "Layout preview did not create an IceBin.");
                Require(preview.Slots.Count >= 3, "Layout preview did not map the table slots.");
                Require(Vector3.Distance(
                        preview.IceBin.transform.localPosition,
                        settings.iceBinPosition) < 0.001f,
                    "Layout preview did not use the saved IceBin position.");
                Debug.Log("[BartendingLayoutPreviewValidator] PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[BartendingLayoutPreviewValidator] FAIL: " + exception);
                EditorApplication.Exit(1);
            }
            finally
            {
                preview?.Destroy(true);
                if (host != null)
                    UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void Begin(bool commandLine)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxScenePath) == null)
                BartendingSandboxSceneGenerator.GenerateFromCommandLine();

            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(RunningKey + ".CommandLine", commandLine);
            EditorSceneManager.OpenScene(SandboxScenePath);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            if (!SessionState.GetBool(RunningKey, false))
                return;

            EditorApplication.update -= ValidateOnUpdate;
            EditorApplication.update += ValidateOnUpdate;
            phase = 0;
            phaseFrames = 0;
            startedAt = EditorApplication.timeSinceStartup;
        }

        private static void ValidateOnUpdate()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (EditorApplication.timeSinceStartup - startedAt > 20d)
            {
                Finish(false, "Timed out waiting for the sandbox ice validation.");
                return;
            }

            try
            {
                phaseFrames++;
                switch (phase)
                {
                    case 0:
                        BeginIcePlacement();
                        break;
                    case 1:
                        ValidateServingGlassAndMoveToShaker();
                        break;
                    case 2:
                        ValidateTransferAndStrainer();
                        break;
                }
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static void BeginIcePlacement()
        {
            BartendingSandboxBootstrap sandbox =
                UnityEngine.Object.FindFirstObjectByType<BartendingSandboxBootstrap>();
            BartendingSessionInstance session = sandbox != null ? sandbox.CurrentSession : null;
            if (session?.ServingGlass?.LiquidTracker == null || phaseFrames < 5)
                return;

            Require(session.IceBin != null, "IceBin was not created in the sandbox.");
            Require(session.Slots.Count >= 3, "The sandbox did not create the expected slots.");
            Require(session.CobblerShaker != null, "The sandbox did not create a cobbler shaker.");
            Camera displayCamera = GameObject.Find("SandboxDisplayCamera")?.GetComponent<Camera>();
            Require(displayCamera != null
                    && displayCamera.targetTexture == null
                    && displayCamera.cullingMask == 0,
                "The sandbox display camera is missing or can render gameplay layers.");

            BusinessBartendingSettings settings =
                Resources.Load<BusinessBartendingSettings>("Bartending/BusinessBartendingSettings");
            testCube = IceCubeController.Create(
                session.World,
                settings,
                session.RenderLayer,
                session.ItemScale,
                false);
            Require(testCube != null, "A physical ice cube could not be created.");

            glassTracker = session.ServingGlass.LiquidTracker;
            BeakerController shaker = session.CobblerShaker.GameObject.GetComponent<BeakerController>();
            shakerTracker = shaker != null ? shaker.LiquidTracker : null;
            Require(shakerTracker != null, "The cobbler shaker has no vessel tracker.");

            testCube.transform.position = glassTracker.transform.position;
            Rigidbody2D body = testCube.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = glassTracker.transform.position;
                body.linearVelocity = Vector2.zero;
            }
            Physics2D.SyncTransforms();
            phase = 1;
            phaseFrames = 0;
        }

        private static void ValidateServingGlassAndMoveToShaker()
        {
            if (phaseFrames < 12)
                return;

            Require(glassTracker.IceCount == 1, "The serving glass did not acquire one ice cube.");
            CocktailComposition composition = glassTracker.BuildComposition();
            Require(composition.HasIce, "The serving composition did not report HasIce.");
            ValidateIceRequirement(composition, IceRequirement.Required, true);
            ValidateIceRequirement(composition, IceRequirement.None, false);

            Rigidbody2D body = testCube.GetComponent<Rigidbody2D>();
            Vector2 target = shakerTracker.transform.position;
            if (body != null)
            {
                body.position = target;
                body.linearVelocity = Vector2.zero;
            }
            else
            {
                testCube.transform.position = target;
            }
            Physics2D.SyncTransforms();
            phase = 2;
            phaseFrames = 0;
        }

        private static void ValidateTransferAndStrainer()
        {
            if (phaseFrames < 12)
                return;

            Require(glassTracker.IceCount == 0, "The serving glass kept stale ice after transfer.");
            Require(shakerTracker.IceCount == 1, "The shaker did not acquire the transferred ice.");
            Require(!glassTracker.BuildComposition().HasIce,
                "Ice in the shaker incorrectly satisfied serving-glass HasIce.");

            testCube.transform.position = shakerTracker.transform.position + Vector3.up * 4f;
            Physics2D.SyncTransforms();
            Require(shakerTracker.IceCount == 1,
                "The integrated strainer failed to retain its owned ice.");
            Finish(true, "Sandbox creation, ice ownership, serving judgement, transfer, and strainer passed.");
        }

        private static void ValidateIceRequirement(
            CocktailComposition composition,
            IceRequirement requirement,
            bool expected)
        {
            CocktailRecipe recipe = new CocktailRecipe
            {
                id = "ice_validation_" + requirement,
                displayName = "Ice Validation",
                iceRequirement = requirement
            };
            CocktailRecipeCatalog catalog = new CocktailRecipeCatalog();
            catalog.Add(recipe);
            CocktailEvaluationResult result =
                new CocktailEvaluator(catalog).EvaluateRecipe(recipe.id, composition);
            Require(result.iceValid == expected,
                $"IceRequirement.{requirement} returned {result.iceValid}, expected {expected}.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static RectTransform FindRect(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rect.name == name)
                        return rect;
                }
            }

            return null;
        }

        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= ValidateOnUpdate;
            SessionState.EraseBool(RunningKey);
            bool commandLine = SessionState.GetBool(RunningKey + ".CommandLine", false);
            SessionState.EraseBool(RunningKey + ".CommandLine");
            if (success)
                Debug.Log("[BartendingIceValidator] PASS: " + message);
            else
                Debug.LogError("[BartendingIceValidator] FAIL: " + message);

            if (commandLine)
                EditorApplication.Exit(success ? 0 : 1);
            else
                EditorApplication.isPlaying = false;
        }
    }
}
