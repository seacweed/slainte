using System;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentBody
    {
        [Header("Ice stock display")]
        [Min(1)] public int iceCapacity = 20;
        public SpriteRenderer iceStockRenderer;
        public Sprite[] iceStockSprites = Array.Empty<Sprite>();
        public int IceStockVisualIndex => iceStock <= 0 ? 0
            : iceStock >= Mathf.Max(1, iceCapacity) ? 6
            : Mathf.Clamp(Mathf.FloorToInt(iceStock * 5f / Mathf.Max(1, iceCapacity)) + 1, 1, 5);
        private int displayedIceStock = int.MinValue, displayedIceCapacity;

        private void LateUpdate()
        {
            if (kind == LabItemKind.IceBucket
                && (displayedIceStock != iceStock || displayedIceCapacity != iceCapacity))
                RefreshIceStockVisual();
        }

        private void RefreshIceStockVisual()
        {
            if (kind != LabItemKind.IceBucket || iceStockRenderer == null || iceStockSprites.Length < 7) return;
            Sprite sprite = iceStockSprites[IceStockVisualIndex];
            if (sprite == null) return;
            iceStockRenderer.sprite = sprite;
            displayedIceStock = iceStock;
            displayedIceCapacity = iceCapacity;
        }
    }
}
