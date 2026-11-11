using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;

namespace Slainte.Bartending
{
    /// <summary>Allocation-free profiler scopes; detailed samples are collected only while subscribed.</summary>
    public static class GpuCraftingDiagnostics
    {
        public enum Stage
        {
            Prepare, Initialize, Environment, Begin, ReadyCallbacks, End, SupplyFinish,
            CabinetPrepare, TakeTool, Suspend, RigReset, WorldReset, LiquidInitialize,
            LiquidReset, Readback, Submit, SurfaceRender, SurfaceResources
        }
        public readonly struct Sample
        {
            public readonly Stage stage;
            public readonly int frame;
            public readonly double milliseconds;
            public readonly long allocatedBytes;
            internal Sample(Stage stage, int frame, double milliseconds, long allocatedBytes)
            { this.stage = stage; this.frame = frame; this.milliseconds = milliseconds; this.allocatedBytes = allocatedBytes; }
        }
        private static readonly ProfilerMarker[] markers = MakeMarkers();
        public static event Action<Sample> SampleRecorded;
        private static ProfilerMarker[] MakeMarkers()
        {
            var names = Enum.GetNames(typeof(Stage));
            var result = new ProfilerMarker[names.Length];
            for (int i = 0; i < result.Length; i++) result[i] = new ProfilerMarker("Bartending.Gpu." + names[i]);
            return result;
        }
        public static Scope Measure(Stage stage) => new Scope(stage);
        public readonly struct Scope : IDisposable
        {
            private readonly ProfilerMarker.AutoScope marker;
            private readonly Stage stage;
            private readonly int frame;
            private readonly long started, allocated;
            private readonly Action<Sample> callback;
            internal Scope(Stage stage)
            {
                marker = markers[(int)stage].Auto();
                this.stage = stage; callback = SampleRecorded;
                frame = callback != null ? Time.frameCount : 0;
                allocated = callback != null ? GC.GetAllocatedBytesForCurrentThread() : 0;
                started = callback != null ? Stopwatch.GetTimestamp() : 0;
            }
            public void Dispose()
            {
                if (callback != null)
                {
                    long nowAllocated = GC.GetAllocatedBytesForCurrentThread();
                    // Some Unity Mono runtimes expose a stub returning zero. Unknown is -1.
                    long bytes = allocated == 0 && nowAllocated == 0 ? -1 : nowAllocated - allocated;
                    callback(new Sample(stage, frame, (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency, bytes));
                }
                marker.Dispose();
            }
        }
    }
}
