using System.Collections;
using System.Collections.Generic;
using System.Text;
using Slainte.Bartending;
using Slainte.Content;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Business
{
    public enum BusinessPlaytestScenario
    {
        NormalDay,
        ScheduledEncounter,
        CandidatePriority,
        ConditionFallback,
        EncounterWithCrafting,
        ProductionSchedule
    }

    // BusinessScene을 실제 GameProgress/설정과 완전히 격리된 상태로 재현해, 사전 정의 시나리오
    // (랜덤 손님만 / 슬롯 배정 에피소드 / 후보 우선순위 / 조건 미충족 폴백 등)를 OnGUI 패널에서 골라
    // 실행하는 QA용 통합 플레이테스트 진입점. 시나리오 일정은 에피소드 복제본으로 만든 메모리 일정표를
    // 주입하고, PlaytestProgressIsolation이 진행 상태 스냅샷을 잡아두므로 Play 종료 시 원래 세이브로 복원된다.
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class BusinessIntegrationPlaytestBootstrap : MonoBehaviour
    {
        private const string SettingsResourcePath = BusinessOrderFlowSettings.ResourcePath;
        public const string ScheduledEncounterId = "StrangeCoin_0";
        public const string BlockedEncounterId = "playtest_blocked_encounter";
        public const int ScheduledEncounterSlot = 3;
        public const int PriorityTestSlot = 2;
        private const string NeverSetFlag = "playtest_flag_never_set";

        [Header("Runtime Pool")]
        [SerializeField, Min(1)] private int customerCount = 12;
        [SerializeField] private CustomerVisitData templateVisit;
        [SerializeField] private BusinessOrderFlowSettings sourceSettings;

        [Header("Production Schedule")]
        [Tooltip("ProductionSchedule 시나리오에서 실제 일정표를 확인할 날짜입니다.")]
        [SerializeField, Min(1)] private int productionDay = 1;

        private readonly List<CustomerVisitData> runtimeVisits = new();
        private readonly List<EpisodeData> runtimeEpisodes = new();
        private readonly List<string> slotLog = new();
        private readonly StringBuilder panel = new(1600);

        private PlaytestProgressIsolation isolation;
        private BusinessOrderFlowSettings productionSettings;
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
        private int startMoney;
        private int startReputation;
        private string status = "Initializing isolated playtest...";

        public bool IsReady => ready;
        public bool IsScenarioStarted => scenarioStarted;
        public BusinessPlaytestScenario ActiveScenario { get; private set; }
        public IReadOnlyList<string> SlotLog => slotLog;

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
            shift.SlotStarted += HandleSlotStarted;
            orderSession.StateChanged += HandleOrderStateChanged;

            GameProgress progress = GameProgress.Instance;
            startMoney = progress != null ? progress.CurrentMoney : 0;
            startReputation = progress != null ? progress.Reputation : 0;
            ready = true;
            status = "Choose a scenario. Original progress and save data are isolated.";
        }

        private void Update()
        {
            if (scenarioStarted && shift != null && Input.GetKeyDown(KeyCode.P))
                TogglePause();
        }

        public bool TryStartScenario(BusinessPlaytestScenario scenario)
        {
            if (!ready || scenarioStarted || shift == null || shift.IsActive)
                return false;

            GameProgress progress = GameProgress.Instance;
            if (progress == null)
            {
                status = "ERROR: GameProgress was not found.";
                return false;
            }

            ActiveScenario = scenario;
            slotLog.Clear();
            runtimeSettings.customerVisitDatabase = runtimeDatabase;

            DayScheduleIndex schedule = new();
            switch (scenario)
            {
                case BusinessPlaytestScenario.NormalDay:
                    break;
                case BusinessPlaytestScenario.ScheduledEncounter:
                    if (!TryScheduleEncounter(ScheduledEncounterSlot, 0, progress))
                        return false;
                    break;
                case BusinessPlaytestScenario.CandidatePriority:
                    // 우선순위가 높은 후보는 등장 조건이 절대 충족되지 않으므로 낮은 후보가 실행되어야 한다.
                    ScheduleBlockedEncounter(PriorityTestSlot, 10, progress);
                    if (!TryScheduleEncounter(PriorityTestSlot, 0, progress))
                        return false;
                    break;
                case BusinessPlaytestScenario.ConditionFallback:
                    ScheduleBlockedEncounter(1, 0, progress);
                    break;
                case BusinessPlaytestScenario.EncounterWithCrafting:
                    if (!TryScheduleEncounter(1, 0, progress))
                        return false;
                    break;
                case BusinessPlaytestScenario.ProductionSchedule:
                    if (!TryConfigureProductionSchedule(progress))
                        return false;
                    break;
            }

            if (scenario != BusinessPlaytestScenario.ProductionSchedule)
            {
                schedule.Rebuild(runtimeEpisodes);
                shift.TrySetScheduleSource(schedule);
            }

            scenarioStarted = true;
            status = $"Scenario started: {scenario}";
            flow.StartBusinessSequence();
            return shift.IsActive;
        }

        // 실제 BusinessOrderFlowSettings/CustomerVisitDatabase를 복제(Instantiate, DontSave)해
        // 시나리오별로 자유롭게 수정 가능한 격리 인스턴스를 만든다 — 원본 프로덕션 에셋은
        // 절대 건드리지 않는다.
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

            productionSettings = source;

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

        // 실제 에피소드를 복제해 오늘의 지정 슬롯에 배정한다. 등장 조건은 비워 슬롯 배정 자체만 검증한다.
        private bool TryScheduleEncounter(int slot, int priority, GameProgress progress)
        {
            if (isolation == null || !isolation.PrepareIncompleteEpisode(ScheduledEncounterId))
            {
                status = "ERROR: Encounter progress could not be isolated: " + ScheduledEncounterId;
                return false;
            }

            EpisodeData source = FindEpisode(ScheduledEncounterId);
            if (source == null)
            {
                status = "ERROR: Encounter episode was not found: " + ScheduledEncounterId;
                return false;
            }

            AddRuntimeEpisode(source, ScheduledEncounterId, slot, priority, progress,
                new EpisodeTriggerCondition());
            return true;
        }

        private void ScheduleBlockedEncounter(int slot, int priority, GameProgress progress)
        {
            EpisodeData source = FindEpisode(ScheduledEncounterId);
            EpisodeTriggerCondition blocked = new();
            blocked.requiredFlags.Add(NeverSetFlag);
            AddRuntimeEpisode(source, BlockedEncounterId, slot, priority, progress, blocked);
        }

        private void AddRuntimeEpisode(
            EpisodeData source,
            string episodeId,
            int slot,
            int priority,
            GameProgress progress,
            EpisodeTriggerCondition condition)
        {
            EpisodeData episode = source != null
                ? Instantiate(source)
                : ScriptableObject.CreateInstance<EpisodeData>();
            episode.name = "PlaytestEpisode_" + episodeId;
            episode.hideFlags = HideFlags.DontSave;
            episode.episodeId = episodeId;
            episode.chapterId = string.Empty;
            episode.scheduledDay = progress.CurrentDay;
            episode.scheduledSlot = slot;
            episode.slotPriority = priority;
            episode.triggerCondition = condition;
            runtimeEpisodes.Add(episode);
        }

        // 실제 일정표(EpisodeManager)와 실제 손님 풀을 그대로 사용해, 지정한 날의 배정 결과를 확인한다.
        private bool TryConfigureProductionSchedule(GameProgress progress)
        {
            if (productionSettings == null || productionSettings.customerVisitDatabase == null)
            {
                status = "ERROR: Production business settings are incomplete.";
                return false;
            }

            progress.SetCurrentDay(productionDay);
            runtimeSettings.customersPerDay = productionSettings.customersPerDay;
            runtimeSettings.customerVisitDatabase = productionSettings.customerVisitDatabase;
            status = $"Production schedule loaded; current progress copied to Day {productionDay}.";
            return true;
        }

        private void HandleSlotStarted(int slot, EpisodeData episode)
        {
            string entry = episode != null
                ? $"{slot}: episode {episode.episodeId}"
                : $"{slot}: customer {shift.LastSelectedVisitKey}";
            slotLog.Add(entry);
            status = "Slot " + entry;
        }

        private void HandleShiftStateChanged(
            BusinessShiftState previous,
            BusinessShiftState next)
        {
            status = $"Shift: {previous} -> {next}";
        }

        private void HandleOrderStateChanged(
            BusinessOrderSessionState previous,
            BusinessOrderSessionState next)
        {
            status = $"Order: {previous} -> {next}";
        }

        private void TogglePause()
        {
            manualPause = !manualPause;
            shift.SetPaused(manualPause);
            status = manualPause ? "Next slot hold ON" : "Next slot hold OFF";
        }

        private void OnGUI()
        {
            EnsureStyles();
            BuildPanelText();
            GUI.Box(new Rect(12f, 12f, 760f, 640f), panel.ToString(), panelStyle);

            if (!ready || scenarioStarted)
            {
                if (scenarioStarted && shift != null
                    && GUI.Button(new Rect(552f, 596f, 200f, 42f),
                        manualPause ? "Release hold (P)" : "Hold next slot (P)",
                        buttonStyle))
                {
                    TogglePause();
                }
                return;
            }

            DrawScenarioButton(32f, 232f, "1. Normal day (random only)",
                BusinessPlaytestScenario.NormalDay);
            DrawScenarioButton(392f, 232f, $"2. Encounter at slot {ScheduledEncounterSlot}",
                BusinessPlaytestScenario.ScheduledEncounter);
            DrawScenarioButton(32f, 284f, $"3. Candidate priority (slot {PriorityTestSlot})",
                BusinessPlaytestScenario.CandidatePriority);
            DrawScenarioButton(392f, 284f, "4. Condition fallback (slot 1)",
                BusinessPlaytestScenario.ConditionFallback);
            DrawScenarioButton(32f, 336f, "5. Encounter with crafting",
                BusinessPlaytestScenario.EncounterWithCrafting);
            DrawScenarioButton(392f, 336f, $"6. Production schedule (Day {productionDay})",
                BusinessPlaytestScenario.ProductionSchedule);
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
                panel.AppendLine();
                panel.AppendLine("Controls after starting:");
                panel.AppendLine("  Dialogue: click / Space    Crafting: existing mouse controls");
                panel.AppendLine("  Hold next slot: P          Submit: drag serving glass upward");
                return;
            }

            GameProgress progress = GameProgress.Instance;
            GameModeManager mode = FindInScene<GameModeManager>(gameObject.scene);
            panel.Append("Shift state: ").Append(shift != null ? shift.State.ToString() : "-")
                .Append(" | Order state: ").Append(orderSession != null ? orderSession.State.ToString() : "-")
                .Append(" | Mode: ").Append(mode != null ? mode.CurrentMode.ToString() : "-")
                .AppendLine();
            panel.Append("Day: ").Append(progress?.CurrentDay ?? 0)
                .Append(" | Slot: ").Append(shift?.CurrentSlot ?? 0)
                .Append("/").Append(shift?.SlotsPerDay ?? 0)
                .Append(" | Customer pool: ").Append(shift?.FrozenCustomerPoolCount ?? 0)
                .AppendLine();
            panel.Append("Started customers: ").Append(shift?.TotalStartedCustomerCount ?? 0)
                .Append(" | Completed orders: ").Append(shift?.CompletedOrderCount ?? 0)
                .Append(" | Started encounters: ").Append(shift?.TotalStartedEncounterCount ?? 0)
                .AppendLine();
            panel.Append("Episode: ")
                .Append(EpisodeManager.Instance?.CurrentPlayingEpisodeID ?? "-")
                .Append(" | Business encounter: ")
                .Append(EpisodeManager.Instance != null
                    && EpisodeManager.Instance.IsBusinessEncounterActive)
                .AppendLine();
            panel.Append("Slots: ").AppendLine(slotLog.Count > 0 ? string.Join(" / ", slotLog) : "-");
            panel.AppendLine();
            panel.Append("Money: ").Append(progress?.CurrentMoney ?? 0)
                .Append(" (start ").Append(startMoney).Append(")")
                .Append(" | Reputation: ").Append(progress?.Reputation ?? 0)
                .Append(" (start ").Append(startReputation).Append(")")
                .AppendLine();
            panel.Append("Recorded sales: ").Append(progress?.DayDrinkSalesCount ?? 0)
                .Append(" | Base: ").Append(progress?.DayDrinkBaseRevenue ?? 0)
                .Append(" | Tip: ").Append(progress?.DayDrinkTipRevenue ?? 0)
                .Append(" | Total pending: ").Append(progress?.DayTotalIncome ?? 0)
                .AppendLine();
            panel.AppendLine();
            AppendScenarioChecklist();
        }

        private void AppendScenarioChecklist()
        {
            panel.AppendLine("Checklist:");
            switch (ActiveScenario)
            {
                case BusinessPlaytestScenario.NormalDay:
                    panel.AppendLine("  - Exactly SlotsPerDay random customers appear, then settlement starts.");
                    panel.AppendLine("  - The recent-two rule applies while other candidates remain.");
                    break;
                case BusinessPlaytestScenario.ScheduledEncounter:
                    panel.AppendLine($"  - Slots 1-{ScheduledEncounterSlot - 1} are random customers.");
                    panel.AppendLine($"  - {ScheduledEncounterId} starts in slot {ScheduledEncounterSlot} (EpisodeMode).");
                    panel.AppendLine("  - The remaining slots return to random customers.");
                    break;
                case BusinessPlaytestScenario.CandidatePriority:
                    panel.AppendLine($"  - Slot {PriorityTestSlot}: {BlockedEncounterId} (priority 10) is skipped.");
                    panel.AppendLine($"  - {ScheduledEncounterId} (priority 0) starts in the same slot.");
                    break;
                case BusinessPlaytestScenario.ConditionFallback:
                    panel.AppendLine($"  - Slot 1 candidate {BlockedEncounterId} fails its condition.");
                    panel.AppendLine("  - Slot 1 is filled by a random customer instead.");
                    break;
                case BusinessPlaytestScenario.EncounterWithCrafting:
                    panel.AppendLine("  - Crafting inside the encounter returns to the episode, then to business.");
                    panel.AppendLine("  - The episode crafting order is not counted as a sale.");
                    break;
                case BusinessPlaytestScenario.ProductionSchedule:
                    panel.AppendLine("  - Uses production episodes and the real eligible customer pool.");
                    panel.AppendLine("  - Current progress is copied, moved to the chosen day, and restored on exit.");
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
                shift.SlotStarted -= HandleSlotStarted;
            }
            if (orderSession != null)
                orderSession.StateChanged -= HandleOrderStateChanged;

            for (int i = 0; i < runtimeVisits.Count; i++)
            {
                if (runtimeVisits[i] != null)
                    Destroy(runtimeVisits[i]);
            }
            for (int i = 0; i < runtimeEpisodes.Count; i++)
            {
                if (runtimeEpisodes[i] != null)
                    Destroy(runtimeEpisodes[i]);
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
            EpisodeData[] episodes = Resources.LoadAll<EpisodeData>(
                ProjectResourcePaths.NarrativeEpisodes);
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
