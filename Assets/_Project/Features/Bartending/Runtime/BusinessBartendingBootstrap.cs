using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    public sealed class BusinessBartendingBootstrap : MonoBehaviour
    {
        private struct SuspendedBodyState
        {
            public Rigidbody2D body;
            public bool simulated;
            public Vector2 linearVelocity;
            public float angularVelocity;
        }

        private const string SceneName = "BusinessScene";
        private const string SettingsResourcePath = "Bartending/BusinessBartendingSettings";

        private readonly List<GameObject> hiddenCanvasItems = new List<GameObject>();
        private readonly List<BottleController> sessionBottles = new List<BottleController>();
        private readonly List<LiquorBottleDef> selectedBottleDefinitions = new List<LiquorBottleDef>();
        private readonly List<SlotController> sessionSlots = new List<SlotController>();
        private readonly Dictionary<BottleController, float> bottleReserveAmounts =
            new Dictionary<BottleController, float>();
        private readonly Dictionary<string, GameObject> cabinetInstances =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<GlassController> deployedServingGlasses =
            new HashSet<GlassController>();
        private readonly ToolCabinetShiftState cabinetShiftState = new ToolCabinetShiftState();
        private Scene targetScene;
        private BusinessBartendingSettings settings;
        private ItemDefCatalog itemCatalog;
        private GameModeManager modeManager;
        private GameObject sessionRoot;
        private Transform sessionWorld;
        private BartendingSelectionCoordinator selectionCoordinator;
        private BartendingViewport sessionViewport;
        private RectTransform slotLayoutTemplate;
        private RectTransform sessionSlotLayout;
        private LiquidPool sessionLiquidPool;
        private BartendingSessionInstance builtSession;
        private int sessionRenderLayer;
        private float sessionItemScale = 1f;
        private Coroutine snapRoutine;
        private Coroutine readyRoutine;
        private Camera sourceCamera;
        private int sourceCameraMask;
        private bool slotLayoutTemplateWasActive;
        private ToolCabinetController toolCabinet;
        private IceBinController cabinetIceBucket;
        private Transform cabinetSlotRoot;
        private Transform cabinetPickupAnchor;
        private FrontCameraRig frontCameraRig;
        private Coroutine resumeViewTransitionRoutine;
        private bool viewTransitionSuspended;
        private readonly List<SuspendedBodyState> suspendedBodyStates =
            new List<SuspendedBodyState>();
        private readonly List<IBartendingViewTransitionParticipant> viewTransitionParticipants =
            new List<IBartendingViewTransitionParticipant>();

        public bool IsSessionReady => sessionRoot != null && builtSession != null;
        public int SessionBottleCount => sessionBottles.Count;
        public event Action SessionReady;
        public event Action SessionDestroyed;
        public event Action<VesselLiquidTracker> ServeRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneHook()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallInInitialScene()
        {
            InstallIfNeeded(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            InstallIfNeeded(scene);
        }

        private static void InstallIfNeeded(Scene scene)
        {
            if (!scene.IsValid() || scene.name != SceneName)
                return;

            EnsureInstalledForScene(scene);
        }

        public static BusinessBartendingBootstrap EnsureInstalledForScene(Scene scene)
        {
            if (!scene.IsValid())
                return null;

            BusinessBartendingBootstrap existing =
                FindInScene<BusinessBartendingBootstrap>(scene);
            if (existing != null)
                return existing;

            BusinessBartendingSettings settings =
                Resources.Load<BusinessBartendingSettings>(SettingsResourcePath);
            if (settings == null)
            {
                Debug.LogError("Resources에서 영업 제조 설정을 불러올 수 없습니다.");
                return null;
            }

            GameObject host = new GameObject("BusinessBartendingRuntime");
            SceneManager.MoveGameObjectToScene(host, scene);
            BusinessBartendingBootstrap bootstrap = host.AddComponent<BusinessBartendingBootstrap>();
            bootstrap.Initialize(scene, settings);
            return bootstrap;
        }

        private void Initialize(Scene scene, BusinessBartendingSettings sessionSettings)
        {
            targetScene = scene;
            settings = sessionSettings;
            itemCatalog = ItemDefCatalog.LoadFromResources("Items", null);
            modeManager = FindInScene<GameModeManager>(scene);
            if (modeManager == null)
            {
                Debug.LogError("영업 제조 화면에서 제조 모드를 관리할 GameModeManager가 필요합니다.");
                enabled = false;
                return;
            }

            frontCameraRig = FindInScene<FrontCameraRig>(scene);
            if (frontCameraRig != null)
            {
                frontCameraRig.MoveStarted += HandleFrontWorldMoveStarted;
                frontCameraRig.MoveUpdated += HandleFrontWorldMoveUpdated;
                frontCameraRig.MoveCompleted += HandleFrontWorldMoveCompleted;
            }

            CaptureSlotLayoutTemplate(scene);
            HideCanvasBartendingItems(scene);
            toolCabinet = GetComponent<ToolCabinetController>();
            if (toolCabinet == null)
                toolCabinet = gameObject.AddComponent<ToolCabinetController>();
            toolCabinet.Initialize(scene, this);
            modeManager.OnModeChanged += HandleModeChanged;
            StartCoroutine(SyncInitialMode());
        }

        private IEnumerator SyncInitialMode()
        {
            yield return null;
            if (modeManager != null && modeManager.CurrentMode == GameMode.CraftingMode)
            {
                CreateSession();
            }
        }

        private void HandleModeChanged(GameMode oldMode, GameMode newMode)
        {
            if (newMode == GameMode.CraftingMode)
            {
                CreateSession();
            }
            else if (oldMode == GameMode.CraftingMode || sessionRoot != null)
            {
                DestroySession(clearBottleSelections: true);
            }
        }

        private void CreateSession()
        {
            if (sessionRoot != null || settings == null)
            {
                return;
            }

            RectTransform counter = FindNamedRectTransform(targetScene, "BarCounter");
            if (counter == null)
            {
                Debug.LogError("영업 제조 화면에 BarCounter RectTransform이 필요합니다.");
                return;
            }

            builtSession = BartendingSessionBuilder.Build(
                transform,
                counter,
                slotLayoutTemplate,
                settings,
                BartendingSessionBuildMode.Runtime,
                settings.useToolCabinet && toolCabinet != null && toolCabinet.IsConfigured,
                toolCabinet != null ? toolCabinet.WorldRenderExtensionRect : null);
            if (builtSession == null)
            {
                Debug.LogError("영업 제조 세션을 생성하지 못했습니다.");
                return;
            }

            sessionRenderLayer = builtSession.RenderLayer;
            sessionRoot = builtSession.Root;
            sessionWorld = builtSession.World;
            selectionCoordinator = sessionWorld != null
                ? sessionWorld.GetComponent<BartendingSelectionCoordinator>()
                    ?? sessionWorld.gameObject.AddComponent<BartendingSelectionCoordinator>()
                : null;
            sessionViewport = builtSession.Viewport;
            sessionViewport?.SetOutputVisible(false);
            sessionSlotLayout = builtSession.SlotLayout;
            sessionLiquidPool = builtSession.LiquidPool;
            sessionItemScale = builtSession.ItemScale;
            sessionSlots.Clear();
            sessionSlots.AddRange(builtSession.Slots);
            CharacterStage characterStage = FindInScene<CharacterStage>(targetScene);
            builtSession.InteractionOverlay?.ConfigureServingTarget(
                characterStage,
                counter,
                allowFallbackTarget: false);
            List<BottleController> selectedBottles = CreateSelectedBottles();
            snapRoutine = StartCoroutine(SnapStartingItems(builtSession.StartingTools, selectedBottles));

            readyRoutine = StartCoroutine(CaptureTargetTracker(builtSession.ServingGlass));
            toolCabinet?.NotifySessionReady();

            sourceCamera = Camera.main;
            if (sourceCamera != null && sourceCamera != builtSession.WorldCamera)
            {
                sourceCameraMask = sourceCamera.cullingMask;
                sourceCamera.cullingMask &= ~(1 << sessionRenderLayer);
            }

            if (frontCameraRig != null && frontCameraRig.IsAnimating)
                SuspendSessionForViewTransition();
        }

        private void DestroySession(bool clearBottleSelections)
        {
            ClearViewTransitionState();
            UnregisterAllServingGlasses();
            if (snapRoutine != null)
            {
                StopCoroutine(snapRoutine);
                snapRoutine = null;
            }

            for (int i = 0; i < sessionBottles.Count; i++)
            {
                if (sessionBottles[i] != null)
                    sessionBottles[i].CapacityChanged -= HandleBottleCapacityChanged;
            }

            if (readyRoutine != null)
            {
                StopCoroutine(readyRoutine);
                readyRoutine = null;
            }
            sessionBottles.Clear();
            bottleReserveAmounts.Clear();
            cabinetInstances.Clear();
            deployedServingGlasses.Clear();
            cabinetIceBucket = null;
            cabinetSlotRoot = null;
            cabinetPickupAnchor = null;
            sessionSlots.Clear();

            if (builtSession != null)
            {
                builtSession.Destroy();
                builtSession = null;
            }
            else if (sessionRoot != null)
            {
                sessionRoot.SetActive(false);
                Destroy(sessionRoot);
            }
            sessionRoot = null;
            sessionWorld = null;
            selectionCoordinator = null;
            sessionViewport = null;
            sessionSlotLayout = null;
            sessionLiquidPool = null;

            if (clearBottleSelections)
                selectedBottleDefinitions.Clear();

            RestoreSessionOverrides();
            toolCabinet?.NotifySessionDestroyed();
            SessionDestroyed?.Invoke();
        }

        private void HandleFrontWorldMoveStarted()
        {
            SuspendSessionForViewTransition();
        }

        private void HandleFrontWorldMoveCompleted()
        {
            if (!viewTransitionSuspended)
                return;

            if (resumeViewTransitionRoutine != null)
                StopCoroutine(resumeViewTransitionRoutine);
            resumeViewTransitionRoutine = StartCoroutine(ResumeSessionAfterViewTransition());
        }

        private void HandleFrontWorldMoveUpdated()
        {
            if (!viewTransitionSuspended
                || sessionViewport == null
                || !sessionViewport.TryMapPointerToWorldWhileSuspended(
                    Input.mousePosition,
                    out Vector3 pointerWorld))
            {
                return;
            }

            for (int i = 0; i < viewTransitionParticipants.Count; i++)
            {
                IBartendingViewTransitionParticipant participant =
                    viewTransitionParticipants[i];
                if (participant is MonoBehaviour behaviour
                    && behaviour != null
                    && behaviour.isActiveAndEnabled)
                {
                    participant.UpdateForViewTransition(pointerWorld);
                }
            }
        }

        private void SuspendSessionForViewTransition()
        {
            if (resumeViewTransitionRoutine != null)
            {
                StopCoroutine(resumeViewTransitionRoutine);
                resumeViewTransitionRoutine = null;
            }

            if (viewTransitionSuspended || sessionWorld == null)
                return;

            viewTransitionSuspended = true;
            sessionViewport?.SetInputSuspended(true);

            viewTransitionParticipants.Clear();
            MonoBehaviour[] behaviours =
                sessionWorld.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null || !behaviours[i].isActiveAndEnabled)
                    continue;
                if (behaviours[i] is not IBartendingViewTransitionParticipant participant)
                    continue;

                viewTransitionParticipants.Add(participant);
                participant.SuspendForViewTransition();
            }

            suspendedBodyStates.Clear();
            Rigidbody2D[] bodies = sessionWorld.GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody2D body = bodies[i];
                if (body == null || !body.gameObject.activeInHierarchy)
                    continue;

                suspendedBodyStates.Add(new SuspendedBodyState
                {
                    body = body,
                    simulated = body.simulated,
                    linearVelocity = body.linearVelocity,
                    angularVelocity = body.angularVelocity
                });
                if (body.simulated)
                    body.simulated = false;
            }
        }

        private IEnumerator ResumeSessionAfterViewTransition()
        {
            yield return null;

            Canvas.ForceUpdateCanvases();
            sessionViewport?.SetInputSuspended(false);

            for (int i = 0; i < suspendedBodyStates.Count; i++)
            {
                SuspendedBodyState state = suspendedBodyStates[i];
                if (state.body == null)
                    continue;

                state.body.simulated = state.simulated;
                if (state.simulated)
                {
                    state.body.linearVelocity = state.linearVelocity;
                    state.body.angularVelocity = state.angularVelocity;
                }
            }

            Physics2D.SyncTransforms();
            viewTransitionSuspended = false;
            for (int i = 0; i < viewTransitionParticipants.Count; i++)
            {
                IBartendingViewTransitionParticipant participant =
                    viewTransitionParticipants[i];
                if (participant is MonoBehaviour behaviour && behaviour != null)
                    participant.ResumeAfterViewTransition();
            }

            suspendedBodyStates.Clear();
            viewTransitionParticipants.Clear();
            resumeViewTransitionRoutine = null;
        }

        private void ClearViewTransitionState()
        {
            if (resumeViewTransitionRoutine != null)
            {
                StopCoroutine(resumeViewTransitionRoutine);
                resumeViewTransitionRoutine = null;
            }

            sessionViewport?.SetInputSuspended(false);
            suspendedBodyStates.Clear();
            viewTransitionParticipants.Clear();
            viewTransitionSuspended = false;
        }

        private IEnumerator CaptureTargetTracker(GlassController glass)
        {
            yield return null;
            if (glass != null)
                RegisterServingGlass(glass);
            readyRoutine = null;
            SessionReady?.Invoke();
        }

        private void HandleGlassServeRequested(GlassController glass)
        {
            VesselLiquidTracker tracker = glass != null ? glass.LiquidTracker : null;
            if (tracker == null || !deployedServingGlasses.Contains(glass))
                return;

            ServeRequested?.Invoke(tracker);
        }

        private void HandleServingGlassHeldStateChanged(GlassController glass, bool isHeld)
        {
            if (!isHeld || glass == null || !deployedServingGlasses.Contains(glass))
                return;

            // This reference only controls the serving-area overlay while a glass
            // is held. Evaluation always uses the glass that raises ServeRequested.
            builtSession?.InteractionOverlay?.SetServingGlass(glass);
        }

        public void PrepareCabinetInventory(ToolCabinetCatalog catalog)
        {
            if (catalog == null || sessionWorld == null || settings == null)
                return;

            if (cabinetSlotRoot == null)
            {
                GameObject slots = new GameObject("__ToolCabinetSlots");
                slots.transform.SetParent(sessionWorld, false);
                // LiquidPool recycles particles outside x +/-14, y -10..12.
                // Keep cabinet contents inside that contract while presentation is hidden.
                slots.transform.localPosition = new Vector3(0f, 7f, 0f);
                cabinetSlotRoot = slots.transform;

                GameObject pickup = new GameObject("__ToolCabinetPickupAnchor");
                pickup.transform.SetParent(sessionWorld, false);
                cabinetPickupAnchor = pickup.transform;
            }

            int slotIndex = 0;
            ToolDef[] tools = catalog.tools ?? Array.Empty<ToolDef>();
            for (int i = 0; i < tools.Length; i++)
            {
                ToolDef definition = tools[i];
                if (definition == null
                    || string.IsNullOrWhiteSpace(definition.StableId)
                    || cabinetInstances.ContainsKey(definition.StableId))
                {
                    continue;
                }

                IBartendingItem item = ToolCabinetWorldFactory.CreateTool(
                    definition,
                    sessionWorld,
                    settings,
                    sessionRenderLayer,
                    sessionItemScale,
                    cabinetShiftState,
                    out GameObject instance);
                if (instance == null)
                {
                    Debug.LogWarning("The cabinet tool could not be prepared: "
                        + definition.StableId);
                    continue;
                }

                cabinetInstances[definition.StableId] = instance;
                BartendingSessionBuilder.ConfigureRotatingMovement(item, settings);
                if (definition.kind == ToolKind.IceBucket)
                    cabinetIceBucket = instance.GetComponent<IceBinController>();

                SlotController cabinetSlot = CreateCabinetSlot(
                    definition.StableId,
                    slotIndex++);
                ToolCabinetRuntimeTag tag = ConfigureCabinetTag(
                    instance,
                    definition,
                    cabinetSlot);
                // Bind before Store: Occupy is raised while the real world renderers
                // are still enabled, allowing the cabinet view to cache their exact
                // projected size instead of inventing a separate icon scale.
                toolCabinet?.BindCabinetSlot(definition.StableId, cabinetSlot);
                if (!tag.Store(item))
                    Debug.LogWarning("The cabinet tool slot could not store " + definition.StableId);
            }

            GlassDef[] glasses = catalog.glasses ?? Array.Empty<GlassDef>();
            for (int i = 0; i < glasses.Length; i++)
            {
                GlassDef definition = glasses[i];
                if (definition == null
                    || string.IsNullOrWhiteSpace(definition.StableId)
                    || cabinetInstances.ContainsKey(definition.StableId))
                {
                    continue;
                }

                GlassController glass = ToolCabinetWorldFactory.CreateGlass(
                    definition,
                    sessionWorld,
                    settings,
                    sessionRenderLayer,
                    sessionItemScale,
                    out GameObject instance);
                if (glass == null || instance == null)
                {
                    if (instance != null)
                        Destroy(instance);
                    Debug.LogWarning("The cabinet glass could not be prepared: "
                        + definition.StableId);
                    continue;
                }

                SlotController cabinetSlot = CreateCabinetSlot(
                    definition.StableId,
                    slotIndex++);
                ToolCabinetRuntimeTag tag = ConfigureCabinetTag(
                    instance,
                    definition,
                    cabinetSlot);
                cabinetInstances[definition.StableId] = instance;
                BartendingSessionBuilder.ConfigureRotatingMovement(glass, settings);
                toolCabinet?.BindCabinetSlot(definition.StableId, cabinetSlot);
                if (!tag.Store(glass))
                    Debug.LogWarning("The cabinet glass slot could not store " + definition.StableId);
            }
        }

        private ToolCabinetRuntimeTag ConfigureCabinetTag(
            GameObject instance,
            ToolDef definition,
            SlotController cabinetSlot)
        {
            ToolCabinetRuntimeTag tag = instance.GetComponent<ToolCabinetRuntimeTag>();
            if (tag == null)
                tag = instance.AddComponent<ToolCabinetRuntimeTag>();
            tag.Configure(definition);
            tag.BindCabinetSlot(cabinetSlot);
            return tag;
        }

        private ToolCabinetRuntimeTag ConfigureCabinetTag(
            GameObject instance,
            GlassDef definition,
            SlotController cabinetSlot)
        {
            ToolCabinetRuntimeTag tag = instance.GetComponent<ToolCabinetRuntimeTag>();
            if (tag == null)
                tag = instance.AddComponent<ToolCabinetRuntimeTag>();
            tag.Configure(definition);
            tag.BindCabinetSlot(cabinetSlot);
            return tag;
        }

        private SlotController CreateCabinetSlot(string id, int index)
        {
            GameObject slotObject = new GameObject("CabinetSlot_" + id);
            slotObject.transform.SetParent(cabinetSlotRoot, false);
            slotObject.transform.localPosition = new Vector3(-9f + index * 3f, 0f, 0f);
            int slotLayer = LayerMask.NameToLayer("Slot");
            slotObject.layer = slotLayer >= 0 ? slotLayer : sessionRenderLayer;

            BoxCollider2D collider = slotObject.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(2f, 2f);
            collider.isTrigger = true;
            SpriteRenderer renderer = slotObject.AddComponent<SpriteRenderer>();
            renderer.enabled = false;
            return slotObject.AddComponent<SlotController>();
        }

        public bool TryPickUpCabinetItem(
            string definitionId,
            Vector2 screenPosition,
            out string failure)
        {
            failure = string.Empty;
            if (!CanUseToolCabinet(out failure))
                return false;

            if (HasHeldBartendingItem())
            {
                failure = "Put down the item already being held before taking another one.";
                return false;
            }

            string id = definitionId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id)
                || !cabinetInstances.TryGetValue(id, out GameObject instance)
                || instance == null)
            {
                failure = "The cabinet slot has no prepared item.";
                return false;
            }

            ToolCabinetRuntimeTag tag = instance.GetComponent<ToolCabinetRuntimeTag>();
            IBartendingItem item = instance.GetComponent<IBartendingItem>();
            if (tag == null
                || item == null
                || !tag.IsInCabinet
                || tag.CabinetSlot == null
                || !ReferenceEquals(tag.CabinetSlot.OccupiedItem, item))
            {
                failure = "The cabinet slot is empty.";
                return false;
            }

            if (item is not Component itemComponent
                || !BartendingSelection.CanAcquire(itemComponent))
            {
                failure = "Put down the item already being held before taking another one.";
                return false;
            }

            if (!TryGetCabinetPickupWorldPosition(screenPosition, out Vector3 pickupWorld))
            {
                failure = "The bartending counter is not ready to receive this item.";
                return false;
            }

            // Cabinet slots store each item by its visible bottom, while a cabinet
            // click is a grab point. Preserve the exact per-item slot offset so the
            // existing SnapToSlot contract places the item's root on the pointer
            // instead of one vessel height above it.
            Vector3 storedSlotOffset =
                item.GameObject.transform.position - tag.CabinetSlot.transform.position;
            storedSlotOffset.z = 0f;

            if (!tag.TakeFromCabinet(item))
            {
                failure = "The cabinet slot could not release its item.";
                return false;
            }

            Vector3 visualCenterOffset = GetVisualCenterOffset(item.GameObject);
            Vector3 targetRootPosition = pickupWorld - visualCenterOffset;
            cabinetPickupAnchor.position = targetRootPosition - storedSlotOffset;
            item.SnapToSlot(cabinetPickupAnchor, null);
            if (item is IPointerAnchoredPickup pointerAnchored)
                pointerAnchored.OnPickedUpAt(pickupWorld);
            else
                item.OnPickedUp();

            if (item is GlassController glass)
                RegisterServingGlass(glass);
            toolCabinet?.NotifyAvailability(id, false);
            return true;
        }

        private static Vector3 GetVisualCenterOffset(GameObject itemObject)
        {
            if (itemObject == null)
                return Vector3.zero;

            SpriteRenderer[] renderers =
                itemObject.GetComponentsInChildren<SpriteRenderer>(true);
            Bounds combined = default;
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null
                    || !renderer.enabled
                    || renderer.sprite == null
                    || !renderer.gameObject.activeInHierarchy
                    || renderer.GetComponentInParent<LiquidParticleData>() != null
                    || renderer.GetComponentInParent<IceCubeController>() != null)
                {
                    continue;
                }

                if (!found)
                {
                    combined = renderer.bounds;
                    found = true;
                }
                else
                    combined.Encapsulate(renderer.bounds);
            }

            if (!found)
                return Vector3.zero;

            Vector3 offset = combined.center - itemObject.transform.position;
            offset.z = 0f;
            return offset;
        }

        private bool HasHeldBartendingItem()
        {
            if (sessionWorld == null)
                return false;

            if (selectionCoordinator != null && selectionCoordinator.HasSelection)
                return true;

            MonoBehaviour[] behaviours =
                sessionWorld.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IBartendingItem item && item.IsPickedUp)
                    return true;
            }
            return false;
        }

        public bool TryReturnCabinetItem(
            IBartendingItem item,
            string targetDefinitionId,
            out string failure)
        {
            failure = string.Empty;
            if (item?.GameObject == null)
                return false;

            ToolCabinetRuntimeTag tag = item.GameObject.GetComponent<ToolCabinetRuntimeTag>();
            if (tag == null || string.IsNullOrWhiteSpace(tag.DefinitionId))
            {
                failure = "Only cabinet items can be placed in cabinet slots.";
                return false;
            }

            if (!string.Equals(
                    tag.DefinitionId,
                    targetDefinitionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                failure = "Return the item to its own cabinet slot.";
                return false;
            }

            CobblerShakerPresentation shakerPresentation =
                item.GameObject.GetComponent<CobblerShakerPresentation>();
            if (shakerPresentation != null && !shakerPresentation.IsFullyAssembled)
            {
                failure = "Reassemble the shaker before returning it to the cabinet.";
                return false;
            }

            if (!tag.Store(item))
            {
                failure = "That cabinet slot is already occupied.";
                return false;
            }

            if (item is GlassController glass)
                UnregisterServingGlass(glass);
            toolCabinet?.NotifyAvailability(tag.DefinitionId, true);
            return true;
        }

        private bool TryGetCabinetPickupWorldPosition(
            Vector2 cabinetScreenPosition,
            out Vector3 worldPosition)
        {
            return BartendingViewport.TryGetPointerWorldPosition(
                Camera.main,
                cabinetScreenPosition,
                out worldPosition);
        }

        public bool TryRefillIceBucket(out string failure)
        {
            failure = string.Empty;
            if (!CanUseToolCabinet(out failure))
                return false;
            if (cabinetIceBucket == null)
            {
                failure = "The cabinet ice bucket is unavailable.";
                return false;
            }
            if (!cabinetIceBucket.Refill())
            {
                failure = "The ice bucket cannot be refilled.";
                return false;
            }
            ToolCabinetRuntimeTag bucketTag =
                cabinetIceBucket.GetComponent<ToolCabinetRuntimeTag>();
            if (bucketTag != null)
                toolCabinet?.NotifyAvailability(bucketTag.DefinitionId, true);
            return true;
        }

        private bool CanUseToolCabinet(out string failure)
        {
            failure = string.Empty;
            if (modeManager == null
                || modeManager.CurrentMode != GameMode.CraftingMode
                || !IsSessionReady
                || sessionWorld == null)
            {
                failure = "Cabinet items are available only during bartending.";
                return false;
            }
            return true;
        }

        private void RegisterServingGlass(GlassController glass)
        {
            if (glass == null || !deployedServingGlasses.Add(glass))
                return;

            glass.ConfigureServeGesture(builtSession.InteractionOverlay);
            glass.ServeRequested -= HandleGlassServeRequested;
            glass.ServeRequested += HandleGlassServeRequested;
            glass.HeldStateChanged -= HandleServingGlassHeldStateChanged;
            glass.HeldStateChanged += HandleServingGlassHeldStateChanged;
            if (glass.IsPickedUp)
                builtSession.InteractionOverlay?.SetServingGlass(glass);
        }

        private void UnregisterServingGlass(GlassController glass)
        {
            if (glass == null || !deployedServingGlasses.Remove(glass))
                return;
            glass.ServeRequested -= HandleGlassServeRequested;
            glass.HeldStateChanged -= HandleServingGlassHeldStateChanged;
            glass.ConfigureServeGesture(null);
            builtSession?.InteractionOverlay?.SetServingGlass(null);
        }

        private void UnregisterAllServingGlasses()
        {
            if (deployedServingGlasses.Count == 0)
                return;

            List<GlassController> glasses =
                new List<GlassController>(deployedServingGlasses);
            for (int i = 0; i < glasses.Count; i++)
                UnregisterServingGlass(glasses[i]);
        }

        public bool TryPlaceBottleFromShelf(LiquorBottleDef shelfDefinition, out string failure)
        {
            failure = string.Empty;
            if (modeManager == null || modeManager.CurrentMode != GameMode.CraftingMode
                || sessionRoot == null || sessionWorld == null)
            {
                failure = "칵테일 제작 중에만 술병을 꺼낼 수 있습니다.";
                return false;
            }

            if (shelfDefinition == null || string.IsNullOrWhiteSpace(shelfDefinition.id))
            {
                failure = "술장 병 데이터가 비어 있습니다.";
                return false;
            }

            string inventoryId = shelfDefinition.InventoryId;
            if (FindSelectedDefinition(inventoryId) != null)
            {
                failure = $"{shelfDefinition.displayName} 병은 이미 테이블에 있습니다.";
                return false;
            }

            ItemDef item = ResolveShelfItem(shelfDefinition);
            if (item == null || item.type != ItemType.Bottle)
            {
                failure = $"{shelfDefinition.displayName}에 연결된 제작용 재료가 없습니다. "
                    + $"(병 ID: {shelfDefinition.id}, 재고 ID: {inventoryId})";
                return false;
            }

            GameProgress progress = GameProgress.Instance;
            float inventoryAmount = progress != null
                ? progress.EnsureBottleAmount(inventoryId, shelfDefinition.DefaultAmount)
                : shelfDefinition.DefaultAmount;
            if (inventoryAmount <= 0f)
            {
                failure = $"{shelfDefinition.displayName} 재고가 없습니다.";
                return false;
            }

            SlotController targetSlot = FindRightmostFreeSlot();
            if (targetSlot == null)
            {
                failure = "테이블에 빈 슬롯이 없습니다.";
                return false;
            }

            BottleController bottle = CreateBottle(item, shelfDefinition, shelfDefinition.DefaultAmount);
            if (bottle == null)
            {
                failure = "술병 오브젝트를 만들지 못했습니다.";
                return false;
            }

            selectedBottleDefinitions.Add(shelfDefinition);
            targetSlot.Occupy(bottle);
            bottle.SnapToSlot(targetSlot.transform, targetSlot);
            return true;
        }

        public bool TryReturnHeldBottleToShelf(out string failure)
        {
            failure = string.Empty;
            BottleController heldBottle = null;
            for (int i = sessionBottles.Count - 1; i >= 0; i--)
            {
                BottleController candidate = sessionBottles[i];
                if (candidate != null && candidate.IsPickedUp)
                {
                    heldBottle = candidate;
                    break;
                }
            }

            if (heldBottle == null)
                return false;

            if (modeManager == null
                || modeManager.CurrentMode != GameMode.CraftingMode
                || sessionWorld == null)
            {
                failure = "칵테일 제작 중에만 술병을 반환할 수 있습니다.";
                return false;
            }

            ItemDef item = heldBottle.BottleData;
            LiquorBottleDef shelfDefinition = item != null
                ? FindSelectedDefinition(item.id)
                : null;
            if (shelfDefinition == null)
            {
                failure = "들고 있는 재료 병에 연결된 술장 데이터를 찾지 못했습니다.";
                return false;
            }

            heldBottle.CapacityChanged -= HandleBottleCapacityChanged;
            sessionBottles.Remove(heldBottle);
            bottleReserveAmounts.Remove(heldBottle);
            selectedBottleDefinitions.Remove(shelfDefinition);
            heldBottle.PrepareForShelfReturn();
            Destroy(heldBottle.gameObject);
            return true;
        }

        private List<BottleController> CreateSelectedBottles()
        {
            List<BottleController> bottles = new List<BottleController>();
            for (int i = 0; i < selectedBottleDefinitions.Count; i++)
            {
                LiquorBottleDef shelfDefinition = selectedBottleDefinitions[i];
                ItemDef item = ResolveShelfItem(shelfDefinition);
                if (shelfDefinition == null || item == null || item.type != ItemType.Bottle)
                {
                    continue;
                }

                BottleController bottle = CreateBottle(item, shelfDefinition, shelfDefinition.DefaultAmount);
                if (bottle != null)
                    bottles.Add(bottle);
            }

            return bottles;
        }

        private BottleController CreateBottle(
            ItemDef item,
            LiquorBottleDef shelfDefinition,
            float defaultInventoryAmount)
        {
            if (item == null || sessionWorld == null)
                return null;

            IBartendingItem bottleItem = BartendingSessionBuilder.CreateItem(
                settings.bottlePrefab,
                sessionWorld,
                string.IsNullOrWhiteSpace(item.displayName) ? item.id : item.displayName,
                settings.bottlePosition,
                sessionRenderLayer,
                sessionItemScale,
                settings);
            BartendingSessionBuilder.ConfigureRotatingMovement(bottleItem, settings);
            if (bottleItem is not BottleController bottle)
            {
                if (bottleItem != null)
                    Destroy(bottleItem.GameObject);
                return null;
            }

            Sprite barSprite = shelfDefinition != null
                ? shelfDefinition.GetBarSprite(item.icon)
                : item.icon;
            bottle.Init(item, barSprite);
            BartendingNativeSpriteSizer.TryMatchRootToSprite(
                bottle.transform,
                BartendingNativeSpriteSizer.FindReferenceRenderer(
                    bottle.gameObject));
            bottle.transform.localScale *= 0.7f;
            RegisterBottle(bottle, defaultInventoryAmount);
            return bottle;
        }

        private LiquorBottleDef FindSelectedDefinition(string itemId)
        {
            for (int i = 0; i < selectedBottleDefinitions.Count; i++)
            {
                LiquorBottleDef definition = selectedBottleDefinitions[i];
                if (definition != null
                    && string.Equals(definition.InventoryId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    return definition;
                }
            }

            return null;
        }

        private ItemDef ResolveShelfItem(LiquorBottleDef shelfDefinition)
        {
            if (shelfDefinition == null)
                return null;
            if (shelfDefinition.item != null)
                return shelfDefinition.item;

            return itemCatalog != null
                && itemCatalog.TryGet(shelfDefinition.InventoryId, out ItemDef item)
                    ? item
                    : null;
        }

        private SlotController FindRightmostFreeSlot()
        {
            for (int i = sessionSlots.Count - 1; i >= 0; i--)
            {
                if (sessionSlots[i] != null && !sessionSlots[i].IsOccupied)
                    return sessionSlots[i];
            }

            return null;
        }

        private void RegisterBottle(BottleController bottle, float defaultInventoryAmount)
        {
            if (bottle == null || sessionBottles.Contains(bottle))
                return;

            ItemDef item = bottle.BottleData;
            if (item != null && !string.IsNullOrWhiteSpace(item.id) && GameProgress.Instance != null)
            {
                float totalAmount = Mathf.Max(
                    0f,
                    GameProgress.Instance.GetBottleAmount(item.id, defaultInventoryAmount));
                float bottleCapacity = Mathf.Max(1f, item.capacityMl);
                float activeBottleAmount = totalAmount % bottleCapacity;
                if (totalAmount > 0f && activeBottleAmount <= Mathf.Epsilon)
                    activeBottleAmount = Mathf.Min(bottleCapacity, totalAmount);
                bottleReserveAmounts[bottle] = Mathf.Max(0f, totalAmount - activeBottleAmount);
                bottle.SetCurrentCapacity(activeBottleAmount);
            }
            else
            {
                bottleReserveAmounts[bottle] = 0f;
            }

            bottle.CapacityChanged += HandleBottleCapacityChanged;
            sessionBottles.Add(bottle);
        }

        private void HandleBottleCapacityChanged(BottleController bottle, float amount)
        {
            ItemDef item = bottle != null ? bottle.BottleData : null;
            if (item == null || string.IsNullOrWhiteSpace(item.id) || GameProgress.Instance == null)
                return;

            bottleReserveAmounts.TryGetValue(bottle, out float reserveAmount);
            float activeAmount = Mathf.Max(0f, amount);
            if (activeAmount <= Mathf.Epsilon && reserveAmount > Mathf.Epsilon)
            {
                float refillAmount = Mathf.Min(Mathf.Max(0f, item.capacityMl), reserveAmount);
                if (refillAmount > Mathf.Epsilon)
                {
                    reserveAmount = Mathf.Max(0f, reserveAmount - refillAmount);
                    bottleReserveAmounts[bottle] = reserveAmount;
                    bottle.SetCurrentCapacity(refillAmount);
                    activeAmount = refillAmount;
                }
            }

            GameProgress.Instance.SetBottleAmount(item.id, reserveAmount + activeAmount);
        }

        private Camera CreateWorldCamera(Transform parent, BusinessBartendingSettings settings, int renderLayer)
        {
            GameObject cameraObject = new GameObject("BartendingCamera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.orthographic = true;
            camera.orthographicSize = settings.cameraOrthographicSize;
            camera.cullingMask = 1 << renderLayer;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.depth = -10f;
            return camera;
        }

        private BartendingViewport CreateViewport(
            RectTransform counter,
            Camera camera,
            BusinessBartendingSettings settings)
        {
            GameObject viewObject = new GameObject(
                "BartendingViewport",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            RectTransform viewRect = (RectTransform)viewObject.transform;
            Transform viewportParent = counter.parent != null ? counter.parent : counter;
            viewRect.SetParent(viewportParent, false);
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.pivot = new Vector2(0.5f, 0.5f);
            viewRect.sizeDelta = Vector2.zero;
            viewRect.anchoredPosition = Vector2.zero;

            if (counter.parent != null)
            {
                viewRect.SetSiblingIndex(counter.GetSiblingIndex() + 1);
            }
            else
            {
                viewRect.SetAsFirstSibling();
            }

            RawImage image = viewObject.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.color = Color.white;

            BartendingViewport viewport = viewObject.AddComponent<BartendingViewport>();
            viewport.Initialize(camera, settings.renderTextureSize);
            return viewport;
        }

        private static void GetSlotLayout(
            RectTransform tableSlots,
            BartendingViewport viewport,
            BusinessBartendingSettings settings,
            out List<Vector3> positions,
            out float itemScale)
        {
            positions = settings.slotPositions != null
                ? new List<Vector3>(settings.slotPositions)
                : new List<Vector3>();
            itemScale = 1f;

            if (tableSlots == null || viewport == null)
            {
                return;
            }

            List<MappedSlot> mappedSlots = new List<MappedSlot>();
            foreach (UIDropSlot dropSlot in tableSlots.GetComponentsInChildren<UIDropSlot>(true))
            {
                if (dropSlot.transform is RectTransform rect &&
                    viewport.TryMapRectToWorld(rect, out Vector3 center, out Vector2 size))
                {
                    mappedSlots.Add(new MappedSlot(center, size.x));
                }
            }

            if (mappedSlots.Count == 0)
            {
                return;
            }

            mappedSlots.Sort((left, right) => left.Position.x.CompareTo(right.Position.x));
            positions.Clear();
            float accumulatedWidth = 0f;
            foreach (MappedSlot mappedSlot in mappedSlots)
            {
                positions.Add(mappedSlot.Position);
                accumulatedWidth += mappedSlot.Width;
            }

            BoxCollider2D referenceCollider = settings.slotPrefab != null
                ? settings.slotPrefab.GetComponent<BoxCollider2D>()
                : null;
            float referenceWidth = referenceCollider != null ? referenceCollider.size.x : 1f;
            if (referenceWidth > Mathf.Epsilon)
            {
                float averageWidth = accumulatedWidth / mappedSlots.Count;
                itemScale = Mathf.Clamp(averageWidth / referenceWidth, 0.1f, 2f);
            }
        }

        private static RectTransform CreateSessionSlotLayout(
            RectTransform template,
            RectTransform viewport,
            RectTransform counter)
        {
            if (template == null)
                return null;

            Transform targetParent = viewport != null && viewport.parent != null
                ? viewport.parent
                : template.parent;
            RectTransform layout = Instantiate(template, targetParent, false);
            layout.name = "BartendingSessionSlots";
            layout.anchorMin = new Vector2(0.5f, 0.5f);
            layout.anchorMax = new Vector2(0.5f, 0.5f);
            layout.pivot = new Vector2(0.5f, 0.5f);
            layout.localRotation = Quaternion.identity;
            layout.localScale = Vector3.one;
            layout.anchoredPosition = Vector2.zero;
            if (viewport != null)
                layout.SetSiblingIndex(viewport.GetSiblingIndex());

            foreach (UIDropSlot dropSlot in layout.GetComponentsInChildren<UIDropSlot>(true))
                dropSlot.enabled = false;
            foreach (Graphic graphic in layout.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
                graphic.enabled = false;
            }

            layout.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(layout);
            Canvas.ForceUpdateCanvases();
            AlignSlotLayoutToVisibleTable(layout, counter, viewport);
            return layout;
        }

        private static void AlignSlotLayoutToVisibleTable(
            RectTransform layout,
            RectTransform content,
            RectTransform viewport)
        {
            if (layout == null || content == null || viewport == null)
                return;

            RectTransform visibleViewport = GetRootCanvasRect(viewport);
            if (visibleViewport == null)
                visibleViewport = viewport;

            Rect contentScreenRect = GetScreenRect(content);
            Rect viewportScreenRect = GetScreenRect(visibleViewport);
            float xMin = Mathf.Max(contentScreenRect.xMin, viewportScreenRect.xMin);
            float xMax = Mathf.Min(contentScreenRect.xMax, viewportScreenRect.xMax);
            float yMin = Mathf.Max(contentScreenRect.yMin, viewportScreenRect.yMin);
            float yMax = Mathf.Min(contentScreenRect.yMax, viewportScreenRect.yMax);
            if (xMax <= xMin || yMax <= yMin
                || !TryGetSlotLayoutScreenCenter(layout, out Vector2 slotScreenCenter))
                return;

            Vector2 targetScreenCenter = new Vector2(
                (xMin + xMax) * 0.5f,
                (yMin + yMax) * 0.5f);
            Camera layoutCamera = GetCanvasCamera(layout);
            Vector2 layoutScreenPosition = RectTransformUtility.WorldToScreenPoint(
                layoutCamera,
                layout.position);
            Vector2 alignedScreenPosition =
                layoutScreenPosition + targetScreenCenter - slotScreenCenter;

            RectTransform parent = layout.parent as RectTransform;
            if (parent != null
                && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parent,
                    alignedScreenPosition,
                    GetCanvasCamera(parent),
                    out Vector3 alignedWorldPosition))
            {
                layout.position = alignedWorldPosition;
            }
        }

        private static bool TryGetSlotLayoutScreenCenter(
            RectTransform layout,
            out Vector2 screenCenter)
        {
            UIDropSlot[] dropSlots = layout.GetComponentsInChildren<UIDropSlot>(true);
            bool hasBounds = false;
            Rect bounds = default;
            foreach (UIDropSlot dropSlot in dropSlots)
            {
                if (!(dropSlot.transform is RectTransform slotRect))
                    continue;

                Rect slotScreenRect = GetScreenRect(slotRect);
                if (!hasBounds)
                {
                    bounds = slotScreenRect;
                    hasBounds = true;
                }
                else
                {
                    bounds = Rect.MinMaxRect(
                        Mathf.Min(bounds.xMin, slotScreenRect.xMin),
                        Mathf.Min(bounds.yMin, slotScreenRect.yMin),
                        Mathf.Max(bounds.xMax, slotScreenRect.xMax),
                        Mathf.Max(bounds.yMax, slotScreenRect.yMax));
                }
            }

            screenCenter = hasBounds ? bounds.center : default;
            return hasBounds;
        }

        private static RectTransform GetRootCanvasRect(RectTransform rectTransform)
        {
            Canvas canvas = rectTransform != null
                ? rectTransform.GetComponentInParent<Canvas>()
                : null;
            return canvas != null && canvas.rootCanvas != null
                ? canvas.rootCanvas.transform as RectTransform
                : null;
        }

        private static Rect GetScreenRect(RectTransform rectTransform)
        {
            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Camera canvasCamera = GetCanvasCamera(rectTransform);
            Vector2 first = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[0]);
            float xMin = first.x;
            float xMax = first.x;
            float yMin = first.y;
            float yMax = first.y;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[i]);
                xMin = Mathf.Min(xMin, point.x);
                xMax = Mathf.Max(xMax, point.x);
                yMin = Mathf.Min(yMin, point.y);
                yMax = Mathf.Max(yMax, point.y);
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static Camera GetCanvasCamera(RectTransform rectTransform)
        {
            Canvas canvas = rectTransform != null
                ? rectTransform.GetComponentInParent<Canvas>()
                : null;
            return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;
        }

        private static List<SlotController> CreateSlots(
            Transform parent,
            BusinessBartendingSettings settings,
            List<Vector3> positions,
            float itemScale)
        {
            List<SlotController> slots = new List<SlotController>();
            if (settings.slotPrefab == null || positions == null)
            {
                return slots;
            }

            int slotLayer = LayerMask.NameToLayer("Slot");
            if (slotLayer < 0)
            {
                slotLayer = 0;
            }

            for (int i = 0; i < positions.Count; i++)
            {
                GameObject slot = Instantiate(settings.slotPrefab, parent);
                slot.name = "BartendingSlot_" + i;
                slot.transform.localPosition = positions[i];
                slot.transform.localRotation = Quaternion.identity;
                slot.transform.localScale = Vector3.one * itemScale;
                SetLayerRecursively(slot, slotLayer);

                SlotController slotController = slot.GetComponent<SlotController>();
                if (slotController != null)
                {
                    slots.Add(slotController);
                }
            }

            return slots;
        }

        private IBartendingItem CreateItem(
            GameObject prefab,
            Transform parent,
            string instanceName,
            Vector3 position,
            int renderLayer,
            float itemScale)
        {
            if (prefab == null)
            {
                Debug.LogWarning(instanceName + " 프리팹이 영업 제조 설정에 할당되지 않았습니다.");
                return null;
            }

            GameObject item = Instantiate(prefab, parent);
            item.name = instanceName;
            item.transform.localPosition = position;
            item.transform.localRotation = Quaternion.identity;
            item.transform.localScale = Vector3.one * itemScale;
            SetLayerRecursively(item, renderLayer);
            foreach (VesselLiquidTracker tracker
                     in item.GetComponentsInChildren<VesselLiquidTracker>(true))
            {
                tracker.ConfigureRuntimeDebugLabel(
                    settings != null && settings.showVesselDebugLabels,
                    settings != null ? settings.vesselDebugRefreshInterval : 0.2f);
            }
            return item.GetComponent<IBartendingItem>();
        }

        private IEnumerator SnapStartingItems(
            List<IBartendingItem> tools,
            List<BottleController> bottles)
        {
            yield return null;

            if (tools != null)
            {
                for (int i = 0; i < tools.Count && i < sessionSlots.Count; i++)
                    SnapToStartingSlot(tools[i], sessionSlots, i);
            }

            if (bottles != null)
            {
                for (int i = 0; i < bottles.Count; i++)
                {
                    SlotController targetSlot = FindRightmostFreeSlot();
                    if (targetSlot == null || bottles[i] == null)
                        continue;

                    targetSlot.Occupy(bottles[i]);
                    bottles[i].SnapToSlot(targetSlot.transform, targetSlot);
                }
            }

            sessionViewport?.SetOutputVisible(true);

            snapRoutine = null;
        }

        private static void SnapToStartingSlot(IBartendingItem item, List<SlotController> slots, int index)
        {
            if (item == null || index < 0 || index >= slots.Count)
            {
                return;
            }

            SlotController slot = slots[index];
            slot.Occupy(item);
            item.SnapToSlot(slot.transform, slot);
        }

        private static LiquidPool CreateLiquidPool(
            Transform parent,
            BusinessBartendingSettings settings,
            int renderLayer,
            float itemScale)
        {
            if (settings.liquidParticlePrefab == null || LiquidPool.Instance != null)
            {
                return null;
            }

            GameObject poolObject = new GameObject("LiquidPoolManager");
            poolObject.SetActive(false);
            poolObject.transform.SetParent(parent, false);
            poolObject.transform.localScale = Vector3.one * itemScale;

            LiquidPool pool = poolObject.AddComponent<LiquidPool>();
            pool.particlePrefab = settings.liquidParticlePrefab;
            pool.poolSize = settings.liquidPoolSize;

            poolObject.SetActive(true);
            SetLayerRecursively(poolObject, renderLayer);
            return pool;
        }

        private readonly struct MappedSlot
        {
            public MappedSlot(Vector3 position, float width)
            {
                Position = position;
                Width = width;
            }

            public Vector3 Position { get; }
            public float Width { get; }
        }

        private void CaptureSlotLayoutTemplate(Scene scene)
        {
            slotLayoutTemplate = FindNamedRectTransform(scene, "TableSlots");
            if (slotLayoutTemplate == null)
            {
                Debug.LogWarning(
                    "영업 제조 화면에서 TableSlots를 찾을 수 없습니다. "
                    + "설정에 저장된 기본 위치를 사용합니다.");
                return;
            }

            slotLayoutTemplateWasActive = slotLayoutTemplate.gameObject.activeSelf;
            slotLayoutTemplate.gameObject.SetActive(false);
        }

        private void HideCanvasBartendingItems(Scene scene)
        {
            foreach (Canvas canvas in FindAllInScene<Canvas>(scene))
            {
                HideItems(canvas.GetComponentsInChildren<BeakerController>(true));
                HideItems(canvas.GetComponentsInChildren<GlassController>(true));
                HideItems(canvas.GetComponentsInChildren<BottleController>(true));
            }
        }

        private void HideItems<T>(T[] items) where T : Behaviour
        {
            foreach (T item in items)
            {
                if (item.gameObject.activeSelf)
                {
                    hiddenCanvasItems.Add(item.gameObject);
                    item.gameObject.SetActive(false);
                }
            }
        }

        private void OnDestroy()
        {
            if (frontCameraRig != null)
            {
                frontCameraRig.MoveStarted -= HandleFrontWorldMoveStarted;
                frontCameraRig.MoveUpdated -= HandleFrontWorldMoveUpdated;
                frontCameraRig.MoveCompleted -= HandleFrontWorldMoveCompleted;
                frontCameraRig = null;
            }

            if (modeManager != null)
            {
                modeManager.OnModeChanged -= HandleModeChanged;
            }

            DestroySession(clearBottleSelections: true);
            RestoreHiddenCanvasItems();
            RestoreSlotLayoutTemplate();
        }

        private void RestoreSessionOverrides()
        {
            if (sourceCamera != null)
            {
                sourceCamera.cullingMask = sourceCameraMask;
                sourceCamera = null;
            }
        }

        private void RestoreHiddenCanvasItems()
        {
            foreach (GameObject item in hiddenCanvasItems)
            {
                if (item != null)
                {
                    item.SetActive(true);
                }
            }

            hiddenCanvasItems.Clear();
        }

        private void RestoreSlotLayoutTemplate()
        {
            if (slotLayoutTemplate != null)
                slotLayoutTemplate.gameObject.SetActive(slotLayoutTemplateWasActive);
            slotLayoutTemplate = null;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
            }
        }

        private static RectTransform FindNamedRectTransform(Scene scene, string objectName)
        {
            foreach (Transform transformInScene in FindAllInScene<Transform>(scene))
            {
                if (transformInScene.name == objectName && transformInScene is RectTransform rectTransform)
                {
                    return rectTransform;
                }
            }

            return null;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static IEnumerable<T> FindAllInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T component in root.GetComponentsInChildren<T>(true))
                {
                    yield return component;
                }
            }
        }
    }
}
