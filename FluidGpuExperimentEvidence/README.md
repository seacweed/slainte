# Fluid GPU Experiment validation

Unity 6000.3.5f2, Direct3D11. Earlier baseline runs used NVIDIA GeForce RTX 3070; the D/E development and performance runs below used AMD Radeon(TM) Graphics. Model D interaction and return-motion runs (51 onward) use NVIDIA GeForce RTX 3070. The experiment was exercised in disposable Unity projects under `HarnessProject`, `Library/FluidSwapHarness` or `Library/FluidModelDHarness`, with source assets and read-only dependencies copied from the working tree. The game project was not opened or saved by Unity for this work.

## F tilted-glass pickup carries owned liquid (2026-10-10)

Only the initial pickup of a tilted Glass in F applies a rigid reference-frame change to its owned liquid. The current pickup path restores the glass upright immediately; it is distinct from the animated right-button return. Active particles, GPU-reserved births and CPU-queued births rotate/translate together. Both position histories and existing velocity directions transform, preserving speed, volume, composition, temperature and flags. Glass and carried-ice collision histories rebase by just the pickup correction, so the next GPU sweep does not apply it again as wall velocity. Existing genuine movement survives the rebase. Released F garnish remains an independent native body. Free/foreign liquid, manual tilt, subsequent pointer movement, right-button upright return and A–E retain their preceding policies.

`159-glass-pickup-liquid` passes **86 checks**, exit 0. Real interactor pickups cover +90/-90/+135/-135/180/855 degrees, ceiling-induced translation, active and both pending-birth paths, unrelated particles, surface endpoints/directions, stale delivery rejection and motion history. Both authored glasses retain all 40 ml in controlled rotated-settled fixtures; maximum speed during 12 post-pickup ticks is **0.06944–0.13393 units/s**, below the declared 0.6 gate. These fixtures isolate artificial pickup impulse; existing real splash/overflow momentum is not erased. An actual D mode switch verifies that the new carry does not run there. Attempt 158 records a test-harness compile error (missing EndRotation pointer argument); 159 corrects the call and completes.

`160-glass-pickup-input-regression` passes **1,925 checks**, exit 0, preserving relative pointer input, manual rotation/return, bottle emission, bucket inventory and detachable shaker controls. `161-glass-pickup-garnish-regression` passes **168 checks**, exit 0, including held pass-through, safe click-release, independent native motion, liquid flow and wet/dry ice contact. The pre-edit feature is retained in `Library/GlassPickupLiquidOriginal`. `glass-pickup-liquid-preservation-audit.json` checks the nine-file runtime/shader/documentation allowlist, harness parity, 158 untouched art/data/prefab/scene/other-shader files and byte-equivalent source for every preceding liquid physics kernel after removing the new dedicated pickup entry point. Tests use hidden Unity batch and no Computer Use; actual user interaction feel is not evaluated.

## F garnish passes through solids while held (2026-10-09, current)

F garnish now temporarily uses trigger colliders while held. The held-only sweep, rotation clipping, overlap recovery and world-boundary position clamp are removed. Cursor movement, rotation and upright return therefore pass through glass, ice, caps and world walls without pushing them. Geometry remains available for picking and release queries. On click-drop, a bounded outward search checks nearby positions before re-enabling solid native contact; it runs only at release. Released motion, buoyancy/flow forces, one-time pickup and the GPU no-feedback policy are preserved. A–E and other item policies are unchanged.

`156-garnish-held-free-release` passes **168 checks**, exit 0. Both peels pass through upright and tilted glasses, remain at a genuinely overlapping wall position, accept the full 850-degree rotation and immediate 5-degree reversal, and return upright without wall correction. Clicking at the overlapping position releases safely and restores Dynamic/solid contacts. Separate actual overlaps with dynamic ice cause no measured ice translation, rotation or velocity while held; release restores the pair's solid contact. Ceiling/side-wall overlap and unconstrained held position are checked as well. The preceding released wall, moving wet glass, native rotation, dry/wet ice settling, no-liquid-feedback, reset and pickup tests still pass. This replaces earlier assertions that held garnishes must stop at walls.

`157-held-garnish-input-regression` passes **1,925 checks**, exit 0, covering relative pointer input, rotation/return, bottle emission, bucket inventory and detachable shaker controls after the shared rotation/release changes.

Attempts 154 and 155 passed the held pass-through checks but exposed a click-drop failure. The old generic nearest-normal projection oscillated across the compound wall; 155 records the alternating positions/normals. The F-only release search in 156 avoids that oscillation. These failures are retained. Source art, physical profiles and released force coefficients were not changed to pass the new held policy.

The pre-edit feature is retained in `Library/GarnishHeldPassThroughOriginal`. `garnish-held-pass-through-preservation-audit.json` verifies the runtime/documentation allowlist, source/harness parity and 160 unchanged art/shader/data/prefab/scene files. All tests use hidden Unity batch with virtual input and no Computer Use; native user interaction feel remains unverified.

## F garnish native dynamics and independent motion (2026-10-09, preceding implementation)

User feedback exposed a missing acceptance condition: the kinematic controller inherited the glass's translation and rotation even with no contact. The previous carrier test actually required nearly constant vessel-local position, so its passing result did not establish independent garnish motion. F released garnish now uses **Dynamic Rigidbody2D with free rotation**. Cursor pickup remains kinematic. The native solver handles gravity, inertia and ordinary glass/ice contacts; the custom released pose integrator, spin clipping, contact impulse and extra contact damping are removed. Held-vessel pose transport and release-velocity injection skip F garnish. Completed world-space GPU velocity supplies drag/buoyancy through `AddForce` and angular response through `AddTorque`; vessel velocity is no longer subtracted. Direct liquid feedback remains disabled.

Held cursor sweep/rotation guards remain. Native continuous collision and the existing finite-outline escape sweep handle fast relative wall translation. Only actual overlaps deeper than one source pixel receive an additional positional correction, without changing angle or spin. Native wall acceptance is one source pixel (0.005556 u for these assets) plus 0.0005 u numerical tolerance, explicitly replacing the previous positive kinematic skin gap. The old ice sweep and ice transport policy retain their behavior. Garnish sprites, collider profiles, mass, prefabs, shaders and A–E settings are unchanged; F applies its damping at runtime.

`153-garnish-native-final` passes **140 checks**, exit 0. It covers the prior held/return/wall/environment/open-mouth/visibility/one-time-pickup/no-feedback cases and native free rotation, actual angular-force response, held-glass independence, no spurious release kick, fast wall crossing and dry/wet ice contact. It also verifies that an existing unheld kinematic piece switches to Dynamic on its next physics tick, covering the body-state transition after script reload (not an automated editor reload test). Controlled angular-gradient tests still use synthetic completed snapshots; the moving wet glass and settling cases use the real GPU at 5 Hz readback.

| Measurement | Synth Lemon Peel | Nananga Peel |
| --- | ---: | ---: |
| Contact-free held glass translation/tilt: garnish world position and angle drift | 0 / 0 | 0 / 0 |
| Same case: vessel-local displacement | 0.121913 u | 0.121913 u |
| Real 60 ml glass, 3 s motion, ±25-degree tilt: relative angular span | 59.209 degrees | 60.836 degrees |
| Same moving glass: relative position span | 0.593494 u | 0.592070 u |
| Same moving glass: minimum wall clearance | 0.000426 u | -0.003499 u (within one pixel) |
| Dry off-center contact turn, no initial spin | 48.553 degrees | 67.747 degrees |
| Dry garnish resting on ice, final 2 s position/angular span | 0 / 0 | 0 / 0 |
| Wet ice/garnish, final 2 s position/angular span | 0.000750 u / 0 degrees | 0.000186 u / 0 degrees |

Each dry mixed case recorded real ice contact in all 100 sampled ticks. Wet mixed cases float clear of the ice and conserve all 40 ml; they do not claim sustained garnish/ice contact. Paired liquid position/velocity differences with versus without garnish remain **0**. `151-native-garnish-ice-regression` passes **57 absolute checks**, exit 0.

`152-native-garnish-input-regression` passes **1,925 checks**, exit 0, covering relative cursor input, rotation return, bottle pouring/accounting, bucket inventory and detachable shaker controls with virtual input.

Failure history is retained: 146 exposed a transient 0.009183 u native wall overlap; 147 showed the interior-only escape contour could not correct that collider contact. 148 corrected actual deep collider overlaps, then stopped at a fixture precondition: the supposed contact-free 20-degree tilt approached within 0.009396 u of a wall. The revised 10-degree fixture explicitly verifies >0.03 u clearance before assessing independence; measured garnish world motion was already zero in 148. 149 passes 126 checks, adding real moving-liquid coverage; 150 passes 138 with dry/wet native ice contacts; 153 adds existing-body state migration. No failed run is treated as passing.

The 250-file pre-edit feature is retained in `Library/GarnishDynamicOriginal`. `garnish-dynamic-preservation-audit.json` checks the five runtime-file allowlist plus documentation, source/harness parity and **160 unchanged art/shader/data/prefab/scene files**. All execution used hidden Unity batch and virtual input, without Computer Use. Native desktop feel, arbitrary high-speed motion and performance are not established by these controlled checks.

## F garnish angular flow, buoyancy and contact rolling (2026-10-09, superseded kinematic implementation)

F garnish now samples two positions along its authored hull's longest axis. A difference in local liquid velocity supplies target angular velocity; a difference in immersion supplies limited restoring torque. Uniform translation cancels from the angular estimate. Existing snapshot ownership, frame conversion, generation and 0.35-second age checks still apply. Air/wet angular damping and extra contact damping were reduced. Off-center closing contact velocity produces an angular impulse; valid small contact-pivot steps let the piece roll instead of discarding its spin. Rejected arcs still stop at a clear pose, with held pivot corrections bounded. Geometric recovery displacement is never converted into velocity. Liquid feedback, one-time pickup and all existing wall constraints remain in force.

`145-garnish-rotation-settling` passes **110 checks**, exit 0. This includes the earlier interaction, collision, carrying, rendering-order, stale-data and no-feedback cases, plus the rotation cases below. `144-garnish-angular-response` passed the first 108 checks; 145 adds real-surface settling checks and strengthens restoring-torque direction acceptance.

| Rotation measurement | Synth Lemon Peel | Nananga Peel |
| --- | ---: | ---: |
| Air spin remaining after 0.2 s from 180 degrees/s | 171.221 degrees/s | 171.221 degrees/s |
| Controlled positive/negative flow angular estimate | +114.055 / -114.055 degrees/s | +114.578 / -114.578 degrees/s |
| Released off-center contact turn, no initial spin | 30.887 degrees | 66.014 degrees |
| Dry-contact final 1 s angular/position span | 0 / 0 | 0 / 0 |
| Real GPU surface final 2 s angular span | 0.272 degrees | 0.136 degrees |
| Real GPU surface final 2 s peak absolute spin | 0.798 degrees/s | 0.376 degrees/s |
| Real GPU surface final 2 s position span | 0.002004 u | 0.001820 u |

Angular flow sign, uniform-translation cancellation and half-turn-consistent restoring torque use deliberately constructed **completed-snapshot fixtures**, not measured turbulent GPU flow. They also verify that the sampled flow produces actual body rotation in either direction. The surface-settling case separately runs the **real GPU**, with 60 ml, 2 seconds of liquid settling and 10 seconds of garnish motion at the normal 5 Hz completed-readback cadence. Both liquid position/velocity comparisons with versus without garnish remain exactly **0**. These are controlled automated cases, not native interaction feel, general stability proof or performance measurements.

Runtime changes are confined to `FluidExperimentBody.GarnishMotion.cs` and `FluidExperimentGpuLiquid.Garnish.cs`. The 250-file pre-edit feature is retained in `Library/GarnishRotationOriginal`. `garnish-rotation-preservation-audit.json` checks the change allowlist, harness/source parity and 160 unchanged art/shader/data/prefab/scene files. The input and ice regressions below belong to the preceding implementation; they were not rerun for this isolated rotation change. All execution used hidden Unity batch processes without Computer Use or desktop input.

## F garnish rigid flow following and wall contact (2026-10-09, preceding implementation)

The user approved rigid garnishes that follow liquid motion without pushing liquid. F now excludes garnish from physical GPU boundary uploads as well as the existing display mask. A CPU rigid-outline controller performs continuous translation sweeps against the shared solid profiles, refines rotation so an edge cannot skip a thin wall, and uses geometric distance queries for overlap correction. Rigidbody2D remains kinematic with native pair response disabled for these F pieces. Contact friction and retention of the last valid pose handle resting corners. Rejected rotation is removed from input accumulation so immediate reversal and release spin use accepted motion. A completed native physics step is reconciled before liquid/rendering; gravity is integrated only once. This is a rigid flow-following approximation, not liquid particles deforming into a garnish or a two-way fluid/solid solver.

Completed GPU readbacks supply local flow and partial immersion. Vessel poses/velocities are captured with the same snapshot and transformed into the current carrier frame. Snapshot generation and simulation age are checked; data older than 0.35 s is ignored. No synchronous GPU readback was added to runtime physics. The normal readback cadence remains 5 Hz. Contained garnish draws at order 2, between liquid 0 and glass fronts 4; a freshly dispensed external garnish retains 45 so it stays visible over its supply. Pickup-after-placement remains disabled. A–E retain native garnish motion and liquid boundaries; ice retains its existing F response.

`139-garnish-final-motion` passes **84 checks**, exit 0. Both peels were exercised against upright and 35-degree highballs and the authored hurricane receiver: whole-glass fast drag, open-mouth entry, interior wall stop, multi-turn rotation, immediate reverse rotation, upright return, released contact, flow following, stale/reset snapshot rejection, fast carried motion, carrier release velocity, pouring out, environment walls/ceiling, supply visibility and one-time pickup. Tested minimum released wall clearance was 0.005475 world units; final-window positional span was at most 0.005526. Controlled GPU particle position/velocity difference with versus without an overlapping garnish was **0**, with unchanged accounting. PNGs show flow following and front-glass occlusion; these are offscreen captures, not native desktop feel or performance approval.

`135-garnish-flow-ice-regression` passes **57 absolute checks**, exit 0. Its moving-ice probe retains ice travel 0.139999, liquid travel 0.111664, peak liquid x-speed 0.697897, and all 0.5 ml. This invocation did not request the historical before/after comparison, so it must not be described as the older 72-gate run.

`143-input-pour-fixture-final` passes **1,925 checks**, exit 0, including the explicit swap-fixture precondition, virtual relative input, return motion, real GPU births/accounting, bucket stock and detachable shaker controls. The unchanged birth-coverage gate observes 30 births and a reconstructed old-path tangential peak of 36.54136 with the pinned pouring fixture.

Intermediate records remain intact: 131 exposed missing resting friction; 132 exposed a tilted corner overlap; 133 passed after the valid-pose guard; 134 added carrier/environment/visibility cases; 136 passed 80 checks including the hurricane and completed-physics correction. Input runs 137 and 140 failed because arbitrary bottle registration order placed the rotation pivot outside a different bottle's pick area (`contains=False`, no swap candidate in 140). Run 138 passed the earlier 1,924 checks when a different bottle happened to be selected. Run 141 explicitly aligned the pick area and passed swapping, then exposed that the old tangential-impulse coverage gate was also calibrated on a particular bottle. The disable/re-enable transition test moves its bottle to the end of the world's registration list, so the later pouring fixture had been selecting another bottle. Run 142 initially pinned the short-lever `Bottle_item_1003` for both fixtures and failed the existing coverage gate. The final fixtures explicitly use 1003 for relative input and 1002 for return/pouring, align the target pick center to the click, and verify actual solid overlap before testing the unchanged runtime swap operation. No gate was relaxed.

The pre-edit 246-file feature snapshot is retained in `Library/GarnishLiquidMotionOriginal`. `garnish-liquid-motion-preservation-audit.json` verifies the changed-file allowlist, source/harness parity, and **160 unchanged art/shader/data/prefab/scene files**. Disposable harness directory GUIDs are excluded from parity; asset GUIDs and source bytes are checked. All execution used hidden Unity batch processes and virtual input, with no Computer Use.

## Garnish display mask and one-time loose-solid pickup (2026-10-09, earlier stage)

The user selected only the visual-mask change from the proposed no-displacement/buoyancy plan. F omits garnish hulls from the improved liquid renderer's solid mask. Garnish sprites keep their existing sorting; physical GPU boundaries, response coefficient, mass/damping, liquid displacement and CPU collisions remain unchanged. Ice/glass masks and D/E garnish masks retain their previous behavior. No buoyancy was added.

All modes now record placement per loose-solid instance. A released/thrown garnish or ice cube rejects both pointer hit-selection and direct `Pick`; bucket-emitted ice starts placed. Used pieces do not obstruct picking the glass underneath or dispensing a new garnish from a supply. Teleport and disable/enable preserve placement state. Reset destroys generated pieces and resets pickup state for initial scene bodies. Other tools, including detachable shaker parts, remain reusable.

`129-garnish-mask-pickup-final` passes **153 reported checks**, exit 0. Actual virtual mouse clicks verify new supply, placement, rejection of garnish/ice re-pick, click-through to glass, repeated glass pickup, explicit throw lockout, fresh ice's first pickup, poured-ice lockout, UI blocking and D/F resets. The existing mixed contacts, containment, carrier motion, moving-liquid response and paired free-drop checks also pass, confirming physical garnish/liquid interaction still exists.

Renderer acceptance uses an intentionally frozen particle overlapped by each garnish, without advancing physics. The actual F liquid composite is pixel-identical before/after adding either garnish (summed alpha difference **0**); D still masks the same particle (**303.290375** alpha difference). Ice produces the same **303.290375** mask difference in both modes. GPU particle position, velocity and ml are unchanged by all garnish render comparisons. `Garnish-Held-Contact.png` was visually inspected; separate `Mask-*` camera captures are retained in attempt 129.

Attempts `127-garnish-mask-single-pick` and `128-garnish-mask-diagnostic` passed pickup checks and the first F garnish mask comparison, then failed the ice visual control. The fixture deactivated the preceding garnish, invoking `ReleaseOwner`, which invalidated liquid display history until another physics tick. Attempt 129 keeps that garnish registered and moves it outside the camera for the ice control, avoiding an unrelated history reset without advancing/changing the liquid. Production rendering was not altered to accommodate the fixture.

The pre-change feature and harness source are retained in ignored `Library/GarnishMaskPickupOriginal`, with `garnish-mask-pickup-original-sha256.json`. `garnish-mask-pickup-preservation-audit.json` passes: the 246-file snapshot is intact, 160 protected prefab/scene/data/art/shader files are unchanged, physical GPU source files are byte-identical and changes are confined to display-mask/pickup integration and documentation. All validation uses the hidden disposable project and virtual devices, without Computer Use or desktop input; human interaction feel remains unverified.

`130-garnish-single-pick-input-regression` passes **1,924 checks**, exit 0, covering relative pointer input, return motion, bottle emission, bucket stock and detachable shaker behavior. `garnish-mask-pickup-final-scope-audit.json` passes source/harness parity, metadata, dependency and change-scope checks. `git diff --check` also passes.

## Gentler garnish contact (2026-10-09, after user play feedback)

Garnish Rigidbody mass is already 0.01 versus ice's 0.04, but the one-way GPU boundary response does not use Rigidbody mass. Reducing mass alone would not soften the liquid impulse. F now scales garnish-owned translation and rotation by `garnishLiquidMotionTransfer` (default 0.2), preserving its carrier's common motion and the actual collision/sweep geometry. Carrier decomposition matches the loose body's shortest angular step and full-tick linear path, including unwrapped multi-turn input and GPU substeps. Setting the coefficient to 1 bypasses this adjustment exactly. Ice and A–E boundary calculations keep their previous paths.

Paired free-drop measurements showed that response scaling alone did not reliably reduce splash: fast garnish displacement still compressed liquid. Both garnish prefabs therefore also use linear/angular damping 4, replacing 0.05/0.1. This garnish Rigidbody setting applies in every comparison mode; mass, gravity, silhouettes and colliders are unchanged. No shader, liquid settings, ice prefab or scene changes were made. This is an artistic response/drag adjustment, not calibrated density, buoyancy or two-way momentum coupling.

`125-garnish-peel-response` passed **118 reported checks**, exit 0. It repeats click/drop/re-pick and stationary mixed-contact checks, verifies full and split-substep carrier response at 12 and 372 degrees, compares full/gentle moving-boundary speeds, and compares actual drops into an upright glass with 40 ml settled before each impact. The within-run old preset is transfer 1 with damping 0.05/0.1; the new preset is transfer 0.2 with damping 4/4. Every accepted case checks finite state, real contacts, no deep interior particles and quantity accounting.

| Measured quantity | Lemon: old → new | Nananga: old → new |
| --- | ---: | ---: |
| Moving-probe peak liquid x-speed | 0.689833 → 0.137966 | 0.688302 → 0.140615 |
| Drop peak sum of ml × speed² | 1407.279 → 619.859 | 1923.256 → 270.397 |
| Drop peak upward liquid speed | 11.8772 → 9.26485 | 12.8243 → 6.17404 |
| Active ml after 3 s | 38 → 40 | 38.5 → 40 |

The volume-weighted speed-squared metric falls by approximately 56% / 86%; it is a motion-energy proxy, not joules or momentum. Active ml includes liquid outside the glass and must not be read as a cup-retention measurement. Total accounting error is zero. The new held/unheld mixed fixtures each retain 40 active ml, zero solid position span and 200/200 sleeping/contact samples, with zero deep-interior samples. Controlled checks do not establish human interaction feel or universal splash suppression.

Intermediate attempts remain recorded: 119 reached the free-drop test but used an invalid inverted receiver; 120 logged zero contacts and zero active ml, and 121 exposed the stale 180-degree pose before impact. Resetting interpolation alone in 122 did not fix the root cause. Attempt 123 showed that re-enabling Rigidbody simulation restored the previous native pose; the fixture now enables simulation before teleporting and explicitly checks an upright, stationary receiver containing 40 ml. It also exposed the need to match wrapped loose-body rotation to unwrapped carrier input. Attempt 124 passed carrier tests and reached a valid drop pair, but **failed** the energy-reduction gate with coefficient-only tuning (1693.85 → 1947.03). Attempt 125 adds peel damping and preserves the original reduction thresholds. Earlier stationary-only and invalid-drop runs are not splash-fix evidence.

The pre-response feature and harness source are preserved in ignored `Library/GarnishResponseOriginal`, with `garnish-response-original-sha256.json`. `HarnessSource/audit_garnish_response.py` checks this snapshot, the exact changed-file allowlist, protected assets, and that only damping/coefficient fields changed in the two garnish prefabs. No Computer Use or desktop input was used; tests run in the existing hidden disposable project.

`126-garnish-response-ice-regression` passes all **72 reported gates**, exit 0, including the six stationary cases and genuine moving-ice response against baseline 98. `garnish-response-preservation-audit.json` passes the 246-file snapshot check with 104 protected shader/data/scene/art files unchanged; the two prefab changes are limited to damping and the new coefficient. Current source/harness parity and scope are recorded separately in `garnish-response-final-scope-audit.json`. `git diff --check` passes.

## Click-supplied garnishes (2026-10-09, F default)

The Manual scene now has fixed Synth Lemon Peel and Nananga Peel supply jars at the upper right. Clicking a jar with an empty hand creates and immediately picks one matching piece; the next click drops it without picking the jar or glass underneath. Dropped pieces can be picked again. Supplies are unlimited, and reset removes generated pieces while keeping the jars. Pieces below the disposal boundary are retired. The four supplied PNGs are copied byte-for-byte; their Sprite import settings and simplified polygon outlines exclude transparent margins. Both actual Sprite references and camera captures were checked.

`LabItemKind.Garnish` is appended after the existing enum values. Garnish and ice share loose-solid containment, held-carrier transport, open-mouth escape, collision filtering and F contact stabilization. Garnishes have their own silhouettes, sprites and 0.01 mass; jars are trigger-only supplies without liquid interiors or dynamic bodies. No shader or existing physics/settings asset changed for this addition. F remains the default, and this does not add liquid-to-solid forces or buoyancy.

| Evidence | Outcome | Coverage |
| --- | --- | --- |
| `113-garnish-texture2d-author` | PASS, exit 0 | Builds two garnish/source prefab pairs and collision profiles, imports all four textures as 2D Sprites and saves two source instances in the comparison scene. Generated assets were copied back to the source project. |
| `114-garnish-visible-validation` | **64 reported checks PASS**, exit 0 | Both types through real Interactor Update with virtual Input System events: click supply, held follow, one spawn per click, release, re-pick, glass overlap priority, no automatic swap on drop and UI blocking. Actual Physics2D/GPU contact, carrier transport, open-mouth release, moving garnish response, reset and D/F switches. Sprite references and camera PNGs verified. |
| `115-garnish-input-regression` | **1,924 PASS**, exit 0 | Relative rotation input, return motion, bottle emission, bucket stock and detachable shaker regression. Native cursor operations remain suppressed in batch. |
| `116-garnish-containment-regression` | **601 PASS**, exit 0 | Existing held/free ice containment, multi-turn transport, open-mouth release, closed shaker, bucket, world collision and liquid recapture suite. |
| `117-garnish-ice-contact-regression` | **72 reported gates PASS**, exit 0 | Existing six-case ice/liquid contact and moving-ice checks, compared with completed baseline 98. |
| `118-garnish-normal-playmode` | **192 PASS**, exit 0 | Ordinary PlayerLoop startup in F, A–F pouring/stirring, body/sprite poses, rendered surface captures and Manual restoration. |
| `111-garnish-restored-compile` | PASS, exit 0 | Full game/editor C# compilation: zero errors and eight existing CS0649 warnings. Later Sprite-import guards and manual help text also compiled in the successful Unity runs. |
| `garnish-preservation-audit.json` | PASS | Exact 220-file pre-change snapshot verified; 137 existing data/prefab/shader files, including all 34 shader files/metadata, unchanged. Existing enum indices and scene fields preserved; all four supplied PNG hashes match. |
| `garnish-final-scope-audit.json` | PASS | Delivered feature/assets and tested copies match, metadata is valid and unique, copied shared dependencies are unchanged and edits remain in experiment/evidence roots. |

The mixed contact fixture uses one ice cube, one piece of each garnish and 40 ml seeded outside the solids. After six seconds of settling, both held and unheld glasses have zero measured solid position span, sleeping solids and real solid contacts in all 200 measured samples. Mean near-solid liquid speed is 0.0028623 in the unheld case and 0.00020952 in the held case, with zero deep-interior particle samples and all 40 ml active. A 725-degree carrier rotation plus translation preserves each loose solid's local position within 0.0000003 units; inversion allows both garnishes to leave through the open mouth. Separate moving-solid probes move each garnish 0.14 units: the initially stationary 0.5 ml particle travels 0.11026 / 0.10725 units and reaches x-speed 0.68983 / 0.68830 for Lemon / Nananga, retaining all 0.5 ml.

Preserved intermediate runs are not counted as final passes:

- `108-garnish-author` exited successfully but serialized null Sprite references because the initially minimal texture metadata imported as Cubemap. This is an incomplete authoring result.
- `109-garnish-validation` passed input/stationary gates but missed the null-vs-null Sprite comparison and failed transport after a capture wrote unsynchronized rendered transforms. The final fixture requires non-null Sprite references and synchronizes those capture-only transforms before transport. This correction did not alter production physics.
- `110-garnish-compile` failed with NETSDK1004 because local restore assets were absent. Attempt 111 restored them and compiled successfully.
- `112-garnish-sprite-author` correctly rejected the Cubemap assets after an explicit import guard was added. Attempt 113 sets `TextureImporterShape.Texture2D`, imports real Sprites and generates the delivered assets. A temporary package experiment in the disposable harness was reverted.

The pre-garnish feature is retained in ignored `Library/GarnishOriginal/FluidGpuExperiment`, recorded by `garnish-original-sha256.json`; it is a local snapshot, not a Git commit. `Editor/FluidExperimentGarnishBuilder.cs` can rebuild the garnish assets from the copied artwork. Reproduce runtime acceptance with `ExperimentGarnishValidation.Begin` and fresh wrapper attempt names. Camera captures are in `114-garnish-visible-validation`, including `Garnish-Manual.png`, `Garnish-Supplies.png` and `Garnish-Held-Contact.png`.

All interaction tests use virtual devices in a hidden disposable Unity project; no Computer Use or desktop mouse/keyboard automation was performed. These controlled measurements do not establish human interaction feel or long-duration/dense-packing performance. Garnish-specific closed-shaker and disposal-boundary cases were not separately exercised; the existing shared containment/shaker regression passed.

## Ice contact stabilization (2026-10-04, F default)

The exact preceding F-default source is retained in ignored `Library/IceContactOriginal/FluidGpuExperiment`, with `ice-contact-original-sha256.json`. Resting ice used to be repositioned by a circumscribed-circle containment repair while Physics2D solved the actual polygon contacts. The replacement leaves shallow/resting contacts to Physics2D and uses the scaled convex ice hull for fast/deep escape recovery. Reapplying an identical held pose no longer rewrites the native body pose. This shared CPU correction applies to every mode; ice remains dynamic, without forced sleep or altered damping/materials.

F removes geometric ice repair from reconstructed liquid velocity, handles simultaneous local contacts containing ice, and transfers genuine moving-boundary velocity separately. At most eight contact constraints are selected deterministically; incompatible gaps use bounded relaxation without shrinking particles or deleting ml. Three separately compiled F kernels keep the new contact arrays out of A–E. F remains the default, with unchanged particle radius/ml, material coefficients, filling, pouring and rendering. F still has one-way solid-to-liquid contact; this adds no liquid-to-ice force or buoyancy.

| Evidence | Outcome | Coverage |
| --- | --- | --- |
| `98-ice-contact-valid-before` | Measurement baseline, exit 0 | Six actual Physics2D/GPU cases: held/unheld stationary glass, four dynamic cubes with/without F liquid, and one fixed cube with F liquid. Fixture v2 seeds exactly 40 ml outside ice plus explicit face particles. This records the bug; it is not a stabilization pass. |
| `103-ice-contact-final` | **72 reported gates PASS**, exit 0 | Identical six-case fixture, 6 s settling plus 2 s measurement at 0.02 s; absolute and before/after gates, finite state, conserved total/ingredient ml, real contacts and separately verified moving-ice response. |
| `100-ice-contact-containment-regression` | **601 PASS**, exit 0 | Shared CPU hull recovery: fast/multi-turn held transport, free gravity/spin, side containment, open-mouth release, inverted shaker strainer/lid, bucket, world collision and liquid recapture in D. CPU source is identical to the final source. |
| `101-ice-contact-input-regression` | **1,924 PASS**, exit 0 | Relative input, rotation return, bottle emission, bucket stock and detachable shaker. Native cursor operations are disabled in batch. CPU/input source is identical to the final source. |
| `104-ice-contact-final-compile` | PASS, exit 0 | Full game/editor C# compilation: zero errors, eight existing CS0649 warnings. |
| `106-ice-contact-f-physics` | **711 PASS**, exit 0 | F pressure/compression, cohesion, material shear, conservative mixing, partial volume and four six-second scenarios. Optional cross-run legacy comparison is separated from these checks, as explained below. |
| `107-ice-contact-normal-playmode` | **192 PASS**, exit 0 | Ordinary PlayerLoop startup in F, A–F pouring/stirring, body/sprite poses, rendered surface captures and Manual restoration. |
| `ice-contact-final-scope-audit.json` | PASS | Delivered/validated feature and harness sources match; metadata is valid and unique; shared dependencies are unchanged; changes remain inside experiment/evidence roots. |

Final stationary measurements (world units; near-liquid speed includes particles within 2.5 radii of an ice edge):

| Case | Before ice position span | After ice position span | Before → after sleeping samples | Before → after near-liquid mean speed | Final retained/generated ml |
| --- | ---: | ---: | --- | --- | --- |
| Unheld glass, 4 ice, empty | 0.04748 | 0 | 0% → 100% | — | — |
| Unheld glass, 4 ice, F | 0.05412 | 0 | 0% → 100% | 0.36888 → 0.000994 | 41 / 41 |
| Held glass, 4 ice, empty | 0.05171 | 0 | 0% → 100% | — | — |
| Held glass, 4 ice, F | 0.06617 | 0 | 0% → 100% | 0.38369 → 0.000513 | 41 / 41 |
| Unheld glass, fixed ice, F | 0 | 0 | fixed body | 0.02536 → 0.000355 | 40.5 / 40.5 |
| Held glass, fixed ice, F | 0 | 0 | fixed body | 0.02536 → 0.000349 | 40.5 / 40.5 |

All four dynamic clusters retain actual contacts throughout the measured window, with zero position span, step displacement and speed p95. Their nonzero RMS of approximately 2–4e-6 comes from floating-point mean accumulation over identical positions. All final liquid cases have zero deep-interior penetration samples and zero ledger error. The independent moving-ice probe retains compatible ownership and enough particle-disk clearance: ice travels 0.140, its initially stationary 0.5 ml particle travels 0.112 and reaches x-speed 0.698; all 0.5 ml remains active. These are automated, controlled measurements, not human playtest approval or a guarantee for every packing arrangement.

Preserved intermediate runs:

- `95-ice-contact-before`: initial measurement, superseded for liquid comparison because bulk Fill placed particles inside existing ice. The public Fill method still does not exclude dynamic solids; initial interior spawning is outside this correction.
- `96-ice-contact-compile`: intermediate C# compile passed.
- `97-ice-contact-valid-before`: setup failed because an exterior-only 60 ml lattice could not fit around four cubes. V2 requests 40 ml in both accepted before/after runs; acceptance thresholds were not relaxed.
- `99-ice-contact-after`: all stationary/relative gates passed, then moving fixture failed. `102-ice-moving-diagnostic` traced the cause: teleporting the probe cube cleared ownership after the glass had been held, so held isolation correctly excluded it. The final fixture positions/acquires ice before pickup and asserts compatible ownership throughout. The failure does not establish a moving-contact regression. Endpoint disk clearance is also explicitly checked in the corrected fixture.
- `105-ice-contact-fluid-regression`: A–D cross-run state matches exactly; E differs from 91 by 0.002894 position / 0.022599 velocity / 0.001903 composition, aborting before F tests. Its complete A–E snapshot exactly matches the already preserved pre-ice attempt 90 (`ice-contact-known-e-variation-comparison.json`). This is the previously observed E repeat variation; do not call the 105 comparison a pass or infer cross-run E determinism. Run 106 independently validates F without the optional cross-run comparison.

`ice-contact-preservation-audit.json` verifies the saved pre-ice snapshot and unchanged data/material/surface/fill/pour assets, and compares preprocessed A–E contact kernel bodies against that snapshot. New F contact kernels compile and execute on D3D11. The compiler still reports F-only loop/finite-analysis warnings and X4714 register-pressure guidance for `UpdateCohesiveContactVelocities`; actual GPU frame cost was not profiled, and passing correctness checks is not performance evidence. No Computer Use or desktop mouse/keyboard automation was used. Actual interaction feel, long-duration/dense-packing performance and initially trapped liquid remain separate validation work.

Reproduce with `ExperimentIceContactValidation.Begin` for a baseline and `BeginValidate` for acceptance; set `FLUID_ICE_CONTACT_BASELINE` to the completed v2 baseline JSON for before/after gates. `BeginMovingOnly` runs the same moving probe with per-step body/particle/boundary traces. Use a fresh wrapper attempt directory for every invocation.

## F selected as the default (2026-10-04)

After trying F, the user selected it as the new default. The runtime component initializer, saved comparison scene and scene builder now all select `FCoherentLiquid`. F is the current development/validation baseline; D remains selectable with **4**, and **6** returns to F. UI labels and the feature README/development plan reflect this decision. Physics parameters and A–E implementations are unchanged by this promotion.

`94-mode-f-default-playmode` passed **192 checks**, exit 0, including a startup assertion before any mode switch that the actual scene is already running F with cohesive physics active. Ordinary A–F pouring, stirring, rendering and Manual restoration also pass. The run compiled the updated feature/editor and harness sources in the disposable Unity project. No desktop input or Computer Use was performed.

`mode-f-default-preservation-audit.json` passes the pre-F snapshot checks with the newly authorized default change: runtime, scene and builder agree on F, the scene differs only in `initialMode`, and old kernel bodies/enum indices remain unchanged. `mode-f-default-scope-audit.json` passes validated source parity, metadata and shared-dependency checks. Earlier reports below retain the defaults and evidence that applied at their execution time; `audit_mode_f_preservation.py` now writes the new default-specific report instead of overwriting the historical D-default report.

## Separate F cohesive liquid mode (2026-10-04, before default promotion)

At the time of attempts 87–93, F was selectable with **6 / F Cohesive Liquid** and D was still the authored default. The later F-default decision is recorded above. A–E keep their enum indices and existing compute kernel bodies. F keeps D's particle radius, ml, smoothing scale, fill, bottle emission and surface rendering, adding compression-only iterative density projection, independent finite-range cohesion, full-vector material shear and conservative ingredient mixing. F material buttons affect particle viscosity/cohesion only. E calibration, reservoir flow, wetting, optics, cosmetics and ice response stay E-only. The optional approximate planar-wall density contribution defaults to zero.

The exact working tree immediately before F, including the uncommitted pointer, stock and return-motion corrections, was copied to ignored `Library/FluidModeFOriginal/FluidGpuExperiment` and recorded in `mode-f-original-sha256.json`. `mode-f-preservation-audit.json` confirms all 214 original files remain present, 207 are byte-identical, and the seven changed original files are only the five F integration points and two documents. New F implementation files are separate. This local snapshot is not a Git commit and depends on retaining the ignored Library folder; D itself remains available in the delivered implementation.

| Evidence | Outcome | Coverage |
| --- | --- | --- |
| `87-mode-f-before-a-e` | PASS, exit 0 | A–E controlled GPU snapshots produced using the exact pre-F runtime. Eight uniquely weighted particles identify state independently of GPU slot allocation. |
| `88-mode-f-compile` | PASS, exit 0 | Full game/editor C# compilation: zero errors, eight existing unused-field warnings. |
| `91-mode-f-physics-isolated` | **711 PASS**, exit 0 | F/D/E switches and feature gates; exact D radius/ml/kernel/fractional fill; independent CPU density oracle; compression, attraction and unequal-volume shear probes; 100-tick conservative mixing; four 300-tick, six-second F replays with finite state and total/ingredient ledgers. |
| `92-mode-f-normal-playmode` | **191 PASS**, exit 0 | Ordinary PlayerLoop A–F pouring, stirring, actual body/sprite poses, GPU surface captures and Manual restoration. The inherited report's final prose still says A/B/C/D/E; the individual F checks and PNGs are present. |
| `93-mode-f-d-input-regression` | **1,952 PASS**, exit 0 | Existing relative-input, rotation-return, bucket-stock, bottle-birth and detachable-shaker suite passes with the new mode available. Native cursor operations are disabled in this hidden batch fixture. |
| `mode-f-a-e-baseline-comparison.json` | PASS | Offline comparison of completed 87→91 GPU probes: A–E settings, identities, positions, velocities and composition match exactly under the original strict tolerances. |
| `mode-f-final-scope-audit.json` | PASS | Delivered/validated runtime, shader, asset and test parity; unique metadata; unchanged shared dependencies; changes confined to the experiment/evidence roots. |

The GPU microprobes measured mean positive compression of **2.10018754 → 1.44501853 → 0.12216451** at 0/1/6 projection iterations in the same deliberately overpacked 49-particle lattice. Pressure-disabled pair cohesion reduces separation from **0.18 to 0.1692678** over three ticks. A separate unequal-volume tangential pair has relative shear **3.50003242 / 3.383851 / 3.05371284** with viscosity off / Water / Syrup; its mass-weighted momentum checks pass. These isolate the mechanisms and are not calibrated real-world material properties or a claim of global momentum conservation under contact/correction clamps.

At six seconds, the default F Rest replay retains all **60 ml**, with maximum particle speed **0.04595145 world units/s** and mean positive compression **0.00265873**. Pour accounts for **140 ml**; Stir accounts for **57 active + 3 retired ml**; SealedShake starts with the inherited fill limit of **56.5 ml** and retains that total. Detailed observations are in `91-mode-f-physics-isolated/cohesive-scenarios.csv`. These replays check accounting and finite state, not universal containment or equilibrium. Rest/Pour/Stir final camera captures and the ordinary D/F Pour captures were visually inspected.

Preserved failures: `89-mode-f-normal-playmode` failed shader compilation because `point` was used as an HLSL identifier; it was renamed to `contactPoint` before subsequent GPU runs. `90-mode-f-physics` failed the strict E pre/post trajectory comparison (maximum position **0.002893814**, velocity **0.02259887**, composition **0.00190323591**); A–D matched exactly. Attempt 91 uses the same runtime as 90 and reproduces attempt 87's A–E snapshots exactly. Thus the observed E replay variation also occurs between unchanged-runtime runs; tolerances were not widened and the failed run remains recorded. A single successful match does not establish cross-run deterministic E trajectories.

No desktop input or Computer Use was performed. These checks do not establish human interaction feel, production integration, a new standalone build, a visual-quality improvement over D, or a performance improvement. F is an opt-in comparison experiment; its extra iterations require separate performance measurement.

To reproduce: use the existing preparation/wrapper instructions with `Library/FluidModelDHarness` and unique attempt names. `ExperimentCohesiveValidation.BeginBaseline` saves A–E probes; `ExperimentCohesiveValidation.Begin` additionally runs the F suite. Optional `FLUID_COHESIVE_BASELINE` points to a completed prior `legacy-a-e-snapshot.json` and enables strict live comparison. Without it, the suite still saves the new snapshots but does not claim a pre/post match. `HarnessSource/compare_cohesive_baselines.py BEFORE AFTER --output REPORT` compares completed snapshots with the same tolerances. `mode-f-e-repeat-variation.json` preserves the failed 90→91 comparison. Retain the exact pre-change runtime to generate a valid baseline. Run `HarnessSource/audit_mode_f_preservation.py` for the snapshot/source check and `audit_current_scope.py --target Library/FluidModelDHarness --output FluidGpuExperimentEvidence/mode-f-final-scope-audit.json` for current scope/parity.

## Relative rotation pointer correction (2026-10-04)

The per-frame native cursor warp and absolute-position subtraction below are superseded. They discarded motion at or below two pixels every frame, which produced discontinuous slow rotation. Manual rotation now consumes Input System relative mouse delta exactly once per input update, with no dead zone. The entry snapshot is excluded because it can contain motion preceding the button press. Buttons and movement use the same input backend.

During rotation the native cursor is locked/hidden and a software arrow is drawn at the rendered pivot. The software pointer is also the source for click/UI hit testing. Ending rotation unlocks immediately and issues at most one native warp. Until absolute position catches up, relative input continues to move the pointer and held object. Coordinate reconciliation rebases the return/grab baseline, without adding physical displacement or throw velocity. If RAWINPUT and absolute coordinates differ due to scaling/acceleration, reconciliation waits for stationary input; it never freezes the drag. Focus loss, Escape, reset, owner removal and disable restore cursor state. Focus/Escape cancellation requires a released RMB before a new capture.

The new `RelativePointerValidation.cs` feeds virtual mouse/keyboard state through Input System and the actual Interactor Update. It runs only in hidden batchmode, where all native lock/visibility/warp operations are suppressed. Its temporary input routing settings ensure a hidden, unfocused Editor can process queued player input; the settings are restored afterwards. These are automated input-path tests, not a human playtest or proof of native OS timing.

| Verification | Outcome | Evidence and limits |
| --- | --- | --- |
| `85-relative-pointer-final-input` | PASS, 1,924 checks, exit 0 | Fractional/one-pixel relative motion, event accumulation, frame distribution, direction reversal, absolute-only jumps, duplicate Update, entry/release snapshots, delayed absolute catch-up including one-pixel movement, immediate return translation, rendered-pointer UI/drop/swap, boundary pivot, Escape/focus/disable/reset. Also reruns the existing GPU return-emission, boundary correction, stock and detachable-shaker suite. |
| `84-relative-pointer-final-compile` | PASS, 0 errors, 8 existing CS0649 warnings | Full project runtime/editor C# compilation on the final runtime source. |
| `86-relative-pointer-normal-playmode` | PASS, 160 checks, exit 0 | Ordinary A/B/C/D/E PlayerLoop Pour/Stir, quantity/ingredient ledgers, body poses, camera PNGs and restoration of Manual controls. This does not operate a desktop cursor. |
| `relative-pointer-final-scope-audit.json` | PASS | Feature and harness source parity, metadata uniqueness, copied shared dependencies unchanged, edits confined to the isolated experiment/evidence paths. |

Attempts remain separate and are not added together. Attempt 79 was blocked by the sandbox's SDK-cache access; attempt 80 lacked restored build assets. Attempt 81 restored local build assets and compiled successfully before the final refinements. Attempt 82 stopped at the first queued RMB edge because an unfocused Editor selected/routed editor input instead of player input; attempt 83 explicitly used manual player input updates and passed relative motion/handoff checks, then exposed a fixture assumption that the target pose and rendered transform were already synchronized. The final fixture explicitly initializes the rendered pose and derives UI expectations from that transform. The intermediate scope audit records the expected copy differences while edits were still in progress; the final audit checks the synchronized sources.

`PointerGuiValidation.cs` and attempts 69–71 remain historical sources/evidence for the superseded warp implementation. That old GUI entry point references fields removed by this correction and must not be reused as a validator for the current implementation. No native pointer experiment, Computer Use, UI automation or human playtest was performed for this correction. Actual OS lock/warp timing and perceived cursor rendering remain unverified. The stationary handoff fallback after 0.25 seconds was code-reviewed but is not time-advanced by the synchronous queued-input fixture.

## Historical rotation pointer, bucket stock and return emission (2026-10-03)

This earlier implementation anchored the native pointer at the held object's constrained pivot every frame and derived rotation from displacement relative to the last warp target. Its two-pixel tolerance ignored small per-frame movement. It was subsequently reported to snap during real mouse movement; the saved passing checks did not exercise that input loop and do not establish that this cursor implementation was correct. The relative-input implementation above replaces it. The bucket and return-emission changes below remain in use.

The bucket front now uses the existing seven stock sprites, from empty to full, with serialized references on the isolated prefab. Actual ice emission updates the display immediately; reset restores the full display. Stock visuals do not create an interior collider or liquid capacity. Seven rendered stock-state PNGs are saved with the input/return suite.

Automatic return motion is separated from the velocity inherited by new liquid and ice emissions. Actual poses still determine nozzle position, direction, flow and swept collision geometry. Both automatic angular motion and the extra translation needed to keep a rotating object inside the side/ceiling bounds are accumulated until the physical tick consumes them. Manual rotation and mouse translation remain physical. The same-request, previous/new-orientation constraint comparison removes only the translation caused by the automatic pose change.

| Verification | Outcome | Evidence and limits |
| --- | --- | --- |
| `76-return-wall-clamp-fix` | PASS, 1,794 checks, exit 0 | Final rounding logic, continuous return/throw input, detachable shaker regression, 30 angle/timestep/cursor emission cases, and 18 boundary cases (three authored bottles × left/right/top × stationary/tangential cursor motion). Pending birth velocities are checked before solver forces; each batch then runs on the real D GPU with quantity conservation checks. |
| `76-return-wall-clamp-fix/return-births.csv` | PASS, 44 inspected births in 30 cases | Maximum residual tangential launch velocity after subtracting user translation is 0.000010894 units/s. Near-threshold 95-degree returns correctly may stop before a full particle is emitted. This is a selected-bottle deterministic fixture, not a new performance measurement. |
| `77-return-final-normal-playmode` | PASS, 160 checks, exit 0 | Final-source ordinary A/B/C/D/E PlayerLoop Pour/Stir, camera PNGs, quantity/ingredient ledgers, body poses and restoration of Manual controls. This does not inject desktop input or move the native cursor. |
| `78-return-clamp-final-compile` | PASS, 0 errors, 8 existing CS0649 warnings | Full project runtime/editor C# compile on the final source. |
| `return-final-scope-audit.json` | PASS | Validated code, shader, scene, prefab and harness copy parity, unique metadata, unchanged shared dependencies, and changes confined to experiment/evidence directories. |

Earlier attempts remain unchanged. Attempt 66 passed 1,350 checks before the last pointer rounding correction. Attempt 67 initially lacked restored build assets, then compiled successfully using local package sources. Attempt 68 passed the existing 600-check D integration suite. Attempt 69 began without an explicitly focused Game view and failed the native pointer probe. Attempt 70 confirmed the pointer reached the pivot within approximately 1.21 input-coordinate pixels, but its raw PASS used an insufficient rotation-drift tolerance: the recorded 0.12 degrees/frame from rounding was subsequently rejected and corrected. Attempt 71 was interrupted by the user's Escape action and has no accepted final native-pointer result; its `review.json` records that boundary. On resumption the user explicitly prohibited Computer Use, so verification continued only through hidden batchmode Unity and command-line checks. The abandoned, specifically identified test process was stopped without interacting with the user's Unity window.

Attempt 72 passed the final rounding/input fixture and 73 compiled it. Attempt 74 passed 160 ordinary PlayerLoop checks before the boundary follow-up. Read-only review then identified a missing stationary-pointer case at world boundaries. Attempt 75 reproduced the bug in the actual emitter: the first right-wall return tick inherited 10.53963 units/s of automatic position correction. Attempt 76 verifies the translation fix, including real cursor motion along the wall, without relaxing its velocity tolerances.

Final OS cursor behavior and human interaction feel remain unverified after the rounding correction. No Computer Use or desktop input was used for the resumed verification. These results apply to the isolated Model D comparison; no production-scene integration is claimed.

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
