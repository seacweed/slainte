using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public readonly struct FluidExperimentLedgerEntry
    {
        public readonly ItemDef Ingredient;
        public readonly double RequestedMl, QueuedMl, CpuRejectedMl, CpuPendingMl;
        public readonly double GeneratedMl, ActiveMl, RetiredMl, TransferredMl, GpuRejectedMl, GpuPendingMl;
        public double ConservationErrorMl => GeneratedMl - ActiveMl - GpuPendingMl - RetiredMl - TransferredMl;
        public double QueueErrorMl => QueuedMl - CpuPendingMl - GeneratedMl - GpuRejectedMl;
        internal FluidExperimentLedgerEntry(ItemDef ingredient, double requested, double queued,
            double rejected, double pending, Vector4 gpu, Vector4 gpuState)
        {
            Ingredient = ingredient; RequestedMl = requested; QueuedMl = queued;
            CpuRejectedMl = rejected; CpuPendingMl = pending;
            GeneratedMl = gpu.x; ActiveMl = gpu.y; RetiredMl = gpu.z; GpuRejectedMl = gpu.w;
            GpuPendingMl = gpuState.x; TransferredMl = gpuState.y;
        }
    }

    public sealed class FluidExperimentTransferReceipt
    {
        public string Id { get; }
        public uint VesselId { get; }
        public int Generation { get; }
        public int SourceRevision { get; }
        public double TotalMl { get; }
        public IReadOnlyDictionary<ItemDef, double> Ingredients { get; }
        internal FluidExperimentTransferReceipt(uint vesselId, int generation, int revision,
            double total, IReadOnlyDictionary<ItemDef, double> ingredients)
        {
            Id = Guid.NewGuid().ToString("N"); VesselId = vesselId; Generation = generation;
            SourceRevision = revision; TotalMl = total; Ingredients = ingredients;
        }
    }

    /// <summary>One completed GPU tick, including CPU requests/pending captured when its readback was queued.</summary>
    public sealed class FluidExperimentLedgerSnapshot
    {
        public int Revision { get; }
        public int Generation { get; }
        public float SimulationTime { get; }
        public FluidExperimentLedgerEntry Total { get; }
        public IReadOnlyList<FluidExperimentLedgerEntry> Ingredients { get; }
        internal FluidExperimentLedgerSnapshot(int revision, int generation, float time,
            FluidExperimentLedgerEntry total, List<FluidExperimentLedgerEntry> ingredients)
        {
            Revision = revision; Generation = generation; SimulationTime = time; Total = total;
            Ingredients = ingredients.AsReadOnly();
        }
    }

    public sealed partial class FluidExperimentGpuLiquid
    {
        private struct EmissionTotals { public double requested, queued, rejected; }
        private sealed class LedgerCapture
        {
            public int generation;
            public float time;
            public long reservations;
            public long stateVersion;
            public EmissionTotals total;
            public double pending;
            public Dictionary<ItemDef, EmissionTotals> requests;
            public Dictionary<ItemDef, int> indices;
            public Dictionary<ItemDef, double> pendingIngredients;
            public Dictionary<uint, GarnishFlowFrame> garnishFrames;
        }

        private readonly Dictionary<ItemDef, EmissionTotals> emissionTotals = new Dictionary<ItemDef, EmissionTotals>();
        private EmissionTotals totalEmissions;
        private readonly Dictionary<uint, IReadOnlyDictionary<ItemDef, double>> vesselIngredientSnapshots =
            new Dictionary<uint, IReadOnlyDictionary<ItemDef, double>>();
        private static readonly IReadOnlyDictionary<ItemDef, double> EmptyIngredients =
            new ReadOnlyDictionary<ItemDef, double>(new Dictionary<ItemDef, double>());
        private GraphicsBuffer ledgerAmountsBuffer, ledgerSnapshotBuffer, conservativeMixWeightsBuffer;
        private int collectLedgerKernel, conservativeMixWeightsKernel, conservativeMixKernel, transferContentsKernel, snapshotRequestToken;
        private long liquidStateVersion, publishedStateVersion;
        private Vector4[] ledgerReadback;
        private GpuLiquidParticle[] stagingParticles;
        private float[] stagingComposition;
        private Vector4[] stagingLedger;
        public FluidExperimentLedgerSnapshot Ledger { get; private set; }

        /// <summary>
        /// Explicit experimental delivery adapter. Requires the latest completed, unchanged GPU
        /// state; stale/replayed revisions or pending births are rejected without changing liquid.
        /// The successful receipt is immutable and the transfer is separately accounted from spills.
        /// </summary>
        public bool TryTransferContents(uint vesselId, int expectedRevision, out FluidExperimentTransferReceipt receipt)
            => TryTransferContents(vesselId, generation, expectedRevision, out receipt);

        public bool TryTransferContents(uint vesselId, int expectedGeneration, int expectedRevision,
            out FluidExperimentTransferReceipt receipt)
        {
            receipt = null;
            if (!IsOperational || vesselId == 0 || Ledger == null || expectedGeneration != generation
                || Ledger.Generation != expectedGeneration || Ledger.Revision != expectedRevision
                || publishedStateVersion != liquidStateVersion || Ledger.Total.GpuPendingMl > 0) return false;
            for (int i = 0; i < pendingSpawnCount; i++)
                if (spawnCommands[i].VesselId == vesselId) return false;
            double total = 0;
            foreach (var particle in snapshotParticles)
                if (particle.Active != 0 && particle.VesselId == vesselId) total += particle.VolumeMl;
            if (total <= 0) return false;
            var mixture = IngredientsIn(vesselId);
            simulationShader.SetInt("_TransferTargetVessel", (int)vesselId);
            BindCurrentComposition(transferContentsKernel);
            DispatchForCount(transferContentsKernel, particleCapacity);
            ResetImprovedSurfaceHistory();
            liquidStateVersion++;
            ReadbackNow();
            receipt = new FluidExperimentTransferReceipt(vesselId, expectedGeneration, expectedRevision, total, mixture);
            return true;
        }

        /// <summary>Read-only ingredient ml from the same completed tick as Snapshot and Ledger.</summary>
        public IReadOnlyDictionary<ItemDef, double> IngredientsIn(uint vesselId) =>
            vesselIngredientSnapshots.TryGetValue(vesselId, out var values) ? values : EmptyIngredients;
        public double IngredientVolumeIn(uint vesselId, ItemDef ingredient) => ingredient != null &&
            IngredientsIn(vesselId).TryGetValue(ingredient, out double amount) ? amount : 0;

        private void CacheLedgerKernels()
        {
            collectLedgerKernel = RequireKernel("CollectLiquidLedger");
            conservativeMixWeightsKernel = RequireKernel("CalculateConservativeMixWeights");
            conservativeMixKernel = RequireKernel("MixCompositionConservative");
            transferContentsKernel = RequireKernel("TransferVesselContents");
        }
        private void AllocateLedgerBuffers()
        {
            int entries = maximumIngredients + 1;
            ledgerAmountsBuffer = CreateStructured<float>(particleCapacity * entries * 4);
            ledgerSnapshotBuffer = CreateStructured<Vector4>(entries * 2 + 1);
            conservativeMixWeightsBuffer = CreateStructured<float>(particleCapacity);
            ledgerReadback = new Vector4[entries * 2 + 1];
            stagingLedger = new Vector4[ledgerReadback.Length];
            stagingParticles = new GpuLiquidParticle[particleCapacity];
            stagingComposition = new float[particleCapacity * maximumIngredients];
        }
        private void BindLedgerBuffers(int kernel)
        {
            simulationShader.SetBuffer(kernel, "_LedgerAmounts", ledgerAmountsBuffer);
            simulationShader.SetBuffer(kernel, "_LedgerSnapshot", ledgerSnapshotBuffer);
            simulationShader.SetBuffer(kernel, "_ConservativeMixWeights", conservativeMixWeightsBuffer);
        }
        private void DisposeLedgerBuffers()
        {
            snapshotRequestToken++;
            ledgerAmountsBuffer?.Dispose(); ledgerSnapshotBuffer?.Dispose(); conservativeMixWeightsBuffer?.Dispose();
            ledgerAmountsBuffer = ledgerSnapshotBuffer = conservativeMixWeightsBuffer = null;
            ResetLedgerState();
        }
        private void ResetLedgerState()
        {
            snapshotRequestToken++; Ledger = null; totalEmissions = default;
            liquidStateVersion++;
            emissionTotals.Clear(); vesselIngredientSnapshots.Clear();
            if (ledgerReadback != null) Array.Clear(ledgerReadback, 0, ledgerReadback.Length);
        }
        private void RecordEmissionRequest(ItemDef ingredient, float volume)
        {
            emissionTotals.TryGetValue(ingredient, out var t); t.requested += volume;
            emissionTotals[ingredient] = t; totalEmissions.requested += volume;
        }
        private bool RejectEmission(ItemDef ingredient, float volume)
        {
            var t = emissionTotals[ingredient]; t.rejected += volume;
            emissionTotals[ingredient] = t; totalEmissions.rejected += volume; return false;
        }
        private void RecordEmissionQueued(ItemDef ingredient, float volume)
        {
            var t = emissionTotals[ingredient]; t.queued += volume;
            emissionTotals[ingredient] = t; totalEmissions.queued += volume;
        }
        private LedgerCapture CaptureLedger()
        {
            var capture = new LedgerCapture { generation = generation, time = simulationTime,
                garnishFrames = CaptureGarnishFlowFrames(),
                stateVersion = liquidStateVersion,
                reservations = reservations - pendingSpawnCount, total = totalEmissions,
                requests = new Dictionary<ItemDef, EmissionTotals>(emissionTotals),
                indices = new Dictionary<ItemDef, int>(ingredientIndices), pendingIngredients = new Dictionary<ItemDef, double>() };
            for (int i = 0; i < pendingSpawnCount; i++)
            {
                GpuLiquidSpawnCommand command = spawnCommands[i];
                ItemDef ingredient = ingredients[(int)command.SourceIngredient];
                capture.pending += command.VolumeMl;
                capture.pendingIngredients.TryGetValue(ingredient, out double previous);
                capture.pendingIngredients[ingredient] = previous + command.VolumeMl;
            }
            return capture;
        }
        private void CollectLedger()
        {
            BindCurrentComposition(collectLedgerKernel);
            DispatchForCount(collectLedgerKernel, maximumIngredients + 1);
        }
        private void PublishLedger(LedgerCapture capture)
        {
            int stride = maximumIngredients + 1;
            var entries = new List<FluidExperimentLedgerEntry>(capture.requests.Count);
            foreach (var pair in capture.requests)
            {
                bool known = capture.indices.TryGetValue(pair.Key, out int index);
                capture.pendingIngredients.TryGetValue(pair.Key, out double pending);
                EmissionTotals t = pair.Value;
                entries.Add(new FluidExperimentLedgerEntry(pair.Key, t.requested, t.queued, t.rejected, pending,
                    known ? ledgerReadback[index] : default, known ? ledgerReadback[stride + index] : default));
            }
            Ledger = new FluidExperimentLedgerSnapshot(SnapshotRevision, capture.generation, capture.time,
                new FluidExperimentLedgerEntry(null, capture.total.requested, capture.total.queued,
                    capture.total.rejected, capture.pending, ledgerReadback[maximumIngredients],
                    ledgerReadback[stride + maximumIngredients]), entries);
            publishedStateVersion = capture.stateVersion;

            var byVessel = new Dictionary<uint, Dictionary<ItemDef, double>>();
            for (int i = 0; i < snapshotParticles.Length; i++)
            {
                GpuLiquidParticle p = snapshotParticles[i];
                if (p.Active == 0) continue;
                if (!byVessel.TryGetValue(p.VesselId, out var mixture))
                    byVessel.Add(p.VesselId, mixture = new Dictionary<ItemDef, double>());
                foreach (var ingredient in capture.indices)
                {
                    double amount = p.VolumeMl * (double)snapshotComposition[i * maximumIngredients + ingredient.Value];
                    if (amount == 0) continue;
                    mixture.TryGetValue(ingredient.Key, out double previous);
                    mixture[ingredient.Key] = previous + amount;
                }
            }
            vesselIngredientSnapshots.Clear();
            foreach (var pair in byVessel)
                vesselIngredientSnapshots.Add(pair.Key, new ReadOnlyDictionary<ItemDef, double>(pair.Value));
        }
    }
}
