using System;
using System.Linq;
using Slainte.Business;
using Slainte.TV;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class TVSystemValidator
    {
        private const string ScenePath =
            "Assets/_Project/Scenes/Production/RestScene.unity";
        private const string PrefabPath = "Assets/RestScene/Prefabs/TVSystem.prefab";
        private const string PanelPrefabPath = "Assets/RestScene/Prefabs/TVPanel.prefab";

        [MenuItem("Slainte/TV/Validate TV System")]
        public static void ValidateFromMenu()
        {
            RunValidation();
            EditorUtility.DisplayDialog("TV", "TV 데이터, 상태, 효과, 배치 검증을 통과했습니다.", "확인");
        }

        public static void RunBatchValidation()
        {
            RunValidation();
        }

        private static void RunValidation()
        {
            TVBroadcastDatabase database = TVBroadcastDatabase.LoadDefault();
            Require(database != null, "기본 TV 방송 데이터베이스가 없습니다.");
            Require(database.broadcasts != null && database.broadcasts.Count == 6,
                "TV 방송이 6개가 아닙니다.");
            Require(Mathf.Approximately(database.broadcasts.Sum(entry => entry.weight), 100f),
                "TV 방송 가중치 합계가 100이 아닙니다.");
            Require(Mathf.Approximately(database.FindById("none").weight, 55f),
                "이상 없음 가중치가 55가 아닙니다.");
            Require(Mathf.Approximately(database.FindById("tip_bonus").effectMultiplier, 1.5f),
                "팁 방송 배율이 1.5가 아닙니다.");
            Require(Mathf.Approximately(database.FindById("high_abv_orders").effectMultiplier, 2f),
                "고도수 주문 가중치 배율이 2가 아닙니다.");
            Require(string.Equals(
                    database.FindById("district_9_patrol").targetTag,
                    CustomerPlanningCsvImporter.NightPatrolAttributeTag,
                    StringComparison.OrdinalIgnoreCase),
                "특정 손님 방송이 야간순찰 속성을 대상으로 하지 않습니다.");
            Require(database.FindById("district_9_patrol").exclusiveCustomerPool,
                "야간순찰 방송이 전용 손님 풀로 설정되지 않았습니다.");
            Require(HasCustomerTargetTag(
                    CustomerPlanningCsvImporter.NightPatrolAttributeTag),
                "프로젝트에 야간순찰 TV 대상 손님 데이터가 없습니다.");

            ValidateRuntime(database);
            ValidatePrefabAndScene();
            RestSceneFinalArtValidator.ValidateOrThrow();
            Debug.Log(
                "[TVSystemValidator] PASS: six broadcasts/100 weight, persistent forecast, "
                + "delivery/shop restrictions, tip/tag multipliers, world TV prefab and Canvas panel.");
        }

        private static void ValidateRuntime(TVBroadcastDatabase database)
        {
            GameProgress existing = GameProgress.Instance;
            GameObject host = null;
            GameProgress progress = existing;
            SaveData restore = existing != null ? Capture(existing) : null;

            try
            {
                if (progress == null)
                {
                    host = new GameObject("TVSystemValidator_GameProgress");
                    progress = host.AddComponent<GameProgress>();
                }

                progress.LoadFrom(new SaveData { dayCount = 7 });
                TVBroadcastEntry forecast = TVBroadcastRuntime.EnsureForecast(
                    progress,
                    database,
                    new System.Random(1234));
                Require(forecast != null && progress.TVForecastBroadcastId == forecast.id,
                    "휴식 진입 방송이 저장되지 않았습니다.");
                Require(TVBroadcastRuntime.EnsureForecast(
                        progress,
                        database,
                        new System.Random(9999)).id == forecast.id,
                    "같은 휴식에서 방송이 다시 추첨됐습니다.");

                progress.SetTVForecast("tip_bonus");
                progress.AdvanceDay();
                TVBroadcastRuntime.ActivateForecastForBusiness(progress, database);
                Require(progress.TVActiveBroadcastId == "tip_bonus"
                    && progress.TVActiveBusinessDay == progress.CurrentDay,
                    "예고 방송이 다음 영업 효과로 활성화되지 않았습니다.");
                Require(Mathf.Approximately(
                        TVBroadcastRuntime.GetTipMultiplier(progress, database),
                        1.5f),
                    "팁 방송 배율을 읽지 못했습니다.");

                BusinessOrderFlowSettings settings =
                    ScriptableObject.CreateInstance<BusinessOrderFlowSettings>();
                settings.goodMoneyReward = 100;
                settings.satisfiedTipRate = 0.2f;
                BusinessOrderReward reward = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Good,
                    settings,
                    TVBroadcastRuntime.GetTipMultiplier(progress, database));
                Require(reward.TipAmount == 30 && reward.TotalRevenue == 130,
                    "TV 팁 1.5배가 영업 보상 계산에 적용되지 않았습니다.");
                UnityEngine.Object.DestroyImmediate(settings);

                progress.SetTVForecast("high_abv_orders");
                progress.ActivateTVForecastForBusiness();
                Require(Mathf.Approximately(
                        TVBroadcastRuntime.GetTaggedWeightMultiplier(
                            progress,
                            database,
                            TVBroadcastEffectType.BoostOrderTagWeight,
                            new[] { "high_abv" }),
                        2f),
                    "고도수 주문 태그 배율을 읽지 못했습니다.");

                ValidateAutomaticAbvAndGlobalOrderWeight(progress, database);

                progress.SetTVForecast("district_9_patrol");
                progress.ActivateTVForecastForBusiness();
                Require(Mathf.Approximately(
                        TVBroadcastRuntime.GetTaggedWeightMultiplier(
                            progress,
                            database,
                            TVBroadcastEffectType.BoostCustomerTagWeight,
                            new[] { "customer_attribute:야간순찰" }),
                        2f),
                    "특정 손님 태그 배율을 읽지 못했습니다.");

                progress.SetTVForecast("delivery_outage");
                progress.ActivateTVForecastForBusiness();
                Require(TVBroadcastRuntime.IsActiveEffect(
                        progress,
                        database,
                        TVBroadcastEffectType.DisableDelivery),
                    "배송 금지 효과를 읽지 못했습니다.");

                progress.SetTVForecast("shop_maintenance");
                progress.ActivateTVForecastForBusiness();
                Require(TVBroadcastRuntime.IsRestShopDisabled(progress, database, out string reason)
                    && !string.IsNullOrWhiteSpace(reason),
                    "일반 상점 금지 상태 또는 사유를 읽지 못했습니다.");

                SaveData serialized = JsonUtility.FromJson<SaveData>(
                    JsonUtility.ToJson(Capture(progress)));
                progress.LoadFrom(serialized);
                Require(progress.TVActiveBroadcastId == "shop_maintenance",
                    "저장 후 TV 활성 효과가 복원되지 않았습니다.");
            }
            finally
            {
                if (restore != null && progress != null)
                    progress.LoadFrom(restore);
                if (host != null)
                    UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void ValidateAutomaticAbvAndGlobalOrderWeight(
            GameProgress progress,
            TVBroadcastDatabase database)
        {
            TVBroadcastEntry entry = database.FindById("high_abv_orders");
            float originalThreshold = entry.minimumAbvPercent;
            CustomerOrderData calculatedOrder = ScriptableObject.CreateInstance<CustomerOrderData>();
            calculatedOrder.key = "tv_abv_validation";
            calculatedOrder.requestedRecipeId = "rec_1001";
            try
            {
                entry.minimumAbvPercent = 14f;
                Require(Mathf.Approximately(
                        TVBroadcastRuntime.GetOrderWeightMultiplier(
                            progress,
                            database,
                            calculatedOrder),
                        2f),
                    "계산 도수 기준으로 고도수 주문을 판정하지 못했습니다.");
            }
            finally
            {
                entry.minimumAbvPercent = originalThreshold;
                UnityEngine.Object.DestroyImmediate(calculatedOrder);
            }

            CustomerOrderData boostedOrder = CreateValidationOrder("boosted", true);
            CustomerOrderData normalOrder = CreateValidationOrder("normal", false);
            CustomerVisitData boostedVisit = CreateValidationVisit("boosted_visit", boostedOrder);
            CustomerVisitData normalVisit = CreateValidationVisit("normal_visit", normalOrder);
            try
            {
                int boostedCount = 0;
                int normalCount = 0;
                var pool = new[] { boostedVisit, normalVisit };
                var recent = new System.Collections.Generic.HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                var invalid = new System.Collections.Generic.HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                System.Random random = new(4921);
                for (int i = 0; i < 600; i++)
                {
                    BusinessVisitSelection selection = BusinessSequencePlanner.PickWeightedVisit(
                        pool,
                        progress,
                        recent,
                        invalid,
                        random);
                    if (selection?.Visit == boostedVisit) boostedCount++;
                    else if (selection?.Visit == normalVisit) normalCount++;
                }

                Require(boostedCount > normalCount * 1.5f,
                    $"고도수 주문의 전체 등장 확률이 충분히 증가하지 않았습니다: {boostedCount}/{normalCount}");

                boostedVisit.reappearanceGroupKey = "shared_person";
                normalVisit.reappearanceGroupKey = "shared_person";
                CustomerOrderData readyOrder = CreateValidationOrder("ready", false);
                CustomerVisitData readyVisit = CreateValidationVisit("ready_visit", readyOrder);
                try
                {
                    recent.Add("shared_person");
                    BusinessVisitSelection readySelection = BusinessSequencePlanner.PickWeightedVisit(
                        new[] { boostedVisit, normalVisit, readyVisit },
                        progress,
                        recent,
                        invalid,
                        new System.Random(7));
                    Require(readySelection?.Visit == readyVisit,
                        "같은 인물의 여러 방문형이 최근 등장 제한을 공유하지 않습니다.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(readyVisit);
                    UnityEngine.Object.DestroyImmediate(readyOrder);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boostedVisit);
                UnityEngine.Object.DestroyImmediate(normalVisit);
                UnityEngine.Object.DestroyImmediate(boostedOrder);
                UnityEngine.Object.DestroyImmediate(normalOrder);
            }
        }

        private static CustomerOrderData CreateValidationOrder(string key, bool highAbv)
        {
            CustomerOrderData order = ScriptableObject.CreateInstance<CustomerOrderData>();
            order.key = key;
            order.requestedRecipeId = "rec_1001";
            if (highAbv) order.tags.Add("high_abv");
            return order;
        }

        private static CustomerVisitData CreateValidationVisit(
            string key,
            CustomerOrderData order)
        {
            CustomerVisitData visit = ScriptableObject.CreateInstance<CustomerVisitData>();
            visit.visitKey = key;
            visit.weight = 1f;
            visit.members.Add(new CustomerVisitMember { characterKey = key });
            visit.orders.Add(new CustomerVisitOrderOption
            {
                order = order,
                weight = 1f,
                condition = new EpisodeTriggerCondition()
            });
            return visit;
        }

        private static bool HasCustomerTargetTag(string targetTag)
        {
            string[] guids = AssetDatabase.FindAssets("t:CustomerVisitData");
            for (int i = 0; i < guids.Length; i++)
            {
                CustomerVisitData visit = AssetDatabase.LoadAssetAtPath<CustomerVisitData>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (visit?.tags != null && visit.tags.Any(
                        tag => string.Equals(tag, targetTag, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidatePrefabAndScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(prefab != null, "TVSystem 프리팹이 없습니다.");
            Require(prefab.transform is not RectTransform,
                "TVSystem 프리팹 루트가 아직 Canvas용 RectTransform입니다.");
            Require(prefab.GetComponent<TVRestBootstrap>() != null,
                "TVSystem 프리팹에 휴식 진입 추첨기가 없습니다.");
            TVSystemController prefabController = prefab.GetComponent<TVSystemController>();
            Require(prefabController != null,
                "TVSystem 프리팹에 월드/UI 연결 컨트롤러가 없습니다.");
            Require(prefab.GetComponentInChildren<SpriteRenderer>(true) != null,
                "TVSystem 프리팹에 월드 SpriteRenderer가 없습니다.");
            Require(prefab.GetComponent<Collider2D>() != null
                && prefab.GetComponent<Rigidbody2D>() != null,
                "TVSystem 프리팹에 월드 클릭용 2D 물리가 없습니다.");
            Require(prefab.GetComponent<ObjectInteraction>() != null,
                "TVSystem 프리팹에 기존 오브젝트 상호작용이 없습니다.");

            GameObject panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            Require(panelPrefab != null, "TVPanel 프리팹이 없습니다.");
            TVUIManager panelManager = panelPrefab.GetComponent<TVUIManager>();
            Require(panelManager != null, "TVPanel 프리팹에 TV UI가 없습니다.");
            Require(panelPrefab.GetComponentInChildren<TVTicker>(true) != null,
                "TVPanel 프리팹에 순환 자막이 없습니다.");
            Require(prefabController.panelPrefab == panelManager,
                "TVSystem 프리팹이 TVPanel 프리팹을 참조하지 않습니다.");
            RectTransform frame = panelPrefab.transform.Find("TVFrame") as RectTransform;
            Require(frame != null && frame.anchorMin.x >= 0.99f,
                "TV 프레임이 화면 오른쪽 기준으로 배치되지 않았습니다.");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject root = GameObject.Find("TVSystemRoot");
            Require(root != null && root.scene == scene,
                "RestScene에 TVSystemRoot가 배치되지 않았습니다.");
            Require(root.GetComponentInParent<Canvas>() == null,
                "RestScene의 TV 본체가 아직 Canvas 아래에 있습니다.");
            TVSystemController sceneController = root.GetComponent<TVSystemController>();
            Require(sceneController != null && sceneController.uiCanvas != null,
                "RestScene 월드 TV가 기존 Canvas와 연결되지 않았습니다.");
            Require(sceneController.uiCanvas.renderMode != RenderMode.WorldSpace,
                "TV 방송 UI가 연결된 Canvas가 화면 공간 Canvas가 아닙니다.");
        }

        private static SaveData Capture(GameProgress progress)
        {
            return new SaveData
            {
                dayCount = progress.CurrentDay,
                flags = progress.GetFlagList(),
                completedEpisodeIds = progress.GetCompletedList(),
                affinityKeys = progress.GetAffinityKeys(),
                affinityValues = progress.GetAffinityValues(),
                boardSlotKeys = progress.GetBoardSlotKeys(),
                boardSlotValues = progress.GetBoardSlotValues(),
                bottleAmountKeys = progress.GetBottleAmountKeys(),
                bottleAmountValues = progress.GetBottleAmountValues(),
                customerAppearanceKeys = progress.GetCustomerAppearanceKeys(),
                customerAppearanceValues = progress.GetCustomerAppearanceValues(),
                upgradeKeys = progress.GetUpgradeKeys(),
                upgradeValues = progress.GetUpgradeValues(),
                currentChapterId = progress.CurrentChapterId,
                currentMoney = progress.CurrentMoney,
                reputation = progress.Reputation,
                dayDrinkSalesCount = progress.DayDrinkSalesCount,
                dayDrinkBaseRevenue = progress.DayDrinkBaseRevenue,
                dayDrinkTipRevenue = progress.DayDrinkTipRevenue,
                dayDrinkRevenue = progress.DayDrinkRevenue,
                dayTotalIncome = progress.DayTotalIncome,
                dayReputationDelta = progress.DayReputationDelta,
                dayDrinkSales = progress.GetDayDrinkSales(),
                tvForecastBroadcastId = progress.TVForecastBroadcastId,
                tvForecastRevealed = progress.TVForecastRevealed,
                tvActiveBroadcastId = progress.TVActiveBroadcastId,
                tvActiveBusinessDay = progress.TVActiveBusinessDay
            };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
