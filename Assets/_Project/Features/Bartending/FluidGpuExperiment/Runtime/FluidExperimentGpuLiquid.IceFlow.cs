using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentGpuLiquid
    {
        private readonly Dictionary<Vector3Int,List<int>> iceFlowCells = new Dictionary<Vector3Int,List<int>>();
        private readonly List<List<int>> iceFlowBuckets = new List<List<int>>();
        private int[] iceFlowComponents;
        private float[] iceFlowVolumes;
        private Vector2[] iceFlowMoments;
        private int iceFlowRevision = -1;

        // A pressure/contact jet is not an ambient current. Feeding that local
        // solver response into a rigid body can create an energy source. Use
        // the signed bulk transport of the connected local pool in both modes.
        // Disconnected liquid elsewhere in the glass cannot become that pool.
        private void BuildIceFlowComponents()
        {
            iceFlowRevision = -1;
            int length = snapshotParticles.Length;
            if (iceFlowComponents == null || iceFlowComponents.Length != length)
            { iceFlowComponents = new int[length]; iceFlowVolumes = new float[length]; iceFlowMoments = new Vector2[length]; }
            iceFlowCells.Clear(); int bucketCount = 0;
            float radius = Mathf.Max(.001f,SolverSmoothingRadius), radiusSquare = radius*radius;
            for (int i = 0; i < length; i++)
            {
                iceFlowComponents[i] = -1; iceFlowVolumes[i] = 0; iceFlowMoments[i] = Vector2.zero;
                var p = snapshotParticles[i];
                if (p.Active == 0 || p.VesselId == 0 || p.VolumeMl <= 0) continue;
                iceFlowComponents[i] = i;
                var cell = new Vector3Int(Mathf.FloorToInt(p.Position.x/radius),Mathf.FloorToInt(p.Position.y/radius),unchecked((int)p.VesselId));
                if (!iceFlowCells.TryGetValue(cell,out var indices))
                {
                    if (bucketCount == iceFlowBuckets.Count) iceFlowBuckets.Add(new List<int>(8));
                    indices = iceFlowBuckets[bucketCount++]; indices.Clear(); iceFlowCells[cell] = indices;
                }
                indices.Add(i);
            }
            for (int i = 0; i < length; i++)
            {
                if (iceFlowComponents[i] < 0) continue;
                var p = snapshotParticles[i];
                var cell = new Vector3Int(Mathf.FloorToInt(p.Position.x/radius),Mathf.FloorToInt(p.Position.y/radius),unchecked((int)p.VesselId));
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
                    if (iceFlowCells.TryGetValue(cell+new Vector3Int(x,y,0),out var indices))
                        foreach (int j in indices)
                            if (j < i && (p.Position-snapshotParticles[j].Position).sqrMagnitude <= radiusSquare)
                            {
                                int a = IceFlowRoot(i), b = IceFlowRoot(j);
                                if (a != b) iceFlowComponents[a] = b;
                            }
            }
            for (int i = 0; i < length; i++)
            {
                if (iceFlowComponents[i] < 0) continue;
                int root = IceFlowRoot(i); float ml = snapshotParticles[i].VolumeMl;
                iceFlowVolumes[root] += ml; iceFlowMoments[root] += iceTransportVelocities[i]*ml;
            }
            iceFlowRevision = SnapshotRevision;
        }

        private int IceFlowRoot(int index)
        {
            int root = index;
            while (iceFlowComponents[root] != root) root = iceFlowComponents[root];
            while (iceFlowComponents[index] != index)
            { int next = iceFlowComponents[index]; iceFlowComponents[index] = root; index = next; }
            return root;
        }

        private bool TryIceBulkFlow(int index, GpuLiquidParticle p, out Vector2 velocity, out float ml)
        {
            velocity = Vector2.zero; ml = 0;
            if (iceFlowRevision != SnapshotRevision || iceFlowComponents[index] < 0
                || previousIceFlowParticles[index].Position != p.Position || previousIceFlowParticles[index].Velocity != p.Velocity) return false;
            int root = IceFlowRoot(index); ml = iceFlowVolumes[root];
            if (ml < 1) return false;
            velocity = Vector2.ClampMagnitude(iceFlowMoments[root]/ml,6);
            return true;
        }
    }
}
