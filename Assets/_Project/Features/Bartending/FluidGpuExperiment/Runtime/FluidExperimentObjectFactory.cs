using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    internal static class FluidExperimentObjectFactory
    {
        internal static GameObject CreateInactive(GameObject template, Transform parent, string name)
        {
            if (template == null)
            {
                var empty = new GameObject(name);
                empty.SetActive(false); empty.transform.SetParent(parent, false);
                return empty;
            }
            // User-authored templates may be active. Parent staging defers all Awake/OnEnable callbacks.
            var staging = new GameObject("Inactive template staging");
            staging.SetActive(false); staging.transform.SetParent(parent, false);
            try
            {
                var instance = Object.Instantiate(template, staging.transform, false);
                instance.SetActive(false); instance.name = name;
                instance.transform.SetParent(parent, false);
                return instance;
            }
            finally
            {
                if (Application.isPlaying) Object.Destroy(staging);
                else Object.DestroyImmediate(staging);
            }
        }
    }
}
