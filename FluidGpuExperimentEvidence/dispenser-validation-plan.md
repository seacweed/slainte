# Dispenser validation

`HarnessSource/DispenserValidation.cs` is the acceptance fixture for the F/G Manual dispenser integrated onto remote commit `415dcf0381f5bf53d64758eca6aab8f510f6c195`. The fixture must be copied to a disposable Unity project with `HarnessSource/prepare_experiment.py`. Its entry point is `ExperimentDispenserValidation.Begin`. The reusable runtime has separate coverage through `ExperimentDispenserRuntimeValidation.Begin`.

The fixture uses hidden Unity batchmode, a real D3D11 compute GPU, `Physics2D.Simulate`, and virtual Input System devices. It does not operate the user's desktop or establish human approval of cursor feel, hose appearance, or return speed. A started process is not a result: read the attempt's `exit.json`, `dispenser-result.txt`, and `dispenser-validation.txt` after Unity has finished. Every new attempt must use a unique name.

## Acceptance coverage

- Authored F/G Manual availability, A–E exclusion, 5000 ml shared stock, None initial mode, three sprites, hot ingredient temperature, ice reference, solid body and collision-free hose.
- Actual virtual left/right mouse routing: button selection and repeat suppression, exclusive tool pickup, held movement during emission, no rotation or cursor capture, immediate RMB stop, Escape and shared focus-loss cancellation.
- Hot/Ice mode changes only while docked, shared inventory, mode preservation during return, and off-mode emission rejection.
- Exact 0, 1, 9, 10 and 11 ml edge cases for both outputs, including the final ice piece for a positive stock below 10 ml.
- Real queued water commands, vertical initial velocity, external ownership, hot temperature, finite GPU particles, and the existing stream representation.
- GPU allocation rejection and recovery without inventory loss or delayed bursts.
- Native dynamic, permanently placed ice; missing prefab and physically blocked nozzle failures with recovery.
- Actual registered ice geometry and real hurricane shard proxies exhausting the conservative boundary admission budget, with no creation or stock loss until geometry is released; accepted ice must preserve the active shard geometry allocation.
- A real 20 ml stream caught by the existing highball, followed by native ice ownership/contact in that receiver.
- Immediate pointer following at the original grab offset, including rendered transforms and targets beyond floors/walls; no held native contacts or GPU liquid displacement/masking; restoration after overlap-safe return, no re-pick or mode change while returning, and completed docking.
- Full 5000 ml Ice stock as 500 sequential native births with GPU geometry submission and offscreen unregistering.
- Full 5000 ml Hot stock with actual GPU allocation, materialization and repeated slot recycling.
- R-equivalent world reset, F/G and A–E switching, actual Hot/Ice output in G, remote 6/4 fracture thresholds, and Manual to automatic-scenario to Manual transitions.

The full-stock fixtures deliberately retire output as it leaves the usable region. They verify inventory and allocation lifetime; they are not a 500 simultaneous ice-body frame-rate benchmark. The Hot recycling fixture deliberately moves snapshot particles outside the GPU domain before the normal GPU retirement step. Heat exchange and ice melting are outside this fixture and this implementation.

## Reproduction

Run preparation from the repository root using the available Python runtime:

```powershell
python FluidGpuExperimentEvidence/HarnessSource/prepare_experiment.py --target Library/FluidDispenserHarness
```

Run Unity attempts sequentially with fresh names. This wrapper already uses a hidden process; do not open or focus the Editor for the fixture.

```powershell
powershell -NoProfile -File FluidGpuExperimentEvidence/Run-Unity.ps1 -Attempt <unique-dispenser-attempt> -Method ExperimentDispenserValidation.Begin -ProjectPath A:/UnityHub/proj/slainte/Library/FluidDispenserHarness -UnityExecutable A:/UnityHub/Unity/6000.3.5f2/Editor/Unity.exe -TimeoutSeconds 300
```

The following existing suites provide focused regression coverage and must be freshly rerun before claiming they pass for the dispenser change:

| Entry point | Regression scope |
| --- | --- |
| `ExperimentInputShakerValidation.Begin` | Model D relative pointer handling, return behavior, bucket and detachable shaker controls |
| `ExperimentGarnishMotionValidation.Begin` | Current F garnish source input, permanent placement, held pass-through, free rotation and one-way liquid response |
| `ExperimentGlassPickupLiquidValidation.Begin` | F pickup upright motion carrying glass-owned liquid safely |
| `ExperimentGlassBreakageValidation.Begin` | F/G hurricane impact trigger, shards, conservation, reset and remote templates |
| `ExperimentSplashValidation.Begin` | Natural/explosive splash transactions and queued/active liquid conservation |
| `ExperimentShardLiquidValidation.Begin` | Thin-wall fragment geometry, boundary budget, moving contact and conservation |
| `ExperimentRuntimeAssemblyValidation.Begin` | Idempotent builders, catalog, templates, independent runtime rigs and real glass drops |
| `ExperimentDesignerValidation.Run` | Drafts, shared/private saves, variants, geometry and preservation of authored data |

Use the same prepared project and wrapper, changing `-Attempt` and `-Method` for each run. Preserve failed attempts as evidence. Inspect render captures from successful dispenser runs separately from the numeric checks; neither kind of automated evidence establishes a human playtest.

`ExperimentDispenserPlayLoopValidation.Begin` additionally keeps the authored scene, seed liquid, native automatic Physics2D, world FixedUpdate, liquid coroutine and asynchronous readback enabled. Only the physical mouse interactor is disabled, with controller commands providing intent. It checks movement, live emission accounting/rate, stopping, retraction and docking, and writes full-scene PNGs and timeline/body CSVs. It never directly ticks motion, physics or the GPU.

`ExperimentDispenserPlayLoopValidation.BeginExplosive` runs the same automatic loop in G. Both F/G fixtures require the upstream 6/4 fracture thresholds, no incidental startup fracture, preservation of all original bodies through dispensing/retraction, and unchanged simulation/type/active state. `BeginWithoutDispenser` remains available as a startup control by clearing only the unsaved harness scene's dispenser reference. The startup fractures recorded before remote integration were specific to old base `01f4fb6` and its lower thresholds; they are historical evidence, not the integrated expected behavior.

`ExperimentDispenserRuntimeValidation.Begin` opens the actual reusable runtime sample and builds a second independent rig. It verifies the 28-entry catalog without duplicate tool spawning, exactly one dispenser per world, full/off initial stock, explicit camera/hose references, dock bounds in the narrower runtime environment, real hot and ice output, native motion/retraction, and independent stock/GPU/reset ownership. After native startup, this fixture uses scripted Physics2D/GPU stepping to isolate ownership. The F/G normal-loop fixtures above separately cover automatic scheduling.

The obsolete `ExperimentGarnishValidation` assumes older liquid exclusion and forced garnish transport and is not the current F regression entry point. Preserve previous failed runs; do not change runtime physics or weaken assertions to make an incompatible historical fixture pass.
