using System;
using System.Collections;
using UnityEngine;

namespace Slainte.Business
{
    public sealed class BusinessSequenceRunner : MonoBehaviour
    {
        private BusinessOrderSessionController orderSession;
        private BusinessOrderSessionUI ui;
        private BusinessOrderFlowSettings settings;
        private BusinessDaySnapshot snapshot;
        private Coroutine continueRoutine;
        private bool initialized;
        private bool started;

        public bool IsRunning => started && snapshot != null && !snapshot.isCompleted;
        public event Action BusinessDayCompleted;

        public void Initialize(
            BusinessOrderSessionController session,
            BusinessOrderSessionUI sessionUi,
            BusinessOrderFlowSettings flowSettings)
        {
            if (initialized)
                return;

            orderSession = session;
            ui = sessionUi;
            settings = flowSettings;
            if (orderSession != null)
                orderSession.OrderCompleted += HandleOrderCompleted;
            initialized = true;
        }

        private void OnDestroy()
        {
            if (orderSession != null)
                orderSession.OrderCompleted -= HandleOrderCompleted;
        }

        public void StartSequence()
        {
            if (!initialized || started)
                return;

            GameProgress progress = GameProgress.Instance;
            if (progress == null)
            {
                ui?.ShowError("GameProgress is unavailable. Business sequence cannot start.");
                return;
            }

            snapshot = progress.GetBusinessDaySnapshot();
            if (!IsUsableSnapshot(snapshot, progress.CurrentDay))
            {
                snapshot = BusinessSequencePlanner.CreateFixed(progress.CurrentDay, settings);
                progress.SetBusinessDaySnapshot(snapshot);
                DataManager.Instance?.Save();
            }

            started = true;
            BeginCurrentEntry();
        }

        private void BeginCurrentEntry()
        {
            if (snapshot == null || snapshot.entries == null || snapshot.currentIndex >= snapshot.entries.Count)
            {
                CompleteBusinessDay();
                return;
            }

            BusinessSequenceEntrySnapshot entry = snapshot.entries[snapshot.currentIndex];
            ui?.ShowSequenceProgress(snapshot.currentIndex + 1, snapshot.entries.Count);

            if (entry != null && string.Equals(
                entry.entryType,
                BusinessSequencePlanner.OrderEntryType,
                StringComparison.OrdinalIgnoreCase))
            {
                orderSession?.BeginOrder(entry);
                return;
            }

            Debug.LogWarning("[BusinessSequenceRunner] Unsupported sequence entry was skipped.");
            AdvanceAndSave();
            ScheduleNextEntry();
        }

        private void HandleOrderCompleted(BusinessOrderSessionResult result)
        {
            if (!started || result == null || result.owner != OrderSessionOwner.Business)
                return;

            AdvanceAndSave();
            ScheduleNextEntry();
        }

        private void AdvanceAndSave()
        {
            GameProgress progress = GameProgress.Instance;
            if (progress == null)
                return;

            progress.AdvanceBusinessSequence();
            snapshot = progress.GetBusinessDaySnapshot();
            DataManager.Instance?.Save();
        }

        private void ScheduleNextEntry()
        {
            if (continueRoutine != null)
                StopCoroutine(continueRoutine);
            continueRoutine = StartCoroutine(ContinueNextFrame());
        }

        private IEnumerator ContinueNextFrame()
        {
            yield return null;
            continueRoutine = null;
            BeginCurrentEntry();
        }

        private void CompleteBusinessDay()
        {
            if (snapshot != null)
                snapshot.isCompleted = true;

            GameProgress progress = GameProgress.Instance;
            if (progress != null && snapshot != null)
                progress.SetBusinessDaySnapshot(snapshot);
            DataManager.Instance?.Save();

            ui?.ShowDayComplete();
            BusinessDayCompleted?.Invoke();
        }

        private static bool IsUsableSnapshot(BusinessDaySnapshot candidate, int currentDay)
        {
            return candidate != null
                && candidate.day == currentDay
                && candidate.HasEntries;
        }
    }
}
