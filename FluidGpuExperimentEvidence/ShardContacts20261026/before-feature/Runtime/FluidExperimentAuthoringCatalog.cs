using System;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    [CreateAssetMenu(menuName = "Slainte/Fluid Experiment/Authoring Catalog")]
    public sealed class FluidExperimentAuthoringCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public string displayName;
            public string category;
            public GameObject prefab;
            public ItemDef ingredient;
        }
        public Entry[] entries = Array.Empty<Entry>();
        public Entry Find(string id) => Array.Find(entries, entry => entry != null && entry.id == id);
        public Entry Find(ItemDef ingredient) => ingredient == null ? null
            : Array.Find(entries, entry => entry != null && entry.ingredient == ingredient);
    }
}
