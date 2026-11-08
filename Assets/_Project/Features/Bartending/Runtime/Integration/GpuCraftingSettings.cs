using System;
using Slainte.Bartending.FluidGpuExperiment;
using UnityEngine;

namespace Slainte.Bartending
{
    // 게임의 제작 공간 설정만 보유한다. 물리 수치와 셰이더는 원본 GPU 런타임이 소유한다.
    [CreateAssetMenu(menuName = "Bartending/GPU Crafting Settings")]
    public sealed class GpuCraftingSettings : ScriptableObject
    {
        [Serializable]
        public struct GarnishBinding
        {
            public string inventoryId;
            public FluidExperimentBody prefab;
        }

        public FluidExperimentRuntimeRig runtimePrefab;
        public GarnishBinding[] garnishes = Array.Empty<GarnishBinding>();
        public float tableThickness = .25f;
        public Vector2 dispenserViewportPosition = new Vector2(.88f, .64f);
    }
}
