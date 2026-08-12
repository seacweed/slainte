using System;
using System.Collections.Generic;
using Slainte.Business;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.EditorTools
{
    public static class BusinessCustomerPoolStressTestTools
    {
        private const string SourceScenePath = "Assets/BusinessScene.unity";
        private const string StressScenePath =
            "Assets/Scenes/Dev/BusinessCustomerPoolStress.unity";
        private const string PlayValidationRunningKey =
            "Slainte.CustomerPoolStress.PlayValidation.Running";
        private static double playValidationStartedAt;

        [MenuItem("Slainte/Business/Create or Open Customer Pool Stress Test")]
        public static void CreateOrOpenStressScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Customer Pool Stress Test",
                    "플레이 모드를 종료한 뒤 테스트 씬을 열어 주세요.",
                    "확인");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StressScenePath) == null
                && !CopySourceScene())
            {
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(StressScenePath, OpenSceneMode.Single);
            BusinessCustomerPoolStressBootstrap bootstrap =
                FindInScene<BusinessCustomerPoolStressBootstrap>(scene);
            if (bootstrap == null)
            {
                GameObject host = new GameObject("[DEV] Customer Pool Stress Test");
                SceneManager.MoveGameObjectToScene(host, scene);
                bootstrap = host.AddComponent<BusinessCustomerPoolStressBootstrap>();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Selection.activeGameObject = bootstrap.gameObject;
            EditorGUIUtility.PingObject(bootstrap.gameObject);
            Debug.Log(
                "[CustomerPoolStress] 테스트 씬을 열었습니다. Play를 누르면 "
                + "원본 에셋을 수정하지 않고 임시 손님 풀이 생성됩니다.");
        }

        [MenuItem("Slainte/Business/Refresh Customer Pool Stress Test Scene")]
        public static void RefreshStressScene()
        {
            if (!EditorUtility.DisplayDialog(
                    "Refresh Customer Pool Stress Test",
                    "개발용 스트레스 테스트 씬을 현재 BusinessScene 기준으로 다시 만들까요?",
                    "새로 만들기",
                    "취소"))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StressScenePath) != null
                && !AssetDatabase.DeleteAsset(StressScenePath))
            {
                EditorUtility.DisplayDialog(
                    "Customer Pool Stress Test",
                    "기존 스트레스 테스트 씬을 삭제하지 못했습니다.",
                    "확인");
                return;
            }

            CreateOrOpenStressScene();
        }

        [MenuItem("Slainte/Business/Validate Customer Pool Stress")]
        public static void ValidateCustomerPoolStressFromMenu()
        {
            RunCustomerPoolStressValidation();
            EditorUtility.DisplayDialog(
                "Customer Pool Stress Test",
                "60명 풀의 50,000회 가중치 추첨과 쿨다운 검증을 통과했습니다.",
                "확인");
        }

        [MenuItem("Slainte/Business/Validate Customer Pool Stress Play Mode")]
        public static void ValidateStressScenePlayModeFromMenu()
        {
            BeginStressScenePlayValidation(false);
        }

        public static void ValidateStressScenePlayModeFromCommandLine()
        {
            BeginStressScenePlayValidation(true);
        }

        public static void RunCustomerPoolStressValidation()
        {
            const int visitCount = 60;
            const int weightedDrawCount = 50000;
            const int fallbackDrawCount = 5000;

            GameObject progressObject = new("CustomerPoolStressValidator_GameProgress");
            GameProgress progress = progressObject.AddComponent<GameProgress>();
            CustomerVisitDatabase database =
                ScriptableObject.CreateInstance<CustomerVisitDatabase>();
            CustomerOrderData order = ScriptableObject.CreateInstance<CustomerOrderData>();
            List<CustomerVisitData> visits = new(visitCount);

            try
            {
                progress.LoadFrom(new SaveData());
                progress.SetCurrentDay(1);
                order.key = "stress_validator_order";
                order.requestedRecipeId = "vodka_lemon";

                for (int i = 0; i < visitCount; i++)
                {
                    CustomerVisitData visit =
                        ScriptableObject.CreateInstance<CustomerVisitData>();
                    visit.visitKey = $"stress_validator_visit_{i:00}";
                    visit.weight = 1f + i % 5;
                    visit.cooldownSeconds = i % 3 switch
                    {
                        0 => 5f,
                        1 => 20f,
                        _ => 100f
                    };
                    visit.members.Add(new CustomerVisitMember
                    {
                        characterKey = "stress_validator_customer"
                    });
                    visit.orders.Add(new CustomerVisitOrderOption
                    {
                        order = order,
                        weight = 1f,
                        condition = new EpisodeTriggerCondition()
                    });
                    visits.Add(visit);
                    database.visits.Add(visit);
                }

                List<CustomerVisitData> pool =
                    BusinessSequencePlanner.BuildEligibleVisitPool(database, progress);
                Require(pool.Count == visitCount,
                    $"임시 손님 풀이 {visitCount}명이 아닙니다: {pool.Count}");

                ValidateWeightedDistribution(
                    pool,
                    progress,
                    weightedDrawCount,
                    new System.Random(1208));
                ValidateReadyCustomerPriority(pool, progress);
                ValidateAllCoolingDownFallback(
                    pool,
                    progress,
                    fallbackDrawCount,
                    new System.Random(812));

                Debug.Log(
                    $"[CustomerPoolStressValidator] PASS: pool={visitCount}, "
                    + $"weightedDraws={weightedDrawCount}, fallbackDraws={fallbackDrawCount}");
            }
            finally
            {
                for (int i = 0; i < visits.Count; i++)
                {
                    if (visits[i] != null)
                        UnityEngine.Object.DestroyImmediate(visits[i]);
                }

                UnityEngine.Object.DestroyImmediate(order);
                UnityEngine.Object.DestroyImmediate(database);
                UnityEngine.Object.DestroyImmediate(progressObject);
            }
        }

        private static void BeginStressScenePlayValidation(bool commandLine)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StressScenePath) == null)
                CreateOrOpenStressScene();

            SessionState.SetBool(PlayValidationRunningKey, true);
            SessionState.SetBool(PlayValidationRunningKey + ".CommandLine", commandLine);
            EditorSceneManager.OpenScene(StressScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void ResumeStressScenePlayValidation()
        {
            if (!SessionState.GetBool(PlayValidationRunningKey, false))
                return;

            EditorApplication.update -= ValidateStressSceneOnUpdate;
            EditorApplication.update += ValidateStressSceneOnUpdate;
            playValidationStartedAt = EditorApplication.timeSinceStartup;
        }

        private static void ValidateStressSceneOnUpdate()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (EditorApplication.timeSinceStartup - playValidationStartedAt > 30d)
            {
                FinishStressScenePlayValidation(
                    false,
                    "스트레스 테스트 씬 초기화를 기다리다 시간이 초과되었습니다.");
                return;
            }

            try
            {
                BusinessCustomerPoolStressBootstrap stress =
                    UnityEngine.Object.FindFirstObjectByType<BusinessCustomerPoolStressBootstrap>();
                BusinessFlowBootstrap flow =
                    UnityEngine.Object.FindFirstObjectByType<BusinessFlowBootstrap>();
                BusinessShiftController shift = flow != null ? flow.ShiftController : null;
                Slainte.Bartending.BusinessBartendingBootstrap bartending =
                    UnityEngine.Object.FindFirstObjectByType<
                        Slainte.Bartending.BusinessBartendingBootstrap>();

                if (stress == null || flow == null || !flow.IsRuntimeReady || shift == null)
                    return;
                if (!shift.IsActive || shift.TotalStartedCustomerCount == 0)
                    return;

                Require(bartending != null,
                    "개발 씬에 BusinessBartendingBootstrap이 설치되지 않았습니다.");
                Require(shift.FrozenCustomerPoolCount == 32,
                    $"실제 영업에 고정된 손님 풀이 32명이 아닙니다: "
                    + shift.FrozenCustomerPoolCount);
                Require(shift.LastSelectedVisitKey.StartsWith("stress_visit_",
                        StringComparison.Ordinal),
                    "첫 손님이 임시 스트레스 풀에서 선택되지 않았습니다: "
                    + shift.LastSelectedVisitKey);

                FinishStressScenePlayValidation(
                    true,
                    $"pool={shift.FrozenCustomerPoolCount}, "
                    + $"firstVisit={shift.LastSelectedVisitKey}, state={shift.State}");
            }
            catch (Exception exception)
            {
                FinishStressScenePlayValidation(false, exception.ToString());
            }
        }

        private static void FinishStressScenePlayValidation(bool success, string message)
        {
            EditorApplication.update -= ValidateStressSceneOnUpdate;
            SessionState.EraseBool(PlayValidationRunningKey);
            bool commandLine =
                SessionState.GetBool(PlayValidationRunningKey + ".CommandLine", false);
            SessionState.EraseBool(PlayValidationRunningKey + ".CommandLine");

            if (success)
                Debug.Log("[CustomerPoolStressPlayValidator] PASS: " + message);
            else
                Debug.LogError("[CustomerPoolStressPlayValidator] FAIL: " + message);

            if (commandLine)
                EditorApplication.Exit(success ? 0 : 1);
            else
                EditorApplication.isPlaying = false;
        }

        private static void ValidateWeightedDistribution(
            IReadOnlyList<CustomerVisitData> pool,
            GameProgress progress,
            int drawCount,
            System.Random random)
        {
            int[] drawsByWeight = new int[6];
            for (int i = 0; i < drawCount; i++)
            {
                BusinessVisitSelection selection = BusinessSequencePlanner.PickWeightedVisit(
                    pool,
                    progress,
                    null,
                    null,
                    0f,
                    random);
                Require(selection != null, $"가중치 추첨 {i}회에서 손님 선택이 실패했습니다.");
                Require(!selection.UsedCooldownFallback,
                    "쿨다운이 없는데 대체 추첨으로 표시되었습니다.");
                int weight = Mathf.RoundToInt(selection.Visit.weight);
                Require(weight >= 1 && weight <= 5, "예상하지 못한 가중치가 선택되었습니다.");
                drawsByWeight[weight]++;
            }

            const float totalWeightPerSet = 15f;
            for (int weight = 1; weight <= 5; weight++)
            {
                float expectedShare = weight / totalWeightPerSet;
                float actualShare = drawsByWeight[weight] / (float)drawCount;
                Require(Mathf.Abs(actualShare - expectedShare) <= 0.02f,
                    $"가중치 {weight}의 추첨 비율이 허용 범위를 벗어났습니다. "
                    + $"expected={expectedShare:P2}, actual={actualShare:P2}");
            }
        }

        private static void ValidateReadyCustomerPriority(
            IReadOnlyList<CustomerVisitData> pool,
            GameProgress progress)
        {
            CustomerVisitData onlyReady = pool[17];
            Dictionary<string, float> cooldowns =
                new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != onlyReady)
                    cooldowns[pool[i].visitKey] = 100f;
            }

            System.Random random = new(77);
            for (int i = 0; i < 1000; i++)
            {
                BusinessVisitSelection selection = BusinessSequencePlanner.PickWeightedVisit(
                    pool,
                    progress,
                    cooldowns,
                    null,
                    0f,
                    random);
                Require(selection?.Visit == onlyReady,
                    "쿨다운이 끝난 유일한 손님보다 쿨다운 중인 손님이 먼저 선택되었습니다.");
                Require(!selection.UsedCooldownFallback,
                    "선택 가능한 손님이 있는데 대체 추첨이 사용되었습니다.");
            }
        }

        private static void ValidateAllCoolingDownFallback(
            IReadOnlyList<CustomerVisitData> pool,
            GameProgress progress,
            int drawCount,
            System.Random random)
        {
            Dictionary<string, float> cooldowns =
                new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pool.Count; i++)
                cooldowns[pool[i].visitKey] = 100f;

            HashSet<string> selectedKeys = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < drawCount; i++)
            {
                BusinessVisitSelection selection = BusinessSequencePlanner.PickWeightedVisit(
                    pool,
                    progress,
                    cooldowns,
                    null,
                    0f,
                    random);
                Require(selection != null,
                    $"전체 쿨다운 대체 추첨 {i}회에서 손님 선택이 실패했습니다.");
                Require(selection.UsedCooldownFallback,
                    "모든 손님이 쿨다운인데 대체 추첨으로 표시되지 않았습니다.");
                selectedKeys.Add(selection.Visit.visitKey);
            }

            Require(selectedKeys.Count >= pool.Count * 0.9f,
                $"전체 쿨다운 대체 추첨에서 선택 다양성이 부족합니다: "
                + $"{selectedKeys.Count}/{pool.Count}");
        }

        private static bool CopySourceScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                EditorUtility.DisplayDialog(
                    "Customer Pool Stress Test",
                    "Assets/BusinessScene.unity를 찾지 못했습니다.",
                    "확인");
                return false;
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, StressScenePath))
            {
                EditorUtility.DisplayDialog(
                    "Customer Pool Stress Test",
                    "BusinessScene 복사본을 만들지 못했습니다.",
                    "확인");
                return false;
            }

            AssetDatabase.ImportAsset(StressScenePath, ImportAssetOptions.ForceUpdate);
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
