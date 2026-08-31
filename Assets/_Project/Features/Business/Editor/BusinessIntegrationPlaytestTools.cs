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
        private const string SourceScenePath = ProjectScenePaths.Business;
        private const string PlaytestScenePath =
            ProjectScenePaths.BusinessFlowIntegrationPlaytest;
        private const string RunningKey =
            "Slainte.BusinessIntegrationPlaytest.Validator.Running";
        private const string ThirdSlotRunningKey =
            "Slainte.BusinessIntegrationPlaytest.ThirdSlotValidator.Running";
        private const string ProductionDay5RunningKey =
            "Slainte.BusinessIntegrationPlaytest.ProductionDay5Validator.Running";

        private static int validationPhase;
        private static double phaseStartedAt;
        private static float encounterRemaining;
        private static int thirdSlotValidationPhase;
        private static double thirdSlotPhaseStartedAt;
        private static float thirdSlotEncounterRemaining;
        private static int productionDay5ValidationPhase;
        private static double productionDay5PhaseStartedAt;
        private static float productionDay5EncounterRemaining;

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

        [MenuItem("Slainte/Business/Validate Encounter At Third Slot Play Mode")]
        public static void ValidateEncounterAtThirdSlotFromMenu()
        {
            BeginEncounterAtThirdSlotValidation(false);
        }

        public static void ValidateEncounterAtThirdSlotFromCommandLine()
        {
            BeginEncounterAtThirdSlotValidation(true);
        }

        public static void ValidateEncounterAfterFirstOrderFromCommandLine()
        {
            ValidateEncounterAtThirdSlotFromCommandLine();
        }

        [MenuItem("Slainte/Business/Validate Production Day 5 Play Mode")]
        public static void ValidateProductionDay5FromMenu()
        {
            BeginProductionDay5Validation(false);
        }

        public static void ValidateProductionDay5FromCommandLine()
        {
            BeginProductionDay5Validation(true);
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

        private static void BeginEncounterAtThirdSlotValidation(bool commandLine)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PlaytestScenePath) == null)
                CreateOrOpenPlaytestScene();

            SessionState.SetBool(ThirdSlotRunningKey, true);
            SessionState.SetBool(ThirdSlotRunningKey + ".CommandLine", commandLine);
            thirdSlotValidationPhase = 0;
            EditorSceneManager.OpenScene(PlaytestScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        private static void BeginProductionDay5Validation(bool commandLine)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PlaytestScenePath) == null)
                CreateOrOpenPlaytestScene();

            SessionState.SetBool(ProductionDay5RunningKey, true);
            SessionState.SetBool(ProductionDay5RunningKey + ".CommandLine", commandLine);
            productionDay5ValidationPhase = 0;
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

            if (SessionState.GetBool(ThirdSlotRunningKey, false))
            {
                EditorApplication.update -= ValidateEncounterAtThirdSlotOnUpdate;
                EditorApplication.update += ValidateEncounterAtThirdSlotOnUpdate;
                thirdSlotPhaseStartedAt = EditorApplication.timeSinceStartup;
            }

            if (SessionState.GetBool(ProductionDay5RunningKey, false))
            {
                EditorApplication.update -= ValidateProductionDay5OnUpdate;
                EditorApplication.update += ValidateProductionDay5OnUpdate;
                productionDay5PhaseStartedAt = EditorApplication.timeSinceStartup;
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

        private static void ValidateEncounterAtThirdSlotOnUpdate()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (EditorApplication.timeSinceStartup - thirdSlotPhaseStartedAt > 40d)
            {
                FinishEncounterAtThirdSlot(
                    false,
                    "3번 영업 슬롯 인카운터 검증 시간이 초과되었습니다.");
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

                switch (thirdSlotValidationPhase)
                {
                    case 0:
                        Require(DataManager.AreDiskWritesSuppressed,
                            "통합 테스트 씬에서 디스크 저장이 차단되지 않았습니다.");
                        Require(bootstrap.TryStartScenario(
                                BusinessPlaytestScenario.EncounterAtThirdSlot),
                            "3번 영업 슬롯 인카운터 시나리오를 시작하지 못했습니다.");
                        thirdSlotValidationPhase = 1;
                        thirdSlotPhaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 1:
                        if (shift.State != BusinessShiftState.OrderActive)
                            return;
                        Require(shift.TotalStartedCustomerCount == 1,
                            $"첫 선택이 손님 1명이 아닙니다: {shift.TotalStartedCustomerCount}");
                        Require(shift.StartedSequenceCount == 1,
                            $"첫 주문의 영업 슬롯 수가 1이 아닙니다: {shift.StartedSequenceCount}");
                        Require(shift.TotalStartedEncounterCount == 0,
                            "첫 주문을 완료하기 전에 인카운터가 시작됐습니다.");
                        Require(orderSession.TryCompleteCurrentOrderForPlaytest(),
                            "첫 주문을 테스트용 완료 처리하지 못했습니다.");
                        thirdSlotValidationPhase = 2;
                        thirdSlotPhaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 2:
                        if (shift.State != BusinessShiftState.OrderActive)
                            return;
                        Require(shift.TotalStartedCustomerCount == 2,
                            $"두 번째 선택까지 손님 2명이 아닙니다: {shift.TotalStartedCustomerCount}");
                        Require(shift.StartedSequenceCount == 2,
                            $"두 번째 주문의 영업 슬롯 수가 2가 아닙니다: {shift.StartedSequenceCount}");
                        Require(shift.TotalStartedEncounterCount == 0,
                            "두 번째 주문을 완료하기 전에 인카운터가 시작됐습니다.");
                        Require(orderSession.TryCompleteCurrentOrderForPlaytest(),
                            "두 번째 주문을 테스트용 완료 처리하지 못했습니다.");
                        thirdSlotValidationPhase = 3;
                        thirdSlotPhaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 3:
                        if (shift.State != BusinessShiftState.EncounterActive)
                            return;
                        Require(shift.CompletedOrderCount == 2,
                            $"인카운터 시작 전 완료 주문 수가 2가 아닙니다: "
                            + shift.CompletedOrderCount);
                        Require(shift.StartedSequenceCount == 3,
                            $"인카운터가 3번 영업 슬롯을 소비하지 않았습니다: "
                            + shift.StartedSequenceCount);
                        Require(shift.TotalStartedEncounterCount == 1,
                            $"세 번째 선택에서 인카운터가 정확히 한 번 시작되지 않았습니다: "
                            + shift.TotalStartedEncounterCount);
                        Require(EpisodeManager.Instance != null
                                && EpisodeManager.Instance.CurrentPlayingEpisodeID
                                == "StrangeCoin_0",
                            "세 번째 선택이 StrangeCoin_0이 아닙니다.");
                        Require(modeManager.CurrentMode == GameMode.EpisodeMode,
                            $"세 번째 선택 인카운터가 EpisodeMode가 아닙니다: "
                            + modeManager.CurrentMode);
                        Require(episodeRunner.IsRunning,
                            "세 번째 선택 인카운터의 EpisodeRunner가 실행 중이 아닙니다.");
                        thirdSlotEncounterRemaining = shift.RemainingSeconds;
                        thirdSlotValidationPhase = 4;
                        thirdSlotPhaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 4:
                        if (EditorApplication.timeSinceStartup
                            - thirdSlotPhaseStartedAt < 1.2d)
                        {
                            return;
                        }

                        Require(shift.State == BusinessShiftState.EncounterActive,
                            "세 번째 선택 인카운터가 검증 도중 종료됐습니다.");
                        Require(modeManager.CurrentMode == GameMode.EpisodeMode,
                            "세 번째 선택 인카운터가 EpisodeMode를 유지하지 못했습니다.");
                        Require(episodeRunner.IsRunning,
                            "세 번째 선택 인카운터의 EpisodeRunner가 중단됐습니다.");
                        Require(thirdSlotEncounterRemaining - shift.RemainingSeconds >= 0.25f,
                            $"세 번째 선택 인카운터 중 타이머가 감소하지 않았습니다: "
                            + $"{thirdSlotEncounterRemaining:0.000} -> "
                            + $"{shift.RemainingSeconds:0.000}");
                        FinishEncounterAtThirdSlot(
                            true,
                            $"completedOrders={shift.CompletedOrderCount}, "
                            + $"sequenceSlot={shift.StartedSequenceCount}, "
                            + $"thirdEncounter={EpisodeManager.Instance.CurrentPlayingEpisodeID}, "
                            + "mode=EpisodeMode");
                        break;
                }
            }
            catch (Exception exception)
            {
                FinishEncounterAtThirdSlot(false, exception.ToString());
            }
        }

        private static void FinishEncounterAtThirdSlot(bool success, string message)
        {
            EditorApplication.update -= ValidateEncounterAtThirdSlotOnUpdate;
            SessionState.EraseBool(ThirdSlotRunningKey);
            bool commandLine = SessionState.GetBool(
                ThirdSlotRunningKey + ".CommandLine",
                false);
            SessionState.EraseBool(ThirdSlotRunningKey + ".CommandLine");

            if (success)
                Debug.Log("[BusinessEncounterThirdSlotValidator] PASS: " + message);
            else
                Debug.LogError("[BusinessEncounterThirdSlotValidator] FAIL: " + message);

            if (commandLine)
                EditorApplication.Exit(success ? 0 : 1);
            else
                EditorApplication.isPlaying = false;
        }

        private static void ValidateProductionDay5OnUpdate()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (EditorApplication.timeSinceStartup - productionDay5PhaseStartedAt > 40d)
            {
                FinishProductionDay5(false, "생산 Day 5 검증 시간이 초과되었습니다.");
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
                GameProgress progress = GameProgress.Instance;

                if (bootstrap == null || flow == null || !flow.IsRuntimeReady
                    || !bootstrap.IsReady || shift == null || orderSession == null
                    || modeManager == null || episodeRunner == null || progress == null)
                {
                    return;
                }

                switch (productionDay5ValidationPhase)
                {
                    case 0:
                        Require(DataManager.AreDiskWritesSuppressed,
                            "생산 Day 5 테스트에서 디스크 저장이 차단되지 않았습니다.");
                        Require(bootstrap.TryStartScenario(
                                BusinessPlaytestScenario.ProductionDay5),
                            "생산 Day 5 시나리오를 시작하지 못했습니다.");
                        Require(bootstrap.IsUsingProductionDay5Configuration,
                            "생산 손님 데이터베이스가 적용되지 않았습니다.");
                        Require(progress.CurrentDay == 5,
                            $"테스트 일차가 Day 5가 아닙니다: {progress.CurrentDay}");
                        Require(!progress.IsEpisodeCompleted("StrangeCoin_0"),
                            "StrangeCoin_0 완료 상태가 테스트에서 해제되지 않았습니다.");
                        Require(shift.FrozenCustomerPoolCount > 0,
                            "Day 5에 실행 가능한 생산 손님이 없습니다.");
                        productionDay5ValidationPhase = 1;
                        productionDay5PhaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 1:
                        if (shift.State != BusinessShiftState.OrderActive)
                            return;
                        Require(shift.TotalStartedCustomerCount == 1
                                && shift.StartedSequenceCount == 1,
                            "첫 생산 손님이 1번 슬롯으로 시작되지 않았습니다.");
                        Require(shift.TotalStartedEncounterCount == 0,
                            "첫 생산 주문 전에 인카운터가 시작됐습니다.");
                        Require(IsProductionVisitKey(shift.LastSelectedVisitKey),
                            $"첫 손님이 테스트용 방문입니다: {shift.LastSelectedVisitKey}");
                        Require(orderSession.TryCompleteCurrentOrderForPlaytest(),
                            "첫 생산 주문을 테스트용 완료 처리하지 못했습니다.");
                        productionDay5ValidationPhase = 2;
                        productionDay5PhaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 2:
                        if (shift.State != BusinessShiftState.OrderActive)
                            return;
                        Require(shift.TotalStartedCustomerCount == 2
                                && shift.StartedSequenceCount == 2,
                            "두 번째 생산 손님이 2번 슬롯으로 시작되지 않았습니다.");
                        Require(shift.TotalStartedEncounterCount == 0,
                            "두 번째 생산 주문 전에 인카운터가 시작됐습니다.");
                        Require(IsProductionVisitKey(shift.LastSelectedVisitKey),
                            $"두 번째 손님이 테스트용 방문입니다: {shift.LastSelectedVisitKey}");
                        Require(orderSession.TryCompleteCurrentOrderForPlaytest(),
                            "두 번째 생산 주문을 테스트용 완료 처리하지 못했습니다.");
                        productionDay5ValidationPhase = 3;
                        productionDay5PhaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 3:
                        if (shift.State != BusinessShiftState.EncounterActive)
                            return;
                        Require(shift.CompletedOrderCount == 2,
                            $"생산 인카운터 전 완료 주문 수가 2가 아닙니다: "
                            + shift.CompletedOrderCount);
                        Require(shift.StartedSequenceCount == 3,
                            $"생산 인카운터가 3번 슬롯이 아닙니다: "
                            + shift.StartedSequenceCount);
                        Require(shift.TotalStartedEncounterCount == 1,
                            "생산 인카운터가 정확히 한 번 시작되지 않았습니다.");
                        Require(EpisodeManager.Instance != null
                                && EpisodeManager.Instance.CurrentPlayingEpisodeID
                                == "StrangeCoin_0",
                            "생산 3번 슬롯이 StrangeCoin_0이 아닙니다.");
                        Require(modeManager.CurrentMode == GameMode.EpisodeMode,
                            "생산 인카운터가 EpisodeMode로 진입하지 않았습니다.");
                        Require(episodeRunner.IsRunning,
                            "생산 StrangeCoin_0 EpisodeRunner가 실행 중이 아닙니다.");
                        productionDay5EncounterRemaining = shift.RemainingSeconds;
                        productionDay5ValidationPhase = 4;
                        productionDay5PhaseStartedAt = EditorApplication.timeSinceStartup;
                        break;
                    case 4:
                        if (EditorApplication.timeSinceStartup
                            - productionDay5PhaseStartedAt < 1.2d)
                        {
                            return;
                        }

                        Require(shift.State == BusinessShiftState.EncounterActive,
                            "생산 StrangeCoin_0이 검증 도중 종료됐습니다.");
                        Require(productionDay5EncounterRemaining - shift.RemainingSeconds >= 0.25f,
                            "생산 StrangeCoin_0 진행 중 영업 타이머가 감소하지 않았습니다.");
                        FinishProductionDay5(
                            true,
                            $"day={progress.CurrentDay}, realVisit={shift.LastSelectedVisitKey}, "
                            + $"completedOrders={shift.CompletedOrderCount}, "
                            + $"sequenceSlot={shift.StartedSequenceCount}, "
                            + $"encounter={EpisodeManager.Instance.CurrentPlayingEpisodeID}");
                        break;
                }
            }
            catch (Exception exception)
            {
                FinishProductionDay5(false, exception.ToString());
            }
        }

        private static bool IsProductionVisitKey(string visitKey)
        {
            return !string.IsNullOrWhiteSpace(visitKey)
                && !visitKey.StartsWith(
                    "integration_visit_",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void FinishProductionDay5(bool success, string message)
        {
            EditorApplication.update -= ValidateProductionDay5OnUpdate;
            SessionState.EraseBool(ProductionDay5RunningKey);
            bool commandLine = SessionState.GetBool(
                ProductionDay5RunningKey + ".CommandLine",
                false);
            SessionState.EraseBool(ProductionDay5RunningKey + ".CommandLine");

            if (success)
                Debug.Log("[BusinessProductionDay5Validator] PASS: " + message);
            else
                Debug.LogError("[BusinessProductionDay5Validator] FAIL: " + message);

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
    }
}
