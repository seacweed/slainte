using System;
using Slainte.Business;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.EditorTools
{
    // 통합 플레이테스트 씬을 만들고, 슬롯 기반 영업 시나리오를 Play 모드에서 자동으로 진행·검증한다.
    // 도메인 리로드를 넘어 진행 상황을 유지해야 하므로 실행 중인 검증 종류는 SessionState에, 단계는
    // 정적 필드에 둔다(리로드되면 단계 0부터 다시 시작해도 시나리오 시작 단계라 안전하다).
    public static class BusinessIntegrationPlaytestTools
    {
        private enum ValidationKind
        {
            None,
            ScheduledEncounter,
            CandidatePriority,
            ConditionFallbackFullDay
        }

        private const string SourceScenePath = ProjectScenePaths.Business;
        private const string PlaytestScenePath =
            ProjectScenePaths.BusinessFlowIntegrationPlaytest;
        private const string ActiveKindKey = "Slainte.BusinessIntegrationPlaytest.Validator.Kind";
        private const string CommandLineKey = "Slainte.BusinessIntegrationPlaytest.Validator.CommandLine";
        private const double TimeoutSeconds = 60d;

        private static int phase;
        private static double startedAt;

        [MenuItem("Slainte/Business/Create or Open Integration Playtest")]
        public static void CreateOrOpenPlaytestScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Business Integration Playtest",
                    "플레이 모드를 종료한 뒤 테스트 씬을 열어 주세요.",
                    "확인");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PlaytestScenePath) == null
                && !CopySourceScene())
            {
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(PlaytestScenePath, OpenSceneMode.Single);
            BusinessIntegrationPlaytestBootstrap bootstrap =
                FindInScene<BusinessIntegrationPlaytestBootstrap>(scene);
            if (bootstrap == null)
            {
                GameObject host = new GameObject("[DEV] Business Integration Playtest");
                SceneManager.MoveGameObjectToScene(host, scene);
                bootstrap = host.AddComponent<BusinessIntegrationPlaytestBootstrap>();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Selection.activeGameObject = bootstrap.gameObject;
            EditorGUIUtility.PingObject(bootstrap.gameObject);
            Debug.Log(
                "[BusinessIntegrationPlaytest] 씬을 열었습니다. Play 후 좌상단에서 "
                + "시나리오를 선택하세요. 저장과 진행도는 격리됩니다.");
        }

        [MenuItem("Slainte/Business/Refresh Integration Playtest Scene")]
        public static void RefreshPlaytestScene()
        {
            if (!EditorUtility.DisplayDialog(
                    "Refresh Business Integration Playtest",
                    "현재 BusinessScene 기준으로 개발용 통합 테스트 씬을 다시 만들까요?",
                    "새로 만들기",
                    "취소"))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PlaytestScenePath) != null
                && !AssetDatabase.DeleteAsset(PlaytestScenePath))
            {
                EditorUtility.DisplayDialog(
                    "Business Integration Playtest",
                    "기존 통합 테스트 씬을 삭제하지 못했습니다.",
                    "확인");
                return;
            }

            CreateOrOpenPlaytestScene();
        }

        [MenuItem("Slainte/Business/Validate Scheduled Encounter Play Mode")]
        public static void ValidateScheduledEncounterFromMenu() =>
            Begin(ValidationKind.ScheduledEncounter, false);

        public static void ValidateScheduledEncounterFromCommandLine() =>
            Begin(ValidationKind.ScheduledEncounter, true);

        [MenuItem("Slainte/Business/Validate Slot Candidate Priority Play Mode")]
        public static void ValidateCandidatePriorityFromMenu() =>
            Begin(ValidationKind.CandidatePriority, false);

        public static void ValidateCandidatePriorityFromCommandLine() =>
            Begin(ValidationKind.CandidatePriority, true);

        [MenuItem("Slainte/Business/Validate Condition Fallback Full Day Play Mode")]
        public static void ValidateConditionFallbackFromMenu() =>
            Begin(ValidationKind.ConditionFallbackFullDay, false);

        public static void ValidateConditionFallbackFromCommandLine() =>
            Begin(ValidationKind.ConditionFallbackFullDay, true);

        private static void Begin(ValidationKind kind, bool commandLine)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PlaytestScenePath) == null)
                CreateOrOpenPlaytestScene();

            SessionState.SetInt(ActiveKindKey, (int)kind);
            SessionState.SetBool(CommandLineKey, commandLine);
            phase = 0;
            startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= ValidateOnUpdate;
            EditorApplication.update += ValidateOnUpdate;
            EditorSceneManager.OpenScene(PlaytestScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            if (ActiveKind == ValidationKind.None)
                return;

            EditorApplication.update -= ValidateOnUpdate;
            EditorApplication.update += ValidateOnUpdate;
            startedAt = EditorApplication.timeSinceStartup;
        }

        private static ValidationKind ActiveKind =>
            (ValidationKind)SessionState.GetInt(ActiveKindKey, (int)ValidationKind.None);

        private static void ValidateOnUpdate()
        {
            ValidationKind kind = ActiveKind;
            if (kind == ValidationKind.None)
            {
                EditorApplication.update -= ValidateOnUpdate;
                return;
            }

            if (!EditorApplication.isPlaying)
                return;

            if (EditorApplication.timeSinceStartup - startedAt > TimeoutSeconds)
            {
                Finish(false, $"{kind} 검증 시간이 초과되었습니다. phase={phase}");
                return;
            }

            try
            {
                Context context = Context.Find();
                if (context == null)
                    return;

                switch (kind)
                {
                    case ValidationKind.ScheduledEncounter:
                        StepScheduledEncounter(context);
                        break;
                    case ValidationKind.CandidatePriority:
                        StepCandidatePriority(context);
                        break;
                    case ValidationKind.ConditionFallbackFullDay:
                        StepConditionFallbackFullDay(context);
                        break;
                }
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        // 1·2번 슬롯은 랜덤 손님, 3번 슬롯에서 배정된 에피소드가 EpisodeMode로 시작해야 한다.
        private static void StepScheduledEncounter(Context c)
        {
            int slot = BusinessIntegrationPlaytestBootstrap.ScheduledEncounterSlot;
            if (phase == 0)
            {
                StartScenario(c, BusinessPlaytestScenario.ScheduledEncounter);
                return;
            }

            if (phase < slot)
            {
                if (!CompleteRandomCustomerAtSlot(c, phase))
                    return;
                phase++;
                return;
            }

            if (c.Shift.State != BusinessShiftState.EncounterActive)
                return;
            RequireEncounterAtSlot(c, slot, BusinessIntegrationPlaytestBootstrap.ScheduledEncounterId);
            Require(c.Shift.CompletedOrderCount == slot - 1,
                $"인카운터 전 완료 주문 수가 {slot - 1}이 아닙니다: {c.Shift.CompletedOrderCount}");
            Finish(true, $"slots=[{string.Join(" / ", c.Bootstrap.SlotLog)}]");
        }

        // 같은 슬롯의 높은 우선순위 후보가 조건 미충족이면 낮은 우선순위 후보가 실행되어야 한다.
        private static void StepCandidatePriority(Context c)
        {
            int slot = BusinessIntegrationPlaytestBootstrap.PriorityTestSlot;
            if (phase == 0)
            {
                StartScenario(c, BusinessPlaytestScenario.CandidatePriority);
                return;
            }

            if (phase < slot)
            {
                if (!CompleteRandomCustomerAtSlot(c, phase))
                    return;
                phase++;
                return;
            }

            if (c.Shift.State != BusinessShiftState.EncounterActive)
                return;
            RequireEncounterAtSlot(c, slot, BusinessIntegrationPlaytestBootstrap.ScheduledEncounterId);
            Finish(true, $"slots=[{string.Join(" / ", c.Bootstrap.SlotLog)}]");
        }

        // 1번 슬롯 후보가 조건 미충족이면 랜덤 손님으로 대체되고, 하루 슬롯 수만큼 손님을 받은 뒤 끝나야 한다.
        private static void StepConditionFallbackFullDay(Context c)
        {
            if (phase == 0)
            {
                StartScenario(c, BusinessPlaytestScenario.ConditionFallback);
                return;
            }

            if (c.Shift.State == BusinessShiftState.Completed)
            {
                Require(c.Shift.TotalStartedEncounterCount == 0,
                    "조건을 만족하지 않는 에피소드가 실행됐습니다.");
                Require(c.Shift.TotalStartedCustomerCount == c.Shift.SlotsPerDay,
                    $"하루 손님 수가 {c.Shift.SlotsPerDay}명이 아닙니다: "
                    + c.Shift.TotalStartedCustomerCount);
                Finish(true, $"slots=[{string.Join(" / ", c.Bootstrap.SlotLog)}]");
                return;
            }

            if (CompleteRandomCustomerAtSlot(c, phase))
                phase++;
        }

        private static void StartScenario(Context c, BusinessPlaytestScenario scenario)
        {
            Require(DataManager.AreDiskWritesSuppressed,
                "통합 테스트 씬에서 디스크 저장이 차단되지 않았습니다.");
            Require(c.Bootstrap.TryStartScenario(scenario),
                $"{scenario} 시나리오를 시작하지 못했습니다.");
            phase = 1;
        }

        // 지정 슬롯이 랜덤 손님 주문으로 시작됐는지 확인하고 테스트용으로 즉시 완료시킨다.
        // 아직 그 슬롯의 주문이 시작되지 않았으면 false를 돌려 다음 프레임에 다시 확인한다.
        private static bool CompleteRandomCustomerAtSlot(Context c, int slot)
        {
            if (c.Shift.State != BusinessShiftState.OrderActive || c.Shift.CurrentSlot != slot)
                return false;

            Require(c.Shift.TotalStartedEncounterCount == 0,
                $"{slot}번 슬롯 전에 인카운터가 시작됐습니다.");
            Require(c.Shift.TotalStartedCustomerCount == slot,
                $"{slot}번 슬롯까지 손님 수가 맞지 않습니다: {c.Shift.TotalStartedCustomerCount}");
            Require(c.OrderSession.TryCompleteCurrentOrderForPlaytest(),
                $"{slot}번 슬롯 주문을 테스트용 완료 처리하지 못했습니다.");
            return true;
        }

        private static void RequireEncounterAtSlot(Context c, int slot, string episodeId)
        {
            Require(c.Shift.CurrentSlot == slot,
                $"인카운터가 {slot}번 슬롯이 아닙니다: {c.Shift.CurrentSlot}");
            Require(c.Shift.TotalStartedEncounterCount == 1,
                $"인카운터가 정확히 한 번 시작되지 않았습니다: {c.Shift.TotalStartedEncounterCount}");
            Require(EpisodeManager.Instance != null
                    && EpisodeManager.Instance.CurrentPlayingEpisodeID == episodeId,
                $"{slot}번 슬롯 에피소드가 {episodeId}가 아닙니다: "
                + EpisodeManager.Instance?.CurrentPlayingEpisodeID);
            Require(c.ModeManager.CurrentMode == GameMode.EpisodeMode,
                $"인카운터가 EpisodeMode가 아닙니다: {c.ModeManager.CurrentMode}");
            Require(c.EpisodeRunner.IsRunning, "인카운터의 EpisodeRunner가 실행 중이 아닙니다.");
        }

        private static void Finish(bool success, string message)
        {
            ValidationKind kind = ActiveKind;
            EditorApplication.update -= ValidateOnUpdate;
            bool commandLine = SessionState.GetBool(CommandLineKey, false);
            SessionState.EraseInt(ActiveKindKey);
            SessionState.EraseBool(CommandLineKey);

            if (success)
                Debug.Log($"[BusinessIntegrationPlayValidator] PASS {kind}: {message}");
            else
                Debug.LogError($"[BusinessIntegrationPlayValidator] FAIL {kind}: {message}");

            if (commandLine)
                EditorApplication.Exit(success ? 0 : 1);
            else
                EditorApplication.isPlaying = false;
        }

        private static bool CopySourceScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                EditorUtility.DisplayDialog(
                    "Business Integration Playtest",
                    $"{ProjectScenePaths.Business}를 찾지 못했습니다.",
                    "확인");
                return false;
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, PlaytestScenePath))
            {
                EditorUtility.DisplayDialog(
                    "Business Integration Playtest",
                    "BusinessScene 복사본을 만들지 못했습니다.",
                    "확인");
                return false;
            }

            AssetDatabase.ImportAsset(PlaytestScenePath, ImportAssetOptions.ForceUpdate);
            return true;
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

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        // 매 프레임 찾는 런타임 참조 묶음. 하나라도 준비되지 않았으면 Find()가 null을 돌려준다.
        private sealed class Context
        {
            public BusinessIntegrationPlaytestBootstrap Bootstrap;
            public BusinessShiftController Shift;
            public BusinessOrderSessionController OrderSession;
            public GameModeManager ModeManager;
            public EpisodeRunner EpisodeRunner;

            public static Context Find()
            {
                var bootstrap = UnityEngine.Object.FindFirstObjectByType<BusinessIntegrationPlaytestBootstrap>();
                var flow = UnityEngine.Object.FindFirstObjectByType<BusinessFlowBootstrap>();
                var modeManager = UnityEngine.Object.FindFirstObjectByType<GameModeManager>();
                var runner = UnityEngine.Object.FindFirstObjectByType<EpisodeRunner>();
                if (bootstrap == null || !bootstrap.IsReady || flow == null || !flow.IsRuntimeReady
                    || flow.ShiftController == null || flow.OrderSessionController == null
                    || modeManager == null || runner == null)
                {
                    return null;
                }

                return new Context
                {
                    Bootstrap = bootstrap,
                    Shift = flow.ShiftController,
                    OrderSession = flow.OrderSessionController,
                    ModeManager = modeManager,
                    EpisodeRunner = runner
                };
            }
        }
    }
}
