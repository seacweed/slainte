using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    public sealed class ToolCabinetShiftState
    {
        private readonly Dictionary<string, int> counts =
            new(StringComparer.OrdinalIgnoreCase);

        public int GetCount(string id, int fallback)
        {
            return !string.IsNullOrWhiteSpace(id) && counts.TryGetValue(id, out int value)
                ? value
                : fallback;
        }

        public void SetCount(string id, int value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            counts[id.Trim()] = Mathf.Clamp(value, 0, Mathf.Max(0, maximum));
        }

        public void Clear()
        {
            counts.Clear();
        }
    }
}
