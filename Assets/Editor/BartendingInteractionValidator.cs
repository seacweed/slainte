using System;
using System.Collections.Generic;
using System.Reflection;
using Slainte.Bartending;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Slainte.Bartending.EditorTools
{
    public static class BartendingInteractionValidator
    {
        private const string SandboxScenePath = "Assets/Scenes/Dev/BartendingSandbox.unity";
        private const string RunningKey = "Slainte.BartendingInteractionValidator.Running";
        private const float TestLiquidVolumeMl = 37.5f;

        private static int phase;
        private static int phaseFrames;
        private static double phaseStartedAt;
        private static int serveEventCount;
        private static ItemDef testLiquid;
        private static LiquidParticleData testParticle;
        private static IceCubeController testCube;
        private static GlassController returningGlass;
        private static float expectedReturnX;
        private static BartendingInteractionOverlay oldOverlay;

        [MenuItem("Slainte/Bartending/Validate Interaction UI And Rotation")]
        public static void RunFromMenu()
        {
            Begin(false);
        }

        public static void RunFromCommandLine()
        {
            Begin(true);
        }

        private static void Begin(bool commandLine)
        {
            try
            {
                ValidateEditModeContracts();
            }
            catch (Exception exception)
            {
                Debug.LogError("[BartendingInteractionValidator] EDIT FAIL: " + exception);
                if (commandLine)
                    EditorApplication.Exit(1);
                else
                    throw;
                return;
            }

            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxScenePath) != null,
                "Bartending sandbox scene is missing.");
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(RunningKey + ".CommandLine", commandLine);
            EditorSceneManager.OpenScene(SandboxScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            if (!SessionState.GetBool(RunningKey, false))
                return;

            phase = 0;
            phaseFrames = 0;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            serveEventCount = 0;
            EditorApplication.update -= ValidateOnUpdate;
            EditorApplication.update += ValidateOnUpdate;
        }

        private static void ValidateOnUpdate()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (EditorApplication.timeSinceStartup - phaseStartedAt > 45d)
            {
                Finish(false, $"Timed out in validation phase {phase}.");
                return;
            }

            try
            {
                phaseFrames++;
                switch (phase)
                {
                    case 0:
                        ValidateSessionAndPickupGlass();
                        break;
                    case 1:
                        ValidateServingClickAndCreateContents();
                        break;
                    case 2:
                        ValidateContentsAndTransport();
                        break;
                    case 3:
                        ValidateAllControllerHorizontalMovement();
                        break;
                    case 4:
                        ValidateReturnCompletionAndResetSession();
                        break;
                    case 5:
                        ValidateSessionRecreation();
                        break;
                }
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static void ValidateSessionAndPickupGlass()
        {
            BartendingSessionInstance session = GetSession();
            if (session?.InteractionOverlay == null || phaseFrames < 12)
                return;

            Require(session.ServingGlass != null, "Serving glass was not created.");
            Require(session.Beaker is BeakerController, "Beaker controller was not created.");
            Require(session.CobblerShaker is BeakerController,
                "Cobbler shaker is not using the common beaker controller.");
            Require(session.Slots.Count >= 3, "Expected at least three bartending slots.");
            Require(session.InteractionOverlay.VisibleVesselLabelCount >= 3,
                "Glass, beaker and shaker content labels were not created after slot snap.");
            Require(!session.InteractionOverlay.IsServingTargetVisible,
                "Serving target is visible while the serving glass is not held.");

            ValidateEmptyContentsLabel(session.InteractionOverlay, session.ServingGlass, "glass");
            ValidateEmptyContentsLabel(session.InteractionOverlay, session.Beaker, "beaker");
            ValidateEmptyContentsLabel(session.InteractionOverlay, session.CobblerShaker, "shaker");

            session.ServingGlass.ServeRequested += HandleServeRequested;
            session.ServingGlass.OnPickedUp();
            AdvancePhase(1);
        }

        private static void ValidateServingClickAndCreateContents()
        {
            BartendingSessionInstance session = GetSession();
            if (session?.InteractionOverlay == null || phaseFrames < 4)
                return;

            BartendingInteractionOverlay overlay = session.InteractionOverlay;
            GlassController glass = session.ServingGlass;
            Require(overlay.IsServingTargetVisible,
                "Serving target did not appear while the serving glass was held.");
            Require(overlay.TryGetServeTargetScreenRect(out Rect targetRect),
                "Sandbox serving target rect could not be resolved.");
            Require(targetRect.width > 0f && targetRect.height > 0f,
                "Sandbox serving target rect has no area.");
            Require(overlay.VisibleVesselLabelCount >= 2,
                "Picking up the glass removed unrelated beaker or shaker labels.");

            Vector2 outside = new Vector2(targetRect.xMin - 10f, targetRect.center.y);
            Require(!glass.TryRequestServeAtScreenPosition(outside, true),
                "A click outside the serving target was accepted.");
            Require(serveEventCount == 0, "Outside click raised a serve event.");
            Require(glass.TryRequestServeAtScreenPosition(targetRect.center, true),
                "A center click inside the serving target was rejected.");
            Require(serveEventCount == 1, "Inside click did not raise exactly one serve event.");
            Require(!glass.TryRequestServeAtScreenPosition(targetRect.center, true),
                "The same glass was served more than once.");
            Require(serveEventCount == 1, "Duplicate serving raised an extra event.");

            CreateTrackedContents(session);
            AdvancePhase(2);
        }

        private static void ValidateContentsAndTransport()
        {
            BartendingSessionInstance session = GetSession();
            if (session?.InteractionOverlay == null
                || EditorApplication.timeSinceStartup - phaseStartedAt < 0.3d)
                return;

            Require(!session.InteractionOverlay.IsServingTargetVisible,
                "Serving target stayed visible after serving completed.");
            BeakerController beaker = session.Beaker as BeakerController;
            Require(beaker?.LiquidTracker != null, "Beaker tracker is missing.");
            CocktailComposition composition = beaker.LiquidTracker.BuildComposition();
            RequireApproximately(TestLiquidVolumeMl, composition.TotalVolumeMl, 0.01f,
                "Tracked test liquid volume is incorrect.");

            Require(session.InteractionOverlay.TryGetVesselContentsText(beaker, out string text),
                "Beaker contents label disappeared while it was slotted.");
            Require(text.Contains(testLiquid.displayName, StringComparison.Ordinal),
                "Beaker contents label does not show the liquid name: " + text);
            Require(text.Contains("37.5 ml", StringComparison.Ordinal),
                "Beaker contents label does not show the liquid volume: " + text);
            Require(text.Contains("합계 37.5 ml", StringComparison.Ordinal),
                "Beaker contents label does not show the total volume: " + text);

            Vector2 particleBefore = GetPosition(testParticle.gameObject);
            Vector2 cubeBefore = GetPosition(testCube.gameObject);
            Vector2 transportDelta = new Vector2(0.1f, 0.05f);
            beaker.LiquidTracker.TranslateTrackedParticles(transportDelta);
            RequireVector(particleBefore + transportDelta, GetPosition(testParticle.gameObject), 0.001f,
                "Tracked liquid did not use the vessel transport rule.");
            RequireVector(cubeBefore + transportDelta, GetPosition(testCube.gameObject), 0.001f,
                "Tracked ice did not use the same vessel transport rule as liquid.");
            beaker.LiquidTracker.TranslateTrackedParticles(-transportDelta);
            RequireVector(particleBefore, GetPosition(testParticle.gameObject), 0.001f,
                "Tracked liquid did not return after transport verification.");
            RequireVector(cubeBefore, GetPosition(testCube.gameObject), 0.001f,
                "Tracked ice did not return after transport verification.");

            AdvancePhase(3);
        }

        private static void ValidateAllControllerHorizontalMovement()
        {
            BartendingSessionInstance session = GetSession();
            if (session == null || phaseFrames < 2)
                return;

            if (Mouse.current == null)
                InputSystem.AddDevice<Mouse>();

            BeakerController beaker = session.Beaker as BeakerController;
            BeakerController shaker = session.CobblerShaker as BeakerController;
            BottleController bottle = UnityEngine.Object.FindFirstObjectByType<BottleController>();
            Require(beaker != null && shaker != null && bottle != null,
                "Could not resolve every rotating bartending controller.");

            Vector2 particleBefore = GetPosition(testParticle.gameObject);
            Vector2 cubeBefore = GetPosition(testCube.gameObject);
            float beakerBefore = GetPosition(beaker.gameObject).x;
            float beakerDelta = ValidateControllerTiltAndReturnMovement(
                beaker,
                "StartTilting",
                "StartReturning",
                "PerformHorizontalRotationMovement",
                "ApplyPendingPhysicsMotion",
                "ReleaseBeaker");
            RequireApproximately(
                beakerDelta,
                GetPosition(testParticle.gameObject).x - particleBefore.x,
                0.01f,
                "Liquid did not match beaker horizontal movement.");
            RequireApproximately(
                beakerDelta,
                GetPosition(testCube.gameObject).x - cubeBefore.x,
                0.01f,
                "Ice did not match beaker horizontal movement.");
            Require(Mathf.Abs(GetPosition(beaker.gameObject).x - beakerBefore) > 0.001f,
                "Beaker did not move horizontally.");

            ValidateControllerTiltAndReturnMovement(
                shaker,
                "StartTilting",
                "StartReturning",
                "PerformHorizontalRotationMovement",
                "ApplyPendingPhysicsMotion",
                "ReleaseBeaker");
            ValidateControllerTiltAndReturnMovement(
                bottle,
                "StartTilting",
                "StartReturning",
                "PerformHorizontalRotationMovement",
                null,
                "ReleaseBottle");

            GlassController glass = session.ServingGlass;
            glass.OnPickedUp();
            InvokeNonPublic(glass, "CancelPointerSynchronization");
            InvokeNonPublic(glass, "StartTilting");
            ApplyInjectedHorizontalMovement(
                glass,
                "PerformHorizontalRotationMovement",
                "ApplyPendingPhysicsMotion");
            InvokeNonPublic(glass, "StartReturning");
            ApplyInjectedHorizontalMovement(
                glass,
                "PerformHorizontalRotationMovement",
                "ApplyPendingPhysicsMotion");
            returningGlass = glass;
            expectedReturnX = GetPosition(glass.gameObject).x;
            AdvancePhase(4);
        }

        private static void ValidateReturnCompletionAndResetSession()
        {
            if (returningGlass == null)
                throw new InvalidOperationException("Returning glass reference was lost.");

            string state = GetPrivateState(returningGlass, "currentState");
            if (state == GlassState.Returning.ToString())
                return;

            Require(state == GlassState.PickedUp.ToString(),
                "Glass did not return to PickedUp after its return animation: " + state);
            RequireApproximately(expectedReturnX, GetPosition(returningGlass.gameObject).x, 0.02f,
                "Glass snapped horizontally when return animation completed.");
            RequireApproximately(0f, returningGlass.transform.eulerAngles.z, 0.1f,
                "Glass did not return to its upright rotation.");
            Require(Cursor.lockState == CursorLockMode.None,
                "Cursor stayed locked after return animation completed.");
            InvokeNonPublic(returningGlass, "ReleaseGlass");

            BartendingSandboxBootstrap sandbox = GetSandbox();
            BartendingSessionInstance session = sandbox.CurrentSession;
            oldOverlay = session.InteractionOverlay;
            sandbox.ResetSession();
            if (testLiquid != null)
                UnityEngine.Object.Destroy(testLiquid);
            testLiquid = null;
            testParticle = null;
            testCube = null;
            returningGlass = null;
            AdvancePhase(5);
        }

        private static void ValidateSessionRecreation()
        {
            BartendingSessionInstance session = GetSession();
            if (session?.InteractionOverlay == null || phaseFrames < 12)
                return;

            Require(session.InteractionOverlay != oldOverlay,
                "Session reset reused the destroyed interaction overlay.");
            Require(oldOverlay == null,
                "Old interaction overlay survived session destruction.");
            Require(session.InteractionOverlay.VisibleVesselLabelCount >= 3,
                "Recreated session did not restore vessel labels.");
            Require(session.InteractionOverlay.TryGetServeTargetScreenRect(out Rect targetRect)
                    && targetRect.width > 0f
                    && targetRect.height > 0f,
                "Recreated session did not restore its serving target.");

            Finish(true,
                "serving boundary, one-shot click, logical target, glass/beaker/shaker labels, "
                + "liquid and ice transport, bottle/glass/beaker/shaker horizontal tilt and return, "
                + "cursor unlock, and session recreation passed.");
        }

        private static float ValidateControllerTiltAndReturnMovement(
            IBartendingItem item,
            string startTiltMethod,
            string startReturnMethod,
            string horizontalMethod,
            string applyPhysicsMethod,
            string releaseMethod)
        {
            Component controller = item as Component;
            Require(controller != null, "Bartending item is not a component.");

            item.OnPickedUp();
            InvokeNonPublic(controller, "CancelPointerSynchronization");
            float startX = GetPosition(controller.gameObject).x;
            InvokeNonPublic(controller, startTiltMethod);
            ApplyInjectedHorizontalMovement(controller, horizontalMethod, applyPhysicsMethod);
            float afterTiltX = GetPosition(controller.gameObject).x;
            Require(Mathf.Abs(afterTiltX - startX) > 0.001f,
                controller.name + " did not move horizontally while tilting.");

            InvokeNonPublic(controller, startReturnMethod);
            Require(GetPrivateState(controller, "currentState").Contains("Returning",
                    StringComparison.Ordinal),
                controller.name + " did not enter Returning state.");
            ApplyInjectedHorizontalMovement(controller, horizontalMethod, applyPhysicsMethod);
            float afterReturnMoveX = GetPosition(controller.gameObject).x;
            Require(Mathf.Abs(afterReturnMoveX - afterTiltX) > 0.001f,
                controller.name + " did not move horizontally while returning.");

            InvokeNonPublic(controller, releaseMethod);
            return afterReturnMoveX - startX;
        }

        private static void ApplyInjectedHorizontalMovement(
            Component controller,
            string horizontalMethod,
            string applyPhysicsMethod)
        {
            Require(Mouse.current != null, "No mouse device is available for delta injection.");
            Require(BartendingViewport.TryGetInputScreenRect(out Rect viewportRect),
                "Bartending viewport screen rect could not be resolved.");
            Collider2D collider = controller.GetComponent<Collider2D>();
            Require(collider != null, controller.name + " has no collider for bounds clamping.");
            Vector2 screenCenter = BartendingViewport.GetPointerScreenPosition(
                Camera.main,
                collider.bounds.center);
            float direction = screenCenter.x <= viewportRect.center.x ? 1f : -1f;
            Vector2 injectedDelta = new Vector2(direction * 36f, 0f);
            InputState.Change(Mouse.current.delta, injectedDelta);
            RequireVector(
                injectedDelta,
                Mouse.current.delta.ReadValue(),
                0.001f,
                "Injected mouse delta was not applied.");
            bool convertible = BartendingViewport.TryConvertClampedHorizontalScreenDelta(
                Camera.main,
                collider.bounds,
                injectedDelta.x,
                12f,
                out float expectedWorldDelta);
            Require(convertible && Mathf.Abs(expectedWorldDelta) > 0.0001f,
                $"{controller.name} mouse delta could not be converted. "
                + $"screenDelta={injectedDelta.x:0.###}, viewport={viewportRect}, "
                + $"worldBounds={collider.bounds}");
            InvokeNonPublic(controller, horizontalMethod);
            if (!string.IsNullOrEmpty(applyPhysicsMethod))
            {
                FieldInfo pendingField = controller.GetType().GetField(
                    "positionTargetPending",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Require(pendingField != null && (bool)pendingField.GetValue(controller),
                    controller.name + " did not queue a horizontal position target.");
                Vector2 pendingTarget = GetPrivateField<Vector2>(
                    controller,
                    "pendingPositionTarget");
                Rigidbody2D body = controller.GetComponent<Rigidbody2D>();
                Require(body != null, controller.name + " has no Rigidbody2D.");
                Require(Mathf.Abs(pendingTarget.x - body.position.x) > 0.0001f,
                    controller.name + " queued a zero-distance position target.");

                InvokeNonPublic(controller, applyPhysicsMethod);
                body.position = pendingTarget;
                Vector3 settledPosition = controller.transform.position;
                settledPosition.x = pendingTarget.x;
                settledPosition.y = pendingTarget.y;
                controller.transform.position = settledPosition;
                Physics2D.SyncTransforms();
            }
        }

        private static void CreateTrackedContents(BartendingSessionInstance session)
        {
            BeakerController beaker = session.Beaker as BeakerController;
            Require(beaker?.LiquidTracker != null, "Beaker tracker is missing.");

            testLiquid = ScriptableObject.CreateInstance<ItemDef>();
            testLiquid.id = "interaction_validation_liquid";
            testLiquid.displayName = "자동 검증 액체";
            testLiquid.type = ItemType.Bottle;

            GameObject particleObject = new GameObject("InteractionValidationLiquid");
            particleObject.transform.SetParent(session.World, false);
            particleObject.transform.position = beaker.transform.position;
            CircleCollider2D particleCollider = particleObject.AddComponent<CircleCollider2D>();
            particleCollider.radius = 0.08f;
            Rigidbody2D particleBody = particleObject.AddComponent<Rigidbody2D>();
            particleBody.gravityScale = 0f;
            testParticle = particleObject.AddComponent<LiquidParticleData>();
            testParticle.SetPayload(testLiquid, TestLiquidVolumeMl);
            InvokeNonPublic(beaker.LiquidTracker, "TrackParticle", testParticle);

            BusinessBartendingSettings settings =
                Resources.Load<BusinessBartendingSettings>("Bartending/BusinessBartendingSettings");
            testCube = IceCubeController.Create(
                session.World,
                settings,
                session.RenderLayer,
                session.ItemScale,
                false);
            Require(testCube != null, "Test ice cube could not be created.");
            testCube.transform.position = beaker.transform.position;
            Rigidbody2D cubeBody = testCube.GetComponent<Rigidbody2D>();
            if (cubeBody != null)
            {
                cubeBody.position = beaker.transform.position;
                cubeBody.linearVelocity = Vector2.zero;
            }
            InvokeNonPublic(beaker.LiquidTracker, "TrackIceCube", testCube);
        }

        private static void ValidateEditModeContracts()
        {
            ValidateServingBoundaryContract();
            ValidateSlotOccupancyContract();
            ValidateLogicalCustomerUnionContract();
            Debug.Log("[BartendingInteractionValidator] Edit-mode contracts passed.");
        }

        private static void ValidateServingBoundaryContract()
        {
            GameObject glassObject = new GameObject("ServingBoundaryContract");
            try
            {
                glassObject.AddComponent<EdgeCollider2D>();
                glassObject.AddComponent<BoxCollider2D>();
                GlassController glass = glassObject.AddComponent<GlassController>();
                Rect targetRect = new Rect(100f, 200f, 300f, 400f);
                glass.ConfigureServeGesture(new FixedServeTarget(targetRect));
                int count = 0;
                glass.ServeRequested += _ => count++;

                Require(!glass.TryRequestServeAtScreenPosition(new Vector2(99f, 300f), true),
                    "Outside serving point was accepted in edit contract.");
                Require(glass.TryRequestServeAtScreenPosition(targetRect.center, true),
                    "Center serving point was rejected in edit contract.");
                Require(!glass.TryRequestServeAtScreenPosition(targetRect.center, true),
                    "Duplicate serving was accepted in edit contract.");
                Require(count == 1, "Serving contract did not emit exactly one event.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(glassObject);
            }
        }

        private static void ValidateSlotOccupancyContract()
        {
            GameObject slotObject = new GameObject("SlotOccupancyContract");
            GameObject beakerObject = new GameObject("SlotOccupancyItem");
            try
            {
                slotObject.AddComponent<SpriteRenderer>();
                slotObject.AddComponent<BoxCollider2D>();
                SlotController slot = slotObject.AddComponent<SlotController>();
                beakerObject.AddComponent<EdgeCollider2D>();
                beakerObject.AddComponent<BoxCollider2D>();
                BeakerController beaker = beakerObject.AddComponent<BeakerController>();
                int events = 0;
                IBartendingItem lastItem = null;
                slot.OccupancyChanged += (_, item) =>
                {
                    events++;
                    lastItem = item;
                };

                slot.Occupy(beaker);
                Require(slot.IsOccupied
                        && ReferenceEquals(slot.OccupiedItem, beaker)
                        && ReferenceEquals(lastItem, beaker),
                    "Slot occupy event did not expose the occupied vessel.");
                slot.Vacate();
                Require(!slot.IsOccupied && slot.OccupiedItem == null && lastItem == null,
                    "Slot vacate event did not clear the vessel.");
                Require(events == 2, "Slot occupancy contract emitted an unexpected event count.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(beakerObject);
                UnityEngine.Object.DestroyImmediate(slotObject);
            }
        }

        private static void ValidateLogicalCustomerUnionContract()
        {
            GameObject canvasObject = new GameObject(
                "LogicalCustomerUnionCanvas",
                typeof(RectTransform),
                typeof(Canvas));
            Texture2D texture = new Texture2D(4, 4);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 4f, 4f),
                new Vector2(0.5f, 0.5f));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(1920f, 1080f);

                CharacterStage stage = canvasObject.AddComponent<CharacterStage>();
                CharacterView left = CreateCharacterView(
                    canvasRect,
                    "logical_left",
                    new Vector2(-300f, 0f),
                    sprite);
                CharacterView right = CreateCharacterView(
                    canvasRect,
                    "logical_right",
                    new Vector2(350f, 0f),
                    sprite);
                Dictionary<string, CharacterView> views =
                    GetPrivateField<Dictionary<string, CharacterView>>(stage, "_activeViews");
                views.Add("left", left);
                views.Add("right", right);
                Canvas.ForceUpdateCanvases();

                Require(left.TryGetVisualScreenRect(out Rect leftRect),
                    "Left logical customer has no screen rect.");
                Require(right.TryGetVisualScreenRect(out Rect rightRect),
                    "Right logical customer has no screen rect.");
                Require(stage.TryGetActiveGroupScreenRect(out Rect union),
                    "Logical customer group did not produce a union rect.");
                RequireApproximately(
                    Mathf.Min(leftRect.xMin, rightRect.xMin),
                    union.xMin,
                    0.01f,
                    "Logical customer union left edge is incorrect.");
                RequireApproximately(
                    Mathf.Max(leftRect.xMax, rightRect.xMax),
                    union.xMax,
                    0.01f,
                    "Logical customer union right edge is incorrect.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static CharacterView CreateCharacterView(
            RectTransform parent,
            string name,
            Vector2 position,
            Sprite sprite)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(parent, false);
            rootRect.sizeDelta = new Vector2(220f, 500f);
            rootRect.anchoredPosition = position;

            GameObject visual = new GameObject(
                "Visual",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup),
                typeof(AspectRatioFitter));
            RectTransform visualRect = visual.GetComponent<RectTransform>();
            visualRect.SetParent(rootRect, false);
            visualRect.sizeDelta = rootRect.sizeDelta;
            CharacterView view = root.AddComponent<CharacterView>();
            InvokeNonPublic(view, "Awake");
            view.Setup(sprite);
            return view;
        }

        private static void ValidateEmptyContentsLabel(
            BartendingInteractionOverlay overlay,
            IBartendingItem item,
            string label)
        {
            Require(overlay.TryGetVesselContentsText(item, out string text),
                label + " contents label is missing.");
            Require(text.Contains("합계 0 ml", StringComparison.Ordinal),
                label + " empty contents label is incorrect: " + text);
        }

        private static void HandleServeRequested(GlassController glass)
        {
            serveEventCount++;
        }

        private static BartendingSandboxBootstrap GetSandbox()
        {
            BartendingSandboxBootstrap sandbox =
                UnityEngine.Object.FindFirstObjectByType<BartendingSandboxBootstrap>();
            if (sandbox == null)
                throw new InvalidOperationException("Bartending sandbox bootstrap is missing.");
            return sandbox;
        }

        private static BartendingSessionInstance GetSession()
        {
            BartendingSandboxBootstrap sandbox =
                UnityEngine.Object.FindFirstObjectByType<BartendingSandboxBootstrap>();
            return sandbox != null ? sandbox.CurrentSession : null;
        }

        private static Vector2 GetPosition(GameObject target)
        {
            Rigidbody2D body = target != null ? target.GetComponent<Rigidbody2D>() : null;
            return body != null ? body.position : (Vector2)target.transform.position;
        }

        private static string GetPrivateState(object target, string fieldName)
        {
            object value = GetPrivateField<object>(target, fieldName);
            return value?.ToString() ?? string.Empty;
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target?.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, $"Private field '{fieldName}' was not found.");
            return (T)field.GetValue(target);
        }

        private static void InvokeNonPublic(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target?.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method != null, $"Private method '{methodName}' was not found.");
            try
            {
                method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static void AdvancePhase(int nextPhase)
        {
            phase = nextPhase;
            phaseFrames = 0;
            phaseStartedAt = EditorApplication.timeSinceStartup;
        }

        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= ValidateOnUpdate;
            SessionState.EraseBool(RunningKey);
            bool commandLine = SessionState.GetBool(RunningKey + ".CommandLine", false);
            SessionState.EraseBool(RunningKey + ".CommandLine");

            if (success)
                Debug.Log("[BartendingInteractionValidator] PASS: " + message);
            else
                Debug.LogError("[BartendingInteractionValidator] FAIL: " + message);

            if (commandLine)
                EditorApplication.Exit(success ? 0 : 1);
            else
                EditorApplication.isPlaying = false;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void RequireApproximately(
            float expected,
            float actual,
            float tolerance,
            string message)
        {
            if (Mathf.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException(
                    $"{message} expected={expected:0.###}, actual={actual:0.###}");
        }

        private static void RequireVector(
            Vector2 expected,
            Vector2 actual,
            float tolerance,
            string message)
        {
            if (Vector2.Distance(expected, actual) > tolerance)
                throw new InvalidOperationException(
                    $"{message} expected={expected}, actual={actual}");
        }

        private sealed class FixedServeTarget : IBartendingServeTarget
        {
            private readonly Rect rect;

            public FixedServeTarget(Rect targetRect)
            {
                rect = targetRect;
            }

            public bool TryGetServeTargetScreenRect(out Rect screenRect)
            {
                screenRect = rect;
                return true;
            }
        }
    }
}
