using System;
using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    // 술 선택 공간에서 꺼낸 병들과 GameProgress 재고를 잇는 재고 규칙.
    // GameProgress의 병 잔량은 "술장 + 제작대에 나와 있는 병"의 총량을 뜻한다 — 병을 꺼내거나
    // 반환할 때는 총량을 건드리지 않고, 실제로 따라서 줄어든 양만 총량에서 뺀다. 그래서 반환·제조
    // 종료 시 병 오브젝트만 없애면 남은 양이 자동으로 술장에 합쳐지고, 세션 도중 저장돼도 재고가 맞다.
    // 가니쉬도 같은 규칙을 따른다 — 꺼낸 가니쉬 1개가 "제작대에 나와 있는 양" 1로 계산된다.
    public sealed class BartendingBottleStock
    {
        private sealed class DeployedBottle
        {
            public LiquorBottleDef Definition;
            public string InventoryId;
            public float LastCapacity;
        }

        private readonly Dictionary<BottleController, DeployedBottle> deployed =
            new Dictionary<BottleController, DeployedBottle>();
        private readonly List<BottleController> releaseBuffer = new List<BottleController>();
        private readonly Dictionary<GarnishController, string> deployedGarnishes =
            new Dictionary<GarnishController, string>();
        private readonly List<GarnishController> garnishReleaseBuffer = new List<GarnishController>();

        public event Action<string> StockChanged;

        public int DeployedCount => deployed.Count + deployedGarnishes.Count;

        public float GetTotalAmount(LiquorBottleDef definition)
        {
            if (definition == null)
                return 0f;

            GameProgress progress = GameProgress.Instance;
            return progress != null
                ? progress.EnsureBottleAmount(definition.InventoryId, definition.DefaultAmount)
                : Mathf.Max(0f, definition.DefaultAmount);
        }

        public float GetShelfAmount(LiquorBottleDef definition)
        {
            if (definition == null)
                return 0f;

            return Mathf.Max(
                0f,
                GetTotalAmount(definition) - GetDeployedAmount(definition.InventoryId));
        }

        // 따 둔 병(한 병 용량으로 나눈 나머지)을 먼저 내보낸다. 나머지가 없으면 가득 찬 병 하나.
        // 예: 800ml(700ml 병) → 100ml 병이 먼저 나오고, 그다음 700ml 병이 나온다.
        public static float CalculateNextBottleAmount(float shelfAmount, float bottleCapacity)
        {
            float shelf = Mathf.Max(0f, shelfAmount);
            float capacity = Mathf.Max(1f, bottleCapacity);
            if (shelf <= Mathf.Epsilon)
                return 0f;

            float opened = shelf % capacity;
            // 부동소수 누적 오차로 700이 699.9999 % 700처럼 계산돼 "거의 가득 찬 따 둔 병"이
            // 생기지 않도록, 용량에 사실상 딱 맞는 나머지는 가득 찬 병으로 취급한다.
            if (opened <= 0.01f || capacity - opened <= 0.01f)
                return Mathf.Min(capacity, shelf);
            return opened;
        }

        public bool TryGetDefinition(BottleController bottle, out LiquorBottleDef definition)
        {
            if (bottle != null && deployed.TryGetValue(bottle, out DeployedBottle entry))
            {
                definition = entry.Definition;
                return true;
            }

            definition = null;
            return false;
        }

        public BottleController FindPickedUpBottle()
        {
            foreach (BottleController bottle in deployed.Keys)
            {
                if (bottle != null && bottle.IsPickedUp)
                    return bottle;
            }

            return null;
        }

        public GarnishController FindPickedUpGarnish()
        {
            foreach (GarnishController garnish in deployedGarnishes.Keys)
            {
                if (garnish != null && garnish.IsPickedUp)
                    return garnish;
            }

            return null;
        }

        public void Register(GarnishController garnish, GarnishDef definition)
        {
            if (garnish == null || definition == null || deployedGarnishes.ContainsKey(garnish))
                return;

            string inventoryId = definition.InventoryId;
            deployedGarnishes.Add(garnish, inventoryId);
            StockChanged?.Invoke(inventoryId);
        }

        public void Release(GarnishController garnish)
        {
            if (garnish == null || !deployedGarnishes.TryGetValue(garnish, out string inventoryId))
                return;

            deployedGarnishes.Remove(garnish);
            StockChanged?.Invoke(inventoryId);
        }

        public void Register(BottleController bottle, LiquorBottleDef definition)
        {
            if (bottle == null || definition == null || deployed.ContainsKey(bottle))
                return;

            DeployedBottle entry = new DeployedBottle
            {
                Definition = definition,
                InventoryId = definition.InventoryId,
                LastCapacity = bottle.CurrentCapacity
            };
            deployed.Add(bottle, entry);
            bottle.CapacityChanged += HandleCapacityChanged;
            StockChanged?.Invoke(entry.InventoryId);
        }

        public void Release(BottleController bottle)
        {
            if (bottle == null || !deployed.TryGetValue(bottle, out DeployedBottle entry))
                return;

            bottle.CapacityChanged -= HandleCapacityChanged;
            deployed.Remove(bottle);
            StockChanged?.Invoke(entry.InventoryId);
        }

        public void ReleaseAll()
        {
            releaseBuffer.Clear();
            releaseBuffer.AddRange(deployed.Keys);
            for (int i = 0; i < releaseBuffer.Count; i++)
            {
                BottleController bottle = releaseBuffer[i];
                if (bottle != null)
                {
                    Release(bottle);
                    continue;
                }

                // 이미 파괴된 병은 이벤트 구독을 풀 수 없으므로 목록에서만 제거하고 알림은 보낸다.
                if (deployed.TryGetValue(bottle, out DeployedBottle entry))
                {
                    deployed.Remove(bottle);
                    StockChanged?.Invoke(entry.InventoryId);
                }
            }

            releaseBuffer.Clear();

            // 가니쉬는 구독한 이벤트가 없으므로 파괴 여부와 관계없이 목록에서 빼고 알리기만 한다.
            garnishReleaseBuffer.Clear();
            garnishReleaseBuffer.AddRange(deployedGarnishes.Keys);
            for (int i = 0; i < garnishReleaseBuffer.Count; i++)
            {
                GarnishController garnish = garnishReleaseBuffer[i];
                string inventoryId = deployedGarnishes[garnish];
                deployedGarnishes.Remove(garnish);
                StockChanged?.Invoke(inventoryId);
            }
            garnishReleaseBuffer.Clear();
        }

        // 배송으로 들어온 가득 찬 병 하나를 총량에 더한다. 꺼내 둔 병은 총량에 이미 포함돼 있으므로
        // 최대 보유량(MaxAmount)으로만 막으면 술장과 제작대를 합친 양이 한도를 넘지 않는다.
        public void AddDeliveredBottle(LiquorBottleDef definition)
        {
            if (definition == null)
                return;

            GameProgress progress = GameProgress.Instance;
            if (progress == null)
                return;

            float total = GetTotalAmount(definition);
            progress.SetBottleAmount(
                definition.InventoryId,
                Mathf.Min(definition.MaxAmount, total + Mathf.Max(0f, definition.unitVolume)));
            StockChanged?.Invoke(definition.InventoryId);
        }

        private float GetDeployedAmount(string inventoryId)
        {
            float amount = 0f;
            foreach (KeyValuePair<BottleController, DeployedBottle> pair in deployed)
            {
                if (pair.Key != null
                    && string.Equals(pair.Value.InventoryId, inventoryId, StringComparison.OrdinalIgnoreCase))
                {
                    amount += Mathf.Max(0f, pair.Key.CurrentCapacity);
                }
            }

            foreach (KeyValuePair<GarnishController, string> pair in deployedGarnishes)
            {
                if (pair.Key != null
                    && string.Equals(pair.Value, inventoryId, StringComparison.OrdinalIgnoreCase))
                {
                    amount += 1f;
                }
            }

            return amount;
        }

        // 병 잔량이 줄어든 만큼만 총량에서 뺀다. 늘어난 경우(외부에서 잔량을 재설정)는 재고를
        // 새로 만들어내면 안 되므로 기준값만 갱신한다. 비어도 자동 리필하지 않는다 — 새 병은
        // 술 선택 공간에서 직접 다시 꺼내야 한다.
        private void HandleCapacityChanged(BottleController bottle, float amount)
        {
            if (bottle == null || !deployed.TryGetValue(bottle, out DeployedBottle entry))
                return;

            float current = Mathf.Max(0f, amount);
            float consumed = entry.LastCapacity - current;
            entry.LastCapacity = current;
            if (consumed <= Mathf.Epsilon)
                return;

            GameProgress progress = GameProgress.Instance;
            if (progress != null)
            {
                float total = progress.EnsureBottleAmount(
                    entry.InventoryId,
                    entry.Definition.DefaultAmount);
                progress.SetBottleAmount(entry.InventoryId, Mathf.Max(0f, total - consumed));
            }

            StockChanged?.Invoke(entry.InventoryId);
        }
    }
}
