using System;
using Slainte.Business;
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
                "타이머, 저장 격리, 판매 기록, 정산 지급 검증을 통과했습니다.",
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
            ValidateDeferredSettlement();
            Debug.Log(
                "[BusinessIntegrationRulesValidator] PASS: timer, encounter type, explicit pause, save isolation, "
                + "mood/tip calculation, detailed sale save, deferred settlement payout and reset");
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
                    requestedRecipeId = "vodka_lemon",
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
                settings.satisfiedTipRate = 0.2f;
                settings.neutralTipRate = 0.05f;
                settings.dissatisfiedTipRate = 0f;

                BusinessOrderReward good = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Good,
                    settings);
                Require(good.Mood == CustomerMood.Satisfied,
                    "Good 결과가 만족 상태로 변환되지 않았습니다.");
                Require(good.BaseRevenue == 100 && good.TipAmount == 20
                    && good.TotalRevenue == 120 && good.ReputationDelta == 2,
                    "만족 보상 계산이 잘못됐습니다.");

                BusinessOrderReward mid = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Mid,
                    settings);
                Require(mid.Mood == CustomerMood.Neutral,
                    "Mid 결과가 보통 상태로 변환되지 않았습니다.");
                Require(mid.BaseRevenue == 50 && mid.TipAmount == 2
                    && mid.TotalRevenue == 52 && mid.ReputationDelta == 0,
                    "보통 팁의 1원 미만 버림 계산이 잘못됐습니다.");

                BusinessOrderReward bad = BusinessOrderRewardCalculator.Calculate(
                    OrderEvaluationGrade.Bad,
                    settings);
                Require(bad.Mood == CustomerMood.Dissatisfied
                    && bad.TipAmount == 0 && bad.ReputationDelta == -1,
                    "불만족 보상 계산이 잘못됐습니다.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
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
