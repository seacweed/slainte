using System;
using System.Collections;
using System.Collections.Generic;
using Slainte.Content;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Bartending
{
    // Canvas/UI와 GPU 제조 서비스를 조립한다. 물리·재고·판정의 세부 처리는 각 서비스가 소유한다.
    public sealed class BusinessBartendingBootstrap : MonoBehaviour
    {
        private const string SceneName = "BusinessScene";
        private readonly List<GameObject> hiddenCanvasItems = new List<GameObject>();
        private readonly BartendingBottleStock inventoryTotals = new BartendingBottleStock();
        private BusinessBartendingSettings settings;
        private GameModeManager modeManager;
        private FrontCameraRig cameraRig;
        private ToolSelectionUI toolSelection;
        private IngredientSelectionUI ingredientSelection;
        private GpuCraftingSession session;
        private bool craftingActive;
        public bool IsSessionReady => craftingActive && session != null && session.Ready;
        public int SessionBottleCount => session?.Supply?.DeployedCount ?? 0;
        public GpuCraftingSession ActiveSession => craftingActive ? session : null;
        public bool IsGpuPrepared => session != null && session.IsPrepared;
        public event Action<string> StockChanged;
        public event Action SessionReady, SessionDestroyed;
        public event Action<CocktailComposition> CompositionServeRequested;
        public event Action<string> ServeRejected;
        // 레거시 에디터 검증의 컴파일 호환만 유지한다. 정식 제조에서는 발행하지 않는다.
        public event Action<VesselLiquidTracker> ServeRequested { add { } remove { } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneHook()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallInInitialScene() => InstallIfNeeded(SceneManager.GetActiveScene());
        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => InstallIfNeeded(scene);
        private static void InstallIfNeeded(Scene scene)
        { if (scene.IsValid() && scene.name == SceneName) EnsureInstalledForScene(scene); }
        public static BusinessBartendingBootstrap EnsureInstalledForScene(Scene scene)
        {
            if (!scene.IsValid()) return null;
            var existing = FindInScene<BusinessBartendingBootstrap>(scene);
            if (existing != null) return existing;
            var configuration = Resources.Load<BusinessBartendingSettings>(ProjectResourcePaths.BartendingSettings);
            if (configuration == null) { Debug.LogError("[GpuCrafting] Business settings are missing."); return null; }
            var host = new GameObject("BusinessBartendingRuntime");
            SceneManager.MoveGameObjectToScene(host, scene);
            var result = host.AddComponent<BusinessBartendingBootstrap>();
            result.Initialize(configuration);
            return result;
        }
        private void Initialize(BusinessBartendingSettings configuration)
        {
            settings = configuration;
            modeManager = FindInScene<GameModeManager>(gameObject.scene);
            if (modeManager == null) { Debug.LogError("[GpuCrafting] GameModeManager is missing."); return; }
            cameraRig = FindInScene<FrontCameraRig>(gameObject.scene);
            if (cameraRig != null)
            { cameraRig.MoveStarted += HandleViewMoveStarted; cameraRig.MoveCompleted += HandleViewMoveCompleted; }
            toolSelection = FindInScene<ToolSelectionUI>(gameObject.scene);
            ingredientSelection = FindInScene<IngredientSelectionUI>(gameObject.scene);
            toolSelection?.Initialize(this);
            inventoryTotals.StockChanged += HandleStockChanged;
            HideLegacyCanvasItems();
            modeManager.OnModeChanged += HandleModeChanged;
            StartCoroutine(SyncInitialMode());
        }
        private IEnumerator SyncInitialMode()
        {
            yield return null;
            PrepareSession();
            if (modeManager != null && modeManager.CurrentMode == GameMode.CraftingMode) CreateSession();
        }
        private void HandleModeChanged(GameMode oldMode, GameMode newMode)
        { if (newMode == GameMode.CraftingMode) CreateSession(); else DestroySession(); }
        private void CreateSession()
        {
            PrepareSession();
            if (session == null || craftingActive) return;
            craftingActive = true;
            session.BeginCrafting();
            if (cameraRig != null && cameraRig.IsAnimating) session.SetSuspended(true);
        }
        private void PrepareSession()
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.Prepare);
            if (session != null) return;
            if (toolSelection == null || !toolSelection.IsConfigured)
            { Debug.LogError("[GpuCrafting] Configure the Canvas tool selection before entering manufacturing."); return; }
            var counter = FindRect("BarCounter");
            if (counter == null) { Debug.LogError("[GpuCrafting] BarCounter is missing."); return; }
            var root = new GameObject("GpuCraftingSession");
            root.transform.SetParent(transform, false);
            session = root.AddComponent<GpuCraftingSession>();
            try
            {
                session.ReadyChanged += HandleSessionReady;
                session.Submitted += HandleSubmitted;
                session.SubmissionRejected += HandleRejected;
                session.Initialize(settings, counter, FindRect("CraftingSpace"), FindRect("CraftingSlotRow"),
                    FindInScene<CharacterStage>(gameObject.scene), GetShelfWorldSize);
                session.Supply.StockChanged += HandleStockChanged;
                session.Supply.ToolAvailabilityChanged += HandleToolAvailability;
                toolSelection.PrewarmSessionVisuals();
            }
            catch (Exception error) { Debug.LogException(error, this); DisposePreparedSession(); }
        }
        private Vector2 GetShelfWorldSize(string id)
        {
            Vector2 size = Vector2.zero;
            bool found = toolSelection != null && toolSelection.TryGetShelfWorldSize(id, out size, session?.Viewport);
            if (!found && (ingredientSelection == null
                || !ingredientSelection.TryGetShelfWorldSize(id, out size, session?.Viewport))) return Vector2.zero;
            // Prewarm can run during a dialogue close-up. Manufacturing uses the full
            // view, so remove that temporary display zoom from its physical size units.
            Vector3 zoom = cameraRig != null ? cameraRig.FrontWorld.localScale : Vector3.one;
            return new Vector2(size.x / Mathf.Max(.0001f, Mathf.Abs(zoom.x)),
                size.y / Mathf.Max(.0001f, Mathf.Abs(zoom.y)));
        }

        private void HandleSessionReady()
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.ReadyCallbacks);
            if (!craftingActive) return;
            toolSelection?.NotifySessionReady();
            SessionReady?.Invoke();
        }
        private void HandleSubmitted(CocktailComposition composition) => CompositionServeRequested?.Invoke(composition);
        private void HandleRejected(string reason) => ServeRejected?.Invoke(reason);
        private void HandleStockChanged(string id) => StockChanged?.Invoke(id);
        private void HandleToolAvailability(string id, bool available) => toolSelection?.NotifyAvailability(id, available);
        private void HandleViewMoveStarted() { if (craftingActive) session?.SetSuspended(true); }
        private void HandleViewMoveCompleted() { if (craftingActive) session?.SetSuspended(false); }
        private void DestroySession(bool notify = true)
        {
            if (session == null || !craftingActive) return;
            craftingActive = false;
            session.EndCrafting();
            if (notify)
            {
                toolSelection?.NotifySessionDestroyed();
                SessionDestroyed?.Invoke();
            }
        }
        private void DisposePreparedSession()
        {
            if (session == null) return;
            session.Close();
            session.ReadyChanged -= HandleSessionReady;
            session.Submitted -= HandleSubmitted;
            session.SubmissionRejected -= HandleRejected;
            if (session.Supply != null)
            { session.Supply.StockChanged -= HandleStockChanged; session.Supply.ToolAvailabilityChanged -= HandleToolAvailability; }
            Destroy(session.gameObject);
            session = null;
            craftingActive = false;
        }
        public void PrepareCabinetInventory(ToolCabinetCatalog catalog) => session?.Supply?.PrepareCabinet(catalog);
        public bool HasHeldBartendingItem() => session?.Supply?.HasHeldItem() ?? false;
        public bool ContainsShelfReturnPoint(Vector2 screenPosition)
            => IsSessionReady && Slainte.Shared.Display.GameAspect.NormalizedToScreenRect(
                settings.shelfReturnAreaNormalized).Contains(screenPosition);
        public bool IsPointerOverWorldItem(Vector2 screenPosition) => session != null && session.IsPointerOverItem(screenPosition);
        public bool TryTakeFromShelf(LiquorBottleDef definition, Vector2 screenPosition, out string failure)
        {
            if (!TryMapPointer(screenPosition, out var position, out failure)) return false;
            return session.Supply.TryTake(definition, position, true, out failure);
        }
        public bool TryPlaceBottleFromShelf(LiquorBottleDef definition, out string failure)
        {
            failure = string.Empty;
            if (!IsSessionReady) { failure = "The GPU crafting session is not ready."; return false; }
            return session.Supply.TryTake(definition, settings.bottlePosition, false, out failure);
        }
        public bool TryPickUpCabinetItem(string definitionId, Vector2 screenPosition, out string failure)
        {
            if (!TryMapPointer(screenPosition, out var position, out failure)) return false;
            return session.Supply.TryTakeTool(definitionId, position, out failure);
        }
        public bool TryReturnHeldBottleToShelf(out string failure)
        { failure = string.Empty; return IsSessionReady && session.Supply.TryReturnIngredient(out failure); }
        public bool TryReturnCabinetItem(IBartendingItem item, string definitionId, out string failure)
        { failure = "Legacy cabinet objects cannot enter the GPU crafting session."; return false; }
        public bool TryRefillIceBucket(out string failure)
        { failure = "Use the GPU dispenser for ice."; return false; }
        public float GetShelfAmount(LiquorBottleDef definition) => session?.Supply != null
            ? session.Supply.GetShelfAmount(definition) : inventoryTotals.GetShelfAmount(definition);
        public void AddDeliveredBottle(LiquorBottleDef definition)
        {
            if (session?.Supply != null) session.Supply.AddDeliveredBottle(definition);
            else inventoryTotals.AddDeliveredBottle(definition);
        }
        private bool TryMapPointer(Vector2 screen, out Vector2 position, out string failure)
        {
            position = default; failure = string.Empty;
            if (!IsSessionReady || modeManager.CurrentMode != GameMode.CraftingMode
                || !BartendingViewport.TryGetPointerWorldPosition(null, screen, out var world))
            { failure = "The GPU crafting viewport is not ready."; return false; }
            position = world;
            return true;
        }
        private RectTransform FindRect(string name)
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var rect in root.GetComponentsInChildren<RectTransform>(true)) if (rect.name == name) return rect;
            return null;
        }
        private void HideLegacyCanvasItems()
        {
            var slots = FindRect("TableSlots");
            if (slots != null && slots.gameObject.activeSelf) { hiddenCanvasItems.Add(slots.gameObject); slots.gameObject.SetActive(false); }
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                    foreach (var behaviour in canvas.GetComponentsInChildren<MonoBehaviour>(true))
                        if (behaviour is BeakerController or GlassController or BottleController && behaviour.gameObject.activeSelf)
                        { hiddenCanvasItems.Add(behaviour.gameObject); behaviour.gameObject.SetActive(false); }
        }
        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects()) { var result = root.GetComponentInChildren<T>(true); if (result != null) return result; }
            return null;
        }
        private void OnDestroy()
        {
            if (modeManager != null) modeManager.OnModeChanged -= HandleModeChanged;
            if (cameraRig != null) { cameraRig.MoveStarted -= HandleViewMoveStarted; cameraRig.MoveCompleted -= HandleViewMoveCompleted; }
            // 씬 종료 중 주문 취소 이벤트로 이미 파괴된 Canvas를 다시 열지 않는다.
            DisposePreparedSession();
            inventoryTotals.StockChanged -= HandleStockChanged;
            foreach (var item in hiddenCanvasItems) if (item != null) item.SetActive(true);
            hiddenCanvasItems.Clear();
        }
    }
}
