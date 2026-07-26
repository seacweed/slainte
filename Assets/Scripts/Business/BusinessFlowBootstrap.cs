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
        private BusinessSequenceRunner sequenceRunner;
        private BusinessOrderSessionUI sessionUi;
        private CraftingJudgeUI legacyCraftingJudge;
        private GameModeManager modeManager;
        private bool runtimeInitialized;
        private bool businessStartRequested;
        private bool businessSequenceActive;
        private bool episodeOrderActive;

        public bool IsRuntimeReady => runtimeInitialized;

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
            if (sequenceRunner != null)
                sequenceRunner.BusinessDayCompleted -= HandleBusinessDayCompleted;
        }

        private void InitializeRuntime()
        {
            Scene scene = gameObject.scene;
            settings ??= Resources.Load<BusinessOrderFlowSettings>(SettingsResourcePath);
            if (settings == null)
            {
                Debug.LogError("Business order flow settings could not be loaded from Resources.");
                return;
            }

            modeManager = FindInScene<GameModeManager>(scene);
            CustomerSpawner customerSpawner = FindInScene<CustomerSpawner>(scene);
            DialogueController dialogue = FindInScene<DialogueController>(scene);
            OrderTicketManager ticketManager = FindInScene<OrderTicketManager>(scene);
            BusinessBartendingBootstrap bartending = FindInScene<BusinessBartendingBootstrap>(scene);
            RectTransform canvasRoot = FindCanvasRoot(scene);
            legacyCraftingJudge = FindInScene<CraftingJudgeUI>(scene);

            if (modeManager == null || customerSpawner == null || dialogue == null
                || ticketManager == null || bartending == null || canvasRoot == null)
            {
                Debug.LogError("Business flow dependencies are incomplete in BusinessScene.");
                return;
            }

            orderSession = GetComponent<BusinessOrderSessionController>();
            if (orderSession == null)
                orderSession = gameObject.AddComponent<BusinessOrderSessionController>();

            sessionUi = GetComponent<BusinessOrderSessionUI>();
            if (sessionUi == null)
                sessionUi = gameObject.AddComponent<BusinessOrderSessionUI>();
            sessionUi.Initialize(canvasRoot, orderSession);

            sequenceRunner = GetComponent<BusinessSequenceRunner>();
            if (sequenceRunner == null)
                sequenceRunner = gameObject.AddComponent<BusinessSequenceRunner>();

            orderSession.Initialize(
                modeManager,
                customerSpawner,
                dialogue,
                ticketManager,
                bartending,
                sessionUi,
                settings);
            sequenceRunner.Initialize(orderSession, sessionUi, settings);
            orderSession.StateChanged += HandleOrderSessionStateChanged;
            sequenceRunner.BusinessDayCompleted += HandleBusinessDayCompleted;
            runtimeInitialized = true;
            if (legacyCraftingJudge != null)
                legacyCraftingJudge.gameObject.SetActive(false);

            if (businessStartRequested)
                StartBusinessSequence();
        }

        public void StartBusinessSequence()
        {
            businessStartRequested = true;
            if (!runtimeInitialized || sequenceRunner == null || episodeOrderActive)
                return;

            businessStartRequested = false;
            businessSequenceActive = true;
            modeManager?.RequestModeChange(GameMode.OrderMode);
            if (legacyCraftingJudge != null)
                legacyCraftingJudge.gameObject.SetActive(false);
            sequenceRunner.StartSequence();
        }

        public bool StartEpisodeOrder(
            OrderSessionRequest request,
            System.Action<BusinessOrderSessionResult> onCompleted)
        {
            if (!runtimeInitialized
                || orderSession == null
                || businessSequenceActive
                || episodeOrderActive
                || request == null
                || request.owner != OrderSessionOwner.Episode)
            {
                return false;
            }

            episodeOrderActive = true;
            if (legacyCraftingJudge != null)
                legacyCraftingJudge.gameObject.SetActive(false);

            bool started = orderSession.BeginOrder(request, result =>
            {
                episodeOrderActive = false;
                if (legacyCraftingJudge != null)
                    legacyCraftingJudge.gameObject.SetActive(false);
                onCompleted?.Invoke(result);
            });

            if (!started)
                episodeOrderActive = false;
            return started;
        }

        private void HandleBusinessDayCompleted()
        {
            businessSequenceActive = false;
            DayFlowManager.Instance.CompleteBusinessDay();
        }

        private void HandleOrderSessionStateChanged(
            BusinessOrderSessionState previous,
            BusinessOrderSessionState next)
        {
            if (legacyCraftingJudge == null)
                return;

            bool hideLegacyJudge = businessSequenceActive
                || episodeOrderActive
                || next == BusinessOrderSessionState.Crafting
                || next == BusinessOrderSessionState.Evaluating;
            legacyCraftingJudge.gameObject.SetActive(!hideLegacyJudge && !runtimeInitialized);
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
