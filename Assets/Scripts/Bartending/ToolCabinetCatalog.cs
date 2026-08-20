using System;
using UnityEngine;

namespace Slainte.Bartending
{
    [CreateAssetMenu(menuName = "Bartending/Tool Cabinet Catalog")]
    public sealed class ToolCabinetCatalog : ScriptableObject
    {
        public Sprite backgroundSprite;
        [Tooltip("Pixel-space storage rectangle in tool_cabinet.png, using a bottom-left origin.")]
        public Rect toolStoragePixels = new Rect(54f, 321f, 1334f, 447f);
        [Tooltip("Pixel-space glass rectangle in tool_cabinet.png, using a bottom-left origin.")]
        public Rect glassStoragePixels = new Rect(1456f, 321f, 1043f, 447f);
        [Tooltip("Pixel-space ice-maker rectangle in tool_cabinet.png, using a bottom-left origin.")]
        public Rect iceMakerPixels = new Rect(1308f, 60f, 1190f, 187f);
        public ToolDef[] tools = Array.Empty<ToolDef>();
        public GlassDef[] glasses = Array.Empty<GlassDef>();

        public bool TryGetNormalizedRect(Rect pixelRect, out Rect normalized)
        {
            if (backgroundSprite == null
                || backgroundSprite.rect.width <= Mathf.Epsilon
                || backgroundSprite.rect.height <= Mathf.Epsilon
                || pixelRect.width <= Mathf.Epsilon
                || pixelRect.height <= Mathf.Epsilon)
            {
                normalized = default;
                return false;
            }

            float width = backgroundSprite.rect.width;
            float height = backgroundSprite.rect.height;
            normalized = Rect.MinMaxRect(
                Mathf.Clamp01(pixelRect.xMin / width),
                Mathf.Clamp01(pixelRect.yMin / height),
                Mathf.Clamp01(pixelRect.xMax / width),
                Mathf.Clamp01(pixelRect.yMax / height));
            return normalized.width > Mathf.Epsilon
                && normalized.height > Mathf.Epsilon;
        }

        public bool TryGetTool(string id, out ToolDef definition)
        {
            string normalized = id?.Trim() ?? string.Empty;
            for (int i = 0; i < tools.Length; i++)
            {
                ToolDef candidate = tools[i];
                if (candidate != null
                    && string.Equals(candidate.StableId, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public bool TryGetGlass(string id, out GlassDef definition)
        {
            string normalized = id?.Trim() ?? string.Empty;
            for (int i = 0; i < glasses.Length; i++)
            {
                GlassDef candidate = glasses[i];
                if (candidate != null
                    && string.Equals(candidate.StableId, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }
    }
}
