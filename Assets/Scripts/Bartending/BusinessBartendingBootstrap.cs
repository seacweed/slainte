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
        private Scene targetScene;
        private BusinessBartendingSettings settings;
        private GameModeManager modeManager;
        private GameObject sessionRoot;
        private BartendingViewport sessionViewport;
        private LiquidPool sessionLiquidPool;
        private Coroutine snapRoutine;
        private Coroutine resetRoutine;
        private Coroutine readyRoutine;
        private Camera sourceCamera;
        private int sourceCameraMask;

        public VesselLiquidTracker CurrentTargetTracker { get; private set; }
        public event Action<VesselLiquidTracker> SessionReady;
        public event Action SessionDestroyed;

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
            if (!scene.IsValid() || scene.name != SceneName || FindInScene<BusinessBartendingBootstrap>(scene) != null)
            {
                return;
            }

            BusinessBartendingSettings settings =
                Resources.Load<BusinessBartendingSettings>(SettingsResourcePath);
            if (settings == null)
            {
                Debug.LogError("Business bartending settings could not be loaded from Resources.");
                return;
            }

            GameObject host = new GameObject("BusinessBartendingRuntime");
            SceneManager.MoveGameObjectToScene(host, scene);
            BusinessBartendingBootstrap bootstrap = host.AddComponent<BusinessBartendingBootstrap>();
            bootstrap.Initialize(scene, settings);
        }

        private void Initialize(Scene scene, BusinessBartendingSettings sessionSettings)
        {
            targetScene = scene;
            settings = sessionSettings;
            modeManager = FindInScene<GameModeManager>(scene);
            if (modeManager == null)
            {
                Debug.LogError("Business bartending needs GameModeManager to track CraftingMode.");
                enabled = false;
                return;
            }

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
                DestroySession();
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
                Debug.LogError("Business bartending needs the BarCounter RectTransform.");
                return;
            }

            int renderLayer = Mathf.Clamp(settings.renderLayer, 8, 31);
            sessionRoot = new GameObject("BartendingSession");
            sessionRoot.transform.SetParent(transform, false);
            GameObject world = new GameObject("BartendingWorld");
            world.transform.SetParent(sessionRoot.transform, false);

            Camera camera = CreateWorldCamera(world.transform, settings, renderLayer);
            sessionViewport = CreateViewport(counter, camera, settings);
            Canvas.ForceUpdateCanvases();

            GetSlotLayout(targetScene, sessionViewport, settings, out List<Vector3> slotPositions, out float itemScale);
            List<SlotController> slots = CreateSlots(world.transform, settings, slotPositions, itemScale);
            List<IBartendingItem> startingItems = new List<IBartendingItem>();
            CreateInitialBottles(world.transform, renderLayer, itemScale, startingItems);
            IBartendingItem beaker = CreateItem(
                settings.beakerPrefab, world.transform, "Beaker", settings.beakerPosition, renderLayer, itemScale);
            IBartendingItem glass = CreateItem(
                settings.glassPrefab, world.transform, "Glass", settings.glassPosition, renderLayer, itemScale);
            startingItems.Add(beaker);
            startingItems.Add(glass);
            sessionLiquidPool = CreateLiquidPool(world.transform, settings, renderLayer, itemScale);
            snapRoutine = StartCoroutine(SnapStartingItems(slots, startingItems));

            readyRoutine = StartCoroutine(CaptureTargetTracker(glass as GlassController));

            sourceCamera = Camera.main;
            if (sourceCamera != null && sourceCamera != camera)
            {
                sourceCameraMask = sourceCamera.cullingMask;
                sourceCamera.cullingMask &= ~(1 << renderLayer);
            }
        }

        private void DestroySession()
        {
            if (snapRoutine != null)
            {
                StopCoroutine(snapRoutine);
                snapRoutine = null;
            }

            if (sessionViewport != null)
            {
                sessionViewport.gameObject.SetActive(false);
                Destroy(sessionViewport.gameObject);
                sessionViewport = null;
            }

            if (sessionLiquidPool != null && LiquidPool.Instance == sessionLiquidPool)
            {
                LiquidPool.Instance = null;
            }
            sessionLiquidPool = null;

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

            if (sessionRoot != null)
            {
                sessionRoot.SetActive(false);
                Destroy(sessionRoot);
                sessionRoot = null;
            }

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

        public void DiscardAndResetSession()
        {
            if (modeManager == null || modeManager.CurrentMode != GameMode.CraftingMode)
                return;

            if (resetRoutine == null)
                resetRoutine = StartCoroutine(ResetSessionRoutine());
        }

        private IEnumerator ResetSessionRoutine()
        {
            DestroySession();
            yield return null;
            if (modeManager != null && modeManager.CurrentMode == GameMode.CraftingMode)
                CreateSession();
            resetRoutine = null;
        }

        private void CreateInitialBottles(
            Transform parent,
            int renderLayer,
            float itemScale,
            List<IBartendingItem> startingItems)
        {
            ItemDef[] bottleItems = settings.initialBottleItems;
            if (bottleItems == null || bottleItems.Length == 0)
            {
                IBartendingItem fallback = CreateItem(
                    settings.orangeJuiceBottlePrefab,
                    parent,
                    "Bottle",
                    settings.bottlePosition,
                    renderLayer,
                    itemScale);
                startingItems.Add(fallback);
                RegisterBottle(fallback as BottleController);
                return;
            }

            for (int i = 0; i < bottleItems.Length; i++)
            {
                ItemDef item = bottleItems[i];
                if (item == null || item.type != ItemType.Bottle)
                    continue;

                Vector3 position = settings.bottlePositions != null && i < settings.bottlePositions.Length
                    ? settings.bottlePositions[i]
                    : settings.bottlePosition + new Vector3(i * 3.2f, 0f, 0f);
                IBartendingItem bottleItem = CreateItem(
                    settings.orangeJuiceBottlePrefab,
                    parent,
                    string.IsNullOrWhiteSpace(item.displayName) ? item.id : item.displayName,
                    position,
                    renderLayer,
                    itemScale);

                if (bottleItem is BottleController bottle)
                {
                    bottle.Init(item);
                    RegisterBottle(bottle);
                }

                startingItems.Add(bottleItem);
            }
        }

        private void RegisterBottle(BottleController bottle)
        {
            if (bottle == null || sessionBottles.Contains(bottle))
                return;

            ItemDef item = bottle.BottleData;
            if (item != null && !string.IsNullOrWhiteSpace(item.id) && GameProgress.Instance != null)
            {
                float amount = GameProgress.Instance.GetBottleAmount(item.id, item.capacityMl);
                bottle.SetCurrentCapacity(amount);
            }

            bottle.CapacityChanged += HandleBottleCapacityChanged;
            sessionBottles.Add(bottle);
        }

        private static void HandleBottleCapacityChanged(BottleController bottle, float amount)
        {
            ItemDef item = bottle != null ? bottle.BottleData : null;
            if (item == null || string.IsNullOrWhiteSpace(item.id) || GameProgress.Instance == null)
                return;

            GameProgress.Instance.SetBottleAmount(item.id, amount);
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
            Scene scene,
            BartendingViewport viewport,
            BusinessBartendingSettings settings,
            out List<Vector3> positions,
            out float itemScale)
        {
            positions = settings.slotPositions != null
                ? new List<Vector3>(settings.slotPositions)
                : new List<Vector3>();
            itemScale = 1f;

            RectTransform tableSlots = FindNamedRectTransform(scene, "TableSlots");
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
                Debug.LogWarning(instanceName + " prefab is not assigned in business bartending settings.");
                return null;
            }

            GameObject item = Instantiate(prefab, parent);
            item.name = instanceName;
            item.transform.localPosition = position;
            item.transform.localRotation = Quaternion.identity;
            item.transform.localScale = Vector3.one * itemScale;
            SetLayerRecursively(item, renderLayer);
            return item.GetComponent<IBartendingItem>();
        }

        private static IEnumerator SnapStartingItems(
            List<SlotController> slots,
            List<IBartendingItem> items)
        {
            yield return null;

            if (items == null)
                yield break;

            for (int i = 0; i < items.Count && i < slots.Count; i++)
                SnapToStartingSlot(items[i], slots, i);
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

            DestroySession();
            RestoreHiddenCanvasItems();
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
