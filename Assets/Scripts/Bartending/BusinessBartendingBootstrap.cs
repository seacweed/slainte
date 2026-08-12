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
        private const string SceneName = "BusinessScene";
        private const string SettingsResourcePath = "Bartending/BusinessBartendingSettings";

        private readonly List<GameObject> hiddenCanvasItems = new List<GameObject>();
        private readonly List<BottleController> sessionBottles = new List<BottleController>();
        private readonly List<LiquorBottleDef> selectedBottleDefinitions = new List<LiquorBottleDef>();
        private readonly List<SlotController> sessionSlots = new List<SlotController>();
        private readonly Dictionary<BottleController, float> bottleReserveAmounts =
            new Dictionary<BottleController, float>();
        private Scene targetScene;
        private BusinessBartendingSettings settings;
        private ItemDefCatalog itemCatalog;
        private GameModeManager modeManager;
        private GameObject sessionRoot;
        private Transform sessionWorld;
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

        public VesselLiquidTracker CurrentTargetTracker { get; private set; }
        public int SessionBottleCount => sessionBottles.Count;
        public event Action<VesselLiquidTracker> SessionReady;
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

            CaptureSlotLayoutTemplate(scene);
            HideCanvasBartendingItems(scene);
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
                BartendingSessionBuildMode.Runtime);
            if (builtSession == null)
            {
                Debug.LogError("영업 제조 세션을 생성하지 못했습니다.");
                return;
            }

            sessionRenderLayer = builtSession.RenderLayer;
            sessionRoot = builtSession.Root;
            sessionWorld = builtSession.World;
            sessionViewport = builtSession.Viewport;
            sessionSlotLayout = builtSession.SlotLayout;
            sessionLiquidPool = builtSession.LiquidPool;
            sessionItemScale = builtSession.ItemScale;
            sessionSlots.Clear();
            sessionSlots.AddRange(builtSession.Slots);
            if (builtSession.ServingGlass is GlassController servingGlass)
            {
                CharacterStage characterStage = FindInScene<CharacterStage>(targetScene);
                builtSession.InteractionOverlay?.ConfigureServingTarget(
                    characterStage,
                    allowFallbackTarget: false);
                servingGlass.ConfigureServeGesture(builtSession.InteractionOverlay);
                servingGlass.ServeRequested += HandleGlassServeRequested;
            }
            List<BottleController> selectedBottles = CreateSelectedBottles();
            snapRoutine = StartCoroutine(SnapStartingItems(builtSession.StartingTools, selectedBottles));

            readyRoutine = StartCoroutine(CaptureTargetTracker(builtSession.ServingGlass));

            sourceCamera = Camera.main;
            if (sourceCamera != null && sourceCamera != builtSession.WorldCamera)
            {
                sourceCameraMask = sourceCamera.cullingMask;
                sourceCamera.cullingMask &= ~(1 << sessionRenderLayer);
            }
        }

        private void DestroySession(bool clearBottleSelections)
        {
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
            sessionViewport = null;
            sessionSlotLayout = null;
            sessionLiquidPool = null;

            if (clearBottleSelections)
                selectedBottleDefinitions.Clear();

            RestoreSessionOverrides();
            CurrentTargetTracker = null;
            SessionDestroyed?.Invoke();
        }

        private IEnumerator CaptureTargetTracker(GlassController glass)
        {
            yield return null;
            CurrentTargetTracker = glass != null ? glass.LiquidTracker : null;
            readyRoutine = null;
            SessionReady?.Invoke(CurrentTargetTracker);
        }

        private void HandleGlassServeRequested(GlassController glass)
        {
            VesselLiquidTracker tracker = glass != null ? glass.LiquidTracker : null;
            if (tracker == null || tracker != CurrentTargetTracker)
                return;

            ServeRequested?.Invoke(tracker);
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

            if (FindSelectedDefinition(shelfDefinition.id) != null)
            {
                failure = $"{shelfDefinition.displayName} 병은 이미 테이블에 있습니다.";
                return false;
            }

            if (itemCatalog == null || !itemCatalog.TryGet(shelfDefinition.id, out ItemDef item)
                || item == null || item.type != ItemType.Bottle)
            {
                failure = $"{shelfDefinition.displayName}에 연결된 제작용 재료가 없습니다.";
                return false;
            }

            GameProgress progress = GameProgress.Instance;
            float inventoryAmount = progress != null
                ? progress.GetBottleAmount(shelfDefinition.id, shelfDefinition.MaxAmount)
                : shelfDefinition.MaxAmount;
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

            BottleController bottle = CreateBottle(item, shelfDefinition.MaxAmount);
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

        private List<BottleController> CreateSelectedBottles()
        {
            List<BottleController> bottles = new List<BottleController>();
            for (int i = 0; i < selectedBottleDefinitions.Count; i++)
            {
                LiquorBottleDef shelfDefinition = selectedBottleDefinitions[i];
                if (shelfDefinition == null
                    || itemCatalog == null
                    || !itemCatalog.TryGet(shelfDefinition.id, out ItemDef item)
                    || item == null
                    || item.type != ItemType.Bottle)
                {
                    continue;
                }

                BottleController bottle = CreateBottle(item, shelfDefinition.MaxAmount);
                if (bottle != null)
                    bottles.Add(bottle);
            }

            return bottles;
        }

        private BottleController CreateBottle(ItemDef item, float defaultInventoryAmount)
        {
            if (item == null || sessionWorld == null)
                return null;

            IBartendingItem bottleItem = BartendingSessionBuilder.CreateItem(
                settings.bottlePrefab,
                sessionWorld,
                string.IsNullOrWhiteSpace(item.displayName) ? item.id : item.displayName,
                settings.bottlePosition,
                sessionRenderLayer,
                sessionItemScale);
            BartendingSessionBuilder.ConfigureRotatingMovement(bottleItem, settings);
            if (bottleItem is not BottleController bottle)
            {
                if (bottleItem != null)
                    Destroy(bottleItem.GameObject);
                return null;
            }

            bottle.Init(item);
            RegisterBottle(bottle, defaultInventoryAmount);
            return bottle;
        }

        private LiquorBottleDef FindSelectedDefinition(string itemId)
        {
            for (int i = 0; i < selectedBottleDefinitions.Count; i++)
            {
                LiquorBottleDef definition = selectedBottleDefinitions[i];
                if (definition != null
                    && string.Equals(definition.id, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    return definition;
                }
            }

            return null;
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

        private static IBartendingItem CreateItem(
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
                tracker.SetDebugViewEnabled(false);
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
