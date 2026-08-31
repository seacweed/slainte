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

            // The planning CSV is authoritative for numeric item_* IDs. Loading this
            // subfolder again with replacement makes the result deterministic even if
            // another Resources asset accidentally retains one of those IDs.
            string planningPath = string.IsNullOrWhiteSpace(resourcesPath)
                ? "Items/Planning"
                : resourcesPath.TrimEnd('/') + "/Planning";
            ItemDef[] planningItems = Resources.LoadAll<ItemDef>(planningPath);
            for (int i = 0; i < planningItems.Length; i++)
                catalog.AddOrReplace(planningItems[i]);

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
                Debug.LogWarning($"ItemDef '{item.name}'에 ID가 없어 레시피 CSV에서 사용할 수 없습니다.");
                return;
            }

            if (itemsById.TryGetValue(id, out ItemDef existing) && existing != item)
            {
                Debug.LogWarning($"ItemDef ID '{id}'가 중복되었습니다. '{existing.name}'을 유지하고 '{item.name}'은 무시합니다.");
                return;
            }

            itemsById[id] = item;
        }

        private void AddOrReplace(ItemDef item)
        {
            if (item == null)
                return;

            string id = GetLookupId(item);
            if (!string.IsNullOrWhiteSpace(id))
                itemsById[id] = item;
        }

        private static string GetLookupId(ItemDef item)
        {
            if (!string.IsNullOrWhiteSpace(item.id))
                return item.id.Trim();

            Debug.LogWarning($"ItemDef '{item.name}'의 ID가 비어 있어 CSV 검색에 에셋 이름을 대신 사용합니다.");
            return item.name;
        }
    }
}
