using System.Collections;
using System.Collections.Generic;
using System.Text;
using Slainte.Bartending;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Business
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class BusinessCustomerPoolStressBootstrap : MonoBehaviour
    {
        private const string SettingsResourcePath = BusinessOrderFlowSettings.ResourcePath;

        [Header("Stress Pool")]
        [SerializeField, Min(1)] private int customerCount = 32;
        [SerializeField, Min(1f)] private float shiftDurationSeconds = 600f;
        [SerializeField] private bool suppressRequiredActions = true;

        [Header("Optional Overrides")]
        [SerializeField] private BusinessOrderFlowSettings sourceSettings;
        [SerializeField] private CustomerVisitData templateVisit;

        private readonly List<CustomerVisitData> runtimeVisits = new();
        private readonly Dictionary<string, int> appearanceCounts = new();
        private readonly StringBuilder panelText = new(1024);

        private BusinessOrderFlowSettings runtimeSettings;
        private CustomerVisitDatabase runtimeDatabase;
        private PlaytestProgressIsolation progressIsolation;
        private BusinessFlowBootstrap flow;
        private BusinessShiftController shift;
        private GUIStyle panelStyle;
        private bool configurationFailed;
        private string failureReason = string.Empty;

        private void Awake()
        {
            progressIsolation = PlaytestProgressIsolation.Attach(gameObject);
            BusinessBartendingBootstrap.EnsureInstalledForScene(gameObject.scene);

            flow = FindInScene<BusinessFlowBootstrap>(gameObject.scene);
            if (flow == null)
            {
                Fail("BusinessFlowBootstrap을 찾지 못했습니다.");
                return;
            }

            if (!TryCreateRuntimeSettings())
                return;

            if (!flow.TryOverrideSettingsBeforeInitialization(runtimeSettings))
            {
                Fail("영업 초기화가 이미 시작되어 스트레스 설정을 주입하지 못했습니다.");
                return;
            }

            Debug.Log(
                $"[CustomerPoolStress] 메모리 임시 손님 {runtimeVisits.Count}명을 주입했습니다. "
                + "원본 손님 데이터와 영업 설정 에셋은 변경되지 않습니다.");
        }

        private IEnumerator Start()
        {
            if (configurationFailed)
                yield break;

            const int maxWaitFrames = 300;
            for (int i = 0; i < maxWaitFrames && shift == null; i++)
            {
                shift = flow != null ? flow.ShiftController : null;
                if (shift == null)
                    yield return null;
            }

            if (shift == null)
            {
                Fail("BusinessShiftController 초기화를 기다리다 시간이 초과되었습니다.");
                yield break;
            }

            shift.CustomerVisitStarted += HandleCustomerVisitStarted;
            while (progressIsolation != null && !progressIsolation.IsReady)
                yield return null;
            flow.StartBusinessSequence();
        }

        private bool TryCreateRuntimeSettings()
        {
            BusinessOrderFlowSettings source = sourceSettings != null
                ? sourceSettings
                : Resources.Load<BusinessOrderFlowSettings>(SettingsResourcePath);
            if (source == null)
            {
                Fail("원본 BusinessOrderFlowSettings를 불러오지 못했습니다.");
                return false;
            }

            CustomerVisitData template = templateVisit != null
                ? templateVisit
                : FindUsableTemplate(source.customerVisitDatabase);
            if (template == null)
            {
                Fail("구성원과 주문이 모두 있는 손님 방문 템플릿이 없습니다.");
                return false;
            }

            runtimeSettings = Instantiate(source);
            runtimeSettings.name = "BusinessOrderFlowSettings_StressRuntime";
            runtimeSettings.hideFlags = HideFlags.DontSave;
            runtimeSettings.autoStart = false;
            runtimeSettings.shiftDurationSeconds = Mathf.Max(1f, shiftDurationSeconds);
            runtimeSettings.randomEncounters = new List<BusinessRandomEncounterEntry>();
            if (suppressRequiredActions)
                runtimeSettings.requiredActions = new List<BusinessRequiredActionRule>();

            runtimeDatabase = ScriptableObject.CreateInstance<CustomerVisitDatabase>();
            runtimeDatabase.name = "CustomerVisitDatabase_StressRuntime";
            runtimeDatabase.hideFlags = HideFlags.DontSave;

            int count = Mathf.Max(1, customerCount);
            for (int i = 0; i < count; i++)
            {
                CustomerVisitData visit = Instantiate(template);
                visit.name = $"CustomerVisit_Stress_{i:00}";
                visit.hideFlags = HideFlags.DontSave;
                visit.visitKey = $"stress_visit_{i:00}";
                visit.reappearanceGroupKey = visit.visitKey;
                visit.weight = 1f + i % 5;
                visit.initiallyAvailable = true;
                visit.availabilityTransitions = new List<CustomerAvailabilityTransition>();
                visit.maxDay = 0;
                visit.condition = new EpisodeTriggerCondition();
                visit.tags ??= new List<string>();
                if (!visit.tags.Contains("stress-test"))
                    visit.tags.Add("stress-test");

                runtimeVisits.Add(visit);
                runtimeDatabase.visits.Add(visit);
            }

            runtimeSettings.customerVisitDatabase = runtimeDatabase;
            return true;
        }

        private void HandleCustomerVisitStarted(CustomerVisitData visit)
        {
            if (visit == null || string.IsNullOrWhiteSpace(visit.visitKey))
                return;

            appearanceCounts.TryGetValue(visit.visitKey, out int count);
            appearanceCounts[visit.visitKey] = count + 1;

            Debug.Log(
                $"[CustomerPoolStress] 등장 #{shift?.TotalStartedCustomerCount ?? 0}: "
                + $"{visit.visitKey}, weight={visit.weight:0.##}");
        }

        private void OnGUI()
        {
            EnsurePanelStyle();
            BuildPanelText();
            GUI.Box(new Rect(12f, 12f, 690f, 455f), panelText.ToString(), panelStyle);
        }

        private void BuildPanelText()
        {
            panelText.Clear();
            panelText.AppendLine("Customer Pool Stress Test (runtime-only data)");
            panelText.Append("Configured pool: ").Append(runtimeVisits.Count);
            if (shift != null)
                panelText.Append(" | Frozen eligible pool: ").Append(shift.FrozenCustomerPoolCount);
            panelText.AppendLine();

            if (configurationFailed)
            {
                panelText.Append("ERROR: ").AppendLine(failureReason);
                return;
            }

            if (shift == null)
            {
                panelText.AppendLine("Waiting for BusinessShiftController...");
                return;
            }

            panelText.Append("State: ").Append(shift.State)
                .Append(" | Remaining: ").Append(shift.RemainingSeconds.ToString("0.0"))
                .AppendLine("s");
            panelText.Append("Started: ").Append(shift.TotalStartedCustomerCount)
                .Append(" | Completed: ").Append(shift.CompletedOrderCount)
                .AppendLine();
            panelText.Append("Last visit: ")
                .Append(string.IsNullOrWhiteSpace(shift.LastSelectedVisitKey)
                    ? "-"
                    : shift.LastSelectedVisitKey)
                .Append(" | Spawning stopped: ")
                .Append(shift.IsRandomCustomerSpawningStopped)
                .AppendLine();

            panelText.AppendLine();
            panelText.AppendLine("Appearance histogram (selected visits):");
            int shown = 0;
            foreach (KeyValuePair<string, int> pair in appearanceCounts)
            {
                panelText.Append("  ").Append(pair.Key).Append(": ").Append(pair.Value).AppendLine();
                if (++shown >= 10)
                    break;
            }

            if (appearanceCounts.Count == 0)
                panelText.AppendLine("  (no customer selected yet)");
            else if (appearanceCounts.Count > shown)
                panelText.Append("  ... ").Append(appearanceCounts.Count - shown).AppendLine(" more");

            panelText.AppendLine();
            panelText.AppendLine("Fast cycle: advance dialogue, enter crafting, then submit even an empty glass.");
            panelText.AppendLine("The repeated Yukari visual is expected; visit keys above are all distinct.");
        }

        private void EnsurePanelStyle()
        {
            if (panelStyle != null)
                return;

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 17,
                padding = new RectOffset(14, 14, 12, 12),
                wordWrap = true
            };
        }

        private void Fail(string reason)
        {
            configurationFailed = true;
            failureReason = reason;
            Debug.LogError("[CustomerPoolStress] " + reason);
        }

        private void OnDestroy()
        {
            if (shift != null)
                shift.CustomerVisitStarted -= HandleCustomerVisitStarted;

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
