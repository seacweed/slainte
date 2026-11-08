using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        public bool CanSuspendContents => IsOperational && pendingSpawnCount == 0;

        // GPU에 예약된 생성이 끝난 뒤 보관한다. 입자와 성분은 그대로 두고 적분·표시만 멈춘다.
        public bool TrySuspendContents(uint owner, bool suspended)
        {
            if (!IsOperational || pendingSpawnCount != 0) return false;
            liquidStateVersion++;
            simulationShader.SetInt("_SuspensionTargetVessel", unchecked((int)owner));
            simulationShader.SetInt("_SuspensionEnabled", suspended ? 1 : 0);
            DispatchForCount(suspendVesselKernel, particleCapacity);
            InvalidateImprovedSurfaceHistory(owner);
            InvalidateSolidFlowSnapshot();
            return true;
        }
    }

    public sealed partial class FluidExperimentBody
    {
        public event System.Action<FluidExperimentBody> SupplyChanged;
        public event System.Action<FluidExperimentBody> Placed;
        private void NotifySupplyChanged() => SupplyChanged?.Invoke(this);
        private void NotifyPlaced() => Placed?.Invoke(this);
        public bool IsStored { get; private set; }
        private readonly List<FluidExperimentBody> storedContents = new List<FluidExperimentBody>();

        // 일반 비활성화는 파손/제거 경로로 소유권을 해제한다. 보관은 ID와 담긴 물체를 유지해야 한다.
        public bool TryStore()
        {
            if (IsStored || World == null || IsHeld || kind == LabItemKind.Dispenser) return false;
            if (!World.Liquid.TrySuspendContents(Id, true)) return false;
            storedContents.Clear();
            foreach (var item in new List<FluidExperimentBody>(World.Items))
                if (item != null && item.ContainingVesselId == Id)
                {
                    item.IsStored = true;
                    World.StoreBody(item);
                    item.gameObject.SetActive(false);
                    storedContents.Add(item);
                }
            IsStored = true;
            World.StoreBody(this);
            SuspendShakerParts();
            gameObject.SetActive(false);
            return true;
        }

        public bool TryRestore(Vector2 position)
        {
            if (!IsStored || World == null || !World.Liquid.TrySuspendContents(Id, false)) return false;
            Vector2 delta = position - Position;
            World.Liquid.TranslateContentsPreservingMotion(Id, delta);
            Teleport(position, Angle);
            // 비활성 Rigidbody의 좌표만 바꾸면 재활성화 시 예전 Transform으로 돌아가 집기 기준점이 어긋난다.
            transform.SetPositionAndRotation(new Vector3(position.x, position.y, transform.position.z), Quaternion.Euler(0, 0, Angle));
            World.RestoreBody(this);
            IsStored = false;
            gameObject.SetActive(true);
            foreach (var item in storedContents)
                if (item != null)
                {
                    item.Teleport(item.Position + delta, item.Angle);
                    item.transform.SetPositionAndRotation(new Vector3(item.Position.x, item.Position.y, item.transform.position.z),
                        Quaternion.Euler(0, 0, item.Angle));
                    item.iceContainer = this;
                    World.RestoreBody(item);
                    item.IsStored = false;
                    item.gameObject.SetActive(true);
                }
            storedContents.Clear();
            World.RefreshCollisionPairs();
            return true;
        }
    }

    public sealed partial class FluidExperimentWorld
    {
        internal void StoreBody(FluidExperimentBody body)
        {
            items.Remove(body);
            interactor?.Forget(body);
            RefreshCollisionPairs();
        }

        internal void RestoreBody(FluidExperimentBody body)
        {
            if (!items.Contains(body)) items.Add(body); // 원래 ID를 유지해 보관 전후 같은 용기로 취급한다.
        }
    }
}
