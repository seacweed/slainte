# Startup failures (preserved; not successful runs)

Unity executable: `A:/UnityHub/Unity/6000.3.5f2/Editor/Unity.exe`

Common arguments: `-batchmode -force-d3d11 -projectPath A:/UnityHub/proj/slainte -executeMethod Slainte.Bartending.PhysicsLab.Editor.PhysicsLabBuilder.Build -quit`

1. PID 19540: sandboxed hidden launch, log requested at `A:/UnityHub/proj/slainte/tmp/physicslab/build.log`. The evidence directory creation had failed with access denied. No usable Editor log or exit code was captured. Failure, not completion.
2. PID 6736: sandboxed hidden launch, log requested at `A:/UnityHub/proj/slainte/PhysicsLabEvidence/build.log`. No Editor log or exit code was captured. Failure, not completion.
3. Sandboxed direct invocation with the second log path: console emitted the following startup failure. The shell's exit status did not establish the Unity process status; this is a crashed run.

```
unable to open database file
unable to open database file
Failed to delete database file C:/Users/boguk/AppData/Local/Unity/Caches/CurlRequestCache.db
windows exception 0x80000003
Unity: DebugStringToFilePostprocessedStacktrace
Unity: DebugStringToFile
Unity: CurlFileCache::Instance
Unity: CurlFileCacheCleanup
Unity: CurlRequestInitialize
Unity: UnityMain
```

The three timestamped dumps are copied alongside this document. `dump-summary.json` contains their exception codes/addresses parsed from the minidump exception streams. The console stack above belongs to the directly observed third failure; the first two have not been individually symbolicated.

Cause supported by the observed third-run log: Unity's startup curl cache cannot access its database outside the restricted workspace. This occurs before project compilation/GPU simulation. No application cache was deleted or modified manually.

Changed condition before the next run: Unity was launched with escalation for its normal external cache access. The subsequent build produced an Editor log, generated 24 isolated prefabs and the new scene, and logged `Application will terminate with return code 0`. That build is separately preserved in `../01-prefab-build/Editor.log`; it does not constitute a Play Mode or GPU test pass.

All subsequent invocations use `../Run-Unity.ps1`: a new attempt directory, exact arguments, stdout/stderr, Editor log, process exit code, and any newly generated crash dumps. No automatic crash retry exists.
