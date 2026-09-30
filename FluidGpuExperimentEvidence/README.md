# Fluid GPU Experiment validation

Unity 6000.3.5f2, Direct3D11. Earlier baseline runs used NVIDIA GeForce RTX 3070; the D/E development and performance runs below used AMD Radeon(TM) Graphics. The latest Model D interaction runs (51–65) use NVIDIA GeForce RTX 3070. The experiment was exercised in disposable Unity projects under `HarnessProject`, `Library/FluidSwapHarness` or `Library/FluidModelDHarness`, with source assets and read-only dependencies copied from the working tree. The game project was not opened or saved by Unity for this work.

## Model D interaction baseline (2026-09-30, attempts 51–65)

D is now the authored scene and builder default. It retains reference physics and the improved surface; E's calibrated liquid model remains a separate comparison mode. These interaction rules supersede the older held/free-liquid policies recorded in historical runs below.

- A held vessel keeps contact with its contained dynamic ice. Swept containment protects side/bottom walls during rapid translation and rotation, while an open mouth still permits pouring ice out. Release carries the vessel's motion into its contents; disabling a vessel clears stale ownership and collision exceptions.
- The ice bucket is a finite source with a closed exterior, like a bottle. It has no liquid interior or liquid capacity; tilting still spends stock to emit ice.
- Rotation return changes the held angle while mouse translation continues. Automatic return spin is excluded from throw samples, while the user's actual translation is retained.
- The authored side walls and ceiling collide with free liquid and mask D's displayed surface. Their transformed box geometry also limits the exterior of held objects.
- Active free liquid can enter a held or released receiver through an inward crossing of its open mouth, including moving/rotating receivers. Side/bottom overlap and dropping a vessel do not capture it. Capture changes ownership while preserving ml and ingredients. Liquid already retired outside the simulation bounds cannot be recovered.
- Shaker cap and strainer are separately selectable, detachable and attachable. An attached cap moves with a detached strainer. Closed holds liquid and ice; Straining opens the actual narrow outlet for liquid while retaining ice; Open permits both. C cycles the three states. Reset and enable/disable preserve a single set of parts.

| Final verification | Outcome | Evidence and scope |
| --- | --- | --- |
| `53-model-d-compile/build-final.log` | PASS, 0 errors, 8 existing CS0649 warnings | Full project runtime/editor C# compilation including the experiment; compilation is separate from runtime evidence. |
| `59-model-d-input-shaker` | PASS, 50 checks, exit 0 | Public input API: off-center pickup, continuous cursor motion during return, interruption/release, independent part pickup, cap/strainer assembly movement and attachment, lifecycle and reset. |
| `62-model-d-integrated` | PASS, 600 checks, exit 0 | Actual D compute dispatches: wall/ceiling response, moving/rotating mouth entry, no overlap capture, same-cup recovery, ingredient/retirement accounting, Physics2D ice containment, bucket stock and geometry, three shaker closure states with liquid and ice. |
| `62-model-d-integrated/D-world-mask.png` | PASS, rendered mask and positive control | D surface has zero pixels beyond the tested side/top bounds; disabling those colliders produces visible leakage. Shaker scene/liquid PNGs verify rendering in all three closure states without changing the physical state. |
| `63-model-d-swap-regression` | PASS, 986 checks, exit 0 | A/B/C/D/E swap, throw, held contents, revised free-liquid side contact and no-overlap-capture rules, and released-ice table fall. |
| `64-model-d-surface-regression` | PASS, 114 checks, exit 0 | D/E interpolation, covariance, fractional volume, delayed births/reset, solid wall masks, open rim, spill and display history. |
| `65-model-d-normal-playmode` | PASS, 160 checks, exit 0 | Ordinary A/B/C/D/E PlayerLoop Pour/Stir, rendered camera PNGs, ledgers, physical/displayed poses, and Manual restoration. |
| `model-d-final-scope-audit.json` | PASS | Validated source/scene/shader/harness parity, metadata GUIDs, shared dependency hashes and changes confined to the experiment/evidence directories. |

Failed and intermediate attempts remain available. Attempt 51 exited before creating an Editor log under the restricted launcher; 52 passed the first 326 GPU checks. Attempt 53 first lacked restore assets, then completed offline restore and compilation. Attempt 54 captured an incomplete source copy during editing. Attempt 55 exposed a return-motion fixture placed against the intentional ceiling clamp; its position was corrected without relaxing its tolerance. Attempts 56–58 exposed real detached-part Rigidbody/Transform synchronization defects; parts now use their carrier's authoritative pose and synchronize physics/displayed pose on detach and movement. Attempt 60 used an invalid render-frame increment assertion for multiple captures within one frame; 61 seeded liquid inside the real closed-lid skirt. Those fixtures now check the current rendered frame and explicit seed clearance respectively. Attempts 59 and 62–65 contain the accepted final runtime evidence.

To reproduce, first finish `HarnessSource/prepare_experiment.py --target Library/FluidModelDHarness`, then run `Run-Unity.ps1` sequentially with that project path and fresh attempt names. Entry points are `ExperimentInputShakerValidation.Begin`, `ExperimentModelDInteractionValidation.Begin`, `ExperimentSwapValidation.Begin`, `ExperimentSurfaceValidation.Begin` and `ExperimentFrameValidation.Begin`; the recorded `command.json` files contain exact invocations. `IceBucketValidation.cs` is included by the integrated D fixture.

This completes implementation and automated verification in the isolated comparison scene. It does not establish hardware mouse interaction feel, human playtest approval, production-scene integration, a rebuilt standalone player for these changes, or new performance measurements. The D normal-frame Pour capture and inverted Straining shaker capture were also visually inspected.

## D / E implementation and completed verification

This section records completed attempts 16–42 on 2026-09-30. Authored-capacity retention, corrected A/B/C regression and ordinary A/B/C/D/E frame validation pass. Run 42 completes 48 controlled standalone offscreen workloads with verified surface rendering; it supplies CPU submission/render-call measurements, while GPU timestamps and gameplay FPS remain unverified. The earlier A/B/C results below are historical evidence for their named attempts.

A later performance follow-up addresses the reported slowdown while moving objects and pouring. Its full authored Manual-scene comparison starts at attempt 43 and is documented separately below; attempt 42's two-vessel measurements do not establish performance for that larger scene.

D keeps C's physics and adds display-only full-tick interpolation, birth/reset/translation-safe GPU history, ml-weighted neighbor covariance and constant-area anisotropic splats. D/E mask actual authored solid geometry, including the path from each particle center to its rendered pixels: support cannot reappear beyond a thin wall. The open rim remains open, real spilled particles remain visible, and held bodies retain their external-collision exception while their own vessel walls still mask owned liquid.

E adds an area-per-ml convention derived from each authored interior and capacity, calibrated initial packing, iterative density projection, bounded shear/cohesion/wetting responses, material presets and conservative ingredient mixing. Its finite bottle reservoir emits accepted quantities at their actual birth times, preserves fractional last drops and debits no stock for rejected reservations. The ledger separates CPU pending, GPU pending, generated, active, retired, rejected and explicitly transferred quantities. Delivery receipts reject stale generations/revisions and repeat consumption. These are experimental game-scale models; no laboratory material calibration or connection to the production drink-delivery system is claimed.

E's secondary response uses completed owned-liquid snapshots for bounded ice buoyancy/drag. A bounded cosmetic pool adds no logical ml; optional audio starts disabled and has explicit resource disposal. The dedicated effects fixture exercises creation/lifetime/disposal. Run 21's original stir replays had zero cosmetic candidates. Run 26 verifies actual spoon immersion and observes peaks of four water and three milk cosmetics; its vigorous stir also spills liquid, which remains separately accounted.

| Completed attempt | Outcome | Evidence and limits |
| --- | --- | --- |
| `16-calibrated-liquid-first` | FAIL, exit 1 | Spawn/update kernels exceeded D3D11.0's eight-UAV limit; Unity skipped dispatches. Ledger storage/bindings were consolidated before the next run. |
| `17-calibrated-liquid-uav-fix` | PASS, 209 checks, exit 0 | Three D Highball and six E Highball/Hurricane 30/60/120ml cases. E generated and retained every requested ml; radius-inclusive P95 height error was at most 14.55%, and visible **interior** area error at most 6.05%, against the declared 20% tolerances. Captures exposed detached surface support outside thin walls, subsequently corrected and checked in run 24. |
| `18-conservative-accounting` | PASS, 8,729 checks, exit 0 | Exact CPU/GPU reservation and rejection accounting, unequal-volume ingredient mixing, ownership isolation, delayed births, 4,096 emissions across reused slots, out-of-bounds retirement, transfer receipt replay protection and stale asynchronous readback rejection. |
| `19-reservoir-effects` | PASS, 41 checks, exit 0 | Fill-dependent pour onset, finite response/tail, equal integrated delivery at .5/.25ml resolution, fractional stock exhaustion, rejected-reservation recovery, source/birth metadata, ice response and held isolation, bounded cosmetics and optional audio disposal. |
| `20-improved-surface` | FAIL, exit 1 | D interpolation, covariance, fractional volume and delayed birth checks passed; wall assertion failed because the fixture disabled Rigidbody simulation and teleported physics poses without synchronizing displayed Transforms before capture. |
| `21-calibrated-scenarios` | PASS, 64 checks across eight replays, exit 0 | E Water Rest/Pour/Tilt/Stir/SealedShake, held ghost-spoon comparison, Syrup Pour and Milk Stir. Finite bounded state, ingredient conservation, owned-liquid retention where expected, lower syrup delivery and spoon-motion influence passed. Actual sustained spoon immersion is checked by the stronger run 26 fixture. |
| `22-all-mode-interactions` | PASS, 781 checks, exit 0 | A/B/C/D/E swap/throw handoffs, liquid ownership, held-body collision policy and released-ice table pass-through. Superseded by run 27's additional held-vessel sweep through owner-zero liquid. |
| `23-baseline-gpu-regression` | INCOMPLETE, timed out | Final report truncation failed with Windows `IOException` / Win32 IO 1224 while `validation.txt` was mapped by a reader. The wrapper stopped the process at 240 seconds (`exitCode=-1`). This attempt is not accepted as a completed baseline regression or performance result. |
| `24-surface-pose-validation` | PASS, 114 checks, exit 0 | Actual D/E GPU centroids at 30/60/120/144fps interpolation fractions; rendering leaves physical state unchanged. Partial .25ml area, rotated covariance, delayed births/reset, translated histories, own/foreign held masking and open-rim/below-glass drops all pass. Each mode has **zero** visible wall pixels and **zero** detached exterior support pixels; the fixture synchronizes only its manually frozen body transforms. |
| `25-final-volume-capacity` | PASS for admission/conservation, exit 0 | Repeated all six E calibrated-volume cases with the corrected wall mask; maximum height/visible-interior-area errors remain 14.55%/6.05%. All direct and topping requests admit and generate the exact authored 400ml, including two .25ml partial particles. However, topping retained only 362.5ml in Highball and 333.5ml in Hurricane after 2s. This raw PASS does **not** establish acceptable full-capacity retention. |
| `26-submerged-stir-scenarios` | PASS, 67 checks across eight replays, exit 0 | Stronger stir fixture verifies the spoon tip below the measured surface with nearby particles in all 20 sampled frames. Unheld/held spoon trajectories separate in all 20 samples (maximum liquid-center difference .350151 world units; RMS-speed difference 2.146555 units/s). Total/ingredient conservation and bounded responses still pass; visible agitation can physically spill liquid. |
| `27-held-free-liquid-regression` | PASS, 1,071 checks, exit 0 | Full A/B/C/D/E interaction suite plus a held vessel swept across an initially unowned particle. The particle remains unowned and unimpulsed while the held wall crosses it; own contents, ordinary released contacts, throw/handoff and ice table exceptions also pass. |
| `28-top-fill-retention` | FAIL the declared retention gate | Starting topping above existing contents improves Hurricane's final owned amount from 333.5ml to 384ml, but 96% remains below the unchanged 97% requirement. Both direct fills and Highball topping pass; all quantities remain conserved. Further initialization refinement is required. |
| `29-baseline-append-report` | FAIL an outdated fixture, exit 1 | The remote-isolation fixture created an owner-zero particle and expected a held glass to acquire it, contradicting the current held-vessel policy. The fixture now assigns its intended initial ownership to the queued birth. This run does not count as completed A/B/C baseline regression. Report completion succeeded with the separate immutable final file. |
| `30-surface-top-fill` | FAIL an incorrect placement oracle, exit 1 | After one complete physics tick, the direct Highball contained 396.5ml and 3.5ml was already free, with all 400ml conserved. The test incorrectly treated post-physics ownership as the initial placement state. Initial owner/interior-disk assertions were moved to actual queued spawn commands before physics; the 97% settled-retention threshold was kept. |
| `31-capacity-seed-validation` | PASS, 223 checks, exit 0 | Both authored 400ml vessels pass direct and settled-content topping admission, safe initial geometry, fractional .25ml births, finite state, total/ingredient conservation and the unchanged ≥97% owned-retention gate after 2s. Final owned quantities are 399.5/400ml for direct/topping Highball and 399/393.75ml for direct/topping Hurricane. |
| `32-player-build` | BUILT, exit 0 | Non-development StandaloneWindows64 player containing the explicit isolated comparison scene, with FrameTiming enabled. Build completed in 4m36s. A successful build alone is not a performance result. |
| `33-player-performance` | INVALID PERFORMANCE despite process exit 0 | Raw output says `COMPLETE`, but every case has a 0×0 liquid surface, no GPU timing samples and only 0–7 physics steps across 90 sampled frames. It did not measure the intended rendered, advancing-fluid workload. Its frame intervals/FPS are rejected. The revised harness requires an actual offscreen camera target, a rendered-frame counter and PNG evidence, at least 2s of sampling and at least 50 physics steps. |
| `34-baseline-owned-fixture` | PASS, 277 checks, exit 0 | Corrected A/B/C suite seeds the remote-isolation particle with explicit initial vessel ownership. Original solver separation, deterministic isolation, mixing/state validity, quantity conservation, reset and rendering checks pass. Included synchronized step timings exclude rendering and remain distinct from GPU timestamps or gameplay FPS. |
| `35-normal-frames-all-modes` | PASS, 160 checks, exit 0 | Ordinary A/B/C/D/E PlayerLoop frames with simulated Rigidbody2D bodies and the world's fixed-tick coroutine active. Pour/Stir state, coherent quantity/ingredient ledgers, displayed/physical poses and restoration of input, supply, bodies and camera on return to Manual pass. |
| `36-final-material-effects` | PASS, 43 checks, exit 0 | Final finite-reservoir/material/effects regression, including conservative accepted-stock debits, fractional exhaustion, bounded ice/cosmetic behavior, held isolation and optional audio creation/disposal. |
| `37-player-render-build` | BUILT, exit 0 | Rebuilt the non-development standalone benchmark with an offscreen target and render/tick validity guards in 40s. |
| `38-player-render-performance` | FAIL validity guard, exit 1 | The hidden player's automatic camera loop produced no liquid surface during warmup despite a created RenderTexture, enabled camera, valid URP asset and supported liquid shaders. The strengthened guard correctly rejected the workload before recording any cases. No timing/FPS claim is made. |
| `39-controlled-player-build` | BUILT, exit 0 | Rebuilt the non-development standalone benchmark with one explicit offscreen `Camera.Render` per regular player frame in approximately 40s. |
| `40-controlled-player-performance` | INVALID WORKLOAD, stopped after review | Explicit rendering worked, but PNG inspection exposed clipped vessel bottoms and liquid. `review.json` records the rejection: the complete intended two-vessel workload was not visible. Its performance values are not accepted. |
| `41-framed-player-build` | BUILT, exit 0 | Rebuilt in approximately 40s with framing derived from both vessels' complete bounds and an asserted 5% viewport margin. |
| `42-framed-player-performance` | COMPLETE, 48 cases, exit 0 | C/D/E × 100/300/600/1,000 particles × 1/4/8/16 ingredients, at 1280×720. Every case advances at least 100 physics steps over at least 2s, verifies the surface on every sampled frame and saves a PNG. Representative captures show both complete vessels and their liquid. Accepted as a controlled offscreen CPU workload; GPU timestamps are unsupported and these results do not establish gameplay FPS. |

The six E measurements below come from run 25 after the wall-coverage correction, in `25-final-volume-capacity/volume-summary.csv`. Heights include the .024-world-unit collision radius; area is measured inside the authored vessel only.

| Vessel | Requested/generated/owned ml | P95 extent height | Visible interior area |
| --- | ---: | ---: | ---: |
| Highball | 30 / 30 / 30 | 0.1774 | 0.1911 |
| Highball | 60 / 60 / 60 | 0.3504 | 0.3884 |
| Highball | 120 / 120 / 120 | 0.7001 | 0.7731 |
| Hurricane | 30 / 30 / 30 | 0.2464 | 0.1620 |
| Hurricane | 60 / 60 / 60 | 0.3981 | 0.3150 |
| Hurricane | 120 / 120 / 120 | 0.6475 | 0.6359 |

Run 25's direct 400ml fills retained 394ml in Highball and 390.5ml in Hurricane after 2s. Topping was less stable because new fill sites occupied gaps in the settled bulk. The final initialization uses a near-surface topping band and finer lattice-spacing refinement. Run 31 checks actual pending spawn geometry before physics, then enforces the previously declared 97% owned-retention gate after 2s. It passes without changing ml or preventing legitimate open-rim spill:

| Run 31, 400ml generated | Owned at 2s | Free at 2s | Retired at 2s | Owned retention |
| --- | ---: | ---: | ---: | ---: |
| Highball direct | 399.5ml | 0.5ml | 0ml | 99.875% |
| Highball topping | 400ml | 0ml | 0ml | 100% |
| Hurricane direct | 399ml | 0ml | 1ml | 99.75% |
| Hurricane topping | 393.75ml | 3.5ml | 2.75ml | 98.4375% |

Reports, CSVs and PNGs live in their named attempt directories. Current production and renderer/volume helper C# compile with zero errors and eight existing unused-field warnings. The four surface compute kernels also compile with `fxc`; run 24 confirms actual Unity/D3D11 shader execution. Run 42 supplies the limited standalone measurements described below. Player GPU time, memory growth, allocation rate and gameplay FPS have not been established.

## Manual motion performance follow-up (attempts 43–50)

After the reported movement/pouring slowdown, the experiment removes repeated work while retaining its authored scene and quality settings. Surface masking first excludes hulls that cannot touch a splat's entire footprint, then retains the existing pixel-inside and center-to-pixel wall tests for every remaining candidate. Large hull counts retain a full-scan fallback. D/E also skip inactive splats, empty composite-pixel work and unchanged metadata uploads, and reuse collider-path storage. Physics metadata is computed once per tick; pour pressure calculations are reused. Conservative swept-arc bounds, zero-contribution neighbor rejection and cosmetic bounds avoid unnecessary exact geometric work. These changes preserve the resolution, particle capacity, substeps and solver coefficients used by the comparison.

| Completed follow-up attempt | Outcome | Evidence and limits |
| --- | --- | --- |
| `43-manual-motion-before` | FAIL before measurement | The fixture incorrectly required at least ten objects and five bottles. The actual authored Manual scene contains nine objects and three bottles. Replaced these assumptions with recorded ID/name/kind inventory checks within each run. |
| `44-manual-motion-before-inventory` | PASS, original source | All four authored Manual workloads complete with inventory, quantity and functional checks. This is the accepted before measurement. |
| `45-manual-motion-after` | PASS, optimized source | The same four workloads and settings complete, retaining the full inventory and 15ml measured pour generation/source debit. |
| `46-motion-surface-regression` | PASS, 114 checks, exit 0 | D/E interpolation, fractional area, covariance, delayed birth/reset, wall/exterior masks, held policy and open-rim/outside drops pass after the rendering optimization. |
| `47-motion-interaction-regression` | PASS, 1,071 checks, exit 0 | A/B/C/D/E throw/swap handoffs, held-body and owner-zero liquid isolation, contents, ordinary contacts and ice table pass-through remain valid. |
| `48-motion-pouring-effects-regression` | PASS, 43 checks, exit 0 | Finite stock and accepted emission, partial drops, material response, bounded ice/cosmetics, held isolation and optional audio lifecycle remain valid. |
| `49-motion-volume-capacity-regression` | PASS, 431 checks, exit 0 | Repeats D/E volume/area/height fixtures, controls lifecycle and full authored-capacity direct/topping cases. All six E 30/60/120ml cases retain the requested amount; four 400ml cases pass the unchanged ≥97% owned-retention gate with exact total/ingredient conservation. |
| `50-motion-fast-rotation-baseline` | PASS, 277 checks, exit 0 | A/B/C GPU baseline regression after swept-bound optimization. All three modes retain the complete liquid amount through 855° sealed rotation with zero sweep exhaustion; the existing isolation, mixing, state, reset and rendering checks also pass. |

Runs 44 and 45 use E mode in the Unity Editor on AMD Radeon(TM) Graphics / Direct3D11, with all nine authored objects, three bottles, 727 boundary segments, capacity 4,096, two substeps and a 1280×720 liquid surface. Automatic readback, controls and effects stay enabled; public held-object movement/rotation APIs replace hardware mouse polling. Each condition uses a 1s warmup and at least 3s of ordinary PlayerLoop measurement, with one explicit offscreen camera render per rendered frame. The recorded inventory is preserved within each run. Runtime IDs depend on discovery order and differ between runs; these are matched workload/settings comparisons, not identical particle trajectories.

The completed-work diagnostic is separate: twenty samples each advance one .02s CPU/liquid tick, render, then read one render-target pixel to wait for completion. Its wall time includes CPU work, GPU work, synchronization and readback overhead. It is not an isolated GPU timestamp or gameplay frame time. The CPU submission P95 column measures `liquid.Step` during ordinary fixed steps; it excludes reservoir/world CPU work and forced GPU completion.

| Condition | Completed-work mean before → after (ms) | Mean reduction | Liquid-step CPU submission P95 before → after (ms) |
| --- | ---: | ---: | ---: |
| Idle | 20.961 → 18.039 | 13.9% | 4.115 → 1.980 |
| Held glass drag | 28.036 → 18.012 | 35.8% | 3.586 → 1.736 |
| Held glass rotation | 26.566 → 20.149 | 24.2% | 3.312 → 1.896 |
| Bottle pour | 21.164 → 18.237 | 13.8% | 4.557 → 1.836 |

Both pour runs generate and debit 15ml during their measured interval. Each report records active, selected-vessel-owned and retired ml; dynamic spill trajectories are not assumed identical. Conservation and finite-state checks passed in the fixture, but full particle snapshots and raw ledger error values are not serialized in these performance reports. The original and optimized `manual-performance.json`, CSV, source hashes and exit records are preserved under attempts 44 and 45. Their all-zero allocation counters are unsupported/unverified readings, **not** evidence of zero allocations. This short Editor/offscreen comparison does not establish automatic-camera gameplay FPS, long-run memory stability or isolated contributions from each optimization.

`manual-performance-comparison.json` records **162/162 checks, PASS_WITH_LIMITATIONS**. The comparison independently recomputes available timing distributions from CSV, verifies matching scene/settings hashes and authored name/kind inventory, and preserves the session-specific ID mapping and measurement limits. There is one Editor run per version; repeated-run statistical significance and identical fluid trajectories are not established. Reproduce this read-only input comparison with `python FluidGpuExperimentEvidence/HarnessSource/compare_manual_performance.py`.

The five original renderer files are preserved under `PerformanceSourceSnapshots/43-before-render-cost-removal`. Separate static checks support conservative rejection: `validate_surface_mask_pruning.py` tests 6,400 splats and 108,800 pixel queries over authored Highball/Hurricane/Spoon/Ice hulls with zero omitted masks; `swept-arc-static/results.json` records 10,000 motions, 2,610,000 sampled trajectory points and 50,000 compact-support checks; `cosmetic-bounds-static/results.json` records 144,000 queries with zero missed narrow-phase hits. These are CPU mathematical checks, not GPU timing or substitutes for the actual Unity regressions.

The post-optimization capacity check in run 49 retains 399.5/400ml in direct/topping Highball and 396/400ml in direct/topping Hurricane after 2s (99.875%, 100%, 99%, 100%). Its CSV records the remaining quantities as free or retired; total conservation errors are zero. The earlier run 31 values above remain the historical pre-optimization measurements. Six-second calibration still measures motion rather than guaranteeing equilibrium: the Hurricane 120ml case is flagged as unsettled by the existing final-second threshold.

After these changes, the full project C# build passes with zero errors and the same eight existing warnings. The current scope audit passes with no changes outside the experiment/evidence roots and no runtime or test-source mismatch against the validation copy. The original benchmark and all failed attempts remain preserved.

## Controlled standalone offscreen measurements (attempt 42)

`42-framed-player-performance/player-performance.json` and its CSV contain all 48 cases on AMD Ryzen 5 5600H / AMD Radeon(TM) Graphics / Direct3D11, in a non-development Windows player. The player calls `Camera.Render` explicitly once per regular frame into a 1280×720 target while ordinary fixed ticks advance the simulation. Each case warms up for 30 frames, then measures 112–121 frames and 100–102 physics steps over 2.000–2.035s. Every measured frame has a verified surface submission. The configured 60Hz cadence is a sampling cap, not an FPS result. PNG readback and grid-occupancy reconstruction occur after timing.

Each case uses two 400ml vessels, nominally 500ml total (62.5% of combined capacity), with unchanged initial/final particle count. Floating-point volume sums range from 499.99881 to 500.00235ml. Varying particle count changes ml per particle to hold the workload's nominal volume fixed. A geometric guard checks the complete vessel bounds with 5% viewport margin; reviewed C/100, E/300 and E/1,000 captures with 16 ingredients also show both vessels and their liquid in frame.

The independent `HarnessSource/validate_player_results.py` verifier passes the completed 48-case matrix, particle counts, volume/fill fraction, framing, rendering and timing-support status checks. Its `42-framed-player-performance/accepted-summary.json` records 5,751 verified surface frames, with minimums of 112 sampled frames, 100 physics steps and 2.000015s per case.

The table is the 16-ingredient subset. CPU fixed-step submission and CPU `Camera.Render` wall time are separate measurements; the render call can include waits. Neither measures GPU solver duration, and their sum is not a complete frame budget.

| Mode | Particles | CPU fixed step mean / P95 (ms) | CPU `Camera.Render` mean / P95 (ms) |
| --- | ---: | ---: | ---: |
| C | 100 | 0.422 / 0.612 | 0.477 / 0.584 |
| C | 300 | 0.452 / 0.694 | 0.513 / 0.732 |
| C | 600 | 0.433 / 0.535 | 0.499 / 0.639 |
| C | 1,000 | 0.968 / 1.287 | 0.981 / 1.230 |
| D | 100 | 0.476 / 0.604 | 0.524 / 0.672 |
| D | 300 | 1.022 / 1.336 | 0.982 / 1.186 |
| D | 600 | 0.936 / 1.149 | 0.911 / 1.099 |
| D | 1,000 | 0.959 / 1.217 | 0.934 / 1.091 |
| E | 100 | 1.412 / 1.709 | 1.040 / 1.298 |
| E | 300 | 1.342 / 1.642 | 1.010 / 1.260 |
| E | 600 | 1.418 / 1.832 | 1.028 / 1.308 |
| E | 1,000 | 1.328 / 1.523 | 0.968 / 1.143 |

All cases report no valid GPU-frame or GPU-surface timestamp samples; separate compute-dispatch GPU timing is unsupported. These fields retain explicit status and `-1` JSON percentiles/blank CSV values. No GPU-performance comparison, automatic-camera frame-rate claim, long-run memory-stability claim or allocation acceptance is inferred. Attempts 33 and 40 remain rejected, and attempt 38 remains a failed validity guard.

## Current scope and validation-copy audit

The current audit is `HarnessSource/audit_current_scope.py`, with its completed PASS in `final-scope-audit.json`. Its basis is the working tree/index versus current `HEAD`, including non-ignored untracked files. All changes are confined to `Assets/_Project/Features/Bartending/FluidGpuExperiment` (including its metadata) and `FluidGpuExperimentEvidence`; ignored Library output is excluded. The audit passes metadata/GUID uniqueness, missing/orphan metadata, original-fork reference isolation, exact feature source/asset and test-source parity against `Library/FluidSwapHarness`, and copied shared-dependency checks. Two shared text assets have explicitly recorded LF/CRLF-equivalent copies.

`audit_isolation.py`, `original-tracked-sha256.json`, `isolation-audit.json` and the failed `final-isolation-audit.json` are preserved historical baseline evidence. The saved 4,451-file manifest is distinct from current `HEAD`: the current report records 576 differences from those older hashes, 486 of which are line-ending-only and 90 other or missing differences. The older failed report remains unchanged. The current PASS establishes present change scope and validation-copy parity; it does not assert that all original historical bytes are identical.

## Historical A/B/C and interaction results (attempts 01–15)

- 2026-09-30 post-volume interaction regression: `15-volume-interaction-regression` passed **469 checks**, exit 0 on the same Unity/AMD/D3D11 setup. Existing swap/throw, held-body collision, liquid ownership and ice table pass-through checks remain valid after adding VolumeCheck. No C# or shader compilation errors; tested runtime/shader/scene and harness sources match the delivered files (disposable project folder metadata is independently generated).
- 2026-09-30 volume baseline: `14-volume-responsive-controls` passed **225 checks**, exit 0, Unity 6000.3.5f2 / AMD Radeon(TM) Graphics / Direct3D11. Twelve six-second GPU fixtures measure A/B/C Highball and C Hurricane at 30/60/120 requested ml, plus partial-particle, reset, selection, fresh-snapshot and Manual restoration checks. Generated quantity is preserved in all twelve stationary-vessel fixtures. The first attempt, `13-volume-baseline`, completed numerical cases but failed the controls-overlap assertion at the actual 640×480 batch viewport. The final responsive panel and camera pass that layout check. Batch mode did not accept the requested 960×640 resolution or produce an actual UI screenshot; camera-rendered scene/liquid PNGs are available, while UI layout is checked geometrically at 640×480.
- 2026-09-30 ice table pass-through: `12-ice-table-fall` passed **469 checks**, exit 0. A/B/C fixtures advance two seconds of real gravity simulation: ice falls completely below the table while an ordinary body remains supported by actual table contact. Ice retains contacts with unheld vessels, stays below the table when picked and dropped there, and keeps the table exception after release and reactivation. Existing throw/handoff, contents and held-collision checks also pass. No C# or shader compilation errors; validated runtime and test sources match the delivered files.
- 2026-09-30 unified release/throw: `11-unified-release-throw` passed **436 checks**, exit 0 on the same Unity/AMD/D3D11 setup. General releases and handoffs now preserve identical sampled linear/angular velocity, pose and subsequent Physics2D trajectories for both fast translation and spinning gestures. New-target samples/impulses are reset; automatic upright motion stays excluded; blocked releases retain held identity, rotation/return mode and samples until placement succeeds. These controlled Play Mode tests invoke the real sampling method with fixed timestamps and public movement/rotation/drop APIs, and advance actual Physics2D simulation. Full game/editor C# compilation passed with 0 errors and the same 8 existing warnings. Validated runtime, shader, scene and test files match the delivered source.
- 2026-09-30 swap correction, Unity 6000.3.5f2 / AMD Radeon(TM) Graphics / Direct3D11: `10-swap-gpu-contact` passed **166 checks**, exit 0. Public `Pick/MoveHeld/Drop` paths verify repeated handoffs at the current placement, continued cursor tracking, GPU content ownership, CPU body/ice/floor collision suppression and restoration. Single-particle GPU probes compare real ice contact against held ice and held-vessel isolation. `09-swap-handoff` passed the earlier 103-check subset. Both use the disposable `Library/FluidSwapHarness` project; these are controlled Play Mode API checks, not an automated desktop mouse test. Full game/editor C# compilation also passed with 0 errors and 8 existing unused-field warnings.
- Initial A/B/C phase `Compile`: full original game + experiment C# compilation, exit 0, 0 errors, 8 existing unused-field warnings. Additional MSBuild targets include new files without editing generated `.csproj` files.
- `07-deterministic-isolation-validation`: **277 PASS**, exit 0, no Unity error/assertion/exception. This uses the original Linear color space, Both input backends, URP assets, time and Physics2D settings.
- `08-final-linear-playmode`: **80 PASS**, exit 0. Ordinary PlayerLoop A/B/C pouring, stirring, actual Rigidbody/sprite agreement and Manual restoration all pass in matching project settings. **357 PASS** is the initial A/B/C phase subtotal from attempts 07 and 08 only, not a total for the current implementation.
- Initial-phase `isolation-audit.json` reported **4,451** files matching its saved SHA-256 baseline, unique experiment GUIDs, no references back to original PhysicsLab assets and validation-copy parity at that stage. This historical result does not describe the present tree; use the current scope audit above, which explicitly preserves and explains the later historical-hash differences.

The initial A/B/C physics checks cover actual compute execution, identical initial amount/count, normalized composition, finite state, reset, remote-object isolation, 855-degree sealed rotation, actual source-tagged bottle particles entering the glass, tilting, stirring, sealed shaking, rendering without physical changes, and 100/300/600/1,000 particle populations.

The controlled 32-birth probe measured zero A/A, B/B and B/C difference while distinguishing A from B. It identifies particles by birth token; GPU buffer slots are not persistent identities. Remote-object isolation uses a moving single particle in a real glass with strict absolute tolerances, avoiding chaotic multi-particle settling as a source of test noise. Dense interactions are exercised in separate fixtures.

The reference solver separates 64 exactly coincident particles without invalid values or lost volume. With pressure and generic damping disabled, pair viscosity reduces approaching relative speed from 2 to 1.542771, preserves the symmetric pair's total momentum, and leaves separating relative speed at 2. This is not a guarantee of momentum conservation in asymmetric clumps where safety caps apply.

Replay volume accounting distinguishes active particles from particles deliberately recycled after leaving the simulation area. Every retired particle must have an observed active-to-inactive transition, a finite endpoint outside the configured bounds, and retained volume. Active + retired ml must equal accepted ml. No additional births or recycled-slot reuse are allowed in these accounting fixtures. Detailed trajectories and retirement positions are stored per attempt.

## Historical volume baseline observations (attempt 14)

Completed baseline data: `14-volume-responsive-controls/volume-summary.csv`, with 360 time-series rows in `volume-trajectories.csv`. These values measure the A/B/C solvers before D/E development, not E's calibrated physical volume or equilibrium guarantees.

| Vessel / mode | Requested ml | Generated / contained ml at 6s | Not initially filled ml | Particle-center P95 height (world units) | Visible liquid area (world units²) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Highball / A | 60 | 60 | 0 | 0.476 | 0.620 |
| Highball / B | 60 | 60 | 0 | 1.131 | 1.360 |
| Highball / C | 30 | 30 | 0 | 0.619 | 0.777 |
| Highball / C | 60 | 60 | 0 | 1.107 | 1.369 |
| Highball / C | 120 | 80 | 40 | 1.391 | 1.715 |
| Hurricane / C | 30 | 30 | 0 | 0.706 | 0.788 |
| Hurricane / C | 60 | 60 | 0 | 1.164 | 1.388 |
| Hurricane / C | 120 | 66 | 54 | 1.235 | 1.486 |

The initial Fill grid saturates at 80ml in Highball and 66ml in Hurricane, independently of the selected solver. The unseeded remainder never enters the simulation, so it is not spill or loss. Every generated ml remains owned by the measured vessel in these fixtures, with zero free/other-owner/retired ml and no liquid image touching capture edges. At 60ml, C's visible area is about 2.21 times A's; B and C use the same physics with different rendering. Small differences between independent B/C runs reflect the solver's settling variation.

The final-second P95 range crosses the provisional half-radius settling threshold for the B/C Highball 120ml-request cases (actually 80ml). Treat their six-second heights as time samples, not final equilibrium. Future volume calibration must coordinate ml, spacing, pressure and area; no baseline solver or renderer coefficient was changed in this slice. The accounting fixture has no further births or slot reuse and is not a permanent material/disposal ledger.

## Historical synchronized timing samples (attempt 07)

Five samples per population, five 0.02-second simulation ticks per sample, after a warm-up sample. Wall time includes CPU submission and GPU completion synchronization; it excludes rendering. These are **not GPU timestamp timings or gameplay FPS**, and B/C timing differences are measurement variation because they use the same physics.

| Particles | A mean ms/tick | B mean ms/tick | C mean ms/tick |
| ---: | ---: | ---: | ---: |
| 100 | 3.792 | 2.970 | 2.962 |
| 300 | 3.842 | 3.002 | 3.297 |
| 600 | 3.806 | 3.098 | 3.226 |
| 1,000 | 4.391 | 3.095 | 3.151 |

Raw samples: `07-deterministic-isolation-validation/synchronized-step-timings.csv`; summarized ranges: `performance-summary.json`. This dispersed free-particle workload is a comparison sample, not a worst-case dense-vessel benchmark or proof of a frame-rate improvement.

## Preserved initial attempts (01–08)

Each invocation has a unique directory containing the exact command, process ID, exit code and local `Editor.log`. The wrapper refuses to overwrite attempts and copies newly created Unity crash directories. No Unity process crash occurred during the initial attempts 01–08; their validation failures returned exit 1. Later incomplete/rejected runs are documented in the D/E table above. Raw logs/screenshots and the disposable Library are local artifacts; commands, outcomes, validation reports and test sources are versioned.

| Attempt | Outcome | Explanation |
| --- | --- | --- |
| 01-author-comparison | PASS | Built and saved only the new comparison scene. |
| 02-initial-validation | FAIL | Cross-run comparison incorrectly treated atomic GPU slot allocation as persistent particle identity; also exposed stale sprite transforms in manually frozen captures. |
| 03-identity-corrected-validation | FAIL | The remote-motion fixture still compared untagged slot indices; corrected that fixture too. |
| 04-normal-playmode-validation | PASS | Normal PlayerLoop, real Physics2D, A/B/C pouring/stirring/manual reset and rendered poses. This early run used the disposable project's default Gamma/legacy input settings, superseded by the final matching-settings run. |
| 05-tagged-particle-validation | FAIL | An active-only volume assertion incorrectly counted intentionally recycled out-of-bounds spill as missing volume. |
| 06-volume-accounting-validation | FAIL | Retirement accounting passed, but a single noisy dense-settling repeat was not a reliable remote-isolation oracle. Replaced that fixture with deterministic free motion, without loosening tolerances. |
| 07-deterministic-isolation-validation | PASS | Full corrected GPU suite in matching project settings. |
| 08-final-linear-playmode | PASS | Initial A/B/C phase's final ordinary-frame validation and camera captures in original Linear/Both settings. |

The corrected tests do not alter baseline A's solver. Stirring setup was fixed in the new comparison controller: both the stationary glass and moving spoon must be unheld kinematic bodies, because the inherited held-vessel policy intentionally ignores unrelated solids.

Initial A/B/C pouring previews from identical 2.42-second replays are preserved in `Previews`:
[A](Previews/A-pour.png), [B](Previews/B-pour.png), [C](Previews/C-pour.png).

## Reproduction

1. Run `HarnessSource/prepare_experiment.py` with Python 3 to copy feature assets and their dependencies without modifying source assets or metadata. Use `--target Library/FluidSwapHarness` to keep the disposable project under the ignored Library folder.
2. Run `Run-Unity.ps1 -Attempt <new-unique-name> -Method ExperimentValidation.Begin` for the GPU suite.
3. Run with `-Method ExperimentFrameValidation.Begin` for ordinary Play Mode validation. It uses actual frames and Rigidbody simulation without manual ticking or forced Transform synchronization.
4. Run `python FluidGpuExperimentEvidence/HarnessSource/audit_current_scope.py --target Library/FluidSwapHarness` from the repository root before committing. Inspect `final-scope-audit.json` for current-HEAD scope, metadata and delivered/validated source parity. The older `audit_isolation.py` compares the historical saved-byte baseline only; retain its reports and do not treat that baseline as current `HEAD`.

For swap/held-collision regression checks, run `Run-Unity.ps1 -Attempt <new-unique-name> -Method ExperimentSwapValidation.Begin -ProjectPath <absolute-harness-path>`. The runner locates the project's Unity version under the standard Unity Hub installation; pass `-UnityExecutable` for a custom installation.

For volume baselines, use `-Method ExperimentVolumeValidation.Begin` with the same harness project. It runs A/B/C Highball and C Hurricane at 30/60/120 requested ml, 300 fixed 0.02-second ticks per case, with readbacks every 0.2 seconds. `volume-summary.csv` separates requested, queued (`accepted_ml`), unseeded, active, owned, free, other-owner and unreused retired amounts. `volume-trajectories.csv` retains the time series. Six observations during the final second report remaining motion; six seconds is not a guarantee of equilibrium.

Additional D/E entrypoints use the same wrapper and a fresh attempt directory:

| Method | Coverage |
| --- | --- |
| `ExperimentVolumeValidation.BeginCalibrated` | D/E 30/60/120ml measurements; the current source also includes full authored-capacity and settled-content topping fixtures, including a separately queued final .25ml, recorded in `volume-capacity.csv`. These capacity additions are not covered by run 17. |
| `ExperimentVolumeValidation.BeginCapacityOnly` | Four E capacity cases only: direct/topping × Highball/Hurricane, exact quantities and conservation, plus the separately declared ≥97% owned-retention gate at 2s. Skips calibration and UI checks. |
| `ExperimentAccountingValidation.Begin` | Conservative ingredient ledger, reservation/rejection, slot reuse, delivery and asynchronous-generation checks. |
| `ExperimentPouringValidation.Begin` | Finite reservoir, resolution-independent integrated flow, materials and secondary effects. |
| `ExperimentSurfaceValidation.Begin` | Full-tick GPU interpolation, partial volume, covariance, wall/exterior masking, held policy and history invalidation. |
| `ExperimentScenarioValidation.Begin` | E physical/material replays and the matched held/unheld spoon comparison. |
| `ExperimentManualPerformanceValidation.Begin` | Full authored Manual scene in E: idle, held-glass drag/rotation and bottle pouring, ordinary-frame CPU measurements and a separate GPU-completion diagnostic. Run before and after versions under unique attempts; `compare_manual_performance.py` checks completed outputs without changing them. |
| `ExperimentPerformanceBuild.BuildBenchmark` | Builds the explicit isolated comparison scene as a non-development Windows player. Run the player with `-fluid-benchmark -fluid-output <new-absolute-output-directory>` for the controlled offscreen workload; inspect both workload validity and unsupported metric statuses before interpreting measurements. |

Verify completed player results from the repository root with `python FluidGpuExperimentEvidence/HarnessSource/validate_player_results.py FluidGpuExperimentEvidence/42-framed-player-performance`, substituting the new completed attempt directory when reproducing. Review the saved PNGs as well as the machine-checked framing and metric statuses.

Wait for the attempt's `exit.json` before opening its result/report files on Windows. A mapped reader can prevent the runner from truncating a report with `File.WriteAllText`, as preserved in attempt 23; reading an unfinished report is not evidence of completion. The baseline runner now appends its live `validation.txt` and writes a separate immutable `validation-final.txt` on completion.

The baseline also saves scene and liquid-only PNGs. Surface area uses the actual liquid composite's alpha, normalized by measured ingredient opacity, with explicit 0.05/0.25 coverage thresholds. World area uses the capture camera's scale; edge-touching pixels flag possible viewport clipping. Particle P95 center height, radius-inclusive extent and rendered maximum height are distinct measurements. Runtime UI P95 adds the collision radius to the reported particle-center P95. Partial-particle, reset, mode/glass selection, fresh-readback, Manual restoration and controls-layout checks accompany these measurements. An actual UI screenshot is attempted separately and any batch capture limitation is recorded.

`Run-Unity.ps1` records failures and exits nonzero. It never retries Unity automatically. Review the saved command, logs and any crash dump before a changed follow-up invocation.
