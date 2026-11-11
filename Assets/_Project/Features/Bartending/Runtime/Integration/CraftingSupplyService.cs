using System;
using System.Collections.Generic;
using Slainte.Bartending.FluidGpuExperiment;
using UnityEngine;

namespace Slainte.Bartending
{
    // 총재고는 GameProgress, 꺼낸 객체는 이 세션이 소유한다. 생성/반납은 예약만 바꾸고 소비만 총량에서 뺀다.
    public sealed class CraftingSupplyService
    {
        private sealed class Supply
        {
            public FluidExperimentBody body;
            public LiquorBottleDef definition;
            public float remaining;
            public bool garnish;
            public bool consumed;
        }

        private sealed class CabinetEntry
        {
            public FluidExperimentBody prefab;
            public FluidExperimentBody instance;
        }

        private readonly FluidExperimentRuntimeRig rig;
        private readonly GpuCraftingSettings settings;
        private readonly Func<string, Vector2> shelfWorldSize;
        private readonly List<Supply> supplies = new List<Supply>();
        private readonly Dictionary<string, CabinetEntry> cabinet = new Dictionary<string, CabinetEntry>(StringComparer.OrdinalIgnoreCase);
        public event Action<string> StockChanged;
        public event Action<string, bool> ToolAvailabilityChanged;
        public int DeployedCount => supplies.Count;

        public CraftingSupplyService(FluidExperimentRuntimeRig runtime, GpuCraftingSettings configuration,
            Func<string, Vector2> getShelfWorldSize = null)
        {
            rig = runtime;
            settings = configuration;
            shelfWorldSize = getShelfWorldSize;
        }

        public float GetTotalAmount(LiquorBottleDef definition)
        {
            if (definition == null) return 0;
            return GameProgress.Instance != null
                ? GameProgress.Instance.EnsureBottleAmount(definition.InventoryId, definition.DefaultAmount)
                : Mathf.Max(0, definition.DefaultAmount);
        }

        public float GetShelfAmount(LiquorBottleDef definition)
        {
            SynchronizeConsumption();
            float amount = GetTotalAmount(definition);
            if (definition == null) return 0;
            foreach (var supply in supplies)
                if (supply.definition.InventoryId == definition.InventoryId && supply.body != null)
                    amount -= supply.garnish ? (supply.consumed ? 0 : 1) : Mathf.Max(0, supply.body.remainingMl);
            return Mathf.Max(0, amount);
        }

        public void AddDeliveredBottle(LiquorBottleDef definition)
        {
            if (definition == null || GameProgress.Instance == null) return;
            SynchronizeConsumption();
            GameProgress.Instance.SetBottleAmount(definition.InventoryId,
                Mathf.Min(definition.MaxAmount, GetTotalAmount(definition) + Mathf.Max(0, definition.unitVolume)));
            StockChanged?.Invoke(definition.InventoryId);
        }

        public bool TryTake(LiquorBottleDef definition, Vector2 position, bool pickUp, out string failure)
        {
            failure = string.Empty;
            if (definition == null || HasHeldItem()) { failure = "The ingredient is missing or another item is held."; return false; }
            FluidExperimentBody prefab = FindIngredientPrefab(definition);
            if (prefab == null) { failure = "No GPU prefab is mapped to " + definition.InventoryId; return false; }
            bool garnish = definition is GarnishDef;
            float shelf = GetShelfAmount(definition);
            float amount = garnish ? (shelf >= 1 ? 1 : 0)
                : BartendingBottleStock.CalculateNextBottleAmount(shelf, prefab.capacityMl);
            if (amount <= 0) { failure = "This ingredient is out of stock."; return false; }
            var body = garnish ? rig.Spawn(prefab, position) : SpawnSized(prefab, definition.InventoryId, position);
            if (!garnish) body.ResetSupply(amount, body.iceStock);
            if (pickUp && !rig.Interactor.Pick(body, position))
            {
                body.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(body.gameObject);
                failure = "The GPU item could not be picked up.";
                return false;
            }
            supplies.Add(new Supply { body = body, definition = definition, remaining = amount, garnish = garnish });
            body.SupplyChanged += HandleSupplyChanged;
            body.Placed += HandleSupplyChanged;
            StockChanged?.Invoke(definition.InventoryId);
            return true;
        }

        // Shelf height is authoritative. Uniform scale keeps the authored collision,
        // mouth and spoon geometry aligned without changing inventory ml.
        private FluidExperimentBody SpawnSized(FluidExperimentBody prefab, string id, Vector2 position)
            => rig.Spawn(prefab, position, scale: GetShelfScale(prefab, id));

        internal float GetShelfScale(FluidExperimentBody prefab, string id)
        {
            Vector2 target = shelfWorldSize != null ? shelfWorldSize(id) : Vector2.zero;
            var renderer = BartendingNativeSpriteSizer.FindReferenceRenderer(prefab.gameObject);
            float height = renderer != null
                ? renderer.transform.TransformVector(Vector3.up * renderer.sprite.bounds.size.y).magnitude : 0;
            return target.y > 0 && height > .0001f ? target.y / height : 1;
        }

        private FluidExperimentBody FindIngredientPrefab(LiquorBottleDef definition)
        {
            if (definition is GarnishDef)
            {
                foreach (var binding in settings.garnishes)
                    if (binding.inventoryId == definition.InventoryId) return binding.prefab;
                return null;
            }
            foreach (var entry in rig.catalog.entries)
                if (entry != null && entry.ingredient != null && entry.ingredient.id == definition.InventoryId
                    && entry.prefab != null && entry.prefab.TryGetComponent(out FluidExperimentBody body)
                    && body.kind == LabItemKind.Bottle) return body;
            return null;
        }

        public void PrepareCabinet(ToolCabinetCatalog catalog)
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.CabinetPrepare);
            if (catalog == null) return;
            // Prefab resolution belongs to scene preparation. Only deployed state
            // changes between orders; resolving the same catalog again can load
            // glass artwork and breakage dependencies on the first visible entry.
            if (cabinet.Count == 0)
            {
                foreach (var glass in catalog.glasses)
                    if (glass != null) AddCabinet(glass.StableId, FindBody(LabItemKind.Glass, glass.glassId));
                foreach (var tool in catalog.tools)
                    if (tool != null)
                    {
                        LabItemKind? kind = tool.kind switch
                        {
                            ToolKind.Jigger => LabItemKind.Jigger,
                            ToolKind.CobblerShaker => LabItemKind.Shaker,
                            ToolKind.BarSpoon => LabItemKind.Spoon,
                            ToolKind.IceBucket => LabItemKind.IceBucket,
                            _ => null
                        };
                        if (kind.HasValue) AddCabinet(tool.StableId, FindBody(kind.Value));
                    }
            }
            foreach (var entry in cabinet)
                ToolAvailabilityChanged?.Invoke(entry.Key, entry.Value.instance == null || entry.Value.instance.IsStored);
        }

        private void AddCabinet(string id, FluidExperimentBody prefab)
        {
            if (prefab == null) { Debug.LogError("[GpuCrafting] Missing cabinet mapping: " + id); return; }
            cabinet.Add(id, new CabinetEntry { prefab = prefab });
        }

        public FluidExperimentBody FindBody(LabItemKind kind, string glassId = null)
        {
            foreach (var entry in rig.catalog.entries)
                if (entry?.prefab != null && entry.prefab.TryGetComponent(out FluidExperimentBody body)
                    && body.kind == kind && (glassId == null || body.servingGlassId == glassId)) return body;
            return null;
        }

        public bool HasHeldItem() => rig.Interactor.Held != null || rig.Interactor.HeldPart != null || rig.Interactor.HeldDispenser != null;

        public bool TryTakeTool(string id, Vector2 position, out string failure)
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.TakeTool);
            failure = string.Empty;
            if (HasHeldItem()) { failure = "Put down the item already held before taking a tool."; return false; }
            if (!cabinet.TryGetValue(id, out var entry)) { failure = "No GPU cabinet mapping is ready for " + id; return false; }
            // 복원 시 액체도 같은 위치로 옮기므로, 집기 전에 안전한 높이를 정한다.
            var tool = entry.instance != null ? entry.instance : entry.prefab;
            Vector2 pickupPosition = tool.IsVessel && rig.World.HeldVesselFloor != null
                ? rig.World.ConstrainHeldPosition(tool, position, tool.Angle)
                : position;
            if (entry.instance == null) entry.instance = SpawnSized(entry.prefab, id, pickupPosition);
            else if (!entry.instance.IsStored || !entry.instance.TryRestore(pickupPosition))
            { failure = "This tool is already deployed or its contents are still updating."; return false; }
            if (!rig.Interactor.Pick(entry.instance, position)) { failure = "The GPU tool could not be picked up."; return false; }
            ToolAvailabilityChanged?.Invoke(id, false);
            return true;
        }

        public bool IsHeldTool()
        {
            foreach (var pair in cabinet)
                if (pair.Value.instance != null && pair.Value.instance == rig.Interactor.Held) return true;
            return false;
        }

        public bool TryReturnTool(out string failure)
        {
            failure = string.Empty;
            var held = rig.Interactor.Held;
            foreach (var pair in cabinet)
            {
                if (pair.Value.instance == null || pair.Value.instance != held) continue;
                if (held.kind == LabItemKind.Shaker && (!held.HasCap || !held.HasStrainer))
                { failure = "Reassemble the shaker before returning it."; return false; }
                // 예약된 생성은 버리지 않고 다음 물리 틱까지 기다린다.
                if (!rig.Liquid.CanSuspendContents) { failure = "Wait for the next liquid tick before returning this tool."; return false; }
                rig.Interactor.ReleaseWithVelocity(Vector2.zero);
                rig.World.RefreshIceContainment();
                if (!held.TryStore()) { failure = "The tool could not be stored."; return false; }
                ToolAvailabilityChanged?.Invoke(pair.Key, true);
                return true;
            }
            return false;
        }

        public bool TryReturnIngredient(out string failure)
        {
            failure = string.Empty;
            var held = rig.Interactor.Held;
            for (int i = 0; i < supplies.Count; i++)
            {
                var supply = supplies[i];
                if (held == null || supply.body != held || supply.consumed) continue;
                SynchronizeConsumption();
                supplies.RemoveAt(i); // 반환용 해제는 가니쉬 사용으로 집계하지 않는다.
                held.SupplyChanged -= HandleSupplyChanged;
                held.Placed -= HandleSupplyChanged;
                rig.Interactor.ReleaseWithVelocity(Vector2.zero);
                held.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(held.gameObject);
                StockChanged?.Invoke(supply.definition.InventoryId);
                return true;
            }
            return false;
        }

        public void SynchronizeConsumption()
        {
            foreach (var supply in supplies)
            {
                if (supply.consumed) continue;
                float current = supply.garnish
                    ? (supply.body != null && supply.body.CanBePicked ? 1 : 0)
                    : (supply.body != null ? Mathf.Max(0, supply.body.remainingMl) : 0);
                float consumed = Mathf.Max(0, supply.remaining - current);
                supply.remaining = current;
                if (supply.garnish && current == 0) supply.consumed = true;
                if (consumed <= 0) continue;
                if (GameProgress.Instance != null)
                    GameProgress.Instance.SetBottleAmount(supply.definition.InventoryId,
                        Mathf.Max(0, GetTotalAmount(supply.definition) - consumed));
                StockChanged?.Invoke(supply.definition.InventoryId);
            }
        }

        private void HandleSupplyChanged(FluidExperimentBody body) => SynchronizeConsumption();

        public void Finish()
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.SupplyFinish);
            SynchronizeConsumption();
            var ids = new HashSet<string>();
            foreach (var supply in supplies)
            {
                ids.Add(supply.definition.InventoryId);
                if (supply.body != null)
                {
                    supply.body.SupplyChanged -= HandleSupplyChanged;
                    supply.body.Placed -= HandleSupplyChanged;
                }
            }
            supplies.Clear();
            foreach (var entry in cabinet.Values) entry.instance = null;
            foreach (string id in ids) StockChanged?.Invoke(id);
        }
    }
}
