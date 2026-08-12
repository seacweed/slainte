using System.Collections;
using UnityEngine;

namespace Slainte.Business
{
    [DefaultExecutionOrder(-2000)]
    [DisallowMultipleComponent]
    public sealed class PlaytestProgressIsolation : MonoBehaviour
    {
        private SaveData snapshot;
        private GameProgress capturedProgress;
        private bool suppressionPushed;

        public bool IsReady { get; private set; }

        public static PlaytestProgressIsolation Attach(GameObject host)
        {
            if (host == null)
                return null;

            PlaytestProgressIsolation isolation =
                host.GetComponent<PlaytestProgressIsolation>();
            return isolation != null
                ? isolation
                : host.AddComponent<PlaytestProgressIsolation>();
        }

        private void Awake()
        {
            DataManager.PushSaveSuppression();
            suppressionPushed = true;
        }

        private IEnumerator Start()
        {
            while (GameProgress.Instance == null)
                yield return null;

            capturedProgress = GameProgress.Instance;
            snapshot = Capture(capturedProgress);
            IsReady = true;
            Debug.Log("[PlaytestIsolation] Progress snapshot captured; disk saves are disabled.");
        }

        public void RestoreNow()
        {
            if (snapshot == null || capturedProgress == null)
                return;

            capturedProgress.LoadFrom(snapshot);
            Debug.Log("[PlaytestIsolation] Progress restored from the pre-playtest snapshot.");
        }

        private void OnDestroy()
        {
            RestoreNow();
            if (suppressionPushed)
            {
                DataManager.PopSaveSuppression();
                suppressionPushed = false;
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
                dayDrinkRevenue = progress.DayDrinkRevenue,
                dayTotalIncome = progress.DayTotalIncome
            };
        }
    }
}
