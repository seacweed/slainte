using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    public sealed class ItemDefCatalog
    {
        private readonly Dictionary<string, ItemDef> itemsById = new(System.StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, ItemDef> ItemsById => itemsById;

        public static ItemDefCatalog LoadFromResources(string resourcesPath, IEnumerable<ItemDef> additionalItems = null)
        {
            ItemDefCatalog catalog = new ItemDefCatalog();

            ItemDef[] resourceItems = Resources.LoadAll<ItemDef>(resourcesPath);
            for (int i = 0; i < resourceItems.Length; i++)
                catalog.Add(resourceItems[i]);

            if (additionalItems != null)
            {
                foreach (ItemDef item in additionalItems)
                    catalog.Add(item);
            }

            return catalog;
        }

        public bool TryGet(string id, out ItemDef item)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                item = null;
                return false;
            }

            return itemsById.TryGetValue(id.Trim(), out item);
        }

        public void Add(ItemDef item)
        {
            if (item == null)
                return;

            string id = GetLookupId(item);
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogWarning($"ItemDef '{item.name}' has no id and cannot be used by recipe CSV.");
                return;
            }

            if (itemsById.TryGetValue(id, out ItemDef existing) && existing != item)
            {
                Debug.LogWarning($"Duplicate ItemDef id '{id}'. Keeping '{existing.name}', ignoring '{item.name}'.");
                return;
            }

            itemsById[id] = item;
        }

        private static string GetLookupId(ItemDef item)
        {
            if (!string.IsNullOrWhiteSpace(item.id))
                return item.id.Trim();

            Debug.LogWarning($"ItemDef '{item.name}' has an empty id. Falling back to asset name for CSV lookup.");
            return item.name;
        }
    }
}
