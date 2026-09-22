# Fluid GPU Experiment validation

Unity 6000.3.5f2, NVIDIA GeForce RTX 3070, Direct3D11. The experiment was exercised in a disposable Unity project under `HarnessProject`, with source assets and read-only dependencies copied from the working tree. The game project was not opened or saved by Unity for this work.

## Results

- `Compile`: full original game + experiment C# compilation, exit 0, 0 errors, 8 existing unused-field warnings. Additional MSBuild targets include new files without editing generated `.csproj` files.
- `07-deterministic-isolation-validation`: **277 PASS**, exit 0, no Unity error/assertion/exception. This uses the original Linear color space, Both input backends, URP assets, time and Physics2D settings.
- `08-final-linear-playmode`: **80 PASS**, exit 0. Ordinary PlayerLoop A/B/C pouring, stirring, actual Rigidbody/sprite agreement and Manual restoration all pass in matching project settings. Total final runtime checks: **357 PASS**.
- `isolation-audit.json`: all **4,451** previously tracked working-tree files are byte-identical to their initial SHA-256 hashes, including pre-existing user changes. Experiment GUIDs are unique; copied assets do not refer back to their original PhysicsLab versions. Validated runtime source/assets match the delivered source.

Physics checks cover actual compute execution in A/B/C, identical initial amount/count, normalized composition, finite state, reset, remote-object isolation, 855-degree sealed rotation, actual source-tagged bottle particles entering the glass, tilting, stirring, sealed shaking, rendering without physical changes, and 100/300/600/1,000 particle populations.

The controlled 32-birth probe measured zero A/A, B/B and B/C difference while distinguishing A from B. It identifies particles by birth token; GPU buffer slots are not persistent identities. Remote-object isolation uses a moving single particle in a real glass with strict absolute tolerances, avoiding chaotic multi-particle settling as a source of test noise. Dense interactions are exercised in separate fixtures.

The reference solver separates 64 exactly coincident particles without invalid values or lost volume. With pressure and generic damping disabled, pair viscosity reduces approaching relative speed from 2 to 1.542771, preserves the symmetric pair's total momentum, and leaves separating relative speed at 2. This is not a guarantee of momentum conservation in asymmetric clumps where safety caps apply.

Replay volume accounting distinguishes active particles from particles deliberately recycled after leaving the simulation area. Every retired particle must have an observed active-to-inactive transition, a finite endpoint outside the configured bounds, and retained volume. Active + retired ml must equal accepted ml. No additional births or recycled-slot reuse are allowed in these accounting fixtures. Detailed trajectories and retirement positions are stored per attempt.

## Timing samples

Five samples per population, five 0.02-second simulation ticks per sample, after a warm-up sample. Wall time includes CPU submission and GPU completion synchronization; it excludes rendering. These are **not GPU timestamp timings or gameplay FPS**, and B/C timing differences are measurement variation because they use the same physics.

| Particles | A mean ms/tick | B mean ms/tick | C mean ms/tick |
| ---: | ---: | ---: | ---: |
| 100 | 3.792 | 2.970 | 2.962 |
| 300 | 3.842 | 3.002 | 3.297 |
| 600 | 3.806 | 3.098 | 3.226 |
| 1,000 | 4.391 | 3.095 | 3.151 |

Raw samples: `07-deterministic-isolation-validation/synchronized-step-timings.csv`; summarized ranges: `performance-summary.json`. This dispersed free-particle workload is a comparison sample, not a worst-case dense-vessel benchmark or proof of a frame-rate improvement.

## Preserved attempts

Each invocation has a unique directory containing the exact command, process ID, exit code and local `Editor.log`. The wrapper refuses to overwrite attempts and copies newly created Unity crash directories. No Unity process crash occurred during these attempts; validation failures returned exit 1. Raw logs/screenshots and the disposable Library are local artifacts; commands, outcomes, validation reports and test sources are versioned.

| Attempt | Outcome | Explanation |
| --- | --- | --- |
| 01-author-comparison | PASS | Built and saved only the new comparison scene. |
| 02-initial-validation | FAIL | Cross-run comparison incorrectly treated atomic GPU slot allocation as persistent particle identity; also exposed stale sprite transforms in manually frozen captures. |
| 03-identity-corrected-validation | FAIL | The remote-motion fixture still compared untagged slot indices; corrected that fixture too. |
| 04-normal-playmode-validation | PASS | Normal PlayerLoop, real Physics2D, A/B/C pouring/stirring/manual reset and rendered poses. This early run used the disposable project's default Gamma/legacy input settings, superseded by the final matching-settings run. |
| 05-tagged-particle-validation | FAIL | An active-only volume assertion incorrectly counted intentionally recycled out-of-bounds spill as missing volume. |
| 06-volume-accounting-validation | FAIL | Retirement accounting passed, but a single noisy dense-settling repeat was not a reliable remote-isolation oracle. Replaced that fixture with deterministic free motion, without loosening tolerances. |
| 07-deterministic-isolation-validation | PASS | Full corrected GPU suite in matching project settings. |
| 08-final-linear-playmode | PASS | Final ordinary-frame validation and camera captures in original Linear/Both settings. |

The corrected tests do not alter baseline A's solver. Stirring setup was fixed in the new comparison controller: both the stationary glass and moving spoon must be unheld kinematic bodies, because the inherited held-vessel policy intentionally ignores unrelated solids.

Final pouring previews from identical 2.42-second replays are preserved in `Previews`:
[A](Previews/A-pour.png), [B](Previews/B-pour.png), [C](Previews/C-pour.png).

## Reproduction

1. Run `HarnessSource/prepare_experiment.py` with Python 3 to copy new feature assets and their dependencies. It only creates metadata inside the new feature folder.
2. Run `Run-Unity.ps1 -Attempt <new-unique-name> -Method ExperimentValidation.Begin` for the GPU suite.
3. Run with `-Method ExperimentFrameValidation.Begin` for ordinary Play Mode validation. It uses actual frames and Rigidbody simulation without manual ticking or forced Transform synchronization.
4. Run `HarnessSource/audit_isolation.py` before committing to verify original-file preservation and delivered/validated source parity.

`Run-Unity.ps1` records failures and exits nonzero. It never retries Unity automatically. Review the saved command, logs and any crash dump before a changed follow-up invocation.
