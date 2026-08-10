using System;
using System.Collections.Generic;
using Slainte.Bartending;
using Slainte.Business;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class CustomerPoolSetup
    {
        private const string OrderPath =
            "Assets/Data/CustomerOrder/data/CustomerOrder_vertical_slice_vodka_lemon.asset";
        private const string OrderDatabasePath =
            "Assets/Data/CustomerOrder/CustomerOrderDatabase.asset";
        private const string VisitFolder = "Assets/Resources/CustomerVisit";
        private const string VisitDataFolder = "Assets/Resources/CustomerVisit/Data";
        private const string VisitPath =
            "Assets/Resources/CustomerVisit/Data/CustomerVisit_yukari_sample.asset";
        private const string VisitDatabasePath =
            "Assets/Resources/CustomerVisit/CustomerVisitDatabase.asset";
        private const string SettingsPath =
            "Assets/Resources/Business/BusinessOrderFlowSettings.asset";

        [MenuItem("Slainte/Business/손님 풀 샘플 설정 적용")]
        public static void Apply()
        {
            EnsureFolder(VisitFolder);
            EnsureFolder(VisitDataFolder);

            CustomerOrderData order = AssetDatabase.LoadAssetAtPath<CustomerOrderData>(OrderPath);
            if (order == null)
                throw new System.IO.FileNotFoundException("샘플 손님 주문 에셋이 없습니다.", OrderPath);

            order.requestedRecipeId = "vodka_lemon";
            order.orderType = CocktailOrderType.RecipeOrder;
            EditorUtility.SetDirty(order);

            CustomerVisitData visit = AssetDatabase.LoadAssetAtPath<CustomerVisitData>(VisitPath);
            bool newVisit = visit == null;
            if (newVisit)
                visit = ScriptableObject.CreateInstance<CustomerVisitData>();

            visit.name = "CustomerVisit_yukari_sample";
            visit.visitKey = "yukari_sample_visit";
            visit.tags = new List<string> { "sample" };
            visit.weight = 1f;
            visit.condition = new EpisodeTriggerCondition();
            visit.maxDay = 0;
            visit.cooldownDays = 0;
            // 실제 손님 데이터가 들어오기 전에도 하루 방문 수를 반복 테스트할 수 있게 한다.
            visit.allowDuplicateInDay = true;
            visit.members = new List<CustomerVisitMember>
            {
                new CustomerVisitMember
                {
                    characterKey = "yukari",
                    slotIndex = 0,
                    expressionKeyMid = "mid",
                    expressionKeyGood = "mid",
                    expressionKeyBad = "mid"
                }
            };
            visit.orders = new List<CustomerVisitOrderOption>
            {
                new CustomerVisitOrderOption
                {
                    order = order,
                    weight = 1f,
                    condition = new EpisodeTriggerCondition()
                }
            };

            if (newVisit)
                AssetDatabase.CreateAsset(visit, VisitPath);
            else
                EditorUtility.SetDirty(visit);

            CustomerVisitDatabase visitDatabase =
                AssetDatabase.LoadAssetAtPath<CustomerVisitDatabase>(VisitDatabasePath);
            bool newDatabase = visitDatabase == null;
            if (newDatabase)
                visitDatabase = ScriptableObject.CreateInstance<CustomerVisitDatabase>();
            visitDatabase.visits = new List<CustomerVisitData> { visit };
            if (newDatabase)
                AssetDatabase.CreateAsset(visitDatabase, VisitDatabasePath);
            else
                EditorUtility.SetDirty(visitDatabase);

            CustomerOrderDatabase orderDatabase =
                AssetDatabase.LoadAssetAtPath<CustomerOrderDatabase>(OrderDatabasePath);
            if (orderDatabase != null && !orderDatabase.customers.Contains(order))
            {
                orderDatabase.customers.Add(order);
                EditorUtility.SetDirty(orderDatabase);
            }

            BusinessOrderFlowSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(SettingsPath);
            if (settings == null)
                throw new System.IO.FileNotFoundException("영업 주문 설정 에셋이 없습니다.", SettingsPath);

            settings.sequenceMode = BusinessSequenceMode.CustomerPool;
            settings.customerVisitDatabase = visitDatabase;
            settings.minVisitsPerDay = 15;
            settings.maxVisitsPerDay = 15;
            EditorUtility.SetDirty(settings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[손님 풀 설정] 방문 유형 구분 없이 members 목록을 사용하는 샘플 풀을 적용했습니다.");
        }

        [MenuItem("Slainte/품질 검증/손님 풀 검증")]
        public static void Validate()
        {
            CustomerVisitDatabase database =
                AssetDatabase.LoadAssetAtPath<CustomerVisitDatabase>(VisitDatabasePath);
            BusinessOrderFlowSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(SettingsPath);
            CustomerOrderData order = AssetDatabase.LoadAssetAtPath<CustomerOrderData>(OrderPath);
            Require(database != null && database.visits.Count == 1, "샘플 방문 데이터베이스가 올바르지 않습니다.");
            Require(settings != null && settings.sequenceMode == BusinessSequenceMode.CustomerPool,
                "영업 설정이 손님 풀 모드가 아닙니다.");
            Require(order != null && order.requestedRecipeId == "vodka_lemon",
                "샘플 주문에 판정 레시피가 연결되지 않았습니다.");

            GameProgress existingProgress = UnityEngine.Object.FindFirstObjectByType<GameProgress>();
            bool createdProgress = existingProgress == null;
            GameProgress progress = existingProgress != null ? existingProgress : GameProgress.Instance;
            SaveData backup = CaptureProgress(progress);

            CustomerVisitData groupVisit = null;
            CustomerVisitDatabase groupDatabase = null;
            CustomerVisitData cooldownVisit = null;
            CustomerVisitDatabase cooldownDatabase = null;
            BusinessOrderFlowSettings temporarySettings = null;
            try
            {
                progress.LoadFrom(new SaveData { dayCount = 7 });
                BusinessDaySnapshot first = BusinessSequencePlanner.Create(7, settings, progress);
                BusinessDaySnapshot second = BusinessSequencePlanner.Create(7, settings, progress);
                int minimumVisits = Mathf.Max(0, settings.minVisitsPerDay);
                int maximumVisits = Mathf.Max(minimumVisits, settings.maxVisitsPerDay);
                Require(first.entries.Count >= minimumVisits && first.entries.Count <= maximumVisits,
                    "샘플 손님 수가 설정한 하루 방문 수 범위를 벗어났습니다.");
                Require(first.seed == second.seed
                    && first.entries[0].entryId == second.entries[0].entryId
                    && first.entries[0].visitKey == second.entries[0].visitKey,
                    "같은 날짜의 손님 추첨 결과가 결정적이지 않습니다.");

                cooldownVisit = ScriptableObject.CreateInstance<CustomerVisitData>();
                cooldownVisit.visitKey = "qa_cooldown_visit";
                cooldownVisit.weight = 1f;
                cooldownVisit.cooldownDays = 1;
                cooldownVisit.members = new List<CustomerVisitMember>
                {
                    new CustomerVisitMember { characterKey = "cooldown_member" }
                };
                cooldownVisit.orders = new List<CustomerVisitOrderOption>
                {
                    new CustomerVisitOrderOption
                    {
                        order = order,
                        weight = 1f,
                        condition = new EpisodeTriggerCondition()
                    }
                };
                cooldownDatabase = ScriptableObject.CreateInstance<CustomerVisitDatabase>();
                cooldownDatabase.visits = new List<CustomerVisitData> { cooldownVisit };

                progress.LoadFrom(new SaveData { dayCount = 7 });
                progress.RecordCustomerVisit(cooldownVisit.visitKey, 7);
                progress.SetCurrentDay(8);
                Require(BusinessSequencePlanner.CreateFromPool(
                    8, settings, cooldownDatabase, progress).entries.Count == 0,
                    "재등장 대기 1일이 적용되지 않았습니다.");
                progress.SetCurrentDay(9);
                Require(BusinessSequencePlanner.CreateFromPool(
                    9, settings, cooldownDatabase, progress).entries.Count == 1,
                    "재등장 대기 기간이 끝난 손님이 후보로 복귀하지 않았습니다.");

                groupVisit = ScriptableObject.CreateInstance<CustomerVisitData>();
                groupVisit.visitKey = "qa_multi_member_visit";
                groupVisit.weight = 1f;
                groupVisit.cooldownDays = 0;
                groupVisit.condition = new EpisodeTriggerCondition
                {
                    requiredFlags = new List<string> { "qa_group_allowed" }
                };
                groupVisit.members = new List<CustomerVisitMember>
                {
                    new CustomerVisitMember { characterKey = "member_a", slotIndex = 1 },
                    new CustomerVisitMember { characterKey = "member_b", slotIndex = 2 }
                };
                groupVisit.orders = new List<CustomerVisitOrderOption>
                {
                    new CustomerVisitOrderOption
                    {
                        order = order,
                        weight = 1f,
                        condition = new EpisodeTriggerCondition()
                    }
                };
                groupDatabase = ScriptableObject.CreateInstance<CustomerVisitDatabase>();
                groupDatabase.visits = new List<CustomerVisitData> { groupVisit };
                temporarySettings = ScriptableObject.CreateInstance<BusinessOrderFlowSettings>();
                temporarySettings.sequenceMode = BusinessSequenceMode.CustomerPool;
                temporarySettings.minVisitsPerDay = 1;
                temporarySettings.maxVisitsPerDay = 1;

                progress.LoadFrom(new SaveData { dayCount = 10 });
                Require(BusinessSequencePlanner.CreateFromPool(
                    10, temporarySettings, groupDatabase, progress).entries.Count == 0,
                    "필수 플래그가 없는 방문이 후보에 포함됐습니다.");
                progress.SetFlag("qa_group_allowed");
                BusinessDaySnapshot groupSnapshot = BusinessSequencePlanner.CreateFromPool(
                    10, temporarySettings, groupDatabase, progress);
                Require(groupSnapshot.entries.Count == 1
                    && groupSnapshot.entries[0].visitKey == "qa_multi_member_visit"
                    && groupVisit.members.Count == 2,
                    "members 목록 기반 다인 방문을 생성하지 못했습니다.");

                BusinessOrderFlowSettings fixedSettings =
                    ScriptableObject.CreateInstance<BusinessOrderFlowSettings>();
                fixedSettings.sequenceMode = BusinessSequenceMode.Fixed;
                fixedSettings.fixedOrders.Add(new FixedBusinessOrder
                {
                    customerOrderKey = "fixed_qa",
                    requestedRecipeId = "vodka_lemon"
                });
                Require(BusinessSequencePlanner.CreateFixed(10, fixedSettings).entries.Count == 1,
                    "기존 고정 주문 모드가 손상됐습니다.");
                UnityEngine.Object.DestroyImmediate(fixedSettings);

                Debug.Log("[손님 풀 검증] 통과: 결정론적 추첨, 조건 필터, 재등장 제한, 다인 members, 고정 모드 호환");
            }
            finally
            {
                if (createdProgress)
                    UnityEngine.Object.DestroyImmediate(progress.gameObject);
                else
                    progress.LoadFrom(backup);
                if (groupVisit != null) UnityEngine.Object.DestroyImmediate(groupVisit);
                if (groupDatabase != null) UnityEngine.Object.DestroyImmediate(groupDatabase);
                if (cooldownVisit != null) UnityEngine.Object.DestroyImmediate(cooldownVisit);
                if (cooldownDatabase != null) UnityEngine.Object.DestroyImmediate(cooldownDatabase);
                if (temporarySettings != null) UnityEngine.Object.DestroyImmediate(temporarySettings);
            }
        }

        private static SaveData CaptureProgress(GameProgress progress)
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
                money = progress.Money,
                reputation = progress.Reputation,
                businessDay = progress.GetBusinessDaySnapshot(),
                customerVisitHistory = progress.GetCustomerVisitHistory()
            };
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
