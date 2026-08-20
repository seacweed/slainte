using System.Collections;
using System.Collections.Generic;
using System.Text;
using Slainte.Bartending;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Business
{
    public enum BusinessPlaytestScenario
    {
        NormalShift,
        ShortTimer,
        RequiredCustomer,
        EncounterTimerRuns,
        RequiredQueue,
        EncounterWithCrafting,
        Settlement,
        RandomEncounterPool,
        EncounterAfterFirstOrder
    }

    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class BusinessIntegrationPlaytestBootstrap : MonoBehaviour
    {
        private const string SettingsResourcePath = "Business/BusinessOrderFlowSettings";
        private const string SimpleEncounterId = "StrangeCoin_0";
        private const string CraftingEncounterId = "StrangeCoin_0";

        [Header("Runtime Pool")]
        [SerializeField, Min(1)] private int customerCount = 12;
        [SerializeField] private CustomerVisitData templateVisit;
        [SerializeField] private BusinessOrderFlowSettings sourceSettings;

        private readonly List<CustomerVisitData> runtimeVisits = new();
        private readonly Dictionary<string, int> appearances = new();
        private readonly StringBuilder panel = new(1600);

        private PlaytestProgressIsolation isolation;
        private BusinessOrderFlowSettings runtimeSettings;
        private CustomerVisitDatabase runtimeDatabase;
        private BusinessFlowBootstrap flow;
        private BusinessShiftController shift;
        private BusinessOrderSessionController orderSession;
        private GUIStyle panelStyle;
        private GUIStyle buttonStyle;
        private bool ready;
        private bool scenarioStarted;
        private bool manualPause;
        private bool encounterWasActive;
        private float encounterStartRemaining;
        private float maximumEncounterTimerDecrease;
        private int encounterCount;
        private int startMoney;
        private int startReputation;
        private string status = "Initializing isolated playtest...";

        public bool IsReady => ready;
        public bool IsScenarioStarted => scenarioStarted;
        public BusinessPlaytestScenario ActiveScenario { get; private set; }
        public float MaximumEncounterTimerDecrease => maximumEncounterTimerDecrease;

        private void Awake()
        {
            isolation = PlaytestProgressIsolation.Attach(gameObject);
            BusinessBartendingBootstrap.EnsureInstalledForScene(gameObject.scene);

            flow = FindInScene<BusinessFlowBootstrap>(gameObject.scene);
            if (flow == null)
            {
                status = "ERROR: BusinessFlowBootstrap was not found.";
                enabled = false;
                return;
            }

            if (!CreateRuntimeSettings())
            {
                enabled = false;
                return;
            }

            if (!flow.TryOverrideSettingsBeforeInitialization(runtimeSettings))
            {
                status = "ERROR: Business flow initialized before playtest injection.";
                enabled = false;
            }
        }

        private IEnumerator Start()
        {
            while (enabled
                && (isolation == null || !isolation.IsReady || !flow.IsRuntimeReady))
            {
                yield return null;
            }

            if (!enabled)
                yield break;

            shift = flow.ShiftController;
            orderSession = flow.OrderSessionController;
            if (shift == null || orderSession == null)
            {
                status = "ERROR: Business runtime controllers were not initialized.";
                yield break;
            }

            shift.StateChanged += HandleShiftStateChanged;
            shift.CustomerVisitStarted += HandleCustomerVisitStarted;
            orderSession.StateChanged += HandleOrderStateChanged;

            GameProgress progress = GameProgress.Instance;
            startMoney = progress != null ? progress.CurrentMoney : 0;
            startReputation = progress != null ? progress.Reputation : 0;
            ready = true;
            status = "Choose a scenario. Original progress and save data are isolated.";
        }

        private void Update()
        {
            if (!scenarioStarted || shift == null)
                return;

            if (Input.GetKeyDown(KeyCode.P))
                TogglePause();

            bool encounterActive = shift.State == BusinessShiftState.EncounterActive;
            if (encounterActive)
            {
                float decrease = Mathf.Max(0f, encounterStartRemaining - shift.RemainingSeconds);
                maximumEncounterTimerDecrease = Mathf.Max(
                    maximumEncounterTimerDecrease,
                    decrease);
            }

            encounterWasActive = encounterActive;
        }

        public bool TryStartScenario(BusinessPlaytestScenario scenario)
        {
            if (!ready || scenarioStarted || shift == null || shift.IsActive)
                return false;

            runtimeSettings.requiredActions = new List<BusinessRequiredActionRule>();
            runtimeSettings.randomEncounters = new List<BusinessRandomEncounterEntry>();
            ActiveScenario = scenario;
            maximumEncounterTimerDecrease = 0f;
            encounterCount = 0;
            appearances.Clear();

            switch (scenario)
            {
                case BusinessPlaytestScenario.NormalShift:
                    runtimeSettings.shiftDurationSeconds = 90f;
                    break;
                case BusinessPlaytestScenario.ShortTimer:
                    runtimeSettings.shiftDurationSeconds = 20f;
                    break;
                case BusinessPlaytestScenario.RequiredCustomer:
                    runtimeSettings.shiftDurationSeconds = 45f;
                    runtimeSettings.requiredActions.Add(CreateRequiredCustomerRule(
                        "playtest_required_customer",
                        runtimeVisits[0],
                        100,
                        BusinessRequiredActionTiming.BeforeFirstCustomer));
                    break;
                case BusinessPlaytestScenario.EncounterTimerRuns:
                    runtimeSettings.shiftDurationSeconds = 3f;
                    if (!TryAddEncounterRule(
                            "playtest_encounter_timer_runs",
                            SimpleEncounterId,
                            100,
                            BusinessRequiredActionTiming.BeforeFirstCustomer))
                    {
                        return false;
                    }
                    break;
                case BusinessPlaytestScenario.RequiredQueue:
                    runtimeSettings.shiftDurationSeconds = 30f;
                    if (!TryAddEncounterRule(
                            "playtest_queue_encounter",
                            SimpleEncounterId,
                            200,
                            BusinessRequiredActionTiming.BeforeFirstCustomer))
                    {
                        return false;
                    }
                    runtimeSettings.requiredActions.Add(CreateRequiredCustomerRule(
                        "playtest_queue_customer",
                        runtimeVisits[1],
                        100,
                        BusinessRequiredActionTiming.BeforeFirstCustomer));
                    runtimeSettings.requiredActions.Add(CreateRequiredCustomerRule(
                        "playtest_after_timer_customer",
                        runtimeVisits[2],
                        50,
                        BusinessRequiredActionTiming.AfterTimer));
                    break;
                case BusinessPlaytestScenario.EncounterWithCrafting:
                    runtimeSettings.shiftDurationSeconds = 90f;
                    if (!TryAddEncounterRule(
                            "playtest_crafting_encounter",
                            CraftingEncounterId,
                            100,
                            BusinessRequiredActionTiming.BeforeFirstCustomer))
                    {
                        return false;
                    }
                    break;
                case BusinessPlaytestScenario.Settlement:
                    runtimeSettings.shiftDurationSeconds = 15f;
                    break;
                case BusinessPlaytestScenario.RandomEncounterPool:
                    runtimeSettings.shiftDurationSeconds = 90f;
                    if (!TryAddRandomEncounter(SimpleEncounterId, 100000f))
                        return false;
                    break;
                case BusinessPlaytestScenario.EncounterAfterFirstOrder:
                    runtimeSettings.shiftDurationSeconds = 90f;
                    runtimeSettings.requiredActions.Add(CreateRequiredCustomerRule(
                        "playtest_customer_before_random_encounter",
                        runtimeVisits[0],
                        100,
                        BusinessRequiredActionTiming.BeforeFirstCustomer));
                    for (int i = 0; i < runtimeVisits.Count; i++)
                        runtimeVisits[i].weight = 0f;
                    if (!TryAddRandomEncounter(SimpleEncounterId, 1f))
                        return false;
                    break;
            }

            scenarioStarted = true;
            status = $"Scenario started: {scenario}";
            flow.StartBusinessSequence();
            return shift.IsActive;
        }

        private bool CreateRuntimeSettings()
        {
            BusinessOrderFlowSettings source = sourceSettings != null
                ? sourceSettings
                : Resources.Load<BusinessOrderFlowSettings>(SettingsResourcePath);
            if (source == null)
            {
                status = "ERROR: BusinessOrderFlowSettings was not found.";
                return false;
            }

            CustomerVisitData template = templateVisit != null
                ? templateVisit
                : FindUsableTemplate(source.customerVisitDatabase);
            if (template == null)
            {
                status = "ERROR: No usable customer visit template was found.";
                return false;
            }

            runtimeSettings = Instantiate(source);
            runtimeSettings.name = "BusinessOrderFlowSettings_IntegrationPlaytest";
            runtimeSettings.hideFlags = HideFlags.DontSave;
            runtimeSettings.autoStart = false;
            runtimeSettings.requiredActions = new List<BusinessRequiredActionRule>();
            runtimeSettings.randomEncounters = new List<BusinessRandomEncounterEntry>();

            runtimeDatabase = ScriptableObject.CreateInstance<CustomerVisitDatabase>();
            runtimeDatabase.name = "CustomerVisitDatabase_IntegrationPlaytest";
            runtimeDatabase.hideFlags = HideFlags.DontSave;

            int count = Mathf.Max(3, customerCount);
            for (int i = 0; i < count; i++)
            {
                CustomerVisitData visit = Instantiate(template);
                visit.name = $"IntegrationVisit_{i:00}";
                visit.hideFlags = HideFlags.DontSave;
                visit.visitKey = $"integration_visit_{i:00}";
                visit.reappearanceGroupKey = visit.visitKey;
                visit.weight = 1f + i % 4;
                visit.initiallyAvailable = true;
                visit.availabilityTransitions = new List<CustomerAvailabilityTransition>();
                visit.condition = new EpisodeTriggerCondition();
                visit.maxDay = 0;
                runtimeVisits.Add(visit);
                runtimeDatabase.visits.Add(visit);
            }

            runtimeSettings.customerVisitDatabase = runtimeDatabase;
            return true;
        }

        private bool TryAddEncounterRule(
            string ruleId,
            string episodeId,
            int priority,
            BusinessRequiredActionTiming timing)
        {
            if (isolation == null || !isolation.PrepareIncompleteEpisode(episodeId))
            {
                status = "ERROR: Encounter progress could not be isolated: " + episodeId;
                return false;
            }

            EpisodeData episode = FindEpisode(episodeId);
            if (episode == null || episode.episodeType != EpisodeType.Encounter)
            {
                status = "ERROR: Encounter episode was not found: " + episodeId;
                return false;
            }

            runtimeSettings.requiredActions.Add(new BusinessRequiredActionRule
            {
                ruleId = ruleId,
                actionType = BusinessRequiredActionType.EncounterEpisode,
                priority = priority,
                timing = timing,
                condition = new EpisodeTriggerCondition(),
                encounterEpisode = episode
            });
            return true;
        }

        private bool TryAddRandomEncounter(string episodeId, float weight)
        {
            if (isolation == null || !isolation.PrepareIncompleteEpisode(episodeId))
            {
                status = "ERROR: Random encounter progress could not be isolated: " + episodeId;
                return false;
            }

            EpisodeData episode = FindEpisode(episodeId);
            if (episode == null || episode.episodeType != EpisodeType.Encounter)
            {
                status = "ERROR: Random encounter episode was not found: " + episodeId;
                return false;
            }

            runtimeSettings.randomEncounters.Add(new BusinessRandomEncounterEntry
            {
                episode = episode,
                weight = Mathf.Max(0f, weight)
            });
            return true;
        }

        private static BusinessRequiredActionRule CreateRequiredCustomerRule(
            string ruleId,
            CustomerVisitData visit,
            int priority,
            BusinessRequiredActionTiming timing)
        {
            return new BusinessRequiredActionRule
            {
                ruleId = ruleId,
                actionType = BusinessRequiredActionType.CustomerVisit,
                priority = priority,
                timing = timing,
                condition = new EpisodeTriggerCondition(),
                customerVisit = visit
            };
        }

        private void HandleShiftStateChanged(
            BusinessShiftState previous,
            BusinessShiftState next)
        {
            if (next == BusinessShiftState.EncounterActive)
            {
                encounterStartRemaining = shift.RemainingSeconds;
                encounterCount++;
                encounterWasActive = true;
            }
            else if (encounterWasActive)
            {
                float decrease = Mathf.Max(0f, encounterStartRemaining - shift.RemainingSeconds);
                maximumEncounterTimerDecrease = Mathf.Max(
                    maximumEncounterTimerDecrease,
                    decrease);
                encounterWasActive = false;
            }

            status = $"Shift: {previous} -> {next}";
        }

        private void HandleOrderStateChanged(
            BusinessOrderSessionState previous,
            BusinessOrderSessionState next)
        {
            status = $"Order: {previous} -> {next}";
        }

        private void HandleCustomerVisitStarted(CustomerVisitData visit)
        {
            if (visit == null)
                return;

            appearances.TryGetValue(visit.visitKey, out int count);
            appearances[visit.visitKey] = count + 1;
            status = $"Visit: {visit.visitKey}";
        }

        private void TogglePause()
        {
            manualPause = !manualPause;
            shift.SetPaused(manualPause);
            status = manualPause ? "Manual timer pause ON" : "Manual timer pause OFF";
        }

        private void OnGUI()
        {
            EnsureStyles();
            BuildPanelText();
            GUI.Box(new Rect(12f, 12f, 760f, 670f), panel.ToString(), panelStyle);

            if (!ready || scenarioStarted)
            {
                if (scenarioStarted && shift != null
                    && GUI.Button(new Rect(552f, 622f, 200f, 42f),
                        manualPause ? "Resume timer (P)" : "Pause timer (P)",
                        buttonStyle))
                {
                    TogglePause();
                }
                return;
            }

            DrawScenarioButton(32f, 232f, "1. Normal shift (90s)",
                BusinessPlaytestScenario.NormalShift);
            DrawScenarioButton(392f, 232f, "2. Short timer (20s)",
                BusinessPlaytestScenario.ShortTimer);
            DrawScenarioButton(32f, 284f, "3. Required customer",
                BusinessPlaytestScenario.RequiredCustomer);
            DrawScenarioButton(392f, 284f, "4. Encounter timer runs (3s)",
                BusinessPlaytestScenario.EncounterTimerRuns);
            DrawScenarioButton(32f, 336f, "5. Required action queue",
                BusinessPlaytestScenario.RequiredQueue);
            DrawScenarioButton(392f, 336f, "6. Encounter with crafting",
                BusinessPlaytestScenario.EncounterWithCrafting);
            DrawScenarioButton(32f, 388f, "7. Settlement (15s)",
                BusinessPlaytestScenario.Settlement);
            DrawScenarioButton(392f, 388f, "8. Random encounter pool",
                BusinessPlaytestScenario.RandomEncounterPool);
            DrawScenarioButton(32f, 440f, "9. Encounter after first order",
                BusinessPlaytestScenario.EncounterAfterFirstOrder);
        }

        private void DrawScenarioButton(
            float x,
            float y,
            string label,
            BusinessPlaytestScenario scenario)
        {
            if (GUI.Button(new Rect(x, y, 340f, 42f), label, buttonStyle))
                TryStartScenario(scenario);
        }

        private void BuildPanelText()
        {
            panel.Clear();
            panel.AppendLine("BUSINESS FLOW INTEGRATION PLAYTEST");
            panel.AppendLine("Runtime-only data | Disk saves disabled | Progress restored on exit");
            panel.Append("Ready: ").Append(ready)
                .Append(" | Scenario: ").Append(scenarioStarted ? ActiveScenario : "not selected")
                .AppendLine();
            panel.Append("Status: ").AppendLine(status);
            panel.AppendLine();

            if (!scenarioStarted)
            {
                panel.AppendLine("Choose one scenario below. Stop Play to select another scenario.");
                panel.AppendLine("Recommended order: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 8 -> 9");
                panel.AppendLine();
                panel.AppendLine("Controls after starting:");
                panel.AppendLine("  Dialogue: click / Space    Crafting: existing mouse controls");
                panel.AppendLine("  Timer pause/resume: P      Submit: drag serving glass upward");
                return;
            }

            GameProgress progress = GameProgress.Instance;
            GameModeManager mode = FindInScene<GameModeManager>(gameObject.scene);
            panel.Append("Shift state: ").Append(shift != null ? shift.State.ToString() : "-")
                .Append(" | Order state: ").Append(orderSession != null ? orderSession.State.ToString() : "-")
                .Append(" | Mode: ").Append(mode != null ? mode.CurrentMode.ToString() : "-")
                .AppendLine();
            panel.Append("Remaining: ").Append(shift != null
                    ? shift.RemainingSeconds.ToString("0.00")
                    : "-")
                .Append("s | Active business: ").Append(shift != null
                    ? shift.ActiveBusinessSeconds.ToString("0.00")
                    : "-")
                .AppendLine("s");
            panel.Append("Customer pool: ").Append(shift?.FrozenCustomerPoolCount ?? 0)
                .Append(" | Encounter pool: ").Append(shift?.FrozenEncounterPoolCount ?? 0)
                .Append(" | Started: ").Append(shift?.TotalStartedCustomerCount ?? 0)
                .Append(" | Completed: ").Append(shift?.CompletedOrderCount ?? 0)
                .Append(" | Spawning stopped: ")
                .Append(shift?.IsRandomCustomerSpawningStopped ?? false)
                .AppendLine();
            panel.Append("Last visit: ").Append(shift?.LastSelectedVisitKey ?? "-")
                .Append(" | Encounter count: ").Append(encounterCount)
                .Append(" | Max encounter timer decrease: ")
                .Append(maximumEncounterTimerDecrease.ToString("0.000"))
                .AppendLine("s");
            panel.Append("Episode: ")
                .Append(EpisodeManager.Instance?.CurrentPlayingEpisodeID ?? "-")
                .Append(" | Business encounter: ")
                .Append(EpisodeManager.Instance != null
                    && EpisodeManager.Instance.IsBusinessEncounterActive)
                .AppendLine();
            panel.AppendLine();
            panel.Append("Money: ").Append(progress?.CurrentMoney ?? 0)
                .Append(" (start ").Append(startMoney).Append(")")
                .Append(" | Reputation: ").Append(progress?.Reputation ?? 0)
                .Append(" (start ").Append(startReputation).Append(")")
                .AppendLine();
            panel.Append("Recorded sales: ").Append(progress?.DayDrinkSalesCount ?? 0)
                .Append(" | Base: ").Append(progress?.DayDrinkBaseRevenue ?? 0)
                .Append(" | Tip: ").Append(progress?.DayDrinkTipRevenue ?? 0)
                .Append(" | Revenue pending settlement: ")
                .Append(progress?.DayDrinkRevenue ?? 0)
                .Append(" | Total pending: ").Append(progress?.DayTotalIncome ?? 0)
                .AppendLine();
            panel.AppendLine();
            AppendScenarioChecklist();
            panel.AppendLine();
            panel.AppendLine("P toggles a manual timer pause. Stop Play to restore the snapshot.");
        }

        private void AppendScenarioChecklist()
        {
            panel.AppendLine("Checklist:");
            switch (ActiveScenario)
            {
                case BusinessPlaytestScenario.NormalShift:
                    panel.AppendLine("  - Complete several orders; verify weighted visits and the recent-two rule.");
                    panel.AppendLine("  - Verify the timer keeps decreasing during order/crafting.");
                    break;
                case BusinessPlaytestScenario.ShortTimer:
                    panel.AppendLine("  - Let the timer expire during an order.");
                    panel.AppendLine("  - Current order must finish; no new normal visit may start.");
                    break;
                case BusinessPlaytestScenario.RequiredCustomer:
                    panel.AppendLine("  - integration_visit_00 must be the first visit.");
                    break;
                case BusinessPlaytestScenario.EncounterTimerRuns:
                    panel.AppendLine("  - Timer must keep decreasing during the encounter.");
                    panel.AppendLine("  - At 00:00 the encounter must remain active until completed.");
                    panel.AppendLine("  - Completing it must return to OrderMode, then settle.");
                    break;
                case BusinessPlaytestScenario.RequiredQueue:
                    panel.AppendLine("  - Encounter first, required customer second (priority order).");
                    panel.AppendLine("  - After timer expiry, integration_visit_02 must run before settlement.");
                    break;
                case BusinessPlaytestScenario.EncounterWithCrafting:
                    panel.AppendLine("  - During encounter crafting, the business timer must keep decreasing.");
                    panel.AppendLine("  - Reaching 00:00 must not cancel crafting or the encounter.");
                    panel.AppendLine("  - Crafting completion must return to episode, then to business.");
                    break;
                case BusinessPlaytestScenario.Settlement:
                    panel.AppendLine("  - Complete at least one sale before the 15-second timer expires.");
                    panel.AppendLine("  - Money must not rise on sale; it rises once on settlement.");
                    break;
                case BusinessPlaytestScenario.RandomEncounterPool:
                    panel.AppendLine("  - StrangeCoin_0 must be selected from the weighted random pool.");
                    panel.AppendLine("  - The same encounter ID must not be selected twice in this shift.");
                    panel.AppendLine("  - After completion it must remain excluded on later days.");
                    break;
                case BusinessPlaytestScenario.EncounterAfterFirstOrder:
                    panel.AppendLine("  - integration_visit_00 must run first as a required customer.");
                    panel.AppendLine("  - After that order completes, StrangeCoin_0 must be second.");
                    panel.AppendLine("  - The encounter must run in EpisodeMode while the timer decreases.");
                    break;
            }
        }

        private void EnsureStyles()
        {
            if (panelStyle != null)
                return;

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 16,
                padding = new RectOffset(16, 16, 14, 14),
                wordWrap = true
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private void OnDestroy()
        {
            if (shift != null)
            {
                shift.StateChanged -= HandleShiftStateChanged;
                shift.CustomerVisitStarted -= HandleCustomerVisitStarted;
            }
            if (orderSession != null)
                orderSession.StateChanged -= HandleOrderStateChanged;

            for (int i = 0; i < runtimeVisits.Count; i++)
            {
                if (runtimeVisits[i] != null)
                    Destroy(runtimeVisits[i]);
            }
            if (runtimeDatabase != null)
                Destroy(runtimeDatabase);
            if (runtimeSettings != null)
                Destroy(runtimeSettings);
        }

        private static CustomerVisitData FindUsableTemplate(CustomerVisitDatabase database)
        {
            if (database?.visits == null)
                return null;

            for (int i = 0; i < database.visits.Count; i++)
            {
                CustomerVisitData visit = database.visits[i];
                if (visit != null
                    && visit.members != null
                    && visit.members.Count > 0
                    && visit.orders != null
                    && visit.orders.Count > 0)
                {
                    return visit;
                }
            }

            return null;
        }

        private static EpisodeData FindEpisode(string episodeId)
        {
            EpisodeData[] episodes = Resources.LoadAll<EpisodeData>("EpisodeData");
            for (int i = 0; i < episodes.Length; i++)
            {
                if (episodes[i] != null && episodes[i].episodeId == episodeId)
                    return episodes[i];
            }
            return null;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid())
                return null;

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
