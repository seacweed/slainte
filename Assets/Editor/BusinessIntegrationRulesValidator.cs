using System;
using Slainte.Business;
using Slainte.Economy;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class BusinessIntegrationRulesValidator
    {
        [MenuItem("Slainte/Business/Validate Timer Sale And Settlement Rules")]
        public static void ValidateFromMenu()
        {
            RunValidation();
            EditorUtility.DisplayDialog(
                "Business Integration Rules",
                "타이머, 팁, 실제 손님 일반 돈/이상한 동전 결제, 판매 기록과 정산 지급 검증을 통과했습니다.",
                "확인");
        }

        public static void RunBatchValidation()
        {
            RunValidation();
        }

        private static void RunValidation()
        {
            ValidateClockRules();
            ValidateEncounterEpisodeType();
            ValidateSaveSuppressionNesting();
            ValidateRewardCalculation();
            ValidateCustomerPaymentRouting();
            ValidateDeferredSettlement();
            ValidateImmediateCurrencyPayout();
            ValidatePlanningInventoryMigration();
            Debug.Log(
                "[BusinessIntegrationRulesValidator] PASS: timer, Day-5 third-slot encounter, explicit pause, save isolation, "
                + "recipe-price rewards/tips, real-customer Money/StrangeCoin routing and immediate payout, detailed sale save, "
                + "deferred settlement payout and reset");
        }

        private static void ValidateClockRules()
        {
            float remaining = 10f;
            float active = 0f;
            BusinessShiftClock.Advance(ref remaining, ref active, 2.5f, paused: false);
            RequireApproximately(7.5f, remaining, "일반 영업 중 남은 시간이 감소하지 않았습니다.");
            RequireApproximately(2.5f, active, "유효 영업 시간이 누적되지 않았습니다.");

            BusinessShiftClock.Advance(ref remaining, ref active, 4f, paused: true);
            RequireApproximately(7.5f, remaining, "명시적 일시정지 중 남은 시간이 감소했습니다.");
            RequireApproximately(2.5f, active, "명시적 일시정지 중 유효 영업 시간이 증가했습니다.");

            BusinessShiftClock.Advance(ref remaining, ref active, 20f, paused: false);
            RequireApproximately(0f, remaining, "영업 시간이 0 아래로 내려갔습니다.");
            RequireApproximately(10f, active, "마지막 프레임에서 제한시간보다 많이 누적됐습니다.");
        }

        private static void ValidateEncounterEpisodeType()
        {
            EpisodeData episode = AssetDatabase.LoadAssetAtPath<EpisodeData>(
                "Assets/Resources/EpisodeData/EpisodeData_StrangeCoin_0.asset");
            Require(episode != null, "StrangeCoin_0 에피소드 데이터를 찾지 못했습니다.");
            Require(episode.episodeType == EpisodeType.Encounter,
                $"StrangeCoin_0의 EpisodeType이 Encounter가 아닙니다: {episode.episodeType}");
            Require(episode.triggerCondition != null
                    && episode.triggerCondition.minDay == 5,
                "StrangeCoin_0 에피소드는 Day 5부터 등장해야 합니다.");

            BusinessOrderFlowSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(
                    "Assets/Resources/Business/BusinessOrderFlowSettings.asset");
            Require(settings != null, "영업 설정 에셋을 찾지 못했습니다.");

            if (settings.randomEncounters != null)
            {
                for (int i = 0; i < settings.randomEncounters.Count; i++)
                {
                    Require(settings.randomEncounters[i]?.episode != episode,
                        "StrangeCoin_0은 랜덤 인카운터 풀에 남아 있으면 안 됩니다.");
                }
            }

            BusinessRequiredActionRule fixedRule = null;
            if (settings.requiredActions != null)
            {
                for (int i = 0; i < settings.requiredActions.Count; i++)
                {
                    BusinessRequiredActionRule candidate = settings.requiredActions[i];
                    if (candidate?.encounterEpisode == episode)
                    {
                        fixedRule = candidate;
                        break;
                    }
                }
            }

            Require(fixedRule != null
                    && fixedRule.actionType == BusinessRequiredActionType.EncounterEpisode
                    && fixedRule.timing == BusinessRequiredActionTiming.SequenceSlot
                    && fixedRule.sequenceSlot == 3
                    && fixedRule.condition != null
                    && fixedRule.condition.minDay == 5,
                "StrangeCoin_0은 Day 5 이후 3번 영업 슬롯의 필수 인카운터여야 합니다.");
        }

        private static void ValidateSaveSuppressionNesting()
        {
            bool initiallySuppressed = DataManager.AreDiskWritesSuppressed;
            DataManager.PushSaveSuppression();
            DataManager.PushSaveSuppression();
            Require(DataManager.AreDiskWritesSuppressed, "저장 억제 중첩 진입에 실패했습니다.");
            DataManager.PopSaveSuppression();
            Require(DataManager.AreDiskWritesSuppressed, "중첩 저장 억제가 너무 일찍 해제됐습니다.");
            DataManager.PopSaveSuppression();
            Require(DataManager.AreDiskWritesSuppressed == initiallySuppressed,
                "저장 억제 상태가 검증 전 상태로 복원되지 않았습니다.");
        }

        private static void ValidateDeferredSettlement()
        {
            GameProgress existing = GameProgress.Instance;
            GameObject host = null;
            GameProgress progress = existing;
            SaveData restore = existing != null ? Capture(existing) : null;

            try
            {
                if (progress == null)
                {
                    host = new GameObject("BusinessIntegrationRulesValidator_GameProgress");
                    progress = host.AddComponent<GameProgress>();
                }

                progress.LoadFrom(new SaveData
                {
                    dayCount = 1,
                    currentMoney = 500,
                    reputation = 10
                });

                DeferredSettlementSalePayoutPolicy policy = new();
                policy.Apply(new BusinessOrderSessionResult
                {
                    outcome = OrderSessionOutcome.Served,
                    accepted = true,
                    customerOrderKey = "validator_order",
                    customerVisitKey = "validator_visit",
                    requestedRecipeId = "rec_1003",
                    grade = OrderEvaluationGrade.Good,
                    customerMood = CustomerMood.Satisfied,
                    baseRevenue = 100,
                    tipAmount = 20,
                    moneyDelta = 120,
                    reputationDelta = 2
                }, progress);

                Require(progress.CurrentMoney == 500,
                    "판매 직후 돈이 지급됐습니다. 정산 전에는 돈이 변하면 안 됩니다.");
                Require(progress.Reputation == 12, "판매 평판이 기록되지 않았습니다.");
                Require(progress.DayDrinkSalesCount == 1, "판매 횟수가 기록되지 않았습니다.");
                Require(progress.DayDrinkBaseRevenue == 100, "기본 판매금이 기록되지 않았습니다.");
                Require(progress.DayDrinkTipRevenue == 20, "팁이 기록되지 않았습니다.");
                Require(progress.DayDrinkRevenue == 120, "총 판매금이 기록되지 않았습니다.");
                Require(progress.DayTotalIncome == 120, "정산 예정 수입이 기록되지 않았습니다.");
                Require(progress.DayReputationDelta == 2, "당일 명성 변화가 기록되지 않았습니다.");

                SaveData serialized = JsonUtility.FromJson<SaveData>(
                    JsonUtility.ToJson(Capture(progress)));
                progress.LoadFrom(serialized);
                Require(progress.GetDayDrinkSales().Count == 1,
                    "저장 후 상세 판매 기록이 복원되지 않았습니다.");
                Require(progress.GetDayDrinkSales()[0].tipAmount == 20,
                    "저장 후 팁 기록이 복원되지 않았습니다.");

                int paid = SettlementManager.ApplyRecordedIncome(progress);
                Require(paid == 120, "정산 지급액이 기본 판매금+팁과 다릅니다.");
                Require(progress.CurrentMoney == 620, "정산에서 돈과 팁이 지급되지 않았습니다.");

                progress.ResetDaySettlement();
                Require(progress.DayDrinkSalesCount == 0
                    && progress.DayDrinkBaseRevenue == 0
                    && progress.DayDrinkTipRevenue == 0
                    && progress.DayDrinkRevenue == 0
                    && progress.DayTotalIncome == 0
                    && progress.DayReputationDelta == 0
                    && progress.GetDayDrinkSales().Count == 0,
                    "정산 종료 후 일일 판매 기록이 초기화되지 않았습니다.");
            }
            finally
            {
                if (restore != null && progress != null)
                    progress.LoadFrom(restore);
                if (host != null)
                    UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void ValidateRewardCalculation()
        {
            BusinessOrderFlowSettings settings =
                ScriptableObject.CreateInstance<BusinessOrderFlowSettings>();
            try
            {
                settings.goodMoneyReward = 100;
                settings.midMoneyReward = 50;
                settings.badMoneyReward = 0;
                settings.goodReputationReward = 2;
                settings.midReputationReward = 0;
                settings.badReputationReward = -1;
                settings.satisfiedTipRate = 0.3f;
                settings.neutralTipRate = 0f;
                settings.dissatisfiedTipRate = 0f;
                settings.badPenaltyRate = 1.3f;
                settings.bigFishGoodBonusRate = 2f;
                settings.bigFishFailurePenaltyRate = 3f;

                BusinessOrderReward good = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Good,
                    settings);
                Require(good.Mood == CustomerMood.Satisfied,
                    "Good 결과가 만족 상태로 변환되지 않았습니다.");
                Require(good.BaseRevenue == 100 && good.TipAmount == 30
                    && good.TotalRevenue == 130 && good.ReputationDelta == 2,
                    "만족 보상 계산이 잘못됐습니다.");

                BusinessOrderReward mid = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Mid,
                    settings);
                Require(mid.Mood == CustomerMood.Neutral,
                    "Mid 결과가 보통 상태로 변환되지 않았습니다.");
                Require(mid.BaseRevenue == 50 && mid.TipAmount == 0
                    && mid.TotalRevenue == 50 && mid.ReputationDelta == 0,
                    "보통 보상 계산이 잘못됐습니다.");

                BusinessOrderReward bad = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Bad,
                    settings);
                Require(bad.Mood == CustomerMood.Dissatisfied
                    && bad.TipAmount == 0 && bad.ReputationDelta == -1,
                    "불만족 보상 계산이 잘못됐습니다.");

                BusinessOrderReward pricedGood = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Good,
                    100,
                    settings);
                Require(pricedGood.BaseRevenue == 100
                    && pricedGood.TipAmount == 30
                    && pricedGood.TotalRevenue == 130,
                    "Good 결과가 정가 130%에 연결되지 않았습니다.");

                BusinessOrderReward pricedMid = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Mid,
                    100,
                    settings);
                Require(pricedMid.BaseRevenue == 100
                    && pricedMid.TipAmount == 0
                    && pricedMid.TotalRevenue == 100,
                    "Mid 결과가 정가 100%에 연결되지 않았습니다.");

                BusinessOrderReward pricedBad = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Bad,
                    100,
                    settings,
                    currentMoney: 0);
                Require(pricedBad.BaseRevenue == 100
                    && pricedBad.PenaltyAmount == 130
                    && pricedBad.TotalRevenue == -30,
                    "Bad 결과가 정가 -30% 또는 음수 잔액 허용 규칙에 연결되지 않았습니다.");

                BusinessOrderReward bigFishGood = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Good,
                    100,
                    settings,
                    rewardProfile: BusinessCustomerRewardProfile.BigFish);
                Require(bigFishGood.TipAmount == 200
                    && bigFishGood.TotalRevenue == 300,
                    "거물 Good 결과가 정가 300%에 연결되지 않았습니다.");

                BusinessOrderReward bigFishMid = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Mid,
                    100,
                    settings,
                    currentMoney: 0,
                    rewardProfile: BusinessCustomerRewardProfile.BigFish);
                Require(bigFishMid.PenaltyAmount == 300
                    && bigFishMid.TotalRevenue == -200,
                    "거물 Mid/Bad 결과가 정가 -200%에 연결되지 않았습니다.");

                BusinessOrderReward free = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Good,
                    0,
                    settings);
                Require(free.BaseRevenue == 0 && free.TotalRevenue == 0,
                    "0원 레시피 보상이 0으로 유지되지 않았습니다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        private static void ValidateImmediateCurrencyPayout()
        {
            GameProgress existing = GameProgress.Instance;
            GameObject host = null;
            GameProgress progress = existing;
            SaveData restore = existing != null ? Capture(existing) : null;

            try
            {
                if (progress == null)
                {
                    host = new GameObject("BusinessCurrencyValidator_GameProgress");
                    progress = host.AddComponent<GameProgress>();
                }

                progress.LoadFrom(new SaveData
                {
                    dayCount = 1,
                    currentMoney = 500,
                    affinityKeys = new System.Collections.Generic.List<string>
                    {
                        GameCurrencyWallet.StrangeCoinVariableName
                    },
                    affinityValues = new System.Collections.Generic.List<int> { 10 }
                });

                BusinessOrderFlowSettings settings =
                    AssetDatabase.LoadAssetAtPath<BusinessOrderFlowSettings>(
                        "Assets/Resources/Business/BusinessOrderFlowSettings.asset");
                Require(settings != null, "실제 영업 보상 설정을 찾지 못했습니다.");
                CustomerVisitData moneyVisit = LoadVisit("d1001");
                CustomerVisitData strangeCoinVisit = LoadVisit("d1016");
                GameCurrency moneyCurrency = ResolveVisitCurrency(moneyVisit);
                GameCurrency strangeCoinCurrency = ResolveVisitCurrency(strangeCoinVisit);
                BusinessOrderReward moneyReward = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Good,
                    100,
                    settings);
                BusinessOrderReward strangeCoinReward = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Good,
                    10,
                    settings);

                ImmediateSalePayoutPolicy policy = new();
                policy.Apply(new BusinessOrderSessionResult
                {
                    outcome = OrderSessionOutcome.Served,
                    accepted = true,
                    customerVisitKey = moneyVisit.visitKey,
                    paymentCurrency = moneyCurrency,
                    listedPrice = 100,
                    grade = OrderEvaluationGrade.Good,
                    customerMood = moneyReward.Mood,
                    baseRevenue = moneyReward.BaseRevenue,
                    tipAmount = moneyReward.TipAmount,
                    moneyDelta = moneyReward.TotalRevenue,
                    totalPayment = moneyReward.TotalRevenue
                }, progress);

                Require(moneyCurrency == GameCurrency.Money,
                    "일반 손님이 일반 화폐로 라우팅되지 않았습니다.");
                Require(moneyReward.TipAmount == 30 && progress.CurrentMoney == 630,
                    "일반 손님의 정가 100+팁 30이 일반 화폐로 즉시 지급되지 않았습니다.");
                Require(GameCurrencyWallet.GetBalance(progress, GameCurrency.StrangeCoin) == 10,
                    "일반 화폐 판매가 이상한 동전 잔액을 변경했습니다.");
                Require(progress.DayPaidMoneyIncome == 130,
                    "즉시 지급된 일반 화폐와 팁이 정산 중복 지급 방지값에 기록되지 않았습니다.");
                Require(SettlementManager.ApplyRecordedIncome(progress) == 0
                    && progress.CurrentMoney == 630,
                    "즉시 지급된 일반 화폐가 정산에서 중복 지급됐습니다.");

                policy.Apply(new BusinessOrderSessionResult
                {
                    outcome = OrderSessionOutcome.Served,
                    accepted = true,
                    customerVisitKey = strangeCoinVisit.visitKey,
                    paymentCurrency = strangeCoinCurrency,
                    listedPrice = 10,
                    grade = OrderEvaluationGrade.Good,
                    customerMood = strangeCoinReward.Mood,
                    baseRevenue = strangeCoinReward.BaseRevenue,
                    tipAmount = strangeCoinReward.TipAmount,
                    strangeCoinDelta = strangeCoinReward.TotalRevenue,
                    totalPayment = strangeCoinReward.TotalRevenue
                }, progress);

                Require(strangeCoinCurrency == GameCurrency.StrangeCoin,
                    "이상한 동전 손님이 이상한 동전으로 라우팅되지 않았습니다.");
                Require(strangeCoinReward.TipAmount == 3
                    && GameCurrencyWallet.GetBalance(progress, GameCurrency.StrangeCoin) == 23,
                    "이상한 동전 손님의 정가 10+팁 3이 이상한 동전으로 즉시 지급되지 않았습니다.");
                Require(progress.CurrentMoney == 630,
                    "이상한 동전 판매가 일반 화폐 잔액을 변경했습니다.");
                Require(progress.DayPaidStrangeCoinIncome == 13,
                    "즉시 지급된 이상한 동전과 팁이 정산 중복 지급 방지값에 기록되지 않았습니다.");
                SettlementManager.ApplyRecordedIncome(progress);
                Require(GameCurrencyWallet.GetBalance(progress, GameCurrency.StrangeCoin) == 23,
                    "즉시 지급된 이상한 동전이 정산에서 중복 지급됐습니다.");
                Require(progress.GetDayDrinkSales().Count == 2
                    && progress.GetDayDrinkSales()[1].paymentCurrency == GameCurrency.StrangeCoin,
                    "판매 기록에 결제 화폐가 보존되지 않았습니다.");
            }
            finally
            {
                if (restore != null && progress != null)
                    progress.LoadFrom(restore);
                if (host != null)
                    UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void ValidateCustomerPaymentRouting()
        {
            string[] strangeCoinVisitIds =
            {
                "d1016", "d1017", "d1018", "d1019",
                "d1020", "d1021", "d1033", "d1034"
            };
            for (int i = 0; i < strangeCoinVisitIds.Length; i++)
            {
                CustomerVisitData visit = LoadVisit(strangeCoinVisitIds[i]);
                Require(ResolveVisitCurrency(visit) == GameCurrency.StrangeCoin,
                    $"이상한 동전 대상 방문이 일반 화폐로 라우팅됩니다: {visit.visitKey}");
            }

            string[] moneyVisitIds = { "d1001", "d1038", "d1040" };
            for (int i = 0; i < moneyVisitIds.Length; i++)
            {
                CustomerVisitData visit = LoadVisit(moneyVisitIds[i]);
                Require(ResolveVisitCurrency(visit) == GameCurrency.Money,
                    $"일반 화폐 대상 방문이 이상한 동전으로 라우팅됩니다: {visit.visitKey}");
            }

            Require(BusinessCustomerRules.ResolveRewardProfile(LoadVisit("d1027"))
                    == BusinessCustomerRewardProfile.BigFish,
                "big_fish 방문이 거물 보상 프로필로 라우팅되지 않았습니다.");
        }

        private static CustomerVisitData LoadVisit(string visitKey)
        {
            CustomerVisitData visit = AssetDatabase.LoadAssetAtPath<CustomerVisitData>(
                $"Assets/Data/CustomerImport/DraftVisits/CustomerVisit_{visitKey}.asset");
            Require(visit != null, $"실제 손님 방문 에셋을 찾지 못했습니다: {visitKey}");
            return visit;
        }

        private static GameCurrency ResolveVisitCurrency(CustomerVisitData visit)
        {
            GameCurrency fallback = visit?.orders != null
                && visit.orders.Count > 0
                && visit.orders[0]?.order != null
                    ? visit.orders[0].order.paymentCurrency
                    : GameCurrency.Money;
            return BusinessCustomerRules.ResolvePaymentCurrency(visit, fallback);
        }

        private static void ValidatePlanningInventoryMigration()
        {
            GameProgress existing = GameProgress.Instance;
            GameObject host = null;
            GameProgress progress = existing;
            SaveData restore = existing != null ? Capture(existing) : null;

            try
            {
                if (progress == null)
                {
                    host = new GameObject("PlanningInventoryMigrationValidator_GameProgress");
                    progress = host.AddComponent<GameProgress>();
                }

                progress.LoadFrom(new SaveData
                {
                    dayCount = 1,
                    bottleAmountKeys = new System.Collections.Generic.List<string>
                    {
                        "item_1005", // old Slop
                        "nanangna",  // semantic old Nanangna
                        "item_1007", // numeric old Nanangna
                        "coffee_powder"
                    },
                    bottleAmountValues = new System.Collections.Generic.List<float>
                    {
                        55f,
                        70f,
                        60f,
                        25f
                    }
                });

                Require(Mathf.Approximately(progress.GetBottleAmount("item_1004", -1f), 55f),
                    "구형 슬롭 재고가 새 item_1004로 이관되지 않았습니다.");
                Require(Mathf.Approximately(progress.GetBottleAmount("item_1005", -1f), 70f),
                    "구형 나낭나 별칭 재고가 새 item_1005로 이관되지 않았습니다.");
                Require(Mathf.Approximately(progress.GetBottleAmount("item_1015", -1f), 25f),
                    "구형 커피 분말 재고가 새 item_1015로 이관되지 않았습니다.");
                Require(Mathf.Approximately(progress.GetBottleAmount("nanangna", -1f), -1f),
                    "이관 후 구형 재고 별칭이 남아 있습니다.");
                Require(progress.HasFlag("csv_item_ids_v2"),
                    "재고 ID 이관 완료 플래그가 저장되지 않았습니다.");

                SaveData migrated = Capture(progress);
                progress.LoadFrom(migrated);
                Require(Mathf.Approximately(progress.GetBottleAmount("item_1004", -1f), 55f)
                    && Mathf.Approximately(progress.GetBottleAmount("item_1005", -1f), 70f),
                    "재고 ID 이관이 저장 재로드 때 중복 적용됐습니다.");

                progress.LoadFrom(new SaveData
                {
                    dayCount = 1,
                    flags = new System.Collections.Generic.List<string> { "csv_item_ids_v2" }
                });
                Require(Mathf.Approximately(
                        progress.EnsureBottleAmount("item_1001", 3000f),
                        3000f),
                    "CSV 기본 재고가 최초 접근 때 생성되지 않았습니다.");
                Require(progress.GetBottleAmountKeys().Contains("item_1001"),
                    "생성된 CSV 기본 재고가 저장 목록에 연결되지 않았습니다.");
                Require(Mathf.Approximately(
                        progress.AddBottleAmount("item_1001", 1000f, 6000f),
                        4000f),
                    "구매 재고가 CSV 기본 재고에 누적되지 않았습니다.");

                LiquorBottleDef legacyJohnny = ScriptableObject.CreateInstance<LiquorBottleDef>();
                try
                {
                    legacyJohnny.id = "johnny_dogs";
                    legacyJohnny.bottleCount = 6;
                    legacyJohnny.defaultBottleCount = 4;
                    legacyJohnny.unitVolume = 700f;
                    Require(legacyJohnny.InventoryId == "item_1010",
                        "구형 조니 독스 ID가 새 item_1010 재고 ID로 변환되지 않았습니다.");
                    Require(Mathf.Approximately(
                            progress.EnsureBottleAmount(
                                legacyJohnny.InventoryId,
                                legacyJohnny.DefaultAmount),
                            2800f),
                        "구형 조니 독스 에셋이 새 기본 재고를 생성하지 못했습니다.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(legacyJohnny);
                }
            }
            finally
            {
                if (restore != null && progress != null)
                    progress.LoadFrom(restore);
                if (host != null)
                    UnityEngine.Object.DestroyImmediate(host);
            }
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
                dayPaidMoneyIncome = progress.DayPaidMoneyIncome,
                dayStrangeCoinBaseRevenue = progress.DayStrangeCoinBaseRevenue,
                dayStrangeCoinTipRevenue = progress.DayStrangeCoinTipRevenue,
                dayStrangeCoinRevenue = progress.DayStrangeCoinRevenue,
                dayPaidStrangeCoinIncome = progress.DayPaidStrangeCoinIncome,
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

        private static void RequireApproximately(float expected, float actual, string message)
        {
            if (Mathf.Abs(expected - actual) > 0.001f)
                throw new InvalidOperationException(
                    $"{message} expected={expected}, actual={actual}");
        }
    }
}
