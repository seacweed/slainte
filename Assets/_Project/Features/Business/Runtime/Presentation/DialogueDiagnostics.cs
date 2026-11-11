using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;

// Native Profiler markers remain available without a sample subscriber. Detailed
// timing is collected only when an editor measurement subscribes to SampleRecorded.
public static class DialogueDiagnostics
{
    public enum Stage
    {
        DialogueController_Advance,
        DialogueController_SkipTypingIfNeeded,
        DialogueController_ShowLine,
        DialogueController_ApplyLayout,
        DialogueController_HideImmediate,
        CustomerSpawner_ShowVisit,
        CustomerSpawner_OnCharactersShown,
        CharacterStage_ShowCharacters,
        CharacterView_Setup,
        CharacterView_SwapSprite,
        DialogueFocusPresenter_Resolve,
        OrderTicketManager_ShowPendingTicket,
        OrderTicketUI_Show,
        OrderTicketUI_ResetScrollToTop,
        BusinessOrderSessionController_BeginOrder,
        BusinessOrderSessionController_BeginCrafting,
        Dialogue_PreferredValues,
        TextWarmupGlyph,
        IndicatorWarmup,
    }

    public readonly struct Sample
    {
        public readonly Stage stage;
        public readonly int frame;
        public readonly double milliseconds;

        internal Sample(Stage stage, int frame, double milliseconds)
        {
            this.stage = stage;
            this.frame = frame;
            this.milliseconds = milliseconds;
        }
    }

    public static event Action<Sample> SampleRecorded;
    private static readonly ProfilerMarker[] markers = CreateMarkers();

    private static ProfilerMarker[] CreateMarkers()
    {
        var names = Enum.GetNames(typeof(Stage));
        var result = new ProfilerMarker[names.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = new ProfilerMarker("Business.Dialogue." + names[i]);
        return result;
    }

    public static Scope Measure(Stage stage) => new Scope(stage);

    public readonly struct Scope : IDisposable
    {
        private readonly ProfilerMarker.AutoScope marker;
        private readonly Action<Sample> callback;
        private readonly Stage stage;
        private readonly long started;
        private readonly int frame;

        internal Scope(Stage stage)
        {
            marker = markers[(int)stage].Auto();
            callback = SampleRecorded;
            this.stage = stage;
            frame = callback != null ? Time.frameCount : 0;
            started = callback != null ? Stopwatch.GetTimestamp() : 0;
        }

        public void Dispose()
        {
            if (callback != null)
                callback(new Sample(stage, frame,
                    (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency));
            marker.Dispose();
        }
    }
}
