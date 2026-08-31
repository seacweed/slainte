using System.Collections;
using System.Collections.Generic;
using Slainte.Content;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    public sealed class BartendingSandboxBootstrap : MonoBehaviour
    {
        private const string SettingsResourcePath = ProjectResourcePaths.BartendingSettings;

        [SerializeField] private BusinessBartendingSettings settings;
        [SerializeField] private RectTransform barCounter;
        [SerializeField] private RectTransform tableSlots;
        [SerializeField] private ItemDef[] testBottleItems;
        [SerializeField] private KeyCode resetKey = KeyCode.R;
        [SerializeField] private bool drawDebugPanel = true;

        private readonly List<BottleController> bottles = new();
        private BartendingSessionInstance session;
        private Coroutine snapRoutine;
        private CocktailEvaluator evaluator;
        private string lastResult =
            "Hold the serving glass over the translucent customer target and left-click to evaluate it.";

        public BartendingSessionInstance CurrentSession => session;

        private void Start()
        {
            EnsureLayout();
            CreateSession();
        }

        private void Update()
        {
            if (Input.GetKeyDown(resetKey))
                ResetSession();
        }

        public void EnsureLayout()
        {
            if (settings == null)
                settings = Resources.Load<BusinessBartendingSettings>(SettingsResourcePath);
            EnsureDisplayCamera();
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem));
            if (FindFirstObjectByType<AudioListener>() == null)
                gameObject.AddComponent<AudioListener>();
            if (barCounter != null && tableSlots != null)
            {
                BartendingSessionBuilder.EnsureSlotLayoutGuideCount(
                    tableSlots,
                    settings != null && settings.slotPositions != null
                        ? settings.slotPositions.Length
                        : 0);
                return;
            }

            Canvas canvas = GetComponentInChildren<Canvas>(true);
            if (canvas == null)
                canvas = CreateCanvas();

            barCounter = FindChildRect(canvas.transform, "BarCounter");
            if (barCounter == null)
                barCounter = CreateBarCounter(canvas.transform);

            tableSlots = FindChildRect(canvas.transform, "TableSlots");
            if (tableSlots == null)
                tableSlots = CreateTableSlots(canvas.transform);

            BartendingSessionBuilder.EnsureSlotLayoutGuideCount(
                tableSlots,
                settings != null && settings.slotPositions != null
                    ? settings.slotPositions.Length
                    : 0);

        }

        private void EnsureDisplayCamera()
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
                for (int i = 0; i < cameras.Length; i++)
                {
                    Camera existing = cameras[i];
                    if (existing != null
                        && existing.enabled
                        && existing.gameObject.activeInHierarchy
                        && existing.targetTexture == null)
                    {
                        return;
                    }
                }
            }

            GameObject cameraObject = new GameObject("SandboxDisplayCamera");
            cameraObject.transform.SetParent(transform, false);
            Camera displayCamera = cameraObject.AddComponent<Camera>();
            displayCamera.clearFlags = CameraClearFlags.SolidColor;
            displayCamera.backgroundColor = Color.black;
            displayCamera.cullingMask = 0;
            displayCamera.depth = -100f;
            displayCamera.allowHDR = false;
            displayCamera.allowMSAA = false;
        }

        public void ResetSession()
        {
            DestroySession();
            CreateSession();
        }

        private void CreateSession()
        {
            if (session != null)
                return;
            if (settings == null || barCounter == null)
            {
                Debug.LogError("Bartending sandbox requires settings and a BarCounter.");
                return;
            }

            session = BartendingSessionBuilder.Build(
                transform,
                barCounter,
                tableSlots,
                settings,
                BartendingSessionBuildMode.Runtime,
                useToolCabinetOverride: false);
            if (session == null)
                return;

            session.Viewport?.SetOutputVisible(false);

            if (session.ServingGlass != null)
            {
                session.InteractionOverlay?.ConfigureServingTarget(
                    null,
                    allowFallbackTarget: true);
                session.ServingGlass.ConfigureServeGesture(session.InteractionOverlay);
                session.ServingGlass.ServeRequested += HandleServeRequested;
            }

            CreateTestBottles();
            snapRoutine = StartCoroutine(SnapAfterInitialization());

            ItemDefCatalog itemCatalog = ItemDefCatalog.LoadFromResources(
                ProjectResourcePaths.BartendingItems,
                null);
            CocktailRecipeCatalog recipes = CocktailRecipeDataLoader.LoadDefault(itemCatalog);
            evaluator = new CocktailEvaluator(recipes);
        }

        private void CreateTestBottles()
        {
            bottles.Clear();
            ItemDef[] items = testBottleItems != null && testBottleItems.Length > 0
                ? testBottleItems
                : settings.initialBottleItems;
            if (items == null || items.Length == 0)
            {
                items = new[]
                {
                    Resources.Load<ItemDef>(
                        ProjectResourcePaths.BartendingPlanningItems + "/item_1006"),
                    Resources.Load<ItemDef>(
                        ProjectResourcePaths.BartendingPlanningItems + "/item_1009")
                };
            }
            if (items == null || session == null)
                return;

            for (int i = 0; i < items.Length; i++)
            {
                ItemDef item = items[i];
                if (item == null || item.type != ItemType.Bottle)
                    continue;

                IBartendingItem created = BartendingSessionBuilder.CreateItem(
                    settings.bottlePrefab,
                    session.World,
                    string.IsNullOrWhiteSpace(item.displayName) ? item.id : item.displayName,
                    settings.bottlePosition,
                    session.RenderLayer,
                    session.ItemScale,
                    settings);
                BartendingSessionBuilder.ConfigureRotatingMovement(created, settings);
                if (created is BottleController bottle)
                {
                    bottle.Init(item);
                    bottles.Add(bottle);
                }
                else if (created != null)
                {
                    Destroy(created.GameObject);
                }
            }
        }

        private IEnumerator SnapAfterInitialization()
        {
            yield return null;
            if (session == null)
                yield break;

            for (int i = 0; i < session.StartingTools.Count && i < session.Slots.Count; i++)
                Snap(session.StartingTools[i], session.Slots[i]);

            for (int i = 0; i < bottles.Count; i++)
            {
                SlotController slot = FindRightmostFreeSlot();
                if (slot == null)
                    break;
                Snap(bottles[i], slot);
            }

            session.Viewport?.SetOutputVisible(true);

            snapRoutine = null;
        }

        private void HandleServeRequested(GlassController glass)
        {
            VesselLiquidTracker tracker = glass != null ? glass.LiquidTracker : null;
            if (tracker == null || evaluator == null)
                return;

            CocktailEvaluationResult result = evaluator.Evaluate(tracker.BuildComposition());
            lastResult = result.ToDebugString();
            Debug.Log("[BartendingSandbox]\n" + lastResult);
        }

        private SlotController FindRightmostFreeSlot()
        {
            if (session == null)
                return null;
            for (int i = session.Slots.Count - 1; i >= 0; i--)
            {
                SlotController slot = session.Slots[i];
                if (slot != null && !slot.IsOccupied)
                    return slot;
            }

            return null;
        }

        private static void Snap(IBartendingItem item, SlotController slot)
        {
            if (item == null || slot == null)
                return;
            slot.Occupy(item);
            item.SnapToSlot(slot.transform, slot);
        }

        private void DestroySession()
        {
            if (snapRoutine != null)
            {
                StopCoroutine(snapRoutine);
                snapRoutine = null;
            }

            if (session?.ServingGlass != null)
                session.ServingGlass.ServeRequested -= HandleServeRequested;
            session?.Destroy();
            session = null;
            bottles.Clear();
        }

        private void OnDestroy()
        {
            DestroySession();
        }

        private void OnGUI()
        {
            if (!drawDebugPanel || !Application.isPlaying)
                return;

            string text = $"Bartending Sandbox | Reset: {resetKey}\n{lastResult}";
            GUI.Box(new Rect(12f, 12f, 520f, 150f), text);
        }

        private Canvas CreateCanvas()
        {
            GameObject canvasObject = new GameObject(
                "Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static RectTransform CreateBarCounter(Transform parent)
        {
            GameObject counter = new GameObject(
                "BarCounter",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            RectTransform rect = (RectTransform)counter.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 460f);
            Image image = counter.GetComponent<Image>();
            image.color = new Color(0.12f, 0.08f, 0.06f, 1f);
            image.raycastTarget = false;
            return rect;
        }

        private static RectTransform CreateTableSlots(Transform parent)
        {
            GameObject layout = new GameObject("TableSlots", typeof(RectTransform));
            RectTransform rect = (RectTransform)layout.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 185f);
            rect.sizeDelta = new Vector2(2200f, 150f);

            const int count = 8;
            const float spacing = 275f;
            for (int i = 0; i < count; i++)
            {
                GameObject slot = new GameObject(
                    "UISlot_" + i,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(UIDropSlot));
                RectTransform slotRect = (RectTransform)slot.transform;
                slotRect.SetParent(rect, false);
                slotRect.anchorMin = new Vector2(0.5f, 0.5f);
                slotRect.anchorMax = new Vector2(0.5f, 0.5f);
                slotRect.pivot = new Vector2(0.5f, 0.5f);
                slotRect.anchoredPosition = new Vector2(
                    (i - (count - 1) * 0.5f) * spacing,
                    0f);
                slotRect.sizeDelta = new Vector2(260f, 105f);
                Image image = slot.GetComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0.14f);
                image.raycastTarget = false;
            }

            return rect;
        }

        private static RectTransform FindChildRect(Transform root, string objectName)
        {
            foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect.name == objectName)
                    return rect;
            }

            return null;
        }
    }
}
