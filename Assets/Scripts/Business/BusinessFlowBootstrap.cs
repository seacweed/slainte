using System.Collections;
using Slainte.Bartending;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Business
{
    public sealed class BusinessFlowBootstrap : MonoBehaviour
    {
        private const string SceneName = "BusinessScene";
        private const string SettingsResourcePath = "Business/BusinessOrderFlowSettings";

        [SerializeField] private BusinessOrderFlowSettings settings;

        private BusinessOrderSessionController orderSession;
        private BusinessOrderSessionUI sessionUi;
        private BusinessShiftController shiftController;
        private EpisodeCraftingBridge episodeCraftingBridge;
        private CraftingJudgeUI legacyCraftingJudge;
        private EpisodeRunner episodeRunner;
        private GameModeManager modeManager;
        private bool runtimeInitialized;
        private bool businessStartRequested;
        private bool businessSequenceActive;
        private bool episodeOrderActive;

        public bool IsRuntimeReady => runtimeInitialized;
        public BusinessShiftController ShiftController => shiftController;
        public BusinessOrderSessionController OrderSessionController => orderSession;

        public bool TryOverrideSettingsBeforeInitialization(
            BusinessOrderFlowSettings overrideSettings)
        {
            if (runtimeInitialized || overrideSettings == null)
                return false;

            settings = overrideSettings;
            return true;
        }

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
            if (!scene.IsValid() || scene.name != SceneName || FindInScene<BusinessFlowBootstrap>(scene) != null)
                return;

            GameObject host = new GameObject("BusinessFlow");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<BusinessFlowBootstrap>();
        }

        private IEnumerator Start()
        {
            yield return null;
            InitializeRuntime();
            yield return null;

            if (settings != null && settings.autoStart && !HasActiveEpisode())
                StartBusinessSequence();
        }

        private void OnDestroy()
        {
            if (orderSession != null)
                orderSession.StateChanged -= HandleOrderSessionStateChanged;
            if (shiftController != null)
                shiftController.ShiftCompleted -= HandleBusinessDayCompleted;
            if (modeManager != null)
                modeManager.OnModeChanged -= HandleGameModeChanged;
        }

        private void Update()
        {
            if (!runtimeInitialized
                || settings == null
                || !settings.autoStart
                || businessSequenceActive
                || episodeOrderActive
                || HasActiveEpisode())
                return;

            GameManager gameManager = GameManager.Instance;
            if (gameManager != null && gameManager.CurrentState == GameState.Business)
                StartBusinessSequence();
        }

        private void InitializeRuntime()
        {
            Scene scene = gameObject.scene;
            settings ??= Resources.Load<BusinessOrderFlowSettings>(SettingsResourcePath);
            if (settings == null)
            {
                Debug.LogError("Resources에서 영업 주문 설정을 불러올 수 없습니다.");
                return;
            }

            modeManager = FindInScene<GameModeManager>(scene);
            CustomerSpawner customerSpawner = FindInScene<CustomerSpawner>(scene);
            DialogueController dialogue = FindInScene<DialogueController>(scene);
            OrderTicketManager ticketManager = FindInScene<OrderTicketManager>(scene);
            BusinessBartendingBootstrap bartending = FindInScene<BusinessBartendingBootstrap>(scene);
            RectTransform canvasRoot = FindCanvasRoot(scene);
            legacyCraftingJudge = FindInScene<CraftingJudgeUI>(scene);
            episodeRunner = FindInScene<EpisodeRunner>(scene);
            BusinessStubUI businessStub = FindInScene<BusinessStubUI>(scene);

            if (modeManager == null || customerSpawner == null || dialogue == null
                || ticketManager == null || bartending == null || canvasRoot == null)
            {
                Debug.LogError("BusinessScene의 영업 진행 필수 구성 요소가 부족합니다.");
                return;
            }

            orderSession = GetComponent<BusinessOrderSessionController>();
            if (orderSession == null)
                orderSession = gameObject.AddComponent<BusinessOrderSessionController>();

            sessionUi = GetComponent<BusinessOrderSessionUI>();
            if (sessionUi == null)
                sessionUi = gameObject.AddComponent<BusinessOrderSessionUI>();
            sessionUi.Initialize(canvasRoot, orderSession);

            orderSession.Initialize(
                modeManager,
                customerSpawner,
                dialogue,
                ticketManager,
                bartending,
                sessionUi,
                settings);
            orderSession.StateChanged += HandleOrderSessionStateChanged;

            shiftController = GetComponent<BusinessShiftController>();
            if (shiftController == null)
                shiftController = gameObject.AddComponent<BusinessShiftController>();
            shiftController.Initialize(orderSession, sessionUi, modeManager, settings);
            shiftController.ShiftCompleted += HandleBusinessDayCompleted;

            episodeCraftingBridge = GetComponent<EpisodeCraftingBridge>();
            if (episodeCraftingBridge == null)
                episodeCraftingBridge = gameObject.AddComponent<EpisodeCraftingBridge>();
            episodeCraftingBridge.Initialize(this);
            episodeRunner?.SetCraftingBridge(episodeCraftingBridge);

            modeManager.OnModeChanged += HandleGameModeChanged;

            runtimeInitialized = true;
            if (businessStub != null)
                businessStub.gameObject.SetActive(false);
            RefreshLegacyCraftingJudge();

            if (businessStartRequested)
                StartBusinessSequence();
        }

        public void StartBusinessSequence()
        {
            businessStartRequested = true;
            if (!runtimeInitialized || episodeOrderActive)
                return;

            businessStartRequested = false;
            if (shiftController == null)
            {
                businessSequenceActive = false;
                Debug.LogError(
                    "[BusinessFlow] 영업 컨트롤러가 없어 시간 기반 영업을 시작할 수 없습니다. "
                    + $"RuntimeReady={runtimeInitialized}, EpisodeOrderActive={episodeOrderActive}");
                return;
            }

            if (shiftController.IsActive)
            {
                businessSequenceActive = true;
                return;
            }

            bool started = shiftController.BeginShift();
            businessSequenceActive = started && shiftController.IsActive;
            if (!started)
            {
                Debug.LogError(
                    "[BusinessFlow] 시간 기반 영업을 시작하지 못했습니다. "
                    + $"RuntimeReady={runtimeInitialized}, ShiftState={shiftController.State}, "
                    + $"ShiftActive={shiftController.IsActive}, EpisodeOrderActive={episodeOrderActive}");
                return;
            }

            if (!businessSequenceActive)
                return;

            modeManager?.RequestModeChange(GameMode.OrderMode);
            RefreshLegacyCraftingJudge();
        }

        public bool StartEpisodeOrder(
            OrderSessionRequest request,
            System.Action<BusinessOrderSessionResult> onCompleted)
        {
            bool allowedDuringBusinessEncounter = businessSequenceActive
                && shiftController != null
                && shiftController.State == BusinessShiftState.EncounterActive;
            if (!runtimeInitialized
                || orderSession == null
                || (businessSequenceActive && !allowedDuringBusinessEncounter)
                || episodeOrderActive
                || request == null
                || request.owner != OrderSessionOwner.Episode)
            {
                return false;
            }

            episodeOrderActive = true;
            RefreshLegacyCraftingJudge();

            bool started = orderSession.BeginOrder(request, result =>
            {
                episodeOrderActive = false;
                RefreshLegacyCraftingJudge();
                onCompleted?.Invoke(result);
            });

            if (!started)
                episodeOrderActive = false;
            return started;
        }

        private void HandleBusinessDayCompleted()
        {
            businessSequenceActive = false;
            DayFlowController.Instance?.OnBusinessCompleted();
        }

        private void HandleOrderSessionStateChanged(
            BusinessOrderSessionState previous,
            BusinessOrderSessionState next)
        {
            RefreshLegacyCraftingJudge();
        }

        private void HandleGameModeChanged(GameMode previous, GameMode next)
        {
            RefreshLegacyCraftingJudge();
        }

        private void RefreshLegacyCraftingJudge()
        {
            if (legacyCraftingJudge == null)
                return;

            bool episodeCrafting = episodeRunner != null
                && episodeRunner.IsRunning
                && episodeRunner.IsUsingManualCrafting
                && modeManager != null
                && modeManager.CurrentMode == GameMode.CraftingMode;
            legacyCraftingJudge.gameObject.SetActive(episodeCrafting);
        }

        private static bool HasActiveEpisode()
        {
            return EpisodeManager.Instance != null
                && !string.IsNullOrWhiteSpace(EpisodeManager.Instance.CurrentPlayingEpisodeID);
        }

        private static RectTransform FindCanvasRoot(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
                for (int i = 0; i < canvases.Length; i++)
                {
                    if (canvases[i].isRootCanvas && canvases[i].transform is RectTransform rect)
                        return rect;
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
                    return component;
            }

            return null;
        }
    }
}
