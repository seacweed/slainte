# Harness startup failure: not a regression result

The separate Unity project entered Play Mode, then UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase threw ArgumentOutOfRangeException during startup indexing. The test had queued Run through EditorApplication.delayCall; no validation report was produced. This attempt is failed/inconclusive, not a passing GPU test. The test-only process (33032) was stopped; the user's Unity editor was left running.

Before the next invocation, the harness was changed to execute directly on EnteredPlayMode and the wrapper gained a 240-second timeout. The copied runtime/shader in HarnessProject remained the original implementation for the next baseline attempt.
