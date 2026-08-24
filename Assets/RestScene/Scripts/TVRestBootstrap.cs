using System.Collections;
using Slainte.Business;
using UnityEngine;

namespace Slainte.TV
{
    public sealed class TVRestBootstrap : MonoBehaviour
    {
        public TVBroadcastDatabase database;

        private IEnumerator Start()
        {
            while (GameProgress.Instance == null || DataManager.Instance == null)
                yield return null;

            BusinessOrderFlowSettings settings = BusinessOrderFlowSettings.LoadDefault();
            if (settings == null || !settings.IsTVUnlocked(GameProgress.Instance))
                yield break;

            bool hadForecast = !string.IsNullOrWhiteSpace(
                GameProgress.Instance.TVForecastBroadcastId);
            TVBroadcastEntry entry = TVBroadcastRuntime.EnsureForecast(
                GameProgress.Instance,
                database);
            if (!hadForecast && entry != null)
                DataManager.Instance.Save();
        }
    }
}
