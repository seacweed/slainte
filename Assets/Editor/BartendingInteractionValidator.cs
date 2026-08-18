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
        private static Vector2 expectedReturnPosition;
        private static BartendingInteractionOverlay oldOverlay;
        private static BeakerController snappingBeaker;
        private static Vector2 expectedSnapPosition;
        private static GameObject snapSlotObject;

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
                        ValidateAllControllerMovement();
                        break;
                    case 4:
                        ValidateReturnCompletionAndResetSession();
                        break;
                    case 5:
                        ValidateSessionRecreation();
                        break;
                    case 6:
                        ValidatePhysicalSlotSnap();
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
            Require(session.Slots.Count == 8, "Expected exactly eight functional bartending slots.");
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

            Require(testParticle.VesselOwner == beaker.LiquidTracker,
                "Tracked liquid lost its vessel ownership.");
            Require(testCube.VesselOwner == beaker.LiquidTracker,
                "Tracked ice did not use the same ownership rule as liquid.");

            Rigidbody2D beakerBody = beaker.GetComponent<Rigidbody2D>();
            Require(beakerBody != null
                    && beakerBody.bodyType == RigidbodyType2D.Kinematic
                    && beakerBody.collisionDetectionMode == CollisionDetectionMode2D.Continuous
                    && beakerBody.interpolation == RigidbodyInterpolation2D.Interpolate,
                "Beaker is not configured for continuous interpolated kinematic transport.");

            AdvancePhase(3);
        }

        private static void ValidateAllControllerMovement()
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
            Vector2 beakerBefore = GetPosition(beaker.gameObject);
            Vector2 beakerDelta = ValidateControllerTiltAndReturnMovement(
                beaker,
                "StartTilting",
                "StartReturning",
                "PerformHorizontalRotationMovement",
                "ApplyPendingPhysicsMotion",
                "ReleaseBeaker",
                true);
            RequireVector(
                particleBefore,
                GetPosition(testParticle.gameObject),
                0.02f,
                "Liquid was translated directly instead of remaining under world physics.");
            RequireVector(
                cubeBefore,
                GetPosition(testCube.gameObject),
                0.02f,
                "Ice was translated directly instead of remaining under world physics.");
            Require(beakerDelta.sqrMagnitude > 0.000001f
                    && Vector2.Distance(GetPosition(beaker.gameObject), beakerBefore) > 0.001f,
                "Beaker did not move during tilt and return validation.");

            ValidateControllerTiltAndReturnMovement(
                shaker,
                "StartTilting",
                "StartReturning",
                "PerformHorizontalRotationMovement",
                "ApplyPendingPhysicsMotion",
                "ReleaseBeaker",
                true);
            ValidateControllerTiltAndReturnMovement(
                bottle,
                "StartTilting",
                "StartReturning",
                "PerformHorizontalRotationMovement",
                null,
                "ReleaseBottle",
                false);

            GlassController glass = session.ServingGlass;
            glass.OnPickedUp();
            InvokeNonPublic(glass, "CancelPointerSynchronization");
            InvokeNonPublic(glass, "StartTilting");
            ApplyInjectedHorizontalMovement(
                glass,
                "PerformHorizontalRotationMovement",
                "ApplyPendingPhysicsMotion");
            InvokeNonPublic(glass, "StartReturning");
            Require(Cursor.lockState == CursorLockMode.None,
                "Glass did not unlock the cursor when return started.");
            InvokeNonPublic(glass, "CancelPointerSynchronization");
            ApplyInjectedReturnMovement(
                glass,
                "ApplyPendingPhysicsMotion");
            returningGlass = glass;
            expectedReturnPosition = GetPosition(glass.gameObject);
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
            RequireVector(expectedReturnPosition, GetPosition(returningGlass.gameObject), 0.02f,
                "Glass snapped when return animation completed.");
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

            snappingBeaker = session.Beaker as BeakerController;
            Require(snappingBeaker != null, "Recreated session has no beaker for slot snap validation.");
            snappingBeaker.OnPickedUp();
            InvokeNonPublic(snappingBeaker, "CancelPointerSynchronization");

            snapSlotObject = new GameObject("InteractionValidationSnapSlot");
            snapSlotObject.transform.SetParent(session.World, false);
            snapSlotObject.transform.position = snappingBeaker.transform.position + new Vector3(0.6f, 0.2f, 0f);
            snapSlotObject.AddComponent<BoxCollider2D>().isTrigger = true;
            snapSlotObject.AddComponent<SpriteRenderer>();
            SlotController snapSlot = snapSlotObject.AddComponent<SlotController>();

            CreateTrackedContents(session);
            Vector2 beakerBeforeSnap = GetPosition(snappingBeaker.gameObject);
            Vector2 particleBeforeSnap = GetPosition(testParticle.gameObject);
            Vector2 cubeBeforeSnap = GetPosition(testCube.gameObject);
            float bottomOffset = InvokeNonPublicWithResult<float>(
                snappingBeaker,
                "GetPivotToBottomOffset");
            expectedSnapPosition = new Vector2(
                snapSlotObject.transform.position.x,
                snapSlotObject.transform.position.y + bottomOffset);

            snappingBeaker.SnapToSlot(snapSlotObject.transform, snapSlot);

            Require(GetPrivateState(snappingBeaker, "currentState") == BeakerState.Idle.ToString(),
                "Runtime slot placement did not complete immediately.");
            Require(snapSlot.IsOccupied && ReferenceEquals(snapSlot.OccupiedItem, snappingBeaker),
                "Immediate slot snap did not reserve its destination slot.");
            RequireVector(expectedSnapPosition, GetPosition(snappingBeaker.gameObject), 0.03f,
                "Immediate slot snap did not reach the requested position.");

            Vector2 vesselDelta = GetPosition(snappingBeaker.gameObject) - beakerBeforeSnap;
            RequireVector(
                particleBeforeSnap + vesselDelta,
                GetPosition(testParticle.gameObject),
                0.03f,
                "Liquid did not move with the beaker during slot placement.");
            RequireVector(
                cubeBeforeSnap + vesselDelta,
                GetPosition(testCube.gameObject),
                0.03f,
                "Ice did not move with the beaker during slot placement.");
            AdvancePhase(6);
        }

        private static void ValidatePhysicalSlotSnap()
        {
            Require(snappingBeaker != null, "Slot-snapped beaker reference was lost.");
            string state = GetPrivateState(snappingBeaker, "currentState");
            Require(state == BeakerState.Idle.ToString(),
                "Beaker did not stay Idle after its immediate slot snap: " + state);
            RequireVector(expectedSnapPosition, GetPosition(snappingBeaker.gameObject), 0.03f,
                "Immediate slot snap did not remain at the requested position.");

            if (snapSlotObject != null)
                UnityEngine.Object.Destroy(snapSlotObject);
            snapSlotObject = null;
            snappingBeaker = null;

            Finish(true,
                "serving boundary, one-shot click, logical target, glass/beaker/shaker labels, "
                + "world-physics liquid and ice ownership, horizontal tilt, two-axis return movement, "
                + "bottle/glass/beaker/shaker return behavior, "
                + "cursor unlock, slot-only contents transport, immediate slot snap, "
                + "and session recreation passed.");
        }

        private static Vector2 ValidateControllerTiltAndReturnMovement(
            IBartendingItem item,
            string startTiltMethod,
            string startReturnMethod,
            string horizontalMethod,
            string applyPhysicsMethod,
            string releaseMethod,
            bool expectHorizontalTiltMovement)
        {
            Component controller = item as Component;
            Require(controller != null, "Bartending item is not a component.");

            item.OnPickedUp();
            InvokeNonPublic(controller, "CancelPointerSynchronization");
            Vector2 startPosition = GetPosition(controller.gameObject);
            InvokeNonPublic(controller, startTiltMethod);
            ApplyInjectedHorizontalMovement(controller, horizontalMethod, applyPhysicsMethod);
            Vector2 afterTiltPosition = GetPosition(controller.gameObject);
            if (expectHorizontalTiltMovement)
            {
                Require(Mathf.Abs(afterTiltPosition.x - startPosition.x) > 0.001f,
                    controller.name + " did not move horizontally while tilting.");
            }
            else
            {
                RequireApproximately(startPosition.x, afterTiltPosition.x, 0.001f,
                    controller.name + " moved horizontally while bottle tilt was active.");
            }
            RequireApproximately(startPosition.y, afterTiltPosition.y, 0.001f,
                controller.name + " moved vertically while tilting.");

            InvokeNonPublic(controller, startReturnMethod);
            Require(GetPrivateState(controller, "currentState").Contains("Returning",
                    StringComparison.Ordinal),
                controller.name + " did not enter Returning state.");
            Require(Cursor.lockState == CursorLockMode.None,
                controller.name + " did not unlock the cursor when return started.");
            InvokeNonPublic(controller, "CancelPointerSynchronization");
            Vector2 returnDelta = ApplyInjectedReturnMovement(controller, applyPhysicsMethod);
            Require(Mathf.Abs(returnDelta.x) > 0.001f && Mathf.Abs(returnDelta.y) > 0.001f,
                controller.name + " did not move on both axes while returning.");

            InvokeNonPublic(controller, releaseMethod);
            return GetPosition(controller.gameObject) - startPosition;
        }

        private static Vector2 ApplyInjectedReturnMovement(
            Component controller,
            string applyPhysicsMethod)
        {
            Vector2 startPosition = GetPosition(controller.gameObject);
            Vector3 pointerAnchor = controller is BottleController bottle
                ? bottle.RotationPivotWorldPosition
                : (Vector3)startPosition;
            Vector2 screenPosition = BartendingViewport.GetPointerScreenPosition(
                Camera.main,
                pointerAnchor);
            Require(BartendingViewport.TryGetInputScreenRect(out Rect viewportRect),
                "Bartending viewport screen rect could not be resolved.");
            float directionX = screenPosition.x <= viewportRect.center.x ? 1f : -1f;
            float directionY = screenPosition.y <= viewportRect.center.y ? 1f : -1f;
            Vector3 targetPointer = pointerAnchor + new Vector3(
                directionX * 0.18f,
                directionY * 0.14f,
                0f);

            InvokeNonPublic(controller, "MoveToPointerPosition", targetPointer);
            if (!string.IsNullOrEmpty(applyPhysicsMethod))
                ApplyQueuedPhysicsPosition(controller, applyPhysicsMethod, "two-axis return");
            InvokeNonPublic(
                controller,
                "BeginPointerSynchronization",
                targetPointer,
                false,
                false);

            Vector2 delta = GetPosition(controller.gameObject) - startPosition;
            Require(Mathf.Abs(delta.x) > 0.001f && Mathf.Abs(delta.y) > 0.001f,
                controller.name + " did not apply the injected two-axis return movement.");
            return delta;
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
                ApplyQueuedPhysicsPosition(controller, applyPhysicsMethod, "horizontal tilt");
        }

        private static void ApplyQueuedPhysicsPosition(
            Component controller,
            string applyPhysicsMethod,
            string movementLabel)
        {
            FieldInfo pendingField = controller.GetType().GetField(
                "positionTargetPending",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(pendingField != null && (bool)pendingField.GetValue(controller),
                $"{controller.name} did not queue a {movementLabel} position target.");
            Vector2 pendingTarget = GetPrivateField<Vector2>(
                controller,
                "pendingPositionTarget");
            Rigidbody2D body = controller.GetComponent<Rigidbody2D>();
            Require(body != null, controller.name + " has no Rigidbody2D.");
            Require(Vector2.Distance(pendingTarget, body.position) > 0.0001f,
                $"{controller.name} queued a zero-distance {movementLabel} position target.");

            InvokeNonPublic(controller, applyPhysicsMethod);
            body.position = pendingTarget;
            Vector3 settledPosition = controller.transform.position;
            settledPosition.x = pendingTarget.x;
            settledPosition.y = pendingTarget.y;
            controller.transform.position = settledPosition;
            Physics2D.SyncTransforms();
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
            BusinessBartendingSettings settings =
                ScriptableObject.CreateInstance<BusinessBartendingSettings>();
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

                RectTransform lowerBoundary = CreateServingLowerBoundary(
                    canvasRect,
                    union.yMin + 40f);
                GameObject viewportObject = new GameObject(
                    "ServingHighlightContractViewport",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage),
                    typeof(BartendingViewport));
                viewportObject.transform.SetParent(canvasRect, false);
                BartendingInteractionOverlay overlay =
                    BartendingInteractionOverlay.Create(
                        viewportObject.GetComponent<BartendingViewport>(),
                        null,
                        null,
                        null,
                        settings);
                Require(overlay != null, "Serving highlight contract overlay was not created.");
                ValidateServingTargetRectangle(overlay, settings);
                overlay.ConfigureServingTarget(
                    stage,
                    lowerBoundary,
                    allowFallbackTarget: false);
                Canvas.ForceUpdateCanvases();

                Require(overlay.TryGetServeTargetScreenRect(out Rect clampedTarget),
                    "Table-clamped logical customer target was not resolved.");
                Vector3[] boundaryCorners = new Vector3[4];
                lowerBoundary.GetWorldCorners(boundaryCorners);
                float tableTop = RectTransformUtility.WorldToScreenPoint(
                    null,
                    boundaryCorners[1]).y;
                RequireApproximately(tableTop, clampedTarget.yMin, 0.01f,
                    "Serving target lower edge was not clamped to the bar table top.");
                Vector4 normalizedTarget = settings.serveTargetNormalized;
                RequireApproximately(
                    Mathf.Clamp01(normalizedTarget.x) * Screen.width,
                    clampedTarget.xMin,
                    0.01f,
                    "Serving target left edge changed with the customer sprite bounds.");
                RequireApproximately(
                    Mathf.Clamp01(normalizedTarget.z) * Screen.width,
                    clampedTarget.width,
                    0.01f,
                    "Serving target width changed with the customer sprite bounds.");
                RequireApproximately(
                    (Mathf.Clamp01(normalizedTarget.y) + Mathf.Clamp01(normalizedTarget.w))
                        * Screen.height,
                    clampedTarget.yMax,
                    0.01f,
                    "Serving target upper edge changed with the customer sprite bounds.");

                right.GetComponent<RectTransform>().anchoredPosition += new Vector2(240f, 80f);
                Canvas.ForceUpdateCanvases();
                Require(overlay.TryGetServeTargetScreenRect(out Rect movedCustomerTarget),
                    "Serving target disappeared after moving a customer sprite.");
                RequireApproximately(clampedTarget.xMin, movedCustomerTarget.xMin, 0.01f,
                    "Serving target position followed a customer sprite.");
                RequireApproximately(clampedTarget.width, movedCustomerTarget.width, 0.01f,
                    "Serving target size followed a customer sprite.");
                RequireApproximately(clampedTarget.yMin, movedCustomerTarget.yMin, 0.01f,
                    "Serving target lower edge followed a customer sprite.");
                RequireApproximately(clampedTarget.yMax, movedCustomerTarget.yMax, 0.01f,
                    "Serving target upper edge followed a customer sprite.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static RectTransform CreateServingLowerBoundary(
            RectTransform canvasRect,
            float targetTopScreenY)
        {
            GameObject boundaryObject = new GameObject(
                "ServingLowerBoundary",
                typeof(RectTransform));
            RectTransform boundary = boundaryObject.GetComponent<RectTransform>();
            boundary.SetParent(canvasRect, false);
            boundary.anchorMin = new Vector2(0.5f, 0.5f);
            boundary.anchorMax = new Vector2(0.5f, 0.5f);
            boundary.pivot = new Vector2(0.5f, 1f);
            boundary.sizeDelta = new Vector2(1200f, 120f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                new Vector2(Screen.width * 0.5f, targetTopScreenY),
                null,
                out Vector2 localTop);
            boundary.anchoredPosition = localTop;
            return boundary;
        }

        private static void ValidateServingTargetRectangle(
            BartendingInteractionOverlay overlay,
            BusinessBartendingSettings settings)
        {
            int expectedBorderCount = settings.serveTargetSprite != null ? 0 : 4;
            Require(overlay.ServingTargetBorderCount == expectedBorderCount,
                $"Serving target border count is incorrect: {overlay.ServingTargetBorderCount}");
            Image[] images = overlay.GetComponentsInChildren<Image>(true);
            int borderCount = 0;
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || !image.name.StartsWith("Border", StringComparison.Ordinal))
                    continue;

                borderCount++;
                Require(image.color == settings.serveTargetOutlineColor,
                    $"Serving target border '{image.name}' has the wrong color.");
                Require(!image.raycastTarget,
                    $"Serving target border '{image.name}' blocks pointer input.");
            }

            Require(borderCount == expectedBorderCount,
                $"Serving target rectangular border image count is incorrect: {borderCount}");
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

        private static T InvokeNonPublicWithResult<T>(
            object target,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = target?.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method != null, $"Private method '{methodName}' was not found.");
            try
            {
                return (T)method.Invoke(target, arguments);
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
