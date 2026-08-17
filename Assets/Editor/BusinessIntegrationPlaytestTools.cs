using System;
using Slainte.Business;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.EditorTools
{
    public static class BusinessIntegrationPlaytestTools
    {
        private const string SourceScenePath = "Assets/BusinessScene.unity";
        private const string PlaytestScenePath =
            "Assets/Scenes/Dev/BusinessFlowIntegrationPlaytest.unity";
        private const string RunningKey =
            "Slainte.BusinessIntegrationPlaytest.Validator.Running";
        private const string AfterFirstOrderRunningKey =
            "Slainte.BusinessIntegrationPlaytest.AfterFirstOrderValidator.Running";

        private static int validationPhase;
        private static double phaseStartedAt;
        private static float encounterRemaining;
        private static int afterFirstOrderValidationPhase;
        private static double afterFirstOrderPhaseStartedAt;
        private static float afterFirstOrderEncounterRemaining;

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

        [MenuItem("Slainte/Business/Validate Integration Playtest Play Mode")]
        public static void ValidatePlayModeFromMenu()
        {
            BeginPlayModeValidation(false);
        }

        public static void ValidatePlayModeFromCommandLine()
        {
            BeginPlayModeValidation(true);
        }

        [MenuItem("Slainte/Business/Validate Encounter After First Order Play Mode")]
        public static void ValidateEncounterAfterFirstOrderFromMenu()
        {
            BeginEncounterAfterFirstOrderValidation(false);
        }

        public static void ValidateEncounterAfterFirstOrderFromCommandLine()
        {
            BeginEncounterAfterFirstOrderValidation(true);
        }

        private static void BeginPlayModeValidation(bool commandLine)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PlaytestScenePath) == null)
                CreateOrOpenPlaytestScene();

            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(RunningKey + ".CommandLine", commandLine);
            validationPhase = 0;
            EditorSceneManager.OpenScene(PlaytestScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        private static void BeginEncounterAfterFirstOrderValidation(bool commandLine)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PlaytestScenePath) == null)
                CreateOrOpenPlaytestScene();

            SessionState.SetBool(AfterFirstOrderRunningKey, true);
            SessionState.SetBool(AfterFirstOrderRunningKey + ".CommandLine", commandLine);
            afterFirstOrderValidationPhase = 0;
            EditorSceneManager.OpenScene(PlaytestScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            if (SessionState.GetBool(RunningKey, false))
            {
                EditorApplication.update -= ValidateOnUpdate;
                EditorApplication.update += ValidateOnUpdate;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }

            if (SessionState.GetBool(AfterFirstOrderRunningKey, false))
            {
                EditorApplication.update -= ValidateEncounterAfterFirstOrderOnUpdate;
                EditorApplication.update += ValidateEncounterAfterFirstOrderOnUpdate;
                afterFirstOrderPhaseStartedAt = EditorApplication.timeSinceStartup;
            }
        }

        private static void ValidateOnUpdate()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (EditorApplication.timeSinceStartup - phaseStartedAt > 40d)
            {
                Finish(false, "통합 Play Mode 검증 시간이 초과되었습니다.");
                return;
            }

            try
            {
                BusinessIntegrationPlaytestBootstrap bootstrap =
                    UnityEngine.Object.FindFirstObjectByType<
                        BusinessIntegrationPlaytestBootstrap>();
                BusinessFlowBootstrap flow =
                    UnityEngine.Object.FindFirstObjectByType<BusinessFlowBootstrap>();
                BusinessShiftController shift = flow != null ? flow.ShiftController : null;
                GameModeManager modeManager =
                    UnityEngine.Object.FindFirstObjectByType<GameModeManager>();
                EpisodeRunner episodeRunner =
                    UnityEngine.Object.FindFirstObjectByType<EpisodeRunner>();

                if (bootstrap == null || flow == null || !flow.IsRuntimeReady
                    || !bootstrap.IsReady || shift == null || modeManager == null
                    || episodeRunner == null)
                {
                    return;
                }

                switch (validationPhase)
                {
                    case 0:
                        Require(DataManager.AreDiskWritesSuppressed,
                            "통합 테스트 씬에서 디스크 저장이 차단되지 않았습니다.");
                        Require(bootstrap.TryStartScenario(
                                BusinessPlaytestScenario.EncounterTimerRuns),
                            "인카운터 타이머 시나리오를 시작하지 못했습니다.");
                        validationPhase = 1;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 1:
                        if (shift.State != BusinessShiftState.EncounterActive)
                            return;
                        Require(modeManager.CurrentMode == GameMode.EpisodeMode,
                            $"영업 시작 직후 인카운터 모드가 덮어쓰여졌습니다: "
                            + modeManager.CurrentMode);
                        Require(episodeRunner.IsRunning,
                            "EncounterActive 상태인데 EpisodeRunner가 실행 중이 아닙니다.");
                        encounterRemaining = shift.RemainingSeconds;
                        validationPhase = 2;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 2:
                        if (EditorApplication.timeSinceStartup - phaseStartedAt < 1.2d)
                            return;
                        Require(shift.State == BusinessShiftState.EncounterActive,
                            "타이머 검증 도중 인카운터가 예기치 않게 종료됐습니다.");
                        Require(modeManager.CurrentMode == GameMode.EpisodeMode,
                            $"진행 중인 인카운터가 EpisodeMode를 유지하지 못했습니다: "
                            + modeManager.CurrentMode);
                        Require(episodeRunner.IsRunning,
                            "타이머 검증 도중 EpisodeRunner가 중단됐습니다.");
                        Require(encounterRemaining - shift.RemainingSeconds >= 0.25f,
                            $"인카운터 중 영업 타이머가 감소하지 않았습니다: "
                            + $"{encounterRemaining:0.000} -> {shift.RemainingSeconds:0.000}");
                        Require(shift.FrozenCustomerPoolCount == 12,
                            $"통합 테스트 손님 풀이 12명이 아닙니다: "
                            + shift.FrozenCustomerPoolCount);
                        validationPhase = 3;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 3:
                        if (!shift.IsTimerExpired)
                            return;
                        Require(shift.RemainingSeconds <= 0.001f,
                            $"만료된 타이머가 0초로 고정되지 않았습니다: {shift.RemainingSeconds:0.000}");
                        Require(shift.State == BusinessShiftState.EncounterActive,
                            $"타이머 만료가 진행 중인 인카운터 상태를 덮어썼습니다: {shift.State}");
                        Require(EpisodeManager.Instance != null
                                && EpisodeManager.Instance.IsBusinessEncounterActive,
                            "타이머 만료로 진행 중인 영업 인카운터가 취소됐습니다.");
                        Finish(true,
                            $"pool={shift.FrozenCustomerPoolCount}, "
                            + $"encounterTimerDecrease="
                            + (encounterRemaining - shift.RemainingSeconds).ToString("0.000")
                            + ", statePreservedAtExpiry=true");
                        break;
                }
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= ValidateOnUpdate;
            SessionState.EraseBool(RunningKey);
            bool commandLine = SessionState.GetBool(RunningKey + ".CommandLine", false);
            SessionState.EraseBool(RunningKey + ".CommandLine");

            if (success)
                Debug.Log("[BusinessIntegrationPlayValidator] PASS: " + message);
            else
                Debug.LogError("[BusinessIntegrationPlayValidator] FAIL: " + message);

            if (commandLine)
                EditorApplication.Exit(success ? 0 : 1);
            else
                EditorApplication.isPlaying = false;
        }

        private static void ValidateEncounterAfterFirstOrderOnUpdate()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (EditorApplication.timeSinceStartup - afterFirstOrderPhaseStartedAt > 40d)
            {
                FinishEncounterAfterFirstOrder(
                    false,
                    "첫 주문 이후 인카운터 검증 시간이 초과되었습니다.");
                return;
            }

            try
            {
                BusinessIntegrationPlaytestBootstrap bootstrap =
                    UnityEngine.Object.FindFirstObjectByType<
                        BusinessIntegrationPlaytestBootstrap>();
                BusinessFlowBootstrap flow =
                    UnityEngine.Object.FindFirstObjectByType<BusinessFlowBootstrap>();
                BusinessShiftController shift = flow != null ? flow.ShiftController : null;
                BusinessOrderSessionController orderSession =
                    flow != null ? flow.OrderSessionController : null;
                GameModeManager modeManager =
                    UnityEngine.Object.FindFirstObjectByType<GameModeManager>();
                EpisodeRunner episodeRunner =
                    UnityEngine.Object.FindFirstObjectByType<EpisodeRunner>();

                if (bootstrap == null || flow == null || !flow.IsRuntimeReady
                    || !bootstrap.IsReady || shift == null || orderSession == null
                    || modeManager == null || episodeRunner == null)
                {
                    return;
                }

                switch (afterFirstOrderValidationPhase)
                {
                    case 0:
                        Require(DataManager.AreDiskWritesSuppressed,
                            "통합 테스트 씬에서 디스크 저장이 차단되지 않았습니다.");
                        Require(bootstrap.TryStartScenario(
                                BusinessPlaytestScenario.EncounterAfterFirstOrder),
                            "첫 주문 이후 인카운터 시나리오를 시작하지 못했습니다.");
                        afterFirstOrderValidationPhase = 1;
                        afterFirstOrderPhaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 1:
                        if (shift.State != BusinessShiftState.OrderActive)
                            return;
                        Require(shift.TotalStartedCustomerCount == 1,
                            $"첫 선택이 손님 1명이 아닙니다: {shift.TotalStartedCustomerCount}");
                        Require(shift.TotalStartedEncounterCount == 0,
                            "첫 주문을 완료하기 전에 인카운터가 시작됐습니다.");
                        Require(shift.LastSelectedVisitKey == "integration_visit_00",
                            $"첫 손님이 integration_visit_00이 아닙니다: "
                            + shift.LastSelectedVisitKey);
                        Require(orderSession.TryCompleteCurrentOrderForPlaytest(),
                            "첫 주문을 테스트용 완료 처리하지 못했습니다.");
                        afterFirstOrderValidationPhase = 2;
                        afterFirstOrderPhaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 2:
                        if (shift.State != BusinessShiftState.EncounterActive)
                            return;
                        Require(shift.CompletedOrderCount == 1,
                            $"인카운터 시작 전 완료 주문 수가 1이 아닙니다: "
                            + shift.CompletedOrderCount);
                        Require(shift.TotalStartedEncounterCount == 1,
                            $"두 번째 선택에서 인카운터가 정확히 한 번 시작되지 않았습니다: "
                            + shift.TotalStartedEncounterCount);
                        Require(EpisodeManager.Instance != null
                                && EpisodeManager.Instance.CurrentPlayingEpisodeID
                                == "StrangeCoin_0",
                            "두 번째 선택이 StrangeCoin_0이 아닙니다.");
                        Require(modeManager.CurrentMode == GameMode.EpisodeMode,
                            $"두 번째 선택 인카운터가 EpisodeMode가 아닙니다: "
                            + modeManager.CurrentMode);
                        Require(episodeRunner.IsRunning,
                            "두 번째 선택 인카운터의 EpisodeRunner가 실행 중이 아닙니다.");
                        afterFirstOrderEncounterRemaining = shift.RemainingSeconds;
                        afterFirstOrderValidationPhase = 3;
                        afterFirstOrderPhaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 3:
                        if (EditorApplication.timeSinceStartup
                            - afterFirstOrderPhaseStartedAt < 1.2d)
                        {
                            return;
                        }

                        Require(shift.State == BusinessShiftState.EncounterActive,
                            "두 번째 선택 인카운터가 검증 도중 종료됐습니다.");
                        Require(modeManager.CurrentMode == GameMode.EpisodeMode,
                            "두 번째 선택 인카운터가 EpisodeMode를 유지하지 못했습니다.");
                        Require(episodeRunner.IsRunning,
                            "두 번째 선택 인카운터의 EpisodeRunner가 중단됐습니다.");
                        Require(afterFirstOrderEncounterRemaining - shift.RemainingSeconds >= 0.25f,
                            $"두 번째 선택 인카운터 중 타이머가 감소하지 않았습니다: "
                            + $"{afterFirstOrderEncounterRemaining:0.000} -> "
                            + $"{shift.RemainingSeconds:0.000}");
                        FinishEncounterAfterFirstOrder(
                            true,
                            $"firstCustomer={shift.LastSelectedVisitKey}, "
                            + $"completedOrders={shift.CompletedOrderCount}, "
                            + $"secondEncounter={EpisodeManager.Instance.CurrentPlayingEpisodeID}, "
                            + "mode=EpisodeMode");
                        break;
                }
            }
            catch (Exception exception)
            {
                FinishEncounterAfterFirstOrder(false, exception.ToString());
            }
        }

        private static void FinishEncounterAfterFirstOrder(bool success, string message)
        {
            EditorApplication.update -= ValidateEncounterAfterFirstOrderOnUpdate;
            SessionState.EraseBool(AfterFirstOrderRunningKey);
            bool commandLine = SessionState.GetBool(
                AfterFirstOrderRunningKey + ".CommandLine",
                false);
            SessionState.EraseBool(AfterFirstOrderRunningKey + ".CommandLine");

            if (success)
                Debug.Log("[BusinessEncounterAfterFirstOrderValidator] PASS: " + message);
            else
                Debug.LogError("[BusinessEncounterAfterFirstOrderValidator] FAIL: " + message);

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
                    "Assets/BusinessScene.unity를 찾지 못했습니다.",
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
    }
}
